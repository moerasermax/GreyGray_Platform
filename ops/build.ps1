<#
    建置。CI 與本機用同一支。
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$Publish,

    [ValidateSet('win-x64')]
    [string]$RuntimeIdentifier = 'win-x64',

    [string]$NodePath,

    [string]$PnpmPath,

    # BE-42：-Publish 時要建的前端在哪一棵樹（可部署的前端在 -fe worktree），
    # 以及兩個 app 各自的 API base。API base 沒給就 throw——見下面那一段。
    [string]$FrontendRoot,

    [string]$StorefrontApiBaseUrl,

    [string]$AdminApiBaseUrl
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

# API base 是建置期 inline 進 bundle 的，事後補不回來；沒給就在**最前面**停，
# 不要先花十幾分鐘建置與跑完整測試，最後才發現參數不齊。
if ($Publish -and (-not $StorefrontApiBaseUrl -or -not $AdminApiBaseUrl)) {
    throw '-Publish 必須同時給 -StorefrontApiBaseUrl 與 -AdminApiBaseUrl（ADR-031：https://greygray.shop 與 https://admin.greygray.shop）；正式 artifact 不准吃 .env.local 的開發機位址。'
}

dotnet build "$repo\GreyGray.slnx" -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "建置失敗。" }

& "$repo\ops\test.ps1" -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw "架構測試沒過。硬邊界被破壞了，不要繞過它。" }

if (-not $Publish) { return }

# 前端是兩個 Next standalone 常駐服務。node/pnpm 任一缺失都要在改動 artifacts 前明確失敗。
$frontendBuild = @{
    OutputRoot           = (Join-Path $repo 'artifacts')
    StorefrontApiBaseUrl = $StorefrontApiBaseUrl
    AdminApiBaseUrl      = $AdminApiBaseUrl
}
if ($FrontendRoot) { $frontendBuild.FrontendRoot = $FrontendRoot }
if ($NodePath) { $frontendBuild.NodePath = $NodePath }
if ($PnpmPath) { $frontendBuild.PnpmPath = $PnpmPath }
& "$PSScriptRoot\build-frontends.ps1" @frontendBuild

# 正式機沒有 dotnet runtime，所以一律 self-contained（ADR-003）。
# 加上兩個 Next standalone artifact，共五個 NSSM service。
$hosts = @('GreyGray.Api.Storefront', 'GreyGray.Api.Admin', 'GreyGray.Worker')
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repo 'artifacts'))
foreach ($h in $hosts) {
    $out = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot $h))
    if (-not $out.StartsWith($artifactsRoot + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "拒絕清除 artifacts 外的路徑：$out"
    }
    if (Test-Path -LiteralPath $out) {
        # publish -o 不會刪掉舊版多出的檔案；先清乾淨，避免把 stale binary 帶去 YC。
        Remove-Item -LiteralPath $out -Recurse -Force
    }
    dotnet publish "$repo\src\Hosts\$h\$h.csproj" `
        -c Release -r $RuntimeIdentifier --self-contained true `
        -o $out --nologo
    if ($LASTEXITCODE -ne 0) { throw "$h publish 失敗。" }
    Write-Host "publish 完成：$out"
}
