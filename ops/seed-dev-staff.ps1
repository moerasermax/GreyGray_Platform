<#
    對 D:\GreyGray 開發環境跑員工 bootstrap 工具，種下最小種子資料
    （FE-12 §5 四角色登入需要的四個帳號）。

    先跑過 ops\install-dev-environment.ps1（要有 secrets\module-role.password
    與 secrets\identity-dataprotection.key），且 dotnet build 過至少一次。
    直接執行 bootstrap 工具，不動任何 Host 行程，可以在三個 Host 開著的時候跑，
    也可以在它們沒開的時候跑（bootstrap 工具自己開一條獨立的 DB 連線）。

    -CredentialsDir 可指定新建帳號的密碼檔輸出目錄；已存在的 email 不會再輸出密碼。
    建議指定還不存在的子目錄，讓腳本建立目錄並收緊 ACL。加入此參數後，
    縮寫 -c 會在 Configuration 與 CredentialsDir 之間產生歧義，請寫全名 -Configuration。
#>
[CmdletBinding()]
param(
    [string]$InstallRoot = 'D:\GreyGray',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [int]$PostgreSqlPort = 5432,
    [string]$SeedFile = (Join-Path $PSScriptRoot 'seed\staff-accounts.json'),
    [string]$CredentialsDir = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

$secretsDir = Join-Path $InstallRoot 'secrets'
$credentialsOutputDir = $secretsDir
$useCustomCredentialsDir = -not [string]::IsNullOrWhiteSpace($CredentialsDir)
if ($useCustomCredentialsDir) {
    $credentialsOutputDir = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($CredentialsDir)
    $credentialsOutputDir = [IO.Path]::GetFullPath($credentialsOutputDir)
    $pathRoot = [IO.Path]::GetPathRoot($credentialsOutputDir)
    if (-not [string]::Equals($credentialsOutputDir, $pathRoot, [StringComparison]::OrdinalIgnoreCase)) {
        $credentialsOutputDir = $credentialsOutputDir.TrimEnd([char[]]@(
                [IO.Path]::DirectorySeparatorChar,
                [IO.Path]::AltDirectorySeparatorChar))
    }

    $probePath = $credentialsOutputDir
    while ($null -ne $probePath) {
        if ((Test-Path -LiteralPath $probePath -PathType Container) -and
            (Test-Path -LiteralPath (Join-Path $probePath '.git'))) {
            throw "密碼檔不能放在 git repo 裡：$credentialsOutputDir"
        }

        $parent = [IO.Directory]::GetParent($probePath)
        if ($null -eq $parent -or
            [string]::Equals($parent.FullName, $probePath, [StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        $probePath = $parent.FullName
    }
}

. (Join-Path $PSScriptRoot 'lib\Secrets.ps1')

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

if ($useCustomCredentialsDir -and -not (Test-Path -LiteralPath $credentialsOutputDir -PathType Container)) {
    if (Test-Path -LiteralPath $credentialsOutputDir) {
        throw "員工密碼輸出路徑已存在且不是目錄：$credentialsOutputDir"
    }
    New-Item -ItemType Directory -Path $credentialsOutputDir -Force | Out-Null
    Protect-SecretDirectory -Path $credentialsOutputDir
}

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
        --seed $SeedFile --secrets-dir $credentialsOutputDir
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
