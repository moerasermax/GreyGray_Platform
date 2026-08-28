// Admin BFF — 內部 API，給你、太太與團隊用。
//
// 完全不對公網開放：身分由 Cloudflare Access（Zero Trust）驗過並簽發 JWT，
// 後端驗 CF 公鑰。團隊成員離職時在 CF 移除即可，不必改 code、不必重新部署。
//
// 權限模型與客人端完全不共用，獨立 OpenAPI 文件、獨立部署、獨立版本線。

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// TODO(M0-4)：AddPlatform()。
// TODO(M0-5)：逐一 AddXxxModule()。
// TODO(M1a-2)：Cloudflare Access JWT 驗證 —— 驗 CF 公鑰、比對 aud，
//              解析失敗一律拒絕，不得 fallback 成匿名。

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "admin" }));

app.Run();
