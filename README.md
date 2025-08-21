# BARFAS RFID Demo (.NET 8 • Minimal API • WebSocket)

Bu demo, **okunacak etiket hedefini** kullanıcıdan alır; benzersiz EPC’leri sayar, hedefe ulaşıldığında **uyarı verir** ve **bir sayaç servisini** tetiklemek için API üzerinden bir bildirim gönderir.  
Sistem iki ayrı web sayfasıyla çalışır:

- **`/simulator.html`** — “El terminali” arayüzü; oturumu **başlatır/durdurur**, canlı etiket akışını görür ve hedefe ulaşınca API’ye **/notify** çağrısını yapar.
- **`/dashboard.html`** — Sadece **dinler ve listeler**; API’nin yayınladığı canlı akışı tabloya işler, **arama** ve **tarih sütununa göre sıralama** sunar.

> **Önemli:** Veriler **daima API üzerinden akar.** `simulator.html` doğrudan `dashboard.html`’a veri göndermez; API, gelen olayları **yayıncı servis** aracılığıyla dashboard abonelerine dağıtır. **Her iki sayfa da aynı anda açık olmalıdır.**

---

## İçindekiler
- [Teknoloji Yığını](#teknoloji-yığını)
- [Mimari ve Veri Akışı](#mimari-ve-veri-akışı)
- [Proje Yapısı (özet)](#proje-yapısı-özet)
- [Kurulum & Çalıştırma](#kurulum--çalıştırma)
- [HTTP & WebSocket Uçları](#http--websocket-uçları)
- [Simulator (simulator.html)](#simulator-simulatorhtml)
- [Dashboard (dashboard.html)](#dashboard-dashboardhtml)
- [Teknik Ayrıntılar](#teknik-ayrıntılar)
- [Yapılandırma (CORS vb.)](#yapılandırma-cors-vb)
- [Hata Ayıklama / Doğrulama](#hata-ayıklama--doğrulama)
- [Geliştirme Fikirleri](#geliştirme-fikirleri)

---

## Teknoloji Yığını
- **.NET 8** Minimal API
- **WebSocket** ile gerçek zamanlı iletişim (ham WS)
- **Swagger/OpenAPI** (yalnızca HTTP uçları için)
- **CORS** yapılandırması (geliştirmede farklı origin’lere izin)
- **DI (Dependency Injection)** & `HttpClientFactory`
- **Vanilla HTML/CSS/JS** (framework bağımsız arayüzler)

> *Veritabanı yoktur.* Etiket akışı simüle edilir, durum bellek içindedir.

---

## Mimari ve Veri Akışı

```
[simulator.html] --(WS: /ws/tags, start/stop)--> [API: SessionManager + SimulatorReader]
        |                                            |
        |                             [Publish] ---- v
        |                             (TagBroadcast /ws/dashboard)
        |                                            |
        '---(HTTP GET /notify) <--- hedef dolunca ---'
                                                   |
                                                   '---(API)--> http://81.213.79.71/barfas/rfid/sayac.php?=ok
```

- **Simulator** sayfası WS ile **oturumu başlatır**, benzersiz EPC’ler akar.
- API, her olayda **yayıncı servise** (TagBroadcast) bildirir.
- **Dashboard** sayfası `/ws/dashboard`’a bağlanır, yayınları **tabloya** işler.
- Hedef dolunca `simulator.html` **`/notify`** endpoint’ine **GET** atar; API bu isteği dış adrese **proxy** eder.

---

## Proje Yapısı (özet)

```
Barfas.Rfid.Api/
 ├─ Program.cs
 ├─ wwwroot/
 │   ├─ simulator.html
 │   └─ dashboard.html
 ├─ WebSockets/
 │   ├─ TagWebSocketEndpoint.cs           ( /ws/tags : simulator kontrol & akış )
 │   ├─ TagDashboardWebSocketEndpoint.cs  ( /ws/dashboard : dashboard dinleyici )
 │   ├─ ITagBroadcast.cs                  ( yayın sözleşmesi )
 │   └─ TagBroadcast.cs                   ( dashboard abonelerine yayın )
 └─ Services/
     └─ SessionManager.cs                 ( oturum & benzersiz EPC sayımı )
Barfas.Rfid.Core/
 └─ Abstractions/ISessionManager.cs
Barfas.Rfid.Simulator/
 └─ SimulatorReaderClient.cs              ( rastgele EPC akışı, test amaçlı )
```

---

## Kurulum & Çalıştırma

**Gereksinimler**
- .NET 8 SDK
- Visual Studio 2022+ (veya `dotnet` CLI)

**Adımlar**
1. Çözümü açın, **Barfas.Rfid.Api** projesini **Startup Project** yapın.
2. Çalıştırın (**F5**). Konsol/VS çıktısındaki portu not edin.
3. Tarayıcıdan:
   - **`https://localhost:{port}/simulator.html`**
   - **`https://localhost:{port}/dashboard.html`**
4. **İki sayfayı aynı anda açık tutun.** (simulator kontrol, dashboard izleme)

> `wwwroot` altındaki dosyalar doğrudan API tarafından servis edilir; harici statik sunucu gerekmez.

---

## HTTP & WebSocket Uçları

**WebSocket**
- **`/ws/tags`** — `simulator.html`’ün bağlandığı uç; komutlar:
  - `{"action":"start","target": <int>}`
  - `{"action":"stop"}`
- **`/ws/dashboard`** — `dashboard.html`’ün bağlandığı uç; **salt okunur** (read-only) yayın alır.

**HTTP**
- **`/notify` [GET]** — hedef dolunca `simulator.html` çağırır. API, dış adrese (`…/sayac.php?=ok`) GET atar.
- **`/healthz`**, **`/about`**, **`/`** — durum & bilgi uçları.
- **(Opsiyonel debug)** `GET /debug/broadcast/recent`, `POST /debug/broadcast/fake`

**JSON Olayları (API → UI)**
```json
// Tag eklendiğinde
{ "type":"TagAdded", "epc":"E200341201...", "rssi":-55, "seenCount":1, "timestamp":"2025-08-21T09:00:01Z" }
// İlerleme güncellendiğinde
{ "type":"ProgressUpdated", "current": 3, "target": 5 }
// Eşik sağlandığında
{ "type":"ThresholdReached" }
```

---

## Simulator (simulator.html)

**Amaç:** Hedef benzersiz EPC sayısını girip akışı **başlatmak/durdurmak**, canlı listeyi görmek, hedef dolunca **uyarı ve /notify** çağrısını yapmak.

**Butonlar & Durum**
- **Bağlan**: `/ws/tags` ile WebSocket el sıkışması yapar. Bağlanınca pasif olur.
- **Başlat**: Hedef (input) ile `{"action":"start","target":N}` gönderir; akış başlar. Başlat → pasif, Durdur → aktif.
- **Durdur**: `{"action":"stop"}` gönderir; akış durur. Durdur → pasif, Başlat → aktif.

**Çalışma Prensibi**
1. **Bağlan** → `/ws/tags` (WS **OPEN**)
2. **Başlat** → `start` komutu; API simülatörü çalıştırır.
3. **Benzersiz EPC’ler** geldikçe tabloya eklenir; sayaç **current/target** güncellenir.
4. **Hedef dolunca** ekran altındaki yeşil bant: **“Yeterli Sayıda Etiket Okunmuştur”**
5. Aynı anda **`GET /notify`** çağrısı yapılır → API dış adrese proxy eder.
6. Yeni hedefle tekrar **Başlat** yapılabilir.

**Notlar**
- *Benzersiz EPC mantığı:* aynı EPC tekrar gelirse tabloya **yeni satır eklenmez**, yalnızca sunucu tarafında “seenCount” artar.
- *Overshoot engeli:* Hedefe ulaştıktan sonra sunucu **ek tag’leri yoksayar** (overshoot yok).

---

## Dashboard (dashboard.html)

**Amaç:** API’nin yayınladığı canlı olayları **`/ws/dashboard`** üzerinden dinlemek ve tabloya işlemek.

**Veriyi nasıl yakalar?**
- Sayfa yüklenince `/ws/dashboard`’a bağlanır (WS 101).
- API, her `TagAdded` ve `ProgressUpdated` olayında **TagBroadcast** üzerinden tüm bağlı dashboard’lara JSON gönderir.
- **Dashboard hiçbir komut göndermez**; yalnızca **dinler** ve tabloyu günceller.

**Listeleme, Arama, Sıralama**
- **Arama kutusu**: EPC / RSSI / Seen / Zaman içerisinde **içeren** eşleşmeleri filtreler (client-side).
- **Tarih sütunu (Zaman)**: başlığa tıklayarak **artan/azalan** sıralamaya geçilir.
- Görünür satır sayısı üstte “**Toplam**” rozeti ile gösterilir.
- Uzun EPC’ler **monospace** yazı tipi ve satır kırma ile taşmadan gösterilir.

---

## Teknik Ayrıntılar

### SimulatorReaderClient (Barfas.Rfid.Simulator)
- Gerçek cihaz yokken rastgele EPC ve **rastgele gecikme** ile `TagObserved` olayı üretir.
- `StartAsync/StopAsync` ile kontrol edilir; `IsRunning` durumu vardır.

### SessionManager (Barfas.Rfid.Api/Services)
- **Tek oturum** yönetir: `StartAsync(target, callback)` ve `StopAsync()`.
- Her tag geldiğinde:
  - **Benzersiz** EPC setine ekler (HashSet)
  - `ReadSessionSnapshot` ile **current/target** ve **IsCompleted** hesaplanır.
  - **Callback** tetiklenir (UI push).
  - Eşik sağlandıysa akış **tek kez** tamamlanır ve **durdurulur**.

### TagWebSocketEndpoint ( `/ws/tags` )
- `simulator.html`’den gelen `start/stop` komutlarını **JSON** olarak alır.
- Oturum başlarken callback içinde **iki yere** yazar:
  1) Aynı WS bağlantısına (`TagAdded / ProgressUpdated / ThresholdReached`)
  2) **TagBroadcast**’a (dashboard’a yayın)

### TagBroadcast & TagDashboardWebSocketEndpoint ( `/ws/dashboard` )
- Basit bir **pub/sub** mekanizması:
  - `TagBroadcast` bağlı dashboard WS istemcilerinin listesini tutar, **her olayı hepsine** gönderir.
  - `TagDashboardWebSocketEndpoint` yalnızca **dinleyici**dir; gelen mesajları işleme almaz (read-only).

### Notify Akışı
- `simulator.html` hedef dolunca **`GET /notify`** çağırır.
- `Program.cs` içindeki `/notify` endpoint’i, `HttpClientFactory` ile **`http://81.213.79.71/barfas/rfid/sayac.php?=ok`** adresine **GET** atar.
- Yanıt içeriği kullanılmaz; tetikleme amaçlıdır.

---

## Yapılandırma (CORS vb.)
- **CORS**: `Program.cs` içinde `CORS_POLICY = "frontend"`; geliştirmede `localhost:5173/3000` gibi origin’lere izin verilir. Arayüzü farklı bir porttan açacaksanız origin’i listeye ekleyin.
- **Statik Dosyalar**: `UseDefaultFiles` + `UseStaticFiles` → `wwwroot` altı otomatik servis edilir.
- **WebSocket**: `UseWebSockets()` **zorunlu** (endpoint’lerden önce çağrılır).

---

## Hata Ayıklama / Doğrulama

**Tarayıcı (Network → WS)**
- `simulator.html` → `/ws/tags` satırında **Frames** sekmesinde `TagAdded`/`ProgressUpdated`/`ThresholdReached` karelerini görün.
- `dashboard.html` → `/ws/dashboard`’da yayın kareleri görünür.

**API Debug (opsiyonel eklendi ise)**
- `GET /debug/broadcast/recent` → Son yayınlanan JSON’ları görürsünüz.
- `POST /debug/broadcast/fake` → Dashboard’a test amaçlı sahte bir `TagAdded` gönderir.

**Ortak Sorunlar**
- **Bağlan butonuna tıklanıyor ama veri akmıyor:** WS 101 (Switching Protocols) görünüyor mu? CORS/HTTPS/port eşleşmelerini kontrol edin.
- **Dashboard boş:** `ITagBroadcast` servis kaydı var mı? `/ws/dashboard` gerçekten map’li mi? API log’larında “Dashboard client connected” mesajı geliyor mu?

---

## Geliştirme Fikirleri
- Çoklu oturum desteği (connectionId → ayrı sayaçlar)
- Sunucu tarafı kalıcılık (in-memory yerine in-proc cache/redis)
- CSV / Excel dışa aktarma
- Tarih aralığı filtreleri, yalnızca **benzersiz EPC**/**tüm okumalar** görünümü arasında geçiş

---

**Hazır 🎯**  
İki sayfayı birlikte açtığınızda (simulator + dashboard), simulator’dan başlatılan her okuma **API → yayıncı** üzerinden dashboard’a düşer; hedef dolduğunda alt bantta uyarı çıkar ve **/notify** tetiklenir.
