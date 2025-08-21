using Barfas.Rfid.Api.Hubs;
using Barfas.Rfid.Core.Abstractions;
using Barfas.Rfid.Core.Models;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

// Barfas.Rfid.Api/Services/SessionManager.cs
// Amaç: Okuma oturumunu yönetir; sadece benzersiz EPC'leri UI'a gönderir; eşik sağlanınca akışı durdurur.

namespace Barfas.Rfid.Api.Services
{
    #region SessionManager
    /// <summary>
    /// Okuma oturumu (read session) iş servisi:
    /// - Yeni oturum başlatır, önceki oturumu güvenle kapatır.
    /// - Benzersiz EPC sayımı yapar; sadece yeni EPC geldiğinde UI'a <c>TagAdded</c> gönderir.
    /// - Hedefe ulaşıldığında <c>ThresholdReached</c> olayını tetikler ve akışı durdurur.
    /// </summary>
    public sealed class SessionManager : ISessionManager
    {
        #region Fields
        private readonly IReaderClient _reader;                        // Simülatör veya gerçek okuyucu
        private readonly object _sync = new();                         // Eşzamanlı erişim kilidi

        private Guid _sessionId;                                       // Aktif oturum ID'si (yoksa Guid.Empty)
        private int _target;                                           // Hedef benzersiz EPC sayısı
        private bool _completed;                                       // Eşik sağlandı mı?

        // Hub tarafından verilen "progress push" callback'i.
        // Not: Null olabilir; StartAsync'te atanır, StopAsync'te sıfırlanır.
        private Func<TagEvent, ReadSessionSnapshot, Task>? _progress;

        // Oturum boyunca görülen benzersiz EPC seti
        private readonly HashSet<string> _unique = new(StringComparer.OrdinalIgnoreCase);

        // (İsteğe bağlı) Her EPC'nin tekrar sayısı — analiz/izleme amaçlı
        private readonly ConcurrentDictionary<string, int> _seen = new();
        #endregion

        #region Ctor
        public SessionManager(IReaderClient reader)
        {
            _reader = reader;
            // Simülatörden/okuyucudan her tag geldiğinde bu handler çağrılır
            _reader.TagObserved += OnTagObserved;
        }
        #endregion

        #region ISessionManager
        /// <summary>
        /// Yeni bir okuma oturumu başlatır. Önceki oturum (varsa) güvenle kapatılır.
        /// </summary>
        public async Task<Guid> StartAsync(
            int targetCount,
            Func<TagEvent, ReadSessionSnapshot, Task> progressHandler,
            CancellationToken ct = default)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(targetCount, 1);

            // Var olanı durdur (idempotent)
            await StopAsync(ct).ConfigureAwait(false);

            lock (_sync)
            {
                _sessionId = Guid.NewGuid();
                _target = targetCount;
                _completed = false;
                _progress = progressHandler;

                _unique.Clear();
                _seen.Clear();
            }

            // Cihaz/simülatör akışını başlat
            if (!_reader.IsRunning)
                await _reader.StartAsync(ct).ConfigureAwait(false);

            return _sessionId;
        }

        /// <summary>
        /// Aktif oturumu durdurur ve tüm sayaçları sıfırlar.
        /// </summary>
        public async Task StopAsync(CancellationToken ct = default)
        {
            if (_reader.IsRunning)
                await _reader.StopAsync(ct).ConfigureAwait(false);

            lock (_sync)
            {
                _sessionId = Guid.Empty;
                _target = 0;
                _completed = false;
                _progress = null;

                _unique.Clear();
                _seen.Clear();
            }
        }
        #endregion

        #region Event Handler (yalnızca benzersizleri listele)
        /// <summary>
        /// Okuyucu/simülatör yeni bir tag gözlemi bildirdiğinde çağrılır.
        /// Yalnızca benzersiz EPC geldiğinde UI'a TagAdded gönderir.
        /// Eşik sağlandığında ThresholdReached olayını tek kez tetikler ve akışı durdurur.
        /// </summary>
        private void OnTagObserved(object? sender, TagEvent e)
        {
            // Yerel kopyalar (lock dışına taşımak için)
            Func<TagEvent, ReadSessionSnapshot, Task>? cb = null;
            ReadSessionSnapshot snap = default;
            bool justCompleted = false;
            bool addedUnique = false;

            lock (_sync)
            {
                // Oturum yoksa ya da zaten tamamlandıysa gelen veriyi tamamen yok say
                if (_sessionId == Guid.Empty || _completed)
                    return;

                // (İsteğe bağlı) tekrar sayacı
                _seen.AddOrUpdate(e.Epc, 1, (_, v) => v + 1);

                // --- SADECE BENZERSİZLER ---
                addedUnique = _unique.Add(e.Epc);      // true: yeni EPC, false: tekrar
                var current = _unique.Count;           // benzersiz EPC sayısı

                // Eşik oluştu mu?
                justCompleted = (_target > 0 && current >= _target);

                // UI'a gönderilecek snapshot (immutable)
                snap = new ReadSessionSnapshot(
                    SessionId: _sessionId,
                    TargetCount: _target,
                    CurrentUniqueCount: current,
                    IsCompleted: justCompleted
                );

                if (justCompleted)
                    _completed = true;                 // bundan sonraki tüm tag'ler başta return edecektir

                // Sadece yeni EPC gelmişse UI'a TagAdded/Progress gönderelim.
                // (Tekrar okumalarda tabloya satır eklemiyoruz.)
                cb = addedUnique ? _progress : null;
            }

            // UI push — fire-and-forget; hata varsa loglayabilirsiniz.
            if (cb is not null)
                _ = cb.Invoke(e, snap);

            // Eşik oluştuysa akışı durdur (asenkron); overshoot'u server tarafında keser
            if (justCompleted)
                _ = _reader.StopAsync();
        }
        #endregion
    }
    #endregion
}
