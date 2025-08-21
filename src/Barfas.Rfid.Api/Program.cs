// Barfas.Rfid.Api
// Amaç: RFID demo (simülasyon). Aynı API içinde hem SignalR Hub'ı hem de ham WebSocket endpoint'i sunar.
// Not: Hangi realtime kanal(lar)ının aktif olacağı appsettings:Realtime üzerinden seçilir.

// [1] Servisler / DI (CORS, Realtime, Core bağımlılıklar, HttpClient)
// [2] Middleware Pipeline (HTTPS, CORS, Statik Dosyalar, Swagger, WebSockets)
// [3] Endpoints (Sistem/Health, Realtime: SignalR & WS, Yardımcı: Notify)

using Barfas.Rfid.Api.Hubs;           // SignalR Hub
using Barfas.Rfid.Api.Services;       // SessionManager (iş mantığı)
using Barfas.Rfid.Api.WebSockets;     // WebSocket endpoint extension
using Barfas.Rfid.Core.Abstractions;  // ISessionManager, IReaderClient
using Barfas.Rfid.Simulator;          // SimulatorReaderClient (donanım yokken)
using Microsoft.OpenApi.Models;       

var builder = WebApplication.CreateBuilder(args);

#region [1] SERVİSLER / DI 

const string CORS_POLICY = "frontend";
string[] ALLOWED_ORIGINS = new[]
{
    "http://localhost:5173", "http://127.0.0.1:5173",
    "http://localhost:3000", "http://127.0.0.1:3000"
};
builder.Services.AddCors(opt =>
{
    opt.AddPolicy(CORS_POLICY, p => p
        .WithOrigins(ALLOWED_ORIGINS)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});


// SignalR servis kaydı (Hub'ı gerçekten map'ler Endpoints aşamasında, config'e bağlı)
builder.Services.AddSignalR();


builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Barfas.Rfid.Api",
        Version = "v1",
        Description = "RFID demo (simülasyon) — Minimal API + SignalR + WebSocket"
    });
});


// Donanım yok: simülatör. Gerçek cihaza geçince yalnızca bu satırı değiştirmeniz yeterli.
builder.Services.AddSingleton<IReaderClient, SimulatorReaderClient>();

// Okuma oturumu yönetimi: benzersiz EPC sayımı + eşik kontrolü + progress push
builder.Services.AddSingleton<ISessionManager, SessionManager>();

builder.Services.AddSingleton<ITagBroadcast, TagBroadcast>();

// "notify" adıyla /notify endpoint'inde kullanıyoruz.
builder.Services.AddHttpClient("notify");
#endregion

var app = builder.Build();

#region [2] MIDDLEWARE PIPELINE (sıra önemli: güvenlik → CORS → statik → Swagger → WS)

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseCors(CORS_POLICY);

// wwwroot altında test sayfaları 
app.UseDefaultFiles();   
app.UseStaticFiles();    


app.UseSwagger();
app.UseSwaggerUI(o =>
{
    o.SwaggerEndpoint("/swagger/v1/swagger.json", "Barfas.Rfid.Api v1");
   
});

// WebSocket desteği
app.UseWebSockets();
#endregion

#region [3] ENDPOINTS (Sistem/Health, Realtime, Yardımcı)

// [5.1] Sistem / Health / Hakkında
app.MapGet("/", () => Results.Ok(new { ok = true, service = "Barfas.Rfid.Api" }))
   .WithName("Root")
   .WithOpenApi();

app.MapGet("/healthz", () => Results.Ok("OK"))
   .WithName("Healthz")
   .WithOpenApi();

app.MapGet("/about", () => new
{
    env = app.Environment.EnvironmentName,
    version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown"
})
.WithName("About")
.WithOpenApi();

// Realtime Kanal Seçimi (appsettings:Realtime üzerinden)
bool useSignalR = app.Configuration.GetValue<bool>("Realtime:UseSignalR", true);
bool useWebSocket = app.Configuration.GetValue<bool>("Realtime:UseWebSocket", true);

// SignalR Hub (istemciler /hub/tags adresine bağlanır)
if (useSignalR)
{
    app.MapHub<TagHub>("/hub/tags")
       .RequireCors(CORS_POLICY);
}

// WebSocket endpoint (JSON protokolü: start/stop ve TagAdded/Progress/Threshold)
if (useWebSocket)
{
    app.MapTagWebSocketEndpoint("/ws/tags");
    app.MapTagDashboardWebSocketEndpoint("/ws/dashboard");
}


app.MapGet("/notify", async (IHttpClientFactory f, HttpContext ctx) =>
{  
    ctx.Response.Headers.CacheControl = "no-store";
    var client = f.CreateClient("notify");
    await client.GetAsync("http://81.213.79.71/barfas/rfid/sayac.php?=ok");

    return Results.Ok(new { ok = true });
})
.RequireCors(CORS_POLICY)
.WithName("Notify")
.WithOpenApi();
#endregion

app.Run();

