<#
    建置。CI 與本機用同一支。
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

dotnet build "$repo\GreyGray.slnx" -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "建置失敗。" }

& "$repo\ops\test.ps1" -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw "架構測試沒過。硬邊界被破壞了，不要繞過它。" }

if (-not $Publish) { return }

# 正式機沒有 dotnet runtime，所以一律 self-contained（ADR-003）。
# 產出是三個獨立資料夾，用 NSSM 各自註冊成 Windows service。
$hosts = @('GreyGray.Api.Storefront', 'GreyGray.Api.Admin', 'GreyGray.Worker')
foreach ($h in $hosts) {
    $out = Join-Path $repo "artifacts\$h"
    dotnet publish "$repo\src\Hosts\$h\$h.csproj" `
        -c Release -r win-x64 --self-contained true `
        -o $out --nologo
    if ($LASTEXITCODE -ne 0) { throw "$h publish 失敗。" }
    Write-Host "publish 完成：$out"
}
