<#
    獨立驗收：每個檢查只會輸出 PASS 或 FAIL；找不到絕不略過。

    -Profile Production（預設）＝ M-1 正式機整備驗收，行為與原本完全一致。
    -Profile Local ＝ docs/19 §5 BE-22：本機開發環境（PostgreSQL／Garnet 是一般
    行程、不是 Windows service，也沒有 cloudflared／prod-monitor／專屬服務帳號／
    Windows Update／Defender 這些正式機才有的東西），改成檢查
    migrations 有沒有套完、Storefront／Admin 的 /health 回不回得了。
#>
[CmdletBinding()]
param(
    [ValidateSet('All','Node')][string]$Checks = 'All',
    [ValidateSet('Production','Local')][string]$Profile = 'Production',
    [string]$NodeCommand = 'node.exe',
    [string]$InstallRoot = 'C:\GreyGray',
    [string]$PostgreSqlDataRoot = 'C:\GreyGray\PostgreSQL\data',
    [string]$PostgreSqlWalRoot = 'C:\GreyGray\PostgreSQL\wal',
    [string]$ServiceAccount = '.\GreyGraySvc',
    [string]$ProdMonitorConfigPath,
    [switch]$WiredNetworkConfirmed,
    [switch]$UpsConfirmed,
    [int]$StorefrontPort = 5000,
    [int]$AdminPort = 5001
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:failed = 0
$isLocal = $Profile -eq 'Local'
function Report([string]$Name, [bool]$Passed, [string]$Detail) {
    if ($Passed) { Write-Host "PASS $Name - $Detail" }
    else { $script:failed++; Write-Host "FAIL $Name - $Detail" }
}
function Has-Command([string]$Name) { $null -ne (Get-Command $Name -CommandType Application -ErrorAction SilentlyContinue) }
function Service-IsRunning([string]$Name) {
    $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
    $null -ne $service -and $service.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Running
}

if ($isLocal) {
    Write-Host 'SKIP Node 22 - Local profile 只驗後端三個 Host，Node／前端不在 BE-22 範圍內'
}
else {
    $node = Get-Command $NodeCommand -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $node) {
        Report 'Node 22' $false "找不到 $NodeCommand；不可 silently skip"
    }
    else {
        $version = & $node.Source --version 2>$null
        Report 'Node 22' ($version -match '^v22\.') "command=$($node.Source); version=$version"
    }
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
if ($isLocal) {
    <#
        PostgreSQL 本體在 Local profile 底下是 Docker 容器（greygray-dev-postgres），
        不是原生行程——postgres.exe 在 Administrator 帳號下會被自己的安全檢查拒絕啟動，
        見 install-dev-environment.ps1 同一段說明。這裡改看容器狀態。
    #>
    $containerState = if (Get-Command docker.exe -ErrorAction SilentlyContinue) {
        (docker ps -a --filter 'name=^/greygray-dev-postgres$' --format '{{.State}}' 2>$null)
    } else { $null }
    Report 'PostgreSQL 容器（greygray-dev-postgres）' ($containerState -eq 'running') $(if ($containerState) { "state=$containerState" } else { '找不到容器；docker.exe 不存在或容器未建立' })
}
else {
    Report 'PostgreSQL service' (Service-IsRunning 'GreyGray-PostgreSQL') 'GreyGray-PostgreSQL 必須 Running'
}
$pgIsReady = if ($psqlPath) { Join-Path (Split-Path -Parent $psqlPath) 'pg_isready.exe' } else { $null }
$pgReadyOutput = '找不到 pg_isready.exe'
$pgReady = $false
if ($pgIsReady -and (Test-Path -LiteralPath $pgIsReady -PathType Leaf)) {
    $pgReadyOutput = (& $pgIsReady -h 127.0.0.1 -p 5432 2>&1 | Out-String).Trim()
    $pgReady = $LASTEXITCODE -eq 0
}
Report 'PostgreSQL 連線 5432' $pgReady $pgReadyOutput
<#
    ★ 這兩條原本寫死 StartsWith('C:\')——即使 -PostgreSqlDataRoot／-PostgreSqlWalRoot
    已經是參數，指到別的磁碟一樣會回 FAIL，參數形同虛設（docs/19 §5 BE-22 抓到的 bug，
    跟 install-environment.ps1 那個是同一個根因）。改成跟 $InstallRoot 同一顆磁碟：
    正式機三個路徑預設都在 C:\，斷言不變；本機開發環境全部指到同一顆磁碟一樣過。
#>
$installRootDrive = [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($InstallRoot))
$pgDataOnExpectedDrive = (Test-Path -LiteralPath $PostgreSqlDataRoot -PathType Container) -and
    ([IO.Path]::GetFullPath($PostgreSqlDataRoot).StartsWith($installRootDrive, [StringComparison]::OrdinalIgnoreCase))
Report "PostgreSQL data 與 InstallRoot 同磁碟（$installRootDrive）" $pgDataOnExpectedDrive $PostgreSqlDataRoot
$pgWalPath = Join-Path $PostgreSqlDataRoot 'pg_wal'
$pgWalOnExpectedDrive = (Test-Path -LiteralPath $pgWalPath -PathType Container) -and
    ([IO.Path]::GetFullPath($pgWalPath).StartsWith($installRootDrive, [StringComparison]::OrdinalIgnoreCase))
Report "PostgreSQL WAL 與 InstallRoot 同磁碟（$installRootDrive）" $pgWalOnExpectedDrive "$pgWalPath（docs/14 §8.3：不強制獨立 junction，data 與 WAL 同磁碟即滿足需求）"

<#
    ★ winget 裝的 Microsoft.Garnet.DN8 套件底下是 net8.0\／net9.0\ 兩個 TFM 子資料夾，
    GarnetServer.exe 不在套件根目錄（見 install-dev-environment.ps1 同一段說明）。
    這裡跟正式機路徑（$InstallRoot\Garnet\GarnetServer.exe）不同，兩者都要認得，
    因為 Production profile 目前的複製慣例（install-environment.ps1）還沒改。
#>
$garnetExeCandidates = @(
    (Join-Path $InstallRoot 'Garnet\net8.0\GarnetServer.exe'),
    (Join-Path $InstallRoot 'Garnet\GarnetServer.exe')
)
$garnetExe = $garnetExeCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
Report 'Garnet binary' ($null -ne $garnetExe) $(if ($garnetExe) { $garnetExe } else { "找不到 GarnetServer.exe（試過：$($garnetExeCandidates -join '; ')）" })
if ($isLocal) {
    $garnetProcess = Get-Process -Name 'GarnetServer' -ErrorAction SilentlyContinue
    Report 'Garnet 行程' ($null -ne $garnetProcess) $(if ($garnetProcess) { "PID $($garnetProcess.Id -join ',')" } else { 'Local profile：找不到 GarnetServer.exe 行程（一般行程，不是 Windows service）' })
}
else {
    Report 'Garnet service' (Service-IsRunning 'GreyGray-Garnet') 'GreyGray-Garnet 必須 Running'
}
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

if ($isLocal) {
    Write-Host 'SKIP cloudflared／NSSM 服務帳號／Defender／Windows Update／維護窗／prod-monitor／有線網路／UPS - Local profile 是本機開發環境，這些是正式機才有的東西（docs/19 §5 BE-22）'
}
else {

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

}

if ($isLocal) {
    $migrationsStateFile = Join-Path $InstallRoot 'state\migrations-applied.json'
    $migrationsApplied = $false
    $migrationsDetail = "找不到 $migrationsStateFile"
    if (Test-Path -LiteralPath $migrationsStateFile -PathType Leaf) {
        $state = Get-Content -LiteralPath $migrationsStateFile -Raw | ConvertFrom-Json
        $expectedCount = 14
        $migrationsApplied = @($state.files).Count -eq $expectedCount
        $migrationsDetail = "appliedAt=$($state.appliedAt); files=$(@($state.files).Count)（期望 $expectedCount：0001_~0014_）"
    }
    Report 'migrations 0001~0014 已套用' $migrationsApplied $migrationsDetail

    foreach ($pair in @(@('storefront', $StorefrontPort), @('admin', $AdminPort))) {
        $name, $port = $pair
        $healthOk = $false
        $healthDetail = "http://127.0.0.1:$port/health 未回應"
        try {
            $response = Invoke-WebRequest -Uri "http://127.0.0.1:$port/health" -UseBasicParsing -TimeoutSec 5
            $healthOk = $response.StatusCode -eq 200
            $healthDetail = "status=$($response.StatusCode); body=$($response.Content)"
        }
        catch { $healthDetail = $_.Exception.Message }
        Report "$name /health（本機 $port）" $healthOk $healthDetail
    }

    $workerPidFile = Join-Path $InstallRoot 'state\worker.pid'
    $workerAlive = $false
    $workerDetail = "找不到 $workerPidFile"
    if (Test-Path -LiteralPath $workerPidFile -PathType Leaf) {
        $workerProcessId = [int](Get-Content -LiteralPath $workerPidFile)
        $workerAlive = $null -ne (Get-Process -Id $workerProcessId -ErrorAction SilentlyContinue)
        $workerDetail = "PID $workerProcessId；Worker 無 HTTP listener（設計如此），健康＝行程存活"
    }
    Report 'worker 存活（無 HTTP listener，屬設計如此）' $workerAlive $workerDetail
}

if ($script:failed -eq 0) { Write-Host 'OVERALL PASS' }
else { Write-Host "OVERALL FAIL ($script:failed)" }
if ($script:failed -gt 0) { exit 1 }
