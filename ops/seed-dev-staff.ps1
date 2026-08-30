<#
    對 D:\GreyGray 開發環境跑員工 bootstrap 工具，種下最小種子資料
    （FE-12 §5 四角色登入需要的四個帳號）。

    先跑過 ops\install-dev-environment.ps1（要有 secrets\module-role.password
    與 secrets\identity-dataprotection.key），且 dotnet build 過至少一次。
    直接執行 bootstrap 工具，不動任何 Host 行程，可以在三個 Host 開著的時候跑，
    也可以在它們沒開的時候跑（bootstrap 工具自己開一條獨立的 DB 連線）。
#>
[CmdletBinding()]
param(
    [string]$InstallRoot = 'D:\GreyGray',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [int]$PostgreSqlPort = 5432,
    [string]$SeedFile = (Join-Path $PSScriptRoot 'seed\staff-accounts.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

$secretsDir = Join-Path $InstallRoot 'secrets'
$modulePasswordFile = Join-Path $secretsDir 'module-role.password'
if (-not (Test-Path -LiteralPath $modulePasswordFile -PathType Leaf)) {
    throw "找不到模組密碼檔：$modulePasswordFile。先執行 ops\install-dev-environment.ps1。"
}
$modulePassword = (Get-Content -LiteralPath $modulePasswordFile -Raw).Trim()

$dataProtectionKeyFile = Join-Path $secretsDir 'identity-dataprotection.key'
if (-not (Test-Path -LiteralPath $dataProtectionKeyFile -PathType Leaf)) {
    throw "找不到 Identity 個資保護金鑰：$dataProtectionKeyFile。先執行 ops\install-dev-environment.ps1。"
}
$dataProtectionKey = (Get-Content -LiteralPath $dataProtectionKeyFile -Raw).Trim()

# 只需要 iam 這一個 schema——這支工具只參考 Identity.Infra（見 GreyGray.Tools.StaffBootstrap.csproj）。
$connectionString = "Host=127.0.0.1;Port=$PostgreSqlPort;Database=postgres;Username=greygray_iam;Password=$modulePassword;Include Error Detail=true"

$previous = @{
    'ConnectionStrings__GreyGray_iam' = [Environment]::GetEnvironmentVariable('ConnectionStrings__GreyGray_iam', 'Process')
    'Identity__DataProtectionKey'     = [Environment]::GetEnvironmentVariable('Identity__DataProtectionKey', 'Process')
}
[Environment]::SetEnvironmentVariable('ConnectionStrings__GreyGray_iam', $connectionString, 'Process')
[Environment]::SetEnvironmentVariable('Identity__DataProtectionKey', $dataProtectionKey, 'Process')

try {
    & dotnet run --project (Join-Path $repo 'src\Tools\GreyGray.Tools.StaffBootstrap') -c $Configuration -- `
        --seed $SeedFile --secrets-dir $secretsDir
    $toolExitCode = $LASTEXITCODE
}
finally {
    foreach ($key in $previous.Keys) {
        [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process')
    }
}

if ($toolExitCode -ne 0) {
    throw "GreyGray.Tools.StaffBootstrap 以非 0 狀態結束（exit code $toolExitCode）。"
}

exit 0
