using System.Security.Cryptography;
using System.Text.Json;
using GreyGray.Modules.Identity.Contracts;
using GreyGray.Modules.Identity.Infra;
using GreyGray.Platform.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// 員工帳號 bootstrap 工具——獨立 console 工具，不是 HTTP 端點。
// 開發環境與正式機第一個 Owner 帳號都靠這支工具建立（見 docs/23 §0 §7）。
// 種子清單本身不含密碼；密碼由本工具產生並寫進 <secrets-dir>\staff-credentials.json。

var seedPath = GetRequiredArg(args, "--seed");
var secretsDir = GetRequiredArg(args, "--secrets-dir");

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddGreyGrayRuntimeContext();
builder.Services.AddIdentityModule(builder.Configuration);
var host = builder.Build();

var seedEntries = JsonSerializer.Deserialize<List<SeedEntry>>(
    File.ReadAllText(seedPath),
    new JsonSerializerOptions(JsonSerializerDefaults.Web));

if (seedEntries is null || seedEntries.Count == 0)
{
    Console.Error.WriteLine($"種子清單是空的或格式不正確：{seedPath}");
    return 1;
}

var newlyCreated = new List<CredentialRecord>();
var hadError = false;

foreach (var entry in seedEntries)
{
    StaffRole role;
    try
    {
        role = Enum.Parse<StaffRole>(entry.Role, ignoreCase: true);
    }
    catch (Exception exception) when (exception is ArgumentException or OverflowException)
    {
        Console.WriteLine($"錯誤：{entry.Email}（{entry.Role} 不是有效的 StaffRole）");
        hadError = true;
        continue;
    }

    var password = GenerateRandomPassword();

    await using var scope = host.Services.CreateAsyncScope();
    var accounts = scope.ServiceProvider.GetRequiredService<IStaffAccounts>();
    var result = await accounts.CreateAsync(
        new CreateStaffInput(entry.DisplayName, entry.Email, password, role),
        CancellationToken.None);

    if (result.IsSuccess)
    {
        newlyCreated.Add(new CredentialRecord(entry.Email, entry.DisplayName, entry.Role, password));
        Console.WriteLine($"新建立：{entry.Email}（{entry.Role}）");
    }
    else if (result.Error.Code == "identity.staff-email-conflict")
    {
        Console.WriteLine($"已存在，略過：{entry.Email}（{entry.Role}）");
    }
    else
    {
        Console.WriteLine($"錯誤：{entry.Email}（{result.Error.Code} {result.Error.Message}）");
        hadError = true;
    }
}

if (newlyCreated.Count > 0)
{
    Directory.CreateDirectory(secretsDir);
    var credentialsPath = Path.Combine(secretsDir, "staff-credentials.json");
    var merged = new Dictionary<string, CredentialRecord>(StringComparer.OrdinalIgnoreCase);
    if (File.Exists(credentialsPath))
    {
        var existing = JsonSerializer.Deserialize<List<CredentialRecord>>(
            File.ReadAllText(credentialsPath),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (existing is not null)
        {
            foreach (var record in existing)
            {
                merged[record.Email] = record;
            }
        }
    }

    foreach (var record in newlyCreated)
    {
        merged[record.Email] = record;
    }

    File.WriteAllText(
        credentialsPath,
        JsonSerializer.Serialize(
            merged.Values.ToList(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
}

Console.WriteLine($"完成：{newlyCreated.Count} 筆新建立，{seedEntries.Count - newlyCreated.Count} 筆略過或錯誤。");
return hadError ? 1 : 0;

static string GetRequiredArg(string[] arguments, string name)
{
    for (var i = 0; i < arguments.Length - 1; i++)
    {
        if (string.Equals(arguments[i], name, StringComparison.Ordinal))
        {
            return arguments[i + 1];
        }
    }

    throw new ArgumentException($"缺少必填參數 {name}。");
}

static string GenerateRandomPassword()
{
    var bytes = new byte[24];
    RandomNumberGenerator.Fill(bytes);
    return Convert.ToBase64String(bytes);
}

internal sealed record SeedEntry(string DisplayName, string Email, string Role);

internal sealed record CredentialRecord(string Email, string DisplayName, string Role, string Password);
