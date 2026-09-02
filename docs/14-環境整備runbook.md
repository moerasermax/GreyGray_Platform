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
```

- 預期：兩個變數均取得值，不顯示明文。
- 失敗：取消執行並重新取得；腳本不接受缺少的正式機憑證。

**這裡不再需要 Cloudflare Tunnel token**（BE-43）。`install-environment.ps1` 只負責用
winget 把 `cloudflared.exe` 裝好；GreyGray 自己的通道是**本機管理式**的，憑證是
`cloudflared tunnel create` 產出的 `<UUID>.json`，由 §12 的 `install-tunnel.ps1` 處理。
正式機上既有的 `Cloudflared` 服務是使用者其他應用共用的通道，**整份 runbook 都不碰它**。

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
- 冪等寫入 prod-monitor 的兩個 Next.js process／port 指紋。

它**不會**做的事（BE-43 拿掉的）：安裝或設定任何 Cloudflare Tunnel 服務。
以前這裡會用 token 裝一支 `cloudflared` service，而且在服務已存在時把它的帳號改成
`GreyGraySvc` 再重啟——在 YC 上那支正是使用者其他應用共用的通道，等於直接動別人的線路。
GreyGray 自己的通道見 §12。

預期輸出：第一次每項為 `已安裝 ...` 或既有項目的 `已存在 ...`，最後為：

```text
PASS M-1 安裝完成。仍須依 runbook 人工確認有線網路與 UPS。
```

失敗處理：

- `winget` 失敗：執行 `winget logs --open-logs`，保存當次 installer log，再針對錯誤修正。
- PostgreSQL zip SHA-256 不符：丟棄該 artifact，重新向已核准來源取得，不要跳過驗證硬裝。
- NSSM／服務帳號失敗：用 `Get-CimInstance Win32_Service` 查看 `StartName`，不要改回 LocalSystem；
  若是 `sc start` 錯誤 5，先看 §10，很可能是服務指到的執行檔對服務帳號沒有讀取權。
- `cloudflared.exe` 沒裝起來：`winget install Cloudflare.cloudflared`，再確認 `Get-Command cloudflared.exe`。
  這一步只裝執行檔，不動任何通道服務；通道見 §12。

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
| cloudflared | `Get-Service cloudflared`; `Get-Process ngrok -ErrorAction SilentlyContinue` | 這是使用者其他應用共用的 token 式通道，**不要重裝、不要改帳號**；只用 Event Viewer 看它為什麼停，並移除舊 ngrok 啟動項 |
| `GreyGray-Tunnel` 服務／config.yml／ingress validate | `Get-Service GreyGray-Tunnel`；`Get-Content C:\GreyGray\cloudflared\config.yml`；`cloudflared tunnel ingress validate --config C:\GreyGray\cloudflared\config.yml` | 見 §12：憑證 JSON 有沒有複製進 `C:\GreyGray\cloudflared\`（ACL）、`install-tunnel.ps1` 有沒有跑過、log 在 `C:\GreyGray\logs\GreyGray-Tunnel.stdout.log` |
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

## 11. 2026-09-02 BE-42：部署五個 app 服務（開發機產 artifact → 送到 YC → `deploy.ps1`）

到目前為止，YC 上只有 `GreyGray-PostgreSQL` 與 `GreyGray-Garnet` 兩個服務在跑；
五個 app 服務（`GreyGray-Storefront`／`GreyGray-Admin`／`GreyGray-Worker`／
`GreyGray-Web-Storefront`／`GreyGray-Web-Admin`）都還沒登記過，`deploy.ps1` 也從沒真的跑過。
這一節是「第一次跑」要打的完整指令。

拓樸是 ADR-031：前台網頁與它的 API **同一個主機名稱**（`greygray.shop`，`/v1/*` 進 5000、
其餘進 5002），後台 `admin.greygray.shop`（`/v1/*` 進 5001、其餘進 5003）。
所以下面前台的 `-StorefrontPublicOrigin`、`-StorefrontPublicApiOrigin` 與
`-StorefrontApiBaseUrl` 三個參數是同一個值——**這不是抄錯**。
通道（cloudflared）怎麼接不在這一節。

### 11.1 開發機：產 artifact

可部署的前端在**前端 worktree**（`GreyGray_Platform-fe\frontend`），不是後端樹裡的
`frontend\`（那一份是舊的）。API base 是 `next build` 當下 inline 進 bundle 的
（`NEXT_PUBLIC_*`），事後改不了，所以要在這裡就給對。

```powershell
# 前端樹的 dev server 要先停掉（它跟建置共用 .next\）
.\ops\build.ps1 -Configuration Release -Publish `
    -FrontendRoot 'D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe\frontend' `
    -StorefrontApiBaseUrl 'https://greygray.shop' `
    -AdminApiBaseUrl 'https://admin.greygray.shop'
```

- 兩個 API base **沒給就直接 throw**：正式 artifact 不准默默吃到 `.env.local` 的
  開發機位址（`http://127.0.0.1:5000`／`5001`）——那種錯不會讓建置失敗，
  要到正式站整站打不通 API 才會發現。
- 兩個 app 是**各自**建的（`pnpm --filter @greygray/storefront build` 等），因為兩個值不一樣；
  共用的 `packages/*` 先一次建完。
- 建完會 grep artifact 自己：該有的 API base 要在、`127.0.0.1:5000`／`5001` 一個都不准在，
  否則 throw 並拒絕產出。
- 產物在 `artifacts\`，五個目錄：`GreyGray.Api.Storefront`／`GreyGray.Api.Admin`／
  `GreyGray.Worker`／`GreyGray.Web.Storefront`／`GreyGray.Web.Admin`。

只想確認參數解析而不建置：

```powershell
.\ops\build-frontends.ps1 -ValidateOnly `
    -FrontendRoot '...\GreyGray_Platform-fe\frontend' `
    -StorefrontApiBaseUrl 'https://greygray.shop' `
    -AdminApiBaseUrl 'https://admin.greygray.shop'
```

### 11.2 把 artifact 送到 YC

YC 是 `192.168.0.92`，同一個內網。用 robocopy（`/MIR` 會鏡像，舊版多出來的檔案會被刪掉，
這正是要的——`publish -o` 不會自己清）：

```powershell
robocopy .\artifacts \\192.168.0.92\C$\GreyGray\incoming /MIR /R:2 /W:2
```

`robocopy` 的 exit code **小於 8 都算成功**（1 = 有複製檔案），不要直接用
`if ($LASTEXITCODE -ne 0) { throw }` 判斷。沒有管理共用可用時改走 SSH：
`scp -r .\artifacts <user>@192.168.0.92:C:/GreyGray/incoming`。

### 11.3 YC：`deploy.ps1`

**YC 只有 Windows PowerShell 5.1，沒有 pwsh**，而且實際部署必須在**系統管理員** PowerShell
執行。`deploy.ps1` 不建置（正式機不得建置），只做「artifact → 版本化 release → NSSM」。

先確認機密就位（`C:\GreyGray\secrets\`）：

| 檔案 | 內容 | 沒有的話 |
|---|---|---|
| `ecpay.json` | `{ "MerchantId": "...", "HashKey": "...", "HashIV": "..." }` | 不注入 `Payment__ECPay__*`，Payment 模組會在 DI 解析期明確報缺設定 |
| `module-role.password`、`identity-dataprotection.key` | `deploy.ps1` 自己生成並重用 | — |

**正式商店代號（E3）到手之前**，`ecpay.json` 先放綠界官方文件公開的「特店測試資料」
（MerchantID `3002607`、HashKey `pwFHCqoQZGmho4w6`、HashIV `EkRm7iFT261dpevs`，
見 <https://developers.ecpay.com.tw/?p=2856>）。這樣可以把整條金流路徑串通、用測試卡付款，
而不會產生真的帳。憑證到手之後**只換這個檔案**，不必改任何程式。

```powershell
# 系統管理員 PowerShell（5.1）
$svc = Get-Credential GreyGraySvc            # 服務執行帳號
$mig = Get-Credential postgres               # migration 用的高權限帳號（只活在子呼叫）
.\ops\deploy.ps1 `
    -ArtifactRoot 'C:\GreyGray\incoming' `
    -InstallRoot 'C:\GreyGray' `
    -NssmPath 'C:\GreyGray\bin\nssm.exe' `
    -ServiceCredential $svc `
    -MigrationCredential $mig `
    -MigrationFiles @(
        'db\migrations\0001_schemas_and_roles.sql',
        # …一路列到目前最新的一份；腳本刻意不猜哪些已經套用過
        'db\migrations\0017_....sql'
    ) `
    -StorefrontPublicOrigin 'https://greygray.shop' `
    -StorefrontPublicApiOrigin 'https://greygray.shop'
```

兩個 origin 是 **Mandatory**，而且要是絕對 `https`、不帶結尾斜線、不帶路徑，不合就 throw：

- `Storefront__PublicOrigin`（#33）→ 綠界完成頁「返回商店」按鈕導回
  `{origin}/payment/result?orderId=…`。
- `Storefront__PublicApiOrigin`（BE-42）→ 綠界的 `ReturnURL`（伺服器對伺服器的付款結果回呼）
  `{origin}/v1/webhooks/ecpay`。**通道後面 `Request.Host` 是 `127.0.0.1:5000`**，
  不由設定指定的話綠界永遠打不回來，而且不會有任何錯誤訊息——症狀只是「付款一直停在待付款」。
  綠界要求 `ReturnURL` 對外可達且只准 80／443。

兩個都**只注給 `GreyGray-Storefront`**；Admin／Worker 不碰這兩條路。
不給預設值是刻意的（跟 #33 同一個原則）：正式機猜錯網址的兩種症狀
（付不了款、付完回不了商店）都不會有錯誤訊息。

先 dry-run 一次（不碰 NSSM／排程／DB，一般權限就能跑）：

```powershell
.\ops\deploy.ps1 -ArtifactRoot 'C:\GreyGray\incoming' -InstallRoot 'C:\GreyGray' `
    -NssmPath 'C:\GreyGray\bin\nssm.exe' -ServiceCredential $svc -SkipMigrations -ValidateOnly `
    -StorefrontPublicOrigin 'https://greygray.shop' -StorefrontPublicApiOrigin 'https://greygray.shop'
```

### 11.4 部署後檢查

```powershell
Get-Service GreyGray-* | Format-Table Name, Status
Invoke-WebRequest http://127.0.0.1:5000/health -UseBasicParsing   # Storefront
Invoke-WebRequest http://127.0.0.1:5001/health -UseBasicParsing   # Admin
# 確認兩個 origin 只出現在 Storefront 上
C:\GreyGray\bin\nssm.exe get GreyGray-Storefront AppEnvironmentExtra
```

五個服務都 `Running`、兩個 `/health` 都 200、`GreyGray-Worker` 沒有 port
（設計如此，見 `service-manifest.ps1` 的 `Port = 0`）才算過。

---

## 12. GreyGray 自己的 Cloudflare Tunnel（`GreyGray-Tunnel`，BE-43／ADR-031）

### 12.1 先講清楚不准碰什麼

正式機 YC 上已經有一支 Windows 服務 `Cloudflared`（**原生服務，不是 NSSM**）：

```text
"C:\Program Files (x86)\cloudflared\cloudflared.exe" tunnel run --token-file C:\ProgramData\cloudflared\token
帳號 LocalSystem，Running／Automatic
```

那是 token 式（遠端管理）通道，路由表在 Cloudflare 儀表板，是使用者其他應用
（Planner、Portfolio、Knowledge、Baby、AskAnythingBot…，都是排程工作）共用的命脈。
排程工作 `CloudflaredWatchdog`（`sc.exe start cloudflared`）也是為它存在的。

**任何情況都不准動它**：不改帳號、不重啟、不重裝、不刪 `C:\ProgramData\cloudflared\token`。
GreyGray 另外起一支**本機管理式**的通道 `GreyGray-Tunnel`，設定與憑證都在
`C:\GreyGray\cloudflared\`，兩支互不影響。

### 12.2 Leader 手動的三步（要人在瀏覽器點授權，不在腳本裡）

```powershell
# ① 授權：會印一個網址，貼給使用者在瀏覽器點，選 greygray.shop 這個 zone。
#    憑證會落在「執行這行的那個 Windows 帳號」的 %USERPROFILE%\.cloudflared\cert.pem。
#    透過 SSH 跑時要用 ssh -tt 保持連線（見 §8.4 那一類坑）。
cloudflared tunnel login

# ② 建通道：印出 <UUID> 與憑證 JSON 的路徑（%USERPROFILE%\.cloudflared\<UUID>.json）
cloudflared tunnel create greygray

# ③ 建 CNAME（兩個主機名稱都要，ADR-031）
cloudflared tunnel route dns greygray greygray.shop
cloudflared tunnel route dns greygray admin.greygray.shop
```

- 預期：`tunnel create` 印出 `Created tunnel greygray with id <UUID>`；`route dns` 各印一筆 CNAME。
- 失敗：`cloudflared tunnel list` 看通道在不在；CNAME 衝突要在 Cloudflare DNS 先清掉舊紀錄。

### 12.3 `install-tunnel.ps1`

先 dry-run（不建目錄、不複製憑證、不登記服務，一般權限就能跑）：

```powershell
.\ops\install-tunnel.ps1 -TunnelCredentialsFile "$env:USERPROFILE\.cloudflared\<UUID>.json" -ValidateOnly
```

- 預期：印出解析後的參數與將寫入的 `config.yml`，五條 ingress 順序正確、最後一條是
  `- service: http_status:404`；有 `cloudflared.exe` 時還會多一行
  `PASS cloudflared tunnel ingress validate`（對 OS temp 的暫存檔驗，不碰 `C:\GreyGray`）。

實際安裝（系統管理員 Windows PowerShell 5.1）：

```powershell
$svc = Get-Credential -UserName '.\GreyGraySvc' -Message 'GreyGray 專屬 Windows 服務帳號'
.\ops\install-tunnel.ps1 `
  -InstallRoot 'C:\GreyGray' `
  -TunnelName 'greygray' `
  -TunnelCredentialsFile "$env:USERPROFILE\.cloudflared\<UUID>.json" `
  -ServiceCredential $svc `
  -NssmPath 'C:\GreyGray\bin\nssm.exe'
```

它做的事，每一步都先查「已存在」再做，重跑不重做：

1. 建 `C:\GreyGray\cloudflared\`，把憑證 JSON **複製**成
   `C:\GreyGray\cloudflared\<UUID>.json`（來源檔不動）。放在 `C:\GreyGray` 底下才會繼承
   已經授予 `GreyGraySvc` 的 Modify ACL——服務讀不到自己的憑證就是 §10 那個 `sc start` 錯誤 5。
2. 寫 `C:\GreyGray\cloudflared\config.yml`（UTF-8 無 BOM、LF），然後跑
   `cloudflared tunnel ingress validate --config <path>`，不過就 throw：

   ```yaml
   tunnel: <UUID>
   credentials-file: C:\GreyGray\cloudflared\<UUID>.json
   ingress:
     - hostname: greygray.shop
       path: ^/v1/
       service: http://127.0.0.1:5000
     - hostname: greygray.shop
       service: http://127.0.0.1:5002
     - hostname: admin.greygray.shop
       path: ^/v1/
       service: http://127.0.0.1:5001
     - hostname: admin.greygray.shop
       service: http://127.0.0.1:5003
     - service: http_status:404
   ```

   `path` 是正規表示式，順序就是優先序：每個主機名稱先比 `/v1/`（後端 Host），再吃其餘
   （Next 網頁）。最後一條沒有 `hostname`，是 cloudflared 要求的 catch-all。
3. 用 NSSM 登記 `GreyGray-Tunnel`（設定比照 `deploy.ps1` 那五個服務：`SERVICE_AUTO_START`、
   `AppExit Default Restart`、`AppRestartDelay 60000`、`AppThrottle 1500`、log 輪替到
   `C:\GreyGray\logs\GreyGray-Tunnel.{stdout,stderr}.log`），以 `GreyGraySvc` 執行。
   服務已存在就只 `set` 更新，不重新 `install`。
4. `Start-Service` 並等 `Running`；執行者有 `cert.pem` 時順便印 `cloudflared tunnel info greygray` 的連線數。

它**不做**：`tunnel login`、`tunnel create`、`route dns`（都要人授權，見 §12.2），
以及任何會碰到 `Cloudflared` 服務、`C:\ProgramData\cloudflared\`、`CloudflaredWatchdog` 的事。

### 12.4 怎麼驗

```powershell
Get-Service GreyGray-Tunnel | Format-Table Name, Status, StartType
Get-CimInstance Win32_Service -Filter "Name='GreyGray-Tunnel'" | Select-Object StartName, PathName
Get-Content C:\GreyGray\logs\GreyGray-Tunnel.stdout.log -Tail 30

# 現有的共用通道必須完全沒被動到：PID／StartTime／帳號跟跑之前一樣
Get-CimInstance Win32_Service -Filter "Name='Cloudflared'" | Select-Object State, StartName, ProcessId
```

從**外面**（不是 YC 本機）驗兩個主機名稱：

```bash
curl -i https://greygray.shop/health          # Storefront Host（/v1/* 以外也走 5002，但 /health 在 BFF 上）
curl -i https://admin.greygray.shop/health    # Admin Host
```

- 預期：`GreyGray-Tunnel` 為 `Running`／`Automatic`／`StartName` 是 `.\GreyGraySvc`；
  log 出現 `Registered tunnel connection`（通常四條）；兩個 `/health` 都 200。
  既有的 `Cloudflared` 服務 `ProcessId` 與 `StartName` **沒變**。
- 失敗：`ingress validate` 不過 → 看 `config.yml`；服務起不來且 log 提到憑證 →
  確認 `C:\GreyGray\cloudflared\<UUID>.json` 在不在、`GreyGraySvc` 讀不讀得到（§10），
  **不要改回 LocalSystem**；`/health` 通不了但服務 Running → 檢查 CNAME（§12.2 ③）
  與五個 app 服務是不是都起來了（§11.4）。

最後跑一次完整驗收（§5），新增的 `GreyGray-Tunnel` 三項應該都 PASS。
