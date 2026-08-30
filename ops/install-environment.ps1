<#
    GreyGray M-1 正式機環境整備。

    預設會修改「目前執行這支腳本的 Windows 主機」，不會搜尋或連線其他機器。
    -SimulationRoot 只供開發機驗證冪等控制流，所有狀態都寫在指定沙箱目錄。
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$InstallRoot = 'C:\GreyGray',
    [string]$PostgreSqlDataRoot = 'C:\GreyGray\PostgreSQL\data',
    [string]$PostgreSqlWalRoot = 'C:\GreyGray\PostgreSQL\wal',
    [string]$PostgreSqlZipPath,
    [string]$PostgreSqlZipSha256 = '6EABDF00D2893713B75DB4336A23C3FDF505F056E217EC6E2E95D901750CFEA3',
    [pscredential]$PostgresSuperuserCredential,
    [pscredential]$ServiceCredential,
    [securestring]$CloudflareTunnelToken,
    [string]$ProdMonitorConfigPath,
    [string]$SimulationRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$isSimulation = -not [string]::IsNullOrWhiteSpace($SimulationRoot)
$managedNames = @(
    'dotnet-sdk-10', 'dotnet-runtime-10', 'aspnet-runtime-10', 'postgresql-17',
    'postgres-data-c', 'postgres-wal-c', 'garnet', 'garnet-copy', 'node-22', 'cloudflared',
    'nssm', 'service-account', 'service-acl', 'defender-exclusion',
    'windows-update-manual', 'maintenance-window', 'prod-monitor',
    'GreyGray-Storefront', 'GreyGray-Admin', 'GreyGray-Worker',
    'GreyGray-Web-Storefront', 'GreyGray-Web-Admin'
)

function Resolve-WingetPath {
    <#
        winget 在 SSH 這種非互動 session 裡常常不在 PATH 上——
        App Installer 是把 winget.exe 放在使用者的 WindowsApps 別名目錄，
        而那個目錄只有互動登入的 session 才會被加進 PATH。
        YC 上實測過：Get-Command winget 找不到，但
        %LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe 確實存在（App Installer 1.29.290.0）。
        只靠 Get-Command 會讓整支安裝腳本在遠端執行時第一步就 throw。
    #>
    $cmd = Get-Command winget.exe -CommandType Application -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($candidate in @(
        (Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\winget.exe'),
        (Join-Path $env:ProgramFiles 'WindowsApps\winget.exe')
    )) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) { return $candidate }
    }
    return $null
}

function Write-Existing([string]$Name) { Write-Host "已存在 $Name" }
function Write-Installed([string]$Name) { Write-Host "已安裝 $Name" }

function Invoke-Simulation {
    $root = [System.IO.Path]::GetFullPath($SimulationRoot)
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
        New-Item -ItemType Directory -Path $root -Force | Out-Null
    }
    foreach ($name in $managedNames) {
        $marker = Join-Path $root ($name + '.installed')
        if (Test-Path -LiteralPath $marker -PathType Leaf) {
            Write-Existing $name
        }
        else {
            New-Item -ItemType File -Path $marker -Force | Out-Null
            Write-Installed $name
        }
    }
    Write-Host 'PASS 模擬模式：只寫 SimulationRoot，未修改套件、服務、帳號、ACL、Defender 或 Windows Update。'
}

if ($isSimulation) {
    Invoke-Simulation
    return
}

if ($env:OS -ne 'Windows_NT') {
    throw 'M-1 安裝腳本只能在 Windows 執行。'
}
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '請在正式機的系統管理員 PowerShell 執行。'
}
if ($null -eq $ServiceCredential) { throw '實際安裝必須提供專屬的 -ServiceCredential。' }
if ($null -eq $PostgresSuperuserCredential) { throw '實際安裝必須提供 -PostgresSuperuserCredential。' }
if ($null -eq $CloudflareTunnelToken) { throw '實際安裝必須提供 -CloudflareTunnelToken；腳本不猜 tunnel。' }
if ([string]::IsNullOrWhiteSpace($ProdMonitorConfigPath)) {
    throw '實際安裝必須提供 -ProdMonitorConfigPath；腳本不猜 prod-monitor 的正式設定檔。'
}
if ($ServiceCredential.UserName -notmatch '^\.\\(?<name>[^\\]+)$') {
    throw '-ServiceCredential 必須是專屬本機帳號，格式為 .\GreyGraySvc。'
}
$serviceUser = $Matches.name

<#
    ★ 這裡曾經硬寫 'C:\'，即使三個路徑都已經是參數也一樣會擋下——
    指到別的磁碟會直接 throw，參數形同虛設（docs/19 §5 BE-22 抓到的 bug）。
    改成「三者必須同一顆磁碟」：正式機三個參數預設都指 C:\，行為不變；
    其他磁碟只要三個路徑彼此一致（例如全部指到 D:\GreyGray）就放行。
#>
$installRootDrive = [System.IO.Path]::GetPathRoot([System.IO.Path]::GetFullPath($InstallRoot))
foreach ($path in @($PostgreSqlDataRoot, $PostgreSqlWalRoot)) {
    $full = [System.IO.Path]::GetFullPath($path)
    if ([System.IO.Path]::GetPathRoot($full) -ne $installRootDrive) {
        throw "PostgreSQL data／WAL 必須與 InstallRoot 同一顆磁碟（藍圖要求同一顆 NVMe）：$full 應與 $InstallRoot 同磁碟"
    }
}

function Invoke-Native {
    param([Parameter(Mandatory)][string]$FilePath, [Parameter(Mandatory)][string[]]$Arguments)
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$FilePath 結束碼 $LASTEXITCODE" }
}

function Update-ProcessPath {
    $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $user = [Environment]::GetEnvironmentVariable('Path', 'User')
    $env:Path = "$machine;$user"
}

function Install-WinGetPackage {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Id,
        [Parameter(Mandatory)][scriptblock]$IsInstalled,
        [string[]]$AdditionalArguments = @()
    )
    if (& $IsInstalled) { Write-Existing $Name; return }
    $winget = Resolve-WingetPath
    if (-not $winget) { throw "找不到 winget，無法安裝 $Name。" }
    $arguments = @('install', '--id', $Id, '--exact', '--silent', '--scope', 'machine',
        '--accept-source-agreements', '--accept-package-agreements', '--disable-interactivity') + $AdditionalArguments
    if ($PSCmdlet.ShouldProcess($Name, "winget install $Id")) { Invoke-Native $winget $arguments }
    Update-ProcessPath
    if (-not (& $IsInstalled)) { throw "$Name 安裝後仍無法驗證。請查看 winget log。" }
    Write-Installed $Name
}

Install-WinGetPackage 'dotnet-sdk-10' 'Microsoft.DotNet.SDK.10' {
    $sdks = if (Get-Command dotnet.exe -ErrorAction SilentlyContinue) { dotnet --list-sdks 2>$null } else { @() }
    @($sdks | Where-Object { $_ -match '^10\.' }).Count -gt 0
}
Install-WinGetPackage 'dotnet-runtime-10' 'Microsoft.DotNet.Runtime.10' {
    $runtimes = if (Get-Command dotnet.exe -ErrorAction SilentlyContinue) { dotnet --list-runtimes 2>$null } else { @() }
    @($runtimes | Where-Object { $_ -match '^Microsoft\.NETCore\.App 10\.' }).Count -gt 0
}
Install-WinGetPackage 'aspnet-runtime-10' 'Microsoft.DotNet.AspNetCore.10' {
    $runtimes = if (Get-Command dotnet.exe -ErrorAction SilentlyContinue) { dotnet --list-runtimes 2>$null } else { @() }
    @($runtimes | Where-Object { $_ -match '^Microsoft\.AspNetCore\.App 10\.' }).Count -gt 0
}
Install-WinGetPackage 'node-22' 'OpenJS.NodeJS.22' {
    $node = Get-Command node.exe -ErrorAction SilentlyContinue
    if (-not $node) { return $false }
    (& $node.Source --version 2>$null) -match '^v22\.'
}
Install-WinGetPackage 'cloudflared' 'Cloudflare.cloudflared' {
    $null -ne (Get-Command cloudflared.exe -ErrorAction SilentlyContinue)
}
Install-WinGetPackage 'nssm' 'NSSM.NSSM' {
    $null -ne (Get-Command nssm.exe -ErrorAction SilentlyContinue)
}
Install-WinGetPackage 'garnet' 'Microsoft.Garnet.DN8' {
    <# 只有 net8.0 這份能跑：net9.0 那份要 .NET 9 runtime，YC 上沒裝、也不打算裝。 #>
    $null -ne (Get-ChildItem -Path (Join-Path $env:ProgramFiles 'WinGet\Packages') -Filter 'Microsoft.Garnet.DN8_*' -Directory -ErrorAction SilentlyContinue)
}

foreach ($path in @($InstallRoot, $PostgreSqlDataRoot, $PostgreSqlWalRoot)) {
    if (Test-Path -LiteralPath $path -PathType Container) { Write-Existing $path }
    elseif ($PSCmdlet.ShouldProcess($path, '建立目錄')) { New-Item -ItemType Directory -Path $path -Force | Out-Null; Write-Installed $path }
}

$existingUser = Get-LocalUser -Name $serviceUser -ErrorAction SilentlyContinue
if ($null -eq $existingUser) {
    if ($PSCmdlet.ShouldProcess($serviceUser, '建立專屬 Windows 服務帳號')) {
        New-LocalUser -Name $serviceUser -Password $ServiceCredential.Password -PasswordNeverExpires -UserMayNotChangePassword | Out-Null
    }
    Write-Installed 'service-account'
}
else { Write-Existing 'service-account' }

<#
    ★ 這一步必須在任何東西被複製進 $InstallRoot 之前做。
    Set-Acl 只改 $InstallRoot 自己的 ACL 項目，ContainerInherit/ObjectInherit
    只對「之後新建」的子項目生效——已經存在的子項目不會被追溯套用。
    如果 PostgreSQL／Garnet 的檔案先解壓、ACL 後補，GreyGraySvc 對那些檔案
    會跟 nssm.exe 一樣連讀都讀不到，複製同一個坑兩次。
#>
$acl = Get-Acl -LiteralPath $InstallRoot
$identity = $ServiceCredential.UserName
$hasRule = @($acl.Access | Where-Object { $_.IdentityReference.Value -like "*$serviceUser" -and $_.FileSystemRights.ToString() -match 'Modify' }).Count -gt 0
if (-not $hasRule -and $PSCmdlet.ShouldProcess($InstallRoot, "授予 $identity Modify ACL")) {
    $rule = New-Object Security.AccessControl.FileSystemAccessRule($identity, 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.SetAccessRule($rule)
    Set-Acl -LiteralPath $InstallRoot -AclObject $acl
    Write-Installed 'service-acl'
}
else { Write-Existing 'service-acl' }

function Get-StableNssmPath {
    <#
        根因：winget 裝的 nssm.exe 是 C:\Program Files\WinGet\Links 底下的 symlink，
        指到 WinGet\Packages\...，那個檔案的 ACL 只開放安裝者本人／Administrators／SYSTEM。
        GreyGraySvc 開不了它——SCM 用服務帳號的 token 去 CreateProcess 這個 nssm.exe 時
        連檔案都讀不到，sc start 回錯誤 5（存取被拒，不是 1069 登入失敗）。
        複製一份到 $InstallRoot\bin，繼承上面剛授予的 GreyGraySvc ACL。
    #>
    param([Parameter(Mandatory)][string]$InstallRoot)
    $source = (Get-Command nssm.exe -CommandType Application -ErrorAction Stop).Source
    $stableDir = Join-Path $InstallRoot 'bin'
    $stablePath = Join-Path $stableDir 'nssm.exe'
    New-Item -ItemType Directory -Path $stableDir -Force | Out-Null
    $needsCopy = $true
    if (Test-Path -LiteralPath $stablePath -PathType Leaf) {
        $needsCopy = (Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $stablePath).Hash
    }
    if ($needsCopy) { Copy-Item -LiteralPath $source -Destination $stablePath -Force }
    return $stablePath
}
$nssm = Get-StableNssmPath -InstallRoot $InstallRoot

function Set-ServiceBinaryPath {
    <# 修正既有服務仍指著 WinGet Links shim 的情形（同一根因，補救既有安裝）。 #>
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$NssmPath)
    $config = Get-CimInstance Win32_Service -Filter "Name='$Name'" -ErrorAction SilentlyContinue
    if ($null -eq $config) { return }
    $desired = '"{0}"' -f $NssmPath
    if ($config.PathName -ne $desired) {
        Invoke-Native 'sc.exe' @('config', $Name, 'binPath=', $NssmPath)
    }
}

# PostgreSQL：EDB 的圖形安裝程式在 SSH 下會自我再提權一次，非互動 session 權限脈絡對不上，
# 連系統管理員都寫不進它自己建的 temp 目錄（docs/14 §8.2）。改用官方 ZIP binaries + 自己 initdb。
$postgresRoot = Join-Path $InstallRoot 'PostgreSQL'
$postgresExe = Join-Path $postgresRoot 'bin\postgres.exe'
if (Test-Path -LiteralPath $postgresExe -PathType Leaf) { Write-Existing 'postgresql-17' }
else {
    if ([string]::IsNullOrWhiteSpace($PostgreSqlZipPath)) {
        throw 'PostgreSQL 改用官方 ZIP binaries；必須提供 -PostgreSqlZipPath（來源見 docs/14-環境整備runbook.md §8.2）。'
    }
    $actualPgHash = (Get-FileHash -LiteralPath $PostgreSqlZipPath -Algorithm SHA256).Hash
    if ($actualPgHash -ne $PostgreSqlZipSha256) { throw "PostgreSQL zip SHA-256 不符：$actualPgHash" }
    if ($PSCmdlet.ShouldProcess($postgresRoot, '解壓官方 PostgreSQL ZIP binaries')) {
        New-Item -ItemType Directory -Path $postgresRoot -Force | Out-Null
        Expand-Archive -LiteralPath $PostgreSqlZipPath -DestinationPath $postgresRoot -Force
        $inner = Join-Path $postgresRoot 'pgsql'
        if (Test-Path -LiteralPath $inner -PathType Container) {
            Get-ChildItem -LiteralPath $inner -Force | Move-Item -Destination $postgresRoot -Force
            Remove-Item -LiteralPath $inner -Force -Recurse
        }
    }
    if (-not (Test-Path -LiteralPath $postgresExe -PathType Leaf)) { throw 'PostgreSQL zip 解壓後找不到 bin\postgres.exe。' }
    Write-Installed 'postgresql-17'
}

$pgVersionFile = Join-Path $PostgreSqlDataRoot 'PG_VERSION'
if (Test-Path -LiteralPath $pgVersionFile -PathType Leaf) { Write-Existing 'postgres-data-c' }
else {
    $pwFile = Join-Path ([IO.Path]::GetTempPath()) ('greygray-pg-pwfile-' + [guid]::NewGuid().ToString('N') + '.txt')
    try {
        # 密碼寫進 --pwfile 指定的檔案，不放進命令列——命令列會進 log、進事件檢視器、進 Win32_Process。
        $plainPostgresPassword = $PostgresSuperuserCredential.GetNetworkCredential().Password
        [IO.File]::WriteAllText($pwFile, $plainPostgresPassword, (New-Object Text.UTF8Encoding($false)))
        $plainPostgresPassword = $null
        $initdb = Join-Path $postgresRoot 'bin\initdb.exe'
        # 不加 -X：外置 WAL 目錄在 Windows 上用 junction 做，initdb 建 pg_wal\archive_status
        # 穿過 junction 會失敗（docs/14 §8.3）。拿掉它，pg_wal 留在 data\pg_wal 一樣在 C 槽。
        if ($PSCmdlet.ShouldProcess($PostgreSqlDataRoot, 'initdb（scram-sha-256）')) {
            Invoke-Native $initdb @('-D', $PostgreSqlDataRoot, '-U', 'postgres', '-A', 'scram-sha-256', '--pwfile', $pwFile, '-E', 'UTF8')
        }
    }
    finally {
        if (Test-Path -LiteralPath $pwFile) { Remove-Item -LiteralPath $pwFile -Force }
    }
    Write-Installed 'postgres-data-c'
}

$postgresConfPath = Join-Path $PostgreSqlDataRoot 'postgresql.conf'
if (Test-Path -LiteralPath $postgresConfPath -PathType Leaf) {
    $conf = [IO.File]::ReadAllText($postgresConfPath)
    $updated = $conf `
        -replace '(?m)^#?\s*listen_addresses\s*=.*$', "listen_addresses = 'localhost'" `
        -replace '(?m)^#?\s*port\s*=\s*\d+.*$', 'port = 5432'
    if ($updated -ne $conf) {
        [IO.File]::WriteAllText($postgresConfPath, $updated, (New-Object Text.UTF8Encoding($false)))
    }
}

<#
    WAL 不再搬到 $PostgreSqlWalRoot 建 junction。docs/14 §8.3 已經拍板：
    分離 WAL 目錄唯一的搭法是 initdb -X，而那個機制在 Windows 上會在
    建立 pg_wal\archive_status 時穿過剛建好的 junction 失敗。
    藍圖真正要的是「data 與 WAL 都在 C 槽 NVMe」——pg_wal 留在
    data\pg_wal 一樣在 C 槽，需求照樣滿足，還少一個會壞的機制，
    不必為了滿足一個字面上的「獨立目錄」去停服務搬一份正在用的 WAL。
    $PostgreSqlWalRoot 仍會建立空目錄（見上面的目錄建立迴圈）並被排進
    Defender exclusion，留著備用，但安裝腳本不會主動把 WAL 搬過去。
#>
$pgWalPath = Join-Path $PostgreSqlDataRoot 'pg_wal'
if (Test-Path -LiteralPath $pgWalPath -PathType Container) { Write-Existing 'postgres-wal-c' }

# Garnet（ADR-022，取代 Valkey）：Valkey 官方沒有原生 Windows 支援
# （<https://github.com/valkey-io/valkey/issues/92>），Garnet 有 winget 套件、RESP 相容。
# winget 裝的檔案一樣在 WinGet\Packages 底下，ACL 只給安裝者/Administrators/SYSTEM，
# 跟 nssm.exe 同一個坑：複製一份到 $InstallRoot\Garnet，讓 GreyGraySvc 讀得到。
$garnetRoot = Join-Path $InstallRoot 'Garnet'
$garnetExe = Join-Path $garnetRoot 'GarnetServer.exe'
if (Test-Path -LiteralPath $garnetExe -PathType Leaf) { Write-Existing 'garnet-copy' }
else {
    $garnetPackageDir = Get-ChildItem -Path (Join-Path $env:ProgramFiles 'WinGet\Packages') -Filter 'Microsoft.Garnet.DN8_*' -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $garnetPackageDir) { throw 'winget 裝好的 Garnet 套件目錄找不到，無法複製到可執行的位置。' }
    if ($PSCmdlet.ShouldProcess($garnetRoot, '複製 Garnet 套件內容到服務帳號讀得到的位置')) {
        New-Item -ItemType Directory -Path $garnetRoot -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $garnetPackageDir.FullName '*') -Destination $garnetRoot -Recurse -Force
    }
    if (-not (Test-Path -LiteralPath $garnetExe -PathType Leaf)) { throw 'Garnet 套件內容缺少 GarnetServer.exe。' }
    Write-Installed 'garnet-copy'
}

$manifest = & (Join-Path $PSScriptRoot 'service-manifest.ps1')
$plainServicePassword = $ServiceCredential.GetNetworkCredential().Password
foreach ($definition in $manifest.Services) {
    $service = Get-Service -Name $definition.Name -ErrorAction SilentlyContinue
    if ($null -eq $service) {
        if ($PSCmdlet.ShouldProcess($definition.Name, '以 NSSM 建立停用的待部署服務')) {
            Invoke-Native $nssm @('install', $definition.Name, $env:ComSpec)
            Invoke-Native $nssm @('set', $definition.Name, 'AppParameters', '/c', 'exit', '1')
            Invoke-Native $nssm @('set', $definition.Name, 'Start', 'SERVICE_DISABLED')
        }
        Write-Installed $definition.Name
    }
    else { Write-Existing $definition.Name }
    Set-ServiceBinaryPath -Name $definition.Name -NssmPath $nssm
    $serviceInfo = Get-CimInstance Win32_Service -Filter "Name='$($definition.Name)'"
    if ($serviceInfo.StartName -notlike "*$serviceUser") {
        Invoke-Native $nssm @('set', $definition.Name, 'ObjectName', $ServiceCredential.UserName, $plainServicePassword)
    }
}

if ($null -eq (Get-Service -Name 'GreyGray-Garnet' -ErrorAction SilentlyContinue)) {
    if ($PSCmdlet.ShouldProcess('GreyGray-Garnet', '以 NSSM 建立 Garnet 服務')) {
        Invoke-Native $nssm @('install', 'GreyGray-Garnet', $garnetExe)
        Invoke-Native $nssm @('set', 'GreyGray-Garnet', 'AppDirectory', $garnetRoot)
        Invoke-Native $nssm @('set', 'GreyGray-Garnet', 'AppParameters', '--bind', '127.0.0.1', '--port', '6379')
        Invoke-Native $nssm @('set', 'GreyGray-Garnet', 'Start', 'SERVICE_AUTO_START')
        Invoke-Native $nssm @('set', 'GreyGray-Garnet', 'ObjectName', $ServiceCredential.UserName, $plainServicePassword)
    }
    Write-Installed 'GreyGray-Garnet'
}
else {
    Write-Existing 'GreyGray-Garnet'
    Set-ServiceBinaryPath -Name 'GreyGray-Garnet' -NssmPath $nssm
}

if ($null -eq (Get-Service -Name 'GreyGray-PostgreSQL' -ErrorAction SilentlyContinue)) {
    if ($PSCmdlet.ShouldProcess('GreyGray-PostgreSQL', '以 NSSM 建立 PostgreSQL 服務')) {
        Invoke-Native $nssm @('install', 'GreyGray-PostgreSQL', $postgresExe)
        Invoke-Native $nssm @('set', 'GreyGray-PostgreSQL', 'AppDirectory', $postgresRoot)
        Invoke-Native $nssm @('set', 'GreyGray-PostgreSQL', 'AppParameters', '-D', $PostgreSqlDataRoot)
        Invoke-Native $nssm @('set', 'GreyGray-PostgreSQL', 'Start', 'SERVICE_AUTO_START')
        Invoke-Native $nssm @('set', 'GreyGray-PostgreSQL', 'ObjectName', $ServiceCredential.UserName, $plainServicePassword)
    }
    Write-Installed 'GreyGray-PostgreSQL'
}
else {
    Write-Existing 'GreyGray-PostgreSQL'
    Set-ServiceBinaryPath -Name 'GreyGray-PostgreSQL' -NssmPath $nssm
}

Start-Service -Name 'GreyGray-PostgreSQL'
(Get-Service -Name 'GreyGray-PostgreSQL').WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [timespan]::FromSeconds(30))
Start-Service -Name 'GreyGray-Garnet'
(Get-Service -Name 'GreyGray-Garnet').WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [timespan]::FromSeconds(30))

$defenderExclusions = @(Get-MpPreference).ExclusionPath
if ($defenderExclusions -contains $PostgreSqlDataRoot -and $defenderExclusions -contains $PostgreSqlWalRoot) {
    Write-Existing 'defender-exclusion'
}
elseif ($PSCmdlet.ShouldProcess('Microsoft Defender', '排除 PostgreSQL data 與 WAL')) {
    Add-MpPreference -ExclusionPath $PostgreSqlDataRoot, $PostgreSqlWalRoot
    Write-Installed 'defender-exclusion'
}

$auPath = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU'
New-Item -Path $auPath -Force | Out-Null
$noAutoUpdate = (Get-ItemProperty -Path $auPath -Name NoAutoUpdate -ErrorAction SilentlyContinue).NoAutoUpdate
if ($noAutoUpdate -eq 1) { Write-Existing 'windows-update-manual' }
elseif ($PSCmdlet.ShouldProcess('Windows Update', '改為人工更新')) {
    New-ItemProperty -Path $auPath -Name NoAutoUpdate -PropertyType DWord -Value 1 -Force | Out-Null
    Set-Service -Name wuauserv -StartupType Manual
    Write-Installed 'windows-update-manual'
}

$maintenanceTask = 'GreyGray-Windows-Maintenance-Reminder'
if (Get-ScheduledTask -TaskName $maintenanceTask -ErrorAction SilentlyContinue) { Write-Existing 'maintenance-window' }
elseif ($PSCmdlet.ShouldProcess($maintenanceTask, '建立每週日 03:00 維護提醒')) {
    $action = New-ScheduledTaskAction -Execute 'eventcreate.exe' -Argument '/T INFORMATION /ID 100 /L APPLICATION /SO GreyGray /D "GreyGray 每週維護窗：人工檢查並套用 Windows Update"'
    $trigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek Sunday -At 3am
    Register-ScheduledTask -TaskName $maintenanceTask -Action $action -Trigger $trigger -RunLevel Highest -Force | Out-Null
    Write-Installed 'maintenance-window'
}

$tokenPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($CloudflareTunnelToken)
$plainTunnelToken = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($tokenPointer)
try {
    if (Get-Service -Name 'cloudflared' -ErrorAction SilentlyContinue) { Write-Existing 'cloudflared-service' }
    elseif ($PSCmdlet.ShouldProcess('cloudflared', '安裝單一 Cloudflare Tunnel Windows service')) {
        Invoke-Native (Get-Command cloudflared.exe -ErrorAction Stop).Source @('service', 'install', $plainTunnelToken)
        Write-Installed 'cloudflared-service'
    }
    $cloudflaredInfo = Get-CimInstance Win32_Service -Filter "Name='cloudflared'"
    if ($cloudflaredInfo.StartName -notlike "*$serviceUser") {
        Invoke-Native $nssm @('set', 'cloudflared', 'ObjectName', $ServiceCredential.UserName, $plainServicePassword)
        Restart-Service -Name 'cloudflared'
    }
}
finally {
    $plainTunnelToken = $null
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($tokenPointer)
}
$plainServicePassword = $null

& (Join-Path $PSScriptRoot 'register-prod-monitor.ps1') -ConfigPath $ProdMonitorConfigPath
Write-Installed 'prod-monitor'

Write-Host 'PASS M-1 安裝完成。仍須依 runbook 人工確認有線網路與 UPS。'
