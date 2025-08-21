using Barfas.Rfid.Core.Abstractions;
using Barfas.Rfid.Core.Models;
using System.Collections.Concurrent;

namespace Barfas.Rfid.Simulator
{
    /// <summary>
    /// Fiziksel cihaz yerine geçen basit simülatör.
    /// Rastgele EPC üreterek belirli aralıklarla TagObserved olayı yayar.
    /// </summary>
    public sealed class SimulatorReaderClient : IReaderClient
    {
        private readonly Random _random = new();
        private CancellationTokenSource? _cts;
        private Task? _loop;
        private volatile bool _running;
        private readonly ConcurrentDictionary<string, int> _seen = new();

        /// <inheritdoc />
        public event EventHandler<TagEvent>? TagObserved;

        /// <inheritdoc />
        public bool IsRunning => _running;

        /// <inheritdoc />
        public Task StartAsync(CancellationToken ct = default)
        {
            if (_running) return Task.CompletedTask;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _running = true;
            _loop = Task.Run(async () =>
            {
                while (!_cts!.IsCancellationRequested)
                {
                    // 50..200 ms arası bekle
                    await Task.Delay(_random.Next(1000, 2000), _cts.Token);

                    // %65 yeni EPC, %35 daha önce görülen
                    var epc = (_random.NextDouble() < 0.65) ? NewEpc() : PickSeenEpc();
                    var seen = _seen.AddOrUpdate(epc, 1, (_, v) => v + 1);

                    var evn = new TagEvent
                    {
                        Epc = epc,
                        Rssi = (sbyte)_random.Next(-75, -40),
                        TimestampUtc = DateTime.UtcNow,
                        SeenCount = seen
                    };
                    TagObserved?.Invoke(this, evn);
                }
            }, _cts.Token);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public async Task StopAsync(CancellationToken ct = default)
        {
            if (!_running) return;
            try
            {
                _cts?.Cancel();
                if (_loop is not null) await _loop;
            }
            catch (OperationCanceledException) { /* expected */ }
            finally
            {
                _running = false;
                _cts?.Dispose();
                _cts = null;
                _loop = null;
                _seen.Clear();
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            // Kaynakları kapat
            await StopAsync().ConfigureAwait(false);

            // Finalizer varsa tekrar çalışmasın
            GC.SuppressFinalize(this);
        }

        private string NewEpc()
        {
           
            Span<byte> bytes = stackalloc byte[4];
            _random.NextBytes(bytes);
            return "TAG-" + Convert.ToHexString(bytes);
        }

        private string PickSeenEpc()
        {
            if (_seen.IsEmpty) return NewEpc();
            // basitçe rastgele bir öğe seç
            var idx = _random.Next(0, _seen.Count);
            return _seen.Keys.ElementAt(idx);
        }
    }

}
