using System.Net.WebSockets;

namespace Barfas.Rfid.Api.WebSockets
{


    /// <summary>
    /// Dashboard abonelerinin bağlanacağı WebSocket endpoint'ini map'ler.
    /// </summary>
    public static class TagDashboardWebSocketEndpoint
    {
        /// <summary>
        /// /ws/dashboard yoluna WebSocket handler'ını bağlar.
        /// </summary>
        public static IEndpointRouteBuilder MapTagDashboardWebSocketEndpoint(
            this IEndpointRouteBuilder app,
            string path = "/ws/dashboard")
        {
            app.Map(path, HandleAsync);
            return app;
        }

        /// <summary>
        /// WebSocket handshake ve yaşam döngüsü. Bu uç yalnızca "dinleyici"dir.
        /// </summary>
        private static async Task HandleAsync(HttpContext ctx)
        {
            // Sadece WebSocket isteklerini kabul ediyoruz
            if (!ctx.WebSockets.IsWebSocketRequest)
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                await ctx.Response.WriteAsync("Expected WebSocket request.");
                return;
            }

            // Handshake
            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            var ct = ctx.RequestAborted;

            // Yayıncı servisi (DI)
            var broadcaster = ctx.RequestServices.GetRequiredService<ITagBroadcast>();

            // Bu istemciyi abone listesine ekle
            await broadcaster.AddClientAsync(ws, ct);

            try
            {
                // Pasif dinleyici: İstemciden gelen mesajları işlemiyoruz.
                // Ancak bağlantıyı nazikçe kapatabilmek için kısa bir receive döngüsü bırakıyoruz.
                var buffer = new byte[1024];

                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await ws.ReceiveAsync(buffer, ct);

                    // İstemci kapanış başlattıysa döngüden çık
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    // Bu endpoint "read-only": Gönderilen içerikleri yok sayıyoruz.
                    // (Gerekirse ping/pong ya da heartbeat burada ele alınabilir.)
                }
            }
            catch (OperationCanceledException)
            {
                // İstek iptal edildi (sunucu kapanışı, bağlantı koptu vs.) — sessizce geç
            }
            catch
            {
                // Basit senaryoda log'a gerek yok; üretimde ILogger enjekte edip loglayabilirsiniz.
            }
            finally
            {
                // Abonelikten çıkar ve nazikçe kapat
                await broadcaster.RemoveClientAsync(ws);

                if (ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    try
                    {
                        await ws.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "bye",
                            ct);
                    }
                    catch { /* ignore */ }
                }
            }
        }
    }

}
