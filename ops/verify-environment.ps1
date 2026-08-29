<# M-1 獨立驗收：每個檢查只會輸出 PASS 或 FAIL；找不到絕不略過。 #>
[CmdletBinding()]
param(
    [ValidateSet('All','Node')][string]$Checks = 'All',
    [string]$NodeCommand = 'node.exe',
    [string]$InstallRoot = 'C:\GreyGray',
    [string]$PostgreSqlDataRoot = 'C:\GreyGray\PostgreSQL\data',
    [string]$PostgreSqlWalRoot = 'C:\GreyGray\PostgreSQL\wal',
    [string]$ServiceAccount = '.\GreyGraySvc',
    [string]$ProdMonitorConfigPath,
    [switch]$WiredNetworkConfirmed,
    [switch]$UpsConfirmed
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:failed = 0
function Report([string]$Name, [bool]$Passed, [string]$Detail) {
    if ($Passed) { Write-Host "PASS $Name - $Detail" }
    else { $script:failed++; Write-Host "FAIL $Name - $Detail" }
}
function Has-Command([string]$Name) { $null -ne (Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue) }
function Service-IsRunning([string]$Name) {
    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    $null -ne $service -and $service.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Running
}

$node = Get-Command $NodeCommand -CommandType Application -ErrorAction SilentlyContinue
if ($null -eq $node) {
    Report 'Node 22' $false "找不到 $NodeCommand；不可 silently skip"
}
else {
    $version = & $node.Source --version 2>$null
    Report 'Node 22' ($version -match '^v22\.') "command=$($node.Source); version=$version"
}

if ($Checks -eq 'Node') {
    if ($script:failed -eq 0) { Write-Host 'OVERALL PASS' }
    else { Write-Host "OVERALL FAIL ($script:failed)" }
    if ($script:failed -gt 0) { exit 1 }
    return
}

$sdks = if (Has-Command 'dotnet.exe') { @(dotnet --list-sdks 2>$null) } else { @() }
$runtimes = if (Has-Command 'dotnet.exe') { @(dotnet --list-runtimes 2>$null) } else { @() }
Report '.NET 10 SDK' (@($sdks | Where-Object { $_ -match '^10\.' }).Count -gt 0) ($sdks -join '; ')
Report '.NET 10 Runtime' (@($runtimes | Where-Object { $_ -match '^Microsoft\.NETCore\.App 10\.' }).Count -gt 0) ($runtimes -join '; ')
Report 'ASP.NET Core 10 Runtime' (@($runtimes | Where-Object { $_ -match '^Microsoft\.AspNetCore\.App 10\.' }).Count -gt 0) ($runtimes -join '; ')

$psql = Get-Command psql.exe -CommandType Application -ErrorAction SilentlyContinue
if (-not $psql) {
    $knownPsql = Join-Path $InstallRoot 'PostgreSQL\bin\psql.exe'
    if (Test-Path -LiteralPath $knownPsql -PathType Leaf) { $psql = Get-Item -LiteralPath $knownPsql }
}
$psqlPath = if ($psql -and $psql.PSObject.Properties['Source']) { $psql.Source } elseif ($psql) { $psql.FullName } else { $null }
$psqlVersion = if ($psqlPath) { & $psqlPath --version 2>$null } else { '找不到 psql.exe' }
Report 'PostgreSQL 17 binary' ($psqlVersion -match ' 17\.') $psqlVersion
Report 'PostgreSQL service' (Service-IsRunning 'GreyGray-PostgreSQL') 'GreyGray-PostgreSQL 必須 Running'
$pgIsReady = if ($psqlPath) { Join-Path (Split-Path -Parent $psqlPath) 'pg_isready.exe' } else { $null }
$pgReadyOutput = '找不到 pg_isready.exe'
$pgReady = $false
if ($pgIsReady -and (Test-Path -LiteralPath $pgIsReady -PathType Leaf)) {
    $pgReadyOutput = (& $pgIsReady -h 127.0.0.1 -p 5432 2>&1 | Out-String).Trim()
    $pgReady = $LASTEXITCODE -eq 0
}
Report 'PostgreSQL 連線 5432' $pgReady $pgReadyOutput
Report 'PostgreSQL data 在 C 槽' ([IO.Path]::GetFullPath($PostgreSqlDataRoot).StartsWith('C:\',[StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $PostgreSqlDataRoot -PathType Container)) $PostgreSqlDataRoot
$pgWalPath = Join-Path $PostgreSqlDataRoot 'pg_wal'
$pgWalOnC = (Test-Path -LiteralPath $pgWalPath -PathType Container) -and [IO.Path]::GetFullPath($pgWalPath).StartsWith('C:\', [StringComparison]::OrdinalIgnoreCase)
Report 'PostgreSQL WAL 在 C 槽' $pgWalOnC "$pgWalPath（docs/14 §8.3：不強制獨立 junction，data 與 WAL 都在 C 槽即滿足需求）"

$garnetExe = Join-Path $InstallRoot 'Garnet\GarnetServer.exe'
Report 'Garnet binary' (Test-Path -LiteralPath $garnetExe -PathType Leaf) $(if (Test-Path -LiteralPath $garnetExe -PathType Leaf) { $garnetExe } else { '找不到 GarnetServer.exe' })
Report 'Garnet service' (Service-IsRunning 'GreyGray-Garnet') 'GreyGray-Garnet 必須 Running'
$garnetPort = Get-NetTCPConnection -State Listen -LocalPort 6379 -ErrorAction SilentlyContinue
Report 'Garnet port 6379' ($null -ne $garnetPort) '127.0.0.1:6379 必須有 listener'
$garnetPingOutput = '未測試'
$garnetPing = $false
if ($null -ne $garnetPort) {
    try {
        $client = New-Object System.Net.Sockets.TcpClient('127.0.0.1', 6379)
        $client.ReceiveTimeout = 3000
        $stream = $client.GetStream()
        $request = [System.Text.Encoding]::ASCII.GetBytes("*1`r`n`$4`r`nPING`r`n")
        $stream.Write($request, 0, $request.Length)
        $stream.Flush()
        $buffer = New-Object byte[] 32
        $read = $stream.Read($buffer, 0, $buffer.Length)
        $garnetPingOutput = [System.Text.Encoding]::ASCII.GetString($buffer, 0, $read).Trim()
        $garnetPing = $garnetPingOutput -eq '+PONG'
        $client.Close()
    }
    catch { $garnetPingOutput = $_.Exception.Message }
}
Report 'Garnet RESP PING' $garnetPing $garnetPingOutput

$cloudflared = Get-Command cloudflared.exe -CommandType Application -ErrorAction SilentlyContinue
Report 'cloudflared binary' ($null -ne $cloudflared) $(if ($cloudflared) { $cloudflared.Source } else { '找不到 cloudflared.exe' })
Report 'cloudflared 單一服務' (Service-IsRunning 'cloudflared') 'cloudflared 必須 Running；不得保留六個 ngrok'
Report 'ngrok 已退出' (@(Get-Process -Name ngrok -ErrorAction SilentlyContinue).Count -eq 0) 'ngrok process count 必須為 0'
Report 'NSSM binary' (Has-Command 'nssm.exe') 'nssm.exe 必須可解析'

function Test-ServiceAccountCanReadBinary([Microsoft.Management.Infrastructure.CimInstance]$Service, [string]$Account) {
    <#
        重現 sc start 錯誤 5 的根因檢查：服務帳號對 binPath 指到的執行檔
        有沒有至少讀取權。這條擋的是「winget 裝的東西被服務直接參照」這一類坑——
        WinGet\Links／WinGet\Packages 底下的檔案 ACL 只開放安裝者與 Administrators/SYSTEM。
    #>
    if ($null -eq $Service -or [string]::IsNullOrWhiteSpace($Service.PathName)) { return $null }
    $exePath = ($Service.PathName -replace '^"([^"]+)".*$', '$1')
    if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) { return $null }
    $expected = $Account -replace '^\.\\', ''
    $acl = Get-Acl -LiteralPath $exePath
    return @($acl.Access | Where-Object {
        $_.IdentityReference.Value -like "*$expected" -or
        $_.IdentityReference.Value -eq 'NT AUTHORITY\Authenticated Users' -or
        $_.IdentityReference.Value -eq 'BUILTIN\Users'
    } | Where-Object { $_.FileSystemRights -match 'ReadAndExecute|GenericRead|GenericExecute|FullControl|Modify' }).Count -gt 0
}

$manifest = & (Join-Path $PSScriptRoot 'service-manifest.ps1')
$allNssmServiceNames = @($manifest.Services | ForEach-Object { $_.Name }) + @('GreyGray-PostgreSQL', 'GreyGray-Garnet')
foreach ($name in $allNssmServiceNames) {
    $service = Get-CimInstance Win32_Service -Filter "Name='$name'" -ErrorAction SilentlyContinue
    Report "NSSM $name" ($null -ne $service) $(if ($service) { "StartName=$($service.StartName); State=$($service.State)" } else { '找不到服務' })
    if ($service) {
        $expected = $ServiceAccount -replace '^\.\\', ''
        Report "$name 專屬帳號" ($service.StartName -like "*$expected") "StartName=$($service.StartName)"
        $canRead = Test-ServiceAccountCanReadBinary -Service $service -Account $ServiceAccount
        if ($null -ne $canRead) {
            Report "$name binPath 服務帳號可讀取" $canRead "PathName=$($service.PathName)"
        }
    }
}

$exclusions = if (Get-Command Get-MpPreference -ErrorAction SilentlyContinue) { @((Get-MpPreference).ExclusionPath) } else { @() }
Report 'Defender 排除 PostgreSQL data' ($exclusions -contains $PostgreSqlDataRoot) $PostgreSqlDataRoot
Report 'Defender 排除 PostgreSQL WAL' ($exclusions -contains $PostgreSqlWalRoot) $PostgreSqlWalRoot
$auPath = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU'
$au = Get-ItemProperty -Path $auPath -Name NoAutoUpdate -ErrorAction SilentlyContinue
$noAuto = if ($au -and $au.PSObject.Properties['NoAutoUpdate']) { $au.NoAutoUpdate } else { $null }
Report 'Windows Update 人工模式' ($noAuto -eq 1) "NoAutoUpdate=$noAuto"
Report '維護窗提醒' ($null -ne (Get-ScheduledTask -TaskName 'GreyGray-Windows-Maintenance-Reminder' -ErrorAction SilentlyContinue)) '每週日 03:00'

$configContent = if ($ProdMonitorConfigPath -and (Test-Path -LiteralPath $ProdMonitorConfigPath -PathType Leaf)) { [IO.File]::ReadAllText($ProdMonitorConfigPath) } else { '' }
foreach ($pair in @(@('GreyGray-Web-Storefront','5002'), @('GreyGray-Web-Admin','5003'))) {
    $hasFingerprint = $configContent.Contains('name = "' + $pair[0] + '"') -and $configContent.Contains('port = ' + $pair[1]) -and $configContent.Contains('process_name = "node.exe"')
    Report "prod-monitor $($pair[0])" $hasFingerprint "process=node.exe; port=$($pair[1])"
}
Report '有線網路（待人工）' ([bool]$WiredNetworkConfirmed) '由現場人員確認並以 -WiredNetworkConfirmed 記錄'
Report 'UPS（待人工）' ([bool]$UpsConfirmed) '由現場人員購買接妥並以 -UpsConfirmed 記錄'

if ($script:failed -eq 0) { Write-Host 'OVERALL PASS' }
else { Write-Host "OVERALL FAIL ($script:failed)" }
if ($script:failed -gt 0) { exit 1 }
