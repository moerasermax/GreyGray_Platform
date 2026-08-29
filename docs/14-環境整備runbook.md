# M-1 環境整備 runbook

本文件只適用於 GreyGray 正式機 YC（Windows 11）。部署採 Native ＋ NSSM，不使用 Docker。
腳本只修改「目前執行它的主機」，不會掃描網路或遠端安裝；實際執行時間與主機由人決定。

## 0. 人工前置檢查

以下兩項無法腳本化，未完成不得把 M-1 視為可上線：

- [ ] **待人工：接有線網路。** 拔掉 Wi-Fi 後，以 `Test-NetConnection 1.1.1.1 -Port 443` 驗證。
  - 預期：`TcpTestSucceeded : True`。
  - 失敗：檢查網路線、交換器、Windows 網卡狀態與 DNS；不要用 Wi-Fi 結果代替。
- [ ] **待人工：購買並接妥 UPS。** 拔掉牆上供電做一次短暫切換測試。
  - 預期：YC 不關機、Windows 仍顯示電池／AC 狀態，網路設備也持續供電。
  - 失敗：停止環境安裝，先修正 UPS 容量、接線或電池。

正式執行前另確認：

```powershell
Get-Volume -DriveLetter C
Get-PhysicalDisk | Format-Table FriendlyName, MediaType, HealthStatus
```

- 預期：C 槽存在、健康，且實體媒體為 SSD/NVMe。
- 失敗：不要把 PostgreSQL data/WAL 改放 D 槽；先由人確認磁碟配置。

## 1. 準備秘密與人工核准的輸入

以系統管理員 Windows PowerShell 開啟 repo，互動輸入密碼／token，不要把值寫進 `.ps1`、shell history 或 repo：

```powershell
$serviceCredential = Get-Credential -UserName '.\GreyGraySvc' -Message 'GreyGray 專屬 Windows 服務帳號'
$postgresCredential = Get-Credential -UserName 'postgres' -Message 'PostgreSQL superuser（只用於首次建 cluster）'
$tunnelToken = Read-Host 'Cloudflare Tunnel token' -AsSecureString
```

- 預期：三個變數均取得值，不顯示明文。
- 失敗：取消執行並重新取得；腳本不接受缺少的正式機憑證，也不會猜 tunnel。

### KV 用 Garnet，不是 Valkey（ADR-022）

Valkey 官方支援清單沒有原生 Windows，官方 repository 的 Windows 支援仍是未完成議題
（<https://github.com/valkey-io/valkey/issues/92>）。改用 Microsoft Garnet：RESP 相容、
有官方 winget 套件（`Microsoft.Garnet.DN8`，只裝 net8.0 那份——net9.0 那份要 .NET 9
runtime，YC 上沒裝也不打算裝），不需要像 Valkey 當初那樣人工核准來源壓縮檔。

winget 裝好的東西放在 `C:\Program Files\WinGet\Packages\...`，那個目錄的 ACL 只開放
給安裝者本人、`Administrators`、`SYSTEM`——服務帳號 `GreyGraySvc` 讀不到。
`install-environment.ps1` 會把套件內容複製一份到 `$InstallRoot\Garnet`，
讓它繼承 `$InstallRoot` 已經授予 `GreyGraySvc` 的 ACL。**這正是 §10 那個 `sc start`
錯誤 5 的根因**，Garnet 與 NSSM 本身都踩過同一個坑。

準備要提供給安裝腳本的 PostgreSQL 官方 ZIP binaries（見 §8.2，EDB 圖形安裝程式在
SSH 下裝不起來）：

```powershell
$postgresZip = 'C:\Installers\postgresql-17.11-1-windows-x64-binaries.zip'
$postgresZipSha256 = (Get-FileHash -LiteralPath $postgresZip -Algorithm SHA256).Hash
$postgresZipSha256   # 應為 6EABDF00D2893713B75DB4336A23C3FDF505F056E217EC6E2E95D901750CFEA3
```

- 預期：輸出 64 位十六進位 SHA-256，與上面的核准值完全相同。
- 失敗：來源或 hash 不一致就停止；不可使用 `-WhatIf` 假裝已安裝。

## 2. 安裝 .NET、PostgreSQL、Garnet、Node、cloudflared、NSSM

先找出 prod-monitor 正式 TOML 的實際路徑；不要猜路徑。然後執行：

```powershell
$monitorConfig = 'C:\Services\tkflyc-monitor\config.toml' # 由正式機現況確認後填入
Set-Location 'C:\Source\GreyGray_Platform'
.\ops\install-environment.ps1 `
  -InstallRoot 'C:\GreyGray' `
  -PostgreSqlDataRoot 'C:\GreyGray\PostgreSQL\data' `
  -PostgreSqlWalRoot 'C:\GreyGray\PostgreSQL\wal' `
  -PostgreSqlZipPath $postgresZip `
  -PostgreSqlZipSha256 $postgresZipSha256 `
  -PostgresSuperuserCredential $postgresCredential `
  -ServiceCredential $serviceCredential `
  -CloudflareTunnelToken $tunnelToken `
  -ProdMonitorConfigPath $monitorConfig
```

腳本使用 WinGet 的精確 package ID：.NET SDK/Runtime 10、ASP.NET Core Runtime 10、
Node 22、cloudflared、NSSM、Garnet（`Microsoft.Garnet.DN8`）。PostgreSQL 不走 winget，
用官方 ZIP binaries 解壓 ＋ 自己 `initdb`（見 §8.2）。它也會：

- 把 PostgreSQL data 與 `pg_wal` 放在 C 槽；`pg_wal` 就留在 `data\pg_wal`，
  不再搬到獨立目錄建 junction（見 §8.3、§10——那個機制本身會壞，且不是必要條件）。
- 建立 `GreyGraySvc`，**先**授予 `C:\GreyGray` Modify ACL，**再**把任何要給
  服務帳號執行的東西（`nssm.exe`、Garnet 套件內容）複製進 `C:\GreyGray` 底下，
  讓它們繼承這個 ACL——順序反過來就會複製到 §10 那個 `sc start` 錯誤 5。
- 以 NSSM 預先登記五個 GreyGray app service；部署前保持 Disabled，`deploy.ps1` 會換成真 entrypoint 並啟用。
- 以 NSSM 登記並啟動 `GreyGray-PostgreSQL` 與 `GreyGray-Garnet`。
- Garnet 綁定 `127.0.0.1:6379`，不暴露到 LAN。
- 把 PostgreSQL data/WAL 加進 Defender exclusion。
- 將 Windows Update 設為人工更新，建立每週日 03:00 的人工維護提醒。
- 安裝一個 cloudflared Windows service，取代六個 ngrok process。
- 冪等寫入 prod-monitor 的兩個 Next.js process／port 指紋。

預期輸出：第一次每項為 `已安裝 ...` 或既有項目的 `已存在 ...`，最後為：

```text
PASS M-1 安裝完成。仍須依 runbook 人工確認有線網路與 UPS。
```

失敗處理：

- `winget` 失敗：執行 `winget logs --open-logs`，保存當次 installer log，再針對錯誤修正。
- PostgreSQL zip SHA-256 不符：丟棄該 artifact，重新向已核准來源取得，不要跳過驗證硬裝。
- NSSM／服務帳號失敗：用 `Get-CimInstance Win32_Service` 查看 `StartName`，不要改回 LocalSystem；
  若是 `sc start` 錯誤 5，先看 §10，很可能是服務指到的執行檔對服務帳號沒有讀取權。
- cloudflared 失敗：用 `Get-Service cloudflared` 與 Windows Event Viewer 檢查；Cloudflare 官方 Windows service 說明：
  <https://developers.cloudflare.com/cloudflare-one/networks/connectors/cloudflare-tunnel/do-more-with-tunnels/local-management/as-a-service/windows/>

## 3. 重跑驗證冪等

以完全相同參數再執行一次第 2 節命令。

- 預期：受管項目回報 `已存在 ...`，不重裝 package、不新增重複 service、不新增第二段 monitor target。
- 失敗：若出現新的 `已安裝` 或重複 target，保存完整輸出並停止部署。

開發機可用沙箱驗證同一套冪等控制流，不需系統管理員，也不碰服務：

```powershell
$sandbox = Join-Path $env:TEMP 'greygray-m1-idempotency'
.\ops\install-environment.ps1 -SimulationRoot $sandbox
.\ops\install-environment.ps1 -SimulationRoot $sandbox
```

- 預期：第一次 22 項 `已安裝`，第二次 22 項 `已存在`、0 項重裝。
- 失敗：執行 `.\ops\environment-self-test.ps1` 取得哪個 assertion 失敗。

## 4. prod-monitor 指紋

安裝腳本內部會呼叫以下命令；亦可獨立重跑：

```powershell
.\ops\register-prod-monitor.ps1 -ConfigPath $monitorConfig
Select-String -LiteralPath $monitorConfig -Pattern 'GreyGray-Web-|port = 500[23]|process_name'
```

預期包含：

| target | process | command line 特徵 | port |
|---|---|---|---:|
| GreyGray-Web-Storefront | node.exe | `GreyGray.Web.Storefront` ＋ `apps\storefront\server.js` | 5002 |
| GreyGray-Web-Admin | node.exe | `GreyGray.Web.Admin` ＋ `apps\admin\server.js` | 5003 |

兩者的 port 都是 `verify_pid = true`，另有本機 HTTP 200 檢查。第二次執行會更新同一個
managed block；若設定已有同名、但不在 managed block 的 target，腳本會 FAIL，要求人工合併。

失敗時：不要只留 port-only 探針；node.exe 在正式機不只一個，必須保留 command line 特徵與 PID 比對。

## 5. 獨立驗收

部署五個 application artifact 後，以系統管理員 PowerShell 執行：

```powershell
.\ops\verify-environment.ps1 `
  -InstallRoot 'C:\GreyGray' `
  -PostgreSqlDataRoot 'C:\GreyGray\PostgreSQL\data' `
  -PostgreSqlWalRoot 'C:\GreyGray\PostgreSQL\wal' `
  -ServiceAccount '.\GreyGraySvc' `
  -ProdMonitorConfigPath $monitorConfig `
  -WiredNetworkConfirmed `
  -UpsConfirmed
```

- 預期：每一行只以 `PASS` 或 `FAIL` 開頭，最後 `OVERALL PASS`，process 結束碼為 0。
- 失敗：最後為 `OVERALL FAIL (N)` 且結束碼 1。逐項修復；找不到的工具、服務或人工確認不會被略過。

主要失敗定位：

| FAIL | 檢查命令 | 修復方向 |
|---|---|---|
| .NET 10 | `dotnet --list-sdks; dotnet --list-runtimes` | 重跑精確 WinGet package 安裝 |
| PostgreSQL | `Get-Service GreyGray-PostgreSQL`; `pg_isready -h 127.0.0.1 -p 5432` | 見下面「NSSM 服務」那列；不是 WAL junction 問題（§8.3 已不用這個機制） |
| Garnet | `Get-Service GreyGray-Garnet`; `Get-NetTCPConnection -LocalPort 6379 -State Listen` | 見下面「NSSM 服務」那列 |
| cloudflared | `Get-Service cloudflared`; `Get-Process ngrok -ErrorAction SilentlyContinue` | tunnel token、Event Viewer、移除舊 ngrok 啟動項 |
| NSSM 服務（含 `GreyGray-PostgreSQL`／`GreyGray-Garnet`／五個 app service） | `Get-CimInstance Win32_Service \| Where-Object Name -like 'GreyGray-*'`；`sc.exe start <name>` 若回**錯誤 5**，先看 §10——多半是 binPath 指到的 `nssm.exe` 對服務帳號沒有讀取權，不是帳號密碼或權限指派問題 | 執行 `deploy.ps1` 接上真 artifact；不可用 LocalSystem |
| Defender | `(Get-MpPreference).ExclusionPath` | 重跑安裝腳本或用 `Add-MpPreference` 補 data/WAL |
| Windows Update | `Get-ItemProperty HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU` | 確認群組原則沒有覆蓋本機設定 |
| prod-monitor | `Select-String $monitorConfig -Pattern 'GreyGray-Web-'` | 重跑 register 腳本並重新啟動 collector |

## 6. PowerShell 5.1／7 與安全自驗

```powershell
powershell.exe -NoProfile -File .\ops\environment-self-test.ps1
pwsh.exe       -NoProfile -File .\ops\environment-self-test.ps1
```

- 預期：兩邊均顯示 AST、安裝冪等、prod-monitor 三個 PASS，最後 `OVERALL PASS environment self-test`。
- 失敗：輸出會列出無法解析的檔名或失敗 assertion；此測試只寫 OS temp，不碰正式服務。

## 7. 維護窗與回復原則

- 每週日 03:00 的 task 只建立 Event Log 提醒，**不會自動安裝 Windows Update 或重開機**；由值班者人工執行、完成後驗五服務與 tunnel。
- PostgreSQL data/WAL 不做破壞性自動回復。任何 junction 或 cluster 歧義都停下由人判斷。
- cloudflared 正常後才移除六個 ngrok 的既有自啟設定；驗收腳本會把仍存在的 ngrok process 判為 FAIL。
- 部署應沿用 `ops/deploy.ps1` 的 PID＋StartTime＋release path 接手驗證，HTTP 200 本身不足以證明新版本接手。

---

## 8. 透過 SSH 遠端執行時會踩到的四個坑

2026-08-29 從開發機 ssh 到 YC（`192.168.0.92`）實際跑一遍時撞到的。
**每一條都是「互動登入沒事、SSH 就壞」**，在自己機器上測不出來。

### 8.1　winget 不在 PATH 上

App Installer 把 `winget.exe` 放在**使用者的 WindowsApps 別名目錄**，
那個目錄只有互動登入的 session 才會被加進 PATH。
`Get-Command winget` 在 SSH session 裡找不到，但 winget 其實裝著。

`install-environment.ps1` 的 `Resolve-WingetPath` 已經處理：先試 `Get-Command`，
再退回 `%LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe`。

### 8.2　EDB 的 PostgreSQL 圖形安裝程式在 SSH 下不能用

症狀：`exit 1`，log 只寫

```
Error writing file ...\postgresql_installer_xxx	emp_check_comspec.bat
Exiting with code 1
```

**不是 COMSPEC 的問題**（實測 COMSPEC 正常、在 TEMP 寫 .bat 並執行也成功）。
真因是安裝程式會自己再提權一次，在非互動 session 下權限脈絡對不上——
它建出來的 temp 目錄是空的，**連原本那個管理員也寫不進去**。

**改用官方 ZIP 二進位**（`postgresql-17.11-1-windows-x64-binaries.zip`，324.9 MB，
SHA256 `6EABDF00D2893713B75DB4336A23C3FDF505F056E217EC6E2E95D901750CFEA3`），
解壓到 `C:\GreyGray\PostgreSQL` 後自己 `initdb`。
這反而更符合 ADR-003（Native ＋ NSSM，不倚賴安裝程式），路徑也完全可控。

### 8.3　`initdb -X`（外置 WAL 目錄）在 Windows 上會失敗

```
initdb: 錯誤: 無法建立目錄 ".../data/pg_wal/archive_status": Invalid argument
```

`-X` 會把 `pg_wal` 做成 junction，而穿過 junction 建子目錄失敗。

**拿掉 `-X` 就好。** 藍圖的要求是「data 與 WAL 都在 C 槽 NVMe」，
`pg_wal` 留在 `data\pg_wal` 一樣在 C 槽，需求照樣滿足，還少一個會壞的機制。

### 8.4　`Start-Process -Credential` 在 SSH 下拿不到 window station

以另一個帳號執行程式會得到 `0xC0000142`（STATUS_DLL_INIT_FAILED）。
非互動 session 建的行程沒有 window station／desktop 存取權。

需要以服務帳號執行一次性工作時，改用**排程工作**（它跑在正確的 session 脈絡下），
或乾脆用目前身分執行再修 ACL——`initdb` 本身**不會**拒絕管理員身分執行
（PostgreSQL 拒絕管理員的是**伺服器行程**，不是 initdb）。

---

## 9. 2026-08-29 在 YC 上實際完成到哪

| 項目 | 狀態 |
|---|---|
| .NET SDK 10.0.400 ＋ runtime 10.0.11 | ✅ winget |
| NSSM | ✅ winget |
| **Garnet 1.0.83**（取代 Valkey，見 ADR-022） | ✅ 實測 RESP `PING` → `+PONG` |
| **PostgreSQL 17.11** | ✅ ZIP 解壓 ＋ `initdb` 完成，`PG_VERSION = 17` |
| `pg_hba.conf` | ✅ 6 條規則全部 `scram-sha-256`，**`trust` 歸零** |
| `postgresql.conf` | ✅ `listen_addresses = 'localhost'`、`port = 5432` |
| 服務帳號 `GreyGraySvc` | ✅ 非管理員，SID `...-1010` |
| NSSM 服務兩個 | ✅ **2026-08-30 BE-13 修復**，見 §10——根因不是帳號，是 `nssm.exe` 本身的 ACL |
| cloudflared | ✅ 本來就 Running／Automatic |
| node v22.17.0 | ✅ 本來就是正確版本 |

`sc start` 錯誤 5 的根因與修復過程見 §10。

### 秘密檔的位置

```
C:\GreyGray\secrets\postgres-superuser.txt   PostgreSQL superuser 密碼
C:\GreyGray\secrets\greygraysvc.txt          GreyGraySvc 服務帳號密碼
```

兩個都 28 字元隨機、ACL 只給 `YC\moera`／`SYSTEM`／`Administrators`。
**這兩組密碼從未離開 YC**，不在任何對話、commit 或 log 裡。

> **一次操作失誤的紀錄**：第一次安裝 PostgreSQL 時把 superuser 密碼放在
> winget 的命令列參數上，winget 把整條指令寫進了它的 log。
> 已刪除該 log 並**更換密碼**，改用 `--optionfile` 讓密碼不進命令列。
> 記在這裡是因為這種錯很容易重犯：**任何 `--password` 類參數都不要放在命令列上。**

---

## 10. 2026-08-30 BE-13：`sc start` 錯誤 5 的根因與修復

### 先排除的可能性

- **SDDL 沒問題**。`sc.exe sdshow GreyGray-PostgreSQL` 與 `GreyGray-Garnet` 都是
  Windows 預設值：`SY`（SYSTEM）與 `BA`（Administrators）完整控制、`IU`／`SU`
  可啟停可讀。沒有任何自訂限制。
- **不是 SSH session 被 UAC 過濾成受限 token**。`whoami /groups` 確認
  `BUILTIN\Administrators` 是 `Enabled group`（不是 deny-only），`whoami /priv`
  裡 `SeDebugPrivilege`／`SeBackupPrivilege`／`SeSecurityPrivilege`／
  `SeTakeOwnershipPrivilege` 全部 `Enabled`，`IsInRole(Administrator)` 回 `True`，
  Mandatory Level 是 `High`——這個 SSH session 本來就是完整管理員權限，
  不是那種「登入進來但沒提權」的情況。
- **不是 `GreyGraySvc` 密碼或 `SeServiceLogonRight`**。docs/13 §5、docs/16 §1 都已經
  記過：錯誤 5 是**存取被拒**，不是 1069（登入失敗），`SeServiceLogonRight` 也已經
  用 LSA API 授予成功。

### 真正的根因

`sc.exe qc GreyGray-PostgreSQL`／`GreyGray-Garnet` 的 `BINARY_PATH_NAME` 都是
`"C:\Program Files\WinGet\Links\nssm.exe"`——NSSM 服務的執行檔本身就是 `nssm.exe`
（它讀自己的服務名去查 `HKLM\...\Services\<name>\Parameters` 才知道要跑誰）。

`icacls 'C:\Program Files\WinGet\Links\nssm.exe'` 只列出三個 ACE：
`YC\moera:(F)`、`BUILTIN\Administrators:(F)`、`NT AUTHORITY\SYSTEM:(F)`。
**`GreyGraySvc` 完全沒有出現**，連 `BUILTIN\Users` 都沒有——雖然這個檔案所在的
`WinGet\Links` 目錄本身有 `BUILTIN\Users:(RX)`，但這個 symlink（指向
`WinGet\Packages\NSSM.NSSM_...\win64\nssm.exe`）在 winget 建立時被寫了一份**不含
`Users`／`Authenticated Users` 的獨立 ACL**，繼承鏈到這裡被切斷了。

SCM 啟動服務時，是用 `GreyGraySvc` 的 token 去 `CreateProcess` 這個 `nssm.exe`。
連檔案都開不了，`StartService` 直接同步回 `ERROR_ACCESS_DENIED`（5）——**服務程式
本身根本沒被啟動過，所以也不可能是 1069 那種登入失敗**。Garnet 的 winget 套件
（`WinGet\Packages\Microsoft.Garnet.DN8_...`）有一模一樣的 ACL 模式，只是當時
還沒被拿來當服務的 binPath，所以沒觸發。

`C:\GreyGray\**` 本身（PostgreSQL data、Garnet 安裝目錄）的 ACL 完全沒問題——
`icacls` 確認 `GreyGraySvc` 在那邊有 `(F)`／`(RX)`，是安裝腳本先前授予
`C:\GreyGray` 的 Modify ACL 正常繼承下來的。**問題只出在 nssm.exe 這一個檔案**，
因為它是 winget 裝的，不在 `C:\GreyGray` 的繼承鏈裡。

### 修復

把 `nssm.exe` 複製一份到 `C:\GreyGray\bin\nssm.exe`（繼承 `GreyGraySvc` 已有的
ACL），再用 `sc.exe config <name> binPath= "C:\GreyGray\bin\nssm.exe"` 把兩個服務
的 binPath 改過去：

```powershell
New-Item -ItemType Directory -Force -Path C:\GreyGray\bin | Out-Null
Copy-Item 'C:\Program Files\WinGet\Links\nssm.exe' 'C:\GreyGray\bin\nssm.exe' -Force
sc.exe config GreyGray-PostgreSQL binPath= 'C:\GreyGray\bin\nssm.exe'
sc.exe config GreyGray-Garnet     binPath= 'C:\GreyGray\bin\nssm.exe'
sc.exe start GreyGray-PostgreSQL
sc.exe start GreyGray-Garnet
```

兩個服務立刻變成 `Running`，`pg_isready` 與 Garnet 的 RESP `PING` 都正常回應。
**沒有改動任何服務帳號設定、沒有降到 LocalSystem**——降低隔離換綠燈正是這個
專案一直在避免的事（docs/16 §5）。

`ops/install-environment.ps1` 已經把這個修復自動化，而且**擴大到任何要用
`GreyGraySvc` 執行的 winget 套件**：`Get-StableNssmPath` 把 `nssm.exe` 複製到
`$InstallRoot\bin`，Garnet 套件內容複製到 `$InstallRoot\Garnet`，兩者都**在
`service-acl` 那一步授予 `GreyGraySvc` 對 `$InstallRoot` 的 Modify ACL 之後**才
複製——順序反過來，複製出來的檔案一樣不會繼承到 ACL，等於重演一次這個坑。
`ops/verify-environment.ps1` 也新增了 `<service> binPath 服務帳號可讀取` 這個
檢查項，直接驗這個根因，不必等服務啟動失敗才發現。
