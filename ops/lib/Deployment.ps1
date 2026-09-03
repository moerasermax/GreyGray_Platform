Set-StrictMode -Version Latest

function Test-PathWithinRoot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Root
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $fullRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    return $fullPath.Equals($fullRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($fullRoot + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-ProcessStartedAfter {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][datetime]$ProcessStartTime,
        [Parameter(Mandatory)][datetime]$RestartBoundaryUtc
    )

    return $ProcessStartTime.ToUniversalTime() -gt $RestartBoundaryUtc.ToUniversalTime()
}

function Get-GreyGrayProcessToken {
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Diagnostics.Process]$Process)

    $path = $null
    try { $path = $Process.Path } catch { }
    $commandLine = $null
    try {
        $cimProcess = Get-CimInstance Win32_Process -Filter "ProcessId = $($Process.Id)" -ErrorAction Stop
        if ($null -ne $cimProcess) { $commandLine = $cimProcess.CommandLine }
    }
    catch { }
    return [pscustomobject]@{
        Id = $Process.Id
        StartTimeUtc = $Process.StartTime.ToUniversalTime()
        Path = $path
        CommandLine = $commandLine
    }
}

function Test-CommandLineReferencesPath {
    [CmdletBinding()]
    param(
        [string]$CommandLine,
        [Parameter(Mandatory)][string]$Path
    )

    if (-not $CommandLine) { return $false }
    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $offset = 0
    while ($offset -lt $CommandLine.Length) {
        $index = $CommandLine.IndexOf($fullPath, $offset, [System.StringComparison]::OrdinalIgnoreCase)
        if ($index -lt 0) { return $false }
        $beforeValid = $index -eq 0 -or [char]::IsWhiteSpace($CommandLine[$index - 1]) -or $CommandLine[$index - 1] -eq '"'
        $afterIndex = $index + $fullPath.Length
        $afterValid = $afterIndex -eq $CommandLine.Length -or
            [char]::IsWhiteSpace($CommandLine[$afterIndex]) -or $CommandLine[$afterIndex] -eq '"'
        if ($beforeValid -and $afterValid) { return $true }
        $offset = $index + 1
    }
    return $false
}

function Test-NodeProcessIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Token,
        [Parameter(Mandatory)][string]$NodePath,
        [Parameter(Mandatory)][string]$EntryPointPath
    )

    if (-not $Token.Path) { return $false }
    if (-not ([System.IO.Path]::GetFullPath($Token.Path).Equals(
            [System.IO.Path]::GetFullPath($NodePath), [System.StringComparison]::OrdinalIgnoreCase))) {
        return $false
    }
    return Test-CommandLineReferencesPath -CommandLine $Token.CommandLine -Path $EntryPointPath
}

function Test-ProcessTokenBelongsToRoot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Token,
        [Parameter(Mandatory)][string]$Root
    )

    if ($Token.Path -and (Test-PathWithinRoot -Path $Token.Path -Root $Root)) { return $true }
    return Test-CommandLineReferencesPath -CommandLine $Token.CommandLine -Path $Root
}

function Test-SameProcessTokenIsAlive {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Token)

    $current = Get-Process -Id $Token.Id -ErrorAction SilentlyContinue
    if ($null -eq $current) { return $false }
    try {
        return $current.StartTime.ToUniversalTime() -eq ([datetime]$Token.StartTimeUtc).ToUniversalTime()
    }
    catch {
        return $true
    }
}

function Get-PortOwnerProcess {
    [CmdletBinding()]
    param([Parameter(Mandatory)][int]$Port)

    $connections = @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)
    if ($connections.Count -eq 0) { return @() }

    $processes = @()
    foreach ($pidValue in ($connections.OwningProcess | Sort-Object -Unique)) {
        $process = Get-Process -Id $pidValue -ErrorAction SilentlyContinue
        if ($null -ne $process) { $processes += $process }
    }
    return $processes
}

function Wait-ProcessTokensExit {
    <#
        .DESCRIPTION
        ★ [AllowEmptyCollection()] 不是可有可無：Mandatory 參數預設**拒收空集合**，
        錯誤是「無法將引數繫結至 'Tokens' 參數，因為它是一個空陣列」。
        而「第一次部署、機器上什麼都沒有」正是空集合最自然的樣子——deploy.ps1 的
        Get-ManagedApplicationTokens 在乾淨機器上找不到任何舊行程、5000-5003 也沒人聽，
        回傳的就是 @()。這條路 self-test 驗不到（deploy.ps1 -ValidateOnly 在更前面就 return），
        所以直到正式機 YC 第二次真跑、17 支 migration 與全部機密都備好之後才炸出來。
        5.1 與 7 都一樣，不是版本差異。

        呼叫端仍然必須明確傳 -Tokens（維持 Mandatory），只是允許它是空的；
        空的代表「沒有舊行程要等」，直接 return。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Tokens,
        [Parameter(Mandatory)][string]$InstallRoot,
        [int]$TimeoutSeconds = 30,
        [scriptblock]$OwnershipValidator
    )

    if ($Tokens.Count -eq 0) { return }

    $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $alive = @($Tokens | Where-Object { Test-SameProcessTokenIsAlive -Token $_ })
        if ($alive.Count -eq 0) { return }
        Start-Sleep -Milliseconds 250
    } while ([datetime]::UtcNow -lt $deadline)

    foreach ($token in $alive) {
        $owned = if ($null -ne $OwnershipValidator) {
            [bool](& $OwnershipValidator $token)
        }
        else {
            Test-ProcessTokenBelongsToRoot -Token $token -Root $InstallRoot
        }
        if (-not $owned) {
            throw "舊 PID $($token.Id) 仍存活，且無法證明它屬於 $InstallRoot；拒絕強制終止。"
        }
        Stop-Process -Id $token.Id -Force -ErrorAction Stop
    }

    Start-Sleep -Milliseconds 500
    $stillAlive = @($alive | Where-Object { Test-SameProcessTokenIsAlive -Token $_ })
    if ($stillAlive.Count -gt 0) {
        throw "舊程序未終止：$($stillAlive.Id -join ', ')。"
    }
}

function Clear-NssmAppParameters {
    <#
        .SYNOPSIS
        把某個服務的 NSSM AppParameters 清空——**不透過 `nssm reset`**。

        .DESCRIPTION
        ★ NSSM 2.24-101-g897c7ad（64-bit 2017-04-26，正式機 YC 的 C:\GreyGray\bin\nssm.exe）
        的 `nssm reset <svc> AppParameters` 一律 heap corruption。Leader 在 YC 逐一實測：

            nssm reset GreyGray-Storefront AppParameters             → exit -1073740940（0xC0000374）
            nssm set   GreyGray-Storefront AppParameters placeholder → exit 0；get 回 [placeholder]
            nssm reset GreyGray-Storefront AppParameters             → exit -1073740940，但 get 回 []
                                                                       （值其實清掉了，crash 在寫完之後）
            nssm set   GreyGray-Storefront AppParameters ''          → exit 1 印 usage（空字串＝沒給值）
            nssm reset GreyGray-Storefront AppEnvironmentExtra       → exit 0（其他參數的 reset 正常）

        也就是：這個 crash 只發生在 AppParameters 這一個鍵上，而且空字串也送不進 CLI，
        所以「用 nssm 清空 AppParameters」這件事在這一版沒有任何可用的 CLI 路徑。
        改成直接寫 registry——nssm 自己讀的就是
        HKLM:\SYSTEM\CurrentControlSet\Services\<svc>\Parameters 的 AppParameters 值。

        流程刻意是「先問再動」：剛 `nssm install` 出來的服務本來就沒有 AppParameters，
        那種情況什麼都不必做（也就不會有任何 registry 寫入）。真的有值才清，清完再問一次驗證。

        .PARAMETER GetAppParameters
        傳回「目前 AppParameters 值」的 scriptblock。做成參數是為了讓 self-test 能在
        HKCU 的暫存鍵上驗這支函式，不必碰真的 nssm 或 HKLM。

        .OUTPUTS
        [bool] 有沒有真的清過（本來就是空的 → $false）。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ServiceName,
        [Parameter(Mandatory)][scriptblock]$GetAppParameters,
        [string]$ServicesRegistryRoot = 'HKLM:\SYSTEM\CurrentControlSet\Services'
    )

    <#
        NSSM 的輸出可能帶 UTF-16 的 NUL 位元組（Invoke-NativeCommand 以 UTF-8 解碼），
        那會讓「其實是空的」看起來非空，於是白白多寫一次 registry。先濾掉再判斷。
    #>
    function Get-NormalizedValue([object]$Raw) {
        if ($null -eq $Raw) { return '' }
        return ([string]$Raw).Replace([string][char]0, '').Trim()
    }

    $current = Get-NormalizedValue (& $GetAppParameters)
    if ($current.Length -eq 0) { return $false }

    $parametersKey = Join-Path (Join-Path $ServicesRegistryRoot $ServiceName) 'Parameters'
    if (-not (Test-Path -LiteralPath $parametersKey)) {
        throw "AppParameters 目前是 '$current' 但找不到 registry 鍵 $parametersKey；拒絕猜測要清哪裡。"
    }
    Set-ItemProperty -LiteralPath $parametersKey -Name 'AppParameters' -Value ''

    $after = Get-NormalizedValue (& $GetAppParameters)
    if ($after.Length -ne 0) {
        throw "已寫入 $parametersKey 的 AppParameters=''，但重讀仍是 '$after'；不接受清不掉還往下走。"
    }
    return $true
}

function Assert-PortReleased {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][int]$Port,
        [Parameter(Mandatory)][string]$InstallRoot,
        [scriptblock]$OwnershipValidator
    )

    $owners = @(Get-PortOwnerProcess -Port $Port)
    if ($owners.Count -eq 0) { return }

    foreach ($owner in $owners) {
        $token = Get-GreyGrayProcessToken -Process $owner
        $owned = if ($null -ne $OwnershipValidator) {
            [bool](& $OwnershipValidator $token)
        }
        else {
            Test-ProcessTokenBelongsToRoot -Token $token -Root $InstallRoot
        }
        if (-not $owned) {
            throw "port $Port 仍由外部 PID $($token.Id) 佔用；拒絕誤殺非 GreyGray 程序。"
        }
        Stop-Process -Id $token.Id -Force -ErrorAction Stop
    }

    Start-Sleep -Milliseconds 500
    if (@(Get-PortOwnerProcess -Port $Port).Count -ne 0) {
        throw "port $Port 的舊 listener 未真正釋放。"
    }
}

function Wait-ManagedServiceStart {
    <#
        .SYNOPSIS
        啟動一個由 NSSM 管理的服務，然後**等 SCM 狀態變成 Running**——不看啟動指令的 exit code。

        .DESCRIPTION
        ★ 2026-09-03 13:02 正式機第二次重複部署死在這裡：`GreyGray-Web-Storefront`
        （Next standalone、`node server.js`）起得慢了幾秒，nssm 等不到 Running 就回

            Unexpected status SERVICE_START_PENDING in response to START control.

        並 exit 非 0。deploy.ps1 當時的 `Invoke-Nssm start` 沒帶 -AllowNonZeroExit，
        於是整支 throw：第五個服務沒 START、真正的綠燈 Assert-NewApplicationProcess 沒跑、
        watchdog 重新登記也沒跑。而那個服務兩秒後就 `Ready in 1641ms`——**它根本沒壞**。
        同一支腳本前一次部署一次過，純粹是那次 Node 起得夠快：這是時間相依的 flaky。

        Leader 在 YC（nssm 2.24-101-g897c7ad）補量的行為：
        `nssm start <已在 Running 的服務>` → 印「已在執行中」、**exit 1**。
        也就是 START 的 exit code 對「正在起」與「已經在跑」都回非 0，
        **exit code 根本不能當成功／失敗的訊號**。

        所以這支函式的成功條件**只有一個**：SCM 狀態變成 Running。
        不看 exit code、不做字串比對（nssm 的訊息會隨語系與版本變）、也不睡固定秒數
        （等的是狀態，不是時間）。真正的綠燈仍然是後面的 Assert-NewApplicationProcess
        （port 擁有者必須是本次 release 起的新行程、/health 200）——這裡只要確認「起來了」。

        .PARAMETER StartService
        真正下啟動指令的 scriptblock，回傳 Invoke-NativeCommand 的結果物件（或 $null）。
        做成參數是為了讓 self-test 用假的 scriptblock 驗決策邏輯，不必碰真的 nssm 或服務。
        ★ 呼叫端傳 **plain** scriptblock，不可以 .GetNewClosure()——動態模組的 parent 是
        global scope，看不到 deploy.ps1 script scope 的 Invoke-Nssm（見 self-test 的 AST 把關）。

        .PARAMETER GetStatus
        回傳目前 [System.ServiceProcess.ServiceControllerStatus] 的 scriptblock。
        ★ 呼叫端一定要先 Refresh()：ServiceController.Status 是快取的，不 Refresh 會永遠讀到舊值。

        .OUTPUTS
        [pscustomobject] ServiceName / Status / StartExitCode / Elapsed。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$ServiceName,
        [Parameter(Mandatory)][scriptblock]$StartService,
        [Parameter(Mandatory)][scriptblock]$GetStatus,
        [Parameter(Mandatory)][ValidateRange(5, 180)][int]$TimeoutSeconds,
        [ValidateRange(10, 10000)][int]$PollMilliseconds = 500
    )

    <#
        nssm 的輸出在 5.1 下可能夾 UTF-16 的 NUL 位元組（Invoke-NativeCommand 以 UTF-8 解碼），
        直接塞進錯誤訊息會變成一串 ？與 NUL。只是為了**印得出來**才正規化——
        這些文字絕對不參與成功判斷。
    #>
    function Get-PrintableNativeText([object]$Raw) {
        if ($null -eq $Raw) { return '' }
        return ([string]$Raw).Replace([string][char]0, '').Trim()
    }

    $startResult = & $StartService
    $startExitCode = $null
    $startStdOut = ''
    $startStdErr = ''
    if ($null -ne $startResult) {
        # Set-StrictMode -Version Latest 下不可以直接碰不存在的屬性，逐一問過再取。
        $properties = $startResult.PSObject.Properties
        if ($null -ne $properties['ExitCode']) { $startExitCode = [int]$startResult.ExitCode }
        if ($null -ne $properties['StdOut']) { $startStdOut = Get-PrintableNativeText $startResult.StdOut }
        if ($null -ne $properties['StdErr']) { $startStdErr = Get-PrintableNativeText $startResult.StdErr }
    }

    <#
        ★ Windows PowerShell 5.1 預設沒有載入 System.ServiceProcess：直接寫
        [System.ServiceProcess.ServiceControllerStatus] 會得到「Unable to find type」。
        deploy.ps1 走到這裡之前一定先跑過 STOP 迴圈的 Get-Service（那會順手載入組件），
        所以正式機碰不到；但 self-test 用假的 scriptblock 直接叫這支函式就會炸——
        別把「湊巧先呼叫過 Get-Service」當成前提。PowerShell 7 本來就找得到，先問再載。
    #>
    if (-not ('System.ServiceProcess.ServiceControllerStatus' -as [type])) {
        Add-Type -AssemblyName 'System.ServiceProcess' | Out-Null
    }
    $runningStatus = [System.ServiceProcess.ServiceControllerStatus]::Running
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSeconds)
    $status = $null
    do {
        $status = & $GetStatus
        if ($status -eq $runningStatus) {
            $stopwatch.Stop()
            if ($null -ne $startExitCode -and $startExitCode -ne 0) {
                Write-Host ("⚠ $ServiceName 的啟動指令回 exit $startExitCode，但服務已 Running（" +
                    "$([int]$stopwatch.Elapsed.TotalMilliseconds) ms）——依 SCM 狀態判定成功。nssm 原話：" +
                    "$startStdOut $startStdErr".Trim())
            }
            return [pscustomobject]@{
                ServiceName = $ServiceName
                Status = $status
                StartExitCode = $startExitCode
                Elapsed = $stopwatch.Elapsed
            }
        }
        Start-Sleep -Milliseconds $PollMilliseconds
    } while ([datetime]::UtcNow -lt $deadline)

    $stopwatch.Stop()
    $lastStatus = if ($null -eq $status) { '<未知>' } else { [string]$status }
    $exitText = if ($null -eq $startExitCode) { '<無>' } else { [string]$startExitCode }
    throw ("$ServiceName 未在 $TimeoutSeconds 秒內進入 Running；最後狀態 $lastStatus。" +
        "啟動指令 exit $exitText，輸出：$("$startStdOut $startStdErr".Trim())")
}

function Suspend-WatchdogTask {
    <#
        .SYNOPSIS
        部署期間停用 GreyGray-Watchdog 排程；回傳「有沒有停用過」（結束時要不要恢復）。

        .DESCRIPTION
        ★ watchdog.ps1 的邏輯是「任何服務不是 Running 就 Start-Service」，而排程每 5 分鐘跑一次。
        deploy.ps1 從 STOP 五個服務到 START 之間，中間隔著 NSSM 參數改寫、機密投遞等幾十秒的空窗；
        watchdog 撞進這個空窗就會拿**還沒改完的參數**（也就是舊 release 的路徑）把服務拉起來，
        接著要嘛 START 撞「已在執行中」、要嘛 Assert-NewApplicationProcess 抓到舊 release 的 PID
        而 throw「並非從本次 release 啟動」。2026-09-03 那次 watchdog 是 13:01:30 跑的、
        STOP 約 13:01:45——差 15 秒沒撞上，純運氣。

        決策邏輯全部吃注入的 scriptblock，是為了讓 self-test 驗得到而**不必呼叫真的
        *-ScheduledTask**（那需要管理員權限，而且在 pwsh 7 走 Windows PowerShell 相容層很慢）。

        .OUTPUTS
        [bool] 有沒有真的停用過。$false = 排程本來就不存在（第一次部署），結束時不必恢復。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$TaskName,
        [Parameter(Mandatory)][scriptblock]$GetTask,
        [Parameter(Mandatory)][scriptblock]$StopTask,
        [Parameter(Mandatory)][scriptblock]$DisableTask
    )

    $tasks = @(& $GetTask | Where-Object { $null -ne $_ })
    if ($tasks.Count -eq 0) {
        Write-Host "· 排程 $TaskName 尚未登記（第一次部署），不必停用。"
        return $false
    }

    $state = ''
    $stateProperty = $tasks[0].PSObject.Properties['State']
    if ($null -ne $stateProperty) { $state = [string]$stateProperty.Value }
    if ($state -eq 'Running') {
        # 正在跑的那一輪 watchdog 已經在動服務了，只 Disable 擋不住它——先 Stop。
        & $StopTask | Out-Null
    }
    & $DisableTask | Out-Null
    Write-Host "✓ 排程 $TaskName 已停用（部署期間；原狀態 $state），結束時會恢復。"
    return $true
}

function Resume-WatchdogTask {
    <#
        .SYNOPSIS
        把 Suspend-WatchdogTask 停用掉的排程恢復。**呼叫端一定要放在 finally**。

        .DESCRIPTION
        成功的部署最後會用 Register-ScheduledTask -Force 以 <Enabled>true</Enabled> 重登記，
        所以成功路徑上恢不恢復其實看不出差別；但**失敗路徑只有 finally 會跑**——
        少了它，一次失敗的部署會讓正式機從此沒有 watchdog，而且不會有任何人說話。

        .OUTPUTS
        [bool] 有沒有真的恢復過。
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$TaskName,
        [Parameter(Mandatory)][bool]$Suspended,
        [Parameter(Mandatory)][scriptblock]$EnableTask
    )

    # 沒停用過就不要動它：排程本來就不存在時 Enable 會炸，而且那是「本來就沒有」，不是我們弄壞的。
    if (-not $Suspended) { return $false }
    & $EnableTask | Out-Null
    Write-Host "✓ 排程 $TaskName 已恢復啟用。"
    return $true
}
