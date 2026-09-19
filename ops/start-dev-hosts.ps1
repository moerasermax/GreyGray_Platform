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

    #38：環境變數用 Start-Process -Environment 直接交給子行程，
    絕對不要改父行程的環境再還原——見 Start-DevHost 上面那一段。
#>
#Requires -Version 7.4
[CmdletBinding()]
param(
    [string]$InstallRoot = 'D:\GreyGray',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [int]$StorefrontPort = 5000,
    [int]$AdminPort = 5001,
    [int]$PostgreSqlPort = 5432,
    [int]$GarnetPort = 6379,
    # 前台（Next dev server）對外的 port。綠界完成頁的「返回商店」要導回這裡，
    # 所以 Host 得知道它——Storefront:PublicOrigin 缺了的話付款端點會明確地炸（#33）。
    [int]$StorefrontPublicPort = 5002,
    # BE-42：綠界 ReturnURL（伺服器對伺服器的回呼）要用的對外 API origin。
    # dev 預設不給——模擬器跟 Host 同一台，Host 用這一次請求的 scheme/host 組出來的
    # http://127.0.0.1:5000/v1/webhooks/ecpay 本來就是對的。
    # 只有在把 5000 透過通道露出去、要打真的綠界測試站時才需要指定
    # （例如 -StorefrontPublicApiOrigin https://xxx.trycloudflare.com）。
    [string]$StorefrontPublicApiOrigin,
    # dev 綠界模擬器（ADR-029）。不加這個開關，行為跟以前完全一樣。
    [switch]$UseEcpaySimulator,
    [int]$EcpaySimulatorPort = 5009,
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

# 14 個 module schema（見 install-dev-environment.ps1 同一份清單）＋ valkey（Garnet）。
# 三個 Host 用到的模組子集不同，但 GetConnectionString 只在真的要開那個 DbContext
# 時才會查表——多給不用的鍵沒有副作用，比逐一對照三份 Program.cs 的模組清單更不容易漂移。
$moduleSchemas = @(
    'iam', 'catalog', 'campaign', 'checkout', 'fulfillment', 'inventory',
    'ledger', 'notify', 'ordering', 'payment', 'pricing', 'procurement', 'platform',
    'customer_service'
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

    # #38：直接把環境交給子行程，不碰父行程。
    #
    # 原本的做法是「改父行程 → Start-Process 讓子行程繼承 → finally 還原」，有兩個坑：
    # ① 還原時 $previous[$key] 對本來不存在的變數是 $null，PowerShell 把 $null 傳給
    #    .NET 的 string 參數會變成**空字串**——還原之後 Test-Path Env:X 是 True、值是空的。
    # ② Leader 2026-09-02 在同一個 shell「起 → 停 → 再起」，第二次起的三個 Host
    #    Hosting environment 印成空的，於是 Program.cs 的 IsDevelopment() 為 false、
    #    CORS 沒開，前台每一個跨源呼叫預檢 OPTIONS 都 405（畫面上是「加入購物車失敗」）。
    #    第二次為什麼沒進子行程沒有查出來——這裡把整個模式拿掉，讓這一類 bug 沒有地方發生。
    $proc = Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) `
        -Environment $Environment `
        -RedirectStandardOutput (Join-Path $logDir "$Name.out.log") `
        -RedirectStandardError (Join-Path $logDir "$Name.err.log") `
        -WindowStyle Hidden -PassThru
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

# ── dev 綠界模擬器（ADR-029）────────────────────────────────────────────────
#
# 假的不是我們的 adapter，假的是綠界的伺服器。這裡只換掉「本來就可設定」的兩個端點網址，
# 外加明確打開 AllowNonEcpayEndpoints——正式機忘了拿掉這些設定會在 DI 解析期立刻炸。
# 換回正式綠界 ＝ 不加 -UseEcpaySimulator ＋ 真憑證 ＋ 明確投遞 CheckoutUrl／CreditDetailUrl。
# 不開模擬器時，五個 Payment__ECPay__* 值由父行程環境提供；兩個網址必填、沒有預設。
#
# 退款跑在 Worker（Payment.Infra/OrderingEventHandlers），所以三個 Host 都要拿到這一組。
$ecpayEnv = @{}
$logisticsEnv = @{}
if ($UseEcpaySimulator) {
    $logisticsKeys = @(
        'Logistics__ECPay__MerchantId',
        'Logistics__ECPay__LogisticsSubType',
        'Logistics__ECPay__MapUrl',
        'Logistics__ECPay__AllowNonEcpayEndpoints'
    )
    $providedLogisticsKeys = @()
    foreach ($key in $logisticsKeys) {
        if (-not [string]::IsNullOrWhiteSpace(
                [Environment]::GetEnvironmentVariable($key, 'Process'))) {
            $providedLogisticsKeys += $key
        }
    }

    if ($providedLogisticsKeys.Count -eq 0) {
        $logisticsEnv['Logistics__ECPay__MerchantId'] = 'DEVFAKE0000'
        $logisticsEnv['Logistics__ECPay__LogisticsSubType'] = 'UNIMARTC2C'
        $logisticsEnv['Logistics__ECPay__MapUrl'] = "http://127.0.0.1:$EcpaySimulatorPort/Express/map"
        $logisticsEnv['Logistics__ECPay__AllowNonEcpayEndpoints'] = 'true'
    }
    elseif ($providedLogisticsKeys.Count -eq $logisticsKeys.Count) {
        foreach ($key in $logisticsKeys) {
            $logisticsEnv[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        }
    }
    else {
        $missingLogisticsKeys = @($logisticsKeys | Where-Object { $providedLogisticsKeys -notcontains $_ })
        throw "-UseEcpaySimulator 的物流設定必須四個全有或全無。缺少：$($missingLogisticsKeys -join ', ')"
    }

    & (Join-Path $PSScriptRoot 'start-dev-ecpay-simulator.ps1') `
        -InstallRoot $InstallRoot -Configuration $Configuration -Port $EcpaySimulatorPort `
        -StartupTimeoutSeconds $StartupTimeoutSeconds | Out-Null

    # 環境已經有值就沿用：Leader 手動投遞過真憑證時，不要被腳本裡的假值蓋掉。
    $ecpayDefaults = @{
        'Payment__ECPay__MerchantId' = 'DEVFAKE0000'
        'Payment__ECPay__HashKey'    = 'DEVFAKEHASHKEY01'
        'Payment__ECPay__HashIV'     = 'DEVFAKEHASHIV001'
    }
    foreach ($key in $ecpayDefaults.Keys) {
        $existing = [Environment]::GetEnvironmentVariable($key, 'Process')
        $ecpayEnv[$key] = if ([string]::IsNullOrWhiteSpace($existing)) { $ecpayDefaults[$key] } else { $existing }
    }
    $ecpayEnv['Payment__ECPay__CheckoutUrl'] = "http://127.0.0.1:$EcpaySimulatorPort/Cashier/AioCheckOut/V5"
    $ecpayEnv['Payment__ECPay__CreditDetailUrl'] = "http://127.0.0.1:$EcpaySimulatorPort/CreditDetail/DoAction"
    $ecpayEnv['Payment__ECPay__AllowNonEcpayEndpoints'] = 'true'
    Write-Host "已接上綠界模擬器（port $EcpaySimulatorPort），三個 Host 都會拿到 DEVFAKE 那一組設定。"
}

function Add-EcpayEnvironment {
    param([Parameter(Mandatory)][hashtable]$Environment)
    foreach ($key in $ecpayEnv.Keys) { $Environment[$key] = $ecpayEnv[$key] }
    return $Environment
}

$storefrontEnv = $sharedConnectionStrings.Clone()
# 綠界完成頁「返回商店」要導回的前台位址（#33）。刻意用 127.0.0.1 而不是 localhost：
# cookie 依 hostname 隔離，猜錯的話 /payment/result 會拿 401，症狀看起來像「登入壞了」。
$storefrontEnv['Storefront__PublicOrigin'] = "http://127.0.0.1:$StorefrontPublicPort"
if (-not [string]::IsNullOrWhiteSpace($StorefrontPublicApiOrigin)) {
    $storefrontEnv['Storefront__PublicApiOrigin'] = $StorefrontPublicApiOrigin.TrimEnd('/')
    Write-Host "綠界 ReturnURL 將用 $($StorefrontPublicApiOrigin.TrimEnd('/'))/v1/webhooks/ecpay（不給這個參數就用請求的 scheme/host）。"
}
$storefrontEnv['ASPNETCORE_ENVIRONMENT'] = 'Development'
$storefrontEnv['ASPNETCORE_URLS'] = "http://127.0.0.1:$StorefrontPort"
$storefrontEnv = Add-EcpayEnvironment -Environment $storefrontEnv
foreach ($key in $logisticsEnv.Keys) { $storefrontEnv[$key] = $logisticsEnv[$key] }
$storefrontPid = Start-DevHost -Name 'storefront' -ProjectName 'GreyGray.Api.Storefront' -Environment $storefrontEnv
$storefrontHealth = Wait-HealthOk -Port $StorefrontPort -ProcessId $storefrontPid -Name 'storefront'
Write-Host "PASS storefront /health（PID $storefrontPid）：$storefrontHealth"

$adminEnv = $sharedConnectionStrings.Clone()
$adminEnv['ASPNETCORE_ENVIRONMENT'] = 'Development'
$adminEnv['ASPNETCORE_URLS'] = "http://127.0.0.1:$AdminPort"
$adminEnv = Add-EcpayEnvironment -Environment $adminEnv
$adminPid = Start-DevHost -Name 'admin' -ProjectName 'GreyGray.Api.Admin' -Environment $adminEnv
$adminHealth = Wait-HealthOk -Port $AdminPort -ProcessId $adminPid -Name 'admin'
Write-Host "PASS admin /health（PID $adminPid）：$adminHealth"

# Worker 刻意沒有 HTTP listener（Program.cs 註解：「沒有 listener，不開任何 port」），
# service-manifest.ps1 裡它的 Port=0、HealthPath=$null 是同一件事——
# 「健康」在這裡等於「啟動驗證沒有丟例外，行程持續存活」，不是打某個 /health。
$workerEnv = $sharedConnectionStrings.Clone()
$workerEnv['DOTNET_ENVIRONMENT'] = 'Development'
$workerEnv = Add-EcpayEnvironment -Environment $workerEnv
$workerPid = Start-DevHost -Name 'worker' -ProjectName 'GreyGray.Worker' -Environment $workerEnv
Start-Sleep -Seconds 5
if ($null -eq (Get-Process -Id $workerPid -ErrorAction SilentlyContinue)) {
    throw "worker（PID $workerPid）啟動 5 秒內就結束了；看 $logDir\worker.err.log"
}
Write-Host "PASS worker 存活（PID $workerPid，無 HTTP listener，屬設計如此，見 Program.cs 註解與 service-manifest.ps1）"

$ecpayNote = if ($UseEcpaySimulator) { "，綠界模擬器=$EcpaySimulatorPort" } else { '' }
Write-Host "PASS 三個 Host 都起來了：storefront=$StorefrontPort admin=$AdminPort worker(no port)$ecpayNote"
