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
    [string]$NodePath,
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
. "$PSScriptRoot\lib\Node.ps1"
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
$nodeServices = @($manifest.Services | Where-Object { $_.Kind -eq 'NextStandalone' })
if ($nodeServices.Count -ne 2) { throw 'service manifest 必須正好有兩個 NextStandalone service。' }
$resolvedNodePath = Resolve-NodeExecutable -NodePath $NodePath
foreach ($definition in $manifest.Services) {
    $artifactDirectory = Join-Path $artifactFull $definition.ArtifactDirectory
    $entryPoint = [System.IO.Path]::GetFullPath((Join-Path $artifactDirectory $definition.ArtifactEntryPoint))
    if (-not (Test-PathWithinRoot -Path $entryPoint -Root $artifactDirectory)) {
        throw "artifact entrypoint 越出自己的目錄：$entryPoint"
    }
    if (-not (Test-Path -LiteralPath $entryPoint -PathType Leaf)) {
        throw "artifact 不完整，找不到：$entryPoint"
    }
}

if ($ValidateOnly) {
    if (-not $SkipMigrations) {
        & "$PSScriptRoot\invoke-migrations.ps1" -MigrationFiles $MigrationFiles `
            -MigrationCredential $MigrationCredential -DatabaseHost $DatabaseHost `
            -DatabasePort $DatabasePort -DatabaseName $DatabaseName -PsqlPath $PsqlPath -ValidateOnly
    }
    Write-Host "✓ deploy 參數驗證通過：3 個 win-x64 self-contained + 2 個 Next standalone；node.exe=$resolvedNodePath；未碰 NSSM、排程或 YC。"
    return
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '實際部署必須在系統管理員 PowerShell 執行。'
}
if (-not (Test-Path -LiteralPath $NssmPath -PathType Leaf)) { throw "找不到 NSSM：$NssmPath" }
$nodeVersion = Invoke-NativeCommand -FilePath $resolvedNodePath -ArgumentList @('--version') -EchoOutput
if ($nodeVersion.StdOut.Trim() -notmatch '^v(?<major>\d+)' -or [int]$Matches.major -lt 20) {
    throw "Node 必須 >= 20；目前輸出：$($nodeVersion.StdOut.Trim())"
}

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
        if ($definition.Kind -eq 'DotNet') {
            foreach ($process in @(Get-Process -Name $definition.ProcessName -ErrorAction SilentlyContinue)) {
                $token = Get-GreyGrayProcessToken -Process $process
                if ($token.Path -and (Test-PathWithinRoot -Path $token.Path -Root $installFull)) {
                    Add-Member -InputObject $token -NotePropertyName ServiceName -NotePropertyValue $definition.Name
                    $tokens += $token
                }
            }
        }
        if ([int]$definition.Port -gt 0) {
            foreach ($process in @(Get-PortOwnerProcess -Port ([int]$definition.Port))) {
                $token = Get-GreyGrayProcessToken -Process $process
                Add-Member -InputObject $token -NotePropertyName ServiceName -NotePropertyValue $definition.Name
                if (-not ($tokens | Where-Object { $_.Id -eq $token.Id -and $_.StartTimeUtc -eq $token.StartTimeUtc })) {
                    $tokens += $token
                }
            }
        }
    }
    return $tokens
}

function Test-DeploymentTokenOwnership {
    param([Parameter(Mandatory)]$Token)

    $definition = $manifest.Services | Where-Object { $_.Name -eq $Token.ServiceName } | Select-Object -First 1
    if ($null -eq $definition) { return $false }
    if ($definition.Kind -eq 'DotNet') {
        return $Token.Path -and (Test-PathWithinRoot -Path $Token.Path -Root $installFull)
    }

    # Node 在 InstallRoot 外；只有命令列精確含某個已安裝 release 的 server.js 才能認領。
    $releasesRoot = Join-Path $installFull 'releases'
    foreach ($release in @(Get-ChildItem -LiteralPath $releasesRoot -Directory -ErrorAction SilentlyContinue)) {
        $entryPoint = Join-Path (Join-Path $release.FullName $definition.ArtifactDirectory) $definition.ArtifactEntryPoint
        if ((Test-Path -LiteralPath $entryPoint -PathType Leaf) -and
            (Test-NodeProcessIdentity -Token $Token -NodePath $resolvedNodePath -EntryPointPath $entryPoint)) {
            return $true
        }
    }
    return $false
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
Wait-ProcessTokensExit -Tokens $oldTokens -InstallRoot $installFull -TimeoutSeconds 30 `
    -OwnershipValidator { param($token) Test-DeploymentTokenOwnership -Token $token }
foreach ($definition in $manifest.Services) {
    if ([int]$definition.Port -gt 0) {
        $portDefinition = $definition
        $portOwnershipValidator = {
            param($token)
            Add-Member -InputObject $token -NotePropertyName ServiceName -NotePropertyValue $portDefinition.Name
            Test-DeploymentTokenOwnership -Token $token
        }.GetNewClosure()
        Assert-PortReleased -Port ([int]$definition.Port) -InstallRoot $installFull `
            -OwnershipValidator $portOwnershipValidator
    }
}

$servicePassword = $ServiceCredential.GetNetworkCredential().Password
try {
    foreach ($definition in $manifest.Services) {
        $service = Get-Service -Name $definition.Name -ErrorAction SilentlyContinue
        $artifactDirectory = Join-Path $releaseRoot $definition.ArtifactDirectory
        $entryPoint = Join-Path $artifactDirectory $definition.ArtifactEntryPoint
        $appDirectory = Join-Path $artifactDirectory $definition.WorkingDirectory
        $executable = if ($definition.Kind -eq 'NextStandalone') { $resolvedNodePath } else { $entryPoint }
        $applicationArguments = @()
        if ($definition.Kind -eq 'NextStandalone') {
            $applicationArguments = @($entryPoint)
        }
        else {
            $applicationArguments = @($definition.Arguments)
        }
        if ($null -eq $service) {
            Invoke-Nssm -Arguments @('install', $definition.Name, $executable) | Out-Null
        }
        Invoke-Nssm -Arguments @('set', $definition.Name, 'Application', $executable) | Out-Null
        Invoke-Nssm -Arguments @('set', $definition.Name, 'AppDirectory', $appDirectory) | Out-Null
        if ($applicationArguments.Count -gt 0) {
            Invoke-Nssm -Arguments (@('set', $definition.Name, 'AppParameters') + $applicationArguments) | Out-Null
        }
        else {
            Invoke-Nssm -Arguments @('reset', $definition.Name, 'AppParameters') | Out-Null
        }
        Invoke-Nssm -Arguments @('set', $definition.Name, 'Start', 'SERVICE_AUTO_START') | Out-Null
        Invoke-Nssm -Arguments @('set', $definition.Name, 'AppExit', 'Default', 'Restart') | Out-Null
        Invoke-Nssm -Arguments @('set', $definition.Name, 'AppRestartDelay', '60000') | Out-Null
        Invoke-Nssm -Arguments @('set', $definition.Name, 'AppThrottle', '1500') | Out-Null
        Invoke-Nssm -Arguments @('set', $definition.Name, 'AppStdout', (Join-Path $logsRoot "$($definition.Name).stdout.log")) | Out-Null
        Invoke-Nssm -Arguments @('set', $definition.Name, 'AppStderr', (Join-Path $logsRoot "$($definition.Name).stderr.log")) | Out-Null
        Invoke-Nssm -Arguments @('set', $definition.Name, 'AppRotateFiles', '1') | Out-Null
        Invoke-Nssm -Arguments @('set', $definition.Name, 'AppRotateBytes', '10485760') | Out-Null
        if ($definition.Kind -eq 'NextStandalone') {
            $environmentArguments = @('set', $definition.Name, 'AppEnvironmentExtra',
                'NODE_ENV=production', "PORT=$($definition.Port)", 'HOSTNAME=127.0.0.1')
        }
        else {
            $environmentArguments = @('set', $definition.Name, 'AppEnvironmentExtra', 'DOTNET_ENVIRONMENT=Production')
        }
        if ($definition.Kind -eq 'DotNet' -and [int]$definition.Port -gt 0) {
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
    $expectedEntryPoint = Join-Path $expectedRoot $Definition.ArtifactEntryPoint
    $deadline = [datetime]::UtcNow.AddSeconds($HealthTimeoutSeconds)
    do {
        $candidates = @()
        if ([int]$Definition.Port -gt 0) {
            $candidates = @(Get-PortOwnerProcess -Port ([int]$Definition.Port))
        }
        else {
            $candidates = @(Get-Process -Name $Definition.ProcessName -ErrorAction SilentlyContinue)
        }

        foreach ($process in $candidates) {
            $token = Get-GreyGrayProcessToken -Process $process
            if (-not (Test-ProcessStartedAfter -ProcessStartTime $process.StartTime -RestartBoundaryUtc $restartBoundaryUtc)) {
                throw "$($Definition.Name) 由舊 PID $($process.Id) 回應；StartTime 不晚於本次重啟，拒絕假綠。"
            }
            if ($Definition.Kind -eq 'NextStandalone') {
                if (-not (Test-NodeProcessIdentity -Token $token -NodePath $resolvedNodePath `
                        -EntryPointPath $expectedEntryPoint)) {
                    throw "$($Definition.Name) PID $($process.Id) 不是可信 node.exe 或沒有執行本次 release 的 server.js：$expectedEntryPoint"
                }
            }
            elseif (-not $token.Path -or -not (Test-PathWithinRoot -Path $token.Path -Root $expectedRoot)) {
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
