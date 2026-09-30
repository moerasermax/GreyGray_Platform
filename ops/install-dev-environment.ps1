<#
    GreyGray 本機開發環境整備（docs/19 第八波 BE-22）。

    只解決 D 階段的阻塞：開發機需要一組能連得到的 PostgreSQL 17 ＋ Garnet，
    ＋ 套完 0001_～0023_ migrations，讓 FE-12 有真後端可以打。

    刻意跟 install-environment.ps1（「正式機環境整備」，見它自己的檔頭）分開，
    不是重複造輪子：那支腳本做的專屬服務帳號、ACL、NSSM 服務、cloudflared tunnel、
    prod-monitor 註冊、Windows Update 改人工、Defender 排除，全部是「正式機」的需求，
    docs/19 §5 BE-22 完全沒有要求開發機做這些事——而且那些是會改變使用者自己這台機器
    安全設定的動作，不該因為兩個腳本共用同一個 InstallRoot 概念就順手一起做掉。
    這支腳本只做 PostgreSQL／Garnet／migrations 三件事，不裝 Windows service、
    不建帳號，方便用 stop-dev-environment.ps1 整組收掉。Garnet 是一般背景行程；
    PostgreSQL 本體改用 Docker 容器（見下方「偏離派工書」那段說明其原因）。

    密碼一律不進命令列：容器的 superuser 密碼走 docker run --env-file；
    ALTER ROLE 走臨時 .sql 檔案；兩個角色密碼各自的明文只落地在
    $InstallRoot\secrets\ 底下，ACL 收緊到目前使用者。
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$InstallRoot = 'D:\GreyGray',
    [string]$PostgreSqlDataRoot = 'D:\GreyGray\PostgreSQL\data',
    [string]$PostgreSqlWalRoot = 'D:\GreyGray\PostgreSQL\wal',
    [string]$PostgreSqlZipPath,
    [string]$PostgreSqlZipSha256 = '6EABDF00D2893713B75DB4336A23C3FDF505F056E217EC6E2E95D901750CFEA3',
    [int]$PostgreSqlPort = 5432,
    [int]$GarnetPort = 6379,
    [pscredential]$PostgresSuperuserCredential,
    [int]$StartupTimeoutSeconds = 60
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot\lib\Process.ps1"
# 只為了取 New-DataProtectionKey 一個函式（docs/22 §5 必做 2）。
# 這個檔也定義了 New-SecretPassword／Protect-SecretDirectory，但下面 54-83 行
# 既有的 New-RandomPassword／Protect-SecretDirectory 刻意原地不動：函式定義是
# 依序執行的，這行之後的定義會蓋掉 dot-source 進來的同名版本，所以這支腳本
# 用到的仍然是它自己那兩份——已經在跑、已經驗證過的那兩份。
. "$PSScriptRoot\lib\Secrets.ps1"

# 這一波要套的完整清單；不是「找 db/migrations 底下所有檔案」，
# 是刻意列死——多一個未預期的檔案代表基準線變了，寧可讓腳本 throw 也不要默默多套。
$migrationFiles = @(
    '0001_schemas_and_roles.sql', '0002_platform.sql', '0003_channel_seams.sql',
    '0004_hello_world.sql', '0005_m1a_payment_ledger.sql', '0006_m1a_core.sql',
    '0007_m1b_procurement.sql', '0008_m1a_line_refund.sql', '0009_m1b_seams.sql',
    '0010_m1b_inventory.sql', '0011_m1b_compensation.sql', '0012_m1b_fulfillment.sql',
    '0013_m1b_appraisal_period.sql', '0014_m1b_price_inquiry_timeout.sql',
    '0015_ordering_partial_purchase_shortfall.sql',
    '0016_fulfillment_shipment_idempotency.sql',
    '0017_inventory_lot_wholesale_idempotency.sql',
    '0018_inventory_reservation_consumed.sql',
    '0019_catalog_favorite.sql',
    '0020_convenience_store_snapshot.sql',
    '0021_order_recipient_snapshot.sql',
    '0022_customer_service_ticket.sql',
    '0023_category_parent.sql'
) | ForEach-Object { Join-Path 'db\migrations' $_ }
# 0001 建的 14 個模組 schema role + platform（共用例外）；audit／reporting 目前沒有
# 任何 *.Infra 專案讀取對應的 ConnectionStrings 鍵，這一波的三個 Host 用不到，不生密碼。
$moduleSchemas = @(
    'iam', 'catalog', 'campaign', 'checkout', 'fulfillment', 'inventory',
    'ledger', 'notify', 'ordering', 'payment', 'pricing', 'procurement', 'platform',
    'customer_service'
)

function New-RandomPassword {
    param([int]$Length = 32)
    $bytes = [byte[]]::new($Length)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return ([Convert]::ToBase64String($bytes) -replace '[+/=]', 'x')
}

function Resolve-WingetPath {
    <# 同 install-environment.ps1 的根因：winget 在非互動 session 常常不在 PATH 上。 #>
    $cmd = Get-Command winget.exe -CommandType Application -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    foreach ($candidate in @(
        (Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\winget.exe'),
        (Join-Path $env:ProgramFiles 'WindowsApps\winget.exe')
    )) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) { return $candidate }
    }
    return $null
}

function Protect-SecretDirectory {
    param([Parameter(Mandatory)][string]$Path)
    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($identity in @([Security.Principal.WindowsIdentity]::GetCurrent().Name, 'BUILTIN\Administrators', 'NT AUTHORITY\SYSTEM')) {
        $rule = New-Object Security.AccessControl.FileSystemAccessRule($identity, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Test-ProcessAlive {
    param([int]$ProcessId)
    if ($ProcessId -le 0) { return $false }
    return $null -ne (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)
}

function Start-BackgroundProcess {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$LogPath
    )
    $proc = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -WorkingDirectory $WorkingDirectory `
        -RedirectStandardOutput "$LogPath.out.log" -RedirectStandardError "$LogPath.err.log" `
        -WindowStyle Hidden -PassThru
    return $proc
}

# ── 目錄骨架 ────────────────────────────────────────────────────────────────
$stateDir = Join-Path $InstallRoot 'state'
$logDir = Join-Path $InstallRoot 'logs'
$secretsDir = Join-Path $InstallRoot 'secrets'
foreach ($d in @($InstallRoot, $PostgreSqlDataRoot, $PostgreSqlWalRoot, $stateDir, $logDir, $secretsDir)) {
    if (-not (Test-Path -LiteralPath $d -PathType Container)) {
        New-Item -ItemType Directory -Path $d -Force | Out-Null
    }
}
Protect-SecretDirectory -Path $secretsDir

# ── PostgreSQL 17：官方 ZIP binaries（docs/14 §8.2 的根因，這裡直接照抄）───────
$postgresRoot = Join-Path $InstallRoot 'PostgreSQL'
$postgresExe = Join-Path $postgresRoot 'bin\postgres.exe'
if (Test-Path -LiteralPath $postgresExe -PathType Leaf) {
    Write-Host "已存在 postgresql-17：$postgresExe"
}
else {
    if ([string]::IsNullOrWhiteSpace($PostgreSqlZipPath)) {
        throw 'PostgreSQL 改用官方 ZIP binaries；必須提供 -PostgreSqlZipPath（來源見 docs/14-環境整備runbook.md §8.2）。'
    }
    $actualHash = (Get-FileHash -LiteralPath $PostgreSqlZipPath -Algorithm SHA256).Hash
    if ($actualHash -ne $PostgreSqlZipSha256) { throw "PostgreSQL zip SHA-256 不符：$actualHash" }
    if ($PSCmdlet.ShouldProcess($postgresRoot, '解壓官方 PostgreSQL ZIP binaries')) {
        New-Item -ItemType Directory -Path $postgresRoot -Force | Out-Null
        Expand-Archive -LiteralPath $PostgreSqlZipPath -DestinationPath $postgresRoot -Force
        $inner = Join-Path $postgresRoot 'pgsql'
        if (Test-Path -LiteralPath $inner -PathType Container) {
            Get-ChildItem -LiteralPath $inner -Force | Move-Item -Destination $postgresRoot -Force
            Remove-Item -LiteralPath $inner -Force -Recurse
        }
    }
    if (-not (Test-Path -LiteralPath $postgresExe -PathType Leaf)) { throw 'PostgreSQL zip 解壓後找不到 bin\postgres.exe。' }
    Write-Host "已安裝 postgresql-17：$postgresExe"
}

<#
    ★ 偏離派工書原本設想的「postgres.exe 當一般背景行程」：實測 postgres.exe
    在 Administrator 帳號下直接拒絕啟動——

        Execution of PostgreSQL by a user with administrative permissions is
        not permitted. The server must be started under an unprivileged
        user ID ...

    這不是設定問題，是 PostgreSQL 內建、寫死的安全檢查（IsUserAnAdmin()），
    initdb 不擋、只有 server 本體擋。正式機用專屬的 GreyGraySvc 這種非管理員
    帳號繞過，但這一波沒有要在開發機建專屬帳號——那是 install-environment.ps1
    的「正式機」ceremony，docs/19 §5 BE-22 沒有要求，也不該為了繞一個
    OS 層級的權限檢查就去動使用者自己機器的帳號設定。

    改用 Docker 跑 PostgreSQL 本體：容器裡是 postgres 這個 Linux 使用者，
    跟 Windows 這邊是不是 Administrator 無關；Docker Desktop 已經裝著且
    這個 repo 的整合測試本來就用 Testcontainers.PostgreSql 跑同一顆映像
    （postgres:17-alpine），不是新引入的依賴。ZIP binaries 繼續解壓，
    只是改當「psql／pg_isready 這兩支客戶端工具」用，讓
    ops/invoke-migrations.ps1 不必跟著改——它本來就是連 host:port，
    不管背後是不是容器。data／WAL 仍然都在同一顆磁碟底下的 data\pg_wal，
    跟 docs/14 §8.3「不強制獨立 junction」的既有結論一致。
#>
if (-not (Get-Command docker.exe -ErrorAction SilentlyContinue)) {
    throw 'PostgreSQL 本體改用 Docker 執行（見上方註解），但找不到 docker.exe。'
}

$containerName = 'greygray-dev-postgres'
$superuserPasswordFile = Join-Path $secretsDir 'postgres-superuser.password'
$containerState = (docker ps -a --filter "name=^/$containerName`$" --format '{{.State}}' 2>$null)

if ([string]::IsNullOrWhiteSpace($containerState)) {
    if (-not (Test-Path -LiteralPath $PostgreSqlDataRoot -PathType Container) -or @(Get-ChildItem -LiteralPath $PostgreSqlDataRoot -Force -ErrorAction SilentlyContinue).Count -eq 0) {
        $superuserPassword = if ($PostgresSuperuserCredential) { $PostgresSuperuserCredential.GetNetworkCredential().Password } else { New-RandomPassword }
        [IO.File]::WriteAllText($superuserPasswordFile, $superuserPassword, (New-Object Text.UTF8Encoding($false)))
    }
    elseif (-not (Test-Path -LiteralPath $superuserPasswordFile -PathType Leaf)) {
        throw "找到既有 data 目錄但找不到對應密碼檔：$superuserPasswordFile。清空 $PostgreSqlDataRoot 重新來，或手動補回密碼檔。"
    }
    else {
        $superuserPassword = (Get-Content -LiteralPath $superuserPasswordFile -Raw).Trim()
    }

    $envFile = Join-Path ([IO.Path]::GetTempPath()) ('greygray-dev-pg-env-' + [guid]::NewGuid().ToString('N') + '.env')
    try {
        [IO.File]::WriteAllText($envFile, "POSTGRES_PASSWORD=$superuserPassword`nPOSTGRES_USER=postgres`n", (New-Object Text.UTF8Encoding($false)))
        if ($PSCmdlet.ShouldProcess($containerName, 'docker run postgres:17-alpine')) {
            Invoke-NativeCommand -FilePath 'docker.exe' -ArgumentList @(
                'run', '-d', '--name', $containerName,
                '-p', "127.0.0.1:${PostgreSqlPort}:5432",
                '--env-file', $envFile,
                '-v', "${PostgreSqlDataRoot}:/var/lib/postgresql/data",
                'postgres:17-alpine'
            ) -EchoOutput | Out-Null
        }
    }
    finally {
        if (Test-Path -LiteralPath $envFile) { Remove-Item -LiteralPath $envFile -Force }
        $superuserPassword = $null
    }
    Write-Host "已建立並啟動容器 $containerName（postgres:17-alpine，127.0.0.1:$PostgreSqlPort → data=$PostgreSqlDataRoot）"
}
elseif ($containerState -ne 'running') {
    if (-not (Test-Path -LiteralPath $superuserPasswordFile -PathType Leaf)) {
        throw "容器 $containerName 已存在但找不到密碼檔：$superuserPasswordFile。"
    }
    if ($PSCmdlet.ShouldProcess($containerName, 'docker start')) {
        Invoke-NativeCommand -FilePath 'docker.exe' -ArgumentList @('start', $containerName) -EchoOutput | Out-Null
    }
    Write-Host "已重新啟動既有容器 $containerName"
}
else {
    if (-not (Test-Path -LiteralPath $superuserPasswordFile -PathType Leaf)) {
        throw "容器 $containerName 正在跑但找不到密碼檔：$superuserPasswordFile。"
    }
    Write-Host "容器 $containerName 已在跑"
}

$pgIsReady = Join-Path $postgresRoot 'bin\pg_isready.exe'
$deadline = [datetime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
$pgReady = $false
do {
    $result = Invoke-NativeCommand -FilePath $pgIsReady -ArgumentList @('-h', '127.0.0.1', '-p', [string]$PostgreSqlPort) -AllowNonZeroExit
    if ($result.ExitCode -eq 0) { $pgReady = $true; break }
    Start-Sleep -Milliseconds 500
} while ([datetime]::UtcNow -lt $deadline)
if (-not $pgReady) { throw "PostgreSQL 在 $StartupTimeoutSeconds 秒內沒有回 pg_isready；看 docker logs $containerName" }
Write-Host "PASS pg_isready：$($result.StdOut.Trim())"

# ── Garnet：跟 install-environment.ps1 同一個根因，winget 裝好的檔案 ACL 只開放
#    安裝者/Administrators/SYSTEM，複製一份到 InstallRoot 底下才能被一般行程開得起來。──
$garnetRoot = Join-Path $InstallRoot 'Garnet'
<#
    ★ winget 裝的 Microsoft.Garnet.DN8 套件底下是 net8.0\／net9.0\ 兩個 TFM 子資料夾，
    GarnetServer.exe 不在套件根目錄——install-environment.ps1 的同一段邏輯假設
    $garnetRoot\GarnetServer.exe 直接存在，實測（本機、user-scope 安裝）會找不到，
    只是它至今沒被真的跑過一次全新安裝才沒露餡（見自驗報告「我發現但沒做的事」）。
    這一支只用 net8.0：net9.0 那份要 .NET 9 runtime，YC 上沒裝、也不打算裝，
    跟 install-environment.ps1 的註解是同一個理由。
#>
$garnetExe = Join-Path $garnetRoot 'net8.0\GarnetServer.exe'
if (Test-Path -LiteralPath $garnetExe -PathType Leaf) {
    Write-Host "已存在 garnet：$garnetExe"
}
else {
    $garnetPackageRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages'
    $garnetPackageDir = Get-ChildItem -Path $garnetPackageRoot -Filter 'Microsoft.Garnet.DN8_*' -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $garnetPackageDir) {
        $winget = Resolve-WingetPath
        if (-not $winget) { throw '找不到 winget，無法安裝 Garnet。' }
        # 刻意不加 --scope machine：開發機用一般使用者權限裝就好，不需要機器層級安裝。
        $arguments = @('install', '--id', 'Microsoft.Garnet.DN8', '--exact', '--silent',
            '--accept-source-agreements', '--accept-package-agreements', '--disable-interactivity')
        if ($PSCmdlet.ShouldProcess('Garnet', "winget install Microsoft.Garnet.DN8")) {
            Invoke-NativeCommand -FilePath $winget -ArgumentList $arguments -EchoOutput | Out-Null
        }
        $garnetPackageDir = Get-ChildItem -Path $garnetPackageRoot -Filter 'Microsoft.Garnet.DN8_*' -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $garnetPackageDir) { throw 'winget 裝完 Garnet 後仍找不到套件目錄。' }
    }
    if ($PSCmdlet.ShouldProcess($garnetRoot, '複製 Garnet 套件內容到一般行程讀得到的位置')) {
        New-Item -ItemType Directory -Path $garnetRoot -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $garnetPackageDir.FullName '*') -Destination $garnetRoot -Recurse -Force
    }
    if (-not (Test-Path -LiteralPath $garnetExe -PathType Leaf)) { throw 'Garnet 套件內容缺少 net8.0\GarnetServer.exe。' }
    Write-Host "已安裝 garnet：$garnetExe"
}

$garnetPidFile = Join-Path $stateDir 'garnet.pid'
$garnetAlreadyRunning = (Test-Path -LiteralPath $garnetPidFile) -and (Test-ProcessAlive -ProcessId ([int](Get-Content -LiteralPath $garnetPidFile)))
if ($garnetAlreadyRunning) {
    Write-Host "Garnet 已在跑（PID $(Get-Content -LiteralPath $garnetPidFile)）"
}
else {
    $portOwner = Get-NetTCPConnection -LocalPort $GarnetPort -State Listen -ErrorAction SilentlyContinue
    if ($portOwner) { throw "port $GarnetPort 已被其他行程占用（PID $($portOwner.OwningProcess -join ',')）；不是這次安裝要啟動的那個。" }
    if ($PSCmdlet.ShouldProcess('Garnet', '啟動背景行程')) {
        $proc = Start-BackgroundProcess -FilePath $garnetExe -ArgumentList @('--bind', '127.0.0.1', '--port', [string]$GarnetPort) `
            -WorkingDirectory (Split-Path -Parent $garnetExe) -LogPath (Join-Path $logDir 'garnet')
        Set-Content -LiteralPath $garnetPidFile -Value $proc.Id
        Write-Host "已啟動 Garnet（PID $($proc.Id)）"
    }
}

function Test-GarnetPing {
    param([int]$Port)
    try {
        $client = New-Object Net.Sockets.TcpClient('127.0.0.1', $Port)
        $client.ReceiveTimeout = 3000
        $stream = $client.GetStream()
        $request = [Text.Encoding]::ASCII.GetBytes("*1`r`n`$4`r`nPING`r`n")
        $stream.Write($request, 0, $request.Length)
        $stream.Flush()
        $buffer = New-Object byte[] 32
        $read = $stream.Read($buffer, 0, $buffer.Length)
        $client.Close()
        return [Text.Encoding]::ASCII.GetString($buffer, 0, $read).Trim()
    }
    catch { return $_.Exception.Message }
}
$deadline = [datetime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
$garnetPingResult = $null
do {
    $garnetPingResult = Test-GarnetPing -Port $GarnetPort
    if ($garnetPingResult -eq '+PONG') { break }
    Start-Sleep -Milliseconds 500
} while ([datetime]::UtcNow -lt $deadline)
if ($garnetPingResult -ne '+PONG') { throw "Garnet 在 $StartupTimeoutSeconds 秒內沒有回 +PONG（最後一次回應：$garnetPingResult）；看 $logDir\garnet.err.log" }
Write-Host "PASS Garnet RESP PING：$garnetPingResult"

# ── migrations：0001_～0023_，用既有的 invoke-migrations.ps1（部署帳號用完即丟）──
<#
    這一段會判斷「已經套過就跳過」。**現在的理由純粹是省時間，不是安全問題。**

    BE-22 當初寫這段時，重放是真的會炸：db/migrations/0003_channel_seams.sql 對
    ledger.account 的 seed row 是 INSERT ... ON CONFLICT (tenant_id, code) DO NOTHING，
    但後面的 0005_m1a_payment_ledger.sql 給 ledger.account 加了一個 NOT NULL、
    沒有預設值的 id 欄位——PostgreSQL 檢查 NOT NULL 是在建構候選列的時候，早於
    ON CONFLICT 判斷衝突，所以「已經存在就跳過」這個保護在欄位加了之後擋不住，
    重放 0003 會噴 NOT NULL violation。

    ★ 那個 bug 已經由 BE-24 修掉了（後端 22061a4）：0003 改用 WHERE NOT EXISTS 而不是
    ON CONFLICT DO NOTHING，那個檔案自己的註解現在明講「重放是常態而不是邊角案例」。
    **所以 db/migrations 現在整套都可以重放**，不要因為看到這段就以為不行。
    保留跳過機制是因為重跑一次全套 migrations 要花時間，不是因為它不安全。
#>
$migrationsStateFile = Join-Path $stateDir 'migrations-applied.json'
$migrationsAlreadyApplied = $false
if (Test-Path -LiteralPath $migrationsStateFile -PathType Leaf) {
    $existingState = Get-Content -LiteralPath $migrationsStateFile -Raw | ConvertFrom-Json
    $existingFiles = @($existingState.files)
    if ($existingFiles.Count -eq $migrationFiles.Count -and -not (Compare-Object $existingFiles $migrationFiles)) {
        $migrationsAlreadyApplied = $true
    }
}
if ($migrationsAlreadyApplied) {
    Write-Host "PASS migrations：已套用過（$migrationsStateFile，appliedAt=$($existingState.appliedAt)），略過重放"
}
else {
    $superuserPassword = (Get-Content -LiteralPath $superuserPasswordFile -Raw).Trim()
    $superuserCredential = New-Object pscredential('postgres', (ConvertTo-SecureString -String $superuserPassword -AsPlainText -Force))
    try {
        & (Join-Path $PSScriptRoot 'invoke-migrations.ps1') `
            -MigrationFiles $migrationFiles `
            -MigrationCredential $superuserCredential `
            -DatabaseHost '127.0.0.1' `
            -DatabasePort $PostgreSqlPort `
            -DatabaseName 'postgres' `
            -PsqlPath (Join-Path $postgresRoot 'bin\psql.exe')
    }
    finally {
        $superuserPassword = $null
    }
    [IO.File]::WriteAllText(
        $migrationsStateFile,
        (@{ appliedAt = (Get-Date).ToString('o'); files = $migrationFiles } | ConvertTo-Json),
        (New-Object Text.UTF8Encoding($false)))
    Write-Host "PASS migrations：$($migrationFiles.Count) 份已套用，記錄於 $migrationsStateFile"
}

# ── 模組角色密碼：0001 建的 role 預設沒有密碼，Host 連不進去 ─────────────────
$modulePasswordFile = Join-Path $secretsDir 'module-role.password'
$modulePassword = if (Test-Path -LiteralPath $modulePasswordFile -PathType Leaf) {
    (Get-Content -LiteralPath $modulePasswordFile -Raw).Trim()
} else {
    $generated = New-RandomPassword
    [IO.File]::WriteAllText($modulePasswordFile, $generated, (New-Object Text.UTF8Encoding($false)))
    $generated
}
$alterStatements = ($moduleSchemas | ForEach-Object { "ALTER ROLE greygray_$_ PASSWORD '$modulePassword';" }) -join "`n"
$alterSqlFile = Join-Path ([IO.Path]::GetTempPath()) ('greygray-dev-role-passwords-' + [guid]::NewGuid().ToString('N') + '.sql')
try {
    [IO.File]::WriteAllText($alterSqlFile, "\set ON_ERROR_STOP on`nBEGIN;`n$alterStatements`nCOMMIT;`n", (New-Object Text.UTF8Encoding($false)))
    $superuserPassword2 = (Get-Content -LiteralPath $superuserPasswordFile -Raw).Trim()
    try {
        Invoke-NativeCommand -FilePath (Join-Path $postgresRoot 'bin\psql.exe') `
            -ArgumentList @('--host', '127.0.0.1', '--port', [string]$PostgreSqlPort, '--username', 'postgres',
                '--dbname', 'postgres', '--no-password', '--set', 'ON_ERROR_STOP=1', '--file', $alterSqlFile) `
            -Environment @{ PGPASSWORD = $superuserPassword2; PGCONNECT_TIMEOUT = '10' } `
            -EchoOutput | Out-Null
    }
    finally { $superuserPassword2 = $null }
}
finally {
    if (Test-Path -LiteralPath $alterSqlFile) { Remove-Item -LiteralPath $alterSqlFile -Force }
}
Write-Host "PASS 模組角色密碼：$($moduleSchemas.Count) 個 role（$($moduleSchemas -join ', ')）已設定，明文只在 $modulePasswordFile"

# ── Identity 個資保護金鑰：三個 Host 解析 IdentityModule 時就會要，缺了就 500 ──
<#
    FE-12（第九波）對真後端實測時，登入／註冊／購物車全部 500，根因是
    Identity:DataProtectionKey 從來沒有人投遞過（ModuleRegistration.cs:59-63
    直接丟「缺少 Identity 個資保護金鑰」）。

    ★ 一定要用 New-DataProtectionKey，不能用上面的 New-RandomPassword：
    後者會把 Base64 裡的 + / = 換成 x，而 IdentityDataProtector 要求
    Convert.FromBase64String 解碼後正好 32 bytes（AES-256）。理由見 lib\Secrets.ps1。

    ★ 「已存在就重用」不是為了省事：金鑰換掉之後，iam.customer 既有的密文欄位
    就再也解不開了。這一段跟上面 module-role.password 是同一個形狀，刻意的。
#>
$dataProtectionKeyFile = Join-Path $secretsDir 'identity-dataprotection.key'
$dataProtectionKeyReused = Test-Path -LiteralPath $dataProtectionKeyFile -PathType Leaf
if (-not $dataProtectionKeyReused) {
    [IO.File]::WriteAllText($dataProtectionKeyFile, (New-DataProtectionKey), (New-Object Text.UTF8Encoding($false)))
}
Write-Host "PASS Identity 個資保護金鑰：$dataProtectionKeyFile（$(if ($dataProtectionKeyReused) { '沿用既有' } else { '本次產生' })，明文不印出）"

<#
    ★ Payment:ECPay:* 這支腳本仍然不接，但理由變了（BE-40／ADR-029）。

    原本的理由是「假值只會讓失敗得很難懂」——那是還沒有模擬器時的判斷：
    拿假憑證去打真綠界一定失敗，而且失敗得莫名其妙。
    現在假值是有目的的：ops\start-dev-ecpay-simulator.ps1 會起一支扮演綠界的
    行程（預設 5009），ops\start-dev-hosts.ps1 -UseEcpaySimulator 會把三個 Host 的
    Payment__ECPay__MerchantId／HashKey／HashIV（DEVFAKE 那一組）與兩個端點網址
    指過去。要用就加那個開關；不加時需自行在父行程投遞五個值，兩個網址必填、沒有預設。

    正式碼不受影響：EcpayGateway、回呼判斷、事件、outbox、分錄全部照跑，
    dev 只換掉本來就可設定的 CheckoutUrl／CreditDetailUrl；
    Payment:ECPay:AllowNonEcpayEndpoints（預設 false）會擋住「正式機忘了拿掉 dev 設定」。

    正式機由 ops/deploy.ps1 讀 $secretsDir\ecpay.json（五鍵格式見該腳本）：
    MerchantId／HashKey／HashIV／CheckoutUrl／CreditDetailUrl 缺一個就拒絕部署，
    齊全才注入對應的 Payment__ECPay__*；檔案不存在就跳過。
    本支整備腳本不讀取或投遞這份檔案。舊版只填三鍵會默默指到測試站，BE-49 已移除網址預設。
#>

Write-Host "PASS 本機開發環境整備完成：PostgreSQL 17 ($PostgreSqlPort)、Garnet ($GarnetPort)、migrations 0001~0023。"
Write-Host "下一步：ops\start-dev-hosts.ps1 啟動三個 Host；ops\stop-dev-environment.ps1 全部收掉。"
