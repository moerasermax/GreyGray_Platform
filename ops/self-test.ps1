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

# AST 解析：先以 UTF-8 讀進來，再 ParseInput——**不要**用 ParseFile。
#
# ParseFile 對「沒有 BOM」的檔案是照**系統 ANSI 代碼頁**讀的。這個 repo 的原始檔一律 UTF-8，
# 其中幾支 dev-only 腳本刻意沒有 BOM；在中文 Windows（ACP=big5，開發機與 YC 都是）上，
# UTF-8 的中文位元組會被 big5 重新分組成雙位元組字元，分出來的位元組可能剛好是 " 或 \，
# 於是完全合法的腳本被判成語法錯誤。BE-42 第二輪實測：五支無 BOM 的在 5.1 全紅、在 7 全綠
# （7 一律當 UTF-8 讀）。明確指定編碼之後，5.1 與 7 讀到的是同一份文字。
# ReadAllText 有 BOM 時會自己去掉它，所以兩種檔案都吃得下。
$parseFailures = @()
$scripts = @(Get-ChildItem -Path $PSScriptRoot -Filter '*.ps1' -Recurse -File)
if ($scripts.Count -eq 0) { throw 'ops 底下找不到任何 *.ps1——這一項等於什麼都沒查。' }
foreach ($script in $scripts) {
    $tokens = $null
    $errors = $null
    $scriptText = [System.IO.File]::ReadAllText($script.FullName, [System.Text.Encoding]::UTF8)
    [void][System.Management.Automation.Language.Parser]::ParseInput(
        $scriptText, $script.FullName, [ref]$tokens, [ref]$errors)
    if ($errors.Count -gt 0) { $parseFailures += "$($script.FullName)：$($errors.Message -join ' | ')" }
}
if ($parseFailures.Count -gt 0) { throw "PowerShell 靜態解析失敗：$($parseFailures -join "`n")" }
Write-Host "PASS PowerShell AST：$($scripts.Count) 個 ops/*.ps1 以 UTF-8 讀入後無語法錯誤"

# 正式機（YC）只有 Windows PowerShell 5.1，而且是中文 Windows（ACP=big5）。
# 理由同上：5.1 讀沒有 BOM 的 UTF-8 會拆錯，所以**會在正式機上執行的腳本**一定要有 UTF-8 BOM。
# self-test.ps1 自己也在清單裡——它就是在 5.1 上跑的那一支。
# dev-only 腳本（檔頭 #Requires -Version 7，只在開發機用 pwsh 跑）不要求，
# 刻意維持它們現在沒有 BOM 的樣子，不要「順手補齊」。
$productionScripts = @(
    'deploy.ps1', 'install-environment.ps1', 'verify-environment.ps1', 'invoke-migrations.ps1',
    'register-prod-monitor.ps1', 'watchdog.ps1', 'environment-self-test.ps1', 'service-manifest.ps1',
    'self-test.ps1', 'install-tunnel.ps1'
) + @(Get-ChildItem -Path (Join-Path $PSScriptRoot 'lib') -Filter '*.ps1' -File |
    ForEach-Object { Join-Path 'lib' $_.Name })
if ($productionScripts.Count -eq 0) { throw '正式機腳本清單是空的——這一項等於什麼都沒查。' }
$missingBom = @()
foreach ($relative in $productionScripts) {
    $bomPath = Join-Path $PSScriptRoot $relative
    if (-not (Test-Path -LiteralPath $bomPath -PathType Leaf)) {
        throw "BOM 斷言列到了不存在的腳本：$bomPath（是清單過期了，不是檔案壞了）"
    }
    $prefix = New-Object byte[] 3
    $stream = [System.IO.File]::OpenRead($bomPath)
    try { [void]$stream.Read($prefix, 0, 3) } finally { $stream.Dispose() }
    if ($prefix[0] -ne 0xEF -or $prefix[1] -ne 0xBB -or $prefix[2] -ne 0xBF) { $missingBom += $relative }
}
if ($missingBom.Count -gt 0) {
    throw ('這些會在正式機（5.1／中文 ACP）執行的腳本沒有 UTF-8 BOM：' + ($missingBom -join '、') +
        '。5.1 會照系統 ANSI 讀它們，中文會被拆錯，甚至拆出 " 或 \ 而變成語法錯誤。')
}
Write-Host "PASS 正式機腳本 BOM：$($productionScripts.Count) 支都有 UTF-8 BOM（5.1 在中文 ACP 下讀無 BOM 的 UTF-8 會拆錯）"

<#
    lib/Secrets.ps1 的兩個產生器，**在目前這個 host 上直接呼叫**。

    為什麼一定要真的叫下去，而不是靜態檢查：這兩個函式原本用
    [RandomNumberGenerator]::Fill($bytes)，那是 .NET Core 3.0／.NET 5+ 才有的靜態方法。
    Windows PowerShell 5.1 跑在 .NET Framework 4.x 上，呼叫下去會得到
    「不包含名為 'Fill' 的方法」。而 deploy.ps1 -ValidateOnly 在更前面就 return，
    所以這兩個函式在 5.1 下**從來沒有被呼叫過**——直到正式機 YC 第一次真的部署，
    17 支 migration 全部成功之後才炸在 deploy.ps1 第 184 行的 New-SecretPassword。
    self-test 本身 5.1 與 7 各跑一趟，這一項就會在兩個 runtime 上各驗一次。
#>
. "$PSScriptRoot\lib\Secrets.ps1"

# New-SecretPassword：Base64 之後把 + / = 換成 x，所以字元集只剩 A-Za-z0-9x，
# 長度是 Base64 的長度＝4 * ceil(位元組數 / 3)（含補位）。
foreach ($length in @(32, 16, 48)) {
    $expectedLength = 4 * [math]::Ceiling($length / 3)
    $password = New-SecretPassword -Length $length
    if ($password.Length -ne $expectedLength) {
        throw "New-SecretPassword -Length $length 長度應為 $expectedLength，實得 $($password.Length)。"
    }
    if ($password -notmatch '^[A-Za-z0-9x]+$') {
        throw "New-SecretPassword -Length $length 出現 A-Za-z0-9x 以外的字元（+ / = 應已換成 x）：$password"
    }
}
if ((New-SecretPassword) -eq (New-SecretPassword)) { throw 'New-SecretPassword 連續兩次結果相同——不是亂數。' }

# New-DataProtectionKey：**不做任何字元替換**，Base64 解碼後必須正好 32 bytes
# （IdentityDataProtector 要的 AES-256 金鑰長度）。
$dataProtectionKey = New-DataProtectionKey
$decodedKey = [Convert]::FromBase64String($dataProtectionKey)
if ($decodedKey.Length -ne 32) {
    throw "New-DataProtectionKey Base64 解碼後應為 32 bytes，實得 $($decodedKey.Length)。"
}
if ($dataProtectionKey -notmatch '^[A-Za-z0-9+/]+={0,2}$') {
    throw "New-DataProtectionKey 不該做字元替換，應為標準 Base64：$dataProtectionKey"
}
if ((New-DataProtectionKey) -eq (New-DataProtectionKey)) { throw 'New-DataProtectionKey 連續兩次結果相同——不是亂數。' }

Write-Host "PASS lib/Secrets.ps1 亂數產生器（host PowerShell $($PSVersionTable.PSVersion)）：New-SecretPassword 長度／字元集正確、New-DataProtectionKey 解碼 32 bytes，各兩次都不同"

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

<#
    「乾淨機器第一次部署」——機器上沒有任何舊行程、沒有任何 port 被佔。

    這條路 deploy.ps1 -ValidateOnly 驗不到（它在複製 artifact 之前就 return），
    開發機也從來沒用 deploy.ps1 部署過，所以直到正式機 YC 第二次真跑、
    17 支 migration 與全部機密都備好之後，才炸在
    「無法將引數繫結至 'Tokens' 參數，因為它是一個空陣列」——
    Mandatory 參數預設拒收空集合，而乾淨機器上 Get-ManagedApplicationTokens 回的就是 @()。
    5.1 與 7 都一樣。這裡把「空集合」這條路釘住，兩個 host 各驗一次。
#>
$cleanRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("greygray-clean-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $cleanRoot -Force | Out-Null
try {
    # ① 沒有任何舊行程要等：空陣列必須合法，而且立刻回來（不能等滿 TimeoutSeconds）。
    $waitStopwatch = [Diagnostics.Stopwatch]::StartNew()
    Wait-ProcessTokensExit -Tokens @() -InstallRoot $cleanRoot -TimeoutSeconds 30
    $waitStopwatch.Stop()
    if ($waitStopwatch.Elapsed.TotalSeconds -gt 5) {
        throw "Wait-ProcessTokensExit -Tokens @() 應立刻 return，實際等了 $($waitStopwatch.Elapsed.TotalSeconds) 秒。"
    }

    # ② 沒人聽的 port：Get-PortOwnerProcess 回空、Assert-PortReleased 直接放行。
    $freePort = 0
    foreach ($candidatePort in 49500..49600) {
        if (@(Get-NetTCPConnection -State Listen -LocalPort $candidatePort -ErrorAction SilentlyContinue).Count -eq 0) {
            $freePort = $candidatePort
            break
        }
    }
    if ($freePort -eq 0) { throw '49500-49600 找不到任何空 port——這一項等於什麼都沒查。' }
    if (@(Get-PortOwnerProcess -Port $freePort).Count -ne 0) {
        throw "Get-PortOwnerProcess -Port $freePort 應為空。"
    }
    Assert-PortReleased -Port $freePort -InstallRoot $cleanRoot

    # ③ releases\ 還不存在（或只有殘留目錄）：列舉不可以炸，回空集合即可。
    $releasesRoot = Join-Path $cleanRoot 'releases'
    if (@(Get-ChildItem -LiteralPath $releasesRoot -Directory -ErrorAction SilentlyContinue).Count -ne 0) {
        throw '不存在的 releases\ 應列舉出空集合。'
    }

    # ④ 沒有這個名字的行程：Get-Process 不可以炸，回空集合即可。
    if (@(Get-Process -Name 'GreyGray.Api.Storefront.selftest-absent' -ErrorAction SilentlyContinue).Count -ne 0) {
        throw '不存在的行程名稱應回空集合。'
    }

    Write-Host "PASS 乾淨機器第一次部署（host PowerShell $($PSVersionTable.PSVersion)）：Wait-ProcessTokensExit 收空陣列並立刻 return；空 port $freePort 直接放行；releases\ 與行程名稱查不到時回空集合"
}
finally {
    if (Test-Path -LiteralPath $cleanRoot) { Remove-Item -LiteralPath $cleanRoot -Recurse -Force }
}

<#
    Clear-NssmAppParameters：正式機那一版 nssm（2.24-101-g897c7ad）的
    `reset <svc> AppParameters` 一定 heap corruption（exit -1073740940），
    所以 deploy.ps1 改成「先 get，真的有值才寫 registry 清掉，再 get 驗證」。

    這裡**只在 HKCU 的暫存鍵上驗**：不碰真的 nssm、不碰 HKLM，
    「目前值」用可注入的 scriptblock 餵進去。
#>
$selfTestRegistryRoot = "HKCU:\Software\GreyGray-selftest\$([guid]::NewGuid().ToString('N'))"
try {
    $selfTestServiceName = 'GreyGray-Selftest-Service'
    $selfTestParametersKey = Join-Path (Join-Path $selfTestRegistryRoot $selfTestServiceName) 'Parameters'
    New-Item -Path $selfTestParametersKey -Force | Out-Null
    New-ItemProperty -LiteralPath $selfTestParametersKey -Name 'AppParameters' -Value 'x' -PropertyType String -Force | Out-Null

    # 「目前值」直接讀那個暫存鍵，所以下面驗到的是真的寫進去了，不是回傳值自己說了算。
    $readSelfTestValue = {
        $item = Get-ItemProperty -LiteralPath $selfTestParametersKey -Name 'AppParameters' -ErrorAction SilentlyContinue
        if ($null -eq $item) { return '' }
        return [string]$item.AppParameters
    }.GetNewClosure()

    # ① 有值 → 清掉、回 $true，而且 registry 真的變空。
    $cleared = Clear-NssmAppParameters -ServiceName $selfTestServiceName `
        -GetAppParameters $readSelfTestValue -ServicesRegistryRoot $selfTestRegistryRoot
    if (-not $cleared) { throw 'Clear-NssmAppParameters 對非空值應回 $true。' }
    if ((& $readSelfTestValue) -ne '') { throw "AppParameters 應已清空，實得 '$(& $readSelfTestValue)'。" }

    # ② 已經是空的（剛 nssm install 的服務）→ 什麼都不做、回 $false。
    $clearedAgain = Clear-NssmAppParameters -ServiceName $selfTestServiceName `
        -GetAppParameters $readSelfTestValue -ServicesRegistryRoot $selfTestRegistryRoot
    if ($clearedAgain) { throw 'Clear-NssmAppParameters 對本來就空的值不應回報清過。' }

    # ③ UTF-16 的 NUL 位元組不可以被當成「有值」——否則每次部署都白寫一次 registry。
    $nulOnly = [string][char]0 + "`r`n"
    $clearedNul = Clear-NssmAppParameters -ServiceName 'GreyGray-Selftest-NoSuchService' `
        -GetAppParameters { $nulOnly }.GetNewClosure() -ServicesRegistryRoot $selfTestRegistryRoot
    if ($clearedNul) { throw '只含 NUL／空白的輸出應視為空值。' }

    # ④ 有值但找不到 registry 鍵 → 必須 throw，不可以默默跳過。
    $missingKeyThrew = $false
    try {
        Clear-NssmAppParameters -ServiceName 'GreyGray-Selftest-NoSuchService' `
            -GetAppParameters { 'still-here' } -ServicesRegistryRoot $selfTestRegistryRoot | Out-Null
    }
    catch { $missingKeyThrew = $true }
    if (-not $missingKeyThrew) { throw '找不到 registry 鍵時應 throw。' }

    Write-Host "PASS Clear-NssmAppParameters（host PowerShell $($PSVersionTable.PSVersion)）：有值→寫 registry 清空並回 true；已空→不動作回 false；純 NUL 視為空；缺 registry 鍵→throw（全程只碰 HKCU 暫存鍵，未觸碰 nssm 或 HKLM）"
}
finally {
    $selfTestRegistryParent = 'HKCU:\Software\GreyGray-selftest'
    if (Test-Path -LiteralPath $selfTestRegistryRoot) {
        Remove-Item -LiteralPath $selfTestRegistryRoot -Recurse -Force
    }
    # 自己建的父節點空了就一起收掉，不要在使用者的 registry 留垃圾。
    if ((Test-Path -LiteralPath $selfTestRegistryParent) -and
        (@(Get-ChildItem -LiteralPath $selfTestRegistryParent -ErrorAction SilentlyContinue).Count -eq 0)) {
        Remove-Item -LiteralPath $selfTestRegistryParent -Force
    }
}

<#
    deploy.ps1 裡不准出現 GetNewClosure()。

    GetNewClosure() 把 scriptblock 綁到一個新的**動態模組**，那個模組的 parent 是
    **global** scope。deploy.ps1 以 script 身分執行時，自己定義的函式（Invoke-Nssm、
    Test-DeploymentTokenOwnership…）在 **script** scope，不在 global——於是 lib 函式
    `& $ScriptBlock` 呼叫下去就變成
    「無法辨識 'Invoke-Nssm' 詞彙是否為 Cmdlet、函數、指令檔或可執行程式的名稱。」
    5.1 與 7 都一樣。YC 第四次真跑（migration ＋ 機密 ＋ nssm install 全過之後）就是這樣炸的，
    而且同一個寫法還藏了第二份在 port ownership validator 裡——那份要「port 被自己的舊行程佔著」
    才會走到，dry-run 與乾淨機器都碰不到。

    ★★ 為什麼開發機試不出來：它只在「deploy.ps1 被**另一支腳本**呼叫」時才炸。
    正式機是 C:\Source\yc-deploy.ps1 裡 `& .\ops\deploy.ps1 @common …`；
    直接 `-File ops\deploy.ps1` 跑的時候，script scope 的 parent 剛好就是 global，
    動態模組因此湊巧看得到函式。下面 ③ 的最小重現**兩種呼叫方式都跑**，
    因為「只測直接呼叫」正是這個 bug 溜過去的原因。

    這一項是機械把關：AST 掃過去，一個都不准有。③ 再用最小重現證明這條規則本身是真的
    （不然這就只是一條沒人驗過的禁令）。
#>
$deployScriptPath = Join-Path $PSScriptRoot 'deploy.ps1'
$deployTokens = $null
$deployErrors = $null
$deployAst = [System.Management.Automation.Language.Parser]::ParseInput(
    [System.IO.File]::ReadAllText($deployScriptPath, [System.Text.Encoding]::UTF8),
    $deployScriptPath, [ref]$deployTokens, [ref]$deployErrors)
if ($deployErrors.Count -gt 0) { throw "deploy.ps1 解析失敗：$($deployErrors.Message -join ' | ')" }
$closureCalls = @($deployAst.FindAll({
    param($node)
    $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
    "$($node.Member)" -eq 'GetNewClosure'
}, $true))
if ($closureCalls.Count -ne 0) {
    throw ("ops/deploy.ps1 不准使用 GetNewClosure()（動態模組看不到 script scope 的函式）；出現在第 " +
        (@($closureCalls | ForEach-Object { $_.Extent.StartLineNumber }) -join '、') + ' 行。')
}

<#
    ③ 最小重現。inner.ps1 就是 deploy.ps1 的形狀：自己定義函式（Get-Marker＝script 函式、
    Invoke-Validator＝lib 函式的角色），把 scriptblock 交給後者 `&` 呼叫；
    closure 與 plain 兩版只差一個 .GetNewClosure()。

    這裡刻意用兩種呼叫方式：
      · 巢狀（self-test.ps1 用 `& $inner` 呼叫它，等同 yc-deploy.ps1 → deploy.ps1）→ 應該炸
      · 直接（同一個 host 起一個新 process `-File inner.ps1`）→ 湊巧會過，
        這正是「開發機試不出來」的原因，所以也要釘住，免得以後有人拿直接呼叫當作證據。
    全程用目前這個 host，所以 5.1 與 7 各驗一次。
#>
$closureProbeDir = Join-Path ([System.IO.Path]::GetTempPath()) ("greygray-closure-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $closureProbeDir -Force | Out-Null
try {
    $innerScript = @'
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Invoke-Validator([scriptblock]$V) { return & $V }
function Get-Marker { return 'script-scope-visible' }
foreach ($mode in @('closure', 'plain')) {
    $sb = if ($mode -eq 'closure') { { Get-Marker }.GetNewClosure() } else { { Get-Marker } }
    try { "$mode=$(Invoke-Validator -V $sb)" }
    catch { "$mode=THROW:$($_.FullyQualifiedErrorId)" }
}
'@
    $innerPath = Join-Path $closureProbeDir 'inner.ps1'
    [System.IO.File]::WriteAllText($innerPath, $innerScript, (New-Object System.Text.UTF8Encoding($true)))

    # (a) 巢狀呼叫——self-test.ps1 自己就是「外層腳本」。
    $nested = @(& $innerPath)
    if (($nested -join '；') -notlike '*closure=THROW:CommandNotFoundException*') {
        throw "巢狀呼叫時 GetNewClosure() 版應該找不到 script scope 的函式，實得：$($nested -join '；')"
    }
    if (($nested -join '；') -notlike '*plain=script-scope-visible*') {
        throw "巢狀呼叫時 plain scriptblock 應該看得到 script scope 的函式，實得：$($nested -join '；')"
    }

    # (b) 直接呼叫——用目前這個 host 另起一個 process。
    $hostExecutable = (Get-Process -Id $PID).Path
    $direct = @(& $hostExecutable -NoProfile -File $innerPath)
    if (($direct -join '；') -notlike '*closure=script-scope-visible*') {
        throw "直接 -File 呼叫時 GetNewClosure() 版預期會湊巧通過（這正是開發機試不出來的原因），實得：$($direct -join '；')"
    }

    Write-Host "PASS deploy.ps1 無 GetNewClosure（host PowerShell $($PSVersionTable.PSVersion)）：AST 掃到 0 個；最小重現＝巢狀呼叫 [$($nested -join '；')]、直接 -File [$($direct -join '；')]"
}
finally {
    if (Test-Path -LiteralPath $closureProbeDir) { Remove-Item -LiteralPath $closureProbeDir -Recurse -Force }
}

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
            -ServiceCredential $credential -SkipMigrations -ValidateOnly `
            -StorefrontPublicOrigin 'https://greygray.shop' -StorefrontPublicApiOrigin 'https://greygray.shop'
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
        -ServiceCredential $credential -SkipMigrations -ValidateOnly `
        -StorefrontPublicOrigin 'https://greygray.shop' -StorefrontPublicApiOrigin 'https://greygray.shop'
    Write-Host 'PASS deploy 參數：ValidateOnly 未觸碰 NSSM、排程、DB 或網路'

    # BE-42：兩個對外 origin 是 Mandatory，而且格式一定要驗。
    # 這一條守的是「正式機不准猜網址」——猜錯的兩種症狀（付不了款、付完回不了商店）
    # 都不會有錯誤訊息，只會看起來像「金流壞了」。四種壞值各代表一類真的會打錯的輸入。
    $badOrigins = @('https://greygray.shop/', 'http://greygray.shop', 'greygray.shop', 'https://greygray.shop/v1')
    foreach ($parameterName in @('StorefrontPublicOrigin', 'StorefrontPublicApiOrigin')) {
        foreach ($badOrigin in $badOrigins) {
            $deployArguments = @{
                ArtifactRoot              = $artifactRoot
                InstallRoot               = $installRoot
                NssmPath                  = $fakeNssm
                NodePath                  = $fakeNode
                ServiceCredential         = $credential
                StorefrontPublicOrigin    = 'https://greygray.shop'
                StorefrontPublicApiOrigin = 'https://greygray.shop'
            }
            $deployArguments[$parameterName] = $badOrigin
            $badOriginRejected = $false
            try {
                & "$PSScriptRoot\deploy.ps1" @deployArguments -SkipMigrations -ValidateOnly
            }
            catch {
                # 要求訊息指名是哪一個參數：兩個參數共用同一個驗證函式，
                # 只看「有沒有 throw」的話，兩個值對調也會通過。
                $badOriginRejected = $_.Exception.Message -match $parameterName
            }
            if (-not $badOriginRejected) {
                throw "deploy 接受了不合法的 -$parameterName（$badOrigin），或錯誤訊息沒指名參數。"
            }
        }
    }
    Write-Host 'PASS deploy 對外 origin 負向測試：兩個參數 × 四種壞值（結尾斜線／非 https／相對網址／帶路徑）全部擋下'

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
    Invoke-NativeCommand -FilePath 'dotnet' `
        -ArgumentList @('build', $toolProject, '-c', $Configuration, '--nologo') `
        -WorkingDirectory $repo -EchoOutput | Out-Null
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
  /v1/future:
    post:
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

    $catalog = Join-Path $tempRoot 'catalog.md'
    $catalogFixture = @'
### Storefront（:5000）

| 方法 | 路徑 | 里程碑 | 說明 |
|---|---|---|---|
| `GET` | `/v1/example` | M1a | current |
| `POST` | `/v1/future` | M1b | future |

### Admin（:5001）
'@
    [System.IO.File]::WriteAllText($catalog, $catalogFixture, (New-Object System.Text.UTF8Encoding($false)))
    $m1aActual = Join-Path $tempRoot 'm1a-actual.yaml'
    $m1aFixture = @'
openapi: 3.1.0
info:
  title: Self Test Actual
  version: "1.0.0"
paths:
  /v1/example:
    get:
      responses:
        '204':
          description: No content
'@
    [System.IO.File]::WriteAllText($m1aActual, $m1aFixture, (New-Object System.Text.UTF8Encoding($false)))
    $coverage = Invoke-NativeCommand -FilePath $tool `
        -ArgumentList @(
            '--expected', $frozen, '--actual', $m1aActual, '--name', 'storefront',
            '--milestone', 'M1a', '--catalog', $catalog) `
        -WorkingDirectory $repo -AllowNonZeroExit -EchoOutput
    if ($coverage.ExitCode -ne 0) { throw 'M1a coverage 不應被尚未實作的 M1b operation 擋住。' }

    $missingM1a = Join-Path $tempRoot 'missing-m1a.yaml'
    $missingFixture = $m1aFixture.Replace('/v1/example:', '/v1/future:').Replace('    get:', '    post:')
    [System.IO.File]::WriteAllText($missingM1a, $missingFixture, (New-Object System.Text.UTF8Encoding($false)))
    $missingCoverage = Invoke-NativeCommand -FilePath $tool `
        -ArgumentList @(
            '--expected', $frozen, '--actual', $missingM1a, '--name', 'storefront',
            '--milestone', 'M1a', '--catalog', $catalog) `
        -WorkingDirectory $repo -AllowNonZeroExit
    if ($missingCoverage.ExitCode -eq 0) { throw 'M1a coverage gate 沒有抓到缺少的 M1a operation。' }
    Write-Host 'PASS OpenAPI milestone gate：忽略未到期 M1b；缺少 M1a operation 時紅燈'
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
