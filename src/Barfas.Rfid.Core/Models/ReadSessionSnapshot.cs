namespace Barfas.Rfid.Core.Models
{
    /// <summary>
    /// Okuma oturumunun anlık durum görüntüsü (immutable).
    /// UI'ya progress güncellemesi için gönderilir.
    /// </summary>
    public sealed record ReadSessionSnapshot(
        Guid SessionId,
        int TargetCount,
        int CurrentUniqueCount,
        bool IsCompleted
    );
}
