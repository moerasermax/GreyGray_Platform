using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Shouldly;
using Xunit;

namespace GreyGray.EndToEnd.Tests;

/// <summary>
/// 壞掉的 request body 一定是 <c>400</c> ＋ <c>application/problem+json</c>，兩個環境一致。
/// </summary>
/// <remarks>
/// <para>
/// 為什麼一定要起<b>真的 Host 行程</b>：這條路整段都發生在<b>進 endpoint lambda 之前</b>。
/// minimal API 先綁定 request body 再進 lambda，直接呼叫 <c>CompleteCheckoutAsync</c> 的
/// 單元測試永遠測不到它——#37 就是這樣漏掉的（契約寫 <c>shippingPolicy</c> 必填、前端送
/// <c>null</c>，綁定期丟 <c>JsonException</c> → 500，客人連 401 都拿不到，而所有測試都是綠的）。
/// </para>
/// <para>
/// 修這一包之前的行為：Development 被 <c>UseExceptionHandler</c> 當成一般例外 → <b>500</b>；
/// 非 Development 的 <c>RouteHandlerOptions.ThrowOnBadRequest</c> 預設 false → <b>空 body 的 400</b>。
/// <c>docs/05-API契約.md</c> 說錯誤一律是 problem+json、可預期的失敗不會是 500，兩個都不合格，
/// 所以<b>兩個環境各跑一次</b>。
/// </para>
/// <para>
/// 這一組不需要資料庫：綁定失敗發生在任何 DB 存取之前，連線字串給假的就好
/// （<c>Identity:DataProtectionKey</c> 要給合法的 32-byte base64，否則 DI 解析期就先炸）。
/// </para>
/// </remarks>
public sealed class MalformedRequestBodyTests
{
    /// <summary>合法的 32-byte base64 金鑰。不是機密，只是讓 Identity 模組的 DI 解析得過。</summary>
    private const string DataProtectionKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    [Theory(DisplayName = "壞掉的 request body → 400 problem+json platform.malformed-request")]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Malformed_bodies_are_rejected_with_problem_details(string environment)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var port = GetAvailablePort();
        await using var storefront = StartStorefront(environment, port);
        using var client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            Timeout = TimeSpan.FromSeconds(10),
        };
        await WaitForHealthAsync(client, storefront, cancellationToken);

        // ① enum 值不合法：JSON 本身合法，綁不進 CompleteCheckoutInput。
        await AssertMalformedAsync(
            client,
            storefront,
            """{"deliveryMethod":"SelfPickup","shippingPolicy":"NotAnEnum"}""",
            cancellationToken);

        // ② 根本不是 JSON。
        await AssertMalformedAsync(
            client,
            storefront,
            "{ this is not json",
            cancellationToken);
    }

    [Theory(DisplayName = "#37 shippingPolicy 送 null 不再是 500——訪客拿到的是 401")]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task Null_shipping_policy_reaches_the_authentication_check(string environment)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var port = GetAvailablePort();
        await using var storefront = StartStorefront(environment, port);
        using var client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"),
            Timeout = TimeSpan.FromSeconds(10),
        };
        await WaitForHealthAsync(client, storefront, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/cart/checkout")
        {
            Content = new StringContent(
                """{"deliveryMethod":"SelfPickup","shippingPolicy":null}""",
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "e2e-null-policy");
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        // 使用者 2026-09-02 撞到的就是這裡：500 ＋ JsonException，而且比登入檢查還早。
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, $"{storefront.Diagnostics}\n{body}");
        JsonDocument.Parse(body).RootElement.GetProperty("code").GetString()
            .ShouldBe("auth.unauthorized");
    }

    private static async Task AssertMalformedAsync(
        HttpClient client,
        HostProcess storefront,
        string payload,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/cart/checkout")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "e2e-malformed");
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var context = $"payload={payload}\n{storefront.Diagnostics}\n{body}";

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, context);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json", context);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("code").GetString().ShouldBe("platform.malformed-request");
        problem.RootElement.GetProperty("status").GetInt32().ShouldBe(400);

        // 不吐內部型別名（docs/05 §10 第 3 條）。框架原本的訊息長這樣：
        // 「Failed to read parameter "CompleteCheckoutInput input" from the request body as JSON.」
        body.ShouldNotContain("CompleteCheckoutInput", Case.Insensitive, context);
        body.ShouldNotContain("GreyGray.Api.Storefront", Case.Insensitive, context);
        body.ShouldNotContain("System.Text.Json", Case.Insensitive, context);
    }

    /// <summary>
    /// 13 個 module schema ＋ valkey，比照 <c>ops/start-dev-hosts.ps1</c> 那份清單。
    /// </summary>
    /// <remarks>
    /// 這裡刻意<b>不</b>開資料庫：這一組要驗的兩件事（綁定失敗、以及綁定成功之後的登入檢查）
    /// 都發生在任何 I/O 之前——<c>BffHttp.GetSessionAsync</c> 沒看到 <c>gg_session</c> cookie
    /// 就直接回 null。設定值只要「存在且格式合法」讓 DI 建得起來就夠，指向哪裡不重要
    /// （port 1 保證接不上，萬一哪天真的連下去了，測試會炸而不是靜靜地變成整合測試）。
    /// </remarks>
    private static Dictionary<string, string> HostEnvironment(string environment, int port)
    {
        var values = new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = environment,
            ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}",
            ["Identity__DataProtectionKey"] = DataProtectionKey,
            ["ConnectionStrings__GreyGray_valkey"] = "127.0.0.1:1",

            // Payment 的三個必要設定同樣只是「要存在」——不設 CheckoutUrl，
            // 端點就是綠界正式網域，網域守衛（ADR-029）照樣是開著的。
            ["Payment__ECPay__MerchantId"] = "DEVFAKE0000",
            ["Payment__ECPay__HashKey"] = "DEVFAKEHASHKEY01",
            ["Payment__ECPay__HashIV"] = "DEVFAKEHASHIV001",
            ["Payment__ECPay__CheckoutUrl"] = "https://payment-stage.ecpay.com.tw/Cashier/AioCheckOut/V5",
            ["Payment__ECPay__CreditDetailUrl"] = "https://payment-stage.ecpay.com.tw/CreditDetail/DoAction",
        };
        string[] schemas =
        [
            "iam", "catalog", "campaign", "checkout", "fulfillment", "inventory",
            "ledger", "notify", "ordering", "payment", "pricing", "procurement", "platform",
            "customer_service",
        ];
        foreach (var schema in schemas)
        {
            values[$"ConnectionStrings__GreyGray_{schema}"] =
                $"Host=127.0.0.1;Port=1;Database=postgres;Username=greygray_{schema};Password=unused";
        }

        return values;
    }

    private static HostProcess StartStorefront(string environment, int port) => StartHost(
        FindRepositoryRoot(),
        "GreyGray.Api.Storefront",
        HostEnvironment(environment, port));

    private static async Task WaitForHealthAsync(
        HttpClient client,
        HostProcess process,
        CancellationToken cancellationToken)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(30))
        {
            process.ThrowIfExited();
            try
            {
                using var response = await client.GetAsync("/health", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // 還沒開始聽，再等。
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // 逾時，再等。
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException("Storefront /health 沒有回應。\n" + process.Diagnostics);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "GreyGray.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Cannot find the repository root.");
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static HostProcess StartHost(
        string repoRoot,
        string projectName,
        IReadOnlyDictionary<string, string> environment)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
            ?? throw new DirectoryNotFoundException("Cannot determine test build configuration.");
        var executableName = OperatingSystem.IsWindows() ? $"{projectName}.exe" : projectName;
        var executable = Path.Combine(
            repoRoot,
            "src",
            "Hosts",
            projectName,
            "bin",
            configuration,
            "net10.0",
            executableName);
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("Host executable was not built.", executable);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var pair in environment)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        return HostProcess.Start(startInfo);
    }

    /// <summary>
    /// 跟 <c>CustomerNotificationFlowTests</c> 裡那個同形狀——那一個是 private nested，
    /// 這裡不動它（那個檔案的每一行都在講另一件事），寧可留一份小的。
    /// </summary>
    private sealed class HostProcess : IAsyncDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _output = new();
        private readonly object _sync = new();

        private HostProcess(Process process)
        {
            _process = process;
            _process.OutputDataReceived += (_, args) => Append(args.Data);
            _process.ErrorDataReceived += (_, args) => Append(args.Data);
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public string Diagnostics
        {
            get
            {
                lock (_sync)
                {
                    return _output.ToString();
                }
            }
        }

        public static HostProcess Start(ProcessStartInfo startInfo)
        {
            var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new InvalidOperationException($"Could not start {startInfo.FileName}.");
            }

            return new HostProcess(process);
        }

        public void ThrowIfExited()
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    $"Host exited with code {_process.ExitCode}.\n{Diagnostics}");
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }

            _process.Dispose();
        }

        private void Append(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (_sync)
            {
                _output.AppendLine(line);
            }
        }
    }
}
