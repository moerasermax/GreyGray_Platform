<# 安全的本機自驗：只寫 OS temp，不碰 NSSM、排程、DB 或 YC。 #>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\lib\Process.ps1"
. "$PSScriptRoot\lib\Deployment.ps1"

$parseFailures = @()
foreach ($script in Get-ChildItem -Path $PSScriptRoot -Filter '*.ps1' -Recurse -File) {
    $tokens = $null
    $errors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { $parseFailures += "$($script.FullName)：$($errors.Message -join ' | ')" }
}
if ($parseFailures.Count -gt 0) { throw "PowerShell 靜態解析失敗：$($parseFailures -join "`n")" }
Write-Host 'PASS PowerShell AST：所有 ops/*.ps1 無語法錯誤'

$manifest = & "$PSScriptRoot\service-manifest.ps1"
if ($manifest.Services.Count -ne 5) { throw 'service manifest 必須正好有五個 service。' }
if (($manifest.Services.Name | Select-Object -Unique).Count -ne 5) { throw 'service manifest 名稱重複。' }
if ((@($manifest.Services.Port | Sort-Object) -join ',') -ne '0,5000,5001,5002,5003') {
    throw 'service ports 必須正好是 Worker=0 與 5000..5003。'
}
$worker = @($manifest.Services | Where-Object { $_.Name -eq 'GreyGray-Worker' -and $_.Port -eq 0 })
$webServices = @($manifest.Services | Where-Object { $_.Kind -eq 'NextStandalone' })
if ($worker.Count -ne 1 -or $webServices.Count -ne 2) { throw 'Worker/NextStandalone 數量不正確。' }
if ((@($webServices.Name | Sort-Object) -join ',') -ne 'GreyGray-Web-Admin,GreyGray-Web-Storefront') {
    throw '前端 NSSM 名稱必須明確使用 GreyGray-Web-*。'
}
foreach ($web in $webServices) {
    if ($web.Executable -ne 'node.exe' -or @($web.Arguments).Count -ne 1 -or $web.Arguments[0] -ne 'server.js') {
        throw "$($web.Name) 必須直接 node.exe + server.js，不可走 npm/pnpm shim。"
    }
}
Write-Host 'PASS service manifest：5 services；ports 5000..5003 + Worker 0；Next 直接 node.exe + server.js'

$boundary = [datetime]::UtcNow
if (-not (Test-ProcessStartedAfter -ProcessStartTime $boundary.AddSeconds(1) -RestartBoundaryUtc $boundary)) {
    throw 'StartTime 新行程判斷應通過但未通過。'
}
if (Test-ProcessStartedAfter -ProcessStartTime $boundary.AddSeconds(-1) -RestartBoundaryUtc $boundary) {
    throw 'StartTime 舊行程判斷應拒絕但未拒絕。'
}
Write-Host 'PASS process takeover：新 StartTime 通過；舊 StartTime 被拒絕'

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("greygray-ops-selftest-" + [guid]::NewGuid().ToString('N'))
try {
    $artifactRoot = Join-Path $tempRoot 'artifacts'
    $installRoot = Join-Path $tempRoot 'install'
    New-Item -ItemType Directory -Path $artifactRoot, $installRoot -Force | Out-Null
    foreach ($definition in $manifest.Services) {
        $directory = Join-Path $artifactRoot $definition.ArtifactDirectory
        $entryPoint = Join-Path $directory $definition.ArtifactEntryPoint
        New-Item -ItemType Directory -Path (Split-Path -Parent $entryPoint) -Force | Out-Null
        New-Item -ItemType File -Path $entryPoint -Force | Out-Null
    }
    $fakeNssm = Join-Path $tempRoot 'nssm.exe'
    New-Item -ItemType File -Path $fakeNssm -Force | Out-Null
    $fakeRuntime = Join-Path $tempRoot 'runtime'
    New-Item -ItemType Directory -Path $fakeRuntime -Force | Out-Null
    $fakeNode = Join-Path $fakeRuntime 'node.exe'
    $fakePnpm = Join-Path $fakeRuntime 'pnpm.cmd'
    New-Item -ItemType File -Path $fakeNode, $fakePnpm -Force | Out-Null
    $secure = New-Object Security.SecureString
    foreach ($character in 'self-test-only'.ToCharArray()) { $secure.AppendChar($character) }
    $secure.MakeReadOnly()
    $credential = New-Object Management.Automation.PSCredential('greygray_selftest', $secure)

    $oldWebEntryPoint = Join-Path $installRoot 'releases\old\GreyGray.Web.Storefront\apps\storefront\server.js'
    New-Item -ItemType Directory -Path (Split-Path -Parent $oldWebEntryPoint) -Force | Out-Null
    New-Item -ItemType File -Path $oldWebEntryPoint -Force | Out-Null
    $validNodeToken = [pscustomobject]@{
        Path = $fakeNode
        CommandLine = '"' + $fakeNode + '" "' + $oldWebEntryPoint + '"'
    }
    if (-not (Test-NodeProcessIdentity -Token $validNodeToken -NodePath $fakeNode -EntryPointPath $oldWebEntryPoint)) {
        throw '外部 node.exe + 精確 server.js 應可建立 ownership 證據。'
    }
    $wrongServerToken = [pscustomobject]@{
        Path = $fakeNode
        CommandLine = '"' + $fakeNode + '" "' + ($oldWebEntryPoint + '.old') + '"'
    }
    if (Test-NodeProcessIdentity -Token $wrongServerToken -NodePath $fakeNode -EntryPointPath $oldWebEntryPoint) {
        throw '錯誤 server.js commandline 不可被認成 GreyGray Node service。'
    }
    $wrongNodeToken = [pscustomobject]@{
        Path = (Join-Path $tempRoot 'foreign\node.exe')
        CommandLine = 'node.exe "' + $oldWebEntryPoint + '"'
    }
    if (Test-NodeProcessIdentity -Token $wrongNodeToken -NodePath $fakeNode -EntryPointPath $oldWebEntryPoint) {
        throw '外部 node path 不可只因 server.js 相同就被認領。'
    }
    Write-Host 'PASS Node ownership：可信 node.exe + 精確 server.js；外部 node/錯 commandline 均拒絕'

    $missingNodeRejected = $false
    try {
        & "$PSScriptRoot\deploy.ps1" -ArtifactRoot $artifactRoot -InstallRoot $installRoot `
            -NssmPath $fakeNssm -NodePath (Join-Path $tempRoot 'missing\node.exe') `
            -ServiceCredential $credential -SkipMigrations -ValidateOnly
    }
    catch {
        $missingNodeRejected = $_.Exception.Message -match 'node\.exe' -and $_.Exception.Message -match 'M-1'
    }
    if (-not $missingNodeRejected) { throw 'deploy 找不到 node.exe 時沒有明確 M-1 fail-fast。' }
    Write-Host 'PASS node 缺失負向測試：deploy 明確 M-1 fail-fast，未 silently skip'

    $missingPnpmRejected = $false
    try {
        & "$PSScriptRoot\build-frontends.ps1" -NodePath $fakeNode `
            -PnpmPath (Join-Path $tempRoot 'missing\pnpm.cmd') -ValidateOnly
    }
    catch {
        $missingPnpmRejected = $_.Exception.Message -match 'pnpm' -and $_.Exception.Message -match 'M-1'
    }
    if (-not $missingPnpmRejected) { throw 'frontend build 找不到 pnpm 時沒有明確 M-1 fail-fast。' }
    Write-Host 'PASS pnpm 缺失負向測試：frontend build 明確 M-1 fail-fast'

    & "$PSScriptRoot\build-frontends.ps1" -NodePath $fakeNode -PnpmPath $fakePnpm -ValidateOnly
    Write-Host 'PASS frontend build 參數：ValidateOnly 未執行 pnpm 或寫 artifacts'

    & "$PSScriptRoot\deploy.ps1" -ArtifactRoot $artifactRoot -InstallRoot $installRoot `
        -NssmPath $fakeNssm -NodePath $fakeNode `
        -ServiceCredential $credential -SkipMigrations -ValidateOnly
    Write-Host 'PASS deploy 參數：ValidateOnly 未觸碰 NSSM、排程、DB 或網路'

    & "$PSScriptRoot\invoke-migrations.ps1" `
        -MigrationFiles @(
            'db\migrations\0001_schemas_and_roles.sql',
            'db\migrations\0002_platform.sql',
            'db\migrations\0003_channel_seams.sql',
            'db\migrations\0004_hello_world.sql') `
        -MigrationCredential $credential -DatabaseName 'greygray_selftest' -ValidateOnly
    Write-Host 'PASS migration 參數：只接受 db/migrations 明確檔案；ValidateOnly 未連線 DB'

    $toolProject = "$PSScriptRoot\OpenApiContractGate\GreyGray.OpenApiContractGate.csproj"
    $tool = "$PSScriptRoot\OpenApiContractGate\bin\$Configuration\net10.0\GreyGray.OpenApiContractGate.exe"
    if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) {
        Invoke-NativeCommand -FilePath 'dotnet' `
            -ArgumentList @('build', $toolProject, '-c', $Configuration, '--nologo') `
            -WorkingDirectory $repo -EchoOutput | Out-Null
    }
    $frozen = Join-Path $tempRoot 'valid.yaml'
    $validFixture = @'
openapi: 3.1.0
info:
  title: Self Test
  version: "1.0.0"
paths:
  /v1/example:
    get:
      responses:
        '204':
          description: No content
'@
    [System.IO.File]::WriteAllText($frozen, $validFixture, (New-Object System.Text.UTF8Encoding($false)))
    $same = Invoke-NativeCommand -FilePath $tool `
        -ArgumentList @('--expected', $frozen, '--actual', $frozen, '--name', 'self-test-same') `
        -WorkingDirectory $repo -AllowNonZeroExit -EchoOutput
    if ($same.ExitCode -ne 0) { throw 'OpenAPI gate 對同一份文件應通過。' }

    $drifted = Join-Path $tempRoot 'drifted.yaml'
    $content = [System.IO.File]::ReadAllText($frozen).Replace('version: "1.0.0"', 'version: "9.9.9"')
    [System.IO.File]::WriteAllText($drifted, $content, (New-Object System.Text.UTF8Encoding($false)))
    $different = Invoke-NativeCommand -FilePath $tool `
        -ArgumentList @('--expected', $frozen, '--actual', $drifted, '--name', 'self-test-drift') `
        -WorkingDirectory $repo -AllowNonZeroExit
    if ($different.ExitCode -eq 0) { throw 'OpenAPI gate 沒有抓到注入的 version drift。' }
    Write-Host 'PASS OpenAPI gate：同檔通過；注入 version drift 後確實紅燈'
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        $resolvedTemp = [System.IO.Path]::GetFullPath($tempRoot)
        $systemTemp = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
        if (-not $resolvedTemp.StartsWith($systemTemp, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "拒絕清除非 temp 路徑：$resolvedTemp"
        }
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}

Write-Host '✓ ops 安全自驗全部通過'
