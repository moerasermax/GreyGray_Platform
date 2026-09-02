# 生效中的派工

**這個檔案是閘門的唯一輸入。** 只有寫在這裡的 `allow:` 路徑才准寫入原始碼。
由**整合者**維護；實作者模式下閘門會擋住對這個檔案本身的寫入，
所以實作者不能自己擴大授權。

格式：

```
package: <包名>
doc: <派工書路徑>
allow: <相對 repo 根的路徑前綴，一行一個>
```

沒有任何 `package:` 行 = 整合者模式，原始碼一律不准寫。
**註解掉（`<!-- -->`）與圍欄（```）裡的內容不會被讀進來**，可以安心放範例。

---

## 開工：你只開一個 terminal

**Leader 模型。** 使用者開一個 terminal 當 Leader，Leader 用 ai-cli fan out 子代理，
一包一個子代理。使用者不必一包一包開 terminal，也不必自己設 `GG_PACKAGE`。

**Leader 的啟動 prompt 與八包的原文都在 `.dispatch/PROMPTS.md`。**

### 三種身分，閘門分得出來

| 身分 | 怎麼宣告 | 寫得了什麼 |
|---|---|---|
| **Leader** | prompt 開頭 `GG_ROLE=leader`，或 `GG_ROLE=leader claude` | 閘門檔（`.dispatch/`、`.claude/`、`.codex/`）、`docs/`、`GreyGray_PM`。**不寫原始碼** |
| **實作者** | prompt 開頭 `GG_PACKAGE=<包名>`，或 `GG_PACKAGE=<包名> codex` | 只有該包 `allow:` 的路徑，加上全域放行的 `docs/` 等 |
| **身分不明** | 沒宣告 | **什麼都寫不了**（有派工生效時）。這是刻意的 fail-closed |

第三列是重點：**「忘記宣告的實作者」與「Leader」從外面看一模一樣**，
所以不能用「沒綁包別」推定是 Leader——那會讓忘記宣告的人擁有改閘門的權力。
Leader 要明講。

> **包別優先於角色。** 子代理的 prompt 一定帶 `GG_PACKAGE=`；
> 萬一同一段文字裡也混進 `GG_ROLE=leader`（例如 Leader 把整份說明貼過去），
> 子代理仍然只會被綁成實作者，不會升級。已實測。

> 為什麼子代理不能用環境變數：ai-cli 的 `run` 只吃 `workFolder`／`prompt`／`model`／
> `reasoning_effort`／`session_id`，**沒有 env 參數**；而且它的子行程是
> `env: process.env`（`src/core/process-service.ts`），繼承的是 MCP server 自己的環境。
> 一個 server 行程 spawn 所有子代理，行程層級的環境變數本質上帶不了
> 「每個子代理不同」的值。所以綁定只能走 prompt ＋ session_id。

一個 session 只認第一次宣告，之後想改包或改角色都會被拒絕。
包名拼錯一律擋下（fail-closed）。

---

## 生效中：BE-41　結帳 `shippingPolicy` 混合才必填、單一模式後端推導（#37 治本）＋ 壞 body 回 400 ＋ #36 後端側 ＋ #38 啟動腳本

使用者 2026-09-02 親自走旅程第一張單就撞到 #37（純預購購物車結帳 500，而且比登入檢查還早）。問「哪一種是治本」後拍板
「那就用第二種方式修」→ ADR-030：**規則的主人是後端**。契約 schema 改成說明文字早就在說的意思（混合才必填，向下相容），
後端混合沒帶 → 422 `checkout.shipping-policy-required`，單一模式忽略客人的值、依 line 組成推導（純現貨 → `ShipSeparately`、純預購 → `HoldUntilComplete`）。
併：壞掉的 request body 兩個環境都要 400 problem+json（現在 Development 500、Production 空 400）；#36 登出清 `gg_cart`＋「不是你的車」就換新車；
#38 兩支 dev 啟動腳本改 `Start-Process -Environment`，不再改父行程的環境變數。

★★ 最容易做錯的：① dev 三個 Host 現在是 **Release** 在跑、使用者正在上面走旅程——建置與測試一律 `-Configuration Debug`，不准停任何 dev 行程、不碰 `D:\GreyGray\`；
② `CheckoutCompleted.ShippingPolicy`／`Order.ShippingPolicy` 維持不可為 null，Ordering 一行不動、不做 migration；
③ 單一模式客人送了值**不要報錯**（舊客戶端會送）；④ 400 的證明要走真管線（EndToEnd 的 StartHost 模式），兩個環境各跑一次；
⑤ 不要動前端 worktree（FE-26 接手重生型別）。

package: BE-41
doc: docs/37-後端第二十五波派工書.md
allow: src/Hosts/GreyGray.Api.Storefront/
allow: src/Hosts/GreyGray.Api.Admin/Program.cs
allow: src/Modules/Checkout/
allow: tests/GreyGray.M1a.CheckoutOrdering.Tests/
allow: tests/GreyGray.EndToEnd.Tests/
allow: ops/start-dev-hosts.ps1
allow: ops/start-dev-ecpay-simulator.ps1

> `src/Hosts/GreyGray.Api.Admin/Program.cs` 只准做必做 3 的同型接線；`docs/api/openapi.storefront.yaml` 與 `docs/05-API契約.md` 走 docs/ 全域放行。

---

<!--
★ 2026-09-02 晚已通過整合驗收並提交（後端 `abdf286`），撤包。原文保留供追溯。

ADR-029 成立：假的是綠界的伺服器，不是我們的 adapter。Leader 活體對真環境走完三條路——
模擬付款成功（webhook `200 1|OK` → `payment.payment` status=1、`captured_at` 是 UTC、
`provider_transaction_id=DEVFAKE…` → outbox `PaymentCaptured`／`OrderPaid` processed →
訂單「已付款，等待截團」→ 分錄「收款」is_posted）、模擬付款失敗（訂單留在待付款、
`last_payment_failure_code=10100251`、`PaymentFailed` processed、零分錄）、
守衛（不開旗標把 `CheckoutUrl` 指到模擬器 → `GET /v1/cart` 500，例外一字不差點名
`Payment:ECPay:AllowNonEcpayEndpoints`）。測試 241 → **253** 條全過（Release，0 Failed，Skipped 2）。

★★ 這一包途中連撞四個「只在真 DB／真行程才炸」的真缺陷：#34（`InvariantGlobalization=true`
下 Windows 查不到 `Asia/Taipei`，付款發動從沒成功過——子代理第一條真測試撞到，這包修）、
#35（回呼把 +08:00 的 `DateTimeOffset` 存進 timestamptz 被 Npgsql 拒收、webhook 500——Leader
用模擬器走真環境撞到，第二輪修＋真 Postgres 迴歸測試）、#36（登出不清 `gg_cart` cookie，
訪客加入購物車一律「找不到購物車」）與 #37（契約 `shippingPolicy` required、前端純現貨送 `null`
→ 結帳 500）留給下一波。同型未修：`Platform/Time/SystemClock.cs:9 TodayInTaipei`（零呼叫者）。

★ 接受子代理超出「`EcpayGateway` 零改動」的時區修正（證據是可執行例外，不修交不出派工書要的測試）、
拆 `.Core` 類別庫（測試專案是 `Microsoft.NET.Sdk`，直接參考 Web exe 會把框架相依帶進測試行程）、
兩個工具專案都進 `ModuleBoundaryTests.Hosts`、守衛多測一條仿冒網域 `ecpay.com.tw.evil.test`。

## 生效中（已撤包）：BE-40　dev 綠界模擬器（獨立行程）＋ 付款完成後回商店（#33）＋ 非綠界網域守衛

使用者 2026-09-02 拍板：先做 dev 模擬付款，**但要能隨時換回 adapter——可以換回去就等於可以上線**。
Leader 的裁決（ADR-029）：假的不是我們的 adapter，**假的是綠界的伺服器**。`EcpayGateway`、回呼處理、
事件、outbox、分錄全部照正式碼跑；dev 只把本來就可設定的 `Payment:ECPay:CheckoutUrl`／`CreditDetailUrl`
指到一支獨立行程的模擬器。正式碼唯一新增：`Payment:ECPay:AllowNonEcpayEndpoints` 守衛（預設 false）。

★ 派工前查證出的真缺陷 **#33**：`CreateCheckoutFields` 只簽 `ReturnURL`，沒有 `ClientBackURL`／`OrderResultURL`；
前台 `/payment/result` 檔頭寫著「假設後端會設」——正式環境客人付完款會被留在綠界頁，沒有路回商店。
併入這一包（`ClientBackURL`；不做 `OrderResultURL`）。

★★ 最容易做錯的：① 不要碰 `AllowSimulatedPaid`、不要讓模擬器送 `SimulatePaid=1`；② 模擬器要通過的是
**現在這一套**驗簽與回呼判斷，不准為了讓它過而改判斷；③ `Storefront:PublicOrigin` 缺就在付款端點明確炸，
不要預設 localhost（cookie 依 hostname 隔離，dev 要 127.0.0.1）；④ 不要新開測試專案、不要動
`Directory.Packages.props`；⑤ Debug 的 bin 被跑著的 Host 鎖住，建置與測試用 `-Configuration Release`，
不要停任何 dev 行程。

package: BE-40
doc: docs/36-後端第二十四波派工書.md
allow: src/Modules/Payment/
allow: src/Hosts/GreyGray.Api.Storefront/M1aEndpoints.cs
allow: src/Tools/GreyGray.Tools.EcpaySimulator/
allow: src/Tools/GreyGray.Tools.EcpaySimulator.Core/
allow: GreyGray.slnx
allow: tests/GreyGray.M1a.PaymentLedger.Tests/
allow: tests/GreyGray.M1a.Migrations.Tests/PaymentLedgerMigrationTests.cs
allow: tests/GreyGray.Architecture.Tests/ModuleBoundaryTests.cs
allow: ops/start-dev-hosts.ps1
allow: ops/start-dev-ecpay-simulator.ps1
allow: ops/stop-dev-environment.ps1
allow: ops/install-dev-environment.ps1

> `src/Tools/GreyGray.Tools.EcpaySimulator.Core/` 只在派工書必做 5 說的那種情況（測試專案參考 Web exe 不順）才建。
> `M1aEndpoints.cs` 只准動 `MapPayment` 那一段；`install-dev-environment.ps1` 只准動註解。
-->

---

<!--
★ 2026-09-01 已通過整合驗收並提交（後端 d43dc1d），撤包。原文保留供追溯。

#26 解掉：預購商品在「逛商品」這條路上買得到了。Leader 真瀏覽器複驗——
列表那張卡不再顯示「目前無法購買」、改顯示 NT$1,000／50 ml；
詳情頁顯示「這是預購商品，隨團出貨：BE-32 test campaign」＋「Seoul．查看開團詳情」
＋ NT$1,000，加入購物車按鈕 `disabled=false`，「這個規格尚未定價」已消失。
測試 227 → **241 條**全過。**前端一行都沒改**——證實派工書的判斷：
前端本來就照契約寫對了，是後端沒給。

★★ 兩個設計判斷值得後續波次照抄：
① **時間規則刻意不下放到 SQL**：`IsAcceptingOrders` 是「狀態 ＋ `IClock`」，
   抄一份到 SQL 就會有兩份各自漂移的定義。倉儲只用有索引的 `status` 縮範圍，
   時間由聚合判斷。
② **「預購 SKU 的售價只能從 offer 來」寫進契約註解**：`SkuSnapshot.ListPrice`
   是現貨標價，不可以拿來冒充開團凍結價——售價在開團時定死，
   現場買貴買便宜都不影響已成立訂單。

★ 子代理正確地指出**派工書的一個錯誤假設**：§1 寫「用 SKU 批次反查」，
但 `StorefrontProductListItem` 根本沒有 SKU 欄位，列表端點手上只有 `ProductId`，
而 `src/Modules/Catalog/` 不在這包的 allow 裡。它改成以 `ProductId` 為鍵，
SKU→商品的反查放在 Campaign Core 用**本來就存在**的 `ICatalogQuery` 做
（csproj 零改動，`ModuleBoundaryTests` 全綠）。Leader 查證後接受。

★ 追蹤項（派工書沒定義、子代理誠實標出）：一個商品的 SKU 分散在多個收單中的團時，
商品層的 `campaign` 該選哪個沒有定義。目前沿用同一條 tie-break、每個 SKU 的價格
各自來自它自己的團，後果是詳情頁的 `campaign` 可能不是某個 SKU 的 `campaignOfferId`
所屬的團。替代做法（先選團、只認那個團的 offer）會讓另一個團裡買得到的規格被畫成
不能買，更糟。今天的資料兩者結果相同，所以沒有測試釘住這一段。

詳見 `.dispatch/reports/BE-39.md` 與 `GreyGray_PM/03-驗收紀錄.md` 第二十五次。

派工 BE-39：預購商品的開團資訊補回商品端點（#26）　·　docs/35-後端第二十三波派工書.md

2026-09-01 真 Chrome 逐頁複驗查出：**預購商品從「逛商品」這條路徑永遠買不到，
而且違反已凍結的契約**。同一個商品、同一個時刻，開團頁 `/campaigns/{id}` 正常顯示
NT$1,000 可加入購物車，商品列表那張卡卻寫「目前無法購買」、詳情頁寫
「這個規格尚未定價，請稍後再試。」＋按鈕 disabled。

★ Leader 已查證並裁決：
① 契約是對的、程式沒跟上——`openapi.storefront.yaml` 明文規定
   `ProductListItem.campaignId`「`mode = Preorder` 時指向所屬的團」、
   `ProductDetail.campaign`「附上團的摘要，前端要顯示截團倒數」、
   `Sku.price`「現貨是標價，**預購是該團的定價**」；
   但 `M1aEndpoints.cs:1114`／`:1126` **把這三個欄位全部硬編碼 `null`**。
   **不要改 `docs/api/*.yaml`。**
② **不要動前端**——`AddToCartPanel.tsx` 已經照契約寫好了，它的註解甚至寫著
   「沒有附上 campaign 資料時保守視為不可下單」。**前端是對的，是後端沒給**；
   修好後端，前端零行變更就會動。
③ **`Sku.available` 預購恆 0 是契約明文**（「前端不要拿這個值擋預購」），**不要改那段**。
④ `ICampaignStorefront` 目前**沒有**「用 SKU 反查開著的 offer」，要補一個**批次**查詢
   （列表一次要查一整頁的 SKU，逐一查會變 N+1）。三層都在 Campaign 模組自己家裡。
⑤ **同一個 SKU 掛多個開著的團**：規則定為取 `ClosesAt` 最早的那一個，
   **要有專屬測試釘住**；若發現與既有假設衝突，停下來問，不要自己換規則。
⑥ 補測試釘住那四個欄位——**#26 能活到現在正是因為全 repo 沒有任何測試斷言過它們**，
   跟 #24（冪等錯誤碼）是同一個形狀。

package: BE-39
doc: docs/35-後端第二十三波派工書.md
allow: src/Modules/Campaign/
allow: src/Hosts/GreyGray.Api.Storefront/M1aEndpoints.cs
allow: tests/GreyGray.M1a.CampaignPricing.Tests/
allow: tests/GreyGray.M1a.IdentityCatalog.Tests/
-->

---

<!--
★ 2026-09-01 已通過整合驗收並提交（後端 f191120），撤包。原文保留供追溯。

前台第一次有東西可以買：`POST /v1/lots` 讓本地現貨進得了庫存，
`available` 由 0 變成真實數量，單品頁的「已售完」消失、加入購物車按鈕可按。

★★ 這一包最值得記住的是**雙重入帳的陷阱**：帳務的進貨成本分錄掛在
`GoodsReceived` 上（`LedgerEventHandlers.cs`），而代購路徑建完批號**還會再發一次
`LotCreated`**。直覺的「加一個 `LotCreated` 帳務 handler」會讓代購那條線對同一批貨
記兩次帳，**而且在這之前沒有人訂閱 `LotCreated`，所以全套測試不會有任何一條紅**。
新 handler 只在 `LocalWholesale` 入帳，並有專屬迴歸測試（代購路徑存貨借方
175,000 而非 350,000）。子代理另外自己推出 `CustomerReturn` 也不該在這裡入帳
（成本沿用原採購成本、不是新的進貨），派工書沒寫，判斷正確。

★ 子代理第一輪**刻意讓一條測試紅著交付**：`LotCreatedLedgerHandler` 的 DI 登錄
所在的 `ModuleRegistration.cs` 不在派工書 §4 的所有權表裡（**Leader 漏列，
`audit-dispatch.sh` ⑪ 同型第二次，第一次是 BE-31**）。它拒絕自己 `new` 一個 handler
讓測試變綠，理由是那會把「邏輯對、正式環境沒接線」藏起來，而假登錄在真登錄補上後
會變成兩個 handler、製造假性雙重入帳。Leader 補列 allow（`7cf81bf`）後第二輪補上，
**測試本體一個字沒改就由紅轉綠**——這正好回頭證明了那個決定是對的。

★ Leader 裁決維持的取捨：`POST /v1/lots` **不驗 SKU 存不存在**。守衛排在模組冪等
查詢之前的話，SKU 事後被刪會讓重播永遠回不了原批號（#22 A″ 死路，唯一出路是換新鍵，
而換新鍵正好繞過模組冪等）；要同時做對兩件事得把守衛塞進模組，那會讓 Inventory
反向依賴 Catalog、違反鐵則第 3 條。代價是打錯 `skuId` 會建出孤兒批號，已記成追蹤項。

順帶收掉第四個「migration 上界寫死」漂移（`OrderingPaymentConstraintTests` 的
`LastMigration = 16`，以及三個既有測試的 `<= 10`／`<= 6` 過濾），全部改成推導最大編號。
BE-37 列的同型漂移到此清完。

Leader 獨立複驗：`ops\test.ps1` 全套自己重跑 12 個專案（Debug 預設路徑）
**227 條**全過（基準 218＋9）、0 Failed、Skipped 2、0 警告、exit 0；
`git diff` 逐行審查確認 `docs/api/` 與 `frontend/` 零改動；
`audit-dispatch.sh` 十一項通過；**活體驗證**對執行中的 admin Host 實送：
不帶 `Idempotency-Key` → 400 `platform.idempotency-key-required`（補上自驗報告 ④
沒驗到的端到端那一格）、帶鍵 → 201、同鍵重送 → 回同一個 lot id；
DB 查證批號恰好一筆、`ledger.journal_entry` 恰好一筆（DR 1300／CR 1100 各 192,000
＝ 24 × 8,000）——分錄是 **Worker 派送出來的**，同時證明那行 DI 登錄在 Worker 裡也接上了。
詳見 `.dispatch/reports/BE-38.md` 與 `GreyGray_PM/03-驗收紀錄.md` 第二十次。

★ 複驗期間另外查出一件**跟這一包無關**的事，已寫進 `GreyGray_PM`：
`GET /v1/cart` 未登入回 **500 而不是 401**，根因是
`InvalidOperationException: 缺少綠界設定 'Payment:ECPay:MerchantId'`——
DI 解析期就炸，所以**登入與否都一樣**。那個依賴是 `afd82f8`（第七波）引入的，
而 `start-dev-hosts.ps1` 對 ECPay 設定的投遞次數是 **0**，
`install-dev-environment.ps1` 明文說「刻意不接」。
**也就是說購物車從第七波起在 dev 就是死的**，卡在 E3（綠界憑證）。

派工 BE-38：M2 批發進貨與批號列表　·　docs/34-後端第二十二波派工書.md

2026-09-01 使用者第一次在真瀏覽器裡測前台，回報「讀不到數量、加不了購物車、
下不了單」。**前端沒有壞**：`inventory.lot` 是空的、五個 SKU 全部 `available: 0`，
單品頁正確地顯示「已售完」，所以數量選擇器與加入購物車按鈕本來就不會出現。
根因是**今天沒有任何方法能讓庫存進來**——唯一會建立批號的路徑是代購流程的
`GoodsReceived` 事件，契約裡的批發進貨 `POST /v1/lots` 從來沒有實作
（M2 ⏸「契約已定、實作刻意延後」）。使用者 2026-09-01 拍板：**只做後端兩個端點，
不種假資料、不做後台進貨畫面**。

★ Leader 已查證並裁決，派工書 §1／§2 是唯一有效版本：
① **雙重入帳的陷阱**：帳務的進貨成本分錄掛在 `GoodsReceived` 上
   （`LedgerEventHandlers.cs:227`），不是掛在 `LotCreated` 上；而 `LotCreated`
   **目前零消費者**，卻**會被代購路徑發出來**（`ModuleRegistration.cs:160`）。
   直覺的「加一個 LotCreated 帳務 handler」會讓代購那條線對同一批貨記兩次帳，
   **而且現有測試一條都不會紅**。所以新 handler 必須只在
   `Source == LotSource.LocalWholesale` 時入帳，並附一條專屬迴歸測試。
② **冪等照 BE-36 的形狀抄**（`M1bFulfillmentEndpoints.cs:69-101`）：呼叫端把
   `Idempotency-Key` 傳進模組、存在批號上、同鍵重送回原本那一張，
   新欄位走 migration `0017`。**不准用自然鍵**（同一個 SKU 重複進貨是日常，
   跟 BE-36 否決自然鍵同一個理由）、**不准改用 BE-35 的兩階段多載**
   （失敗的鍵留 `IN_FLIGHT` 24 小時，唯一出路是換新 key，而換新 key 正好繞過冪等）。
③ 資料層已查證：`from_campaign_id` 本來就 nullable、`quantity_available` 是
   `GENERATED ALWAYS` 欄位，**兩者都不需要 migration 動它們**。
④ 契約一個字都不用改：`openapi.admin.yaml:863` 與 `docs/05-API契約.md:321-322`
   早就有這兩個端點；`check-openapi.ps1` 預設只驗 M1a，做 M2 是加法。

package: BE-38
doc: docs/34-後端第二十二波派工書.md
allow: db/migrations/0017_
allow: src/Modules/Inventory/
allow: src/Modules/Ledger/GreyGray.Modules.Ledger.Infra/LedgerEventHandlers.cs
allow: src/Modules/Ledger/GreyGray.Modules.Ledger.Infra/ModuleRegistration.cs
allow: src/Hosts/GreyGray.Api.Admin/M2InventoryEndpoints.cs
allow: src/Hosts/GreyGray.Api.Admin/Program.cs
allow: ops/install-dev-environment.ps1
allow: ops/verify-environment.ps1
allow: tests/GreyGray.M1a.Inventory.Tests/
allow: tests/GreyGray.M1a.PaymentLedger.Tests/
allow: tests/GreyGray.M1a.Migrations.Tests/
allow: tests/GreyGray.M1a.CheckoutOrdering.Tests/OrderingPaymentConstraintTests.cs
-->

---

<!--
★ 2026-09-01 已通過整合驗收並提交（後端 f8e357f），撤包。原文保留供追溯。
「現在卡在哪」#24 解掉：六處冪等錯誤碼由 `request.` 前綴改成契約規定的 `platform.`，
前端 `problem.ts` 的「409 用同一把 key 自動重試」**第一次真的會觸發**。
四條迴歸測試逐字釘住 code 與狀態碼，並實測「暫時改回 `request.*` 會四條全紅、
還原後 SHA256 逐位元組相符」——證明不是空跑。
`docs/api/` 零行變更、`check-openapi.ps1` 實跑 PASS。
**明文沒有動 `BffHttp.StatusFor`**：模組層三處（比派工書列的多一處，
`ShipmentAggregate.cs:92`）會落到 422 而非契約的 400，但 `BeginAsync` 在 `work`
執行前就把缺鍵擋成 400，那條路經由 HTTP 走不到——新測試用 `workCalls == 0`
把這個推論變成可執行的證據。
順帶把第三個「migration 上界寫死」漂移改成推導（子代理取**最大編號**而不是
派工書字面的檔案數，理由是編號出現空號時檔案數會安靜少套一份，Leader 接受）。
Leader 獨立複驗：12 個測試專案 **218 條**全過（基準 214＋4）、build 0/0、
`src/`／`tests/` 的 `request.idempotency` 零命中、`audit-dispatch.sh` 十一項通過
（第 ⑦ 項由 Leader 把 `docs/05-API契約.md` 同步到前端樹後轉綠——那是整合者的工作，
子代理正確地停下來回報而沒有跨樹動手）。
詳見 `.dispatch/reports/BE-37.md` 與 `GreyGray_PM/03-驗收紀錄.md` 第十九次。

派工 BE-37：冪等錯誤碼對齊契約（#24）　·　docs/33-後端第二十一波派工書.md

「現在卡在哪」#24：`docs/05-API契約.md` §4 規定冪等三個錯誤碼都是 `platform.` 前綴，
但 `BffHttp` 與兩個端點共**六處**全部回 `request.` 前綴。後果不是潔癖問題——
前端 `problem.ts` 的 `isInFlight` 比對的是 `platform.request-in-flight`，
`http.ts` 拿它決定「409 要不要用同一把 key 自動重試」（預設 2 次），
**後端從來沒有回過那個字串，所以那段自動重試從上線第一天起就是死的**。

★ Leader 已裁決，派工書 §1 是唯一有效版本：
① **改程式、不改契約**——`docs/05` 是已凍結的契約；而且 `docs/api/*.yaml`
   完全沒有列舉錯誤碼（只引用 `IdempotencyKey` header 參數），所以改這些字串
   **不會動到 OpenAPI、不會動到前端 codegen**，`check-openapi.ps1` 也不受影響。
② `request.idempotency-key-too-long` 契約表裡沒有，**保留這個碼**（比併回
   key-required 更精確），前綴跟著改成 `platform.`，並在 `docs/05` 補一行
   記錄它是 400 的子類——那是補文件記錄既有行為，不是改契約語意。
③ **不准動 `BffHttp.StatusFor`**。模組層也回 `platform.idempotency-key-required`，
   那條路走 `Problem(error)` 不帶狀態碼，`StatusFor` 比對不到 → 回 422 而非契約說的 400；
   但 `BeginAsync` 在 `work` 執行前就把缺鍵擋成 400，**那條路徑經由 HTTP 走不到**。
   動 `StatusFor` 會波及所有走 `Problem(error)` 的呼叫點，風險遠大於收益。
④ 順帶根治第三個「migration 上界寫死」漂移（`M1aCoreMigrationTests.cs:296` 的
   `0001→0014`，BE-36 報告「我發現但沒做的事 ①」找到）——改成從實際檔案數推導。

package: BE-37
doc: docs/33-後端第二十一波派工書.md
allow: src/Platform/Http/BffHttp.cs
allow: src/Hosts/GreyGray.Api.Storefront/M1aEndpoints.cs
allow: src/Hosts/GreyGray.Api.Admin/M1bFulfillmentEndpoints.cs
allow: tests/GreyGray.Platform.Tests/
allow: tests/GreyGray.M1a.Migrations.Tests/
-->

---

<!--
★ 2026-09-01 已通過整合驗收並提交（後端 e2b4bf1），撤包。原文保留供追溯。
「現在卡在哪」#23 解掉：`POST /v1/shipments` 不會再建出重複的出貨單。
修法是呼叫端把 `Idempotency-Key` 傳進 Fulfillment 模組、存在出貨單上、
同一把鍵重送回原本那一張（比照 `Cart.CheckoutIdempotencyKey`：
`string?` 欄位 ＋ 過濾式唯一索引 `ux_shipment_tenant_creation_key`）。
**兩個方向是 Leader 在派工前就查證後否決的，不要在後續波次走回去**：
① `(tenant_id, 排序後 orderIds, method)` 自然鍵會破壞 `openapi.admin.yaml`
明文保證的 N:M（「同一批訂單、同一個配送方式建成兩張」就是「一張訂單拆兩箱」
這個日常情境本身），而且既有兩條 N:M 測試拆包裹時都刻意用了兩個**不同**的
`DeliveryMethod`，蓋不到這個回歸——那個修法會全套測試全綠然後在正式環境壞掉。
BE-36 新增的第 3 條測試（同批訂單、**同一個 method**、不同 key → 建得出第二張）
就是為了永久擋住這個方向。
② 改用 BE-35 的兩階段多載會讓失敗的鍵留在 `IN_FLIGHT`，retention 24 小時，
店員同鍵重送一律 409，唯一出路是換新 key，而換新 key 正好繞過模組冪等、
建出第二張。所以 `src/Platform/Http/BffHttp.cs` 與 `docs/api/` 零改動。
③ `CreateAsync` 的冪等查詢排在「訂單必須是 `ReadyToShip`」守衛**之前**——
排在後面的話重播會被守衛擋成失敗，等於複製一次 #22 (A″) 的死路，有專屬測試守著。
順帶修掉三個既有落差：`ops/` 的 migration 清單停在 `0014`（BE-31 的 `0015`
**從來沒補上**，全新 dev 環境會缺 `quantity_shortfall`）、`verify-environment.ps1`
的 `expectedCount` 同款、`install-dev-environment.ps1` 裡一段警告 `0003` 不能重放
的過期註解（該 bug 已由 BE-24 修掉）。
Leader 獨立複驗：`ops\test.ps1` 全套重跑，12 個測試專案 **214 條**全過
（基準 203＋11）0 Failed、Skipped 2、exit 0；build 0 警告 0 錯誤；
diff 逐行審查確認 `BffHttp.cs` 與 `docs/api/` **零行**變更；
`audit-dispatch.sh` 十一項通過。複驗退回三次，其中第三次退回的是 **Leader 自己**
（拿壞掉的量法去「更正」一段本來正確的行尾敘述，子代理堅持不寫自己量不出來的
斷言，重量後證實子代理是對的）。詳見 `.dispatch/reports/BE-36.md` 與
`GreyGray_PM/03-驗收紀錄.md` 第十八次。

派工 BE-36：`POST /v1/shipments` 加模組層冪等，堵掉重複出貨單（#23）　·　docs/32-後端第二十波派工書.md

「現在卡在哪」#23：`FulfillmentApplicationService.CreateAsync` 一層冪等都沒有，
唯一鍵 `(tenant_id, shipment_id, order_id)` 擋不到「同一批訂單建成兩張出貨單」，
重複那張會被撿貨、被 `dispatch`、物流成本重複入帳。

★ Leader 已裁決兩件事，派工書 §1 是唯一有效版本：
① **不准**用 `(tenant_id, orderIds, method)` 當自然鍵——`openapi.admin.yaml` 明文
   保證 N:M（一張訂單拆多個包裹是日常），那樣會擋掉「同一個宅配拆兩箱」，
   而且既有測試蓋不到（既有 N:M 測試刻意用兩個不同的 `DeliveryMethod`）。
   改成比照 `Cart.CheckoutIdempotencyKey`：**呼叫端把冪等鍵傳進來**。
② **不准**把這條端點改成 BE-35 的兩階段多載——新多載讓失敗的鍵留在 `IN_FLIGHT`，
   retention 24 小時，店員同鍵重送一律 409，只能換新 key，而換新 key 正好繞過
   要加的模組冪等、建出第二張。底層有冪等之後，舊多載的 abandon 重試才是最好的路徑。
   **`src/Platform/Http/BffHttp.cs` 一個位元組都不准動。**

package: BE-36
doc: docs/32-後端第二十波派工書.md
allow: src/Modules/Fulfillment/
allow: src/Hosts/GreyGray.Api.Admin/M1bFulfillmentEndpoints.cs
allow: db/migrations/0016_fulfillment_shipment_idempotency.sql
allow: tests/GreyGray.M1b.Fulfillment.Tests/
allow: tests/GreyGray.M1a.CheckoutOrdering.Tests/OrderingPaymentConstraintTests.cs
allow: ops/install-dev-environment.ps1
allow: ops/verify-environment.ps1
-->

---

<!--
★ 2026-09-01 已通過整合驗收並提交（後端 213e8a7），撤包。原文保留供追溯。
「現在卡在哪」#22 的 (A′) 幽靈訂單與 (A″) 換 key 死路解掉，BE-34 留下的
[Skip] 迴歸測試 `Committed_order_must_not_be_reported_as_a_failure` 轉綠。
修法是 `BffHttp` 新增兩階段多載：`work`（會失敗）與 `render`（組回應）分開，
`render` 回傳 `TResponse` 而不是 `Result<TResponse>`，**「組回應失敗」在型別上
就表達不出來**——不靠人記得標記「副作用已產生」，靠型別讓錯的寫法編不過。
另一個關鍵判斷：**刻意不接手 `CompleteAsync` 自己的失敗**，讓冪等鍵留在
`IN_FLIGHT`（重送在 lease 到期前拿 409）而不是 abandon，因為 abandon 才會讓
已產生的副作用被重做一次——這正好堵住 BE-34 在 `POST /v1/shipments` 找到的
那條觸發路徑。五個同款端點改用新多載（checkout ＋ BE-34 新找到的四個）。
**舊多載一個位元組都沒動**（`git diff --numstat` 152/0，純新增），
33 個呼叫點裡 28 個繼續用它。
Leader 裁決兩項超出「五個端點」字面範圍但屬必然後果的變更，均接受：
① GET 訂單詳情（前後台各一）改成退化回 200 而非 422（共用組裝函式的必然結果，
客人看得到訂單金額與狀態比整頁 422 好，契約沒破、退化有 log）；
② `ToAdminOrderAsync` 的 `skuById[...]` 索引器改 `TryGetValue`，補掉一個獨立於
#22 的潛在 `KeyNotFoundException`。
Leader 獨立複驗：build 0/0、12 個測試專案逐一前景執行合計 203（基準 195＋8）
0 Failed、Skipped 由 3 減為 2、`BffHttp` diff 逐行審查確認舊多載零改動、
全部檔案無 BOM、audit-dispatch.sh 十一項通過。
**剩下沒解的**：(B) 購物車結案卻沒訂單（要 saga 化，有 outbox 緩解）、
★ `POST /v1/shipments` 重複風險（BE-34 找到，嚴重性高於 #22 本身，下一包）、
A8／A9 兩個可自癒的幽靈、非泛型多載同款缺陷（目前 0 個呼叫點在用）。
詳見 `.dispatch/reports/BE-35.md` 與 `GreyGray_PM/03-驗收紀錄.md`。

派工 BE-35：修 #22 家族——副作用已 commit 就不准 abandon　·　docs/31-後端第十九波派工書.md

package: BE-35
doc: docs/31-後端第十九波派工書.md
allow: src/Platform/Http/BffHttp.cs
allow: src/Hosts/GreyGray.Api.Storefront/M1aEndpoints.cs
allow: src/Hosts/GreyGray.Api.Admin/M1aEndpoints.cs
allow: src/Hosts/GreyGray.Api.Admin/M1bShortfallRefundEndpoints.cs
allow: tests/GreyGray.M1a.CheckoutOrdering.Tests/
allow: tests/GreyGray.Platform.Tests/
-->

---

<!--
★ 2026-09-01 已通過整合驗收並提交（後端 cb0d2f0），撤包。原文保留供追溯。
查證包，不含修法。**核心結論是推翻既有文件**：`00-進度總表.md` #22 與
`04-交接書.md` 第四節寫的「同一把 Idempotency-Key 重試會建出第二張訂單」
是錯的——測試證實回 201、訂單總數維持 1、回原本那一張，因為 Checkout 與
Ordering 兩個模組各自都綁在同一把 checkout 冪等鍵上做冪等，BFF 的 key 被
abandon 只是讓可重入的業務邏輯重跑一次。實際成立的是三條較輕的：
(A′) 幽靈訂單（訂單與 PaymentRequested 都已產生，客人拿到 422）、
(A″) 換一把 key 走進死路、(B) 購物車結案卻沒訂單（同 key 重試自癒，
且 outbox 會非同步補建——這條自癒路徑交接書與派工書都沒提到）。
**必做 5 找到一個之前沒人發現的 ★ 重複風險：`POST /v1/shipments`**
（`M1bFulfillmentEndpoints.cs:72`）——`CreateAsync` 每次 `ShipmentId.New()`、
不改訂單狀態、唯一鍵擋不到「同一批訂單建成兩張出貨單」，一層冪等都沒有。
觸發條件比 checkout 窄（commit 之後、`CompleteAsync` 之前的基礎設施故障），
後果嚴重得多（重複那張會被撿貨、被 dispatch、重複入帳物流成本）。
另找到四個同款幽靈（S12／A3／A4／A20，退款事件已送出但畫面顯示失敗、
且重試回不了成功）與第 34 個手寫冪等呼叫點（綠界回呼，分類安全但修法要同步）。
Leader 獨立複驗：build 0/0、12 個測試專案逐一前景執行合計 195（基準 187＋8）
0 Errors 0 Failed Skipped 3、必做 5 表格抽樣四個對照點全吻合、A16 的 ★ 另行
獨立讀原始碼確認、audit-dispatch.sh 十一項通過。複驗退回一次（兩個被編輯的檔
長出 UTF-8 BOM，根因是 Python 以 utf-8-sig 寫檔），已修正並確認 diff 除少掉
BOM 外完全相同、行尾未變動。
子代理兩次正確拒絕 stop-gate 要它還原整合者派工前寫的三個 `.dispatch/` 檔——
還原 `ACTIVE.md` 等於刪掉自己的授權、還原 `.selftest-stamp` 會讓稽核 ⑩ 由綠
轉紅。**根因是閘門盲點**：`stop-gate.sh` 用 `git diff` 對 HEAD 比，分不出
「實作者改的」與「session 開始前就髒的」。下一波派工前，Leader 應該先把閘門檔
commit 掉再派工。詳見 `.dispatch/reports/BE-34.md` 與 `GreyGray_PM/03-驗收紀錄.md`。

派工 BE-34：查證「現在卡在哪」#22 checkout 幽靈訂單／冪等 abandon　·　docs/30-後端第十八波派工書.md

package: BE-34
doc: docs/30-後端第十八波派工書.md
allow: src/Hosts/GreyGray.Api.Storefront/M1aEndpoints.cs
allow: tests/GreyGray.M1a.CheckoutOrdering.Tests/
-->

---

<!--
★ 2026-08-31 已通過整合驗收並提交（後端 299b3d5），撤包。原文保留供追溯。
BuildOrderNumber 原本取 OrderId(GUID v7)"N"格式的前 7 hex 碼，剛好落在 GUID v7
的 48-bit 時間戳記區段內，同一視窗（約 17.5 分鐘）建立的訂單幾乎必然撞號——
這正是 BE-32 驗收過程中意外發現、記錄但沒動手修的那個新 bug。改成取尾端
7 hex 碼（純亂數的 random_b 區段），格式／長度不變、不需要遷移，撞號機率
降到約 1/2^28。新增迴歸測試確認修法前紅、修法後綠，不影響既有的
OrderNumber 前綴斷言。
Leader 派工過程踩到兩個環境限制，一併記在這裡供下一波參考：
① `claude --resume <session-id>` 若沒有讓 resume prompt 重新以
`GG_PACKAGE=BE-33` 開頭，`claim-package.sh` 不會重新綁定，閘門會對新寫入
fail-closed（即使該 session 先前的寫入完全合法）——之後任何 resume 都要
比照全新派工一樣帶上 `GG_PACKAGE=`；
② 背景執行的 `ops/test.ps1` 整套跑法在這個 harness 上，session 重啟／resume
時背景行程會被一併砍掉、不會存活，連續兩次都卡在同一個位置——後來改成
12 個測試專案逐一在前景個別執行的方式繞過，全部覆蓋、沒有跳過任何專案。
Leader 獨立複驗：diff 逐行核對、重新 build、audit-dispatch.sh、
CheckoutOrdering.Tests.exe 自己重跑一次 36/36，皆與自驗報告一致。
詳見 `.dispatch/reports/BE-33.md` 與 `GreyGray_PM/03-驗收紀錄.md`。

派工 BE-33：修訂單編號 GUID v7 撞號　·　docs/29-後端第十七波派工書.md

package: BE-33
doc: docs/29-後端第十七波派工書.md
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Core/Order.cs
allow: tests/GreyGray.M1a.CheckoutOrdering.Tests/OrderingTests.cs
-->

---

<!--
★ 2026-08-31 已通過整合驗收並提交（後端 7efca02），撤包。原文保留供追溯。
跟 BE-29 修過的 OrderBy/ThenBy 同一種手法、同一個根因，比照 EntryId 已驗證過的
運算子重載模式修好。訂單側活體 HTTP 驗證完整（4 頁 cursor 分頁全部 200），
出貨側改採信任同等嚴謹度的自動化測試（Leader 裁決同意，成本效益考量）。
過程中意外發現訂單編號 GUID v7 撞號的全新 bug，記錄但沒有動手修，留給下一波。
Leader 派工過程中撞到 ai-cli MCP 斷線＋claude --bg 的 worktree 隔離政策與
專案閘門互相矛盾兩層障礙，改用 claude -p（前景 print 模式）+ harness 自己的
背景追蹤完成派工，全程無 ai-cli。詳見 .dispatch/reports/BE-32.md 與
GreyGray_PM/03-驗收紀錄.md。

派工 BE-32：修 cursor 分頁的 Where 子句同款排序翻譯失敗　·　docs/28-後端第十六波派工書.md

package: BE-32
doc: docs/28-後端第十六波派工書.md
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Contracts/OrderingContracts.cs
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Infra/OrderingRepository.cs
allow: src/Modules/Fulfillment/GreyGray.Modules.Fulfillment.Contracts/FulfillmentContracts.cs
allow: src/Modules/Fulfillment/GreyGray.Modules.Fulfillment.Infra/FulfillmentRepository.cs
allow: tests/GreyGray.M1a.CheckoutOrdering.Tests/OrderingAdminListSortPostgresTests.cs
allow: tests/GreyGray.M1b.Fulfillment.Tests/FulfillmentAdminListSortPostgresTests.cs
-->

---

---

<!--
★ 2026-08-31 已通過整合驗收並提交（後端 c6fb2bf），撤包。原文保留供追溯。

派工 BE-31：支援部分買到（ADR-026）　·　docs/27-後端第十五波派工書.md

package: BE-31
doc: docs/27-後端第十五波派工書.md
allow: src/Modules/Procurement/GreyGray.Modules.Procurement.Core/PurchaseItemAggregate.cs
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Core/Order.cs
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Core/OrderingApplicationService.cs
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Contracts/OrderingContracts.cs
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Infra/OrderingDbContext.cs
allow: src/Hosts/GreyGray.Api.Admin/M1bShortfallRefundEndpoints.cs
allow: src/Hosts/GreyGray.Api.Admin/Program.cs
allow: src/Hosts/GreyGray.Api.Admin/OpenApiComponents.cs
allow: src/Hosts/GreyGray.Api.Admin/M1aEndpoints.cs
allow: docs/api/openapi.admin.yaml
allow: db/migrations/0015_
allow: tests/
-->

---

<!--
★ 2026-08-31 已通過整合驗收並提交（後端 dc0ea1f），撤包。原文保留供追溯。

派工 BE-29：修正三處 `.ThenBy(x => x.Id.Value)` 導致的 admin 列表端點 500　·　docs/26-後端第十四波派工書.md

package: BE-29
doc: docs/26-後端第十四波派工書.md
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Infra/OrderingRepository.cs
allow: src/Modules/Fulfillment/GreyGray.Modules.Fulfillment.Infra/FulfillmentRepository.cs
allow: src/Modules/Procurement/GreyGray.Modules.Procurement.Infra/ProcurementRepository.cs
allow: tests/GreyGray.M1a.CheckoutOrdering.Tests/
allow: tests/GreyGray.M1b.Fulfillment.Tests/
allow: tests/GreyGray.M1b.Procurement.Tests/

派工 BE-30：拿掉兩處過期守衛，讓已付款訂單的「原路退款」真的打得到　·　docs/26-後端第十四波派工書.md

package: BE-30
doc: docs/26-後端第十四波派工書.md
allow: src/Hosts/GreyGray.Api.Admin/M1aEndpoints.cs
allow: tests/GreyGray.M1a.CheckoutOrdering.Tests/AdminCancelLineEndpointTests.cs
-->

---

<!--
★ 2026-08-31 已通過整合驗收並提交（後端 0c4f83e），撤包。原文保留供追溯。

派工 BE-28：storefront Host 補上 CORS（比照 admin Host 的 ADR-021 模式）　·　docs/25-後端第十三波派工書.md

package: BE-28
doc: docs/25-後端第十三波派工書.md
allow: src/Hosts/GreyGray.Api.Storefront/Program.cs
allow: tests/
-->

---

<!--
★ 2026-08-30 已通過整合驗收並提交（後端 c9d3646），撤包。原文保留供追溯。

派工 BE-27：修 Ordering ↔ Fulfillment 循環相依，解除 admin BFF 三組端點永久掛住　·　docs/24-後端第十二波派工書.md

package: BE-27
doc: docs/24-後端第十二波派工書.md
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Infra/ModuleRegistration.cs
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Core/OrderingApplicationService.cs
allow: src/Modules/Fulfillment/GreyGray.Modules.Fulfillment.Infra/ModuleRegistration.cs
allow: src/Modules/Fulfillment/GreyGray.Modules.Fulfillment.Core/FulfillmentApplicationService.cs
allow: tests/GreyGray.M1b.Fulfillment.Tests/
allow: tests/GreyGray.M1a.CheckoutOrdering.Tests/
-->

---

<!--
★ 2026-08-30 已通過整合驗收並提交（後端 5950a10），撤包。原文保留供追溯。

派工 BE-26：員工帳號 bootstrap 工具 ＋ 開發環境最小種子資料　·　docs/23-後端第十一波派工書.md

package: BE-26
doc: docs/23-後端第十一波派工書.md
allow: src/Tools/
allow: GreyGray.slnx
allow: tests/GreyGray.Architecture.Tests/ModuleBoundaryTests.cs
allow: ops/seed/
allow: ops/seed-dev-staff.ps1
-->

---

<!--
★ 2026-08-30 已通過整合驗收並提交（後端 bd616ea），撤包。原文保留供追溯。

派工 BE-25：開發環境與正式機的機密投遞機制　·　docs/22-後端第十波派工書.md

package: BE-25
doc: docs/22-後端第十波派工書.md
allow: ops/lib/Secrets.ps1
allow: ops/install-dev-environment.ps1
allow: ops/start-dev-hosts.ps1
allow: ops/deploy.ps1
-->

---

<!--
★ 2026-08-30 已通過整合驗收並提交（後端 22061a4），撤包。原文保留供追溯。

派工 BE-23：`CapturePayment` 誤設 `RefundedCurrency` ＋ EF model 補約束　·　docs/21-後端第九波派工書.md

package: BE-23
doc: docs/21-後端第九波派工書.md
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Core/Order.cs
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Infra/OrderingDbContext.cs
allow: tests/GreyGray.M1a.CheckoutOrdering.Tests/
allow: tests/GreyGray.M1a.Migrations.Tests/OrderingAppraisalMigrationTests.cs

派工 BE-24：`0003_channel_seams.sql` 的 `ledger.account` seed 非冪等　·　docs/21-後端第九波派工書.md

package: BE-24
doc: docs/21-後端第九波派工書.md
note: 修訂既有檔——`0003_channel_seams.sql` 已在 HEAD 裡，這一包刻意改動它本身
  （不是新增編號）。老闆已核准：專案還沒上線，沒有正式資料依賴舊版 `0003` 的行為，
  理由與範圍見 `docs/21` §5 BE-24。
allow: db/migrations/0003_channel_seams.sql
allow: tests/GreyGray.M1a.Migrations.Tests/M1aCoreMigrationTests.cs

-->

---

<!--
★ 2026-08-30 已通過整合驗收並提交（後端 ad72f69），撤包。原文保留供追溯。

派工 BE-22：本機開發環境（D:\GreyGray）　·　docs/19-後端第八波派工書.md

package: BE-22
doc: docs/19-後端第八波派工書.md
allow: ops/
allow: .github/workflows/

派工 BE-21：帶回→待出貨接線 ＋ OrderLineId 改必填　·　docs/19-後端第八波派工書.md

package: BE-21
doc: docs/19-後端第八波派工書.md
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Infra/
allow: src/Modules/Procurement/GreyGray.Modules.Procurement.Contracts/
allow: src/Modules/Procurement/GreyGray.Modules.Procurement.Core/
allow: tests/

-->

---

<!--
★ 從未啟用，被下面的 BE-31（docs/27）取代。原文保留供追溯。

派工 BE-20：部分買到（ADR-026）　·　docs/19-後端第八波派工書.md
⏸ 等 BE-21 通過整合驗收才啟用（兩包會撞 Procurement 與 Ordering）。
BE-21 通過之後這一包一直沒有真的排進派工，範圍等 2026-08-31 由 Leader
重新設計成 BE-31（docs/27-後端第十五波派工書.md），不再用這份舊的粗略範圍。

package: BE-20
doc: docs/19-後端第八波派工書.md
allow: src/Modules/Procurement/
allow: src/Modules/Ordering/
allow: src/Modules/Ledger/
allow: db/migrations/0015_
allow: tests/
-->

> **`tests/` 給了兩個包**：BE-22 不寫測試，BE-21 只准動 `tests/**/Ordering*`
> 與 `tests/**/Procurement*`。由總驗收逐檔看 diff 把關。
>
> `GreyGray.slnx` 與各 Host 的 `Program.cs` 不在任何 allow 裡——
> 那種一行的變更請整合者代為套用，或臨時開一筆 package 留痕。

---

## 已經通過、不再生效的（保留軌跡）

- **BE-40** dev 綠界模擬器（獨立行程）＋ 付款完成後回商店（#33）＋ 非綠界網域守衛　·　2026-09-02 通過　·　`abdf286`　·
  ADR-029：假的是綠界的伺服器，不是我們的 adapter——換回正式綠界＝不設那兩個網址＋真憑證，零程式碼改動。
  順修 #34（`InvariantGlobalization` 下查不到 `Asia/Taipei`，付款發動從沒成功過）與 #35（回呼 +08:00 存進
  timestamptz 被 Npgsql 拒收、webhook 500）——兩個都是「測試全用樁、沒對真 DB／真行程跑過」才活到現在。
  253 條測試全過；Leader 活體走完成功／失敗／守衛三條路，見 `.dispatch/reports/BE-40.md`
- **BE-37** 冪等錯誤碼對齊契約（#24）　·　2026-09-01 通過　·　`f8e357f`　·
  六處 `request.` 前綴改成契約規定的 `platform.`，前端的「409 用同一把 key 自動重試」
  第一次真的會觸發。四條迴歸測試逐字釘住 code 與狀態碼（先紅後綠 ＋ SHA256 還原證明）。
  `docs/api/` 零改動、`check-openapi.ps1` PASS。218 條測試全過，見 `.dispatch/reports/BE-37.md`
- **BE-36** `POST /v1/shipments` 加模組層冪等，堵掉重複出貨單（#23）　·　2026-09-01 通過　·　`e2b4bf1`　·
  呼叫端傳 `Idempotency-Key` 進模組（比照 `Cart.CheckoutIdempotencyKey`），
  **刻意不用自然鍵**（會破壞契約保證的 N:M）、**刻意不改用兩階段多載**（會製造 24 小時 409），
  `BffHttp` 與 `docs/api/` 零改動。順帶補上 `ops/` 從 BE-31 起就漏掉的 `0015`。
  214 條測試全過，見 `.dispatch/reports/BE-36.md`
- **BE-35** 修 #22 家族：副作用已 commit 就不准 abandon　·　2026-09-01 通過　·　`213e8a7`　·
  `BffHttp` 加兩階段 work／render 多載（讓「組回應失敗」在型別上表達不出來），
  五個同款端點改用；(A′)(A″) 解掉，(B) 與 ★ `POST /v1/shipments` 留給後續
- **BE-34** 查證 #22 checkout 幽靈訂單／冪等 abandon　·　2026-09-01 通過　·　`cb0d2f0`　·
  查證包不含修法。推翻「同一把 key 重試會建出第二張訂單」（測試證實訂單維持 1 張），
  並找到之前沒人發現的 ★ 重複風險 `POST /v1/shipments`，見 `.dispatch/reports/BE-34.md`
- **BE-33** 修訂單編號 GUID v7 撞號（現在卡在哪 #21）　·　2026-08-31 通過　·　`299b3d5`　·
  BuildOrderNumber 改取 GUID hex 尾端 7 碼（純亂數區段）而非前 7 碼（時間戳記區段），見 `.dispatch/reports/BE-33.md`
- **BE-32** 修 cursor 分頁 Where 子句同款排序翻譯失敗（現在卡在哪 #17）　·　2026-08-31 通過　·　`7efca02`　·
  跟 BE-29 同一類問題，比照 EntryId 運算子重載模式修好；意外發現訂單編號 GUID v7 撞號的全新 bug，見 `.dispatch/reports/BE-32.md`
- **BE-31** 支援部分買到（ADR-026）　·　2026-08-31 通過　·　`c6fb2bf`
- **BE-30** 拿掉兩處過期守衛，讓已付款訂單的「原路退款」真的打得到　·　2026-08-31 通過　·　`dc0ea1f`
- **BE-29** 修正三處 `.ThenBy(x => x.Id.Value)` 導致的 admin 列表端點 500　·　2026-08-31 通過　·　`dc0ea1f`
- **BE-28** storefront Host 補上 CORS（比照 admin Host 的 ADR-021 模式）　·　2026-08-31 通過　·　`0c4f83e`
- **BE-27** 修 Ordering ↔ Fulfillment 循環相依，解除 admin BFF 三組端點永久掛住　·　2026-08-30 通過　·　`c9d3646`
- **BE-26** 員工帳號 bootstrap 工具 ＋ 開發環境最小種子資料　·　2026-08-30 通過　·　`5950a10`
- **BE-25** 開發環境與正式機的機密投遞機制　·　2026-08-30 通過　·　`bd616ea`
- **BE-24** `0003_channel_seams.sql` 的 `ledger.account` seed 非冪等　·　2026-08-30 通過　·　`22061a4`
- **BE-23** `CapturePayment` 誤設 `RefundedCurrency` ＋ EF model 補約束　·　2026-08-30 通過　·　`22061a4`
- **BE-22** 本機開發環境（`D:\GreyGray`）　·　2026-08-30 通過　·　`ad72f69`
- **BE-21** 帶回→待出貨接線 ＋ `OrderLineId` 改必填　·　2026-08-30 通過　·　`ad72f69`

BE-1～BE-8（M0）· BE-10（`2c05c99`）· BE-12（`dbec9cd`）·
第六波 BE-13／BE-9／BE-11／BE-14／BE-15（`de3a022`，157 條）·
**第七波 BE-17／BE-18／BE-19（`afd82f8`，171 條全綠）**
