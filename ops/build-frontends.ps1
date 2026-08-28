<# 建置兩個 Next standalone app，並組成可直接交給 node.exe 的部署 artifacts。 #>
[CmdletBinding()]
param(
    [string]$NodePath,
    [string]$PnpmPath,
    [string]$OutputRoot,
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$frontendRoot = Join-Path $repo 'frontend'
. "$PSScriptRoot\lib\Process.ps1"
. "$PSScriptRoot\lib\Node.ps1"
$materializeScript = Join-Path $PSScriptRoot 'materialize-next-standalone.mjs'
if (-not (Test-Path -LiteralPath $materializeScript -PathType Leaf)) {
    throw "找不到 Next standalone materializer：$materializeScript"
}

$node = Resolve-NodeExecutable -NodePath $NodePath
$pnpm = Resolve-PnpmCommand -PnpmPath $PnpmPath
$apps = @(
    @{ Name = 'storefront'; ArtifactDirectory = 'GreyGray.Web.Storefront' },
    @{ Name = 'admin'; ArtifactDirectory = 'GreyGray.Web.Admin' }
)

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
    Write-Host "✓ frontend build 參數驗證通過：node.exe=$node；pnpm=$pnpm；未執行 install/build 或寫 artifacts。"
    return
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
Invoke-PnpmCommand -PnpmPath $pnpm `
    -ArgumentList @('--dir', $frontendRoot, 'run', 'build') -WorkingDirectory $repo

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
    Write-Host "Next standalone artifact 完成：$destination"
}
