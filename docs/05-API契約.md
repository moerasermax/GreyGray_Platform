# API 契約

**這份文件是前端與後端之間唯一的邊界。** 兩邊各自照它做，不看對方的程式碼。

> **凍結狀態**：v1.0 凍結於 2026-08-28。
> 任何一方要改，**回來改這份文件並說明理由**，不得單方面改實作。
> 改了之後 `docs/api/*.yaml` 與本文件的端點索引要同步更新。

機器可讀的版本在 `docs/api/openapi.storefront.yaml` 與 `docs/api/openapi.admin.yaml`。
本文件講的是**兩份 YAML 都適用的通則**，以及 YAML 表達不出來的約束。

### 凍結之下，工具可以改什麼

兩份 YAML 同時是兩件事：前端 codegen（`pnpm api:generate`）的輸入，
以及後端 CI（`ops/check-openapi.ps1`）拿來斷言 `AddOpenApi()` 實際產物的目標。
所以它們偶爾需要為了「能被嚴格的 parser 讀」而調整寫法。規則是：

- ✅ **允許正規化格式，不得改變語意。** 例如把 flow context 裡含 `1:1` 的
  plain scalar 加上引號——YAML 1.2 在 flow context 對 `:` 的處理本來就模糊，
  嚴格 parser 會失敗。這種改動的判準是：**改完重跑 `pnpm api:generate`，
  產出的 TS 型別必須逐字節相同。**
- ❌ **不得改變任何語意**：欄位增刪、型別變更、required 變動、enum 成員、
  路徑、狀態碼——全部走「回來改本文件並說明理由」的流程。
- 每一次格式正規化都要在 PR 說明改了什麼、為什麼，以及型別無變化的證據。

> 2026-08-28 已發生一次：`OpenApiContractGate` 為了能 parse，把 storefront YAML
> 兩處 `description` 加了引號。語意零變化、型別重產後逐字節相同，事後補認可。
> 記在這裡是因為下一次未必這麼無害。

---

## 0. 兩個 BFF、兩個前端

| 行程 | port | 對象 | 前面擋著 |
|---|---|---|---|
| `GreyGray.Api.Storefront` | 5000 | 客人（Web / LINE LIFF） | Cloudflare WAF ＋ Turnstile ＋ Rate Limit |
| `GreyGray.Api.Admin` | 5001 | 團隊成員 | Cloudflare Access（Zero Trust） |
| `apps/storefront`（Next.js） | 5002 | — | 同上，經 cloudflared |
| `apps/admin`（Next.js） | 5003 | — | 同上 |

**BFF 只做組裝與授權，不含業務邏輯。** 一個端點可以呼叫多個模組的 Contracts 介面把資料拼起來——
那正是「禁止跨 schema JOIN」之後資料該在哪裡會合的答案。

---

## 1. 版本

路徑前綴 `/v1`。破壞性變更開 `/v2` 並讓兩版並存一段時間，不原地改。

**什麼算破壞性**：刪欄位、改欄位型別、改欄位語意、新增必填欄位、改變既有 enum 值的意義。
**什麼不算**：新增可選欄位、新增 enum 成員（前端必須容忍未知 enum 值，見 §6）。

---

## 2. 認證

**access token 永遠不進瀏覽器。** 前端只拿到 cookie，BFF 自己保管 session。

```
Set-Cookie: gg_session=<opaque>; HttpOnly; Secure; SameSite=Lax; Path=/; Max-Age=2592000
```

- session 內容存 Valkey，cookie 裡只有不透明的 id。
- 前端**永遠不需要**、也拿不到 token。任何要求前端存 token 的設計都是錯的。
- 所有需要登入的端點，未登入回 `401`，前端導向登入頁。
- Storefront 與 Admin 的 cookie 名稱不同、網域不同、session 不共用。
- 跨站請求偽造：`SameSite=Lax` ＋ 所有寫入端點要求 `Origin` 符合允許清單。

**M1a 的登入方式**：手機號碼 ＋ 密碼。
LINE Login 綁定排 M1b（LIFF 內免登入是 M1b 才有的體驗）。

> **2026-08-28 定案（ADR-019）**：**不遷移**歷史會員與訂單。
> 因此 `POST /v1/auth/register` 的欄位**不再是暫定**——
> `phoneNumber` / `password` / `displayName` / `email` / `referralCode` 就是定案。
>
> Google 帳號串接排在 M1a 之後，屆時是**新增** `googleLinked` 這類綁定旗標
> （`Me` 已經有 `lineLinked` 的形狀），是加欄位不是改欄位，不算破壞性變更。
> 前端仍然建議把註冊表單的欄位集中在一處——不是因為會變，
> 而是之後要在同一個流程裡插入 OAuth 的入口。

---

## 3. 錯誤：RFC 9457 Problem Details

所有非 2xx 回應的 `Content-Type` 是 `application/problem+json`，形狀固定：

```json
{
  "type": "https://greygray.tw/errors/checkout.cart-empty",
  "title": "購物車是空的",
  "status": 422,
  "detail": "購物車沒有任何品項，無法結帳。",
  "instance": "/v1/cart/checkout",
  "code": "checkout.cart-empty",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "errors": { "quantity": ["數量必須大於 0。"] }
}
```

| 欄位 | 說明 |
|---|---|
| `code` | 對應 `GreyGray.Shared.Kernel.Error.Code`。**機器讀這個，不要讀 `title`。** |
| `title` | 繁體中文，**客服可以直接照念**。前端可以直接顯示。 |
| `detail` | 補充說明，可能為 null。 |
| `traceId` | W3C traceparent。客服拿這個就能查到整條鏈。 |
| `errors` | 只有欄位驗證失敗（422）才有。 |

`type` 永遠是 `https://greygray.tw/errors/{code}`。這個 URL 不保證可以打開——它是識別碼，不是文件連結。

### 狀態碼

| 狀態 | 用在 |
|---|---|
| `400` | 請求本身格式錯誤（JSON 壞掉、缺必要 header） |
| `401` | 未登入或 session 過期 |
| `403` | 已登入但沒有權限（Admin 的角色檢查） |
| `404` | 資源不存在，**或存在但不屬於這個使用者**（不洩漏存在性） |
| `409` | 狀態衝突（訂單已付款不能再付、`Idempotency-Key` 正在處理中） |
| `422` | 語意錯誤：欄位驗證失敗、業務規則不允許（團已截止、庫存不足） |
| `429` | 觸發速率限制，帶 `Retry-After` |
| `500` | 沒預期到的錯誤。**`title` 一律是「系統發生問題，請稍後再試」，不吐內部訊息。** |

**`Result` 與狀態碼的對應規則**：`Result.Failure` 是**可預期的業務失敗**，一律映射到
`404` / `409` / `422` 三者之一，**不會**是 `500`。丟到 `500` 的只有例外，而例外代表
「不該發生」——那是 bug，不是業務結果。

### 錯誤碼命名

`{模組}.{kebab-case 描述}`，例如 `ordering.order-already-paid`、`campaign.closed`、
`inventory.insufficient-stock`。**一經上線不可改名**，跟事件型別名同一個規矩。

完整清單在 `docs/api/openapi.*.yaml` 每個端點的 responses 裡列舉。

---

## 4. 冪等

所有會改變狀態的請求（`POST` / `PUT` / `PATCH` / `DELETE`）**必須**帶：

```
Idempotency-Key: <opaque, 1..255 chars, 建議 UUID>
```

沒帶就是 `400`（`code: platform.idempotency-key-required`）。

前端規則：

- **同一個使用者動作用同一把 key**，重試時 key 不變——這正是冪等的意義。
  按鈕連點兩下、網路逾時後自動重送，用的都必須是同一把 key。
- 使用者重新發起一次新的動作（改了數量再送一次），要換一把新 key。
- key 建議在**表單載入時**產生一次，送出成功後才丟掉。

BFF 的四種結果（對應 `IIdempotencyStore.TryBeginAsync`）：

| 內部結果 | HTTP |
|---|---|
| `Proceed` | 正常往下做 |
| `AlreadyCompleted` | **回傳第一次的結果**（含當時的狀態碼），不重跑 |
| `InFlight` | `409`，`code: platform.request-in-flight`，前端稍後重試（同一把 key） |
| `KeyReusedWithDifferentPayload` | `422`，`code: platform.idempotency-key-reused` |

`GET` 永遠不需要這個 header。

**唯一的例外：`POST /v1/webhooks/ecpay`。** 綠界送的是它自己格式的表單，
不可能帶我們的 header。那個端點改用**綠界的 `MerchantTradeNo` ＋ 交易編號**當去重鍵，
一樣寫進 `platform.idempotency_key`，只是 key 由後端從 payload 推出來而不是由呼叫端給。
驗簽與時戳容忍窗照樣要做——三者缺一不可。

---

## 5. 分頁

一律**游標式**，不用 offset。理由：所有 ID 是 UUIDv7（時間可排序），
游標天然穩定；offset 在資料一直進來的清單上會漏資料或重複。

```
GET /v1/orders?cursor=0198c3d4e5f607189abc0123456789ab&limit=20
```

```json
{
  "items": [ ... ],
  "nextCursor": "0198c3d4e5f607189abc0123456789ab"
}
```

- `limit` 預設 20，上限 100。超過上限回 `422`。
- `nextCursor` 為 `null` 代表沒有下一頁。
- **不回傳總筆數。** 要總數的畫面請改設計——那是 Reporting 的事，不是列表端點的事。

---

## 6. 資料形狀

跟 outbox payload **用同一組規則**（ADR-018）。`GreyGray.Shared.Kernel.Json.GreyGrayJson.Options`
是後端唯一的來源。

| 型別 | 線上形狀 | TypeScript |
|---|---|---|
| property 名稱 | camelCase | — |
| 各種 `XxxId`（以及任何 GUID）| 32 字元十六進位字串，**無連字號** | `type CustomerId = string` |
| 金額 | `{"amountMinor": 18000, "currency": "TWD"}` | `interface Money { amountMinor: number; currency: Currency }` |
| enum | **字串**（`"ConvenienceStore"`，不是 `1`） | union of string literals |
| 時間點 | ISO 8601 含位移：`"2026-08-28T14:30:00+08:00"` | `string` |
| 日期 | `"2026-08-28"` | `string` |
| null | **照寫不省略** | `T \| null` |

### 金額

**前端永遠不做金額運算，只做顯示。** 加總、分攤、折扣一律由後端算好回傳。

`amountMinor` 是**最小單位**：TWD 的最小單位是分，`NT$180` = `18000`。
換算倍率（TWD/USD/EUR… = 100，JPY/KRW = 1）由 `packages/api-client` 的
`formatMoney()` 統一處理，**不要在元件裡自己除以 100**。

`long` 最小單位的值域遠在 IEEE-754 安全整數範圍內（±2⁵³），`JSON.parse` 不會失真。

### enum 的向前相容

**前端必須容忍未知的 enum 值。** 新增 enum 成員不算破壞性變更（§1），
所以 `switch` 一定要有 `default`，不要用會拋例外的窮舉。

未知值的顯示規則：顯示原始字串，不要顯示空白、不要當成錯誤。

---

## 7. 追蹤

- 請求帶 `traceparent`（W3C Trace Context）；沒帶的話 BFF 自己開一條。
- 回應一律帶回 `traceparent`。
- 前端在錯誤畫面上**顯示 `traceId` 的後 8 碼**，讓客人可以報給客服。

---

## 8. 端點索引

`M1a` = 這一輪要做。`M1b` / `M3` = 契約先定、實作延後，**前端可以先做 UI 但要能降級**。

### Storefront（:5000）

| 方法 | 路徑 | 里程碑 | 說明 |
|---|---|---|---|
| `POST` | `/v1/auth/register` | M1a | 註冊（欄位已定案，見 §2 與 ADR-019） |
| `POST` | `/v1/auth/login` | M1a | 手機號碼 ＋ 密碼 |
| `POST` | `/v1/auth/logout` | M1a | |
| `GET` | `/v1/me` | M1a | 目前登入的客戶 |
| `PATCH` | `/v1/me` | M1a | 改暱稱等非敏感欄位 |
| `GET` | `/v1/me/addresses` | M1a | |
| `POST` | `/v1/me/addresses` | M1a | |
| `PUT` | `/v1/me/addresses/{addressId}` | M1a | |
| `DELETE` | `/v1/me/addresses/{addressId}` | M1a | |
| `GET` | `/v1/me/stored-value` | M1a | 儲值金餘額（Ledger） |
| `GET` | `/v1/categories` | M1a | 橫捲圓形分類標的資料來源 |
| `GET` | `/v1/products` | M1a | 商品卡牆；支援 `categoryId` / `q` / `mode` |
| `GET` | `/v1/products/{productId}` | M1a | 含所有 SKU 與可用量 |
| `GET` | `/v1/campaigns` | M1a | 開團列表 |
| `GET` | `/v1/campaigns/{campaignId}` | M1a | 含商品清單與截團倒數 |
| `GET` | `/v1/cart` | M1a | |
| `POST` | `/v1/cart/lines` | M1a | 加入購物車 |
| `PATCH` | `/v1/cart/lines/{lineId}` | M1a | 改數量 |
| `DELETE` | `/v1/cart/lines/{lineId}` | M1a | |
| `POST` | `/v1/cart/quote` | M1a | 詢價。**純函式，不寫入任何東西** |
| `POST` | `/v1/cart/checkout` | M1a | 結帳 → 建單 |
| `GET` | `/v1/orders` | M1a | 我的訂單 |
| `GET` | `/v1/orders/{orderId}` | M1a | |
| `POST` | `/v1/orders/{orderId}/cancel` | M1a | 只有 `AwaitingPayment` 可自助取消 |
| `POST` | `/v1/orders/{orderId}/payment` | M1a | 取得綠界導轉參數 |
| `GET` | `/v1/orders/{orderId}/shipments` | M1b | 物流狀態 |
| `POST` | `/v1/inquiries/{inquiryId}/reply` | M1b | LINE postback 打進來的漲價回覆 |
| `POST` | `/v1/webhooks/ecpay` | M1a | 綠界回呼。**不是給前端的**，驗簽 ＋ 時戳窗 ＋ event id 去重 |

### Admin（:5001）

一行一個端點，**與 `openapi.admin.yaml` 逐條對應**（合併寫法會讓兩邊對不起來，
也沒辦法用腳本檢查）。`角色` 是該端點要求的最低角色；`里程碑` 是 live OpenAPI
coverage gate 的唯一過濾來源，不再從 description 猜。

| 方法 | 路徑 | 角色 | 里程碑 | 說明 |
|---|---|---|---|---|
| `POST` | `/v1/auth/login` | — | M1a | 團隊成員登入 |
| `POST` | `/v1/auth/logout` | — | M1a | 登出 |
| `GET` | `/v1/me` | — | M1a | 目前登入的團隊成員 |
| `GET` | `/v1/categories` | ReadOnly | M1a | 分類清單 |
| `POST` | `/v1/categories` | Operator | M1a | 新增分類 |
| `PATCH` | `/v1/categories/{categoryId}` | Operator | M1a | 修改分類 |
| `GET` | `/v1/products` | ReadOnly | M1a | 商品列表 |
| `POST` | `/v1/products` | Operator | M1a | 建立商品 |
| `GET` | `/v1/products/{productId}` | ReadOnly | M1a | 商品詳情 |
| `PATCH` | `/v1/products/{productId}` | Operator | M1a | 修改商品 |
| `PATCH` | `/v1/skus/{skuId}` | Operator | M1a | 修改 SKU |
| `GET` | `/v1/campaigns` | ReadOnly | M1a | 開團列表 |
| `POST` | `/v1/campaigns` | Operator | M1a | 建立開團（草稿） |
| `GET` | `/v1/campaigns/{campaignId}` | ReadOnly | M1a | 開團詳情 |
| `PATCH` | `/v1/campaigns/{campaignId}` | Operator | M1a | 修改開團 |
| `POST` | `/v1/campaigns/{campaignId}/publish` | Operator | M1a | 發布開團（Draft → Open） |
| `POST` | `/v1/campaigns/{campaignId}/close` | Operator | M1a | 提前截團（Open → Closed） |
| `POST` | `/v1/campaigns/{campaignId}/cancel` | Owner | M1a | 取消開團 |
| `POST` | `/v1/campaigns/{campaignId}/settle` | Accountant | M1a | 結團 |
| `GET` | `/v1/campaigns/{campaignId}/offers` | ReadOnly | M1a | 開團商品清單 |
| `POST` | `/v1/campaigns/{campaignId}/offers` | Operator | M1a | 加入開團商品 |
| `DELETE` | `/v1/campaigns/{campaignId}/offers/{offerId}` | Operator | M1a | 移除開團商品（有訂單就擋） |
| `POST` | `/v1/campaigns/{campaignId}/trip-costs` | Accountant | M1b | 登錄旅程成本 |
| `GET` | `/v1/orders` | ReadOnly | M1a | 訂單列表（可搜尋、依狀態與團篩選） |
| `GET` | `/v1/orders/{orderId}` | ReadOnly | M1a | 訂單詳情 |
| `POST` | `/v1/orders/{orderId}/cancel` | Operator | M1a | 取消整張訂單並退款 |
| `POST` | `/v1/orders/{orderId}/lines/{lineId}/cancel` | Operator | M1a | 取消單一品項並退款 |
| `POST` | `/v1/orders/{orderId}/lines/{lineId}/refund-shortfall` | Operator | M1b | 部分買到的短缺數量退款（第十五波 BE-31 加，ADR-026） |
| `GET` | `/v1/campaigns/{campaignId}/purchase-items` | Operator | M1b | 該團的採購清單 |
| `POST` | `/v1/purchase-items/{purchaseItemId}/purchased` | Operator | M1b | 標記買到 |
| `POST` | `/v1/purchase-items/{purchaseItemId}/unavailable` | Operator | M1b | 標記缺貨 |
| `POST` | `/v1/purchase-items/{purchaseItemId}/price-changed` | Operator | M1b | 回報現場漲價 |
| `GET` | `/v1/shipments` | Operator | M1b | 出貨單列表 |
| `GET` | `/v1/shipments/{shipmentId}` | Operator | M1b | 出貨單詳情（第七波 BE-19 加，索引由整合者補） |
| `POST` | `/v1/shipments` | Operator | M1b | 建立出貨單 |
| `POST` | `/v1/shipments/{shipmentId}/dispatch` | Operator | M1b | 交運 |
| `POST` | `/v1/shipments/{shipmentId}/deliver` | Operator | M1b | 標記已送達 |
| `GET` | `/v1/lots` | ReadOnly | M2 | 批號列表 |
| `POST` | `/v1/lots` | Operator | M2 | 批發進貨 |
| `GET` | `/v1/ledger/entries` | Accountant | M1a | 分錄查詢 |
| `GET` | `/v1/ledger/campaign-margin/{campaignId}` | Accountant | M1a | 每團真實毛利 |
| `GET` | `/v1/ledger/liability-vs-cash` | Accountant | M1a | 負債與現金對照 |

### M3 的端點：**形狀尚未定義**

下列會有，但**現在沒有契約**，`openapi.admin.yaml` 裡也刻意沒有它們：

`/v1/reports/monthly-pnl` · `/v1/reports/lot-cost` · `/v1/audit` · `/v1/fee-rule-sets`

**不要照著這幾行做 UI。** 報表要呈現什麼、稽核要怎麼查，都還沒設計過，
現在定形狀等於憑空捏一份之後一定要重做的契約。M3 開工時再補進 YAML。

---

## 9. 前端不可以做的四件事

1. **不做金額運算。** 加總、分攤、含運總額全部由後端回傳。
   前端做了，帳就有兩個來源，而其中一個沒有測試。
2. **不猜業務規則。** 「這個團還能不能下單」問 `GET /v1/campaigns/{id}` 的 `isAcceptingOrders`，
   不要自己用 `closesAt` 跟現在時間比——截團是後端的 Saga Timer 說了算。
3. **不快取寫入結果。** 寫入完成後重新 `GET`，不要拿 request 的內容當成新狀態。
4. **不把 enum 當窮舉。** 見 §6。

## 10. 後端不可以做的三件事

1. **不回傳模組內部模型。** 回應只放 Contracts 的 DTO 形狀，
   `*.Core` 的聚合根永遠不出現在 API 上。
2. **不在 BFF 寫業務邏輯。** BFF 只做組裝與授權（藍圖 §07）。
   出現 `if (order.Status == ...)` 這種判斷就代表邏輯放錯層。
3. **不吐內部錯誤訊息。** `500` 的 `title` 固定是「系統發生問題，請稍後再試」，
   細節進 log 與 trace，不進回應。

---

## 11. 契約異動紀錄

| 日期 | 異動 | 依據 | 影響 |
|---|---|---|---|
| 2026-08-30 | `refundTo` 的語意從「營運在後台代選」改成「客人自己選」；`RefundDestination.StoredValue` 註明 M1b 期間不開放，回可預期的業務失敗，M3 開啟 | ADR-023 | **純語意變更，schema 不動**——`refundTo` 早已 `required`，enum 兩個值早已都在。只改了兩個 cancel 端點（`/v1/orders/{orderId}/cancel`、`/v1/orders/{orderId}/lines/{lineId}/cancel`）的 `refundTo` description，與 `RefundDestination` 的 description。重跑 `pnpm api:generate`：`types.storefront.ts` 逐位元組不變，`types.admin.ts` 差異只有三行新增的 `@description` JSDoc，欄位、型別、`required`、enum 成員一個都沒變 |
| 2026-08-31 | 新增 `POST /v1/orders/{orderId}/lines/{lineId}/refund-shortfall`（部分買到的短缺數量退款）；`AdminOrderLine` 新增 `quantityShortfall`（非必填） | ADR-026（第十五波 BE-31） | **純新增，不改既有 operation 語意**——完全比照既有 `.../lines/{lineId}/cancel` 的形狀（`reason`／`refundTo` request body、回 `AdminOrder`、`422`）。`quantityShortfall` 退款後不歸零，保留原值供追溯，「是否已退」看 `quantityShortfall > 0 且 refundedAmount 為 null`。前端還沒消化這個欄位與新端點，`pnpm api:generate` 待前端下一波跑 |
