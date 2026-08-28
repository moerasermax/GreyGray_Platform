Set-StrictMode -Version Latest

function Resolve-NodeExecutable {
    [CmdletBinding()]
    param([string]$NodePath)

    $candidate = $NodePath
    if (-not $candidate) {
        $command = Get-Command 'node.exe' -CommandType Application -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -eq $command) {
            throw '找不到原生 node.exe；GreyGray-Web-* 不可 silently skip。請先完成 M-1 Node 安裝。'
        }
        $candidate = $command.Source
    }

    $fullPath = [System.IO.Path]::GetFullPath($candidate)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "找不到原生 node.exe：$fullPath。GreyGray-Web-* 不可 silently skip；請先完成 M-1 Node 安裝。"
    }
    if (-not [System.IO.Path]::GetFileName($fullPath).Equals('node.exe', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "正式服務必須直接使用 node.exe，不可使用 npm/pnpm shim：$fullPath"
    }
    return $fullPath
}

function Resolve-PnpmCommand {
    [CmdletBinding()]
    param([string]$PnpmPath)

    $candidate = $PnpmPath
    if (-not $candidate) {
        $command = Get-Command 'pnpm.cmd' -CommandType Application -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -eq $command) {
            throw '找不到 pnpm.cmd；無法建置 Next standalone artifacts。請先完成 M-1 pnpm 安裝。'
        }
        $candidate = $command.Source
    }

    $fullPath = [System.IO.Path]::GetFullPath($candidate)
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "找不到 pnpm：$fullPath。無法建置 Next standalone artifacts；請先完成 M-1 pnpm 安裝。"
    }
    $extension = [System.IO.Path]::GetExtension($fullPath)
    if ($extension -notin @('.cmd', '.exe')) {
        throw "Windows 建置只接受 pnpm.cmd 或原生 pnpm.exe，不使用 .ps1/extensionless shim：$fullPath"
    }
    return $fullPath
}

function Invoke-PnpmCommand {
    <# pnpm 通常是 .cmd，不能直接交給 ProcessStartInfo。先保存完整 output/exit code，再 throw。 #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PnpmPath,
        [string[]]$ArgumentList = @(),
        [Parameter(Mandatory)][string]$WorkingDirectory
    )

    $previousPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        Push-Location $WorkingDirectory
        try {
            $output = @(& $PnpmPath @ArgumentList 2>&1)
            $exitCode = $LASTEXITCODE
        }
        finally { Pop-Location }
    }
    finally { $ErrorActionPreference = $previousPreference }

    foreach ($line in $output) { Write-Host ([string]$line) }
    if ($exitCode -ne 0) {
        throw "pnpm 失敗（exit $exitCode）；stdout/stderr 已先完整輸出。"
    }
}
