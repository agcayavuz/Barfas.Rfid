


using Barfas.Rfid.Core.Abstractions;
using Barfas.Rfid.Core.Models;
using Microsoft.AspNetCore.SignalR;


namespace Barfas.Rfid.Api.Hubs
{
    // Amaç: Okuma oturumunu başlat/durdur; tag/ilerleme olaylarını güvenli biçimde yayınla.
    // Not: Yayınlar IHubContext ile yapılır; böylece Hub ömrü dışından da çalışır.
    public sealed class TagHub : Hub
    {
        private readonly ISessionManager _sessions;
        private readonly IHubContext<TagHub> _hub; 

        public TagHub(ISessionManager sessions, IHubContext<TagHub> hub)
        {
            _sessions = sessions;
            _hub = hub;
        }

        /// <summary>
        /// Yeni bir okuma oturumu başlatır. Her yeni BENZERSİZ EPC geldiğinde
        /// sadece bu bağlantıya (ConnectionId) TagAdded ve ProgressUpdated gönderilir.
        /// Eşik oluştuğunda ThresholdReached gönderilir.
        /// </summary>
        public async Task<Guid> StartSession(int target)
        {
            if (target < 1)
                throw new HubException("Target must be >= 1.");

            var connectionId = Context.ConnectionId;

            var sessionId = await _sessions.StartAsync(
                target,
                async (tag, snap) =>
                {
                    // ÖNEMLİ: Hub ömründen bağımsız güvenli yayın
                    var client = _hub.Clients.Client(connectionId);

                    // Liste satırı
                    await client.SendAsync("TagAdded", new
                    {
                        epc = tag.Epc,
                        rssi = tag.Rssi,
                        timestamp = tag.TimestampUtc,
                        seenCount = tag.SeenCount
                    });

                    // Sayaç
                    await client.SendAsync("ProgressUpdated", snap.CurrentUniqueCount, snap.TargetCount);

                    // Eşik sağlandı
                    if (snap.IsCompleted)
                        await client.SendAsync("ThresholdReached");
                }
            );

            return sessionId;
        }

        /// <summary>Aktif okuma oturumunu durdurur (varsa).</summary>
        public Task StopSession() => _sessions.StopAsync();
    }
}
