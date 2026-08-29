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
    [string]$ValkeyArchivePath,
    [string]$ValkeyArchiveSha256,
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
    'postgres-data-c', 'postgres-wal-c', 'valkey', 'valkey-config', 'node-22', 'cloudflared',
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

foreach ($path in @($InstallRoot, $PostgreSqlDataRoot, $PostgreSqlWalRoot)) {
    $full = [System.IO.Path]::GetFullPath($path)
    if ([System.IO.Path]::GetPathRoot($full) -ne 'C:\') {
        throw "M-1 要求 PostgreSQL 與 GreyGray runtime 位於 C 槽 NVMe：$full"
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

# PostgreSQL 的 EDB 安裝器需要在第一次建立 cluster 時取得 superuser 密碼。
$postgresService = Get-Service -Name 'postgresql-x64-17' -ErrorAction SilentlyContinue
if ($null -eq $postgresService) {
    $plainPostgresPassword = $PostgresSuperuserCredential.GetNetworkCredential().Password
    try {
        $override = '--mode unattended --unattendedmodeui none --servicename postgresql-x64-17 --serverport 5432 --datadir "{0}" --superpassword "{1}"' -f $PostgreSqlDataRoot, $plainPostgresPassword.Replace('"', '\"')
        Install-WinGetPackage 'postgresql-17' 'PostgreSQL.PostgreSQL.17' {
            $null -ne (Get-Service -Name 'postgresql-x64-17' -ErrorAction SilentlyContinue)
        } @('--override', $override)
    }
    finally { $plainPostgresPassword = $null }
}
else { Write-Existing 'postgresql-17' }

foreach ($path in @($InstallRoot, $PostgreSqlDataRoot, $PostgreSqlWalRoot)) {
    if (Test-Path -LiteralPath $path -PathType Container) { Write-Existing $path }
    elseif ($PSCmdlet.ShouldProcess($path, '建立目錄')) { New-Item -ItemType Directory -Path $path -Force | Out-Null; Write-Installed $path }
}

$pgWalLink = Join-Path $PostgreSqlDataRoot 'pg_wal'
if (Test-Path -LiteralPath $pgWalLink) {
    $item = Get-Item -LiteralPath $pgWalLink -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
        Write-Existing 'postgres-wal-c'
    }
    else {
        if (@(Get-ChildItem -LiteralPath $PostgreSqlWalRoot -Force -ErrorAction SilentlyContinue).Count -gt 0) {
            throw "$PostgreSqlWalRoot 非空且 $pgWalLink 不是 junction；來源不唯一，拒絕搬動 WAL。"
        }
        if ($postgresService.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
            Stop-Service -Name $postgresService.Name -Force
            $postgresService.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [timespan]::FromSeconds(30))
        }
        if ($PSCmdlet.ShouldProcess($pgWalLink, "搬到 $PostgreSqlWalRoot 並建立 junction")) {
            $moved = @()
            try {
                foreach ($child in Get-ChildItem -LiteralPath $pgWalLink -Force) {
                    Move-Item -LiteralPath $child.FullName -Destination $PostgreSqlWalRoot
                    $moved += $child.Name
                }
                Remove-Item -LiteralPath $pgWalLink -Force
                New-Item -ItemType Junction -Path $pgWalLink -Target $PostgreSqlWalRoot | Out-Null
            }
            catch {
                if (-not (Test-Path -LiteralPath $pgWalLink)) { New-Item -ItemType Directory -Path $pgWalLink -Force | Out-Null }
                foreach ($name in $moved) {
                    $movedPath = Join-Path $PostgreSqlWalRoot $name
                    if (Test-Path -LiteralPath $movedPath) { Move-Item -LiteralPath $movedPath -Destination $pgWalLink }
                }
                throw
            }
            Write-Installed 'postgres-wal-c'
        }
    }
}
else {
    if ($postgresService.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        Stop-Service -Name $postgresService.Name -Force
        $postgresService.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [timespan]::FromSeconds(30))
    }
    if ($PSCmdlet.ShouldProcess($pgWalLink, "建立指向 $PostgreSqlWalRoot 的 junction")) {
        New-Item -ItemType Junction -Path $pgWalLink -Target $PostgreSqlWalRoot | Out-Null
        Write-Installed 'postgres-wal-c'
    }
}

# Valkey 官方未提供 Windows binary；只接受人工審核過且帶 SHA-256 的壓縮檔。
$valkeyRoot = Join-Path $InstallRoot 'runtime\valkey'
$valkeyServer = Join-Path $valkeyRoot 'valkey-server.exe'
$valkeyData = Join-Path $InstallRoot 'data\valkey'
$valkeyConfig = Join-Path $valkeyRoot 'valkey.conf'
if (Test-Path -LiteralPath $valkeyServer -PathType Leaf) { Write-Existing 'valkey' }
else {
    if ([string]::IsNullOrWhiteSpace($ValkeyArchivePath) -or [string]::IsNullOrWhiteSpace($ValkeyArchiveSha256)) {
        throw 'Valkey 官方不支援原生 Windows；必須人工核准來源後提供 -ValkeyArchivePath 與 -ValkeyArchiveSha256。'
    }
    $actualHash = (Get-FileHash -LiteralPath $ValkeyArchivePath -Algorithm SHA256).Hash
    if ($actualHash -ne $ValkeyArchiveSha256) { throw "Valkey archive SHA-256 不符：$actualHash" }
    if ($PSCmdlet.ShouldProcess($valkeyRoot, '解壓已核准的 Valkey Windows artifact')) {
        New-Item -ItemType Directory -Path $valkeyRoot -Force | Out-Null
        Expand-Archive -LiteralPath $ValkeyArchivePath -DestinationPath $valkeyRoot -Force
    }
    if (-not (Test-Path -LiteralPath $valkeyServer -PathType Leaf)) { throw 'Valkey archive 缺少 valkey-server.exe。' }
    Write-Installed 'valkey'
}
New-Item -ItemType Directory -Path $valkeyData -Force | Out-Null
$desiredValkeyConfig = "bind 127.0.0.1`r`nprotected-mode yes`r`nport 6379`r`ndir $($valkeyData.Replace('\','/'))`r`nappendonly yes`r`n"
$currentValkeyConfig = if (Test-Path -LiteralPath $valkeyConfig -PathType Leaf) { [IO.File]::ReadAllText($valkeyConfig) } else { '' }
if ($currentValkeyConfig -eq $desiredValkeyConfig) { Write-Existing 'valkey-config' }
elseif ($PSCmdlet.ShouldProcess($valkeyConfig, '寫入 localhost-only Valkey 設定')) {
    [IO.File]::WriteAllText($valkeyConfig, $desiredValkeyConfig, (New-Object Text.UTF8Encoding($false)))
    Write-Installed 'valkey-config'
}

$existingUser = Get-LocalUser -Name $serviceUser -ErrorAction SilentlyContinue
if ($null -eq $existingUser) {
    if ($PSCmdlet.ShouldProcess($serviceUser, '建立專屬 Windows 服務帳號')) {
        New-LocalUser -Name $serviceUser -Password $ServiceCredential.Password -PasswordNeverExpires -UserMayNotChangePassword | Out-Null
    }
    Write-Installed 'service-account'
}
else { Write-Existing 'service-account' }

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

$nssm = (Get-Command nssm.exe -ErrorAction Stop).Source
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
    $serviceInfo = Get-CimInstance Win32_Service -Filter "Name='$($definition.Name)'"
    if ($serviceInfo.StartName -notlike "*$serviceUser") {
        Invoke-Native $nssm @('set', $definition.Name, 'ObjectName', $ServiceCredential.UserName, $plainServicePassword)
    }
}

$valkeyService = Get-Service -Name 'GreyGray-Valkey' -ErrorAction SilentlyContinue
if ($null -eq $valkeyService) {
    if ($PSCmdlet.ShouldProcess('GreyGray-Valkey', '以 NSSM 建立 Valkey 服務')) {
        Invoke-Native $nssm @('install', 'GreyGray-Valkey', $valkeyServer)
        Invoke-Native $nssm @('set', 'GreyGray-Valkey', 'AppDirectory', $valkeyRoot)
        Invoke-Native $nssm @('set', 'GreyGray-Valkey', 'AppParameters', $valkeyConfig)
        Invoke-Native $nssm @('set', 'GreyGray-Valkey', 'Start', 'SERVICE_AUTO_START')
        Invoke-Native $nssm @('set', 'GreyGray-Valkey', 'ObjectName', $ServiceCredential.UserName, $plainServicePassword)
    }
    Write-Installed 'GreyGray-Valkey'
}
else { Write-Existing 'GreyGray-Valkey' }

$postgresInfo = Get-CimInstance Win32_Service -Filter "Name='postgresql-x64-17'"
if ($postgresInfo.StartName -notlike "*$serviceUser") {
    Invoke-Native $nssm @('set', 'postgresql-x64-17', 'ObjectName', $ServiceCredential.UserName, $plainServicePassword)
}
Start-Service -Name 'postgresql-x64-17'
(Get-Service -Name 'postgresql-x64-17').WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [timespan]::FromSeconds(30))
Start-Service -Name 'GreyGray-Valkey'
(Get-Service -Name 'GreyGray-Valkey').WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [timespan]::FromSeconds(30))

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
