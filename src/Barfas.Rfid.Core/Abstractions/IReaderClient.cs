using Barfas.Rfid.Core.Models;

namespace Barfas.Rfid.Core.Abstractions
{

    /// <summary>
    /// RFID cihazını veya simülasyonunu temsil eden kaynak.
    /// TagEvent üreterek SignalR katmanına veri sağlar.
    /// </summary>
    public interface IReaderClient : IAsyncDisposable
    {
        /// <summary>
        /// Her bir tag gözlemi için tetiklenen olay.
        /// </summary>
        event EventHandler<TagEvent>? TagObserved;

        /// <summary>Etiket akışını başlatır (idempotent).</summary>
        Task StartAsync(CancellationToken ct = default);

        /// <summary>Etiket akışını durdurur (idempotent).</summary>
        Task StopAsync(CancellationToken ct = default);

        /// <summary>Akışın aktif olup olmadığını belirtir.</summary>
        bool IsRunning { get; }
    }
}
