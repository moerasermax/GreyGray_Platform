# 第四十二波派工書 —— BE-59　#60 綠界 ATM／超商代碼回呼查證（後端，查證包）

**給一個子代理。BE-59：查清楚客人在綠界選 ATM 或超商代碼時，我們的系統實際會發生什麼；用 characterization test 把現行行為釘住，交一份有證據的報告與修法建議。不修 `src/`。**

- 計畫：`docs/58-第四十二波計畫書.md` 的 BE-59 一節與「新發現的既有風險 #60」。文件編號兩棵樹共用，本文件是 61。
- 查證包，產出本身就是證據，不送外部覆驗；派工前已由 Leader 開的唯讀查證代理逐行核對，結論已併入。結論會決定第四十三波 FE-55 開不開、開多大。
- **BE-58 撤包之後才派**（它會建置並執行同一個測試專案，也改到 Payment 間接參考的 Catalog.Contracts）。你開工時這棵樹沒有其他包在動。
- **effort 用 high 的理由**：這條路牽涉客人的錢（收了款但訂單不變已付款），而且綠界協定與我們程式的互動有多處歧義，一次 medium 很可能漏。
- 派工書以 `.dispatch/ACTIVE.md` 的 `doc:` 為準；`CLAUDE.md`／`AGENTS.md` 提到的 `docs/13` 已過期。

---

## 0. 事實（2026-09-30 唯讀查證）

**行號以後端 commit `7096a27` 為準**。BE-58 之後改過 `src/Hosts/GreyGray.Api.Storefront/M1aEndpoints.cs`，現在的行號已位移——引用那個檔一律用 `git show 7096a27:src/Hosts/GreyGray.Api.Storefront/M1aEndpoints.cs` 取行號。

### 0.1 送給綠界的東西

- `src/Modules/Payment/GreyGray.Modules.Payment.Infra/EcpayGateway.cs:28`～44 的 AioCheckOut 只有 12 個欄位：`MerchantID`、`MerchantTradeNo`、`MerchantTradeDate`、`PaymentType=aio`、`TotalAmount`、`TradeDesc`、`ItemName`、
  `ReturnURL`（第 38 行）、`ClientBackURL`（第 41 行）、**`ChoosePayment = "ALL"`（第 42 行）**、`EncryptType`、`CheckMacValue`。
  **沒有** `PaymentInfoURL`、`OrderResultURL`、`ClientRedirectURL`、`ExpireDate`、`StoreExpireDate`、`IgnorePayment`。
- ADR-029（`docs/00-decisions.md`）明寫不做 `OrderResultURL`。**沒有任何 ADR 提到 ATM、超商代碼、`PaymentInfoURL` 或付款期限。**
- 設定預設：`InitiationLifetimeMinutes` 30、`CallbackMaxAgeMinutes` 20、`AllowSimulatedPaid` false（`src/Modules/Payment/GreyGray.Modules.Payment.Infra/ModuleRegistration.cs:143`～145；`ops/` 與 Host 都沒有覆寫）。
- 正式站目前接的是綠界**測試商店** `3002607`（`docs/14-環境整備runbook.md` 第 440-442 行；Leader 09-19、09-26 的正式機紀錄確認仍是測試站），沒有真實金流。

### 0.2 收到通知之後

**webhook 端點**（Storefront Host，inline lambda；`7096a27` 版第 1058-1113 行）：
- 先算整份表單的 hash、用 `MerchantTradeNo` 當冪等鍵（scope `webhook:ecpay`）呼叫 `TryBeginAsync`（第 1072-1079 行），**之後**才進服務層驗簽（第 1098 行）。
- **同鍵不同 hash 是永久擋下**，不是 24 小時：`src/Platform/Idempotency/IdempotencyStore.cs:81`～83 只有「hash 相同且（已 ABANDONED 或已過期）」才能重新認領，不同 hash 一律回 `KeyReusedWithDifferentPayload`（第 132-134 行）→ 端點回 `payment.callback-payload-mismatch`；
  `AbandonAsync`（第 207-214 行）只改狀態、保留資料列；全 repo 沒有任何清除 `idempotency_key` 的工作。
- 推論（**要你證實或推翻**）：因為驗簽排在冪等之後，**任何人只要拿到某筆訂單的 `MerchantTradeNo`（它就在客人瀏覽器送往綠界的表單裡），先送一次偽造的 POST，那筆訂單之後綠界真正的付款通知就永遠進不來**。

**服務層** `src/Modules/Payment/GreyGray.Modules.Payment.Core/PaymentApplicationService.cs:150`～246，依序：
驗簽 → 必要欄位（`MerchantID` 必須等於設定值）→ 用 `MerchantTradeNo` 找 Payment → 冪等（已 Captured／退款且 TradeNo 相同就回成功）
→ **`SimulatePaid=1` 且未開旗標就回 `payment.simulated-callback-rejected`（第 188-193 行，排在金額、時間窗、收款之前）**
→ 金額 → **時間窗**（`PaymentDate` 有值用它，否則 `TradeDate`，**以台北牆上時間換算**；差超過 20 分回 `payment.stale-callback`，第 201-205 行）
→ **第 208 行 `if (rtnCode == 1)` 才收款**；**其他任何 RtnCode（含 2、10100073）一律 `payment.Fail`，發 `PaymentFailed`**（第 228-241 行）。

- `Payment` 狀態只有 `Pending/Captured/Failed/Refunded/PartiallyRefunded`（`src/Modules/Payment/GreyGray.Modules.Payment.Contracts/PaymentContracts.cs:44`～51），**沒有「已取號、待繳費」**。
- **`Payment.Capture` 在狀態不是 Pending 時丟 `InvalidOperationException`**（`src/Modules/Payment/GreyGray.Modules.Payment.Core/Payment.cs:169`～172）。
- **`Payment.Fail`**：Pending 才轉 Failed；**已 Failed 又收到不同 TradeNo 會丟例外**（第 189-194 行）——`Expire` 不設 `ProviderTransactionId`，所以**被 Expire 的舊單號再收到任何 RtnCode≠1 的通知也會丟例外**。
- **`Expire`**（第 209-223 行）只在 `InitiateAsync` 第 111-117 行被呼叫：舊 Pending 過期（30 分）才轉 Failed，**不發事件**，之後開新一筆、新 `MerchantTradeNo`（第 109-132 行）。
  30 分鐘內重新付款則**沿用同一筆 Payment 與同一個 `MerchantTradeNo`**（第 94-108 行）。
- **收款路徑完全不看 `ExpiresAt`**：過期但沒人重新付款的 Pending，收到時間窗內的 RtnCode=1 仍會 Captured。
- `PaymentFailed` 的消費端：Ordering 只寫 `LastPaymentFailureCode`，**不取消訂單**，訂單停在 `AwaitingPayment`；這個欄位不在 `OrderView`，前台看不到。

### 0.3 付款期限

- `Order.PaymentDueAt`（`src/Modules/Ordering/GreyGray.Modules.Ordering.Core/Order.cs:139`）**全 `src` 沒有任何一處賦值**。
- 「逾期未付自動取消」只存在於設計：`docs/02-事件與狀態機.md` 第 88、143 行，以及 `ISagaTimerScheduler.cs` 第 11 行的註解；Ordering 唯一實作的 saga 是鑑賞期。**沒有任何「刻意保留」的紀錄。**
- 前台付款結果頁（前端 worktree `(checkout)/payment/result/page.tsx` 第 179-205 行）：輪詢約 19 秒後顯示「尚未確認付款」＋「重新前往付款」；付款期限那句依賴 `paymentDueAt`，**永遠不會出現**。

### 0.4 開發機模擬器與測試落點

- `src/Tools/GreyGray.Tools.EcpaySimulator.Core/EcpaySimulatorCore.cs`：只會回 `RtnCode` 1 或 `10100251`、`PaymentType` 寫死 `Credit_CreditCard`、只打 `ReturnURL`、**刻意不送 `SimulatePaid=1`**——不支援 ATM／超商。
- 測試落點 `tests/GreyGray.M1a.PaymentLedger.Tests/`：`EcpaySimulatorTests.cs` 用 in-memory 替身把真的 `EcpayGateway`＋`PaymentApplicationService` 組起來，**但所有 helper 與替身都是 `private`**（`CreateService`、`InitiateAsync`、`BuildNotification`、`StubRepository`、`StubClock`…），
  而且 **`StubClock` 的時間是固定的、不能推**，`InitiateAsync` helper 每次用新的 `OrderId`。`EcpayGateway.ComputeCheckMacValue` 是 `internal static`，這個專案有 `InternalsVisibleTo`。
- 這個專案**另有 Testcontainers 測試**（例如會套全部 migration 的帳務測試）：**跑完整測試執行檔需要 Docker Desktop 開著**；跑完 dev 的 Postgres 容器有時會消失（不用你處理，記一句即可）。
- 專案沒有參考 Ordering；webhook 端點是 inline lambda，repo 沒有完整路由管線的測試基礎——**端點冪等那一段用讀程式碼＋引用 `7096a27` 行號說明即可，不寫測試**。

---

## 1. 要回答的問題（報告的「查證結論」一節，一題一小節）

1. **綠界協定**（以綠界官方文件為準，**每一條附官方網址**；查不到或無法上網就標「尚待確認」並列出要去哪一頁確認）：
   - 測試商店 `3002607` 在 `ChoosePayment=ALL` 下，付款頁實際列出哪些付款方式。
   - ATM、超商代碼、超商條碼的**取號結果**打到哪個網址（`PaymentInfoURL`？沒設時呢？）、帶什麼 `RtnCode`、哪些欄位（`BankCode`、`vAccount`、`PaymentNo`、`ExpireDate`…）。
   - **繳費完成**時打哪個網址、`RtnCode`、`PaymentDate` 與實際通知時間可能相差多久。
   - **測試商店的 ATM／超商「繳費完成」是不是只能從廠商後台「模擬付款」觸發、通知是否一律帶 `SimulatePaid=1`**——若是，現行程式在測試商店上根本走不到收款。
   - 預設繳費期限（ATM、超商各幾天），`ExpireDate`／`StoreExpireDate` 能不能調。
   - 同一個 `MerchantTradeNo` 重送 AioCheckOut 時綠界的行為。
2. **現行程式在每一種通知下的實際行為**——用 §2 的測試釘住，報告逐列寫「通知 → Payment 狀態 → 事件 → 訂單狀態 → 前台看到什麼」。
3. **下面四條推論，逐條證實或推翻**（Leader 與查證代理追出來的，**都還沒實測**）：
   - (a) 取號通知若打到 `ReturnURL`（RtnCode≠1）→ Payment 變 Failed；之後同一個 `MerchantTradeNo` 的繳費通知**第一道就被端點冪等永久擋下**（同鍵不同內容）；只有在取號通知根本沒進到端點時，才會走到 `Capture` 丟例外。結論都是**錢收了、訂單永遠不會變已付款**。
   - (b) 取號後超過 30 分鐘才按「重新前往付款」→ 舊 Payment 被 `Expire`、開新單號；客人照**舊**帳號／代碼繳費 → 舊單號的 RtnCode=1 在 `Capture` 丟例外（RtnCode≠1 則在 `Fail` 丟例外）。
   - (c) 綠界 ATM 入帳通知若比 `PaymentDate` 晚超過 20 分鐘會被判 stale，重送只會越來越晚。
   - (d) **偽造 POST 先佔用冪等鍵，就能讓一筆訂單的真正付款通知永遠進不來**（§0.2）。這一條是安全問題，即使 A 方案（只收信用卡）也要處理，請評估嚴重度與修法方向（例如驗簽移到冪等之前）。
4. **`PaymentDueAt` 從未賦值**：刻意保留還是漏做？依 §0.3 下結論，並說明「不修它」對 ATM／超商的影響。
5. **修法選項與建議**：
   - A：`ChoosePayment=Credit`（只收信用卡）——改哪一行、對前台文案與條款的影響、之後能不能再開。
   - B：加 `PaymentInfoURL`、新增「已取號待繳費」狀態、把繳費資訊與期限帶到前台——要改哪些模組、契約、模擬器，**取號通知與繳費通知的冪等鍵／scope 要怎麼設計才不互相擋**，要不要補逾期 saga，粗估包數。
   - C：你認為更好的做法。
   - 最後給**一個建議**，理由寫成「開張前的風險 × 實作成本」；推論 (d) 的處理另列。

---

## 2. 要寫的 characterization test（`tests/GreyGray.M1a.PaymentLedger.Tests/` 新檔）

**目的是把現況釘住，不是證明它對**：測試要**綠**（描述現在真實發生的事），名稱與註解寫明「現況」，將來修法時會有人刻意翻轉。

**組法（照做，否則結論會被測試組錯污染）**：
- 既有 helper 都是 private：**把需要的替身複製到你的新檔**（不要改既有檔）。
- 自寫一個**可設定時間**的時鐘（例如 `UtcNow { get; set; }`），**整個情境只建一個 service、共用同一個 repository**；同一情境的 `OrderId` 固定用同一個值。
- 通知欄位：`MerchantID` 用 harness 的設定值（不是 3002607）、`SimulatePaid=0`（C9 除外）、`PaymentDate`／`TradeDate` 一律用**當下時鐘換算的台北牆上時間**（照 `PaymentApplicationService` 第 327-329 行的換算）。
- **先寫 C0 正向對照**；C1～C9 另外斷言「沒有被驗簽、必要欄位、模擬旗標、金額、時間窗任何一道擋下」（除非那一列本來就在測那一道）。

| # | 前置 | 操作 | 預期（現況） | 怎麼驗 |
|---|---|---|---|---|
| C0 | 剛建立的 Pending | 同一個組裝器送 `RtnCode=1` | Captured、發 `PaymentCaptured` | 斷言狀態與事件（證明組法正確） |
| C1 | Pending | 送 `RtnCode=2`、`PaymentType=ATM_*`、ATM 取號欄位 | Failed、發 `PaymentFailed`、`FailureCode` 為 `2` | 斷言狀態、事件、欄位 |
| C2 | Pending | 送 `RtnCode=10100073`、`PaymentType=CVS_*` | 同 C1 | 同上 |
| C3 | C1 之後 | 同一單號送 `RtnCode=1` | `Capture` 丟 `InvalidOperationException`（服務層冒出例外，不是 `Result`） | 斷言例外型別與訊息片段 |
| C4 | Pending | 時鐘推 31 分鐘 → 同一個 `OrderId` 再 `InitiateAsync` | 舊 Payment Failed、**沒有發事件**；新一筆、新單號 | 斷言兩筆狀態、單號不同、事件數 |
| C5 | C4 之後 | 舊單號送 `RtnCode=1`（`PaymentDate` 用推過的時鐘） | `Capture` 丟例外。**若回 `payment.stale-callback` 是組法錯了，不算推翻 (b)** | 斷言例外 |
| C6 | Pending | `RtnCode=1`，`PaymentDate` 比時鐘早 19 分鐘；另一情境早 21 分鐘 | 19 分收款成功；21 分回 `payment.stale-callback` 且仍 Pending | 兩個情境各一條 |
| C7 | C4 之後 | 舊單號送 `RtnCode=2` | `Fail` 丟例外（不同 TradeNo） | 斷言例外 |
| C8 | Pending，時鐘推 3 天，**不**呼叫 `InitiateAsync` | 送 `RtnCode=1`（時間窗內） | 仍 Captured（收款不看 `ExpiresAt`） | 斷言狀態 |
| C9 | Pending | `RtnCode=1`、`SimulatePaid=1` | 回 `payment.simulated-callback-rejected`，狀態不變 | 斷言 `Result.Error.Code` |
| C10 | Pending | 時鐘推 10 分鐘 → 同一個 `OrderId` 再 `InitiateAsync` | 沿用同一筆 Payment、同一個單號 | 斷言 |

- ATM／超商欄位名稱與值，**以你在 §1 查到的官方文件為準**；查不到就用最小欄位集，報告標明「欄位形狀未經官方確認」。
- 端點冪等（推論 (a) 第一道、(d)）**不寫測試**：引用 `7096a27` 行號說明。

`docs/45` 邊界六類：**重複與併發**＝C3／C5／C7／C10（同一單號的第二則通知、重新付款）；**事件重放**＝C1→C3；**非法狀態轉移**＝C3、C7；**上下限**＝C6（時間窗兩側）、C4 對 C10（30 分界線兩側）；**權限**不適用（沒有新端點）；**部分失敗**＝C4 的 Expire 不發事件。

---

## 3. 你的 `allow`（`.dispatch/ACTIVE.md` 生效中的那份為準）

```
tests/GreyGray.M1a.PaymentLedger.Tests/
.dispatch/reports/BE-59.md
```

- **只新增檔案**；既有測試檔不改。**不准改 `src/`、`docs/`、`ops/`、模擬器、任何其他測試專案。** 覺得非改不可就停下來回報。
- 報告就是交付物：`.dispatch/reports/BE-59.md`。

---

## 4. 驗證與報告

1. **本包以 Release 為準**（`.dispatch/reports/README.md` 的 `-c Debug` 不適用）。**Docker Desktop 要開著**，沒開就停下來回報。
   動手前 Release 建置 solution 並跑一次 `tests\GreyGray.M1a.PaymentLedger.Tests\bin\Release\net10.0\GreyGray.M1a.PaymentLedger.Tests.exe` 記下現況條數（BE-58 撤包時是 66 條、1 skipped）；改完再跑，寫「前 → 後」。
   **前景跑完**，不要用 `dotnet test`、不要掛背景就結束。
2. 報告三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」，另加「## 查證結論」一節（§1 五題，每條標 **已觀察（附官方網址或程式行號）／推論／尚待確認**）。
   「逐條自驗」以本文件 §1 與 §2 的 C0～C10 為準（不是 README 說的 §5）。
3. 你不能宣告通過；驗收是 Leader 的事。

### 停下來回報的情況

- §0 描述的**程式結構**（檔案、方法、呼叫順序、欄位）和你讀到的對不上（附檔案與行號）
- 要改 allow 以外的任何檔，或必須改既有測試檔
- Docker 沒開、或建置失敗且原因不在你的檔

**不算停工的情況**：§2「預期（現況）」欄描述的**執行結果**和實際不同（例如 C3 沒有丟例外），而且 C0 正向對照是綠的。這時照實把**真正的現況**釘住、測試保持綠，並在報告寫明「這條推論不成立」——這本身就是查證結果。
