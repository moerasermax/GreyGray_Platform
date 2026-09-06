using System.Globalization;
using System.Net;
using System.Text;
using GreyGray.Tools.EcpaySimulator.Core;

// ── dev 用的綠界模擬器（ADR-029）───────────────────────────────────────────
//
// 假的不是我們的 adapter，假的是綠界的伺服器。這支行程扮演綠界：
//   收結帳表單 → 驗簽 → 畫一頁讓人按「成功／失敗」→ 把付款結果通知 POST 回 ReturnURL
//   → 顯示 webhook 的回應（預期 1|OK）→ 提供「返回商店」（ClientBackURL）。
// 另外回應退刷 /CreditDetail/DoAction。
//
// 換回正式綠界 ＝ 明確填入 Payment:ECPay:CheckoutUrl／CreditDetailUrl 的正式站網址（兩者必填、沒有預設）＋ 真憑證
//              ＋ 不開 Payment:ECPay:AllowNonEcpayEndpoints。沒有任何一行程式碼要改。

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

var merchantId = Required(configuration, "Payment:ECPay:MerchantId");
var hashKey = Required(configuration, "Payment:ECPay:HashKey");
var hashIv = Required(configuration, "Payment:ECPay:HashIV");

// 這支只能配假憑證。拿真的特店代號起這支，等於用一台假伺服器冒充真綠界——
// 沒有任何情境需要那樣做，所以直接拒絕啟動，不留給紀律去守。
if (!merchantId.StartsWith(EcpaySimulatorCore.FakePrefix, StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        $"Payment:ECPay:MerchantId 目前是 '{merchantId}'，不是以 '{EcpaySimulatorCore.FakePrefix}' 開頭。" +
        "綠界模擬器只能配假憑證——真的特店代號請直接指向綠界，不要經過這支。");
}

builder.Services.AddHttpClient("Webhook", client => client.Timeout = TimeSpan.FromSeconds(10));

var app = builder.Build();
var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("EcpaySimulator");

app.Use(async (context, next) =>
{
    // 每個請求印一行。dev 用的東西看不見就等於不存在。
    log.LogInformation("{Method} {Path}", context.Request.Method, context.Request.Path.Value);
    await next(context);
});

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "ecpay-simulator", merchantId }));

app.MapPost("/Cashier/AioCheckOut/V5", async (HttpContext context) =>
{
    var fields = await ReadFormAsync(context);
    var validation = EcpaySimulatorCore.ValidateCheckoutForm(fields, merchantId, hashKey, hashIv);
    if (!validation.IsValid)
    {
        log.LogWarning("結帳表單被拒：{RtnCode} {RtnMsg}", validation.RtnCode, validation.RtnMsg);
        return Html(RenderError(validation.RtnCode, validation.RtnMsg), StatusCodes.Status400BadRequest);
    }

    return Html(RenderCheckout(fields));
});

app.MapPost("/Cashier/AioCheckOut/V5/decide", async (
    HttpContext context,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken) =>
{
    var posted = await ReadFormAsync(context);
    posted.Remove("Outcome", out var outcomeText);
    var validation = EcpaySimulatorCore.ValidateCheckoutForm(posted, merchantId, hashKey, hashIv);
    if (!validation.IsValid)
    {
        log.LogWarning("決策頁的表單被拒：{RtnCode} {RtnMsg}", validation.RtnCode, validation.RtnMsg);
        return Html(RenderError(validation.RtnCode, validation.RtnMsg), StatusCodes.Status400BadRequest);
    }

    var outcome = StringComparer.OrdinalIgnoreCase.Equals(outcomeText, nameof(SimulatedOutcome.Failure))
        ? SimulatedOutcome.Failure
        : SimulatedOutcome.Success;
    var taipeiNow = EcpaySimulatorCore.ToTaipei(DateTimeOffset.UtcNow);
    var tradeNo = EcpaySimulatorCore.CreateTradeNo(taipeiNow, Random.Shared.Next(0, 10));
    var notification = EcpaySimulatorCore.BuildPaymentNotification(
        posted, outcome, tradeNo, taipeiNow, hashKey, hashIv);

    var returnUrl = posted.GetValueOrDefault("ReturnURL", string.Empty);
    string statusLine;
    string body;
    try
    {
        var httpClient = httpClientFactory.CreateClient("Webhook");
        using var response = await httpClient.PostAsync(
            returnUrl,
            new FormUrlEncodedContent(notification),
            cancellationToken);
        body = await response.Content.ReadAsStringAsync(cancellationToken);
        statusLine = $"{(int)response.StatusCode} {response.StatusCode}";
    }
    catch (Exception exception)
    {
        // 這一頁的用途就是「看得見 webhook 發生了什麼」，所以連線失敗也要顯示在頁面上，
        // 不能只留在 log 裡——不然症狀又變成「按了沒反應」。
        statusLine = "（呼叫失敗）";
        body = exception.Message;
    }

    log.LogInformation("webhook POST {ReturnUrl} → {Status}：{Body}", returnUrl, statusLine, body);
    return Html(RenderResult(posted, notification, outcome, statusLine, body));
});

app.MapPost("/CreditDetail/DoAction", async (HttpContext context) =>
{
    var fields = await ReadFormAsync(context);
    var body = EcpaySimulatorCore.BuildDoActionResponse(fields, hashKey, hashIv);
    log.LogInformation("DoAction → {Body}", body);
    return Results.Text(body, "application/x-www-form-urlencoded", Encoding.UTF8);
});

app.Run();

static string Required(IConfiguration configuration, string key)
{
    var value = configuration[key];
    return !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new InvalidOperationException(
            $"缺少設定 '{key}'。綠界模擬器讀的鍵跟 Host 同名，" +
            "用 ops\\start-dev-ecpay-simulator.ps1 啟動就會投遞。");
}

static async Task<Dictionary<string, string>> ReadFormAsync(HttpContext context)
{
    var form = await context.Request.ReadFormAsync(context.RequestAborted);
    return form.ToDictionary(pair => pair.Key, pair => pair.Value.ToString(), StringComparer.Ordinal);
}

static IResult Html(string html, int statusCode = StatusCodes.Status200OK)
    => Results.Content(html, "text/html", Encoding.UTF8, statusCode);

static string Page(string title, string bodyHtml)
    => $$"""
        <!DOCTYPE html>
        <html lang="zh-Hant">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{E(title)}}｜綠界模擬器（dev）</title>
        <style>
        body{font-family:system-ui,"Noto Sans TC",sans-serif;margin:0;background:#f4f4f5;color:#18181b}
        main{max-width:34rem;margin:0 auto;padding:1.5rem}
        .banner{background:#7f1d1d;color:#fff;padding:.75rem 1.5rem;font-weight:700}
        .card{background:#fff;border-radius:.75rem;padding:1.25rem;margin-bottom:1rem;
        box-shadow:0 1px 3px rgba(0,0,0,.1)}
        dt{font-size:.8rem;color:#71717a;margin-top:.6rem}
        dd{margin:.15rem 0 0;word-break:break-all;font-family:ui-monospace,monospace;font-size:.9rem}
        button{font-size:1rem;padding:.7rem 1.2rem;border-radius:.5rem;border:0;cursor:pointer;
        width:100%;margin-top:.6rem}
        .ok{background:#15803d;color:#fff}
        .ng{background:#b91c1c;color:#fff}
        a.back{display:block;text-align:center;padding:.7rem 1.2rem;border-radius:.5rem;
        background:#1d4ed8;color:#fff;text-decoration:none;margin-top:.6rem}
        pre{background:#18181b;color:#e4e4e7;padding:.75rem;border-radius:.5rem;overflow:auto;
        font-size:.85rem;white-space:pre-wrap}
        .warn{color:#b91c1c;font-size:1.05rem;font-weight:700;line-height:1.6}
        </style>
        </head>
        <body>
        <div class="banner">綠界模擬器（開發環境專用，不是真的金流）</div>
        <main>{{bodyHtml}}</main>
        </body>
        </html>
        """;

static string RenderError(string rtnCode, string rtnMsg)
    => Page("錯誤", $"""
        <div class="card">
        <h1>{E(rtnCode)} {E(rtnMsg)}</h1>
        <p>模擬器拒絕了這張表單。真綠界在同樣的情況也會拒絕。</p>
        </div>
        """);

static string RenderCheckout(IReadOnlyDictionary<string, string> fields)
{
    var hidden = new StringBuilder();
    foreach (var pair in fields)
    {
        hidden.Append(
            CultureInfo.InvariantCulture,
            $"""<input type="hidden" name="{E(pair.Key)}" value="{E(pair.Value)}">""");
    }

    var clientBackUrl = fields.GetValueOrDefault("ClientBackURL", string.Empty);
    var clientBackRow = string.IsNullOrWhiteSpace(clientBackUrl)
        ? """<dt>ClientBackURL</dt><dd class="warn">沒有帶 ClientBackURL——付完款會沒有路回商店（#33）。</dd>"""
        : $"<dt>ClientBackURL</dt><dd>{E(clientBackUrl)}</dd>";

    return Page("模擬付款", $"""
        <div class="card">
        <h1>模擬付款</h1>
        <dl>
        <dt>MerchantTradeNo</dt><dd>{E(fields.GetValueOrDefault("MerchantTradeNo", string.Empty))}</dd>
        <dt>TotalAmount</dt><dd>NT$ {E(fields.GetValueOrDefault("TotalAmount", string.Empty))}</dd>
        <dt>ItemName</dt><dd>{E(fields.GetValueOrDefault("ItemName", string.Empty))}</dd>
        <dt>ReturnURL</dt><dd>{E(fields.GetValueOrDefault("ReturnURL", string.Empty))}</dd>
        {clientBackRow}
        </dl>
        </div>
        <form method="post" action="/Cashier/AioCheckOut/V5/decide" class="card">
        {hidden}
        <button class="ok" type="submit" name="Outcome" value="Success">模擬付款成功</button>
        <button class="ng" type="submit" name="Outcome" value="Failure">模擬付款失敗</button>
        </form>
        """);
}

static string RenderResult(
    IReadOnlyDictionary<string, string> checkoutForm,
    IReadOnlyDictionary<string, string> notification,
    SimulatedOutcome outcome,
    string statusLine,
    string body)
{
    var clientBackUrl = checkoutForm.GetValueOrDefault("ClientBackURL", string.Empty);
    var backHtml = string.IsNullOrWhiteSpace(clientBackUrl)
        ? """
            <p class="warn">這張結帳表單沒有帶 ClientBackURL，所以這裡沒有「返回商店」可以按。<br>
            真綠界的完成頁一樣會這樣——客人付完款就停在這裡回不去（#33）。</p>
            """
        : $"""<a class="back" href="{E(clientBackUrl)}">返回商店</a>""";

    return Page("付款結果", $"""
        <div class="card">
        <h1>{(outcome == SimulatedOutcome.Success ? "模擬付款成功" : "模擬付款失敗")}</h1>
        <dl>
        <dt>MerchantTradeNo</dt><dd>{E(notification.GetValueOrDefault("MerchantTradeNo", string.Empty))}</dd>
        <dt>TradeNo</dt><dd>{E(notification.GetValueOrDefault("TradeNo", string.Empty))}</dd>
        <dt>TradeAmt</dt><dd>NT$ {E(notification.GetValueOrDefault("TradeAmt", string.Empty))}</dd>
        <dt>RtnCode / RtnMsg</dt>
        <dd>{E(notification.GetValueOrDefault("RtnCode", string.Empty))}
        {E(notification.GetValueOrDefault("RtnMsg", string.Empty))}</dd>
        </dl>
        </div>
        <div class="card">
        <h2>付款結果通知（webhook）</h2>
        <dl>
        <dt>ReturnURL</dt><dd>{E(checkoutForm.GetValueOrDefault("ReturnURL", string.Empty))}</dd>
        <dt>回應狀態</dt><dd>{E(statusLine)}</dd>
        <dt>回應內容（預期 1|OK）</dt><dd><pre>{E(body)}</pre></dd>
        </dl>
        </div>
        <div class="card">{backHtml}</div>
        """);
}

static string E(string value) => WebUtility.HtmlEncode(value);
