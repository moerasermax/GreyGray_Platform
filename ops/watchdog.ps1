[CmdletBinding()]
param(
    [string]$ManifestPath = (Join-Path $PSScriptRoot 'service-manifest.ps1'),
    [ValidateRange(1, 120)][int]$StartTimeoutSeconds = 30
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$manifest = & $ManifestPath

foreach ($definition in $manifest.Services) {
    $service = Get-Service -Name $definition.Name -ErrorAction Stop
    if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Running) {
        Write-Host "$($definition.Name) 不是 Running，watchdog 嘗試啟動。"
        Start-Service -Name $definition.Name -ErrorAction Stop
        $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running,
            [timespan]::FromSeconds($StartTimeoutSeconds))
    }
}

Write-Host "✓ $($manifest.Services.Count) 個 GreyGray NSSM service 都是 Running"
