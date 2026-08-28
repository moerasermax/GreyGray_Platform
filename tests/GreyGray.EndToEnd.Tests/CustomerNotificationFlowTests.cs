using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace GreyGray.EndToEnd.Tests;

/// <summary>從真正 Host process 打 API，再讓真正 Worker 消費 PostgreSQL outbox。</summary>
public sealed class CustomerNotificationFlowTests : IAsyncLifetime
{
    private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
    private const string ParentSpanId = "00f067aa0ba902b7";
    private const string RolePassword = "greygray-e2e-only";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public async ValueTask InitializeAsync() =>
        await _postgres.StartAsync(TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    [Fact(DisplayName = "API → Identity/outbox 同交易 → Worker → Notification 去重且 trace 連續")]
    public async Task Customer_registration_reaches_notification_once_with_one_trace()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var repoRoot = FindRepositoryRoot();
        await ApplyMigrationsAsync(repoRoot, cancellationToken);
        await ConfigureRolePasswordsAsync(cancellationToken);
        (await ScalarAsync<int>("""
            SELECT count(*)::int
            FROM pg_tables
            WHERE tableowner = 'greygray_owner'
              AND (schemaname, tablename) IN (
                  ('iam', 'customer'),
                  ('notify', 'notification'));
            """, cancellationToken)).ShouldBe(2);

        var storefrontPort = GetAvailablePort();
        await using var storefront = StartHost(
            repoRoot,
            "GreyGray.Api.Storefront",
            new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{storefrontPort}",
                ["ConnectionStrings__GreyGray_iam"] = ConnectionStringFor("greygray_iam"),
            });

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{storefrontPort}"),
            Timeout = TimeSpan.FromSeconds(5),
        };
        await WaitForHttpAsync(httpClient, "/health", storefront, cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/customers")
        {
            Content = JsonContent.Create(new { displayName = "M0 測試客戶" }),
        };
        request.Headers.TryAddWithoutValidation(
            "traceparent",
            $"00-{TraceId}-{ParentSpanId}-01");
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, storefront.Diagnostics);
        response.Headers.GetValues("traceparent").Single().ShouldContain(TraceId);

        using var payload = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        var customerId = payload.RootElement.GetProperty("id").GetString();
        customerId.ShouldNotBeNullOrWhiteSpace();

        (await ScalarAsync<int>("""
            SELECT count(*)::int
            FROM iam.customer;
            """, cancellationToken)).ShouldBe(1);
        (await ScalarAsync<string>("""
            SELECT event_type || ':' || correlation_id || ':' || (causation_id IS NOT NULL)::text
            FROM platform.outbox_message;
            """, cancellationToken)).ShouldBe($"iam.CustomerRegistered.v1:{TraceId}:true");
        (await ScalarAsync<bool>("""
            SELECT EXISTS (
                SELECT 1
                FROM iam.customer customer
                JOIN platform.outbox_message message
                  ON message.aggregate_id = replace(customer.id::text, '-', '')
                WHERE message.aggregate_type = 'Customer');
            """, cancellationToken)).ShouldBeTrue(
                "Customer 與 CustomerRegistered outbox 必須一起出現。");

        await using (var worker = StartWorker(repoRoot))
        {
            await WaitUntilAsync(
                async () => await ScalarAsync<bool>("""
                    SELECT
                        (SELECT count(*) = 1 FROM notify.notification)
                        AND (SELECT count(*) = 1 FROM platform.processed_message)
                        AND (SELECT bool_and(processed_at IS NOT NULL) FROM platform.outbox_message);
                    """, cancellationToken),
                worker,
                cancellationToken);

            (await ScalarAsync<string>("""
                SELECT template_code || ':' || trace_id || ':' || (handler_span_id IS NOT NULL)::text
                FROM notify.notification;
                """, cancellationToken)).ShouldBe($"customer.registered:{TraceId}:true");
            (await ScalarAsync<int>("""
                SELECT count(*)::int
                FROM platform.processed_message;
                """, cancellationToken)).ShouldBe(1);
            (await ScalarAsync<bool>("""
                SELECT processed_at IS NOT NULL
                FROM platform.outbox_message;
                """, cancellationToken)).ShouldBeTrue();
        }

        // 重開 Worker 並人為重送同一則 at-least-once 訊息；不可再寫第二筆通知。
        await ExecuteAdminSqlAsync("""
            UPDATE platform.outbox_message
            SET processed_at = NULL,
                next_attempt_at = now();
            """, cancellationToken);

        await using (var restartedWorker = StartWorker(repoRoot))
        {
            await WaitUntilAsync(
                async () => await ScalarAsync<bool>("""
                    SELECT processed_at IS NOT NULL
                    FROM platform.outbox_message;
                    """, cancellationToken),
                restartedWorker,
                cancellationToken);
        }

        (await ScalarAsync<int>("""
            SELECT count(*)::int
            FROM notify.notification;
            """, cancellationToken)).ShouldBe(1);
        (await ScalarAsync<int>("""
            SELECT count(*)::int
            FROM platform.processed_message;
            """, cancellationToken)).ShouldBe(1);

        // API Host 本身也必須能在停止後以新 PID 重啟並回應 health。
        await storefront.StopAsync();
        await using var restartedStorefront = StartHost(
            repoRoot,
            "GreyGray.Api.Storefront",
            new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{storefrontPort}",
                ["ConnectionStrings__GreyGray_iam"] = ConnectionStringFor("greygray_iam"),
            });
        using var restartedClient = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{storefrontPort}"),
            Timeout = TimeSpan.FromSeconds(5),
        };
        await WaitForHttpAsync(restartedClient, "/health", restartedStorefront, cancellationToken);

        var adminPort = GetAvailablePort();
        await using (var admin = StartApiHost(repoRoot, "GreyGray.Api.Admin", adminPort))
        {
            using var adminClient = CreateHttpClient(adminPort);
            await WaitForHttpAsync(adminClient, "/health", admin, cancellationToken);
        }

        await using var restartedAdmin = StartApiHost(
            repoRoot,
            "GreyGray.Api.Admin",
            adminPort);
        using var restartedAdminClient = CreateHttpClient(adminPort);
        await WaitForHttpAsync(
            restartedAdminClient,
            "/health",
            restartedAdmin,
            cancellationToken);

        // M0 test hook 絕不可意外暴露到 Production，也不可被當成 frozen public API。
        var productionPort = GetAvailablePort();
        await using var productionStorefront = StartHost(
            repoRoot,
            "GreyGray.Api.Storefront",
            new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Production",
                ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{productionPort}",
            });
        using var productionClient = CreateHttpClient(productionPort);
        await WaitForHttpAsync(
            productionClient,
            "/health",
            productionStorefront,
            cancellationToken);
        using var productionResponse = await productionClient.PostAsJsonAsync(
            "/v1/customers",
            new { displayName = "不可建立" },
            cancellationToken);
        productionResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // 0004 末段 owner 斷言必須真的能失敗，不只是檔案裡有一段 DO block。
        await ExecuteAdminSqlAsync(
            "CREATE TABLE iam.owner_assertion_probe (id int);",
            cancellationToken);
        var ownerException = await Should.ThrowAsync<PostgresException>(() =>
            ExecuteMigrationAsync(
                Path.Combine(repoRoot, "db", "migrations", "0004_hello_world.sql"),
                cancellationToken));
        ownerException.SqlState.ShouldBe("P0001");
        await ExecuteAdminSqlAsync(
            "DROP TABLE iam.owner_assertion_probe;",
            cancellationToken);
    }

    private HostProcess StartWorker(string repoRoot) =>
        StartHost(
            repoRoot,
            "GreyGray.Worker",
            new Dictionary<string, string>
            {
                ["DOTNET_ENVIRONMENT"] = "Development",
                ["ConnectionStrings__GreyGray_platform"] = ConnectionStringFor("greygray_platform"),
                ["ConnectionStrings__GreyGray_notify"] = ConnectionStringFor("greygray_notify"),
            });

    private async Task ApplyMigrationsAsync(string repoRoot, CancellationToken cancellationToken)
    {
        var migrationRoot = Path.Combine(repoRoot, "db", "migrations");
        foreach (var fileName in new[]
                 {
                     "0001_schemas_and_roles.sql",
                     "0002_platform.sql",
                     "0003_channel_seams.sql",
                     "0004_hello_world.sql",
                     "0004_hello_world.sql",
                 })
        {
            await ExecuteMigrationAsync(
                Path.Combine(migrationRoot, fileName),
                cancellationToken);
        }
    }

    private Task ExecuteMigrationAsync(string path, CancellationToken cancellationToken)
    {
        var sql = string.Join(
            Environment.NewLine,
            File.ReadLines(path).Where(line => !line.TrimStart().StartsWith('\\')));
        return ExecuteAdminSqlAsync(sql, cancellationToken);
    }

    private Task ConfigureRolePasswordsAsync(CancellationToken cancellationToken) =>
        ExecuteAdminSqlAsync($"""
            ALTER ROLE greygray_iam PASSWORD '{RolePassword}';
            ALTER ROLE greygray_notify PASSWORD '{RolePassword}';
            ALTER ROLE greygray_platform PASSWORD '{RolePassword}';
            """, cancellationToken);

    private string ConnectionStringFor(string role)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = role,
            Password = RolePassword,
            Pooling = false,
            IncludeErrorDetail = true,
        };
        return connectionString.ConnectionString;
    }

    private async Task ExecuteAdminSqlAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection)
        {
            CommandTimeout = 30,
        };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<T> ScalarAsync<T>(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return (T)(value ?? throw new InvalidOperationException("Expected a scalar value."));
    }

    private static async Task WaitForHttpAsync(
        HttpClient client,
        string path,
        HostProcess process,
        CancellationToken cancellationToken)
    {
        await WaitUntilAsync(
            async () =>
            {
                try
                {
                    using var response = await client.GetAsync(path, cancellationToken);
                    return response.IsSuccessStatusCode;
                }
                catch (HttpRequestException)
                {
                    return false;
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
            },
            process,
            cancellationToken);
    }

    private static async Task WaitUntilAsync(
        Func<Task<bool>> condition,
        HostProcess process,
        CancellationToken cancellationToken)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(30))
        {
            process.ThrowIfExited();
            if (await condition())
            {
                return;
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException("Host did not reach the expected state.\n" + process.Diagnostics);
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

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static HostProcess StartApiHost(string repoRoot, string projectName, int port) =>
        StartHost(
            repoRoot,
            projectName,
            new Dictionary<string, string>
            {
                ["ASPNETCORE_ENVIRONMENT"] = "Development",
                ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}",
            });

    private static HttpClient CreateHttpClient(int port) => new()
    {
        BaseAddress = new Uri($"http://127.0.0.1:{port}"),
        Timeout = TimeSpan.FromSeconds(5),
    };

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "GreyGray.slnx")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new DirectoryNotFoundException("Cannot locate GreyGray.slnx from test output directory.");
    }

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

        public async Task StopAsync()
        {
            if (_process.HasExited)
            {
                return;
            }

            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
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
