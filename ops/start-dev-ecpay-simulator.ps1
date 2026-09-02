<#
    啟動 dev 用的綠界模擬器（ADR-029）。

    它扮演的是「綠界的伺服器」，不是我們的 adapter：EcpayGateway、回呼判斷、事件、outbox、
    Worker、分錄全部照正式碼跑，只有對面那台機器是假的。start-dev-hosts.ps1 -UseEcpaySimulator
    會呼叫這一支，也可以單獨跑起來用 curl 驗。

    形狀照 start-dev-hosts.ps1 的 Start-DevHost／Wait-HealthOk：直接跑已建置的 exe、
    pid 檔落在 $InstallRoot\state\ecpay-simulator.pid、log 落在 $InstallRoot\logs、
    等 /health 回 200 才算起來。不自己偷跑 dotnet build。

    下面三個 DEVFAKE 值不是機密，是刻意的：模擬器拒絕啟動於任何不以 DEVFAKE 開頭的
    MerchantId，而它發出的 TradeNo 也以 DEVFAKE 開頭——在後台與 DB 一眼看得出是模擬的。

    #38：環境變數用 Start-Process -Environment 直接交給子行程，不改父行程
    （理由見 start-dev-hosts.ps1 的 Start-DevHost）。
#>
#Requires -Version 7.4
[CmdletBinding()]
param(
    [string]$InstallRoot = 'D:\GreyGray',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [int]$Port = 5009,
    [int]$StartupTimeoutSeconds = 45
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

$stateDir = Join-Path $InstallRoot 'state'
$logDir = Join-Path $InstallRoot 'logs'
foreach ($dir in @($stateDir, $logDir)) {
    if (-not (Test-Path -LiteralPath $dir -PathType Container)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
}

$projectName = 'GreyGray.Tools.EcpaySimulator'
$exe = Join-Path $repo "src\Tools\$projectName\bin\$Configuration\net10.0\$projectName.exe"
if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw "找不到已建置的綠界模擬器：$exe。先 dotnet build .\GreyGray.slnx -c $Configuration。"
}

$pidFile = Join-Path $stateDir 'ecpay-simulator.pid'
if (Test-Path -LiteralPath $pidFile) {
    $existingId = [int](Get-Content -LiteralPath $pidFile)
    if ($null -ne (Get-Process -Id $existingId -ErrorAction SilentlyContinue)) {
        Write-Host "ecpay-simulator 已在跑（PID $existingId）"
        return $existingId
    }
}

$environment = @{
    'ASPNETCORE_ENVIRONMENT'       = 'Development'
    'ASPNETCORE_URLS'              = "http://127.0.0.1:$Port"
    'Payment__ECPay__MerchantId'   = 'DEVFAKE0000'
    'Payment__ECPay__HashKey'      = 'DEVFAKEHASHKEY01'
    'Payment__ECPay__HashIV'       = 'DEVFAKEHASHIV001'
}

$proc = Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe) `
    -Environment $environment `
    -RedirectStandardOutput (Join-Path $logDir 'ecpay-simulator.out.log') `
    -RedirectStandardError (Join-Path $logDir 'ecpay-simulator.err.log') `
    -WindowStyle Hidden -PassThru
Set-Content -LiteralPath $pidFile -Value $proc.Id
Write-Host "已啟動 ecpay-simulator（PID $($proc.Id)）"

$deadline = [datetime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
do {
    if ($null -eq (Get-Process -Id $proc.Id -ErrorAction SilentlyContinue)) {
        throw "ecpay-simulator（PID $($proc.Id)）在 /health 回應前就結束了；看 $logDir\ecpay-simulator.err.log"
    }
    try {
        $response = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/health" -UseBasicParsing -TimeoutSec 3
        if ($response.StatusCode -eq 200) {
            Write-Host "PASS ecpay-simulator /health（PID $($proc.Id)）：$($response.Content)"
            return $proc.Id
        }
    }
    catch { Start-Sleep -Milliseconds 300 }
} while ([datetime]::UtcNow -lt $deadline)

throw "ecpay-simulator 的 http://127.0.0.1:$Port/health 在 $StartupTimeoutSeconds 秒內沒有回應。"
