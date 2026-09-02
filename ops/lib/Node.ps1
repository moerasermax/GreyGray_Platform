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
    <#
        pnpm 通常是 .cmd，不能直接交給 ProcessStartInfo。先保存完整 output/exit code，再 throw。

        -Environment（BE-42）：只在這一次呼叫期間存在的環境變數。
        `next build` 要的 NEXT_PUBLIC_* 兩個 app 值不一樣，所以不能一次設好跑到底。
        為什麼是「改本行程再還原」而不是 Start-Process -Environment：pnpm 是 .cmd，
        要靠 PowerShell 的呼叫運算子才跑得起來（見上面那句），而 -Environment 是
        PowerShell 7 才有的參數，這支腳本要維持 5.1 可讀。
        還原**一定**要用 Remove-Item Env:——#38 的教訓：把 $null 塞回 Set-Item／
        [Environment]::SetEnvironmentVariable 會留下一個「存在但是空字串」的變數，
        那比不存在更難查（Test-Path Env:X 是 True，值卻是空的）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$PnpmPath,
        [string[]]$ArgumentList = @(),
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [hashtable]$Environment = @{}
    )

    $previousValues = @{}
    $absentKeys = @()
    foreach ($key in $Environment.Keys) {
        if (Test-Path -LiteralPath "Env:$key") {
            $previousValues[$key] = (Get-Item -LiteralPath "Env:$key").Value
        }
        else {
            $absentKeys += $key
        }
        Set-Item -LiteralPath "Env:$key" -Value ([string]$Environment[$key])
    }

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
    finally {
        $ErrorActionPreference = $previousPreference
        foreach ($key in $previousValues.Keys) {
            Set-Item -LiteralPath "Env:$key" -Value $previousValues[$key]
        }
        foreach ($key in $absentKeys) {
            Remove-Item -LiteralPath "Env:$key" -ErrorAction SilentlyContinue
        }
    }

    foreach ($line in $output) { Write-Host ([string]$line) }
    if ($exitCode -ne 0) {
        throw "pnpm 失敗（exit $exitCode）；stdout/stderr 已先完整輸出。"
    }
}
