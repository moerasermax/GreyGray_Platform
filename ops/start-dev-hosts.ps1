<#
    啟動三個 Host（Storefront／Admin／Worker）打本機開發環境
    （先跑 install-dev-environment.ps1，PostgreSQL／Garnet／migrations 都要先備妥）。

    直接執行 bin\<Configuration>\net10.0\*.exe——不跑 dotnet build。
    BE-22 不寫 C#、不跑 ops/test.ps1，這支腳本也一樣：用既有已建置的產物，
    避免跟同時可能在跑 dotnet build 的其他包搶 obj/、bin/（docs/19 §2）。
    找不到就直接說「先建置」，不會自己偷跑 dotnet build。

    連線字串走 ConnectionStrings__<Key> 環境變數（跟 ops/check-openapi.ps1、
    ops/deploy.ps1 同一個慣例），密碼從 install-dev-environment.ps1 落地的
    secrets 檔讀，不進命令列、不印到 console。
#>
[CmdletBinding()]
param(
    [string]$InstallRoot = 'D:\GreyGray',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [int]$StorefrontPort = 5000,
    [int]$AdminPort = 5001,
    [int]$PostgreSqlPort = 5432,
    [int]$GarnetPort = 6379,
    [int]$StartupTimeoutSeconds = 45
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

$stateDir = Join-Path $InstallRoot 'state'
$logDir = Join-Path $InstallRoot 'logs'
$secretsDir = Join-Path $InstallRoot 'secrets'
$modulePasswordFile = Join-Path $secretsDir 'module-role.password'
if (-not (Test-Path -LiteralPath $modulePasswordFile -PathType Leaf)) {
    throw "找不到模組密碼檔：$modulePasswordFile。先執行 ops\install-dev-environment.ps1。"
}
if (-not (Test-Path -LiteralPath (Join-Path $stateDir 'migrations-applied.json') -PathType Leaf)) {
    throw "找不到 migrations 完成標記。先執行 ops\install-dev-environment.ps1。"
}
$modulePassword = (Get-Content -LiteralPath $modulePasswordFile -Raw).Trim()

# 13 個 module schema（見 install-dev-environment.ps1 同一份清單）＋ valkey（Garnet）。
# 三個 Host 用到的模組子集不同，但 GetConnectionString 只在真的要開那個 DbContext
# 時才會查表——多給不用的鍵沒有副作用，比逐一對照三份 Program.cs 的模組清單更不容易漂移。
$moduleSchemas = @(
    'iam', 'catalog', 'campaign', 'checkout', 'fulfillment', 'inventory',
    'ledger', 'notify', 'ordering', 'payment', 'pricing', 'procurement', 'platform'
)
$sharedConnectionStrings = @{}
foreach ($schema in $moduleSchemas) {
    $sharedConnectionStrings["ConnectionStrings__GreyGray_$schema"] =
        "Host=127.0.0.1;Port=$PostgreSqlPort;Database=postgres;Username=greygray_$schema;Password=$modulePassword;Include Error Detail=true"
}
$sharedConnectionStrings['ConnectionStrings__GreyGray_valkey'] = "127.0.0.1:$GarnetPort"

# Identity 個資保護金鑰：三個 Host 都呼叫 AddIdentityModule，缺這個鍵的話
# DI 解析期就丟「缺少 Identity 個資保護金鑰」，登入／註冊／購物車全部 500
# （FE-12 第九波實測）。巢狀設定鍵用雙底線，跟上面的 ConnectionStrings__ 同一個慣例。
$dataProtectionKeyFile = Join-Path $secretsDir 'identity-dataprotection.key'
if (-not (Test-Path -LiteralPath $dataProtectionKeyFile -PathType Leaf)) {
    throw "找不到 Identity 個資保護金鑰：$dataProtectionKeyFile。先執行 ops\install-dev-environment.ps1。"
}
$sharedConnectionStrings['Identity__DataProtectionKey'] =
    (Get-Content -LiteralPath $dataProtectionKeyFile -Raw).Trim()

function Start-DevHost {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ProjectName,
        [Parameter(Mandatory)][hashtable]$Environment
    )
    $exe = Join-Path $repo "src\Hosts\$ProjectName\bin\$Configuration\net10.0\$ProjectName.exe"
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        throw "找不到已建置的 Host：$exe。先 dotnet build .\GreyGray.slnx -c $Configuration。"
    }

    $pidFile = Join-Path $stateDir "$Name.pid"
    if (Test-Path -LiteralPath $pidFile) {
        $existingId = [int](Get-Content -LiteralPath $pidFile)
        if ($null -ne (Get-Process -Id $existingId -ErrorAction SilentlyContinue)) {
            Write-Host "$Name 已在跑（PID $existingId）"
            return $existingId
        }
    }

    $previous = @{}
    foreach ($key in $Environment.Keys) {
        $previous[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, $Environment[$key], 'Process')
    }
    try {
        $proc = Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) `
            -RedirectStandardOutput (Join-Path $logDir "$Name.out.log") `
            -RedirectStandardError (Join-Path $logDir "$Name.err.log") `
            -WindowStyle Hidden -PassThru
    }
    finally {
        foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key], 'Process') }
    }
    Set-Content -LiteralPath $pidFile -Value $proc.Id
    Write-Host "已啟動 $Name（PID $($proc.Id)）"
    return $proc.Id
}

function Wait-HealthOk {
    param([Parameter(Mandatory)][int]$Port, [Parameter(Mandatory)][int]$ProcessId, [string]$Name)
    $deadline = [datetime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    do {
        if ($null -eq (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)) {
            throw "$Name（PID $ProcessId）在 /health 回應前就結束了；看 $logDir\$Name.err.log"
        }
        try {
            $response = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/health" -UseBasicParsing -TimeoutSec 3
            if ($response.StatusCode -eq 200) { return $response.Content }
        }
        catch { Start-Sleep -Milliseconds 300 }
    } while ([datetime]::UtcNow -lt $deadline)
    throw "$Name 的 http://127.0.0.1:$Port/health 在 $StartupTimeoutSeconds 秒內沒有回應。"
}

$storefrontEnv = $sharedConnectionStrings.Clone()
$storefrontEnv['ASPNETCORE_ENVIRONMENT'] = 'Development'
$storefrontEnv['ASPNETCORE_URLS'] = "http://127.0.0.1:$StorefrontPort"
$storefrontPid = Start-DevHost -Name 'storefront' -ProjectName 'GreyGray.Api.Storefront' -Environment $storefrontEnv
$storefrontHealth = Wait-HealthOk -Port $StorefrontPort -ProcessId $storefrontPid -Name 'storefront'
Write-Host "PASS storefront /health（PID $storefrontPid）：$storefrontHealth"

$adminEnv = $sharedConnectionStrings.Clone()
$adminEnv['ASPNETCORE_ENVIRONMENT'] = 'Development'
$adminEnv['ASPNETCORE_URLS'] = "http://127.0.0.1:$AdminPort"
$adminPid = Start-DevHost -Name 'admin' -ProjectName 'GreyGray.Api.Admin' -Environment $adminEnv
$adminHealth = Wait-HealthOk -Port $AdminPort -ProcessId $adminPid -Name 'admin'
Write-Host "PASS admin /health（PID $adminPid）：$adminHealth"

# Worker 刻意沒有 HTTP listener（Program.cs 註解：「沒有 listener，不開任何 port」），
# service-manifest.ps1 裡它的 Port=0、HealthPath=$null 是同一件事——
# 「健康」在這裡等於「啟動驗證沒有丟例外，行程持續存活」，不是打某個 /health。
$workerEnv = $sharedConnectionStrings.Clone()
$workerEnv['DOTNET_ENVIRONMENT'] = 'Development'
$workerPid = Start-DevHost -Name 'worker' -ProjectName 'GreyGray.Worker' -Environment $workerEnv
Start-Sleep -Seconds 5
if ($null -eq (Get-Process -Id $workerPid -ErrorAction SilentlyContinue)) {
    throw "worker（PID $workerPid）啟動 5 秒內就結束了；看 $logDir\worker.err.log"
}
Write-Host "PASS worker 存活（PID $workerPid，無 HTTP listener，屬設計如此，見 Program.cs 註解與 service-manifest.ps1）"

Write-Host "PASS 三個 Host 都起來了：storefront=$StorefrontPort admin=$AdminPort worker(no port)"
