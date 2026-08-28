Set-StrictMode -Version Latest

function Test-PathWithinRoot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Root
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $fullRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    return $fullPath.Equals($fullRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($fullRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-ProcessStartedAfter {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][datetime]$ProcessStartTime,
        [Parameter(Mandatory)][datetime]$RestartBoundaryUtc
    )

    return $ProcessStartTime.ToUniversalTime() -gt $RestartBoundaryUtc.ToUniversalTime()
}

function Get-GreyGrayProcessToken {
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Diagnostics.Process]$Process)

    $path = $null
    try { $path = $Process.Path } catch { }
    return [pscustomobject]@{
        Id = $Process.Id
        StartTimeUtc = $Process.StartTime.ToUniversalTime()
        Path = $path
    }
}

function Test-SameProcessTokenIsAlive {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Token)

    $current = Get-Process -Id $Token.Id -ErrorAction SilentlyContinue
    if ($null -eq $current) { return $false }
    try {
        return $current.StartTime.ToUniversalTime() -eq ([datetime]$Token.StartTimeUtc).ToUniversalTime()
    }
    catch {
        return $true
    }
}

function Get-PortOwnerProcess {
    [CmdletBinding()]
    param([Parameter(Mandatory)][int]$Port)

    $connections = @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)
    if ($connections.Count -eq 0) { return @() }

    $processes = @()
    foreach ($pidValue in ($connections.OwningProcess | Sort-Object -Unique)) {
        $process = Get-Process -Id $pidValue -ErrorAction SilentlyContinue
        if ($null -ne $process) { $processes += $process }
    }
    return $processes
}

function Wait-ProcessTokensExit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object[]]$Tokens,
        [Parameter(Mandatory)][string]$InstallRoot,
        [int]$TimeoutSeconds = 30
    )

    $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $alive = @($Tokens | Where-Object { Test-SameProcessTokenIsAlive -Token $_ })
        if ($alive.Count -eq 0) { return }
        Start-Sleep -Milliseconds 250
    } while ([datetime]::UtcNow -lt $deadline)

    foreach ($token in $alive) {
        if (-not $token.Path -or -not (Test-PathWithinRoot -Path $token.Path -Root $InstallRoot)) {
            throw "舊 PID $($token.Id) 仍存活，且無法證明它屬於 $InstallRoot；拒絕強制終止。"
        }
        Stop-Process -Id $token.Id -Force -ErrorAction Stop
    }

    Start-Sleep -Milliseconds 500
    $stillAlive = @($alive | Where-Object { Test-SameProcessTokenIsAlive -Token $_ })
    if ($stillAlive.Count -gt 0) {
        throw "舊程序未終止：$($stillAlive.Id -join ', ')。"
    }
}

function Assert-PortReleased {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int]$Port,
        [Parameter(Mandatory)][string]$InstallRoot
    )

    $owners = @(Get-PortOwnerProcess -Port $Port)
    if ($owners.Count -eq 0) { return }

    foreach ($owner in $owners) {
        $token = Get-GreyGrayProcessToken -Process $owner
        if (-not $token.Path -or -not (Test-PathWithinRoot -Path $token.Path -Root $InstallRoot)) {
            throw "port $Port 仍由外部 PID $($token.Id) 佔用；拒絕誤殺非 GreyGray 程序。"
        }
        Stop-Process -Id $token.Id -Force -ErrorAction Stop
    }

    Start-Sleep -Milliseconds 500
    if (@(Get-PortOwnerProcess -Port $Port).Count -ne 0) {
        throw "port $Port 的舊 listener 未真正釋放。"
    }
}
