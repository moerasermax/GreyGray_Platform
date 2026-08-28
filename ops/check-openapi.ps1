<#
    啟動兩個 Host，抓 AddOpenApi() 的實際產物，與 docs/api 的 v1.0 凍結契約做語意比對。
    現階段 Host 只有 /health 時會明確 FAIL-FAST；不提供 skip/continue-on-error 開關。
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [switch]$NoBuild,
    [ValidateRange(1024, 65534)]
    [int]$StorefrontPort = 15000,
    [ValidateRange(1025, 65535)]
    [int]$AdminPort = 15001,
    [ValidateRange(5, 180)]
    [int]$StartupTimeoutSeconds = 45,
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\lib\Process.ps1"

if ($StorefrontPort -eq $AdminPort) { throw 'StorefrontPort 與 AdminPort 不可相同。' }
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("greygray-openapi-" + [guid]::NewGuid().ToString('N'))
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

if (-not $NoBuild) {
    Invoke-NativeCommand -FilePath 'dotnet' `
        -ArgumentList @('build', "$repo\GreyGray.slnx", '-c', $Configuration, '--nologo') `
        -WorkingDirectory $repo -EchoOutput | Out-Null
}

function Assert-PortAvailable {
    param([int]$Port)
    if (@(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "OpenAPI gate 的暫用 port $Port 已被占用；拒絕連到既有程序取得假 schema。"
    }
}

function Get-OpenApiDocument {
    param(
        [string]$HostName,
        [string]$ProjectName,
        [int]$Port,
        [string]$Destination
    )

    Assert-PortAvailable -Port $Port
    $exe = "$repo\src\Hosts\$ProjectName\bin\$Configuration\net10.0\$ProjectName.exe"
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        throw "找不到已建置 Host：$exe。拿掉 -NoBuild 或先建置 solution。"
    }

    # 不用 Start-Process：Windows PowerShell 5.1 遇到同時存在 Path/PATH 的環境時會直接丟 duplicate key。
    # 直接用 ProcessStartInfo，Host stdout/stderr 繼承目前 console，CI 仍能保留完整啟動診斷。
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $exe
    $psi.WorkingDirectory = Split-Path -Parent $exe
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi
    $oldEnvironment = $env:ASPNETCORE_ENVIRONMENT
    $oldUrls = $env:ASPNETCORE_URLS
    try {
        $env:ASPNETCORE_ENVIRONMENT = 'Development'
        $env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
        if (-not $process.Start()) { throw "無法啟動 $HostName。" }
    }
    finally {
        $env:ASPNETCORE_ENVIRONMENT = $oldEnvironment
        $env:ASPNETCORE_URLS = $oldUrls
    }

    try {
        $deadline = [datetime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
        $url = "http://127.0.0.1:$Port/openapi/v1.json"
        do {
            if ($process.HasExited) {
                throw "$HostName 在 OpenAPI 可讀前就退出（exit $($process.ExitCode)）；啟動輸出已寫入目前 console。"
            }

            $owners = @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)
            if ($owners.Count -gt 0 -and ($owners.OwningProcess -notcontains $process.Id)) {
                throw "$HostName 的暫用 port 被 PID $($owners.OwningProcess -join ',') 接手，並非本次啟動 PID $($process.Id)。"
            }

            try {
                $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5
                if ($response.StatusCode -eq 200) {
                    [System.IO.File]::WriteAllText($Destination, [string]$response.Content, (New-Object System.Text.UTF8Encoding($false)))
                    Write-Host "$HostName AddOpenApi：$Destination"
                    return
                }
            }
            catch {
                Start-Sleep -Milliseconds 250
            }
        } while ([datetime]::UtcNow -lt $deadline)

        throw "$HostName 在 $StartupTimeoutSeconds 秒內沒有提供 $url；啟動輸出已寫入目前 console。"
    }
    finally {
        if ($null -ne $process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
            $process.WaitForExit(5000) | Out-Null
        }
        if ($null -ne $process) { $process.Dispose() }
    }
}

$storefrontActual = Join-Path $OutputDirectory 'openapi.storefront.generated.json'
$adminActual = Join-Path $OutputDirectory 'openapi.admin.generated.json'
Get-OpenApiDocument -HostName 'storefront' -ProjectName 'GreyGray.Api.Storefront' -Port $StorefrontPort -Destination $storefrontActual
Get-OpenApiDocument -HostName 'admin' -ProjectName 'GreyGray.Api.Admin' -Port $AdminPort -Destination $adminActual

$toolProject = "$PSScriptRoot\OpenApiContractGate\GreyGray.OpenApiContractGate.csproj"
$tool = "$PSScriptRoot\OpenApiContractGate\bin\$Configuration\net10.0\GreyGray.OpenApiContractGate.exe"
if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) {
    Invoke-NativeCommand -FilePath 'dotnet' `
        -ArgumentList @('build', $toolProject, '-c', $Configuration, '--nologo') `
        -WorkingDirectory $repo -EchoOutput | Out-Null
}
$comparisons = @(
    @{ Name = 'storefront'; Expected = "$repo\docs\api\openapi.storefront.yaml"; Actual = $storefrontActual },
    @{ Name = 'admin'; Expected = "$repo\docs\api\openapi.admin.yaml"; Actual = $adminActual }
)

$failed = @()
foreach ($comparison in $comparisons) {
    $result = Invoke-NativeCommand -FilePath $tool `
        -ArgumentList @('--expected', $comparison.Expected, '--actual', $comparison.Actual, '--name', $comparison.Name) `
        -WorkingDirectory $repo -AllowNonZeroExit -EchoOutput
    if ($result.ExitCode -ne 0) { $failed += $comparison.Name }
}

if ($failed.Count -gt 0) {
    throw "OpenAPI 契約 gate 失敗：$($failed -join ', ')。實際產物保留在 $OutputDirectory"
}

Write-Host "✓ AddOpenApi 產物與兩份凍結契約一致"
