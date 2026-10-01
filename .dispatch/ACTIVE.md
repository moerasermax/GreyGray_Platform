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

## BE-67　dev 員工種子腳本可指定密碼輸出位置（第四十五波第 1 輪，2026-10-01）

`ops/seed-dev-staff.ps1` 加 `-CredentialsDir`，只改新建立帳號的密碼寫到哪；沒給參數時行為一字不變。讓 Leader 建 Claude 專用 dev 後台帳號、讀得到密碼。dev 資料庫停著，除了 T3 不准實跑。

★★ 最容易做錯的三件事：
① 沒給參數時傳給工具的 `--secrets-dir` 必須和現在完全一樣；
② repo 檢查（往上找含 `.git` 的祖先）要在讀 secrets 之前，而且不准先建目錄；不准用 `Resolve-Path`；
③ 只改這一支 dev 腳本，維持無 BOM、LF；不准改 `src/Tools/`（正式機建 Owner 也用同一支工具）。

package: BE-67
doc: docs/73-第四十五波BE-67開發員工種子密碼輸出位置派工書.md
allow: ops/seed-dev-staff.ps1
allow: .dispatch/reports/BE-67.md

---

## BE-63　取號接線：取號通知路由、訂單帶取號資訊、409、後台付款摘要（第四十五波第 1 輪，2026-10-01；BE-67 撤包後才派）

把 BE-62 的 Payment 能力接到 Host：`POST /v1/webhooks/ecpay/payment-info`（scope `webhook:ecpay:payment-info`）、`GET /v1/orders/{orderId}` 帶 `paymentInstructions`、`StatusFor` 精確比對三個碼對到 409、Admin `PaymentSummary` 帶 `method`／`instructions`。契約與 `docs/05` 已由 Leader 寫好。以 Release 為準。

★★ 最容易做錯的三件事：
① 取號通知路由照付款結果路由（先驗簽→冪等五種結果），只換 scope 與服務方法；付款結果路由既有測試一條斷言都不准改；
② 訂單詳情只在 `AwaitingPayment` 才查取號資訊、其他狀態一律 `null` 且不呼叫查詢、查詢失敗回 Problem；不動 `ToOrderAsync` 簽章與結帳／取消兩條 render 路徑；
③ `StatusFor` 只加 `payment.instructions-already-issued`、`payment.concurrent-update`、`ordering.concurrent-update` 的精確比對（`string.Equals`），用近似碼反例證明；不准送 `PaymentInfoURL` 或非信用卡 `ChoosePayment`（BE-66）。

package: BE-63
doc: docs/75-第四十五波BE-63取號接線派工書.md
allow: src/Hosts/GreyGray.Api.Storefront/M1aEndpoints.cs
allow: src/Hosts/GreyGray.Api.Admin/M1aEndpoints.cs
allow: src/Platform/Http/BffHttp.cs
allow: tests/GreyGray.M1a.CheckoutOrdering.Tests/
allow: tests/GreyGray.Platform.Tests/
allow: .dispatch/reports/BE-63.md

---

<!--
★ 2026-10-01 撤包：BE-62 驗收——範圍 23 檔全在 allow，0005／0008 各只改一行 CHECK；Leader 重建 Release 0 警告 0 錯誤、13 個測試執行檔共 545 條 0 失敗（Migrations 37、PaymentLedger 117）；dev 實測：install 清單變更觸發 0001～0024 整套重放成功，重放後 CHECK 含 5、部分唯一索引 (0,1,4,5)、11 個新欄位齊全；Release Host 走信用卡：ChoosePayment=Credit、偽造通知 422 且冪等表零寫入、真付款 Captured、method=0。取號真流程待 BE-63（路由）與 BE-66（模擬器）。原文保留供追溯。

## 已撤包：BE-62　Payment 已取號待繳費狀態＋取號資訊＋依付款方式送綠界（第四十四波第 1 輪，2026-10-01）

路線 B（ADR-044）的 Payment 模組那一段：`InstructionsIssued`、migration `0024`、單一 `ChoosePayment`＋`PaymentInfoURL`＋3 天期限、取號通知的服務方法、新事件 `PaymentInstructionsIssued`。**不碰 `src/Hosts/`**（路由、訂單回應、409、Admin 對應都是 BE-63）。以 Release 為準。

★★ 最容易做錯的三件事：
① 新功能一律掛在**新介面**（`IPaymentInstructionsQuery`、`IEcpayPaymentInfoHandler`），不加到既有介面；不改 `PaymentApplicationService` 建構子與 `EcpaySettings`；
② ATM 的 `ExpireDate` 只有日期 → 當天 23:59:59 台北時間；所有綠界時間都先取台北偏移再 `ToUniversalTime()`（#35）；取號通知與非即時付款不套 20 分鐘時間窗；
③ 部署每次重放全部 migration：`0005`／`0008` 那兩條 CHECK 要改成含 5（只准改那兩條），0024 只加新欄位、新 CHECK、重建部分唯一索引（`DROP INDEX` 要帶 `payment.`）；事件總數有兩支測試寫死 44（Contracts 與 Platform），都要改成 45。

(撤包) package: BE-62
(撤包) note: 修訂既有檔——`0005_m1a_payment_ledger.sql` 與 `0008_m1a_line_refund.sql` 已部署，正式機每次部署都會重放；只改 `payment_status_known`、`payment_refund_status_consistent` 兩條 CHECK 定義加入 5，否則下次部署重放時會用舊定義擋掉已取號的資料
(撤包) doc: docs/70-第四十四波BE-62付款已取號狀態派工書.md
(撤包) allow: src/Modules/Payment/
(撤包) allow: db/migrations/0024
(撤包) allow: db/migrations/0005_m1a_payment_ledger.sql
(撤包) allow: db/migrations/0008_m1a_line_refund.sql
(撤包) allow: ops/install-dev-environment.ps1
(撤包) allow: ops/verify-environment.ps1
(撤包) allow: tests/GreyGray.M1a.PaymentLedger.Tests/
(撤包) allow: tests/GreyGray.M1a.Migrations.Tests/
(撤包) allow: tests/GreyGray.Contracts.Tests/EventCatalogTests.cs
(撤包) allow: tests/GreyGray.Platform.Tests/EventTypeRegistryTests.cs
(撤包) allow: .dispatch/reports/BE-62.md

---

-->

<!--
★ 2026-10-01 撤包：BE-61 驗收——Leader 重建 Release（0 警告 0 錯誤）並重跑七個測試執行檔共 366 條 0 失敗（PaymentLedger 91、CheckoutOrdering 143、Migrations 25、Architecture 28、IdentityCatalog 42、CustomerService 32、EndToEnd 5）；dev 端到端對照：舊版（Debug）送一次偽造通知後，舊 scope 留下 ABANDONED 列、真付款停在 Pending；新版（Release）偽造通知回 422 且冪等表零寫入，真付款走 webhook:ecpay:payment-result、key MerchantTradeNo:TradeNo:1、COMPLETED、Payment Captured；送往模擬器的表單 ChoosePayment=Credit。報告漏寫派工書 §1.2 第 6 點（InFlight 殘留的已知限制），已記在 03。原文保留供追溯。

## 已撤包：BE-61　綠界付款通知先驗簽再去重（#64）＋過渡期只收信用卡（第四十三波第 1 輪，2026-09-30）

BE-59 查證確認的 High 安全缺陷：付款通知端點在驗簽之前就佔住冪等鍵。**不寫 migration、不刪任何資料、不改 `src/Platform/`。**
以 Release 建置與測試為準（dev 環境已停）。

★★ 最容易做錯的三件事：
① 驗簽失敗時冪等表要「零寫入」——斷言儲存內容與呼叫次數，不是只看 422；
② W1／W6 要用正式碼簽章（模擬器或 gateway 的算法），不准用固定回 true／false 的假驗簽器；
③ `tests/GreyGray.M1a.PaymentLedger.Tests/EcpayGatewayTests.cs` 第 24、65 行的 "ALL" 是綠界官方範例向量，不准改。

(撤包) package: BE-61
(撤包) doc: docs/67-第四十三波BE-61付款通知驗簽先於冪等派工書.md
(撤包) allow: src/Modules/Payment/
(撤包) allow: src/Hosts/GreyGray.Api.Storefront/M1aEndpoints.cs
(撤包) allow: tests/GreyGray.M1a.CheckoutOrdering.Tests/
(撤包) allow: tests/GreyGray.M1a.PaymentLedger.Tests/
(撤包) allow: .dispatch/reports/BE-61.md

---

-->

<!--
★ 2026-09-30 撤包：BE-60 驗收——Leader 重跑 Debug 測試執行檔 Migrations 25、IdentityCatalog 42 全過；在 dev 資料庫實測：舊版 trigger 下「孫、子、根」單一語句三層插入會成功，套上新版 0023 後被 category_two_level 擋下（交易 ROLLBACK，無殘留）。原文保留供追溯。

## 已撤包：BE-60　分類兩層守衛的漏洞＋資料庫路徑測試（第四十二波修正包，2026-09-30）

第四十二波整體驗證找到、經反駁代理確認的兩條缺陷。**修訂既有檔**：`0023_category_parent.sql` 已 commit 但未部署，直接改這支、不開新號。**不准改 `src/`。**
dev 三個 Host 正在跑 Release，**建置與測試用 Debug，不要停 Host**。

★★ 最容易做錯的三件事：
① 只改 trigger 函式：「有任何同租戶分類的 parent_id = NEW.id」一律要檢查，不受「上層查不到」影響；其他部分一個字不動；
② M9 與 S15 都要**先紅後綠**，S15 要讓服務層的 SaveChanges 真的撞上 trigger 並回 Result，不是只測純函式；
③ BE-58 報告只改那一句誇大的說法，其他不動。

(撤包) package: BE-60
(撤包) note: 修訂既有檔——`0023_category_parent.sql` 已在 HEAD（BE-58 0d0e6bd），未部署；這一包只改它的 trigger 函式
(撤包) doc: docs/64-第四十二波BE-60分類守衛修正派工書.md
(撤包) allow: db/migrations/0023
(撤包) allow: tests/GreyGray.M1a.Migrations.Tests/CategoryParentMigrationTests.cs
(撤包) allow: tests/GreyGray.M1a.IdentityCatalog.Tests/CategoryHierarchyMigrationServiceTests.cs
(撤包) allow: .dispatch/reports/BE-58.md
(撤包) allow: .dispatch/reports/BE-60.md

---

-->

<!--
★ 2026-09-30 撤包：BE-58 已由 Leader 驗收——自己重跑六個 Release 測試執行檔（Migrations 24、IdentityCatalog 41、CheckoutOrdering 130／1 skipped、PaymentLedger、Inventory、Architecture），審過 0023（可重放、函式 owner 斷言、advisory lock、查不到上層交外鍵）、前台分類清單查詢與 Infra 例外轉換；check-openapi 兩份 PASS（實作者自驗）。原文保留供追溯。

## 已撤包：BE-58　商品分類固定兩層（第四十二波第 1 輪，2026-09-30）

ADR-041。契約 Leader 已寫好、兩棵樹同步、與閘門檔同一個 commit，**本包不准改 `docs/`**。
同一輪前端樹平行 FE-51（`docs/60`，在另一棵樹），檔案不重疊。
migration 用 `0023`（`docs/53` 第 89 行曾為 BE-50 預留；BE-50 之後開包時取當時的下一號，不再預留）。
派工前已做兩層覆驗（Codex 證據包＋5 個唯讀查證代理），結論已併入 `docs/59`。

★★ 最容易做錯的五件事：
① `0023` 要能重放（部署每次重跑全部 migration），照 `0019` 的 `SET ROLE greygray_owner` 與 owner 斷言；
② 兩層規則在應用層回 `Result`（`catalog.invalid-parent-category`／`catalog.category-depth-exceeded`，一字不差；碼裡不能有 `not-found`），trigger 只是守底，而且要先拿每個租戶的 advisory lock；
③ `IdentityCatalogTests` 的手寫 `SchemaSql` 要加 `parent_id`，**連 `UNIQUE (tenant_id, id)` 一起補**，否則複合外鍵建不起來、整個專案紅；
④ 契約 record 的新參數一律加在最後、給預設值，但「編得過」不等於對——`ValidateCategory` 重建 `CategoryInput` 那行（`CatalogServices.cs` 第 566 行）一定要帶 `ParentId`；別的測試專案只跑、不准動；
⑤ PATCH 維持整筆取代，`parentId` 沒帶就是根分類；
⑥ 競態被資料庫擋下（`23514`）也要轉成同樣的 422 錯誤碼，轉換只寫在 Infra，Core 不碰 Npgsql。

(撤包) package: BE-58
(撤包) doc: docs/59-第四十二波BE-58分類父子兩層派工書.md
(撤包) allow: db/migrations/0023
(撤包) allow: src/Modules/Catalog/
(撤包) allow: src/Hosts/GreyGray.Api.Storefront/M1aEndpoints.cs
(撤包) allow: ops/install-dev-environment.ps1
(撤包) allow: ops/verify-environment.ps1
(撤包) allow: tests/GreyGray.M1a.IdentityCatalog.Tests/
(撤包) allow: tests/GreyGray.M1a.Migrations.Tests/
(撤包) allow: .dispatch/reports/BE-58.md

---

-->

<!--
★ 2026-09-30 撤包：BE-59 查證包驗收——Leader 重跑 PaymentLedger.Tests（78 條、0 失敗、1 skipped，原 66）；只新增一個測試檔＋報告。結論：推論 (a) 部分推翻（取號走 PaymentInfoURL，未設就不會打到我們）、(b)(c) 成立、(d)＝#64 確認 High；PaymentDueAt 是漏做；建議開張前只收信用卡（A）＋獨立優先修 #64，B 排後。原文保留供追溯。

## 已撤包：BE-59　#60 綠界 ATM／超商代碼回呼查證（第四十二波第 2 輪，2026-09-30，查證包）

目標是證據，不是修法：綠界協定（附官方網址）、現行程式在每種通知下的行為、三條推論逐條證實或推翻、`PaymentDueAt` 刻意或漏做、修法選項與建議。
**BE-58 撤包之後才派**（它會建置並執行同一個測試專案）。**只新增 `tests/GreyGray.M1a.PaymentLedger.Tests/` 底下的檔**；不准改 `src/`、`docs/`、`ops/`、模擬器、既有測試檔。Docker Desktop 要開著。

★★ 最容易做錯的三件事：
① characterization test 是釘住**現況**（測試要綠），現況和派工書表格不同時照實釘住並回報，不要為了配合表格改測試；
② 綠界協定每一條都要附官方網址，查不到就標「尚待確認」，不准憑記憶寫成事實；
③ 測試組法照派工書 §2：可設定時間的時鐘、同一情境共用一個 service 與 repository、先寫 C0 正向對照；C5 若回 stale-callback 是組法錯，不是推翻推論。

(撤包) package: BE-59
(撤包) doc: docs/61-第四十二波BE-59綠界ATM超商代碼回呼查證派工書.md
(撤包) allow: tests/GreyGray.M1a.PaymentLedger.Tests/
(撤包) allow: .dispatch/reports/BE-59.md

---

-->

<!--
★ 2026-09-24 撤包：BE-57 已由 Leader 重跑 28/28 並以 Playwright 驗收，整合提交 ec9dbea。原文保留供追溯。

## 已撤包：BE-57 客服訊息列表 status 篩選綁定修正

(撤包) package: BE-57
(撤包) doc: docs/57-後端客服訊息篩選綁定修正派工書.md
(撤包) allow: src/Hosts/GreyGray.Api.Admin/SupportEndpoints.cs
(撤包) allow: tests/GreyGray.CustomerService.Tests/
(撤包) allow: .dispatch/reports/BE-57.md

---

-->

<!--
★ 2026-09-19 已通過整合驗收並提交，撤包。原文保留供追溯。

修法：`CanConvert` 開頭一行排除 `Nullable<>`，交給 System.Text.Json 內建的 `NullableConverter`，
它會把底層型別（`Guid` 或 `XxxId`）交回這個 factory 再判斷一次——所以 `XxxId?` 那條路完全不變。
子代理照要求做了先紅後綠：新增的迴歸測試在修之前真的丟 `TypeLoadException`。

Leader 驗：`Contracts.Tests` 16 → **18**、`Architecture.Tests` **28**，兩支 Release 執行檔自己重跑，
數字與自驗報告相符。改動只有兩個檔加報告，零越界。

⚠ 留下：BE-55 的 `TicketOrderId` wrapper 刻意沒拿掉（已在生產路徑上，拿掉是另一個決定）。

## 生效中（已撤包）：BE-56　修 #59 的根因——`GuidIdJsonConverterFactory.CanConvert` 把裸 `Guid?` 誤判成 `XxxId`

第四十波 BE-55 做客服工單時第一次在契約型別用到裸 `Guid?`，踩到一個一直存在但從沒被觸發的臭蟲：
`Nullable<Guid>` 剛好滿足 `CanConvert` 的每一個條件（是 value type、有 `Value` 屬性且型別是 `Guid`、
有吃 `Guid` 的建構式），於是被誤判成 `XxxId`，而 `where TId : struct` 不接受 `Nullable<T>`，
執行期丟 `TypeLoadException`。BE-55 用 wrapper 型別繞過並建議另開包修根因。

**單檔小包、無契約變更**，依 `docs/45` 不走外部覆驗鏈。

★★ 最容易做錯的三件事：
① 只在 `CanConvert` 開頭排除 `Nullable<>`，**不要動其餘四個條件、不要動 `CreateConverter` 與
`GuidIdJsonConverter<TId>` 本體**——`XxxId?` 走的是內建 `NullableConverter` 再交給我們，那條路本來就對；
② **先紅後綠**：修之前那條迴歸測試要真的丟 `TypeLoadException`，把實際訊息貼進報告；
③ **不要拿掉** BE-55 的 `TicketOrderId` wrapper（它已在生產路徑上，拿掉是另一個決定）。

⚠ dev 三個 Host 跑著 Debug、bin 被鎖——用 Release 建置與測試，不要去停 Host。

package: BE-56
doc: docs/56-後端第三十九波派工書.md
allow: src/Shared.Kernel/Json/GuidIdJsonConverter.cs
allow: tests/GreyGray.Contracts.Tests/
allow: .dispatch/reports/BE-56.md

---
-->
<!--
★ 2026-09-19 兩包已通過整合驗收並提交，撤包。原文保留供追溯。

兩包同一個 commit `5637409`（60 檔、+4295 −35）——使用者因額度要求暫停時先做保全性提交，
之後 Leader 補跑整包總驗收才算通過。

BE-54（opus＋high）：收件人姓名／手機／宅配地址凍結進訂單（migration `0021`）、
後台輸出明文、冪等指紋納入新欄位、outbox 保存期限（#57）。
⚠ Leader 驗收時擋下一條並要它補做：**後台完全沒有地址欄位、宅配一樣寄不出去**——
這是它自己在報告裡挖出來的，踩到使用者這次的目標本身，
Leader 據此修訂契約（`AdminOrder` 新增 `recipientAddress`）。

BE-55（sonnet＋medium）：新模組 `CustomerService`（migration `0022`）、匿名留言端點、
後台工單列表與結案。順手補掉 #58（`ops/` 的 migration 清單只列到 `0017`，
`0018`～`0020` 從來沒補進去），並多抓到 Leader 沒列的第六處。
⚠ 它**拒絕照抄 Leader 的 hardcode 表格第 4 列**，而且是對的：
`CustomerNotificationFlowTests` 只套 `0001`～`0004`，加 `customer_service` 會對
一個不存在的 role 下 `ALTER ROLE`，直接讓 E2E 紅掉。
⚠ 它也繞過了 #59（`GuidIdJsonConverterFactory.CanConvert` 把裸 `Guid?` 誤判成 `XxxId`、
丟 `TypeLoadException`），改用 `TicketOrderId` wrapper，**根因未修，建議另開小包**。

Leader 驗：Release 建置 0 警告 0 錯誤；13 個測試專案的 Release 執行檔逐一前景跑完，
結果見 `03-驗收紀錄.md` 2026-09-19。dev 資料庫實際套過 22 支 migration（含 `0021` 三欄、`0022`），
並用真 HTTP 打過客服工單端點、用真瀏覽器走完小幫手全流程到資料庫。

⚠ 留下：BE-54 那條「舊版兩欄 → 新版三欄」的 migration 重放測試被中止沒補完
（Leader 已用真 dev 資料庫驗證重放成立，缺的是自動化測試）；
`platform.outbox_message` 沒有 `processed_at` 索引（現階段量小，等正式機資料量再判斷）。

## 生效中（已撤包）：BE-54　訂單收件人姓名與手機——宅配從地址簿凍結快照、超商由客人填、後台看得到明文、outbox 補保存期限（ADR-039）

使用者 2026-09-19 以 `/goal` 一次拍板三件事，這是第一件。計畫書 `docs/53-第四十波計畫書.md`（`8ab47fc`）。
Leader 跨家盤點（Fable 後端、Gemini 前端）＋ Gemini 逐條覆驗，三條擋派工全部複驗後採納。
**契約（兩份 OpenAPI 與 `docs/05`）與 ADR-039 Leader 已經寫好並提交——實作者一個字都不要改。**
同一波後端還有 BE-55（`docs/55`），前端 FE-34／FE-35／FE-36 在前端樹 `docs/36`～`docs/38`。

★★ 最容易做錯的：① **宅配不要叫客人填**——`ValidateDeliveryAsync` 宅配分支已經取到地址，在那裡抄一份就好；
② `Cart.Complete` 的 `Completed*` 那一段**不要漏**（漏了事件重放時快照會變 null）；
③ **冪等指紋一定要加這兩個欄位**，加了之後同鍵換收件人會回 422 `platform.idempotency-key-reused`，這是要的，補測試；
④ 前台 `OrderResponse` 的收件人**從訂單快照取、不准從即時回查的地址簿取**；
⑤ outbox 清理**不刪 `IsDeadLettered`**；⑥ `ops/`、`GreyGray.slnx`、架構測試是 BE-55 的，碰了會撞。

package: BE-54
doc: docs/54-後端第三十八波派工書.md
allow: db/migrations/0021
allow: src/Modules/Checkout/
allow: src/Modules/Ordering/
allow: src/Platform/Outbox/
allow: src/Hosts/GreyGray.Api.Storefront/M1aEndpoints.cs
allow: src/Hosts/GreyGray.Api.Admin/M1aEndpoints.cs
allow: src/Hosts/GreyGray.Worker/
allow: tests/GreyGray.M1a.CheckoutOrdering.Tests/
allow: tests/GreyGray.M1a.Migrations.Tests/
allow: tests/GreyGray.Platform.Tests/
allow: .dispatch/reports/BE-54.md

---

## 生效中（已撤包）：BE-55　客服工單——新模組 `CustomerService`、前台匿名留言端點、後台工單列表與結案（ADR-040）

使用者 2026-09-19 `/goal` 拍板的第二件事：FAQ 有三處寫「請聯絡客服」但站上沒有客服管道。
前台右下角做引導式小視窗（FE-35），留言進後台工單列表。**不接 AI、不開 GitHub issue。**
**契約與 ADR-040 Leader 已經寫好並提交——一個字都不要改。**

★★ 最容易做錯的：① **端點寫在自己的新檔** `SupportEndpoints.cs`，**不准改 `M1aEndpoints.cs`**（BE-54 正在改，一定撞）；
② **五處 hardcode schema 清單**（三個 ops 腳本 ＋ 兩個 E2E 測試）漏一個就 dev 起不來或 CI 紅，自己 grep 確認不要照抄行號；
③ 防灌的 **fail-open 要自己包 try-catch**——`IDistributedCache` 斷線會拋例外，照抄就變成 Garnet 一掛連正常留言都 500；
④ 模組叫 `CustomerService` **不叫 `Support`**（`SupportModules` 是「支撐模組」的意思），但**端點路徑就是 `/v1/support/tickets`**，這個不一致是刻意的；
⑤ **不要**把它加進架構測試的 `SupportModules` 清單；⑥ 前台**沒有**查詢工單的端點，不要自己補。

★ **Leader 2026-09-19 中途補授權（`ops/verify-environment.ps1`）＋ 追加一項工作**：
子代理查知識庫查出「`ops/` 的 migration 清單是刻意列死的」，Leader 複驗後發現**比那更糟**——
`ops/install-dev-environment.ps1` 第 46 行的 `$migrationFiles` **只列到 `0017`**，
`ops/verify-environment.ps1` 第 274 行的 `$expectedCount = 17` 也寫死，
**`0018`／`0019`／`0020` 從來沒補進去**（2026-09-19 Leader 起 dev 環境時被迫手動補套三支才發現）。
這是既有缺口 **#58**，本波兩包各加一支會讓它更糟。

**BE-55 一併補齊**：把 `0018`～`0022`（含 BE-54 的 `0021`）全部補進 `$migrationFiles`，
`$expectedCount` 改成 22 並更新那兩行訊息字串。理由：這兩個檔本來就在 BE-55 的範圍，
交給一個包統一補，比兩包各改一半安全。

package: BE-55
doc: docs/55-後端第三十八波派工書-第二包.md
allow: db/migrations/0022
allow: src/Modules/CustomerService/
allow: src/Hosts/GreyGray.Api.Storefront/SupportEndpoints.cs
allow: src/Hosts/GreyGray.Api.Storefront/Program.cs
allow: src/Hosts/GreyGray.Api.Storefront/GreyGray.Api.Storefront.csproj
allow: src/Hosts/GreyGray.Api.Admin/SupportEndpoints.cs
allow: src/Hosts/GreyGray.Api.Admin/Program.cs
allow: src/Hosts/GreyGray.Api.Admin/GreyGray.Api.Admin.csproj
allow: ops/install-dev-environment.ps1
allow: ops/start-dev-hosts.ps1
allow: ops/deploy.ps1
allow: ops/verify-environment.ps1
allow: tests/GreyGray.Architecture.Tests/
allow: tests/GreyGray.EndToEnd.Tests/
allow: tests/GreyGray.CustomerService.Tests/
allow: GreyGray.slnx
allow: .dispatch/reports/BE-55.md

---
-->
<!--
★ 2026-09-15 已通過整合驗收並提交（後端 `a4138a4`），撤包。原文保留供追溯。

一輪。Storefront Host `Logistics/CvsLogisticsEndpoints`（開票存 Garnet、鍵為票的 SHA-256、Pending 15 分／Selected 60 分；回傳一律 303、不讀不發 cookie、手動讀 form 並接住解析例外、偽造回傳不燒票；同車讀票 `private, no-store`）＋ 結帳在冪等 `work` 內解票、Host 內指紋含選店票 ＋ `StorefrontUrls` 抽共用 ＋ Checkout／Ordering 門市名稱地址（init 屬性、事件維持 v1）＋ migration `0020` ＋ Admin 三欄 ＋ 模擬器 `/Express/map`（HTML 純函式、注入反例）＋ `start-dev-hosts.ps1` 四鍵全有／全無。測試 340 → **382**。
Leader 驗：Debug／Release 建置 0/0、12 專案測試執行檔逐一前景重跑 382（0 失敗、2 既有 Skip）、check-openapi Storefront 32／Admin 30、audit 通過、diff 範圍（契約、Payment、Platform、deploy、OpenApiContractGate 零改動）；dev 資料庫套 `0020`、`start-dev-hosts.ps1 -Configuration Release -UseEcpaySimulator` 起環境，HTTP 旅程 **32 項全過**（開票欄位、假地圖三家含離島、偽造 303 invalid-reply 後真回傳仍成功、already-used、expired、同車讀票／無 cookie 404／別台車 404、壞票結帳 422、好票結帳 201 且名稱地址取自票而非客人送的代號、模擬付款到 ReadyToShip），資料庫 `ordering.orders`／`checkout.cart` 三欄正確。
留下：既有「結帳成功換購物車 cookie ＋ 指紋含 CartId」讓收到新 cookie 後的斷線重送拿到 `idempotency-key-reused`（不在這包修）；真綠界回傳編碼未實測；`logistics-stage` 真地圖未走（`ServerReplyURL` 要對外 443）。
Leader 驗收時自己的量法錯了三次（`dotnet test` 在 .NET 10 走 VSTest 全失敗、`--project` 未知參數、PowerShell `H` 撞 `Get-History` 別名），都沒有當成結果，改用測試執行檔與 HttpClient 重量。

## 生效中（已撤包）：BE-53　7-ELEVEN 超商取貨門市——選店票 ＋ 綠界回傳驗證 ＋ 門市名稱地址凍結進訂單 ＋ 後台門市欄位 ＋ 模擬器假地圖（ADR-038）

使用者 2026-09-15 以 `/goal` 問「可以新增 7-11 收貨嗎」。Leader 查證：契約從 M1a 起就有超商取貨與 `convenienceStoreCode`，但**從來沒接電子地圖**——前台手動輸入代號、
`M1aEndpoints.cs` 第 1491 行把代號塞進「門市名稱」、後台訂單回應沒有任何門市欄位。契約（三條 `/v1/logistics/*` 與四個新欄位）、ADR-038 Leader 已寫好並複製進前端樹。
同一波前端 FE-33 平行（前端樹 `docs/35`）。派工書經 Codex 逐行覆驗 13 條與 Gemini 情境覆驗 10 條。

★★ 最容易做錯的：① 綠界回傳是**跨站 POST、沒有檢查碼**——回傳端點不讀不發 cookie、只信自己發的票，一律 **303**；② 結帳**解票放在冪等 `work` 裡**、指紋含選店票；
③ 選店票欄位不是 null 就要驗，**不准退回信 `convenienceStoreCode`**；④ `CompleteCheckoutRequest`／`CheckoutCompleted` **只加 init 屬性**、`EventType` 維持 v1、`Cart.ReplayCompletedEvent` 要帶；
⑤ migration **`SET ROLE greygray_owner`**、表名 `ordering.orders`、`M1aCoreMigrationTests` 第 106 行保持 `0019`；⑥ 沒有物流設定不擋開機、有設定但壞掉開機就炸；
⑦ 模擬器 HTML 全部 HtmlEncode 並有注入反例；⑧ Checkout／Ordering／Admin 的 allow 是**檔案層級**，清單外的檔要先問。

package: BE-53
doc: docs/52-後端第三十七波派工書.md
allow: db/migrations/0020
allow: src/Modules/Checkout/GreyGray.Modules.Checkout.Contracts/CheckoutContracts.cs
allow: src/Modules/Checkout/GreyGray.Modules.Checkout.Core/CheckoutApplicationService.cs
allow: src/Modules/Checkout/GreyGray.Modules.Checkout.Core/Cart.cs
allow: src/Modules/Checkout/GreyGray.Modules.Checkout.Infra/CheckoutDbContext.cs
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Core/Order.cs
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Contracts/OrderingContracts.cs
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Infra/OrderingDbContext.cs
allow: src/Hosts/GreyGray.Api.Storefront/
allow: src/Hosts/GreyGray.Api.Admin/M1aEndpoints.cs
allow: src/Tools/GreyGray.Tools.EcpaySimulator/
allow: src/Tools/GreyGray.Tools.EcpaySimulator.Core/
allow: ops/start-dev-hosts.ps1
allow: tests/GreyGray.M1a.CheckoutOrdering.Tests/
allow: tests/GreyGray.M1a.Migrations.Tests/
allow: tests/GreyGray.M1a.PaymentLedger.Tests/
allow: tests/GreyGray.EndToEnd.Tests/
allow: tests/GreyGray.Contracts.Tests/

> `db/migrations/` 只准新增 `0020_convenience_store_snapshot.sql`；`src/Hosts/GreyGray.Api.Storefront/Logistics/` 是新目錄；Admin 只准加三個門市欄位；`ops/start-dev-hosts.ps1` 只准加 `Logistics__ECPay__*` 那一段。
> 後兩個 tests 專案只在既有測試因建構式／回應形狀變了才動。`docs/` 全域放行，但契約 YAML 與 `docs/05` 不准動。
-->

---

<!--
★ 2026-09-15 已通過整合驗收並提交（後端 `46ec1c5`），撤包。原文保留供追溯。

一輪。migration `0019`（SET ROLE greygray_owner ＋ owner 斷言、無 GRANT）＋ `IStorefrontFavorites`（ON CONFLICT DO NOTHING、商品詳情那一套可見規則在 SQL 分頁前、24-byte 雙欄 cursor、`ProductId` 比較運算子供 EF keyset）＋ 三條端點與 `isFavorited` 真值 ＋ 正式 migration／正式 DB／HTTP 層測試。測試 332 → **340**。
Leader 驗：Debug／Release 建置 0/0、12 專案逐一前景重跑 340（0 失敗、2 既有 Skip，Architecture 28）、check-openapi Storefront M1a 29/29／Admin 30/30、audit 通過、`git diff` 契約／`ops/`／其他模組零改動；dev 環境補套 0018／0019 後用 HTTP＋SSR 走完最愛旅程（匿名 401、A 收藏後清單／列表／詳情／SSR 愛心為真、B 看不到、刪除冪等、格式錯 404、limit=0 422）。
留下：正式 DB 整合測試把多個情境塞在同一個 Fact（壞掉時難定位）；收藏清單逐筆查 SKU 與圖片（沿用既有商品列表寫法）；子代理用 `dotnet run` 誤跑一次被 runner 拒絕（已如實寫進報告）。
提交子代理收工時被 stop gate 以稽核 ② 擋下（0019 已進 HEAD、包仍生效）——預期中，撤包後解除。

## 生效中（已撤包）：BE-52　最愛清單後端——`catalog.favorite` ＋ 三條新端點 ＋ `isFavorited` 填真值（ADR-036）

使用者 2026-09-15 以 `/goal` 下達「不是購物車而是最愛清單——可以加上我的最愛瀏覽」。Leader 查證：契約從 M1a 起就有 `isFavorited`，
但**沒有任何寫入端點**，後端在 `M1aEndpoints.cs` 第 1390、1441 行寫死 `false`，前台愛心是本地狀態、重新整理就消失。
契約（`docs/api/openapi.storefront.yaml` 三條 operation）與 `docs/05` 三列 Leader 已寫好並複製進前端樹。同一波前端 FE-31／FE-32 平行（前端樹 `docs/34`）。

★★ 最容易做錯的：① migration **先 `SET ROLE greygray_owner`**（照 0018），否則 `greygray_catalog` 拿不到預設權限；
② **cursor 要同時帶 `created_at` 與 `product_id`**，現有 Catalog 的 `Slice`／cursor 不能沿用；③ 重複／併發加入用 `ON CONFLICT DO NOTHING`，**不准先查再寫**；
④ 可見規則＝商品詳情那一套（上架且至少一個上架 SKU），**在 SQL 分頁之前**過濾；⑤ 匿名看商品時**不准查最愛**（測試要斷言「沒查」）；
⑥ 資料庫測試用**正式 migrations**（`M1aCoreMigrationTests` 那一套），不准往 IdentityCatalog 手寫的 `SchemaSql` 加表；⑦ 不改契約、不改 `ops/`。
（`check-openapi` 的 M1a 模式只比 method＋path，參數與回應碼要靠自己的 HTTP 層測試——Codex 覆驗更正。）

package: BE-52
doc: docs/51-後端第三十六波派工書.md
allow: db/migrations/0019
allow: src/Modules/Catalog/
allow: src/Hosts/GreyGray.Api.Storefront/
allow: tests/GreyGray.M1a.IdentityCatalog.Tests/
allow: tests/GreyGray.M1a.Migrations.Tests/
allow: tests/GreyGray.Contracts.Tests/

> `db/migrations/` 只准新增 `0019_catalog_favorite.sql`；後兩個 tests 專案只在既有測試因新 migration／新 operation 需要調整時才動。`docs/` 全域放行，但契約 YAML 與 `docs/05` 不准動。
-->

---

<!--
★ 2026-09-06 已通過整合驗收並提交（後端 `2ad080a`），撤包。原文保留供追溯。

兩輪。第一輪子代理**還沒動任何原始碼就停下來**，指出必做 E 自相矛盾（`ops/build.ps1:39` 無條件跑整支 `ops/test.ps1`，與「分專案前景跑」不可能同時成立——這個矛盾從 BE-46 之後每一份派工書都帶著），並回報 `EcpaySimulator/Program.cs:13` 的註解會在 E3 換憑證那一刻誤導人。兩則裁決見 `docs/47` §6；第二條補了 `src/Tools/GreyGray.Tools.EcpaySimulator/` 的授權（只准改那一句註解）。
第二輪交付：兩個網址改走 `Required()`（錯誤訊息同時給正式站與測試站網址，網址是去查綠界官方文件抄的）、`RequireEcpayEndpoint` 零改動、`deploy.ps1` 五鍵齊全才過並全部投遞、12 個設定點補齊、`ops/self-test.ps1` 加五鍵投遞與缺鍵拒絕的檢查並在 PowerShell 5.1 實跑。先讓新測試在舊實作上紅了 6 條才移除預設，又注入違規版本反向確認，原始碼以 SHA-256 確認逐位元還原。測試 315 → **332**。
Leader 驗：`dotnet build -c Debug` 0/0、12 專案 332（0 失敗、2 個既有 Skip）、`check-openapi` PASS、`ops/self-test.ps1` 全過、`selftest.sh` claude 137／codex 139、audit 通過、`git diff` 逐行（契約 YAML、其他業務模組、前端零改動）。
★ 部署前必做（Leader）：正式機 `yc-deploy.ps1` 與 YC 上的 `ecpay.json` 要先改成五個值，否則部署後三個服務會拒絕開機。
★ 行為變更：`ops\start-dev-hosts.ps1` 不加 `-UseEcpaySimulator` 時，父行程必須提供五個 `Payment__ECPay__*` 值。
留下：`ops/build.ps1` 該不該有「只建置」的參數（子代理提，裁決不在這一包做）。

## 生效中：BE-49　綠界五個值缺一個就開不了機——拿掉兩個「悄悄指向測試站」的預設值，`deploy.ps1` 要求並投遞五個值

Leader 2026-09-06 逐行查證：`Payment.Infra/ModuleRegistration.cs` 的 `MerchantId`／`HashKey`／`HashIV` 走 `Required()`（第 105-107 行）沒問題，
但 `CheckoutUrl`（第 108-109 行）與 `CreditDetailUrl`（第 118-119 行）是 `??` 預設到 **`payment-stage.ecpay.com.tw`**。
現有的網域守衛 `RequireEcpayEndpoint`（第 152-167 行）只認 `https` ＋ `*.ecpay.com.tw`——**測試站完全符合**，所以它擋得住「指到模擬器」，擋不住「留在測試站」。
`ops/deploy.ps1` 第 242-248 行只驗三個鍵、第 266-269 行只注入三個，第 245 行的錯誤訊息本身就把格式寫成三鍵。
**結論：拿到老闆的正式商店代號、照腳本自己的訊息只換三個值，正式金鑰會安靜地打到綠界測試站，一個字都不會抱怨。**
E3 是上線必要條件、憑證要等老闆，但這個「不會報錯的錯」現在就能治，而且要趁憑證到手之前治好。

★★ 最容易做錯的：① **不要改 `RequireEcpayEndpoint`**（它負責的是另一件事而且是對的）；② **不要把正式站網址變成新的預設值**（只是把病換一個方向）；
③ 必做 A 會讓一票測試與 `ops/` 腳本開不了機，那是預期的，但要全部掃過補齊（`grep -rn "Payment:ECPay\|Payment__ECPay" src/ tests/ ops/` 目前 12 個檔）；
④ **模擬器那條路（ADR-029）不能斷**；⑤ `ops/` 的腳本要能在 PowerShell 5.1 跑；⑥ 正式機的 `C:\Source\yc-deploy.ps1` 是 Leader 的事，不在這一包。

package: BE-49
doc: docs/47-後端第三十三波派工書.md
allow: src/Modules/Payment/
allow: ops/
allow: tests/
allow: src/Tools/GreyGray.Tools.EcpaySimulator/

> `docs/` 全域放行。契約 YAML、`db/migrations/`、其他業務模組、前端不在 allow——派工書 §2 說明過；真的需要就停下來回報。
> **2026-09-06 Leader 補授權**：`src/Tools/GreyGray.Tools.EcpaySimulator/` —— 子代理查證後回報 `Program.cs:13` 附近的註解說「換回正式綠界可以不設兩個 URL」，必做 A 之後那句話會是錯的，而且會剛好在 E3 換憑證那一刻誤導人。**只准改那一句註解，不准動那支工具的行為。**

-->
---

<!--
★ 2026-09-04 已通過整合驗收並提交（後端 `89bb5c5`），撤包。原文保留供追溯。

一輪。`CompleteCheckoutAsync` 成功（201）後在**方法回傳前**換新 `gg_cart`（放回傳前而不是 `work` 裡：兩階段多載重放時根本不會進 `work`，`BffHttp.cs:249-256`）；撞到 `cart-already-completed` 時換車＋訊息改成講得出下一步的話（錯誤碼不動、不拿新車重試）；`CartView.IsCompleted`（init 屬性，既有建構點零改動）＋唯一組裝點填值，`GetCartAsync` 拿到已結案的車就換車回空車；`StockReservationService` 釋放路徑加「已出庫就安靜成功＋log」一支（一致性守衛沒動）。測試 308 → **315**。
接受子代理一個自主判斷：`AddInventoryModule` 冪等補 `AddLogging()`（而不是改四個手搭 ServiceProvider 的測試）。
Leader 驗：build Release 0/0、12 專案 315 全過、`check-openapi` 綠、契約 YAML 與 `docs/05` 零改動、audit 通過；第六次部署 release `20260904015905167` 一次過。
留下：#46 只讓事件不卡住，「已取消卻已交運」的業務狀態（退款／退貨入庫／成本迴轉）仍然沒人做，派工書 §2 明列不碰。

## 生效中（已撤包）：BE-48　結帳成功後讓購物車退休（#44）＋ 取消已出貨的訂單不再讓事件卡住重試（#46）

2026-09-04 00:34-00:51 使用者在正式站走新單後回報兩個症狀，Leader 查 log 與資料庫查出三件事，這一包修其中兩件（第三件 #45 是前端，同時派 FE-29）。
**#44**：Storefront log 有**連續 15 次 `POST /v1/cart/checkout` → 422**（`checkout.cart-already-completed`），使用者是重新 `POST /v1/cart/lines` 才脫困。根因：`M1aEndpoints.cs` 全站只有 `AddCartLineAsync`（第 985-996 行，#36 的修法）會換車；`GetCartAsync`（第 1059 行）只處理 `cart-not-found`，`CompleteCheckoutAsync`（第 585-637 行）成功後完全不換。所以下單成功後 `gg_cart` 一直指著已結案的車。
**#46**（BE-47 帶進來的回歸）：`ordering.OrderCancelled.v1` 在正式機卡住重試，錯誤「reservation … 的 allocation 與 lot 保留量不一致，拒絕部分釋放」。BE-47 之後保留單在交運轉「已出庫」並扣掉 `quantity_reserved`，`OrderCancelledInventoryHandler` 仍無條件釋放。守衛是對的，`StockReservationService.cs:163` 已有「已釋放就安靜成功」的先例，少的是「已出庫」那一支。
派工書 `docs/44-後端第三十二波派工書.md` §0 有正式機 log 的實際次數與所有行號。

★★ 最容易做錯的：① 換 cookie 要放在**真的成功之後**，注意 `ExecuteIdempotentAsync` 的兩階段與重放；② 撞到「已結案」時換車但**不要**拿新車重試 checkout（新車是空的）；③ `CartView` 加欄位用 `init` 屬性不要動建構式，而且**服務端要真的填值**（只加屬性不填就是 #43 那種形狀）；④ 不動釋放路徑的一致性守衛——是它把缺口叫出來的；⑤ 不動契約 YAML、不新增 migration。

package: BE-48
doc: docs/44-後端第三十二波派工書.md
allow: src/Hosts/GreyGray.Api.Storefront/
allow: src/Modules/Checkout/
allow: src/Modules/Inventory/
allow: tests/

> `docs/` 全域放行。`src/Modules/Fulfillment/`、`src/Modules/Ordering/`、`src/Hosts/GreyGray.Worker/`、`ops/`、契約 YAML 不在 allow——派工書 §2 說明過；真的需要就停下來回報。
-->

---

<!--
★ 2026-09-04 已通過整合驗收並提交（後端 `ac63775`），撤包。原文保留供追溯。

兩輪。migration `0018`（保留單第三個狀態「已出庫」＋ `consumed_at`，刻意不挪用 `released_at`）；Inventory `ConsumeAsync` ＋ `FulfillmentEventHandlers` 訂閱 `ShipmentDispatched`
（逐筆 allocation 帶雙守衛 UPDATE、不寫 generated column、冪等靠 reservation 狀態而不是 `processed_message`、批號缺成本時 fail-closed）；
Ledger 兩個 handler（DR 5100／CR 1300、DR 5200／CR 1100，運費為零不開分錄）；Ordering `MarkLinesShipped()` ＋ `CompleteAfterAppraisal` 轉 `Completed`；
`LedgerCoverageTests` 機械點名分錄表九列都有人發、有人收（含負向對照，證明「有人接」與「Ledger 接了」不是同一件事）。測試 286 → **308**。
★ 子代理第一輪停下來提兩個否決，**Leader 查證後兩個都成立**（第五次否決、第五次都對），補授權 `767dbd5`：
① §0.6「不需要 migration」是 Leader 寫錯——`0006_m1a_core.sql:291` 的 CHECK 擋掉 status=2；
② `SourceRef` 少了 `lotId` 會撞 `LedgerPostingService` 的 `(sourceModule, sourceRef)` 去重，同 SKU 跨兩批號的第二筆被靜靜吃掉、銷貨成本少認一段。
Leader 驗：build Release 0/0、12 專案 **308** 全過、audit 通過、`git diff` 只動 allow 內路徑（Fulfillment 模組與 csproj 零改動）。
接受子代理一個自主判斷：批號沒有單位成本時整筆失敗（`inventory.lot-unit-cost-missing`），不照扣也不發零成本事件。

## 生效中（已撤包）：BE-47　把「出貨」這個階段接起來——交運時扣庫存並結轉銷貨成本 ＋ 運費成本入帳 ＋ 品項狀態轉 `Shipped`／`Completed`（#43、#42）＋ 分錄覆蓋的機械把關

2026-09-03 15:35 使用者問「要不要再測一次完整流程」，Leader 先查正式機帳務，查出 **#43：出貨那一段完全沒落帳也沒出庫**。帳上只有兩筆進貨＋一筆收款；`inventory.lot` 還是 on_hand 60／reserved 5（貨已寄出）。
對照 `docs/02-事件與狀態機.md` 第 165-175 行分錄表第 ⑦ 階段該有兩筆：出貨從批號結轉（`StockCostAllocated`，DR 5100／CR 1300）、支付宅配運費（`ShipmentDispatched`，DR 5200／CR 1100）。
`StockCostAllocated` 契約型別定義了、`EventTypeRegistry` 登記了、`docs/02` 第 24 行寫明 M1b 由 Ledger 消費——**沒有任何地方發它、也沒有 handler 收它**；`ShipmentDispatched` 有發但 Ledger 沒 handler；Inventory 沒有任何 shipment 事件的 handler，`IStockReservation` 也根本沒有「出庫」這個操作。順帶 **#42**：`OrderLineStatus` 只被指派過 Pending／Purchased／Unavailable／Cancelled，`Shipped`／`Completed` 從來沒有。
派工書 `docs/43-後端第三十一波派工書.md` §0 把正式機數字、行號、現成可用的東西（`AccountCodes` 四個科目、`StockReservationPlan.ForOrder`、釋放路徑的 SQL 形狀）全列了。

★★ 最容易做錯的：① 觸發點是**交運**不是簽收；② 一張出貨單可合併多張訂單、一張訂單可拆多張出貨單，冪等要靠 reservation 狀態擋（框架的 processed_message 擋不到）；③ `quantity_available` 是 generated column 不准寫；④ **不新增 migration**（新狀態值不動 schema）；⑤ 不動 `ShipmentDelivered` 那條路（BE-46 剛修好）；⑥ `Reserved` 這一包不做。

**★ 2026-09-03 23:10 Leader 補授權（子代理第一輪的兩個否決都成立，查證過）**：
① §0.6「不需要 migration」是 Leader 寫錯——`db/migrations/0006_m1a_core.sql:291` 的 `reservation_status_known CHECK (status IN (0, 1))` 與 `reservation_release_consistent` 都擋掉 status=2。**配號 migration `0018`**，allow 加 `db/migrations/`。
② `SourceRef = "{orderId}:{skuId}"` 會撞 `LedgerPostingService` 的 `(sourceModule, sourceRef)` 去重（`reservation_allocation` 主鍵是 `(reservation_id, lot_id)`，同 SKU 跨兩批號＝兩列、成本不同，第二筆會被靜靜吃掉）→ 改成 **`{orderId}:{skuId}:{lotId}`**。
兩點都已改進派工書 §0.6／§0.7／必做 A。子代理第一輪已交付必做 B、C、D-1（測試 286 → 302），A 與 D-2 續做。

package: BE-47
doc: docs/43-後端第三十一波派工書.md
allow: db/migrations/0018_inventory_reservation_consumed.sql
allow: src/Modules/Inventory/
allow: src/Modules/Ledger/
allow: src/Modules/Ordering/
allow: tests/

> `docs/` 全域放行。`src/Modules/Fulfillment/`、`src/Hosts/`、`ops/`、契約 YAML 不在 allow——派工書 §2 說明過；真的需要就停下來回報。
> migration **只准 `0018` 這一支**（allow 直接寫到檔名，別的編號寫不進去）。
-->

---

<!--
★ 2026-09-03 已通過整合驗收並提交（後端 `1d58dfa`），撤包。原文保留供追溯。

兩輪。`WorkerModules.AddWorkerModules`（唯一清單，＋Fulfillment）、Program.cs 開機驗每個 `IIntegrationEventHandler<T>` ＋ 點名 `IFulfillmentQuery`（刻意接受「沒有 Payment:ECPay:* 就不開機」）、`WorkerCompositionTests`（正向三斷言＋負向對照）、Ordering 缺模組時例外講人話；
第二輪：E2E 的 Worker 替身補齊 13 個 schema 連線字串＋綠界三鍵（第一輪被新驗證正確擋下）。測試 284 → **286**（Architecture 14 → 16），Leader Release 全套重跑全過。
第四次部署 release `20260903071555793`：Worker 一起來就把停在 attempts=8 的 `ShipmentDelivered` 處理掉（15:16:29），訂單 ReadyToShip → **Shipped**、鑑賞期 saga timer 2026-09-10 排上。

## 生效中（已撤包）：BE-46　Worker 掛上 Fulfillment 模組（#41：出貨單簽收後訂單永遠停在「準備出貨」）＋ 開機驗 handler 相依 ＋ 架構測試（含負向對照）＋ Ordering 缺模組時講人話

2026-09-03 13:53 使用者在正式站後台把出貨單標記送達，後台資料都對，但 Worker 處理 `fulfillment.ShipmentDelivered.v1` 時炸 `ArgumentNullException (resolvedFulfillmentQuery)` 連續 8 次：`Worker/Program.cs:59-70` 掛了 11 個模組、**沒有 `AddFulfillmentModule`**（第 72 行留著「之後的波次再納入」從沒做），而 Ordering 的 `ShipmentDeliveredHandler` 靠 `Lazy<IFulfillmentQuery?>` 在真的被叫到時才發現沒有。訂單因此停在 ReadyToShip，前台顯示「準備出貨」。
Leader 13:59:58 先停掉正式機 Worker（與 watchdog）讓訊息停在 attempts=8 不進死信；這一包部署上去就會接著處理。派工書 `docs/42-後端第三十波派工書.md` §0 有完整證據與「不用資料庫就能重現」的方法（空 orderIds）。

★★ 最容易做錯的：① 模組清單抽成 `WorkerModules.AddWorkerModules`，測試呼叫**同一個方法**（不是抄一份清單）；② 開機驗證要點名 `IFulfillmentQuery`（Lazy 相依光解析 handler 抓不到）；③ 負向對照那條要真的 throw；④ 不動 Admin／Storefront 的模組清單、不動 Lazy 做法；⑤ dev Host 不准停，B-4 只起自己的 Worker 行程。

package: BE-46
doc: docs/42-後端第三十波派工書.md
allow: src/Hosts/GreyGray.Worker/
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Core/OrderingApplicationService.cs
allow: tests/

> `docs/` 全域放行。`Ordering.Infra`、`Fulfillment`、`Api.Admin`、`Api.Storefront`、`ops/` 不在 allow——派工書 §2 說明過；真的需要就停下來回報。
-->

---

<!--
★ 2026-09-03 已通過整合驗收並提交（後端 `485910d`），撤包。原文保留供追溯。

一輪交付。`Wait-ManagedServiceStart`／`Suspend-WatchdogTask`／`Resume-WatchdogTask` 進 `ops/lib/Deployment.ps1`（注入 scriptblock）；`deploy.ps1` START 迴圈改看 SCM 狀態、STOP→健康檢查整段 try/finally 恢復 watchdog；self-test 18 → 20（19 PASS ＋ 總結行）。
子代理四件裁決全接受：docs 沒有「18 項」可改、計數歧義寫明、5.1 先 `Add-Type System.ServiceProcess`、finally 不吞例外。
Leader 重跑：self-test 5.1／7 各 20/20、`git diff -w` 只動三檔、audit 通過；**YC 第三次真跑一次過**（release `20260903053352999`：watchdog Ready→停用→五個 STOP→五個 START→五個 ✓→恢復→「✓ 部署完成」；外部探測全綠）。

## 生效中（已撤包）：BE-45　`deploy.ps1` START 不看 `nssm start` 的 exit code（改看 SCM 狀態、綠燈交給 `Assert-NewApplicationProcess`）＋ 部署期間暫停 `GreyGray-Watchdog` ＋ self-test 18 → 20 項

2026-09-03 13:02 正式機第二次重複部署：`GreyGray-Web-Storefront`（Next standalone）起得慢了幾秒，NSSM 回 `Unexpected status SERVICE_START_PENDING in response to START control.` 並 exit 非 0，`deploy.ps1:474` 的 `Invoke-Nssm start` 沒帶 `-AllowNonZeroExit` → 整支 throw，第五個服務沒起、健康檢查與 watchdog 重登記都沒跑（Leader 手動救回）。服務兩秒後就 Ready——是 flaky，不是壞。
同時查出 `deploy.ps1` 從沒在部署期間停用 watchdog（每 5 分鐘 `Start-Service` 任何不是 Running 的服務），STOP→START 的空窗撞到就會把舊 release 拉起來。今天差 15 秒。
派工書 `docs/41-後端第二十九波派工書.md` §0 把兩件事的證據與 YC 量到的 nssm 行為都列了；§1 A（等待函式進 lib、注入 scriptblock）、B（Suspend／Resume 放 try/finally）、C（self-test 兩項：AST 釘 `-AllowNonZeroExit` ＋ 假 scriptblock 走案例）。

★★ 最容易做錯的：① 成功條件只有 `Running`，不准用字串比對或固定 sleep；② `Resume-WatchdogTask` 一定在 `finally`；③ 不准 `GetNewClosure()`、不准 .NET Core 專屬 API（YC 是 5.1）；④ self-test 不准呼叫真的 `*-ScheduledTask`；⑤ 不對 YC 做任何事，Leader 自己上去真跑。

package: BE-45
doc: docs/41-後端第二十九波派工書.md
allow: ops/deploy.ps1
allow: ops/lib/Deployment.ps1
allow: ops/self-test.ps1

> `docs/` 全域放行。`ops/watchdog.ps1`、`ops/service-manifest.ps1`、`ops/lib/Process.ps1` 不在 allow——派工書 §2 說明過；真的需要就停下來回報。
-->

---

<!--
★ 2026-09-03 已通過整合驗收並提交（後端 `fb15ad6`），撤包。原文保留供追溯。

一輪交付。契約純新增 27 行、`docs/05` 加一列（M1a）、Host `MapPost` 抽成具名 `CreateSkuAsync`（`AdminSkuRequest` private→internal）、`OpenApiComponents` 加一列、6 條端點測試（201／404／404 格式／422＋冪等鍵 Abandoned／400／同 key 重播只建一個）。
Leader 重跑：build Debug 0/0、12 專案 **284** 條（278＋6）、`check-openapi` admin **30/30**；活體 Debug Host：201、404、400、422、**ReadOnly 403**（子代理明講沒有路由管線測試基礎、留給活體）、admin／storefront GET 都看得到新 SKU。
留下：admin 路由管線測試基礎（一次釘住 20 幾條角色守衛）；milestone gate 不驗 response 集合。

## 生效中（已撤包）：BE-44　修訂凍結契約——新增 `POST /v1/products/{productId}/skus`（建立 SKU）＋ Admin Host 端點（ADR-032）

使用者 2026-09-03 08:51 在正式機後台建了商品，卡在「這個商品還沒有 SKU」：契約只有 PATCH 既有 SKU、沒有新增端點，正式機沒有任何合法路徑建出第一個 SKU（開發機那幾個是 Leader 直寫 DB 種的）。
使用者拍板「不種，等正式做法」→ ADR-032：純新增一條 operation；Catalog 模組的 `CreateSkuAsync` port 早就在（`CatalogContracts.cs` 第 203 行、`CatalogServices.cs` 第 260 行），只缺 HTTP 路由、契約與 `docs/05` 那一列。
前端 FE-27（新增 SKU 表單＋批號進貨頁，`GreyGray_Platform-fe/docs/30`）等這一包撤包、Leader 把契約檔複製到前端樹之後才派。

★★ 最容易做錯的：① 契約只准純新增，`AdminSkuInput`／`AdminSku`／`AdminProductInput` 不動；② `docs/05` 表要加一列標 M1a，否則 `check-openapi` 里程碑模式會紅；
③ `OpenApiComponents.cs` 的 `IdempotentEndpoints` 要加新路徑，否則 live OpenAPI 少宣告 header；④ 404／422 照既有 `BffHttp.StatusFor`，不改 `BffHttp`；⑤ 一律 `-Configuration Debug`，dev Host 沒在跑、不要自己起。

package: BE-44
doc: docs/40-後端第二十八波派工書.md
allow: src/Hosts/GreyGray.Api.Admin/M1aEndpoints.cs
allow: src/Hosts/GreyGray.Api.Admin/OpenApiComponents.cs
allow: tests/

> `docs/api/openapi.admin.yaml`、`docs/05-API契約.md` 走 docs/ 全域放行。Catalog／Inventory 模組不在 allow——派工書 §0.3 說明過 port 已存在；若真的缺什麼，停下來回報。
-->

---

<!--
★ 2026-09-03 已通過整合驗收並提交（後端 `47ec987` → `34a8f20` → `12da09e` → `b56e1dd` → `9caa2b6` → `eab1898`；閘門補授權 `5e229e7`／`f102dfc`），撤包。原文保留供追溯。

七輪：一二輪通道腳本本體（`install-tunnel.ps1`、verify 多三項、`install-environment.ps1` 不碰共用通道、environment-self-test 改 UTF-8 讀）；
三輪 ingress validate 的 `--config` 位置錯、cloudflared 回 Incorrect Usage 但 exit 0 → 改順序＋要看到獨立一行 OK；四輪 `lib/Secrets.ps1` 的 `::Fill` 是 .NET 5+ → `::Create()`＋`GetBytes`、self-test 直接呼叫；
五輪 `Wait-ProcessTokensExit` Mandatory 拒收空陣列（乾淨機器）→ AllowEmptyCollection；六輪 nssm 2.24 `reset AppParameters` heap corruption → registry 清空＋驗證；七輪 `GetNewClosure()` 在「被另一支腳本呼叫」時看不到 script 函式 → 拿掉＋AST 把關。
後四個都是 Leader 在 YC 真跑 deploy.ps1 抓到的（-ValidateOnly／self-test 永遠碰不到），詳 `.dispatch/reports/BE-43.md` 與 GreyGray_PM/03 第三十一次。
YC：`GreyGray-Tunnel` Running、五個 app 服務 Running（release `20260902175515538`）、外部 `https://greygray.shop/v1/products` 200、`/v1/me` 401、後台登入頁 200；verify-environment 44 PASS／8 FAIL（全是既有 M-1 環境項目）。

## 生效中（已撤包）：BE-43　正式機 GreyGray 自己的 Cloudflare Tunnel（本機管理、進 repo）——`install-tunnel.ps1` ＋ verify 多一項 ＋ `install-environment.ps1` 不碰現有通道

使用者 2026-09-02 拍板「另起一個新的本機管理 tunnel，現有的完全不動」。Leader 已在 YC 上完成需要人授權的部分：`cloudflared tunnel login`（使用者點了）、
`tunnel create greygray`（id `7daa50aa-8b70-483a-a670-9d44ddc3499c`，憑證在 `C:\Users\moera\.cloudflared\`）、`route dns` 建了 `greygray.shop` 與 `admin.greygray.shop`。
這一包把剩下可重現的部分寫成腳本：複製憑證到 `C:\GreyGray\cloudflared\`、寫 `config.yml`（四條 ingress ＋ 404）、NSSM 登記 `GreyGray-Tunnel`（GreyGraySvc）。
`install-environment.ps1` 第 406-423 行會把現有 `cloudflared` 服務的帳號改掉並重啟——**那是使用者 Planner／Portfolio／Knowledge 共用的通道，必須拿掉那段**。

★★ 最容易做錯的：① 絕對不碰 `Cloudflared` 服務／`C:\ProgramData\cloudflared\token`／`CloudflaredWatchdog`；② 正式機只有 PowerShell 5.1；
③ 不在腳本裡做 login／create／route dns；④ 開發機沒裝 cloudflared，不要 winget install，`ingress validate` 留給 Leader 在 YC 跑；⑤ `install-tunnel.ps1` 要有 UTF-8 BOM（self-test 會斷言正式機腳本有 BOM）。

package: BE-43
doc: docs/39-後端第二十七波派工書.md
allow: ops/install-tunnel.ps1
allow: ops/verify-environment.ps1
allow: ops/install-environment.ps1
allow: ops/environment-self-test.ps1
allow: ops/self-test.ps1
allow: ops/lib/Secrets.ps1
allow: ops/lib/Deployment.ps1
allow: ops/deploy.ps1

> `docs/14-環境整備runbook.md` 走 docs/ 全域放行。
> 第五輪（2026-09-03 01:50，第四輪提交 `12da09e` 後 YC 第二次真跑）：migration、13 個 role 密碼、資料保護金鑰、ecpay.json 都過了，
> 停在 `deploy.ps1` 第 338 行 `Wait-ProcessTokensExit -Tokens $oldTokens`——乾淨機器上沒有舊行程，`$oldTokens` 是空陣列，
> `ops/lib/Deployment.ps1` 第 128 行 `[Parameter(Mandatory)][object[]]$Tokens` 拒收空集合（`ParameterArgumentValidationErrorEmptyArrayNotAllowed`，5.1 與 7 都會）。
> 這一輪：修掉它，並把 `deploy.ps1` 第 327 行以後「第一次部署、機器上什麼都沒有」這條路逐段審一遍（空集合進 Mandatory、StrictMode 下對 `$null` 取 `.Count`／屬性、
> `Select-Object -First 1` 拿到 `$null` 後續使用、`releases\` 不存在……），self-test 補「乾淨機器」案例。`ops/deploy.ps1` 只准為了這條路改，不准動 origin／secrets／NSSM 設定的語意。
> 第一輪交付後 Leader 補授權（2026-09-03）：`ops/self-test.ps1` 只准把 `install-tunnel.ps1` 加進正式機腳本 BOM 清單（一行）；
> `ops/environment-self-test.ps1` 第 8-13 行的 AST 改成跟 `self-test.ps1` 一樣以 UTF-8 讀入再 `ParseInput`（BE-42 修掉的同一個 big5 問題漏了這一支）。
> 第二輪驗收提交 `47ec987` 後 Leader 在 YC 真的跑了（2026-09-03 01:00）：`GreyGray-Tunnel` 起來了、17 支 migration 全過，但抓到兩個缺陷，同一個 session 續做——
> 第三輪：`ops/install-tunnel.ps1` 的 `Invoke-IngressValidate` 把 `--config` 放在 `ingress validate` 後面，cloudflared 回「flag provided but not defined」但 exit 0，腳本照印 PASS——
> 引數順序改成 `tunnel --config <cfg> ingress validate`，而且要看輸出有獨立一行 `OK` 才算過（只准改 `ops/install-tunnel.ps1`，用假的 cloudflared 替身證明三種情況）。
> 第四輪：`ops/deploy.ps1` 第一次真跑到 migration 之後炸在 `New-SecretPassword`——`ops/lib/Secrets.ps1` 第 37／56 行的 `RandomNumberGenerator::Fill` 是 .NET 5+ 才有，5.1（.NET Framework）沒有；
> 兩處改成 `::Create()` ＋ `GetBytes`，`ops/self-test.ps1` 加一條「目前 host 下 `New-SecretPassword`／`New-DataProtectionKey` 真的產得出來、後者解碼正好 32 bytes」讓 5.1 那趟會咬到。不准碰 `ops/deploy.ps1`。
-->

---

<!--
★ 2026-09-03 已通過整合驗收並提交（後端 `8e6f2c4`），撤包。原文保留供追溯。

ADR-031 前置落地：`BuildEcpayReturnUrl`（有設 `Storefront:PublicApiOrigin` 就用它，沒設維持 request-based——dev 模擬器靠它）＋ 8 條測試；
`deploy.ps1` Mandatory 兩個公開 origin（只注給 GreyGray-Storefront，5.1 語法、-ValidateOnly 也驗）；`build-frontends.ps1` `-FrontendRoot`／兩個 API base、
兩個 app 各自帶 NEXT_PUBLIC_* 建、建完 grep artifact；`Invoke-PnpmCommand -Environment` 用 `Remove-Item Env:` 還原；`self-test.ps1` AST 改 UTF-8 讀＋正式機腳本 BOM 斷言＋origin 負向測試；
CI 前端那步改 `-ValidateOnly`（這條分支的 frontend/ 是 FE-1 骨架）。測試 270 → **278**（Leader 自己重跑 Debug 1088 秒）；self-test 5.1／7 Leader 重跑都 exit 0。
三輪：第一輪指出 self-test／ci 會被 Mandatory 參數打壞（對）；第二輪查出 5.1 在 big5 機器讀無 BOM 檔會拆錯（既有問題，修 self-test 不加 BOM）與 CI 的 frontend/ 是骨架（改 -ValidateOnly）；第三輪收尾。
留下：三支 dev 腳本既無 BOM 也沒標 `#Requires 7`（斷言清單寫死，暫可）。

## 生效中（已撤包）：BE-42　串真綠界的前置——`ReturnURL` 可由 `Storefront:PublicApiOrigin` 設定 ＋ `deploy.ps1` 投遞公開 origin ＋ `build-frontends.ps1` 建另一棵樹並指定 API base

使用者 2026-09-02 拍板：串綠界＝**佈署到正式機 YC**、先接綠界公開測試商店；拓樸 ADR-031（前台與其 API 同主機名稱、`/v1/*` 分流，
網域 `greygray.shop`：前台根網域、後台 `admin.greygray.shop`）。通道後面 `Request.Host` 是 `127.0.0.1:5000`，現在的 `ReturnURL`（`GreyGray.Api.Storefront/M1aEndpoints.cs` 第 835 行）綠界打不到；
`deploy.ps1` 沒有投遞 `Storefront__PublicOrigin`（正式機付款端點會炸）；`build-frontends.ps1` 寫死建這棵樹的舊 `frontend/`、API base 會吃到 `.env.local` 的開發機位址。

★★ 最容易做錯的：① `Storefront:PublicApiOrigin` 是**選填**，沒設維持 request-based（dev 模擬器靠它）；② `deploy.ps1` 是正式機 **PowerShell 5.1** 跑的，不准用 7 的語法；
③ 不要在任何一棵樹真的跑 `next build`（前端樹 dev server 共用 `.next`）；④ dev Host 是 Release 在跑，一律 `-Configuration Debug`、不停 dev 行程；
⑤ 不碰 cloudflared／通道（Leader 另外處理）；⑥ 建置期環境變數用完要 `Remove-Item Env:`，不要用 `$null` 還原（#38 的坑）。

package: BE-42
doc: docs/38-後端第二十六波派工書.md
allow: src/Hosts/GreyGray.Api.Storefront/M1aEndpoints.cs
allow: tests/GreyGray.M1a.CheckoutOrdering.Tests/
allow: ops/start-dev-hosts.ps1
allow: ops/deploy.ps1
allow: ops/build-frontends.ps1
allow: ops/build.ps1
allow: ops/lib/Node.ps1
allow: ops/self-test.ps1
allow: .github/workflows/ci.yml

> `M1aEndpoints.cs` 只准動 `MapPayment` 那段與新的靜態方法；`docs/14-環境整備runbook.md` 走 docs/ 全域放行。
> 第一輪交付後 Leader 補授權（2026-09-03）：`ops/self-test.ps1` 第 102／126 行那兩處 `deploy.ps1 -ValidateOnly` 補上兩個 origin 參數；
> `.github/workflows/ci.yml` 第 71 行那個真建置補 `-FrontendRoot`／兩個 API base（用正式網址 `https://greygray.shop`／`https://admin.greygray.shop`）。
> `-ValidateOnly` 缺 URL 印出不 throw：Leader 接受。
-->

---

<!--
★ 2026-09-02 深夜已通過整合驗收並提交（後端 `3523904`），撤包。原文保留供追溯。

ADR-030 落地：契約 `shippingPolicy` 混合才必填（向下相容）、後端 `ResolveShippingPolicy` 純函式推導（純現貨 ShipSeparately／純預購 HoldUntilComplete）、
混合沒帶 422；壞 body 兩個 Host 兩個環境都 400 problem+json（`MalformedRequestExceptionHandler`，Admin 那份是複製、待搬 Platform）；#36 logout 刪 `gg_cart`、
不是你的車就換新車；#38 兩支腳本改 `Start-Process -Environment`。測試 253 → **270**（Leader 自己重跑 Debug 全套 794 秒）、check-openapi PASS。
Leader 活體（Release 換上、乾淨 shell）：`shippingPolicy: null` 匿名送出 → 401；壞 body → 400 problem+json；拿別人的車 → 200 空車＋新 cookie；三個行程 `Development`、預檢 204。
子代理先紅後綠兩段都對（把修法退回就重現派工書 §0.3 的 500／空 400；用 ADR-030 否決的「補預設值」做法會讓純現貨那條紅、混合漏帶被默默放行）。
留下：`MalformedRequestExceptionHandler` 兩份複製、契約與程式沒有 schema 級機械把關、`ThrowOnBadRequest` 影響面擴大（接受）。

## 生效中（已撤包）：BE-41　結帳 `shippingPolicy` 混合才必填、單一模式後端推導（#37 治本）＋ 壞 body 回 400 ＋ #36 後端側 ＋ #38 啟動腳本

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
-->

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

- **BE-53** 7-ELEVEN 超商取貨門市（ADR-038）：電子地圖選店票（Garnet、回傳一律 303、偽造不燒票）、結帳在冪等 work 內解票、migration `0020` 門市名稱地址快照、Admin 三個門市欄位、綠界模擬器 `/Express/map` 假地圖　·　2026-09-15 通過　·　`a4138a4`　·
  測試 340 → 382；第三十九波與前端 FE-33 平行；dev 環境 HTTP 旅程 32 項驗過；尚未部署（正式機要放 `Logistics:ECPay:*`），見 `.dispatch/reports/BE-53.md`
- **BE-52** 最愛清單後端（ADR-036）：migration `0019` `catalog.favorite`、`IStorefrontFavorites`、`GET/PUT/DELETE /v1/me/favorites`、商品列表／詳情 `isFavorited` 填真值　·　2026-09-15 通過　·　`46ec1c5`　·
  測試 332 → 340；第三十八波與前端 FE-31／FE-32 平行；dev 環境 HTTP＋SSR 旅程驗過；尚未部署，見 `.dispatch/reports/BE-52.md`
- **BE-48** 結帳成功後讓購物車退休（#44：正式機 log 連續 15 次 422「購物車已完成結帳」）＋ 取消已出貨的訂單不再讓事件卡住重試（#46，BE-47 的回歸）　·　2026-09-04 通過　·　`89bb5c5`　·
  測試 308 → 315；第六次部署 release `20260904015905167`；與前端 FE-29 同一輪；見 `.dispatch/reports/BE-48.md`
- **BE-47** 把「出貨」這個階段接起來（#43、#42）：交運時扣庫存並結轉銷貨成本（`StockCostAllocated`）、運費成本入帳、訂單品項轉 `Shipped`／`Completed`；migration `0018` 給保留單第三個狀態「已出庫」；`LedgerCoverageTests` 釘住「分錄表每個階段都要有人發、有人收」　·　2026-09-04 通過　·　`ac63775`　·
  測試 286 → 308；兩輪（子代理兩個否決都成立，補授權 `767dbd5`）；是使用者問「要不要再測一次完整流程」時 Leader 去查帳才查出來的；見 `.dispatch/reports/BE-47.md`
- **BE-46** Worker 掛上 Fulfillment 模組（#41：出貨單簽收後訂單永遠停在「準備出貨」）＋ 開機驗每個事件 handler 相依並點名 `IFulfillmentQuery` ＋ `WorkerCompositionTests`（同一個 `AddWorkerModules`、空 orderIds 重現炸點、負向對照）＋ Ordering 缺模組時例外講人話　·　2026-09-03 通過　·　`1d58dfa`　·
  測試 284 → 286；兩輪（E2E Worker 替身補齊設定）；第四次部署後訂單 ReadyToShip → Shipped；見 `.dispatch/reports/BE-46.md`
- **BE-45** `deploy.ps1` START 只看 SCM 狀態（`nssm start` 的 exit code 對「正在起」與「已在跑」都回非 0）＋ 部署期間暫停 `GreyGray-Watchdog`（Resume 在 finally）＋ self-test 18 → 20 項；`Wait-ManagedServiceStart`／`Suspend-WatchdogTask`／`Resume-WatchdogTask` 進 lib（注入 scriptblock）　·　2026-09-03 通過　·　`485910d`　·
  第六個 dry-run 碰不到的坑（13:02 第二次重複部署被 SERVICE_START_PENDING 打斷）；YC 第三次真跑一次過（release `20260903053352999`）；見 `.dispatch/reports/BE-45.md`
- **BE-44** 修訂凍結契約：新增 `POST /v1/products/{productId}/skus`（建立 SKU，ADR-032）＋ Admin Host 端點 ＋ `docs/05` 加列 ＋ OpenApiComponents ＋ 6 條端點測試（#39：正式機後台建商品後沒有任何合法路徑建出第一個 SKU）　·　2026-09-03 通過　·　`fb15ad6`　·
  測試 278 → 284；check-openapi admin 29/29 → 30/30；活體 201／404／400／422／ReadOnly 403；見 `.dispatch/reports/BE-44.md`
- **BE-43** 正式機 GreyGray 自己的 Cloudflare Tunnel 腳本（`install-tunnel.ps1`、verify 多三項、`install-environment.ps1` 不碰共用通道）＋ 正式機第一次部署抓到的四個 dry-run 盲點（ingress validate 假 PASS、`::Fill` 5.1 沒有、Mandatory 拒收空陣列、nssm `reset AppParameters` 崩潰、`GetNewClosure` 看不到 script 函式）　·　2026-09-03 通過　·　`47ec987`→`34a8f20`→`12da09e`→`b56e1dd`→`9caa2b6`→`eab1898`（七輪）　·
  YC 上線：`GreyGray-Tunnel`＋五個服務 Running，外部 `/v1/products` 200、`/v1/me` 401；self-test 從 13 項長到 18 項（乾淨機器、亂數產生器、Clear-NssmAppParameters、無 GetNewClosure），見 `.dispatch/reports/BE-43.md`
- **BE-42** 串真綠界的前置：`ReturnURL` 可由 `Storefront:PublicApiOrigin` 設定、`deploy.ps1` 投遞公開 origin、`build-frontends.ps1` 建另一棵樹並指定 API base、self-test 改 UTF-8 讀＋BOM 斷言、CI 改 -ValidateOnly　·　2026-09-03 通過　·　`8e6f2c4`　·
  測試 270 → 278；5.1／7 self-test 都 exit 0；三輪交付（第一輪指出 self-test／ci 會被打壞、第二輪查出 big5 讀無 BOM 檔的既有問題與 CI frontend 骨架），見 `.dispatch/reports/BE-42.md`
- **BE-41** 結帳 `shippingPolicy` 混合才必填、單一模式後端推導（#37 治本，ADR-030）＋ 壞 body 回 400 ＋ #36 後端側 ＋ #38 dev 啟動腳本　·　2026-09-02 通過　·　`3523904`　·
  規則的主人是後端：契約向下相容放寬、`ResolveShippingPolicy` 純函式；兩個 Host 兩個環境壞 body 都 400 problem+json；logout 刪 `gg_cart`、不是你的車就換新車；
  `Start-Process -Environment`。測試 253 → 270，見 `.dispatch/reports/BE-41.md`
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
