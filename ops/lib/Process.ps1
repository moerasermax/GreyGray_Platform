Set-StrictMode -Version Latest

function ConvertTo-NativeArgument {
    [CmdletBinding()]
    param([AllowEmptyString()][Parameter(Mandatory)][string]$Value)

    if ($Value.Length -eq 0) { return '""' }
    if ($Value -notmatch '[\s"]') { return $Value }

    # Windows CommandLineToArgvW / C runtime quoting：雙引號前與結尾的反斜線必須加倍。
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    $escaped = [regex]::Replace($escaped, '(\\+)$', '$1$1')
    return '"' + $escaped + '"'
}

function Invoke-NativeCommand {
    <#
        用 ProcessStartInfo 收完整 stdout / stderr / exit code，再決定是否失敗。
        不直接用 PowerShell 的 2>&1；Windows PowerShell 在 ErrorActionPreference=Stop 下，
        native stderr 可能先升格成終止性錯誤，讓診斷與 LASTEXITCODE 來不及保存。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [string]$WorkingDirectory,
        [hashtable]$Environment = @{},
        [switch]$AllowNonZeroExit,
        [switch]$EchoOutput
    )

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
    $psi.StandardErrorEncoding = New-Object System.Text.UTF8Encoding($false)
    $psi.Arguments = (($ArgumentList | ForEach-Object { ConvertTo-NativeArgument -Value ([string]$_) }) -join ' ')

    if ($WorkingDirectory) { $psi.WorkingDirectory = $WorkingDirectory }
    foreach ($key in $Environment.Keys) {
        $psi.EnvironmentVariables[[string]$key] = [string]$Environment[$key]
    }

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi
    try {
        if (-not $process.Start()) { throw "無法啟動原生程序：$FilePath" }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()

        # 先完整保存三個訊號，再做任何 throw。
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        $exitCode = $process.ExitCode
    }
    finally {
        $process.Dispose()
    }

    $result = [pscustomobject]@{
        FilePath = $FilePath
        ExitCode = $exitCode
        StdOut = $stdout
        StdErr = $stderr
    }

    if ($EchoOutput -or ($exitCode -ne 0)) {
        if ($stdout) { Write-Host $stdout.TrimEnd() }
        if ($stderr) { Write-Host $stderr.TrimEnd() }
    }

    if (($exitCode -ne 0) -and -not $AllowNonZeroExit) {
        throw "原生程序失敗（exit $exitCode）：$FilePath。stdout/stderr 已先完整輸出。"
    }

    return $result
}
