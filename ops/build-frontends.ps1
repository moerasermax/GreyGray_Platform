<# 建置兩個 Next standalone app，並組成可直接交給 node.exe 的部署 artifacts。 #>
[CmdletBinding()]
param(
    [string]$NodePath,
    [string]$PnpmPath,
    [string]$OutputRoot,
    # BE-42：可部署的前端在**另一棵 worktree**（feat/frontend-wave-1）。
    # 預設仍是這棵樹的 frontend\，行為跟以前一樣。
    [string]$FrontendRoot,
    # 兩個 app 的 API base 是**建置期**決定的（NEXT_PUBLIC_* 會被 inline 進 bundle），
    # 而且兩個值不一樣（ADR-031：前台 https://greygray.shop、後台 https://admin.greygray.shop），
    # 所以 app 要各自建。非 -ValidateOnly 時兩個都必填——artifact 默默吃到 .env.local 的
    # 開發機位址的話，正式站會整站打 127.0.0.1，而建置本身完全不會失敗。
    [string]$StorefrontApiBaseUrl,
    [string]$AdminApiBaseUrl,
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($FrontendRoot)) { $FrontendRoot = Join-Path $repo 'frontend' }
$frontendRoot = [System.IO.Path]::GetFullPath($FrontendRoot)
if (-not (Test-Path -LiteralPath (Join-Path $frontendRoot 'pnpm-workspace.yaml') -PathType Leaf)) {
    throw "找不到 pnpm workspace：$frontendRoot（-FrontendRoot 要指向含 pnpm-workspace.yaml 的 frontend\ 目錄）"
}

function Resolve-ApiBaseUrl {
    <#
        建置期注進 bundle 的 API base：絕對 http(s)、不帶結尾斜線、不帶路徑。
        不自動修正而是 throw——前端是把它跟 '/v1/...' 直接串接的，
        悄悄修掉錯誤輸入只會產生一份「看起來成功」的壞 artifact。
        （5.1 可執行：不用 ??、?.、三元運算子。）
    #>
    param([Parameter(Mandatory)][string]$Name, [string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "-$Name 必填：正式 artifact 不准默默吃到 .env.local 的開發機位址。"
    }
    $trimmed = $Value.Trim()
    $uri = $null
    if (-not [System.Uri]::TryCreate($trimmed, [System.UriKind]::Absolute, [ref]$uri)) {
        throw "-$Name 必須是絕對網址（例如 https://greygray.shop）：$Value"
    }
    if ($uri.Scheme -ne 'http' -and $uri.Scheme -ne 'https') {
        throw "-$Name 的 scheme 必須是 http 或 https：$Value"
    }
    if ($trimmed.EndsWith('/')) {
        throw "-$Name 不可有結尾斜線（前端會直接串上 /v1/...）：$Value"
    }
    if ($uri.AbsolutePath -ne '/' -or $uri.Query -or $uri.Fragment) {
        throw "-$Name 只接受 scheme + 主機名稱，不可帶路徑／查詢字串：$Value"
    }
    return $trimmed
}
. "$PSScriptRoot\lib\Process.ps1"
. "$PSScriptRoot\lib\Node.ps1"
$materializeScript = Join-Path $PSScriptRoot 'materialize-next-standalone.mjs'
if (-not (Test-Path -LiteralPath $materializeScript -PathType Leaf)) {
    throw "找不到 Next standalone materializer：$materializeScript"
}

$node = Resolve-NodeExecutable -NodePath $NodePath
$pnpm = Resolve-PnpmCommand -PnpmPath $PnpmPath
$apps = @(
    @{ Name = 'storefront'; PackageName = '@greygray/storefront'; ArtifactDirectory = 'GreyGray.Web.Storefront'
       ApiBaseUrlParameter = 'StorefrontApiBaseUrl'; ApiBaseUrl = $StorefrontApiBaseUrl },
    @{ Name = 'admin'; PackageName = '@greygray/admin'; ArtifactDirectory = 'GreyGray.Web.Admin'
       ApiBaseUrlParameter = 'AdminApiBaseUrl'; ApiBaseUrl = $AdminApiBaseUrl }
)

# .env.local 的開發機位址：正式 artifact 裡出現任何一個都代表 NEXT_PUBLIC_API_BASE_URL 沒吃到。
$forbiddenApiBaseUrls = @('127.0.0.1:5000', '127.0.0.1:5001')

foreach ($app in $apps) {
    $appRoot = Join-Path (Join-Path $frontendRoot 'apps') $app.Name
    if (-not (Test-Path -LiteralPath (Join-Path $appRoot 'package.json') -PathType Leaf)) {
        throw "找不到 Next app：$appRoot"
    }
    $nextConfig = Join-Path $appRoot 'next.config.ts'
    if (-not (Test-Path -LiteralPath $nextConfig -PathType Leaf) -or
        -not ([System.IO.File]::ReadAllText($nextConfig).Contains("output: 'standalone'"))) {
        throw "$($app.Name) 未設定 output: 'standalone'，拒絕產生不可部署 artifact。"
    }
}

if ($ValidateOnly) {
    Write-Host "FrontendRoot=$frontendRoot"
    foreach ($app in $apps) {
        if ([string]::IsNullOrWhiteSpace($app.ApiBaseUrl)) {
            Write-Host "$($app.Name) API base=（未指定；真的建置時 -$($app.ApiBaseUrlParameter) 必填）"
        }
        else {
            Write-Host "$($app.Name) API base=$(Resolve-ApiBaseUrl -Name $app.ApiBaseUrlParameter -Value $app.ApiBaseUrl)"
        }
    }
    Write-Host "✓ frontend build 參數驗證通過：node.exe=$node；pnpm=$pnpm；未執行 install/build 或寫 artifacts。"
    return
}

foreach ($app in $apps) {
    $app.ApiBaseUrl = Resolve-ApiBaseUrl -Name $app.ApiBaseUrlParameter -Value $app.ApiBaseUrl
}

if (-not $OutputRoot) { $OutputRoot = Join-Path $repo 'artifacts' }
$outputFull = [System.IO.Path]::GetFullPath($OutputRoot)

$nodeVersion = Invoke-NativeCommand -FilePath $node -ArgumentList @('--version') `
    -WorkingDirectory $frontendRoot -EchoOutput
if ($nodeVersion.StdOut.Trim() -notmatch '^v(?<major>\d+)' -or [int]$Matches.major -lt 22) {
    throw "Node 必須 >= 22（與 frontend/package.json 的 engines 一致）；目前輸出：$($nodeVersion.StdOut.Trim())"
}
Invoke-PnpmCommand -PnpmPath $pnpm -ArgumentList @('--version') -WorkingDirectory $frontendRoot
Invoke-PnpmCommand -PnpmPath $pnpm `
    -ArgumentList @('--dir', $frontendRoot, 'install', '--frozen-lockfile') -WorkingDirectory $repo
# 共用套件先建一次（兩個 app 都吃它），再讓兩個 app **各自**帶自己的 NEXT_PUBLIC_* 建。
# 不能一句 pnpm run build 跑完：那樣兩個 app 會拿到同一個 API base。
Invoke-PnpmCommand -PnpmPath $pnpm `
    -ArgumentList @('--dir', $frontendRoot, '--recursive', '--filter', './packages/*', 'build') `
    -WorkingDirectory $repo
foreach ($app in $apps) {
    Write-Host "建置 $($app.PackageName)：NEXT_PUBLIC_API_BASE_URL=$($app.ApiBaseUrl)、NEXT_PUBLIC_USE_MOCK=0"
    Invoke-PnpmCommand -PnpmPath $pnpm `
        -ArgumentList @('--dir', $frontendRoot, '--filter', $app.PackageName, 'build') `
        -WorkingDirectory $repo `
        -Environment @{
            'NEXT_PUBLIC_API_BASE_URL' = $app.ApiBaseUrl
            'NEXT_PUBLIC_USE_MOCK'     = '0'
        }
}

foreach ($app in $apps) {
    $appRoot = Join-Path (Join-Path $frontendRoot 'apps') $app.Name
    $standalone = Join-Path (Join-Path $appRoot '.next') 'standalone'
    $entryPoint = Join-Path (Join-Path (Join-Path $standalone 'apps') $app.Name) 'server.js'
    if (-not (Test-Path -LiteralPath $entryPoint -PathType Leaf)) {
        throw "$($app.Name) build 沒產出 standalone server.js：$entryPoint"
    }

    $destination = [System.IO.Path]::GetFullPath((Join-Path $outputFull $app.ArtifactDirectory))
    if (-not $destination.StartsWith($outputFull + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "拒絕清除 frontend artifacts 外的路徑：$destination"
    }
    if (Test-Path -LiteralPath $destination) {
        Remove-Item -LiteralPath $destination -Recurse -Force
    }
    try {
        Invoke-NativeCommand -FilePath $node `
            -ArgumentList @($materializeScript, $standalone, $destination) `
            -WorkingDirectory $repo -EchoOutput | Out-Null
    }
    catch {
        if (Test-Path -LiteralPath $destination) {
            Remove-Item -LiteralPath $destination -Recurse -Force
        }
        throw
    }

    $deployedAppRoot = Join-Path (Join-Path $destination 'apps') $app.Name
    $staticSource = Join-Path (Join-Path $appRoot '.next') 'static'
    if (Test-Path -LiteralPath $staticSource -PathType Container) {
        $deployedNext = Join-Path $deployedAppRoot '.next'
        New-Item -ItemType Directory -Path $deployedNext -Force | Out-Null
        Copy-Item -LiteralPath $staticSource -Destination $deployedNext -Recurse -Force
    }
    $publicSource = Join-Path $appRoot 'public'
    if (Test-Path -LiteralPath $publicSource -PathType Container) {
        Copy-Item -LiteralPath $publicSource -Destination $deployedAppRoot -Recurse -Force
    }

    $deployedEntryPoint = Join-Path $deployedAppRoot 'server.js'
    if (-not (Test-Path -LiteralPath $deployedEntryPoint -PathType Leaf)) {
        throw "frontend artifact 不完整：$deployedEntryPoint"
    }
    $remainingReparsePoint = Get-ChildItem -LiteralPath $destination -Recurse -Force `
        -Attributes ReparsePoint -ErrorAction Stop | Select-Object -First 1
    if ($null -ne $remainingReparsePoint) {
        throw "frontend artifact 仍含不可攜的 reparse point：$($remainingReparsePoint.FullName)"
    }

    # NEXT_PUBLIC_* 是建置期 inline 進 bundle 的，沒吃到的話建置一樣「成功」，
    # 錯誤要到正式站打不通 API 才會發現。所以在這裡就驗 artifact 本身：
    # 該有的字串要在、開發機位址一個都不准在。
    $scanRoots = @((Join-Path $deployedAppRoot '.next'), (Join-Path $deployedAppRoot 'server.js')) |
        Where-Object { Test-Path -LiteralPath $_ }
    if ($scanRoots.Count -eq 0) {
        throw "找不到可掃描的 artifact 內容（$deployedAppRoot）；無法確認 API base 有吃到。"
    }
    $scanFiles = @(Get-ChildItem -LiteralPath $scanRoots -Recurse -File -Force -ErrorAction Stop)
    $apiBaseHit = @($scanFiles | Select-String -SimpleMatch -Pattern $app.ApiBaseUrl -List |
        Select-Object -First 1)
    if ($apiBaseHit.Count -eq 0) {
        throw ("$($app.Name) artifact 裡找不到 $($app.ApiBaseUrl)：" +
            "NEXT_PUBLIC_API_BASE_URL 沒有被 inline 進 bundle，這份 artifact 不可部署。")
    }
    Write-Host "✓ $($app.Name) artifact 含 $($app.ApiBaseUrl)（$($apiBaseHit[0].Path)）"
    foreach ($forbidden in $forbiddenApiBaseUrls) {
        $forbiddenHit = @($scanFiles | Select-String -SimpleMatch -Pattern $forbidden -List |
            Select-Object -First 1)
        if ($forbiddenHit.Count -gt 0) {
            throw ("$($app.Name) artifact 仍含開發機位址 $forbidden（$($forbiddenHit[0].Path)）：" +
                'NEXT_PUBLIC_API_BASE_URL 沒有蓋掉 .env.local，這份 artifact 不可部署。')
        }
    }
    Write-Host "✓ $($app.Name) artifact 不含開發機位址（$($forbiddenApiBaseUrls -join '、')）"
    Write-Host "Next standalone artifact 完成：$destination"
}
