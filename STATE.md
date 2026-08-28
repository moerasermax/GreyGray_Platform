# 現況

**最後更新**：2026-08-28（後端第四波：BE-6 M0 hello-world 本機驗收候選）

## 一句話

後端四波的本機程式已落地：`Identity → outbox → Worker → Notification` 真實垂直切片與永久 PostgreSQL 17 E2E 已通過；M0 `/v1/customers` 是 Development-only、OpenAPI-excluded test hook。**YC 的 NSSM＋BootTrigger reboot 與可查詢 OTLP trace 尚未驗收，frozen M1a `/v1` API 尚未實作**，所以仍不可宣稱完整 M0、整條 CI 或正式部署完成。

## 已完成

| 項目 | 狀態 |
|---|---|
| Solution（52 專案） | ✅ `dotnet build -c Release` 0 error 0 warning |
| 模組硬邊界 | ✅ **14 條**架構測試全綠；新組合根 public-type 規則已故障注入驗證會紅 |
| 線上格式與事件目錄 | ✅ **16 條**契約測試全綠（`tests/GreyGray.Contracts.Tests`，同樣注入驗證過） |
| Platform 基礎設施 | ✅ Outbox、processed-message decorator、API idempotency、Saga Timer、44 事件 registry、`PlatformDbContext`、OTel、clock／correlation context |
| Platform 整合測試 | ✅ **19 條**全綠；真 PostgreSQL 17 Testcontainers，含 rollback／retry、並行去重、lease fencing、雙 worker timer 與 `0003` 實跑 |
| M0 永久 E2E | ✅ **1 條**全綠；真 PostgreSQL 17、真 Storefront／Admin／Worker processes、重送去重、停止後再啟動、Production test hook 404、`0004` owner 故障注入 |
| 14 個模組的 Contracts（ID／DTO／介面／44 個事件） | ✅ 可編譯 |
| Shared.Kernel（Money、Currency、Result、IClock、Dimensions、**JSON**） | ✅ |
| Platform.Abstractions（事件、Outbox、Idempotency、Saga、**事件型別登錄、IAuditWriter**） | ✅ 介面 |
| DB schema 與 role 的 migration | ✅ `0001..0004` **已在 PG 17 容器上實跑並驗證**；新增 `iam.customer`／`notify.notification`，owner、權限、重跑與約束都對 |
| **API 契約（`docs/05` ＋ 兩份 OpenAPI）** | ✅ v1.0 已凍結，24 ＋ 29 個端點 |
| **前端 workspace（Next.js ×2 ＋ token ＋ api-client）** | ✅ `pnpm build` 兩個 app 都過 |
| **設計 token（Soft Seoul ＋ Admin）** | ✅ 對比度實際量過，都達 AA |
| 三個 Host 的 `Program.cs` | ✅ Storefront 有 Development-only M0 test hook；Worker 已接 outbox、Saga Timer 與啟動時 registry 驗證；Admin health 可重啟。正式公開 `/v1` 仍待 M1a |
| CI／Windows 部署工具 | ⚠️ 五服務 manifest、.NET／Next standalone artifacts、PS 5 self-test、versioned release、NSSM、watchdog 已完成；即時 OpenAPI gate 等 `/v1` 端點後才能綠 |

## 後端第一波總驗收（2026-08-28，由 Claude 執行）

在 `GreyGray_Platform-verify`（detached 於 `0a3d151`）跑的，**不在 Codex 的工作樹上**——
它當時還在寫 BE-2，在那棵樹上驗會把未交付的東西一起算進來。

```
dotnet build .\GreyGray.slnx     0 警告 0 錯誤，20.85 秒
.\ops\test.ps1                   3 個專案 34 條全綠（架構 12 ／契約 16 ／Platform 6）
```

十條逐項：

| # | 檢查 | 結果 |
|---|---|---|
| 1 | 越界檔案 | ⚠️ 兩處，見下 |
| 2 | 金額位置的 `decimal` / `double` | ✅ 三處全在 `Money.OfMajor` 與 `FxSnapshot.Rate`（換算邊界與匯率，非金額欄位），且非本波新增 |
| 3 | `DateTimeOffset.UtcNow` | ✅ 只有 `Time/SystemClock.cs:12`（`IClock` 實作）＋ 一處註解 |
| 4 | `new JsonSerializerOptions` | ✅ 只有 `Json/GreyGrayJson.cs:50` |
| 5 | migration 的 `SET ROLE` ＋ owner 斷言 | N/A —— 本波沒動 migration |
| 6 | `SET LOCAL` 而非 `SET` | ✅ `OutboxDispatcher.cs:119` 是 `SET LOCAL app.tenant_id`（⚠️ 見下） |
| 7 | 注入架構違規會不會紅 | ✅ 實測：注入 `Ordering.Core → Ledger.Core`，「Core 不得參考其他模組的 Core」FAIL 且訊息直指違規者；復原後 12/12 回綠 |
| 8 | `KnownEventTypes` 對事件目錄 | ✅ `Registry_contains_the_exact_44_event_catalog_entries` 同時斷言 `Count == 44` 與 `SetEquals`，是逐條比對不是只比數量 |
| 9 | `AddOpenApi()` 對凍結契約 | ✅ gate 行為正確（⚠️ 見下） |
| 10 | 服務清單有沒有前端 5002／5003 | ❌ 沒有——但這條是驗收當天才加進派工書的，Codex 派工時不知情 |

**結論：實質通過。** 不合格的兩條都不是「地基歪了」，是流程與後補要求。

### 第 1 條：兩處越界，都沒有先回報

| 檔案 | 表上的狀態 | 判斷 |
|---|---|---|
| `docs/api/openapi.storefront.yaml` | `docs/**` 是無主檔 | 技術上必要（flow context 的 `1:1` 嚴格 parser 讀不了），語意零變化。已在 `docs/05` 補規則並事後認可 |
| 14 × `*.Contracts/IntegrationEventJsonContext.cs` | 任何 `*.Contracts/**` 是無主檔 | 架構上**非放這裡不可**——source-generated JSON metadata 必須與事件型別同組件，registry 才掃得到。所有權表改成 BE-1 明確擁有這個檔名 |

兩處都是新增而非覆蓋，且第一波沒有別的包會碰到這些路徑，**沒有造成任何工作被蓋掉**。

### 第一波驗收揭露的兩個工程問題

1. ✅ **字串內插 `SET LOCAL` 已修正**：
   `var sql = $"SET LOCAL app.tenant_id = '{tenantId.Value:D}'"`。
   `tenantId.Value` 是 `Guid` 且用 `:D` 格式化，塞不進引號，所以**現在是安全的**。
   但形狀是 SQL injection 的形狀，下一個人照抄去接字串型別的參數就出事。
   Outbox、processed-message 與 Saga dispatcher 現在都改用
   `SELECT set_config('app.tenant_id', @tenantId, true)`——等效 transaction-local 且可參數化。
2. **CI 的 `windows-contract` job 從現在到 M1a 端點接線前會一直是紅的。**
   gate 本身沒寫錯（它拒絕把只有 `/health` 當成契約同步，這正是要的行為），
   但「CI 長期紅燈」會訓練所有人忽略 CI，那比沒有 gate 更危險。要決定是接受，
   還是讓這個 job 在端點數為 0 時回報「預期中的未完成」而非 fail。

### 順帶：Codex 越波次了

驗收當下它已經在寫 BE-2（`ProcessedMessage`、`IdempotentIntegrationEventHandler`），
但派工書寫的是「只做這三包，第二波等驗收過再說」。
交付本身沒被汙染（BE-2 的檔案是未提交的），但**下一次派工要把這條講得更死**。

---

## 後端第二波交付內容（**Codex 自述**，2026-08-28）

> 這一段是 Codex 自己寫的交付說明，**不是驗收結論**。
> 總驗收在下一段，由 Claude 獨立執行（`docs/07` §5：「由我做，不是由 Codex 自己說了算」）。
> 兩邊的數字互相對得上，所以自述沒有灌水——但結論仍然不該由交付方自己下。

- **BE-2**：`platform.processed_message` 以 `(event_id, handler_name)` 去重；marker、handler
  副作用與 `SaveChanges` 共用模組 DbContext transaction。兩個獨立 scope 並行競爭時仍只執行一次，
  handler 中途失敗會完整 rollback 並可重試。
- **BE-3 / Idempotency**：四種 outcome、24 小時 retention、首次 response 原狀快照與原 HTTP status
  都保留；同 key 不同 payload 會拒絕。過期／abandon reclaim 使用 DB `created_at` fencing token，
  舊請求不能覆蓋新 lease 的完成結果。
- **BE-3 / Saga Timer**：排程、取消、cancel-all、固定 advisory lock `1002` 與失敗重試已完成；
  兩個 worker 同時掃描只會派送一次，tenant 經 correlation scope 與 transaction-local
  `set_config(..., true)` 傳遞。
- **BE-8 補齊**：service manifest 現為 3 個 .NET ＋ 2 個 Next standalone；前端固定
  `GreyGray-Web-Storefront:5002`、`GreyGray-Web-Admin:5003`，正式服務直接執行可信
  `node.exe + server.js`。Node／pnpm 缺失會明確 fail-fast。
- **Windows + pnpm artifact**：standalone 內的 symlink／virtual-store context 會在建置時實體化，
  不要求 YC 開 symlink 權限。Storefront／Admin 各解參考 20 個 link，最終 artifact 均為
  0 reparse point，從 workspace 外啟動後 `/` 都回 HTTP 200。

自驗結果：Release build **0 warning／0 error**；Architecture **12/12**、Contracts **16/16**、
Platform **18/18**，合計 **46/46**；`ops/self-test.ps1` 全綠。Live OpenAPI gate 仍因尚無
`/v1` endpoints 誠實紅燈，狀態與第一波相同。

---

## 後端第二波總驗收（2026-08-28，由 Claude 執行）

```
dotnet build .\GreyGray.slnx     0 警告 0 錯誤
.\ops\test.ps1                   46 條全綠（架構 12 ／契約 16 ／Platform 18）
```

Platform 從 6 → 18 條，全部跑在真的 PostgreSQL 17 容器上。
建置與測試是在主工作區跑的（未提交狀態），事後比對確認 **16:49 之後沒有任何 `.cs` 被改動**，
所以結果與 commit `2930223` 的程式碼完全對應。第 7、9 條在 detached 於 `2930223` 的
乾淨 worktree 補跑。

| # | 檢查 | 結果 |
|---|---|---|
| 1 | 越界檔案 | ✅ 一處但正當，見下 |
| 2 | 金額位置的 `decimal` / `double` | ✅ 只有 `Money.OfMajor`、`FxSnapshot.Rate` ＋ 註解 |
| 3 | `DateTimeOffset.UtcNow` | ✅ 只有 `Time/SystemClock.cs:12` |
| 4 | `new JsonSerializerOptions` | ✅ 只有 `Json/GreyGrayJson.cs:50` |
| 5 | migration 的 `SET ROLE` ＋ owner 斷言 | N/A —— `platform` 四張表 `0002` 就建好了，本波沒動 |
| 6 | 租戶設定用 transaction-local | ✅ Outbox／processed-message／Saga 三處**一致**改用 `set_config(..., true)` 並參數化 |
| 7 | 注入架構違規會不會紅 | ✅ 實測：注入 `Ordering.Core → Ledger.Core`，**只有**「Core 不得參考其他模組的 Core」紅、訊息直指違規者；復原後 12/12 |
| 8 | `KnownEventTypes` 對事件目錄 | ✅ 測試涵蓋，44 條逐項比對 |
| 9 | `AddOpenApi()` 對凍結契約 | ✅ 實跑，仍正確 FAIL-FAST（與第一波相同，等 `/v1` 端點） |
| 10 | 服務清單有沒有前端 5002／5003 | ✅ 補齊，`-Web-` 命名有隔開，缺 node 是明確 `throw` |

**結論：通過。** 第一波不合格的兩條這次都補上了。

**第 1 條的那一處**：`src/Platform/Outbox/OutboxDispatcher.cs` 是 BE-1 的檔案，
不在 BE-2／BE-3／BE-8 的車道內。但那是**照第一波驗收意見修正**字串內插 SQL，
屬於回應驗收而非擅自越界。其餘 25 個檔案全部在自己的車道內。

### BE-2 最關鍵的性質是對的

`IdempotentIntegrationEventHandler` 自己開交易，**拒絕被包在呼叫端既有交易內**（會 throw），
`processed_message` 的 marker 與業務 `SaveChanges` 在**同一個交易**內 commit，
`ON CONFLICT DO NOTHING` 回傳 0 就直接 commit 並返回。
rollback 用 `CancellationToken.None`——取消也會確實回滾，marker 不會留在未完成交易裡。

### 三個待處理

1. **Node 版本四邊對不起來**：
   `frontend/package.json` 宣告 `>=22.0.0`、`ops/build-frontends.ps1` 守 `>= 20`、
   `ci.yml` 裝 `20.x`、開發機跑 `24.15`。`.npmrc` 沒開 `engine-strict`，
   所以 pnpm 只警告不擋——**CI 會用一個 workspace 自己宣告不支援的版本建置成功**。
   四個數字要收斂成一個，建議統一到 22。
2. **`ci.yml` 的 `timeout-minutes: 30` 要重算**：Platform.Tests 現在單獨就要 **5.6 分鐘**
   （Testcontainers 起真 Postgres，值得，但成本是真的）。兩個 job 都跑這套，
   加上新增的前端 standalone 建置，30 分鐘會開始緊。
3. **`OutboxMessage.cs:50` 的文件漂移**：註解還寫「派送前先 `SET LOCAL app.tenant_id`」，
   但程式碼已改成 `set_config(..., true)`。等效，但下一個人會照註解找不到對應的碼。

**第一波留下的「CI 長期紅燈」仍未決定**（見上一段第 2 點）。

---

## 後端第三＋四波總驗收（2026-08-28，由 Claude 執行）—— **M0 程式完成，部署未驗**

> 我原本寫「M0 完成」，那講過頭了。Codex 的 `HANDOFF_6` 用的是「M0 本機功能驗收候選」，
> 那個講法才對：程式路徑、交易、冪等、trace、migration、架構邊界都驗過了，
> 但 **YC 上沒跑過 NSSM ＋ 重開機**，trace 也**沒送進可查詢的 OTLP backend**。
> M-1 環境整備一件都還沒做，所以部署那一半根本還沒有機會被驗。

驗 `7fe8237`（BE-5 組合根 ＋ BE-7 通路接縫）與 `25a0dad`（BE-6 hello-world 垂直切片）。
在 detached 於 `25a0dad` 的乾淨 worktree 跑，不在 Codex 的工作樹上。

```
dotnet build .\GreyGray.slnx     0 警告 0 錯誤，20.12 秒
.\ops\test.ps1                   50 條全綠
                                 架構 14 ／契約 16 ／端對端 1 ／Platform 19
```

| # | 檢查 | 結果 |
|---|---|---|
| 1 | 越界檔案 | ⚠️ 五處，全是「跨波」不是「同波互蓋」，見下 |
| 2 | 金額位置的 `decimal` / `double` | ✅ `Shared.Kernel` 以外**為零** |
| 3 | `DateTimeOffset.UtcNow` | ✅ 只有 `Time/SystemClock.cs:12` |
| 4 | `new JsonSerializerOptions` | ✅ 只有 `Json/GreyGrayJson.cs:50` |
| 5 | migration 的 `SET ROLE` ＋ owner 斷言 | ✅ **這次真的適用**：`0003`、`0004` 都有 `SET ROLE greygray_owner` ＋ `RESET ROLE` ＋ 斷言 `DO` 區塊，訊息直指「開頭少了 SET ROLE」 |
| 6 | 租戶設定 transaction-local | ✅ 沿用第二波的 `set_config(..., true)` |
| 7 | 注入架構違規會不會紅 | ✅ 這次注入的是**這一波新加的規則**（在 `Identity.Infra` 加一個 public 型別），「Infra 對外只暴露各自的組合根」FAIL 且訊息清楚；移除後 14/14 |
| 8 | `KnownEventTypes` 對事件目錄 | ✅ 契約測試涵蓋，仍是 44 |
| 9 | `AddOpenApi()` 對凍結契約 | ✅ 行為正確，仍 FAIL-FAST（見下） |
| 10 | 服務清單有前端 5002／5003 | ✅ 第二波已補，本波未動 |

**結論：通過。M0 完成。**

### 第一次有業務程式碼，六條鐵則額外查過

| 鐵則 | 結果 |
|---|---|
| 金額一律 `Money` | N/A —— 這一波沒有金額欄位 |
| 時間一律經 `IClock` | ✅ `CustomerProvisioningService` 與 Notification handler 都用 `clock.UtcNow` |
| 模組只參考別人的 `*.Contracts` | ✅ Host 只呼叫 `*.Infra` 的 `Add*Module`，沒有 `using` 任何 `*.Core` |
| 禁止跨 schema JOIN | ✅ 全 repo 掃過，**零** |
| 可預期失敗回 `Result` | ✅ 顯示名稱空白／超長都回 `Result.Failure`，不是丟例外 |
| 註解繁中、命名英文 | ✅ |

`Customer` 是 `internal sealed`，outbox 與業務資料共用 `IdentityDbContext`，
一次 `SaveChanges` 的隱式交易保證原子性——ADR-016 做對了。
`0003` 用 CHECK constraint 把 M0 鎖住（`source_channel = 0`、
`quantity_channel_allocated = 0`），欄位為 M3 多通路預留但現在改不動。

### 第 1 條：五處越界，但性質不同

`Identity.Contracts`（＋`ICustomerProvisioning` input port）·
`tests/GreyGray.Architecture.Tests/ModuleCompositionRootTests.cs`（＋2 條新規則）·
三個 Host 的 `Program.cs`（模組接線）· `ops/self-test.ps1` · `GreyGray.slnx`

**全部都是「後面的波動到前面的波已完成的檔案」，不是「同一波兩個 agent 互相覆蓋」。**
所有權表的目的是防後者。這件事每一波都會重複發生，所以規則要講清楚：

> **所有權表是「同一波之內」的邊界，不是跨波凍結。**
> 後續波次為了接線而修改前面波次的檔案是正常的；
> 要擋的是同一波裡兩個平行 agent 動到同一個檔案。

`ICustomerProvisioning` 是純新增、沒動任何事件、44 條契約測試未變。
Core 的型別全是 internal，Host 要呼叫就必須有 Contracts 上的 port——架構上非放那裡不可。

### M0「完成」的實際含義，不要誤讀

1. **14 個模組只接了 2 個**（Identity、Catalog）。這是 BE-5 的明訂範圍（「先只做 Identity 與 Catalog」），
   其餘 12 個是照樣板複製的工作。
2. **`/v1/customers` 是 Development-only ＋ `ExcludeFromDescription()`**，
   正式環境仍然只有 `/health`。它是驗證垂直切片用的，不是契約端點。
3. **因此 BE-8 的 OpenAPI gate 最終驗收沒有隨 BE-6 完成**——
   `HANDOFF_3` 原本寫「待 BE-6 提供 `/v1` endpoints 後完成」，但 BE-6 提供的不算。
   那一關要等 M1a 真正實作契約端點。

**一句話**：地基與第一條垂直切片都通了，架構被端對端證明可行；
但系統對外仍然什麼都不做。業務功能是 M1a 的事。

---

## M1a 總驗收（2026-08-28，由 Claude 執行）—— **不通過，第 9 條**

驗 `6158077 feat(m1a): implement end-to-end commerce backend`
（120 檔案／17,591 行新增／8 個業務模組／6 個新測試專案），
在 detached 於該 commit 的乾淨 worktree 上跑。

```
dotnet build .\GreyGray.slnx     0 警告 0 錯誤
.\ops\test.ps1                   10 個專案 108 條全綠（原本 50 條）
```

第 2～8、10 條全過。第 7 條實測注入 `Identity.Infra` 的 public 型別，
「Infra 對外只暴露各自的組合根」會紅、移除後 14/14。

### 第 9 條：gate 這次真的比對了，抓到兩類

**這是契約凍結以來第一次能做完整差異比對**（在此之前 Host 只有 `/health`，
gate 一直是 FAIL-FAST）。

**一、真的少做了 12 個端點——M1a 是實質未完成**

```
storefront   凍結 24 → 實作 23
             缺 /v1/inquiries/{inquiryId}/reply
                /v1/orders/{orderId}/shipments
admin        凍結 32 → 實作 23
             缺 purchase-items 的 purchased／unavailable／price-changed
                shipments 的建立／dispatch／deliver
                lots、trip-costs、訂單單品項取消
```

admin 缺的那批是**採購與出貨**，也就是代購生意的後半段。
不是收尾問題，是整塊沒做。

**二、行為對了但 OpenAPI 沒宣告**

`Idempotency-Key` 在產出的 OpenAPI 裡完全不存在——不是元件、也沒有 inline。
但實作是對的：`Platform/Http/BffHttp.cs` 會讀這個 header、沒帶回 400、
超過 255 字回 400、payload 雜湊比對、四種 outcome 都處理（含 409 key-reused），
`IIdempotencyStore` 注入到 18 個以上的端點。

同類還有 `components/headers` 的 `SessionCookie` 與 `components/parameters`
的 `Cursor`／`Limit`。**要補的是端點的 OpenAPI metadata，不是重寫邏輯。**

`/health` 多出來是設計如此，不算差異。

### 這一條的意義

gate 沒有寫錯，也沒有誤報。**它擋下的正是「實作悄悄偏離凍結契約」**——
如果沒有這一關，缺的那 12 個端點會等到前端第二波接上去、
打了 404 才被發現，而那時前端已經照契約把畫面都做完了。

---

## 2026-08-28 第二輪：邊界稽核修掉的六類問題

派工前做了一次完整稽核，發現的都不是小事——每一條都會在平行開發時被放大。

| # | 問題 | 處置 |
|---|---|---|
| A1 | `outbox_message.aggregate_type/id` 是 NOT NULL，但 `PublishAsync` 拿不到值 | `IIntegrationEvent` 加兩個 abstract 成員，44 個事件全部填好，**編譯器強制** |
| A2 | `static abstract EventType` 只能正向，dispatcher 反查不回 CLR 型別 | 新增 `IIntegrationEventTypeRegistry` |
| A3 | 「outbox 同交易」與「每模組一個 DbContext、不 map 別人的表」互斥 | ADR-016：`platform` schema 是刻意的共用例外 |
| B | `ALTER DEFAULT PRIVILEGES FOR ROLE greygray_owner` 只對 owner 建的物件生效，`0002` 沒 `SET ROLE` → **所有模組 role 對所有表零權限** | 加 `SET ROLE` ＋ 結尾 owner 斷言。**已在真的 PG 17 上三種情境各跑一次驗證**（見下） |
| C | 5 條訂閱關係缺 `ProjectReference`，含 `Ledger → procurement.GoodsReceived`（DR 存貨/CR 現金，關鍵路徑） | 全部補上 |
| D | 沒有測試阻止業務模組依賴支撐模組；Contracts 可用 PackageReference 繞過 | 架構測試 10 → 12 條，兩條都注入驗證過 |
| E | 44 個事件的 JSON 形狀沒定案，outbox payload 一旦有資料就改不動 | ADR-018 ＋ `GreyGrayJson.Options` 為唯一來源 |
| F | Storefront（公開行程）參考全部 14 個 Infra | 砍掉 Notification 與 Reporting |

**連帶**：`IAuditWriter` 與 `AuditCategory` 從 `Audit.Contracts` 移到
`Platform.Abstractions.Audit`（ADR-017）——個資存取留痕是同步的，
留在 Audit.Contracts 會逼 `Identity.Core` 依賴支撐模組。

### B 的實測（2026-08-28，PostgreSQL 17.11 容器）

| 情境 | migration 回報 | 實際結果 |
|---|---|---|
| 修正前：無 `SET ROLE`、無斷言 | **成功** | 14 個模組 role 對 `outbox_message` **零權限**——靜默失敗 |
| 只忘 `SET ROLE`（斷言在）| **失敗並整個 rollback** | platform schema 一張表都沒建，錯誤訊息直指原因 |
| 修正後 | 成功 | 15 個 role 權限正確、4 張表 owner 都是 `greygray_owner`、無 BYPASSRLS |

第一列就是這條沒被抓到的話會發生的事：部署腳本一片綠，
然後服務啟動時報 `permission denied for table`，而訊息完全不指向真正的原因。

## 未完成

**後端**：BE-1～BE-5、BE-7 與 BE-6 的本機程式／永久 E2E 已完成；BE-8 的五服務工具與
fail-closed 門檻已完成。正式 M0 尚差 YC NSSM＋BootTrigger reboot 與可查詢 OTLP trace；
strict live OpenAPI 要等 M1a frozen endpoints 實作後才能歸零。詳見 `management/history/HANDOFF_6.md`。

**前端**：見 `docs/06-前端工作包.md`。**工作在 `GreyGray_Platform-fe` 分支
`feat/frontend-wave-1`，不在這棵樹上**——下面講的東西在這個分支上看不到，要切過去。

| 包 | 狀態 |
|---|---|
| FE-1 型別 ＋ mock ＋ 端點層 | ✅ 通過。msw 掛好（SSR ＋ browser 兩端實測），27 條 smoke 測試 |
| FE-2 Soft Seoul 元件庫 | ✅ 通過。kitchen-sink 頁展示全部狀態 |
| FE-6 後台殼 ＋ 登入 ＋ 儀表板 | ✅ 通過。淺色與深色兩套都量過對比度 |
| FE-3 逛與找 · FE-4 買 · FE-5 我的 · FE-7 後台商品／開團 · FE-8 後台訂單／帳務 | ⬜ 第二波，`docs/09` 還缺這五則子 agent prompt |

第一波驗收紀錄在 `docs/09-前端第一波派工prompt.md` 末尾。
`NEXT_PUBLIC_USE_MOCK=1` 可用，**第二波完全不需要後端**。

### 前端留下的一題（產品決定）

契約的 `unitPriceLabel` 範例寫 `NT$780／32 顆`，但共用的 `formatMoney`
對台幣輸出 `$780`——`$` 才是正確的 ICU 行為（`zh-TW` 是台幣本地語系，
外幣才帶 `US$`／`HK$` 前綴）。**同一張商品卡上會同時出現兩種寫法。**
要嘛契約改成 `$`，要嘛 `formatMoney` 對 TWD 特別加 `NT$`。改一處就好，
不要讓呼叫端各自加前綴。

## 環境現況

| 項目 | 狀態 |
|---|---|
| 開發機 dotnet 10.0.301 / Node 24.15 / pnpm 11.5 | ✅ |
| 開發機 Docker Desktop 4.87（Testcontainers 用）| ✅ `postgres:17-alpine` 已預先拉好 |
| 開發機 PostgreSQL（原生安裝）| ❌ 沒有，也不需要——測試走容器 |
| 正式機 YC：dotnet / PostgreSQL / Valkey / cloudflared | ❌ 四項都還沒裝（M-1） |
| ngrok → cloudflared 遷移 | ❌ 未開始 |
| 有線網路、UPS、Defender 排除、專屬 Windows 帳號 | ❌ 全部未做（M-1） |

**M-1 環境整備一件都還沒做。** 它不擋現在的開發，但擋 M1 上線。
前端多了兩個 native process（:5002 storefront、:5003 admin），要一併登記進 prod-monitor。

## 已知問題

### `dotnet test` 在 SDK 10.0.301 上跑不起來
`dotnet test` 會走 VSTest 路徑並直接報錯。`dotnet.config` 的 `[dotnet.test:runner]` 與
`TestingPlatformDotnetTestSupport` 兩種 opt-in 都試過，都沒生效（2026-08-28 實測）。
**繞法**：`.\ops\test.ps1`，直接跑 xunit.v3 產出的執行檔。

### `Microsoft.OpenApi` pin 在 2.12.2，不要跳 3.x
`Microsoft.AspNetCore.OpenApi` 的 source generator 產的碼依賴 2.x 的 API 形狀。

### 即時 OpenAPI gate 現在仍必定紅
`ops/check-openapi.ps1` 會啟動剛建好的 Storefront／Admin Host 並抓 `/openapi/v1.json`。
公開文件中兩個 Host 現在都只有 `/health`；M0 `/v1/customers` 刻意只在 Development 映射並
`ExcludeFromDescription()`，所以 gate 會以 exit 1 fail-fast。這是防止空 schema 假綠，不是工具故障。
Frozen `/v1/auth/register` 與其餘 endpoints 屬 M1a，完整比較只能在正式 API 接線後完成。

### 多個 source-generated `JsonSerializerContext` 不可共用同一個 options instance
實測會拋出 `InvalidOperationException`：options 被第一個 context 封裝後不能再修改。
各 Contracts context 必須各呼叫 `GreyGrayJson.CreateOptions()`，設定來源仍只有 ADR-018 那一份。

### YAML flow mapping 內含冒號的文字必須加引號
Storefront frozen OpenAPI 曾有兩個 `1:1` description 未加引號，嚴格 YAML parser 會判定語法錯誤。
目前只修正 quoting，沒有改變 API shape。

### 前端套件版本還沒釘死
`frontend/*/package.json` 目前用 caret 範圍，靠 `pnpm-lock.yaml` 保證重現。
第一輪整合完成後應改成精確版本，與 `Directory.Packages.props` 的做法一致。

### Windows 不可直接複製 pnpm 的 Next standalone
`.next/standalone` 會含指向 pnpm virtual store 的 symlink／reparse point；`Copy-Item` 與
`robocopy` 在一般 Windows 權限下可能 Access denied，單純解參考又會遺失套件的 realpath
依賴 context。正式入口只能走 `ops/build-frontends.ps1` 的 materializer，並以「0 reparse ＋
workspace 外 `node server.js` 回 HTTP 200」作 portability 證據。

## 下一步

1. **後端 M1a**：實作 frozen `/v1/auth/register` 的手機／密碼／session／idempotency 正式流程
2. M1a endpoints 接線後讓 strict live OpenAPI schema drift 歸零，不可用 M0 hook 冒充
3. **正式機驗收**：M-1、五服務部署、NSSM＋BootTrigger reboot 與可查詢 OTLP trace
4. **前端**：FE-1（型別 ＋ mock）、FE-2（Soft Seoul 元件庫）、FE-6（後台殼）可平行

## 待決策

**沒有了。** 唯一那題（歷史會員與訂單是否遷移）於 2026-08-28 定案：
**不遷移**，客人重新註冊，Google 帳號串接排在 M1a 之後降低摩擦（ADR-019）。
理由是匯出要年繳才 NT$1,399、非年繳 NT$2,599，為了搬資料綁一整年租約不划算。

連帶解鎖：`RegisterRequest` 的「欄位暫定」已拿掉，`Identity` 的 schema 可以直接設計。

**儲值金沒有遺留問題**（2026-08-28 老闆確認）：舊平台從來沒啟用過儲值金，
餘額全是 0。新系統的儲值金照做，但**期初一律從 0 開始**，沒有要遷的負債、
也沒有要對的帳。不要因為「代購生意通常有儲值金」就自己假設要做遷移。

剩下唯一的雜事：去查舊平台的租約到期日，填進上線公告與註冊頁的
「舊訂單請到原平台查詢，期限到 ____」。

## 相關資源

- [後端藍圖 v1.0](https://claude.ai/code/artifact/9a61eb42-df39-418e-9789-38b8ffa8a6f2)（23 張圖，本 repo 的規格來源）
- [前台五套風格樣板](https://claude.ai/code/artifact/334496ce-48ac-4fd9-805f-19f64f66ac1b)（已選 Soft Seoul）
