<#
    GreyGray 自己的 Cloudflare Tunnel（本機管理式，ADR-031）。

    正式機 YC 上已經有一支 token 式的 `Cloudflared` 服務，那是使用者其他應用
    （Planner／Portfolio／Knowledge／Baby…）共用的命脈——這支腳本**完全不碰它**，
    也不碰 C:\ProgramData\cloudflared\ 與排程工作 CloudflaredWatchdog。
    GreyGray 另外起一支 NSSM 服務 GreyGray-Tunnel，設定與憑證都在 $InstallRoot\cloudflared\。

    需要人在瀏覽器點授權的兩步**不在這裡**（Leader 手動先做完，見 docs/14 §12）：
        cloudflared tunnel login
        cloudflared tunnel create greygray          # 產出 <UUID>.json
        cloudflared tunnel route dns greygray greygray.shop
        cloudflared tunnel route dns greygray admin.greygray.shop

    正式機只有 Windows PowerShell 5.1：不用 ??、?.、三元運算子、#Requires -Version 7。
#>
[CmdletBinding()]
param(
    [string]$InstallRoot = 'C:\GreyGray',
    [string]$TunnelName = 'greygray',
    [Parameter(Mandatory)][string]$TunnelCredentialsFile,
    [string]$StorefrontHostname = 'greygray.shop',
    [string]$AdminHostname = 'admin.greygray.shop',
    [int]$StorefrontApiPort = 5000,
    [int]$StorefrontWebPort = 5002,
    [int]$AdminApiPort = 5001,
    [int]$AdminWebPort = 5003,
    [string]$NssmPath,
    [pscredential]$ServiceCredential,
    [string]$CloudflaredPath,
    [string]$ServiceName = 'GreyGray-Tunnel',
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. "$PSScriptRoot\lib\Process.ps1"

if ([string]::IsNullOrWhiteSpace($NssmPath)) { $NssmPath = Join-Path $InstallRoot 'bin\nssm.exe' }

$installFull = [System.IO.Path]::GetFullPath($InstallRoot)
$tunnelDirectory = Join-Path $installFull 'cloudflared'
$logsRoot = Join-Path $installFull 'logs'
$configPath = Join-Path $tunnelDirectory 'config.yml'

# ── 憑證 JSON：只讀 TunnelID，不印 TunnelSecret ─────────────────────────────
if (-not (Test-Path -LiteralPath $TunnelCredentialsFile -PathType Leaf)) {
    throw "找不到 tunnel 憑證 JSON：$TunnelCredentialsFile（Leader 要先跑 cloudflared tunnel create $TunnelName 產生）"
}
$credentialsFull = [System.IO.Path]::GetFullPath($TunnelCredentialsFile)
$credentialsJson = [System.IO.File]::ReadAllText($credentialsFull, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
if ($null -eq $credentialsJson.PSObject.Properties['TunnelID']) {
    throw "憑證 JSON 缺少 TunnelID 欄位：$credentialsFull（cloudflared tunnel create 的產出應含 AccountTag／TunnelSecret／TunnelID）"
}
$tunnelId = [string]$credentialsJson.TunnelID
if ([string]::IsNullOrWhiteSpace($tunnelId)) { throw "憑證 JSON 的 TunnelID 是空的：$credentialsFull" }

$targetCredentialsPath = Join-Path $tunnelDirectory ($tunnelId + '.json')

<#
    config.yml 內容（UTF-8 無 BOM、LF）。
    順序就是優先序：每個主機名稱先比 /v1/（後端 Host），再吃其餘（Next 網頁）。
    path 是正規表示式；最後一條沒有 hostname，是 cloudflared 要求的 catch-all。
#>
$ingressLines = @(
    "  - hostname: $StorefrontHostname"
    '    path: ^/v1/'
    "    service: http://127.0.0.1:$StorefrontApiPort"
    "  - hostname: $StorefrontHostname"
    "    service: http://127.0.0.1:$StorefrontWebPort"
    "  - hostname: $AdminHostname"
    '    path: ^/v1/'
    "    service: http://127.0.0.1:$AdminApiPort"
    "  - hostname: $AdminHostname"
    "    service: http://127.0.0.1:$AdminWebPort"
    '  - service: http_status:404'
)
$configLines = @(
    "tunnel: $tunnelId"
    "credentials-file: $targetCredentialsPath"
    'ingress:'
) + $ingressLines
$configContent = ($configLines -join "`n") + "`n"

# ── cloudflared.exe：實際安裝一定要有；-ValidateOnly 沒有就只是少一步驗證 ──
if ([string]::IsNullOrWhiteSpace($CloudflaredPath)) {
    $cloudflaredCommand = Get-Command cloudflared.exe -CommandType Application -ErrorAction SilentlyContinue
    if ($null -ne $cloudflaredCommand) { $CloudflaredPath = $cloudflaredCommand.Source }
}
$hasCloudflared = (-not [string]::IsNullOrWhiteSpace($CloudflaredPath)) -and (Test-Path -LiteralPath $CloudflaredPath -PathType Leaf)

function Invoke-IngressValidate([string]$Path) {
    <#
        離線驗規則：不連線、不啟動通道。通過就靜靜回來，任何一種沒驗到都 throw。

        ★ `--config` 是 `tunnel` 這一層的 flag，必須放在 `ingress validate` **前面**
        （cloudflared 自己的 USAGE：`cloudflared tunnel [--config FILEPATH] ingress validate`）。
        放到後面會變成 `Incorrect Usage: flag provided but not defined: -config`，
        而 cloudflared 印完 help 之後 **exit code 仍然是 0**——只看 exit code 會以為驗過了，
        其實一條規則都沒驗。BE-43 第一、二輪就是這樣：兩條路都印了「PASS ... ingress validate」，
        而那個 PASS 是假的（Leader 在 YC 用 cloudflared 2026.8.2 實測出來的）。

        所以這裡三件事都要判，缺一不可：
          ① exit code 非 0        → 真的驗失敗（例如最後一條 ingress 帶了 hostname），訊息在 stderr
          ② 輸出含 Incorrect Usage → 用法錯了，根本沒驗（exit 0 騙不過這一條）
          ③ 輸出沒有獨立一行 OK    → 沒看到成功訊號就不算過，不要對「安靜的成功」給好處
    #>
    $result = Invoke-NativeCommand -FilePath $CloudflaredPath `
        -ArgumentList @('tunnel', '--config', $Path, 'ingress', 'validate') -AllowNonZeroExit -EchoOutput
    $output = (@($result.StdOut, $result.StdErr) -join "`n").Trim()
    if ($result.ExitCode -ne 0) {
        throw "cloudflared ingress validate 失敗（exit $($result.ExitCode)）：$output"
    }
    if ($output -match 'Incorrect Usage') {
        throw "cloudflared 沒有真的驗證（用法錯誤，但 cloudflared 仍以 exit 0 結束），輸出：$output"
    }
    if (@($output -split "`r?`n" | Where-Object { $_.Trim() -eq 'OK' }).Count -eq 0) {
        throw "cloudflared 沒有真的驗證（輸出裡沒有獨立一行 OK），輸出：$output"
    }
}

if ($ValidateOnly) {
    Write-Host "InstallRoot      = $installFull"
    Write-Host "TunnelName       = $TunnelName"
    Write-Host "TunnelID         = $tunnelId"
    Write-Host "憑證來源         = $credentialsFull"
    Write-Host "憑證將複製到     = $targetCredentialsPath"
    Write-Host "config.yml       = $configPath"
    Write-Host "ServiceName      = $ServiceName"
    Write-Host "NssmPath         = $NssmPath"
    if ($hasCloudflared) { Write-Host "CloudflaredPath  = $CloudflaredPath" }
    else { Write-Host 'CloudflaredPath  = （這台機器上找不到 cloudflared.exe）' }
    Write-Host ''
    Write-Host '--- 將寫入的 config.yml ---'
    Write-Host $configContent.TrimEnd()
    Write-Host '--- config.yml 結束 ---'
    if ($hasCloudflared) {
        $probe = Join-Path ([System.IO.Path]::GetTempPath()) ('greygray-tunnel-' + [guid]::NewGuid().ToString('N') + '.yml')
        try {
            [System.IO.File]::WriteAllText($probe, $configContent, (New-Object System.Text.UTF8Encoding($false)))
            Invoke-IngressValidate $probe
            Write-Host 'PASS cloudflared tunnel ingress validate'
        }
        finally {
            if (Test-Path -LiteralPath $probe -PathType Leaf) { Remove-Item -LiteralPath $probe -Force }
        }
    }
    else {
        Write-Host 'WARN 找不到 cloudflared.exe，略過 ingress validate——這一步要在正式機（或裝了 cloudflared 的機器）上跑。'
    }
    Write-Host 'PASS install-tunnel 參數驗證：未建目錄、未複製憑證、未登記服務、未碰現有的 Cloudflared 服務。'
    return
}

if ($env:OS -ne 'Windows_NT') { throw 'install-tunnel.ps1 只能在 Windows 執行。' }
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '請在正式機的系統管理員 PowerShell 執行。'
}
if ($null -eq $ServiceCredential) { throw "實際安裝必須提供 -ServiceCredential（$ServiceName 要以 GreyGraySvc 執行，不可用 LocalSystem）。" }
if (-not $hasCloudflared) { throw '找不到 cloudflared.exe；請先由 install-environment.ps1（winget Cloudflare.cloudflared）安裝，或用 -CloudflaredPath 指定。' }
if (-not (Test-Path -LiteralPath $NssmPath -PathType Leaf)) { throw "找不到 NSSM：$NssmPath" }

<#
    ① 目錄與憑證。放在 $InstallRoot 底下才會繼承已經授予 GreyGraySvc 的 Modify ACL——
    docs/14 §10 那個 sc start 錯誤 5 的根因就是「服務帳號讀不到自己要用的檔案」。
#>
foreach ($directory in @($tunnelDirectory, $logsRoot)) {
    if (Test-Path -LiteralPath $directory -PathType Container) { Write-Host "已存在 $directory" }
    else { New-Item -ItemType Directory -Path $directory -Force | Out-Null; Write-Host "已建立 $directory" }
}

$needsCopy = $true
if (Test-Path -LiteralPath $targetCredentialsPath -PathType Leaf) {
    $existingHash = (Get-FileHash -LiteralPath $targetCredentialsPath -Algorithm SHA256).Hash
    $sourceHash = (Get-FileHash -LiteralPath $credentialsFull -Algorithm SHA256).Hash
    if ($existingHash -eq $sourceHash) { $needsCopy = $false; Write-Host "已存在 $targetCredentialsPath（內容相同，不重複複製）" }
}
if ($needsCopy) {
    # 來源檔不動；只複製一份進 $InstallRoot，讓它繼承目錄 ACL。
    Copy-Item -LiteralPath $credentialsFull -Destination $targetCredentialsPath -Force
    Write-Host "已複製憑證 $targetCredentialsPath"
}

# ② config.yml：內容一樣就不重寫（重跑不重做）。
$configUpToDate = $false
if (Test-Path -LiteralPath $configPath -PathType Leaf) {
    $existingConfig = [System.IO.File]::ReadAllText($configPath, [System.Text.Encoding]::UTF8)
    if ($existingConfig -eq $configContent) { $configUpToDate = $true }
}
if ($configUpToDate) { Write-Host "已存在 $configPath（內容相同，不重寫）" }
else {
    [System.IO.File]::WriteAllText($configPath, $configContent, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "已寫入 $configPath"
}
Invoke-IngressValidate $configPath
Write-Host 'PASS cloudflared tunnel ingress validate'

<#
    ③ NSSM 登記（比照 deploy.ps1 第 315-379 行那組設定）。
    NSSM API 只能收明文密碼；它只活在這個 process 的記憶體與短暫子程序命令列，
    不落任何檔案、不進 log、不印出來。
#>
$servicePassword = $ServiceCredential.GetNetworkCredential().Password
try {
    $existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($null -eq $existingService) {
        Invoke-NativeCommand -FilePath $NssmPath -ArgumentList @('install', $ServiceName, $CloudflaredPath) -EchoOutput | Out-Null
        Write-Host "已登記服務 $ServiceName"
    }
    else { Write-Host "已存在服務 $ServiceName（只更新設定，不重新 install）" }

    $nssmSettings = @(
        @('Application', $CloudflaredPath),
        @('AppDirectory', $tunnelDirectory),
        @('AppParameters', '--config', $configPath, '--no-autoupdate', 'tunnel', 'run', $TunnelName),
        @('Start', 'SERVICE_AUTO_START'),
        @('AppExit', 'Default', 'Restart'),
        @('AppRestartDelay', '60000'),
        @('AppThrottle', '1500'),
        @('AppStdout', (Join-Path $logsRoot "$ServiceName.stdout.log")),
        @('AppStderr', (Join-Path $logsRoot "$ServiceName.stderr.log")),
        @('AppRotateFiles', '1'),
        @('AppRotateBytes', '10485760')
    )
    foreach ($setting in $nssmSettings) {
        Invoke-NativeCommand -FilePath $NssmPath -ArgumentList (@('set', $ServiceName) + $setting) -EchoOutput | Out-Null
    }
    Invoke-NativeCommand -FilePath $NssmPath `
        -ArgumentList @('set', $ServiceName, 'ObjectName', $ServiceCredential.UserName, $servicePassword) | Out-Null
    Write-Host "已設定 $ServiceName 以 $($ServiceCredential.UserName) 執行"
}
finally {
    $servicePassword = $null
}

# ④ 啟動並等 Running。
$service = Get-Service -Name $ServiceName
if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Running) {
    Start-Service -Name $ServiceName
}
$service.Refresh()
$service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [timespan]::FromSeconds(60))
Write-Host "PASS $ServiceName 服務 Running"

<#
    cloudflared tunnel info 需要 cert.pem（在跑過 tunnel login 的那個 Windows 帳號底下）。
    服務本身只需要 config.yml ＋ 憑證 JSON，沒有 cert.pem 不代表通道有問題。
#>
$certPath = Join-Path $env:USERPROFILE '.cloudflared\cert.pem'
if (Test-Path -LiteralPath $certPath -PathType Leaf) {
    Invoke-NativeCommand -FilePath $CloudflaredPath -ArgumentList @('tunnel', 'info', $TunnelName) `
        -AllowNonZeroExit -EchoOutput | Out-Null
}
else {
    Write-Host "服務 Running；這個帳號沒有 $certPath，看不到連線數——連線狀態請看 $logsRoot\$ServiceName.stdout.log。"
}

Write-Host "PASS GreyGray 通道安裝完成：$StorefrontHostname ／ $AdminHostname → 127.0.0.1 的 $StorefrontApiPort／$StorefrontWebPort／$AdminApiPort／$AdminWebPort。未碰現有的 Cloudflared 服務。"
