<#
    YC 原生部署：已在開發機產出的 win-x64 self-contained artifacts → 版本化 release → NSSM。
    本腳本不做 build（正式機不得建置），也不會把 migration 高權限帳密寫進任何持久設定。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArtifactRoot,
    [Parameter(Mandatory)][string]$InstallRoot,
    [Parameter(Mandatory)][string]$NssmPath,
    [Parameter(Mandatory)][pscredential]$ServiceCredential,
    # 前台網頁的對外 origin（綠界完成頁「返回商店」用，#33）與前台 API 的對外 origin
    # （綠界 ReturnURL 用，BE-42）。ADR-031 的拓樸下兩者同一個主機名稱，
    # 但仍然分成兩個參數：日後若把 API 拆到別的主機名稱，這裡不必再改一次腳本。
    # 刻意是 Mandatory：正式機不准猜網址（跟 #33「不預設 localhost」同一個原則）——
    # 猜錯的話症狀是「付款永遠停在待付款」或「付完款回不了商店」，兩個都不會有錯誤訊息。
    [Parameter(Mandatory)][string]$StorefrontPublicOrigin,
    [Parameter(Mandatory)][string]$StorefrontPublicApiOrigin,
    [string]$NodePath,
    [pscredential]$MigrationCredential,
    [string[]]$MigrationFiles = @(),
    [switch]$SkipMigrations,
    [string]$DatabaseHost = '127.0.0.1',
    [ValidateRange(1, 65535)][int]$DatabasePort = 5432,
    # 'greygray' 是既有 bug：全 repo 沒有任何 CREATE DATABASE，
    # 實際一直用的是 PostgreSQL 內建的 postgres 資料庫
    # （install-dev-environment.ps1 明確傳 'postgres'）。deploy.ps1 從沒被真的跑過，
    # 這個錯的預設值才沒露餡；下面新接的 ConnectionStrings 會用這個值，不修就全部指向不存在的 DB。
    [string]$DatabaseName = 'postgres',
    [string]$PsqlPath = 'psql.exe',
    [string]$WatchdogTaskName = 'GreyGray-Watchdog',
    [string]$WatchdogUserSid,
    [ValidateRange(5, 180)][int]$HealthTimeoutSeconds = 60,
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\lib\Process.ps1"
. "$PSScriptRoot\lib\Deployment.ps1"
. "$PSScriptRoot\lib\Node.ps1"
. "$PSScriptRoot\lib\Secrets.ps1"
$manifestPath = "$PSScriptRoot\service-manifest.ps1"
$manifest = & $manifestPath

function Assert-PublicOrigin {
    <#
        正式機的對外 origin：必須是絕對 https 網址、沒有結尾斜線、沒有路徑／查詢字串。
        不自動修正而是直接 throw——這兩個值會被 Host 拿去跟 '/payment/result'、
        '/v1/webhooks/ecpay' 直接串接，悄悄「幫忙」修掉輸入錯誤只會讓錯的部署看起來成功。
        （Windows PowerShell 5.1 可執行：不用 ??、?.、三元運算子。）
    #>
    param(
        [Parameter(Mandatory)][string]$Name,
        [string]$Value
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "-$Name 不可空白；正式機的對外網址不猜。"
    }
    if ($Value.EndsWith('/')) {
        throw "-$Name 不可有結尾斜線：$Value"
    }
    $uri = $null
    if (-not [System.Uri]::TryCreate($Value, [System.UriKind]::Absolute, [ref]$uri)) {
        throw "-$Name 必須是絕對網址（例如 https://greygray.shop）：$Value"
    }
    if ($uri.Scheme -ne 'https') {
        throw "-$Name 必須是 https（綠界只接受 80／443，且正式機一律走 TLS）：$Value"
    }
    if ($uri.AbsolutePath -ne '/' -or $uri.Query -or $uri.Fragment) {
        throw "-$Name 只接受 scheme + 主機名稱，不可帶路徑／查詢字串：$Value"
    }
}

Assert-PublicOrigin -Name 'StorefrontPublicOrigin' -Value $StorefrontPublicOrigin
Assert-PublicOrigin -Name 'StorefrontPublicApiOrigin' -Value $StorefrontPublicApiOrigin

if ($SkipMigrations -and $MigrationFiles.Count -gt 0) { throw '-SkipMigrations 與 -MigrationFiles 不可同時使用。' }
if (-not $SkipMigrations) {
    if ($null -eq $MigrationCredential) { throw '未帶 -SkipMigrations 時，必須提供部署專用 -MigrationCredential。' }
    if ($MigrationFiles.Count -eq 0) { throw '未帶 -SkipMigrations 時，必須明確列出 -MigrationFiles；腳本不猜哪些 migration 已套用。' }
}

$artifactFull = [System.IO.Path]::GetFullPath($ArtifactRoot)
$installFull = [System.IO.Path]::GetFullPath($InstallRoot)
if ($artifactFull.Equals($installFull, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'ArtifactRoot 與 InstallRoot 不可相同。'
}

$serviceNames = @($manifest.Services | ForEach-Object { $_.Name })
if (($serviceNames | Select-Object -Unique).Count -ne $serviceNames.Count) { throw 'service manifest 有重複的 Name。' }
$nodeServices = @($manifest.Services | Where-Object { $_.Kind -eq 'NextStandalone' })
if ($nodeServices.Count -ne 2) { throw 'service manifest 必須正好有兩個 NextStandalone service。' }
$resolvedNodePath = Resolve-NodeExecutable -NodePath $NodePath
foreach ($definition in $manifest.Services) {
    $artifactDirectory = Join-Path $artifactFull $definition.ArtifactDirectory
    $entryPoint = [System.IO.Path]::GetFullPath((Join-Path $artifactDirectory $definition.ArtifactEntryPoint))
    if (-not (Test-PathWithinRoot -Path $entryPoint -Root $artifactDirectory)) {
        throw "artifact entrypoint 越出自己的目錄：$entryPoint"
    }
    if (-not (Test-Path -LiteralPath $entryPoint -PathType Leaf)) {
        throw "artifact 不完整，找不到：$entryPoint"
    }
}

if ($ValidateOnly) {
    if (-not $SkipMigrations) {
        & "$PSScriptRoot\invoke-migrations.ps1" -MigrationFiles $MigrationFiles `
            -MigrationCredential $MigrationCredential -DatabaseHost $DatabaseHost `
            -DatabasePort $DatabasePort -DatabaseName $DatabaseName -PsqlPath $PsqlPath -ValidateOnly
    }
    Write-Host "✓ 對外 origin：前台 $StorefrontPublicOrigin；前台 API $StorefrontPublicApiOrigin（只注給 GreyGray-Storefront）"
    Write-Host "✓ deploy 參數驗證通過：3 個 win-x64 self-contained + 2 個 Next standalone；node.exe=$resolvedNodePath；未碰 NSSM、排程或 YC。"
    return
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '實際部署必須在系統管理員 PowerShell 執行。'
}
if (-not (Test-Path -LiteralPath $NssmPath -PathType Leaf)) { throw "找不到 NSSM：$NssmPath" }
$nodeVersion = Invoke-NativeCommand -FilePath $resolvedNodePath -ArgumentList @('--version') -EchoOutput
if ($nodeVersion.StdOut.Trim() -notmatch '^v(?<major>\d+)' -or [int]$Matches.major -lt 20) {
    throw "Node 必須 >= 20；目前輸出：$($nodeVersion.StdOut.Trim())"
}

$releaseId = [datetime]::UtcNow.ToString('yyyyMMddHHmmssfff')
$releaseRoot = Join-Path $installFull "releases\$releaseId"
$logsRoot = Join-Path $installFull 'logs'
$installedOpsRoot = Join-Path $installFull 'ops'
New-Item -ItemType Directory -Path $releaseRoot, $logsRoot, $installedOpsRoot -Force | Out-Null

foreach ($definition in $manifest.Services) {
    Copy-Item -LiteralPath (Join-Path $artifactFull $definition.ArtifactDirectory) `
        -Destination (Join-Path $releaseRoot $definition.ArtifactDirectory) -Recurse -Force
}
Copy-Item -LiteralPath "$PSScriptRoot\watchdog.ps1", $manifestPath -Destination $installedOpsRoot -Force

# migration 先於停服；失敗時舊版仍繼續服務。高權限帳號只活在此子呼叫。
if (-not $SkipMigrations) {
    & "$PSScriptRoot\invoke-migrations.ps1" -MigrationFiles $MigrationFiles `
        -MigrationCredential $MigrationCredential -DatabaseHost $DatabaseHost `
        -DatabasePort $DatabasePort -DatabaseName $DatabaseName -PsqlPath $PsqlPath
}

<#
    ── 正式機的機密與連線設定投遞 ────────────────────────────────────────────
    在這之前，deploy.ps1 對正式機服務只設了 DOTNET_ENVIRONMENT／
    ASPNETCORE_ENVIRONMENT／ASPNETCORE_URLS 三個鍵——一個 ConnectionStrings 都沒有。
    三個 .NET Host 開機後連資料庫都連不上（docs/22 §0）。

    模式刻意跟 ops/install-dev-environment.ps1 一模一樣，不是另設計一套：
      · $InstallRoot\secrets\，ACL 收緊到目前使用者 ＋ Administrators ＋ SYSTEM
      · 明文檔案，「已存在就重用、不存在就生成」，所以 deploy.ps1 可以安全重跑
      · 密碼永遠不上命令列：ALTER ROLE 走臨時 .sql 檔，連線密碼走 PGPASSWORD
        （照 ops/invoke-migrations.ps1:52-66）

    放在 migration 之後、停服之前：這一段若失敗，舊版服務還在跑，沒有停機。
    -ValidateOnly 在更前面就 return 了，dry-run 路徑碰不到這裡。
#>
$secretsDir = Join-Path $installFull 'secrets'
if (-not (Test-Path -LiteralPath $secretsDir -PathType Container)) {
    New-Item -ItemType Directory -Path $secretsDir -Force | Out-Null
}
Protect-SecretDirectory -Path $secretsDir

# 0001_schemas_and_roles.sql 建的 role 預設沒有密碼，服務連不進去。
# 這份清單與 install-dev-environment.ps1／start-dev-hosts.ps1 的同一份刻意一致：
# 15 個模組 schema 裡 audit／reporting 沒有任何 *.Infra 讀對應的 ConnectionStrings 鍵
# （customer_service 是 BE-55／ADR-040 新增的第 15 個模組）。
$moduleSchemas = @(
    'iam', 'catalog', 'campaign', 'checkout', 'fulfillment', 'inventory',
    'ledger', 'notify', 'ordering', 'payment', 'pricing', 'procurement', 'platform',
    'customer_service'
)
$modulePasswordFile = Join-Path $secretsDir 'module-role.password'
$modulePasswordIsNew = -not (Test-Path -LiteralPath $modulePasswordFile -PathType Leaf)
if ($modulePasswordIsNew) {
    # 首次部署：沒有 -MigrationCredential 就套不進資料庫，生了也只是留一個
    # 「服務拿著連不進去的密碼」的檔案。寧可 fail-closed，也不要部署出一個
    # 看起來成功、實際上三個 Host 全部連不上 DB 的版本。
    if ($null -eq $MigrationCredential) {
        throw "首次部署（找不到 $modulePasswordFile）必須提供 -MigrationCredential，才能對 $($moduleSchemas.Count) 個模組 role 設定密碼。"
    }
    [IO.File]::WriteAllText($modulePasswordFile, (New-SecretPassword), (New-Object Text.UTF8Encoding($false)))
}
$modulePassword = (Get-Content -LiteralPath $modulePasswordFile -Raw).Trim()

if ($null -ne $MigrationCredential) {
    # 有部署帳號（有 DDL 權限，見 db/migrations/0001_schemas_and_roles.sql:7）就重設一次。
    # 重放同一個密碼是冪等的，而且能自我修復「密碼檔還在、role 密碼被人動過」的狀況。
    $alterStatements = ($moduleSchemas | ForEach-Object { "ALTER ROLE greygray_$_ PASSWORD '$modulePassword';" }) -join "`n"
    $alterSqlFile = Join-Path ([IO.Path]::GetTempPath()) ('greygray-role-passwords-' + [guid]::NewGuid().ToString('N') + '.sql')
    try {
        [IO.File]::WriteAllText($alterSqlFile, "\set ON_ERROR_STOP on`nBEGIN;`n$alterStatements`nCOMMIT;`n", (New-Object Text.UTF8Encoding($false)))
        $migrationPassword = $MigrationCredential.GetNetworkCredential().Password
        try {
            # role 是 cluster 層級的，連哪個 db 都一樣；用 $DatabaseName 是因為
            # invoke-migrations.ps1 剛剛才用同一個值連成功過，不必再猜第二個名字。
            Invoke-NativeCommand -FilePath $PsqlPath `
                -ArgumentList @('--host', $DatabaseHost, '--port', [string]$DatabasePort,
                    '--username', $MigrationCredential.UserName, '--dbname', $DatabaseName,
                    '--no-password', '--set', 'ON_ERROR_STOP=1', '--file', $alterSqlFile) `
                -Environment @{ PGPASSWORD = $migrationPassword; PGCONNECT_TIMEOUT = '10' } | Out-Null
        }
        finally { $migrationPassword = $null }
    }
    finally {
        if (Test-Path -LiteralPath $alterSqlFile) { Remove-Item -LiteralPath $alterSqlFile -Force }
    }
    Write-Host "✓ 模組角色密碼：$($moduleSchemas.Count) 個 role 已設定，明文只在 $modulePasswordFile"
}
else {
    Write-Host "✓ 模組角色密碼：沿用 $modulePasswordFile（帶了 -SkipMigrations，沒有部署帳號可以重設 role 密碼）"
}

# Identity 個資保護金鑰。★ 一定要 New-DataProtectionKey：New-SecretPassword 會替換
# Base64 字元，IdentityDataProtector 要求解碼後正好 32 bytes（見 lib\Secrets.ps1）。
# ★「已存在就重用」是硬需求，不是省事：換掉金鑰，iam.customer 既有密文就解不開了。
$dataProtectionKeyFile = Join-Path $secretsDir 'identity-dataprotection.key'
$dataProtectionKeyIsNew = -not (Test-Path -LiteralPath $dataProtectionKeyFile -PathType Leaf)
if ($dataProtectionKeyIsNew) {
    [IO.File]::WriteAllText($dataProtectionKeyFile, (New-DataProtectionKey), (New-Object Text.UTF8Encoding($false)))
}
$dataProtectionKey = (Get-Content -LiteralPath $dataProtectionKeyFile -Raw).Trim()
Write-Host "✓ Identity 個資保護金鑰：$dataProtectionKeyFile（$(if ($dataProtectionKeyIsNew) { '本次產生' } else { '沿用既有' })）"

<#
    綠界憑證（E3）：正式商店代號與金鑰是老闆要去辦的事，不是這支腳本能生的。
    憑證到手之後，把三個憑證值與兩個必填網址一起放進 $secretsDir\ecpay.json。
    舊版只給三個會打到測試站；現在五個值缺一個就拒絕部署，兩個網址刻意沒有預設。
    以下是正式站範例；測試站請將兩個網址的 host 明確填成 payment-stage.ecpay.com.tw
    （測試站 DoAction 僅供串接設定，不支援實際退刷），不必改任何程式：

        { "MerchantId": "...", "HashKey": "...", "HashIV": "...", "CheckoutUrl": "https://payment.ecpay.com.tw/Cashier/AioCheckOut/V5", "CreditDetailUrl": "https://payment.ecpay.com.tw/CreditDetail/DoAction" }

    檔案不存在就跳過（不 throw）。Payment 模組維持現有的清楚報錯
    （ModuleRegistration.cs 的 Required），比灌一組編出來的假值好。
#>
$ecpayFile = Join-Path $secretsDir 'ecpay.json'
$ecpaySettings = $null
if (Test-Path -LiteralPath $ecpayFile -PathType Leaf) {
    $ecpaySettings = Get-Content -LiteralPath $ecpayFile -Raw | ConvertFrom-Json
    foreach ($key in @('MerchantId', 'HashKey', 'HashIV', 'CheckoutUrl', 'CreditDetailUrl')) {
        # StrictMode 下不存在的屬性會直接丟，所以先問 PSObject 有沒有這個名字。
        $present = @($ecpaySettings.PSObject.Properties.Name) -contains $key
        if (-not $present -or [string]::IsNullOrWhiteSpace([string]$ecpaySettings.$key)) {
            throw "$ecpayFile 缺少或空白的 $key；正式站格式：{ ""MerchantId"": ""..."", ""HashKey"": ""..."", ""HashIV"": ""..."", ""CheckoutUrl"": ""https://payment.ecpay.com.tw/Cashier/AioCheckOut/V5"", ""CreditDetailUrl"": ""https://payment.ecpay.com.tw/CreditDetail/DoAction"" }；測試站請明確填入 payment-stage.ecpay.com.tw 的對應網址（DoAction 不支援實際退刷）。兩個網址刻意沒有預設，避免正式金鑰安靜地打到測試站。"
        }
    }
    Write-Host "✓ 綠界憑證：$ecpayFile 五個鍵齊全，將注入三個 .NET 服務"
}
else {
    Write-Host "⚠ 找不到 $ecpayFile；Payment:ECPay:* 不注入，Payment 模組會照舊明確報缺設定（等 E3 憑證到位）。"
}

# 真正要送進 NSSM AppEnvironmentExtra 的那一包。鍵名用雙底線，跟 .NET 設定綁定
# 慣例一致（ConnectionStrings__X → ConnectionStrings:X）。
# ★ 正式機刻意不加 Include Error Detail=true：那個開發用旗標會讓 Npgsql 例外訊息
#   帶出 SQL 與參數明細，正式機不該把這些吐出去。start-dev-hosts.ps1 有、這裡沒有，是刻意的差異。
$secretConnectionStrings = [ordered]@{}
foreach ($schema in $moduleSchemas) {
    $secretConnectionStrings["ConnectionStrings__GreyGray_$schema"] =
        "Host=$DatabaseHost;Port=$DatabasePort;Database=$DatabaseName;Username=greygray_$schema;Password=$modulePassword"
}
# Garnet 與三個 Host 同機（ADR-003 原生部署），不走 LAN。
$secretConnectionStrings['ConnectionStrings__GreyGray_valkey'] = '127.0.0.1:6379'
$secretConnectionStrings['Identity__DataProtectionKey'] = $dataProtectionKey
if ($null -ne $ecpaySettings) {
    $secretConnectionStrings['Payment__ECPay__MerchantId'] = [string]$ecpaySettings.MerchantId
    $secretConnectionStrings['Payment__ECPay__HashKey'] = [string]$ecpaySettings.HashKey
    $secretConnectionStrings['Payment__ECPay__HashIV'] = [string]$ecpaySettings.HashIV
    $secretConnectionStrings['Payment__ECPay__CheckoutUrl'] = [string]$ecpaySettings.CheckoutUrl
    $secretConnectionStrings['Payment__ECPay__CreditDetailUrl'] = [string]$ecpaySettings.CreditDetailUrl
}
$modulePassword = $null
$dataProtectionKey = $null
$ecpaySettings = $null

function Invoke-Nssm {
    param([string[]]$Arguments, [switch]$AllowNonZeroExit)
    return Invoke-NativeCommand -FilePath $NssmPath -ArgumentList $Arguments `
        -AllowNonZeroExit:$AllowNonZeroExit -EchoOutput
}

function Get-ManagedApplicationTokens {
    $tokens = @()
    foreach ($definition in $manifest.Services) {
        if ($definition.Kind -eq 'DotNet') {
            foreach ($process in @(Get-Process -Name $definition.ProcessName -ErrorAction SilentlyContinue)) {
                $token = Get-GreyGrayProcessToken -Process $process
                if ($token.Path -and (Test-PathWithinRoot -Path $token.Path -Root $installFull)) {
                    Add-Member -InputObject $token -NotePropertyName ServiceName -NotePropertyValue $definition.Name
                    $tokens += $token
                }
            }
        }
        if ([int]$definition.Port -gt 0) {
            foreach ($process in @(Get-PortOwnerProcess -Port ([int]$definition.Port))) {
                $token = Get-GreyGrayProcessToken -Process $process
                Add-Member -InputObject $token -NotePropertyName ServiceName -NotePropertyValue $definition.Name
                if (-not ($tokens | Where-Object { $_.Id -eq $token.Id -and $_.StartTimeUtc -eq $token.StartTimeUtc })) {
                    $tokens += $token
                }
            }
        }
    }
    return $tokens
}

function Test-DeploymentTokenOwnership {
    param([Parameter(Mandatory)]$Token)

    $definition = $manifest.Services | Where-Object { $_.Name -eq $Token.ServiceName } | Select-Object -First 1
    if ($null -eq $definition) { return $false }
    if ($definition.Kind -eq 'DotNet') {
        return $Token.Path -and (Test-PathWithinRoot -Path $Token.Path -Root $installFull)
    }

    # Node 在 InstallRoot 外；只有命令列精確含某個已安裝 release 的 server.js 才能認領。
    $releasesRoot = Join-Path $installFull 'releases'
    foreach ($release in @(Get-ChildItem -LiteralPath $releasesRoot -Directory -ErrorAction SilentlyContinue)) {
        $entryPoint = Join-Path (Join-Path $release.FullName $definition.ArtifactDirectory) $definition.ArtifactEntryPoint
        if ((Test-Path -LiteralPath $entryPoint -PathType Leaf) -and
            (Test-NodeProcessIdentity -Token $Token -NodePath $resolvedNodePath -EntryPointPath $entryPoint)) {
            return $true
        }
    }
    return $false
}

<#
    ★ 部署期間停用 GreyGray-Watchdog 排程。

    watchdog.ps1 的邏輯是「任何服務不是 Running 就 Start-Service」，排程每 5 分鐘跑一次；
    而下面 STOP → 改 NSSM 參數 → START 之間有幾十秒的空窗。watchdog 撞進去就會拿
    **還沒改完的參數**（舊 release 路徑）把服務拉起來，接著要嘛 START 撞「已在執行中」、
    要嘛 Assert-NewApplicationProcess 抓到舊 release 的 PID 而 throw。
    2026-09-03 那次 watchdog 13:01:30 跑、STOP 約 13:01:45，差 15 秒沒撞上，純運氣。

    ★ Resume 一定要在 finally：成功路徑最後的 Register-ScheduledTask -Force 會以
    <Enabled>true</Enabled> 重登記（所以兩者不衝突），但**失敗路徑只有 finally 會跑**——
    少了它，一次失敗的部署會讓正式機從此沒有 watchdog，而且沒有任何東西會說話。

    -ValidateOnly 在第 107-116 行就 return 了，碰不到這裡（dry-run 不准動排程）。
#>
$watchdogSuspended = Suspend-WatchdogTask -TaskName $WatchdogTaskName `
    -GetTask { Get-ScheduledTask -TaskName $WatchdogTaskName -ErrorAction SilentlyContinue } `
    -StopTask { Stop-ScheduledTask -TaskName $WatchdogTaskName -ErrorAction SilentlyContinue } `
    -DisableTask { Disable-ScheduledTask -TaskName $WatchdogTaskName -ErrorAction SilentlyContinue }
try {
    $oldTokens = @(Get-ManagedApplicationTokens)
    foreach ($definition in $manifest.Services) {
        $service = Get-Service -Name $definition.Name -ErrorAction SilentlyContinue
        if ($null -ne $service -and $service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
            Invoke-Nssm -Arguments @('stop', $definition.Name) -AllowNonZeroExit | Out-Null
            $service.Refresh()
            $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [timespan]::FromSeconds(30))
        }
    }

    # 不能只相信 NSSM/排程狀態：逐一驗證舊 PID 與 port holder 真的消失。
    Wait-ProcessTokensExit -Tokens $oldTokens -InstallRoot $installFull -TimeoutSeconds 30 `
        -OwnershipValidator { param($token) Test-DeploymentTokenOwnership -Token $token }
    foreach ($definition in $manifest.Services) {
        if ([int]$definition.Port -gt 0) {
            $portDefinition = $definition
            <#
                ★ 這裡**不可以**加 .GetNewClosure()。GetNewClosure() 會把 scriptblock 綁到一個
                新的動態模組，而那個模組的 parent 是 **global** scope，不是建立它的 script scope。
                deploy.ps1 自己定義的函式（Test-DeploymentTokenOwnership、Invoke-Nssm…）與
                dot-source 進來的 lib 函式都在 script scope，於是 lib 函式 `& $OwnershipValidator`
                呼叫下去時會得到 CommandNotFoundException：
                「無法辨識 'Invoke-Nssm' 詞彙是否為 Cmdlet、函數、指令檔或可執行程式的名稱。」

                ★★ 它只在「deploy.ps1 被**另一支腳本**呼叫」時才炸（正式機就是這樣：
                C:\Source\yc-deploy.ps1 裡 `& .\ops\deploy.ps1 @common …`）。直接
                `-File ops\deploy.ps1` 跑的時候，script scope 的 parent 剛好就是 global，
                動態模組因此「湊巧」看得到——所以開發機怎麼試都試不出來。5.1 與 7 行為一致。
                （self-test.ps1 有這條的最小重現，兩種呼叫方式都跑。）

                plain scriptblock 綁的是**建立它的 script scope**，看得到 script 函式與 script 變數，
                兩種呼叫方式都對——第 339 行那個 -OwnershipValidator 從第一天就是這樣寫的。
                $portDefinition 不需要被「捕捉」：scriptblock 在同一個 iteration 內就被同步呼叫完，
                執行時沿 scope 鏈讀到的就是這一圈的值。
            #>
            $portOwnershipValidator = {
                param($token)
                # -Force：同一個行程可能已經被 Get-ManagedApplicationTokens 標過 ServiceName
                # （重複部署時，舊 PID 同時被行程名稱與 port 兩條路抓到）。Add-Member 遇到同名屬性
                # 會發非終止性錯誤，而這支腳本 $ErrorActionPreference='Stop'，等於整個部署炸掉。
                Add-Member -InputObject $token -NotePropertyName ServiceName -NotePropertyValue $portDefinition.Name -Force
                Test-DeploymentTokenOwnership -Token $token
            }
            Assert-PortReleased -Port ([int]$definition.Port) -InstallRoot $installFull `
                -OwnershipValidator $portOwnershipValidator
        }
    }

    $servicePassword = $ServiceCredential.GetNetworkCredential().Password
    try {
        foreach ($definition in $manifest.Services) {
            $service = Get-Service -Name $definition.Name -ErrorAction SilentlyContinue
            $artifactDirectory = Join-Path $releaseRoot $definition.ArtifactDirectory
            $entryPoint = Join-Path $artifactDirectory $definition.ArtifactEntryPoint
            $appDirectory = Join-Path $artifactDirectory $definition.WorkingDirectory
            $executable = if ($definition.Kind -eq 'NextStandalone') { $resolvedNodePath } else { $entryPoint }
            $applicationArguments = @()
            if ($definition.Kind -eq 'NextStandalone') {
                $applicationArguments = @($entryPoint)
            }
            else {
                $applicationArguments = @($definition.Arguments)
            }
            if ($null -eq $service) {
                Invoke-Nssm -Arguments @('install', $definition.Name, $executable) | Out-Null
            }
            Invoke-Nssm -Arguments @('set', $definition.Name, 'Application', $executable) | Out-Null
            Invoke-Nssm -Arguments @('set', $definition.Name, 'AppDirectory', $appDirectory) | Out-Null
            if ($applicationArguments.Count -gt 0) {
                Invoke-Nssm -Arguments (@('set', $definition.Name, 'AppParameters') + $applicationArguments) | Out-Null
            }
            else {
                <#
                    三個 Kind='DotNet' 的服務 manifest 是 Arguments = @()，要的是「沒有參數」。
                    ★ 不可以用 `nssm reset <svc> AppParameters`：正式機那一版（2.24-101-g897c7ad）
                    一定 heap corruption（exit -1073740940 / 0xC0000374），而且 `set … ''` 會被當成
                    沒給值而印 usage——見 lib\Deployment.ps1 的 Clear-NssmAppParameters 註解與 YC 實測。
                    剛 install 的服務本來就沒有這個值，所以第一次部署這裡什麼都不會做。
                #>
                $serviceNameForGet = $definition.Name
                <#
                    ★ 同上，**不可以**加 .GetNewClosure()：動態模組的 parent 是 global scope，
                    看不到 script scope 的 Invoke-Nssm——而且只有「被另一支腳本呼叫」時才會炸
                    （見上面 port ownership validator 那一段的完整說明）。
                    YC 第四次真跑炸的就是這一份：
                    「無法辨識 'Invoke-Nssm' 詞彙是否為 Cmdlet、函數、指令檔或可執行程式的名稱。」
                    plain scriptblock 會在 script scope 底下執行，Invoke-Nssm 與 $serviceNameForGet 都看得到。
                #>
                $getAppParameters = {
                    $getResult = Invoke-Nssm -Arguments @('get', $serviceNameForGet, 'AppParameters') -AllowNonZeroExit
                    if ($getResult.ExitCode -ne 0) {
                        throw "nssm get $serviceNameForGet AppParameters 失敗（exit $($getResult.ExitCode)）：$($getResult.StdErr.Trim())"
                    }
                    return $getResult.StdOut
                }
                if (Clear-NssmAppParameters -ServiceName $definition.Name -GetAppParameters $getAppParameters) {
                    Write-Host "✓ $($definition.Name) 的 AppParameters 已清空（走 registry；這一版 nssm 的 reset AppParameters 會 heap corruption）"
                }
            }
            Invoke-Nssm -Arguments @('set', $definition.Name, 'Start', 'SERVICE_AUTO_START') | Out-Null
            Invoke-Nssm -Arguments @('set', $definition.Name, 'AppExit', 'Default', 'Restart') | Out-Null
            Invoke-Nssm -Arguments @('set', $definition.Name, 'AppRestartDelay', '60000') | Out-Null
            Invoke-Nssm -Arguments @('set', $definition.Name, 'AppThrottle', '1500') | Out-Null
            Invoke-Nssm -Arguments @('set', $definition.Name, 'AppStdout', (Join-Path $logsRoot "$($definition.Name).stdout.log")) | Out-Null
            Invoke-Nssm -Arguments @('set', $definition.Name, 'AppStderr', (Join-Path $logsRoot "$($definition.Name).stderr.log")) | Out-Null
            Invoke-Nssm -Arguments @('set', $definition.Name, 'AppRotateFiles', '1') | Out-Null
            Invoke-Nssm -Arguments @('set', $definition.Name, 'AppRotateBytes', '10485760') | Out-Null
            if ($definition.Kind -eq 'NextStandalone') {
                $environmentArguments = @('set', $definition.Name, 'AppEnvironmentExtra',
                    'NODE_ENV=production', "PORT=$($definition.Port)", 'HOSTNAME=127.0.0.1')
            }
            else {
                $environmentArguments = @('set', $definition.Name, 'AppEnvironmentExtra', 'DOTNET_ENVIRONMENT=Production')
                # 三個 Kind='DotNet' 的服務（Storefront／Admin／Worker）全部呼叫
                # AddIdentityModule 與 AddPaymentModule，也全部要連資料庫，所以整包都要。
                # NextStandalone 那兩個（上面的分支）不需要，刻意不動。
                foreach ($secretKey in $secretConnectionStrings.Keys) {
                    $environmentArguments += "$secretKey=$($secretConnectionStrings[$secretKey])"
                }
                # 只有前台 BFF 需要這兩個：Storefront:PublicOrigin 組綠界完成頁的
                # ClientBackURL（#33），Storefront:PublicApiOrigin 組綠界的 ReturnURL（BE-42）。
                # Admin／Worker 不碰這兩條路，多給只會讓「哪個服務需要什麼」更難看清楚。
                if ($definition.Name -eq 'GreyGray-Storefront') {
                    $environmentArguments += "Storefront__PublicOrigin=$StorefrontPublicOrigin"
                    $environmentArguments += "Storefront__PublicApiOrigin=$StorefrontPublicApiOrigin"
                }
            }
            if ($definition.Kind -eq 'DotNet' -and [int]$definition.Port -gt 0) {
                $environmentArguments += 'ASPNETCORE_ENVIRONMENT=Production'
                # Cloudflare Tunnel 與 health probe 都在本機；不要把 BFF listener 暴露到 LAN。
                $environmentArguments += "ASPNETCORE_URLS=http://127.0.0.1:$($definition.Port)"
            }
            Invoke-Nssm -Arguments $environmentArguments | Out-Null
            # NSSM API 只能收明文密碼；只存在目前 process 記憶體與短暫子程序命令列，不落部署設定檔。
            Invoke-Nssm -Arguments @('set', $definition.Name, 'ObjectName', $ServiceCredential.UserName, $servicePassword) | Out-Null
        }
    }
    finally {
        $servicePassword = $null
        # 連線字串與金鑰的明文只需要活到 NSSM 設定寫完為止；之後 script 還有
        # 健康檢查、排程註冊等好幾十行，不要讓它們一路活到最後。
        $secretConnectionStrings = $null
        $environmentArguments = $null
    }

    $restartBoundaryUtc = [datetime]::UtcNow
    foreach ($definition in $manifest.Services) {
        <#
            ★ START 的成功條件只有一個：SCM 狀態變成 Running。**不看 nssm 的 exit code。**
            2026-09-03 13:02 正式機第二次重複部署就是死在這裡——GreyGray-Web-Storefront
            起得慢了幾秒，nssm 印「Unexpected status SERVICE_START_PENDING in response to
            START control.」並 exit 非 0，這一行當時沒帶 -AllowNonZeroExit → 整支 throw，
            第五個服務沒 START、Assert-NewApplicationProcess 與 watchdog 重登記都沒跑；
            而那個服務兩秒後就 Ready，根本沒壞。Leader 在 YC 另外量到
            `nssm start <已在 Running 的服務>` 也是 exit 1——exit code 對「正在起」與
            「已經在跑」都回非 0，本來就不是成功／失敗的訊號。

            ★ 兩個 scriptblock 都是 **plain** 的，不可以 .GetNewClosure()：動態模組的 parent 是
            global scope，看不到這支腳本 script scope 的 Invoke-Nssm（見上面 AppParameters
            那一段的完整說明，以及 self-test 的 AST 把關）。迴圈變數先複製成
            $serviceNameForStart，照第 405-425 行 $serviceNameForGet 的既有做法。
        #>
        $serviceNameForStart = $definition.Name
        $startManagedService = {
            return Invoke-Nssm -Arguments @('start', $serviceNameForStart) -AllowNonZeroExit
        }
        $getManagedServiceStatus = {
            $serviceForStatus = Get-Service -Name $serviceNameForStart
            # ServiceController.Status 是快取的，不 Refresh() 會一直讀到 START 之前的舊值。
            $serviceForStatus.Refresh()
            return $serviceForStatus.Status
        }
        Wait-ManagedServiceStart -ServiceName $definition.Name -StartService $startManagedService `
            -GetStatus $getManagedServiceStatus -TimeoutSeconds $HealthTimeoutSeconds | Out-Null
    }

    function Assert-NewApplicationProcess {
        param($Definition)

        $expectedRoot = Join-Path $releaseRoot $Definition.ArtifactDirectory
        $expectedEntryPoint = Join-Path $expectedRoot $Definition.ArtifactEntryPoint
        $deadline = [datetime]::UtcNow.AddSeconds($HealthTimeoutSeconds)
        do {
            $candidates = @()
            if ([int]$Definition.Port -gt 0) {
                $candidates = @(Get-PortOwnerProcess -Port ([int]$Definition.Port))
            }
            else {
                $candidates = @(Get-Process -Name $Definition.ProcessName -ErrorAction SilentlyContinue)
            }

            foreach ($process in $candidates) {
                $token = Get-GreyGrayProcessToken -Process $process
                <#
                    $process.StartTime 對取不到權限的行程會丟 Win32Exception
                    （Get-GreyGrayProcessToken 對同一件事就是包 try/catch 的，這裡本來沒包）。
                    拿不到 StartTime 就無法證明它是本次 release 起的——當成「還不是新行程」
                    跳過、繼續輪詢到逾時，而不是讓整支部署當場炸掉。
                    注意這不會放寬判斷：證明不了就不算數，仍然拿不到綠燈。
                #>
                $processStartTime = $null
                try { $processStartTime = $process.StartTime } catch { }
                if ($null -eq $processStartTime) { continue }
                if (-not (Test-ProcessStartedAfter -ProcessStartTime $processStartTime -RestartBoundaryUtc $restartBoundaryUtc)) {
                    throw "$($Definition.Name) 由舊 PID $($process.Id) 回應；StartTime 不晚於本次重啟，拒絕假綠。"
                }
                if ($Definition.Kind -eq 'NextStandalone') {
                    if (-not (Test-NodeProcessIdentity -Token $token -NodePath $resolvedNodePath `
                            -EntryPointPath $expectedEntryPoint)) {
                        throw "$($Definition.Name) PID $($process.Id) 不是可信 node.exe 或沒有執行本次 release 的 server.js：$expectedEntryPoint"
                    }
                }
                elseif (-not $token.Path -or -not (Test-PathWithinRoot -Path $token.Path -Root $expectedRoot)) {
                    throw "$($Definition.Name) PID $($process.Id) 並非從本次 release 啟動：$($token.Path)"
                }

                if ([int]$Definition.Port -eq 0) {
                    Write-Host "✓ $($Definition.Name) PID $($process.Id)，StartTime=$($processStartTime.ToString('o'))"
                    return
                }

                try {
                    $healthUrl = "http://127.0.0.1:$($Definition.Port)$($Definition.HealthPath)"
                    $response = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 5
                    if ($response.StatusCode -eq 200) {
                        Write-Host "✓ $($Definition.Name) $healthUrl → 200，PID $($process.Id)，StartTime=$($processStartTime.ToString('o'))"
                        return
                    }
                }
                catch { }
            }
            Start-Sleep -Milliseconds 500
        } while ([datetime]::UtcNow -lt $deadline)

        throw "$($Definition.Name) 未在 $HealthTimeoutSeconds 秒內由本次 release 接手。"
    }

    foreach ($definition in $manifest.Services) { Assert-NewApplicationProcess -Definition $definition }
}
finally {
    Resume-WatchdogTask -TaskName $WatchdogTaskName -Suspended $watchdogSuspended `
        -EnableTask { Enable-ScheduledTask -TaskName $WatchdogTaskName -ErrorAction SilentlyContinue } | Out-Null
}

if (-not $WatchdogUserSid) {
    $WatchdogUserSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
}
$watchdogScript = Join-Path $installedOpsRoot 'watchdog.ps1'
$installedManifest = Join-Path $installedOpsRoot 'service-manifest.ps1'
$startBoundary = [datetime]::Now.AddMinutes(1).ToString('s')
$escapedScript = [Security.SecurityElement]::Escape($watchdogScript)
$escapedManifest = [Security.SecurityElement]::Escape($installedManifest)
$escapedSid = [Security.SecurityElement]::Escape($WatchdogUserSid)
$xml = @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <Triggers>
    <BootTrigger><Enabled>true</Enabled></BootTrigger>
    <TimeTrigger><Repetition><Interval>PT5M</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition><StartBoundary>$startBoundary</StartBoundary><Enabled>true</Enabled></TimeTrigger>
  </Triggers>
  <Principals><Principal id="Author"><UserId>$escapedSid</UserId><LogonType>S4U</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT5M</ExecutionTimeLimit>
    <RestartOnFailure><Interval>PT1M</Interval><Count>999</Count></RestartOnFailure><Enabled>true</Enabled>
  </Settings>
  <Actions Context="Author"><Exec><Command>powershell.exe</Command><Arguments>-NoProfile -ExecutionPolicy Bypass -File &quot;$escapedScript&quot; -ManifestPath &quot;$escapedManifest&quot;</Arguments></Exec></Actions>
</Task>
"@
Register-ScheduledTask -TaskName $WatchdogTaskName -Xml $xml -Force | Out-Null

Write-Host "✓ 部署完成：$releaseRoot"
Write-Host "✓ NSSM 自動重啟 + S4U BootTrigger / 每 5 分 watchdog / RestartOnFailure 999 已登記"
