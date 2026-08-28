<#
    跑 tests/ 底下的所有測試專案。

    為什麼不用 dotnet test：
      SDK 10.0.301 ＋ xunit.v3 4.x 的組合下，`dotnet test` 會走 VSTest 路徑並直接報錯
      （Microsoft.Testing.Platform.MSBuild.targets(320,5)）。
      dotnet.config 的 [dotnet.test:runner] 與 TestingPlatformDotnetTestSupport 兩種
      opt-in 在這個 SDK 版本上都沒生效，已於 2026-08-28 實測。
      xunit.v3 產出的是可執行檔，直接跑它結果一樣，而且沒有中間層。
      等 SDK 升上去之後回頭再試一次 dotnet test，能通就把這個腳本簡化掉。

    為什麼要比對「專案數」與「找到的執行檔數」：
      這個腳本原本寫死只跑 Architecture.Tests。後來新增測試專案時，
      它會靜默地永遠不被執行——而畫面上仍然一片綠。
      那正是這個 repo 最不能接受的失敗模式（見 HANDOFF_1「架構測試的空跑陷阱」）。
      所以下面數量對不上就直接 throw，不讓它默默少跑。
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

dotnet build "$repo\GreyGray.slnx" -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "建置失敗，測試不跑。" }

$projects = Get-ChildItem -Path "$repo\tests" -Filter '*.csproj' -Recurse -File
if ($projects.Count -eq 0) { throw "tests\ 底下找不到任何測試專案——這比沒有測試更糟。" }

$failed = @()
$ran = 0

foreach ($project in $projects) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($project.Name)
    $exe = Join-Path $project.Directory.FullName "bin\$Configuration\net10.0\$name.exe"

    if (-not (Test-Path $exe)) {
        throw "找不到測試執行檔：$exe（專案 $name 有建置嗎？OutputType 是 Exe 嗎？）"
    }

    Write-Host ""
    Write-Host "═══ $name ═══"
    & $exe
    if ($LASTEXITCODE -ne 0) { $failed += $name }
    $ran++
}

if ($ran -ne $projects.Count) {
    throw "測試專案有 $($projects.Count) 個，只跑了 $ran 個。"
}

Write-Host ""
if ($failed.Count -gt 0) {
    Write-Host "✗ 失敗的測試專案：$($failed -join ', ')"
    exit 1
}

Write-Host "✓ $ran 個測試專案全部通過"
exit 0
