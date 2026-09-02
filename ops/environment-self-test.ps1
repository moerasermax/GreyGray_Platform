<# 只在 OS temp 驗證 M-1 腳本的 AST、沙箱冪等與 prod-monitor upsert。 #>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

<#
    AST 解析：先以 UTF-8 讀進來，再 ParseInput——**不要**用 ParseFile。理由與
    ops/self-test.ps1 第 13-20 行完全相同（那支在 BE-42 第二輪修過，這支漏了）：

    ParseFile 對「沒有 BOM」的檔案是照**系統 ANSI 代碼頁**讀的。這個 repo 的原始檔一律
    UTF-8，其中幾支 dev-only 腳本刻意沒有 BOM；在中文 Windows（ACP=big5，開發機與 YC
    都是）上，UTF-8 的中文位元組會被 big5 重新分組成雙位元組字元，分出來的位元組可能剛好
    是 " 或 \，於是完全合法的腳本被判成語法錯誤——而這支自測本身就是要在正式機的 5.1 上跑的。
    明確指定編碼之後，5.1 與 7 讀到的是同一份文字。ReadAllText 有 BOM 時會自己去掉它，
    所以兩種檔案都吃得下。
#>
$parseFailures = @()
$scripts = @(Get-ChildItem -Path $PSScriptRoot -Filter '*.ps1' -Recurse -File)
if ($scripts.Count -eq 0) { throw 'ops 底下找不到任何 *.ps1——這一項等於什麼都沒查。' }
foreach ($script in $scripts) {
    $tokens = $null; $errors = $null
    $scriptText = [IO.File]::ReadAllText($script.FullName, [Text.Encoding]::UTF8)
    [void][Management.Automation.Language.Parser]::ParseInput(
        $scriptText, $script.FullName, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { $parseFailures += "$($script.Name): $($errors.Message -join ' | ')" }
}
if ($parseFailures.Count -gt 0) { throw ($parseFailures -join "`n") }
Write-Host "PASS PowerShell AST：$($scripts.Count) 個 ops/**/*.ps1 以 UTF-8 讀入後無語法錯誤"

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
