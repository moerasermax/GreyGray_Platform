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
    $commandLine = $null
    try {
        $cimProcess = Get-CimInstance Win32_Process -Filter "ProcessId = $($Process.Id)" -ErrorAction Stop
        if ($null -ne $cimProcess) { $commandLine = $cimProcess.CommandLine }
    }
    catch { }
    return [pscustomobject]@{
        Id = $Process.Id
        StartTimeUtc = $Process.StartTime.ToUniversalTime()
        Path = $path
        CommandLine = $commandLine
    }
}

function Test-CommandLineReferencesPath {
    [CmdletBinding()]
    param(
        [string]$CommandLine,
        [Parameter(Mandatory)][string]$Path
    )

    if (-not $CommandLine) { return $false }
    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $offset = 0
    while ($offset -lt $CommandLine.Length) {
        $index = $CommandLine.IndexOf($fullPath, $offset, [System.StringComparison]::OrdinalIgnoreCase)
        if ($index -lt 0) { return $false }
        $beforeValid = $index -eq 0 -or [char]::IsWhiteSpace($CommandLine[$index - 1]) -or $CommandLine[$index - 1] -eq '"'
        $afterIndex = $index + $fullPath.Length
        $afterValid = $afterIndex -eq $CommandLine.Length -or
            [char]::IsWhiteSpace($CommandLine[$afterIndex]) -or $CommandLine[$afterIndex] -eq '"'
        if ($beforeValid -and $afterValid) { return $true }
        $offset = $index + 1
    }
    return $false
}

function Test-NodeProcessIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Token,
        [Parameter(Mandatory)][string]$NodePath,
        [Parameter(Mandatory)][string]$EntryPointPath
    )

    if (-not $Token.Path) { return $false }
    if (-not ([System.IO.Path]::GetFullPath($Token.Path).Equals(
            [System.IO.Path]::GetFullPath($NodePath), [System.StringComparison]::OrdinalIgnoreCase))) {
        return $false
    }
    return Test-CommandLineReferencesPath -CommandLine $Token.CommandLine -Path $EntryPointPath
}

function Test-ProcessTokenBelongsToRoot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Token,
        [Parameter(Mandatory)][string]$Root
    )

    if ($Token.Path -and (Test-PathWithinRoot -Path $Token.Path -Root $Root)) { return $true }
    return Test-CommandLineReferencesPath -CommandLine $Token.CommandLine -Path $Root
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
        [int]$TimeoutSeconds = 30,
        [scriptblock]$OwnershipValidator
    )

    $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $alive = @($Tokens | Where-Object { Test-SameProcessTokenIsAlive -Token $_ })
        if ($alive.Count -eq 0) { return }
        Start-Sleep -Milliseconds 250
    } while ([datetime]::UtcNow -lt $deadline)

    foreach ($token in $alive) {
        $owned = if ($null -ne $OwnershipValidator) {
            [bool](& $OwnershipValidator $token)
        }
        else {
            Test-ProcessTokenBelongsToRoot -Token $token -Root $InstallRoot
        }
        if (-not $owned) {
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
        [Parameter(Mandatory)][string]$InstallRoot,
        [scriptblock]$OwnershipValidator
    )

    $owners = @(Get-PortOwnerProcess -Port $Port)
    if ($owners.Count -eq 0) { return }

    foreach ($owner in $owners) {
        $token = Get-GreyGrayProcessToken -Process $owner
        $owned = if ($null -ne $OwnershipValidator) {
            [bool](& $OwnershipValidator $token)
        }
        else {
            Test-ProcessTokenBelongsToRoot -Token $token -Root $InstallRoot
        }
        if (-not $owned) {
            throw "port $Port 仍由外部 PID $($token.Id) 佔用；拒絕誤殺非 GreyGray 程序。"
        }
        Stop-Process -Id $token.Id -Force -ErrorAction Stop
    }

    Start-Sleep -Milliseconds 500
    if (@(Get-PortOwnerProcess -Port $Port).Count -ne 0) {
        throw "port $Port 的舊 listener 未真正釋放。"
    }
}
