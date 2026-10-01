# 第四十五波派工書 —— BE-68　期限、逾期與取消來源接到 Host（後端）

**給一個子代理。BE-68：把 BE-64 在 Ordering 做好的期限與取消來源交給 API——storefront 訂單回應帶 `paymentOverdue`、`cancelledAt`、`cancellationSource`，付款端點在繳費期限已過時回 `422 ordering.payment-overdue`；後台訂單回應帶 `paymentDueAt`、`cancellationSource`。**

- 計畫：`docs/72-第四十五波計畫書.md`；決定：`docs/00-decisions.md` 的 **ADR-044**（含 2026-10-01 補記：寬限期間顯示確認中、不能再付款；付款端點**先檢查逾期、再檢查已取號**）。
- **契約 Leader 已經寫好**（`docs/api/openapi.storefront.yaml`：`Order.paymentOverdue`／`cancelledAt`／`cancellationSource`、`OrderCancellationSource`、付款端點 `422 ordering.payment-overdue`；`openapi.admin.yaml`：`AdminOrder.paymentDueAt`／`cancellationSource`）。前端 FE-58、FE-61 已照契約用 mock 做好。**你不准改 `docs/` 底下任何檔。**
- **只動兩支 Host 檔與測試**。不准改 `src/Modules/`（Ordering 的欄位 BE-64 已加好）、`src/Platform/`。
- 派工書以 `.dispatch/ACTIVE.md` 的 `doc:` 為準。
- **前提**：本包在 **BE-64 驗收、commit、撤包之後**才派；§0 的「HEAD」指那個 commit（BE-64 的 `OrderView` 新欄位已在其中）。

---

## 0. 事實（後端 HEAD 為準；對不上就停下來回報）

### 0.1 BE-64 交給本包的欄位

- `src/Modules/Ordering/GreyGray.Modules.Ordering.Contracts/OrderingContracts.cs`：新列舉 `OrderCancellationSource { Customer = 1, Staff = 2, PaymentExpired = 3 }`；`OrderView` 新增 init 屬性 `CancelledAt`（`DateTimeOffset?`）、`CancellationSource`（`OrderCancellationSource?`）；`PaymentDueAt` 原本就有。名稱與契約一致；`GreyGrayJson` 用 `JsonStringEnumConverter`，序列化成 `"Customer"` 等字串。
- 期限語意（BE-64）：建單 `PaymentDueAt`＝`PaymentAutoCancelAt`＝建單＋24 小時；**已取號的** ATM／超商代碼／條碼 `PaymentDueAt`＝綠界期限、`PaymentAutoCancelAt`＝綠界期限＋2 天。所以「待付款且期限已過」＝已取號訂單的 2 天寬限；**未取號的訂單（含信用卡）**則是計時器觸發前的空窗——通常幾秒，但 `0025` 回填的舊單部署後至少 1 小時、計時器佇列卡住時更久。這些情況回 `paymentOverdue: true`／422 都是對的。
- 舊資料：`cancelled_at` 欄位從 `0006` 就有、舊的取消流程一直會寫，所以**第四十五波之前取消的舊訂單是 `CancelledAt` 有值、`CancellationSource` 為 `null`**。
- Payment 把綠界期限當成「最後一個有效秒」——`now >= ProviderExpiresAt + 1 秒`才算過期（`src/Modules/Payment/GreyGray.Modules.Payment.Core/PaymentApplicationService.cs` 約第 499～503 行）。

### 0.2 Storefront `src/Hosts/GreyGray.Api.Storefront/M1aEndpoints.cs`

- 訂單詳情（BE-63 抽成 internal static，約第 950～1001 行）：只在 `AwaitingPayment` 時查 `IPaymentInstructionsQuery`，最後 `Results.Ok(response with { PaymentInstructions = instructions })`。
- 付款端點約第 1052～1110 行，**寫在 private `MapPayment` 裡的 inline lambda、沒有 internal static 入口**（測試叫不到）：`ExecuteIdempotentAsync` 的 work 先 `ordering.GetCustomerAsync`，`Cancelled` 回 `ordering.order-cancelled`，再 `payments.InitiateAsync`（已取號的 409 由 Payment 回）；第 1076 行 `BuildPaymentResultUrl` 在進冪等之前就呼叫，缺 `Storefront:PublicOrigin` 會丟例外。抽出 internal static 的前例：同檔約第 744 行 `CompleteCheckoutAsync`（route 在約第 706～730 行用 lambda 包著呼叫、work closure 捕捉 `clock`）。**BE-63 的坑**（`.dispatch/reports/BE-63.md` 第 65 行）：route 改成 method group 時非泛型 `ILogger` 會被推斷成 body——**route 一律維持 lambda 包法**。
- `ToOrderAsync`（約第 1620 行）被結帳 render、取消 render 與訂單詳情共用（約第 848、999、1047 行）；`OrderResponse`（約第 1986～2005 行）最後一個欄位是 `PaymentInstructions = null`。`tests/GreyGray.M1a.CheckoutOrdering.Tests/PaymentInstructionsHostTests.cs` 第 113～124 行（O3）**用反射鎖死 `ToOrderAsync` 的參數型別**——不准改它的簽章。
- 時間一律經 `IClock`（鐵則）。

### 0.3 Admin `src/Hosts/GreyGray.Api.Admin/M1aEndpoints.cs`

- `internal static ToAdminOrderAsync`（約第 1060 行）組 `AdminOrderResponse`（約第 1358～1380 行，最後一個欄位 `CustomerContactMasked`）。呼叫點：同檔第 168、272、329 行、`src/Hosts/GreyGray.Api.Admin/M1bShortfallRefundEndpoints.cs` 第 103 行（**不在 allow**——所以簽章不准改）、測試 `CvsLogisticsEndpointTests.cs`、`PaymentInstructionsHostTests.cs`、`OrderRecipientSnapshotTests.cs`。

### 0.4 測試

- `tests/GreyGray.M1a.CheckoutOrdering.Tests/PaymentInstructionsHostTests.cs`（BE-63）有訂單詳情與後台摘要的 Host 測試寫法可沿用；它的第 25 行固定 `Now = 2026-10-01T04:00Z`、第 248 行 `PaymentDueAt = Now.AddDays(1)`，**第 91、201 行呼叫 `GetCustomerOrderAsync`**。假時鐘：`tests/GreyGray.M1a.CheckoutOrdering.Tests/TestDoubles.cs` 第 197 行 `FakeClock`；冪等替身：同檔的 `InspectableIdempotencyStore`。目前付款端點沒有任何單元測試。

---

## 1. 要做的事

1. **storefront 訂單回應**：`OrderResponse` 最後加 `PaymentOverdue`（`bool`，預設 `false`）、`CancelledAt`、`CancellationSource`（都有預設值 `null`）。`CancelledAt`、`CancellationSource` 在 `ToOrderAsync` 從 `OrderView` **照原樣**帶（三條路徑都會有；不准依來源是否為 `null` 去改 `CancelledAt`）；`ToOrderAsync` 的簽章不改。
   **共用判斷**：同檔新增 `private static bool IsPaymentOverdue(OrderView order, DateTimeOffset now)` ＝ `order.Status == OrderStatus.AwaitingPayment && order.PaymentDueAt is { } due && now >= due.AddSeconds(1)`（與 Payment 的「最後一個有效秒」一致）。訂單詳情與付款端點**都只准用這一個函式**。
2. **逾期判斷（只在訂單詳情算）**：`GetCustomerOrderAsync` **加一個 `IClock` 參數**（允許的簽章變更；route lambda 一起補）；`IsPaymentOverdue(view, clock.UtcNow)` → `PaymentOverdue = true`，**而且不查取號資訊、`paymentInstructions` 為 `null`**（契約：逾期時是 `null`）。其他情況 `false`、取號資訊照 BE-63 的規則。結帳與取消回應的 `PaymentOverdue` 一律 `false`。
   `PaymentInstructionsHostTests.cs` 第 91、201 行兩個呼叫點**只准補 `new FakeClock(Now)`**，O1～O5 的斷言一字不改；任何 Host 測試都不准用真實時鐘（O 系列的期限是 2026-10-02T04:00Z，用真時鐘的話明天中午之後就會轉紅）。
3. **付款端點**：比照 `CompleteCheckoutAsync`，把付款處理**原樣抽成** `internal static Task<IResult> InitiateCustomerPaymentAsync(…, IClock clock, …)`；route **維持 lambda 包法**（參數綁定 `IClock` 後 await 呼叫，不准改成 method group），work closure 捕捉 `clock`。在「已取消」檢查之後、呼叫 `payments.InitiateAsync` 之前，`IsPaymentOverdue` → `Result<PaymentInitiation>.Failure("ordering.payment-overdue", "繳費期限已過，正在等待確認付款，不能再付款。")`（經 `StatusFor` 落到 422）。**順序：已取消（409）→ 逾期（422）→ 才交給 Payment（已取號 409）**。已付款的訂單即使期限已過也**不是**逾期（`IsPaymentOverdue` 只看待付款）。
4. **後台訂單回應**：`AdminOrderResponse` 最後加 `PaymentDueAt`、`CancellationSource`（預設 `null`），在 `ToAdminOrderAsync` 從 `OrderView` 帶。
5. 不改 `docs/`、`src/Modules/`、`src/Platform/`。

---

## 2. 你的 `allow`（`.dispatch/ACTIVE.md` 生效中的那份為準）

```
src/Hosts/GreyGray.Api.Storefront/M1aEndpoints.cs
src/Hosts/GreyGray.Api.Admin/M1aEndpoints.cs
tests/GreyGray.M1a.CheckoutOrdering.Tests/
.dispatch/reports/BE-68.md
```

`tests/GreyGray.M1a.CheckoutOrdering.Tests/` 只准為本包新增或修改測試；不准改 `ModuleShapeTests.cs`。

---

## 3. 驗證要求

| # | 內容 | 預期 | 怎麼驗 |
|---|---|---|---|
| H1 | 逾期邊界 | 待付款＋`now == PaymentDueAt` → `false`；`now == PaymentDueAt + 1 秒` → `true`；`PaymentDueAt` 為 `null` → `false`；非待付款（已付款、已取消）且期限已過 → `false`（用 `FakeClock`） | 單元測試 |
| H2 | 逾期時的取號資訊 | 逾期 → `paymentInstructions: null` 且**沒有呼叫**取號查詢；未逾期 → BE-63 行為不變（既有 O1～O5 綠） | 單元測試 |
| H3 | 取消資訊 | 已取消訂單的回應帶 `cancelledAt`、`cancellationSource`（三種來源都測）；**未取消 → 兩者 `null`；第四十五波之前取消的舊訂單 → `cancelledAt` 有值、`cancellationSource` 為 `null`**；結帳與取消回應也帶（取消回應的來源是 `Customer`）；**寬限中（待付款＋期限已過）自助取消仍回 200、來源 `Customer`**，取消端點的程式碼不得出現逾期判斷 | 單元測試 |
| H4 | JSON 形狀 | 序列化後欄位名 `paymentOverdue`、`cancelledAt`、`cancellationSource`（值為 `"Customer"`／`"Staff"`／`"PaymentExpired"`）；後台 `paymentDueAt`、`cancellationSource` | JSON 序列化斷言 |
| H5 | 付款端點 | 逾期 → `422 ordering.payment-overdue`、`payments.InitiateAsync` **沒被呼叫**（新寫一個記錄呼叫次數的 `IPaymentCommand` 替身）、冪等鍵被 Abandon（`InspectableIdempotencyStore`）；已取消仍是 409 且優先於逾期；**已付款（`PaidAwaitingClose`／`ReadyToShip`）且期限已過 → 不回 422、照舊呼叫 Payment 一次**；未逾期照舊呼叫 Payment；同一把 key 在 422 之後重送仍是 422、`InitiateAsync` 仍沒被呼叫。測試用 in-memory 設定提供 `Storefront:PublicOrigin` | 單元測試 |
| H6 | 後台 | `ToAdminOrderAsync` 帶出 `PaymentDueAt` 與 `CancellationSource` | 單元測試 |
| B1 | 建置與全套測試 | Release 建置 0 錯誤；`tests` 底下 **13 個測試執行檔**全數 0 失敗，報告「前 → 後」條數；`ops/check-openapi.ps1` 全綠 | 指令輸出 |

`docs/45` 邊界六類：上下限（剛好到期與 +1 秒）；非法狀態（逾期又付款、已取消又付款、已付款且期限已過的優先順序）；重複（逾期回 422 的同一把 key 重送；**期限前已成功的 key 在期限後重送會回快取的 200——既有冪等語意、不在本包範圍，由「取消後才入帳」兜底，寫進「我發現但沒做的事」**）；部分失敗（計時器空窗：信用卡與 0025 回填舊單在計時器觸發前也會是逾期）；權限、事件重放說明為何不適用。

**以 Release 為準**；Docker Desktop 要開著，沒開就停下來回報。動手前先 Release 建置並跑 13 個測試執行檔記下現況，改完**前景**跑完（不要用 `dotnet test`、不要掛背景就結束）。dev 環境由 Leader 管，不准啟停。

---

## 4. 報告

`.dispatch/reports/BE-68.md`，三個標頭一字不差：`## 指令與輸出`、`## 逐條自驗`、`## 我發現但沒做的事`。逐條自驗以本文件 §1、§3 為準。你不能宣告通過。

---

## 5. 停下來回報的情況

- §0 的事實和你讀到的檔案對不上（特別是 `OrderView` 沒有 `CancelledAt`／`CancellationSource`）
- 要動 allow 以外的檔（特別是 `src/Modules/`、`src/Platform/`）
- 需要改 `ToOrderAsync` 或 `ToAdminOrderAsync` 的簽章才能完成
- 需要改 O1～O5 的斷言才能變綠
