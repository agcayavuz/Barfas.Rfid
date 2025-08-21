using System.Net.WebSockets;

namespace Barfas.Rfid.Api.WebSockets
{
    /// <summary>Dashboard abonelerine JSON mesajları yayınlar.</summary>
    public interface ITagBroadcast
    {
        Task AddClientAsync(WebSocket ws, CancellationToken ct = default);
        Task RemoveClientAsync(WebSocket ws);

        Task BroadcastTagAsync(string epc, sbyte rssi, int seenCount, DateTime timestampUtc, CancellationToken ct = default);
        Task BroadcastProgressAsync(int current, int target, CancellationToken ct = default);
        Task BroadcastThresholdAsync(CancellationToken ct = default);
    }
}
