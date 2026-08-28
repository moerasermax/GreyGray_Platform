// Storefront BFF — 公開 API，給客人用（Web / LINE LIFF）。
//
// 這一層只做「組裝與授權」，不含任何業務邏輯（藍圖 §07）。
// 前面擋著 Cloudflare WAF ＋ Turnstile ＋ Rate Limit，自架端零 inbound port（走 cloudflared）。
//
// M0 的驗收條件之一：服務重開機後自動起來。部署以 NSSM 註冊成 Windows service，
// 沿用既有 12 個服務的 S4U ＋ BootTrigger ＋ 每 5 分鐘 watchdog 慣例。

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// TODO(M0-4)：AddPlatform() —— Outbox、Idempotency、Saga Timer、OTel。
// TODO(M0-5)：逐一 AddXxxModule() —— 只能呼叫 *.Infra 公開的註冊擴充方法，
//             不得 using 任何 *.Core 命名空間（Architecture.Tests 會擋）。
// TODO(M1a-1)：BFF 認證 —— access token 永不進瀏覽器，
//              前端只拿 HttpOnly; Secure; SameSite=Lax cookie。

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

// 健康檢查。prod-monitor 會收錄這個端點（port 5000，5000 號段避開既有占用）。
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "storefront" }));

app.Run();
