<#
    跑架構測試。

    為什麼不用 dotnet test：
      SDK 10.0.301 ＋ xunit.v3 4.x 的組合下，`dotnet test` 會走 VSTest 路徑並直接報錯
      （Microsoft.Testing.Platform.MSBuild.targets(320,5)）。
      dotnet.config 的 [dotnet.test:runner] 與 TestingPlatformDotnetTestSupport 兩種
      opt-in 在這個 SDK 版本上都沒生效，已於 2026-08-28 實測。
      xunit.v3 產出的是可執行檔，直接跑它結果一樣，而且沒有中間層。
      等 SDK 升上去之後回頭再試一次 dotnet test，能通就把這個腳本簡化掉。
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

dotnet build "$repo\Daigou.slnx" -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "建置失敗，測試不跑。" }

$exe = Join-Path $repo "tests\Daigou.Architecture.Tests\bin\$Configuration\net10.0\Daigou.Architecture.Tests.exe"
if (-not (Test-Path $exe)) { throw "找不到測試執行檔：$exe" }

& $exe
exit $LASTEXITCODE
