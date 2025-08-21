using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Barfas.Rfid.Api.WebSockets
{
    /// <summary>Basit pub/sub: bağlı dashboard WS istemcilerine JSON yayınlar.</summary>
    public sealed class TagBroadcast : ITagBroadcast
    {
        private readonly ConcurrentDictionary<Guid, WebSocket> _clients = new();
        private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        public Task AddClientAsync(WebSocket ws, CancellationToken ct = default)
        {
            _clients[Guid.NewGuid()] = ws;
            return Task.CompletedTask;
        }

        public Task RemoveClientAsync(WebSocket ws)
        {
            foreach (var kv in _clients)
            {
                if (kv.Value == ws)
                {
                    _clients.TryRemove(kv.Key, out _);
                    break;
                }
            }
            return Task.CompletedTask;
        }

        public Task BroadcastTagAsync(string epc, sbyte rssi, int seenCount, DateTime timestampUtc, CancellationToken ct = default)
            => BroadcastAsync(new { type = "TagAdded", epc, rssi, seenCount, timestamp = timestampUtc }, ct);

        public Task BroadcastProgressAsync(int current, int target, CancellationToken ct = default)
            => BroadcastAsync(new { type = "ProgressUpdated", current, target }, ct);

        public Task BroadcastThresholdAsync(CancellationToken ct = default)
            => BroadcastAsync(new { type = "ThresholdReached" }, ct);

        private async Task BroadcastAsync(object payload, CancellationToken ct)
        {
            if (_clients.IsEmpty) return;

            var json = JsonSerializer.Serialize(payload, _json);
            var bytes = Encoding.UTF8.GetBytes(json);

            foreach (var (id, ws) in _clients.ToArray())
            {
                try
                {
                    if (ws.State == WebSocketState.Open)
                        await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
                    else
                        _clients.TryRemove(id, out _);
                }
                catch
                {
                    _clients.TryRemove(id, out _);
                }
            }
        }
    }
}
