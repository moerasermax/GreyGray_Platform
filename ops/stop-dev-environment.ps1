<#
    收掉 install-dev-environment.ps1 ＋ start-dev-hosts.ps1 啟動的所有背景行程。
    只憑 $InstallRoot\state\*.pid 動作，不碰任何 Windows service／帳號／設定——
    這一組本來就只是一般行程，這樣才收得乾淨、也才敢常常重跑。
#>
[CmdletBinding()]
param(
    [string]$InstallRoot = 'D:\GreyGray'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$stateDir = Join-Path $InstallRoot 'state'

if (-not (Test-Path -LiteralPath $stateDir -PathType Container)) {
    Write-Host "找不到 $stateDir，視為沒有東西要收。"
    return
}

# 先收三個 Host 與 Garnet；PostgreSQL 是 Docker 容器（見 install-dev-environment.ps1
# 裡「postgres.exe 拒絕在 Administrator 帳號下啟動」那段說明），用 docker stop 收，
# 不是 PID 檔。
foreach ($name in @('storefront', 'admin', 'worker', 'garnet', 'ecpay-simulator')) {
    $pidFile = Join-Path $stateDir "$name.pid"
    if (-not (Test-Path -LiteralPath $pidFile -PathType Leaf)) { continue }
    $processId = [int](Get-Content -LiteralPath $pidFile)
    $proc = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if ($null -eq $proc) {
        Write-Host "$name 沒有在跑（PID $processId 已不存在）"
    }
    else {
        Stop-Process -Id $processId -Force
        Write-Host "已停止 $name（PID $processId）"
    }
    Remove-Item -LiteralPath $pidFile -Force
}

$containerName = 'greygray-dev-postgres'
if (Get-Command docker.exe -ErrorAction SilentlyContinue) {
    $containerState = (docker ps -a --filter "name=^/$containerName`$" --format '{{.State}}' 2>$null)
    if ($containerState -eq 'running') {
        docker stop $containerName | Out-Null
        Write-Host "已停止容器 $containerName"
    }
    elseif (-not [string]::IsNullOrWhiteSpace($containerState)) {
        Write-Host "容器 $containerName 本來就沒在跑（狀態：$containerState）"
    }
    else {
        Write-Host "找不到容器 $containerName（視為沒有東西要收）"
    }
}

Write-Host 'PASS 本機開發環境已收掉。PostgreSQL（容器內）／Garnet 的資料與二進位仍留在 InstallRoot／Docker volume 掛載目錄，下次直接重跑 install-dev-environment.ps1 會沿用。'
