<#
    YC 原生部署：已在開發機產出的 win-x64 self-contained artifacts → 版本化 release → NSSM。
    本腳本不做 build（正式機不得建置），也不會把 migration 高權限帳密寫進任何持久設定。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArtifactRoot,
    [Parameter(Mandatory)][string]$InstallRoot,
    [Parameter(Mandatory)][string]$NssmPath,
    [Parameter(Mandatory)][pscredential]$ServiceCredential,
    [pscredential]$MigrationCredential,
    [string[]]$MigrationFiles = @(),
    [switch]$SkipMigrations,
    [string]$DatabaseHost = '127.0.0.1',
    [ValidateRange(1, 65535)][int]$DatabasePort = 5432,
    [string]$DatabaseName = 'greygray',
    [string]$PsqlPath = 'psql.exe',
    [string]$WatchdogTaskName = 'GreyGray-Watchdog',
    [string]$WatchdogUserSid,
    [ValidateRange(5, 180)][int]$HealthTimeoutSeconds = 60,
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\lib\Process.ps1"
. "$PSScriptRoot\lib\Deployment.ps1"
$manifestPath = "$PSScriptRoot\service-manifest.ps1"
$manifest = & $manifestPath

if ($SkipMigrations -and $MigrationFiles.Count -gt 0) { throw '-SkipMigrations 與 -MigrationFiles 不可同時使用。' }
if (-not $SkipMigrations) {
    if ($null -eq $MigrationCredential) { throw '未帶 -SkipMigrations 時，必須提供部署專用 -MigrationCredential。' }
    if ($MigrationFiles.Count -eq 0) { throw '未帶 -SkipMigrations 時，必須明確列出 -MigrationFiles；腳本不猜哪些 migration 已套用。' }
}

$artifactFull = [System.IO.Path]::GetFullPath($ArtifactRoot)
$installFull = [System.IO.Path]::GetFullPath($InstallRoot)
if ($artifactFull.Equals($installFull, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'ArtifactRoot 與 InstallRoot 不可相同。'
}

$serviceNames = @($manifest.Services | ForEach-Object { $_.Name })
if (($serviceNames | Select-Object -Unique).Count -ne $serviceNames.Count) { throw 'service manifest 有重複的 Name。' }
foreach ($definition in $manifest.Services) {
    $artifactDirectory = Join-Path $artifactFull $definition.ArtifactDirectory
    $executable = Join-Path $artifactDirectory $definition.Executable
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "self-contained artifact 不完整，找不到：$executable"
    }
}

if ($ValidateOnly) {
    if (-not $SkipMigrations) {
        & "$PSScriptRoot\invoke-migrations.ps1" -MigrationFiles $MigrationFiles `
            -MigrationCredential $MigrationCredential -DatabaseHost $DatabaseHost `
            -DatabasePort $DatabasePort -DatabaseName $DatabaseName -PsqlPath $PsqlPath -ValidateOnly
    }
    Write-Host "✓ deploy 參數驗證通過：$($manifest.Services.Count) 個 win-x64 self-contained service；未碰 NSSM、排程或 YC。"
    return
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '實際部署必須在系統管理員 PowerShell 執行。'
}
if (-not (Test-Path -LiteralPath $NssmPath -PathType Leaf)) { throw "找不到 NSSM：$NssmPath" }

$releaseId = [datetime]::UtcNow.ToString('yyyyMMddHHmmssfff')
$releaseRoot = Join-Path $installFull "releases\$releaseId"
$logsRoot = Join-Path $installFull 'logs'
$installedOpsRoot = Join-Path $installFull 'ops'
New-Item -ItemType Directory -Path $releaseRoot, $logsRoot, $installedOpsRoot -Force | Out-Null

foreach ($definition in $manifest.Services) {
    Copy-Item -LiteralPath (Join-Path $artifactFull $definition.ArtifactDirectory) `
        -Destination (Join-Path $releaseRoot $definition.ArtifactDirectory) -Recurse -Force
}
Copy-Item -LiteralPath "$PSScriptRoot\watchdog.ps1", $manifestPath -Destination $installedOpsRoot -Force

# migration 先於停服；失敗時舊版仍繼續服務。高權限帳號只活在此子呼叫。
if (-not $SkipMigrations) {
    & "$PSScriptRoot\invoke-migrations.ps1" -MigrationFiles $MigrationFiles `
        -MigrationCredential $MigrationCredential -DatabaseHost $DatabaseHost `
        -DatabasePort $DatabasePort -DatabaseName $DatabaseName -PsqlPath $PsqlPath
}

function Invoke-Nssm {
    param([string[]]$Arguments, [switch]$AllowNonZeroExit)
    return Invoke-NativeCommand -FilePath $NssmPath -ArgumentList $Arguments `
        -AllowNonZeroExit:$AllowNonZeroExit -EchoOutput
}

function Get-ManagedApplicationTokens {
    $tokens = @()
    foreach ($definition in $manifest.Services) {
        $processName = [System.IO.Path]::GetFileNameWithoutExtension($definition.Executable)
        foreach ($process in @(Get-Process -Name $processName -ErrorAction SilentlyContinue)) {
            $token = Get-GreyGrayProcessToken -Process $process
            if ($token.Path -and (Test-PathWithinRoot -Path $token.Path -Root $installFull)) { $tokens += $token }
        }
        if ([int]$definition.Port -gt 0) {
            foreach ($process in @(Get-PortOwnerProcess -Port ([int]$definition.Port))) {
                $token = Get-GreyGrayProcessToken -Process $process
                if (-not ($tokens | Where-Object { $_.Id -eq $token.Id -and $_.StartTimeUtc -eq $token.StartTimeUtc })) {
                    $tokens += $token
                }
            }
        }
    }
    return $tokens
}

$oldTokens = @(Get-ManagedApplicationTokens)
foreach ($definition in $manifest.Services) {
    $service = Get-Service -Name $definition.Name -ErrorAction SilentlyContinue
    if ($null -ne $service -and $service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        Invoke-Nssm -Arguments @('stop', $definition.Name) -AllowNonZeroExit | Out-Null
        $service.Refresh()
        $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [timespan]::FromSeconds(30))
    }
}

# 不能只相信 NSSM/排程狀態：逐一驗證舊 PID 與 port holder 真的消失。
Wait-ProcessTokensExit -Tokens $oldTokens -InstallRoot $installFull -TimeoutSeconds 30
foreach ($definition in $manifest.Services) {
    if ([int]$definition.Port -gt 0) {
        Assert-PortReleased -Port ([int]$definition.Port) -InstallRoot $installFull
    }
}

$servicePassword = $ServiceCredential.GetNetworkCredential().Password
try {
    foreach ($definition in $manifest.Services) {
        $service = Get-Service -Name $definition.Name -ErrorAction SilentlyContinue
        $executable = Join-Path (Join-Path $releaseRoot $definition.ArtifactDirectory) $definition.Executable
        $appDirectory = Split-Path -Parent $executable
        if ($null -eq $service) {
            Invoke-Nssm -Arguments @('install', $definition.Name, $executable) | Out-Null
        }
        Invoke-Nssm -Arguments @('set', $definition.Name, 'Application', $executable) | Out-Null
        Invoke-Nssm -Arguments @('set', $definition.Name, 'AppDirectory', $appDirectory) | Out-Null
        Invoke-Nssm -Arguments @('set', $definition.Name, 'Start', 'SERVICE_AUTO_START') | Out-Null
        Invoke-Nssm -Arguments @('set', $definition.Name, 'AppExit', 'Default', 'Restart') | Out-Null
        Invoke-Nssm -Arguments @('set', $definition.Name, 'AppRestartDelay', '60000') | Out-Null
        Invoke-Nssm -Arguments @('set', $definition.Name, 'AppThrottle', '1500') | Out-Null
        Invoke-Nssm -Arguments @('set', $definition.Name, 'AppStdout', (Join-Path $logsRoot "$($definition.Name).stdout.log")) | Out-Null
        Invoke-Nssm -Arguments @('set', $definition.Name, 'AppStderr', (Join-Path $logsRoot "$($definition.Name).stderr.log")) | Out-Null
        Invoke-Nssm -Arguments @('set', $definition.Name, 'AppRotateFiles', '1') | Out-Null
        Invoke-Nssm -Arguments @('set', $definition.Name, 'AppRotateBytes', '10485760') | Out-Null
        $environmentArguments = @('set', $definition.Name, 'AppEnvironmentExtra', 'DOTNET_ENVIRONMENT=Production')
        if ([int]$definition.Port -gt 0) {
            $environmentArguments += 'ASPNETCORE_ENVIRONMENT=Production'
            # Cloudflare Tunnel 與 health probe 都在本機；不要把 BFF listener 暴露到 LAN。
            $environmentArguments += "ASPNETCORE_URLS=http://127.0.0.1:$($definition.Port)"
        }
        Invoke-Nssm -Arguments $environmentArguments | Out-Null
        # NSSM API 只能收明文密碼；只存在目前 process 記憶體與短暫子程序命令列，不落部署設定檔。
        Invoke-Nssm -Arguments @('set', $definition.Name, 'ObjectName', $ServiceCredential.UserName, $servicePassword) | Out-Null
    }
}
finally {
    $servicePassword = $null
}

$restartBoundaryUtc = [datetime]::UtcNow
foreach ($definition in $manifest.Services) {
    Invoke-Nssm -Arguments @('start', $definition.Name) | Out-Null
}

function Assert-NewApplicationProcess {
    param($Definition)

    $expectedRoot = Join-Path $releaseRoot $Definition.ArtifactDirectory
    $deadline = [datetime]::UtcNow.AddSeconds($HealthTimeoutSeconds)
    do {
        $candidates = @()
        if ([int]$Definition.Port -gt 0) {
            $candidates = @(Get-PortOwnerProcess -Port ([int]$Definition.Port))
        }
        else {
            $processName = [System.IO.Path]::GetFileNameWithoutExtension($Definition.Executable)
            $candidates = @(Get-Process -Name $processName -ErrorAction SilentlyContinue)
        }

        foreach ($process in $candidates) {
            $token = Get-GreyGrayProcessToken -Process $process
            if (-not (Test-ProcessStartedAfter -ProcessStartTime $process.StartTime -RestartBoundaryUtc $restartBoundaryUtc)) {
                throw "$($Definition.Name) 由舊 PID $($process.Id) 回應；StartTime 不晚於本次重啟，拒絕假綠。"
            }
            if (-not $token.Path -or -not (Test-PathWithinRoot -Path $token.Path -Root $expectedRoot)) {
                throw "$($Definition.Name) PID $($process.Id) 並非從本次 release 啟動：$($token.Path)"
            }

            if ([int]$Definition.Port -eq 0) {
                Write-Host "✓ $($Definition.Name) PID $($process.Id)，StartTime=$($process.StartTime.ToString('o'))"
                return
            }

            try {
                $healthUrl = "http://127.0.0.1:$($Definition.Port)$($Definition.HealthPath)"
                $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 5
                if ($response.StatusCode -eq 200) {
                    Write-Host "✓ $($Definition.Name) $healthUrl → 200，PID $($process.Id)，StartTime=$($process.StartTime.ToString('o'))"
                    return
                }
            }
            catch { }
        }
        Start-Sleep -Milliseconds 500
    } while ([datetime]::UtcNow -lt $deadline)

    throw "$($Definition.Name) 未在 $HealthTimeoutSeconds 秒內由本次 release 接手。"
}

foreach ($definition in $manifest.Services) { Assert-NewApplicationProcess -Definition $definition }

if (-not $WatchdogUserSid) {
    $WatchdogUserSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
}
$watchdogScript = Join-Path $installedOpsRoot 'watchdog.ps1'
$installedManifest = Join-Path $installedOpsRoot 'service-manifest.ps1'
$startBoundary = [datetime]::Now.AddMinutes(1).ToString('s')
$escapedScript = [Security.SecurityElement]::Escape($watchdogScript)
$escapedManifest = [Security.SecurityElement]::Escape($installedManifest)
$escapedSid = [Security.SecurityElement]::Escape($WatchdogUserSid)
$xml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <Triggers>
    <BootTrigger><Enabled>true</Enabled></BootTrigger>
    <TimeTrigger><Repetition><Interval>PT5M</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition><StartBoundary>$startBoundary</StartBoundary><Enabled>true</Enabled></TimeTrigger>
  </Triggers>
  <Principals><Principal id="Author"><UserId>$escapedSid</UserId><LogonType>S4U</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT5M</ExecutionTimeLimit>
    <RestartOnFailure><Interval>PT1M</Interval><Count>999</Count></RestartOnFailure><Enabled>true</Enabled>
  </Settings>
  <Actions Context="Author"><Exec><Command>powershell.exe</Command><Arguments>-NoProfile -ExecutionPolicy Bypass -File &quot;$escapedScript&quot; -ManifestPath &quot;$escapedManifest&quot;</Arguments></Exec></Actions>
</Task>
"@
Register-ScheduledTask -TaskName $WatchdogTaskName -Xml $xml -Force | Out-Null

Write-Host "✓ 部署完成：$releaseRoot"
Write-Host "✓ NSSM 自動重啟 + S4U BootTrigger / 每 5 分 watchdog / RestartOnFailure 999 已登記"
