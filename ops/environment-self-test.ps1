<# 只在 OS temp 驗證 M-1 腳本的 AST、沙箱冪等與 prod-monitor upsert。 #>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$parseFailures = @()
foreach ($script in Get-ChildItem -Path $PSScriptRoot -Filter '*.ps1' -Recurse -File) {
    $tokens = $null; $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { $parseFailures += "$($script.Name): $($errors.Message -join ' | ')" }
}
if ($parseFailures.Count -gt 0) { throw ($parseFailures -join "`n") }
Write-Host 'PASS PowerShell AST：ops/**/*.ps1 全部可解析'

$temp = Join-Path ([IO.Path]::GetTempPath()) ('greygray-m1-' + [guid]::NewGuid().ToString('N'))
try {
    $simulation = Join-Path $temp 'simulation'
    $first = @(& (Join-Path $PSScriptRoot 'install-environment.ps1') -SimulationRoot $simulation 6>&1 | ForEach-Object { $_.ToString() })
    $second = @(& (Join-Path $PSScriptRoot 'install-environment.ps1') -SimulationRoot $simulation 6>&1 | ForEach-Object { $_.ToString() })
    if (@($first | Where-Object { $_ -like '已安裝 *' }).Count -ne 22) { throw '第一次模擬沒有安裝全部 22 項。' }
    if (@($second | Where-Object { $_ -like '已存在 *' }).Count -ne 22) { throw '第二次模擬沒有把全部 22 項回報為已存在。' }
    if (@($second | Where-Object { $_ -like '已安裝 *' }).Count -ne 0) { throw '第二次模擬仍執行安裝。' }
    Write-Host 'PASS 安裝冪等：第一次 22 項已安裝；第二次 22 項已存在、0 項重裝'

    $config = Join-Path $temp 'monitor.toml'
    [IO.File]::WriteAllText($config, "process_timeout_seconds = 10`nprobe_timeout_seconds = 20`n", (New-Object Text.UTF8Encoding($false)))
    & (Join-Path $PSScriptRoot 'register-prod-monitor.ps1') -ConfigPath $config | Out-Null
    $once = [IO.File]::ReadAllText($config)
    & (Join-Path $PSScriptRoot 'register-prod-monitor.ps1') -ConfigPath $config | Out-Null
    $twice = [IO.File]::ReadAllText($config)
    if ($once -ne $twice) { throw 'prod-monitor 第二次登記改變了設定。' }
    if (($twice.Split(@('# BEGIN GREYGRAY-M1 MANAGED BLOCK'), [StringSplitOptions]::None).Count - 1) -ne 1) { throw 'prod-monitor managed block 數量不是 1。' }
    Write-Host 'PASS prod-monitor：兩個 process/port 指紋可登記，第二次不重複'
}
finally {
    if (Test-Path -LiteralPath $temp) {
        $full = [IO.Path]::GetFullPath($temp); $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $full.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) { throw "拒絕清除非 temp 路徑：$full" }
        Remove-Item -LiteralPath $full -Recurse -Force
    }
}
Write-Host 'OVERALL PASS environment self-test'
