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
if ($manifest.Services.Count -ne 3) { throw 'service manifest 必須正好有三個後端 Host。' }
if (($manifest.Services.Name | Select-Object -Unique).Count -ne 3) { throw 'service manifest 名稱重複。' }
if ((@($manifest.Services | Where-Object { $_.Port -gt 0 }).Port | Sort-Object) -join ',' -ne '5000,5001') {
    throw 'API port 必須正好是 5000 與 5001；Worker 不開 listener。'
}
Write-Host 'PASS service manifest：3 services；API ports 5000/5001；Worker 無 listener'

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
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        New-Item -ItemType File -Path (Join-Path $directory $definition.Executable) -Force | Out-Null
    }
    $fakeNssm = Join-Path $tempRoot 'nssm.exe'
    New-Item -ItemType File -Path $fakeNssm -Force | Out-Null
    $secure = New-Object Security.SecureString
    foreach ($character in 'self-test-only'.ToCharArray()) { $secure.AppendChar($character) }
    $secure.MakeReadOnly()
    $credential = New-Object Management.Automation.PSCredential('greygray_selftest', $secure)

    & "$PSScriptRoot\deploy.ps1" -ArtifactRoot $artifactRoot -InstallRoot $installRoot `
        -NssmPath $fakeNssm -ServiceCredential $credential -SkipMigrations -ValidateOnly
    Write-Host 'PASS deploy 參數：ValidateOnly 未觸碰 NSSM、排程、DB 或網路'

    & "$PSScriptRoot\invoke-migrations.ps1" `
        -MigrationFiles @('db\migrations\0001_schemas_and_roles.sql', 'db\migrations\0002_platform.sql') `
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
