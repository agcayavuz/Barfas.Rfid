using Barfas.Rfid.Core.Models;

namespace Barfas.Rfid.Core.Abstractions
{
    /// <summary>
    /// Okuma oturumunu yöneten servis için sözleşme.
    /// </summary>
    public interface ISessionManager
    {
        /// <summary>
        /// Yeni bir okuma oturumu başlatır. Önceki oturum (varsa) güvenle kapatılır.
        /// </summary>
        /// <param name="targetCount">Hedef benzersiz EPC sayısı (>= 1).</param>
        /// <param name="progressHandler">
        /// Her yeni tag gözleminde çağrılacak geri çağırım.
        /// <para>
        /// <c>TagAdded</c> ve <c>ProgressUpdated</c> yayınları için gerekli bilgiyi taşır:
        /// <list type="bullet">
        ///   <item><description><see cref="TagEvent"/>: gelen tag (EPC, RSSI, zaman, seen).</description></item>
        ///   <item><description><see cref="ReadSessionSnapshot"/>: anlık sayaç/oturum durumu.</description></item>
        /// </list>
        /// </para>
        /// </param>
        /// <param name="ct">İptal belirteci (isteğe bağlı).</param>
        /// <returns>Başlatılan oturumun <see cref="Guid"/> kimliği.</returns>
        Task<Guid> StartAsync(
            int targetCount,
            Func<TagEvent, ReadSessionSnapshot, Task> progressHandler,
            CancellationToken ct = default
        );

        /// <summary>
        /// Aktif oturumu durdurur (varsa) ve sayaçları sıfırlar.
        /// </summary>
        /// <param name="ct">İptal belirteci (isteğe bağlı).</param>
        Task StopAsync(CancellationToken ct = default);
    }
 
}
