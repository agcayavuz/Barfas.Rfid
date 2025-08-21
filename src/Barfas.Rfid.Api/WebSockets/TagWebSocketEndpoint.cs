using Barfas.Rfid.Core.Abstractions;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Barfas.Rfid.Api.WebSockets
{

    public static class TagWebSocketEndpoint
    {
        private static readonly JsonSerializerOptions JsonOpt = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        /// <summary>/ws/tags yoluna WebSocket handler’ını bağlar.</summary>
        public static IEndpointRouteBuilder MapTagWebSocketEndpoint(this IEndpointRouteBuilder app, string path = "/ws/tags")
        {
            app.Map(path, HandleAsync);
            return app;
        }

        /// <summary>Handshake + mesaj döngüsü.</summary>
        private static async Task HandleAsync(HttpContext ctx)
        {
            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ctx.Response.WriteAsync("Expected WebSocket request.");
                return;
            }

            var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            var ct = ctx.RequestAborted;

            // DI’dan servisler
            var sessions = ctx.RequestServices.GetRequiredService<ISessionManager>();
            var broadcaster = ctx.RequestServices.GetRequiredService<ITagBroadcast>();

            try
            {
                var buffer = new byte[8 * 1024];

                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(buffer, ct);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    if (result.MessageType != WebSocketMessageType.Text) continue;

                    var json = Encoding.UTF8.GetString(buffer.AsSpan(0, result.Count));
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var action = root.TryGetProperty("action", out var a) ? a.GetString() : null;

                    if (string.Equals(action, "start", StringComparison.OrdinalIgnoreCase))
                    {
                        var target = root.TryGetProperty("target", out var t) ? t.GetInt32() : 1;
                        if (target < 1) target = 1;

                        // Oturumu başlat: callback’te hem bu ws istemcisine hem dashboard’a yayın yap
                        await sessions.StartAsync(target, async (tag, snap) =>
                        {
                            // 1) websocket.html (aynı bağlantı)
                            if (ws.State == WebSocketState.Open)
                            {
                                await SendJsonAsync(ws, new
                                {
                                    type = "TagAdded",
                                    epc = tag.Epc,
                                    rssi = tag.Rssi,
                                    seenCount = tag.SeenCount,
                                    timestamp = tag.TimestampUtc
                                }, ct);

                                await SendJsonAsync(ws, new
                                {
                                    type = "ProgressUpdated",
                                    current = snap.CurrentUniqueCount,
                                    target = snap.TargetCount
                                }, ct);

                                if (snap.IsCompleted)
                                    await SendJsonAsync(ws, new { type = "ThresholdReached" }, ct);
                            }

                            // 2) dashboard.html (PUB/SUB)
                            await broadcaster.BroadcastTagAsync(tag.Epc, tag.Rssi, tag.SeenCount, tag.TimestampUtc, ct);
                            await broadcaster.BroadcastProgressAsync(snap.CurrentUniqueCount, snap.TargetCount, ct);
                            if (snap.IsCompleted)
                                await broadcaster.BroadcastThresholdAsync(ct);

                        }, ct);
                    }
                    else if (string.Equals(action, "stop", StringComparison.OrdinalIgnoreCase))
                    {
                        await sessions.StopAsync(ct);
                    }
                    else
                    {
                        await SendJsonAsync(ws, new { type = "Error", message = "Unknown action" }, ct);
                    }
                }
            }
            catch (OperationCanceledException) { /* istek iptal edildi */ }
            catch (Exception ex)
            {
                await SafeSend(ws, new { type = "Error", message = ex.Message });
            }
            finally
            {
                try { await sessions.StopAsync(ct); } catch { /* ignore */ }

                if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", ct); } catch { /* ignore */ }
                }
                ws.Dispose();
            }
        }

        private static Task SendJsonAsync(WebSocket ws, object payload, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(payload, JsonOpt);
            var bytes = Encoding.UTF8.GetBytes(json);
            return ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }

        private static async Task SafeSend(WebSocket ws, object payload)
        {
            try
            {
                if (ws.State == WebSocketState.Open)
                    await SendJsonAsync(ws, payload, CancellationToken.None);
            }
            catch { /* ignore */ }
        }
    }
}
