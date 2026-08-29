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

### Valkey 的 Windows 限制

Valkey 官方支援清單沒有原生 Windows，官方 repository 的 Windows 支援仍是未完成議題：

- <https://github.com/valkey-io/valkey>
- <https://github.com/valkey-io/valkey/issues/92>

因此腳本**不會偷偷改裝 Redis、Memurai、WSL 或 Docker**。負責人須先核准一份 Windows x64
artifact，確認授權與來源，且壓縮檔根目錄含 `valkey-server.exe`、`valkey-cli.exe`，再計算：

```powershell
$valkeyArchive = 'C:\Installers\valkey-windows-x64.zip'
$valkeySha256 = (Get-FileHash -LiteralPath $valkeyArchive -Algorithm SHA256).Hash
$valkeySha256
```

- 預期：輸出 64 位十六進位 SHA-256，與核准紀錄完全相同。
- 失敗：來源或 hash 不一致就停止；不可使用 `-WhatIf` 假裝已安裝。

## 2. 安裝 .NET、PostgreSQL、Valkey、Node、cloudflared、NSSM

先找出 prod-monitor 正式 TOML 的實際路徑；不要猜路徑。然後執行：

```powershell
$monitorConfig = 'C:\Services\tkflyc-monitor\config.toml' # 由正式機現況確認後填入
Set-Location 'C:\Source\GreyGray_Platform'
.\ops\install-environment.ps1 `
  -InstallRoot 'C:\GreyGray' `
  -PostgreSqlDataRoot 'C:\GreyGray\PostgreSQL\data' `
  -PostgreSqlWalRoot 'C:\GreyGray\PostgreSQL\wal' `
  -ValkeyArchivePath $valkeyArchive `
  -ValkeyArchiveSha256 $valkeySha256 `
  -PostgresSuperuserCredential $postgresCredential `
  -ServiceCredential $serviceCredential `
  -CloudflareTunnelToken $tunnelToken `
  -ProdMonitorConfigPath $monitorConfig
```

腳本使用 WinGet 的精確 package ID：.NET SDK/Runtime 10、ASP.NET Core Runtime 10、
PostgreSQL 17、Node 22、cloudflared、NSSM。它也會：

- 把 PostgreSQL data 與 `pg_wal` 放在 C 槽；若發現來源不唯一會停止，不會猜哪份 WAL 正確。
- 建立 `GreyGraySvc`，授予 `C:\GreyGray` Modify ACL，並讓服務使用專屬帳號。
- 以 NSSM 預先登記五個 GreyGray app service；部署前保持 Disabled，`deploy.ps1` 會換成真 entrypoint 並啟用。
- 以 NSSM 登記並啟動 `GreyGray-Valkey`。
- Valkey 綁定 `127.0.0.1:6379`、開啟 protected mode 與 AOF，不暴露到 LAN。
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
- PostgreSQL WAL 拒絕搬動：保持 PostgreSQL 停止，分辨 data cluster 與 WAL 的唯一來源；不要刪任一邊。
- Valkey hash 不符：丟棄該 artifact，重新向已核准來源取得。
- NSSM／服務帳號失敗：用 `Get-CimInstance Win32_Service` 查看 `StartName`，不要改回 LocalSystem。
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
| PostgreSQL | `Get-Service postgresql-x64-17`; `pg_isready -h 127.0.0.1 -p 5432`; `Get-Item C:\GreyGray\PostgreSQL\data\pg_wal -Force` | 服務帳號、ACL、WAL junction |
| Valkey | `Get-Service GreyGray-Valkey`; `Get-NetTCPConnection -LocalPort 6379 -State Listen` | artifact、NSSM AppDirectory、ACL |
| cloudflared | `Get-Service cloudflared`; `Get-Process ngrok -ErrorAction SilentlyContinue` | tunnel token、Event Viewer、移除舊 ngrok 啟動項 |
| NSSM 五服務 | `Get-CimInstance Win32_Service | Where-Object Name -like 'GreyGray-*'` | 執行 `deploy.ps1` 接上真 artifact；不可用 LocalSystem |
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
| NSSM 服務兩個 | ⚠️ 已建立、設為 Automatic、帳號是 `.\GreyGraySvc`，**但啟動失敗** |
| cloudflared | ✅ 本來就 Running／Automatic |
| node v22.17.0 | ✅ 本來就是正確版本 |

**沒完成的那一項**：`sc start` 回 `錯誤 5：存取被拒`。
不是 1069（登入失敗），所以不是服務帳號密碼或 `SeServiceLogonRight` 的問題
（後者已用 LSA API 授予成功）。**待在機器前面用互動 session 啟動一次確認**；
兩個服務都是 Automatic，重開機也會自動嘗試啟動。

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
