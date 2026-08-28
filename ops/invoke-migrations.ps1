<#
    只在部署階段執行指定 SQL migration。高權限密碼只放進 psql 子程序的 PGPASSWORD，
    不寫檔、不寫 NSSM AppEnvironmentExtra，也不傳在命令列上。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string[]]$MigrationFiles,
    [Parameter(Mandatory)][pscredential]$MigrationCredential,
    [string]$DatabaseHost = '127.0.0.1',
    [ValidateRange(1, 65535)][int]$DatabasePort = 5432,
    [Parameter(Mandatory)][string]$DatabaseName,
    [string]$PsqlPath = 'psql.exe',
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$migrationRoot = [System.IO.Path]::GetFullPath("$repo\db\migrations")
. "$PSScriptRoot\lib\Process.ps1"
. "$PSScriptRoot\lib\Deployment.ps1"

if ($MigrationFiles.Count -eq 0) { throw '至少要明確指定一份 migration；若這次不需 migration，deploy.ps1 要明確帶 -SkipMigrations。' }

$resolvedFiles = @()
foreach ($file in $MigrationFiles) {
    $candidate = if ([System.IO.Path]::IsPathRooted($file)) { $file } else { Join-Path $repo $file }
    $fullPath = [System.IO.Path]::GetFullPath($candidate)
    if (-not (Test-PathWithinRoot -Path $fullPath -Root $migrationRoot)) {
        throw "migration 必須位於 db\migrations：$file"
    }
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw "找不到 migration：$fullPath" }
    if ([System.IO.Path]::GetFileName($fullPath) -notmatch '^\d{4}_[A-Za-z0-9_-]+\.sql$') {
        throw "migration 檔名必須是四位序號開頭：$fullPath"
    }
    $resolvedFiles += $fullPath
}

if (($resolvedFiles | Select-Object -Unique).Count -ne $resolvedFiles.Count) {
    throw 'MigrationFiles 含重複檔案。'
}

$resolvedFiles = @($resolvedFiles | Sort-Object)
Write-Host "部署階段 migration（高權限帳號只用於此步驟）："
$resolvedFiles | ForEach-Object { Write-Host "  $([System.IO.Path]::GetFileName($_))" }

if ($ValidateOnly) {
    Write-Host '✓ migration 參數與路徑驗證通過；ValidateOnly 未連線資料庫。'
    return
}

$password = $MigrationCredential.GetNetworkCredential().Password
try {
    foreach ($file in $resolvedFiles) {
        Write-Host "套用 $([System.IO.Path]::GetFileName($file))"
        Invoke-NativeCommand -FilePath $PsqlPath `
            -ArgumentList @('--host', $DatabaseHost, '--port', [string]$DatabasePort,
                '--username', $MigrationCredential.UserName, '--dbname', $DatabaseName,
                '--no-password', '--set', 'ON_ERROR_STOP=1', '--file', $file) `
            -Environment @{ PGPASSWORD = $password; PGCONNECT_TIMEOUT = '10' } `
            -EchoOutput | Out-Null
    }
}
finally {
    $password = $null
}

Write-Host "✓ $($resolvedFiles.Count) 份 migration 已完成；高權限帳號未寫入服務設定。"
