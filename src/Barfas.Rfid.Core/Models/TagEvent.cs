namespace Barfas.Rfid.Core.Models
{

    /// <summary>
    /// Tek bir etiket gözlemini temsil eder.
    /// Aynı EPC birden çok kez okunabilir; UI'da satır bazında SeenCount gösterilebilir.
    /// </summary>
    public sealed class TagEvent
    {
        /// <summary>Hex formatında EPC metni (örn: E2000017221101441890A1B2).</summary>
        public required string Epc { get; init; }

        /// <summary>RSSI sinyal gücü (dBm cinsinden yaklaşık değer; simülasyonda -75..-40).</summary>
        public sbyte Rssi { get; init; }

        /// <summary>Okumanın gerçekleştiği zaman damgası (UTC).</summary>
        public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;

        /// <summary>Aynı oturumda bu EPC'nin kaçıncı kez görüldüğü.</summary>
        public int SeenCount { get; init; } = 1;
    }
}
