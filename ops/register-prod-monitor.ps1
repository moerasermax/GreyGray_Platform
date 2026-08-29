<# 將 GreyGray 兩個 Next.js process／port 指紋冪等登記到 tkflyc-monitor TOML。 #>
[CmdletBinding(SupportsShouldProcess)]
param([Parameter(Mandatory)][string]$ConfigPath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$begin = '# BEGIN GREYGRAY-M1 MANAGED BLOCK'
$end = '# END GREYGRAY-M1 MANAGED BLOCK'
$block = @'
# BEGIN GREYGRAY-M1 MANAGED BLOCK
[[targets]]
name = "GreyGray-Web-Storefront"
  [targets.process]
  process_name = "node.exe"
  cmdline_contains = ["GreyGray.Web.Storefront", "apps\\storefront\\server.js"]
  [targets.port]
  port = 5002
  verify_pid = true
  [targets.http_health]
  url = "http://127.0.0.1:5002/"
  expected_status = 200

[[targets]]
name = "GreyGray-Web-Admin"
  [targets.process]
  process_name = "node.exe"
  cmdline_contains = ["GreyGray.Web.Admin", "apps\\admin\\server.js"]
  [targets.port]
  port = 5003
  verify_pid = true
  [targets.http_health]
  url = "http://127.0.0.1:5003/"
  expected_status = 200
# END GREYGRAY-M1 MANAGED BLOCK
'@

$full = [System.IO.Path]::GetFullPath($ConfigPath)
$parent = Split-Path -Parent $full
if (-not (Test-Path -LiteralPath $parent -PathType Container)) { throw "prod-monitor 設定目錄不存在：$parent" }
$content = if (Test-Path -LiteralPath $full -PathType Leaf) { [IO.File]::ReadAllText($full) } else { "" }
$beginIndex = $content.IndexOf($begin, [StringComparison]::Ordinal)
$endIndex = $content.IndexOf($end, [StringComparison]::Ordinal)
if (($beginIndex -ge 0) -xor ($endIndex -ge 0)) { throw 'prod-monitor managed block 標記不完整，拒絕覆寫。' }

if ($beginIndex -ge 0) {
    $after = $endIndex + $end.Length
    $next = $content.Substring(0, $beginIndex) + $block.TrimEnd() + $content.Substring($after)
    if ($next -eq $content) { Write-Host '已存在 prod-monitor GreyGray process／port 指紋'; return }
}
else {
    if ($content -match '(?m)^name\s*=\s*"GreyGray-Web-(Storefront|Admin)"\s*$') {
        throw '設定已有未受管的 GreyGray target；拒絕產生重複名稱，請人工合併。'
    }
    $separator = if ([string]::IsNullOrWhiteSpace($content)) { '' } else { "`r`n`r`n" }
    $next = $content.TrimEnd() + $separator + $block.TrimEnd() + "`r`n"
}

if ($PSCmdlet.ShouldProcess($full, '登記 GreyGray-Web-Storefront/Admin 監控指紋')) {
    [IO.File]::WriteAllText($full, $next, (New-Object Text.UTF8Encoding($false)))
}
Write-Host "已安裝 prod-monitor GreyGray process／port 指紋：$full"
