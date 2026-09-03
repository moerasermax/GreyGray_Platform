# 啟動 prompt

**生效中的派工（2026-09-03 傍晚，第三十五波）：後端 BE-47（把「出貨」這個階段接起來——交運時扣庫存並結轉銷貨成本、運費成本入帳、品項狀態轉 `Shipped`／`Completed`，#43／#42，`docs/43-後端第三十一波派工書.md`）。** 前端樹沒有生效中的派工。啟動 prompt 在下面「BE-47 的啟動 prompt」一節。

★ **BE-47**：使用者問「要不要再測一次完整流程」，Leader 先查正式機帳務 → **#43：出貨完全沒落帳也沒出庫**（`docs/02` 分錄表第 ⑦ 階段兩筆都沒人發沒人收；`inventory.lot` 還記著 60 件在倉庫、5 件永遠保留中）。修：Inventory 加「出庫」操作並訂閱 `ShipmentDispatched`（扣 on_hand＋reserved、reservation 轉已出庫、每筆 allocation 發 `StockCostAllocated`）；Ledger 補兩個 handler（DR 5100／CR 1300、DR 5200／CR 1100）；Ordering 品項轉 `Shipped`／`Completed`（#42）；加「分錄表每個階段都要有人發、有人收」的架構測試。

第三十四波 BE-46（`1d58dfa`：Worker 掛上 Fulfillment 模組——#41）已驗收撤包，第四次部署 release `20260903071555793` 後那則 `ShipmentDelivered` 事件處理完、訂單 ReadyToShip → Shipped。
第三十三波 BE-45（`485910d`：部署腳本）已撤包、YC 第三次真跑一次過。第三十二波 FE-28（`5b01314`，#40）已撤包並部署。第三十一波（BE-44 `fb15ad6`、FE-27 `56a221d`）已撤包並部署；**付款這條路 11:36 第一次在正式機用真的綠界測試站走通**。

★ **BE-46**：`Worker/Program.cs` 缺 `AddFulfillmentModule`（11 個模組、留了一句「之後再納入」），Ordering 的 `ShipmentDeliveredHandler` 對 `IFulfillmentQuery` 是 `Lazy` 相依 → 正式機 outbox `fulfillment.ShipmentDelivered.v1` 連炸 8 次 `ArgumentNullException`。修：模組清單抽成 `WorkerModules.AddWorkerModules`（加 Fulfillment）、Worker 開機驗每個 `IIntegrationEventHandler<T>` 與點名 `IFulfillmentQuery`、架構測試用同一個方法建容器＋空 orderIds 呼叫 `RecordShipmentDeliveredAsync` 重現炸點＋負向對照、Ordering 缺模組時的例外講人話。只動 Worker、Ordering.Core 一個方法、tests。

★ **BE-45**：`ops/lib/Deployment.ps1` 加 `Wait-ManagedServiceStart`（吃注入的 `-StartService`／`-GetStatus` scriptblock，成功條件只有 `Running`）與 `Suspend-WatchdogTask`／`Resume-WatchdogTask`；`deploy.ps1` START 迴圈改用它、`Invoke-Nssm start` 一律 `-AllowNonZeroExit`、STOP→健康檢查整段 try/finally 恢復 watchdog；self-test 第 19（AST ＋ 四案例）、20 項（假 scriptblock 四案例 ＋ AST 確認在 finally）。只動 `ops/` 三個檔。
★ **FE-28**：純函式 `subtotalPreview`（單價 × 數量，只用於顯示，ADR-033 唯一例外）＋ `BottomBarSummary`（「N 件 · 小計」）＋ `UnitPriceBlock`（資訊區單價）＋ `frontend/README.md` 例外註記；不動契約、`packages/*`、購物車／結帳頁。第三十波三段（BE-41、FE-26／BE-42、BE-43 七輪）已全部撤包；**正式機 YC 五個服務已上線（greygray.shop／admin.greygray.shop）**，使用者在後台建商品時撞到「沒有新增 SKU 的端點」，這一波就是補它。

★ **BE-44**：契約純新增一條 operation（Operator、Idempotency-Key、body `AdminSkuInput`、201 `AdminSku`、403／404／422，M1a）＋ `docs/05` 表加列 ＋ Admin Host `MapPost("/products/{productId}/skus")`（形狀照 PATCH `/skus/{skuId}`）＋ `OpenApiComponents` 清單 ＋ HTTP 層測試 ＋ `check-openapi` admin 30/30。
Catalog 模組的 `CreateSkuAsync` 早就在，不動模組。

★ **BE-43**：`ops/install-tunnel.ps1`（5.1 可跑、冪等：複製憑證到 `C:\GreyGray\cloudflared\`、寫 `config.yml` 四條 ingress ＋ 404、NSSM 登記 `GreyGray-Tunnel`）＋
`verify-environment.ps1` 多一項 ＋ `install-environment.ps1` **拿掉會動到現有 `cloudflared` 服務的那一段**（那是使用者其他應用共用的通道）。
tunnel `greygray`（`7daa50aa-…`）與兩筆 DNS Leader 已在 YC 上建好；`tunnel login`／`create`／`route dns` 不在腳本裡。

★ **FE-26**：跟上 ADR-030 的契約（`pnpm api:generate`、拿掉 `checkout/page.tsx` 的 `shippingPolicy!`）＋ #36 前端側（登出 `publishCart(null)`）
＋ FE-25 ⑦（`/login?next=` 亮 `next` 所屬分頁）＋ 結帳頁被帶去登入再回來保留已填內容（`sessionStorage`）。契約檔 Leader 已複製進前端樹，不要動。
★ **BE-42**：串真綠界的前置——`ReturnURL` 改成可由 `Storefront:PublicApiOrigin` 設定（通道／反向代理後面 `Request.Host` 是錯的）、
`deploy.ps1` 投遞兩個公開 origin、`build-frontends.ps1` 能指定另一棵樹的 `frontend/` 與各 app 的 API base。

★ **BE-41**：使用者親自走旅程第一張單就撞到 #37（純預購購物車結帳 500，比登入檢查還早）。問「哪一種是治本」後拍板「用第二種方式修」→
ADR-030：規則的主人是後端——契約 `shippingPolicy` 改成「混合才必填」（向下相容），後端混合沒帶回 422、單一模式依 line 組成推導。
併：壞 body 兩個環境都回 400 problem+json；#36 登出清 `gg_cart`＋「不是你的車」換新車；#38 兩支 dev 啟動腳本改 `Start-Process -Environment`。
★ dev Host 現在是 Release 在跑、使用者正在走旅程：子代理一律 `-Configuration Debug`，不准停 dev 行程。

---

## BE-47 的啟動 prompt（生效中）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端（.NET 10 modular monolith）。這一包動三個模組：Inventory、Ledger、Ordering，加 tests。

GG_PACKAGE=BE-47

開工前務必先讀：
  CLAUDE.md                         六條鐵則 ＋ 派工規則
  docs/43-後端第三十一波派工書.md     ★ 整份讀完：§0 事實（正式機帳務與庫存的實際數字、所有行號、現成可用的東西）＋ §1 必做 A～E ＋ §2 不要做 ＋ §5 可能寫錯的地方
  docs/02-事件與狀態機.md            第 161-186 行：M1 分錄對照表，這一包補的是第 ⑦ 階段兩筆
  src/Modules/Inventory/GreyGray.Modules.Inventory.Infra/StockReservationService.cs   第 18、163-200、428-435 行：釋放的完整形狀，出庫照它寫
  src/Modules/Ledger/GreyGray.Modules.Ledger.Infra/LedgerEventHandlers.cs             第 13-59 行：LedgerPostingService 的用法樣板
  tests/GreyGray.Architecture.Tests/WorkerCompositionTests.cs                          BuildConfiguration() 的 in-memory 設定樣板
  .dispatch/reports/README.md       ★ 自驗報告格式，以及「測試要分專案前景跑」

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-47.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。要跑的是：
     ops\build.ps1 -Configuration Debug（0 警告 0 錯誤）
     測試分專案前景跑（整支 ops\test.ps1 會超過 600 秒被移到背景然後整輪消失），基準 286 ＋ 你新增的，逐專案條數貼進報告
     bash .dispatch/audit-dispatch.sh
     輸出貼進報告。

★ 觸發點是「交運」（ShipmentDispatched）不是「簽收」；不要動 ShipmentDelivered 那條路（BE-46 剛修好）。
★ 一張出貨單可合併多張訂單、一張訂單可拆多張出貨單：冪等要靠 reservation 狀態自己擋，框架的 processed_message 擋不到。
★ 不新增 migration；quantity_available 是 generated column 不准寫；金額一律 Money。
★ 不碰正式機、不動 Fulfillment 模組、不動契約 YAML、不動前端與 ops/、不起停任何 Host。
★ 派工書 §5 列了六個可能寫錯的地方：撞到就停下來寫進報告問，不要自己換做法。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3（ACTIVE.md 的 allow 是機械執行的那份）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## BE-46 的啟動 prompt（已撤包，保留供參考；第二輪是 Leader 用同一個 session 補的裁決，見 `.dispatch/reports/BE-46.md`）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端（.NET 10 modular monolith）。這一包只動三處：Worker host 的組合根（src/Hosts/GreyGray.Worker/）、Ordering.Core 一個方法的錯誤訊息、tests/。

GG_PACKAGE=BE-46

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/42-後端第三十波派工書.md      ★ 整份讀完：§0 事實（正式機 outbox 失敗證據、Worker/Program.cs 行號、Ordering 的 Lazy 相依、為什麼測試沒抓到、不用資料庫的重現法）＋ §1 必做 A～E ＋ §2 不要做 ＋ §5 可能寫錯的地方
  src/Hosts/GreyGray.Worker/Program.cs                         第 59-83 行：模組清單與開機驗證區塊
  src/Modules/Ordering/GreyGray.Modules.Ordering.Infra/ModuleRegistration.cs   第 76-86 行：Lazy<IFulfillmentQuery?> 的由來（不動）
  tests/GreyGray.Architecture.Tests/ModuleCompositionRootTests.cs   第 79-90 行：in-memory configuration 的樣板
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-46.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。要跑的是：
     ops\build.ps1 -Configuration Debug（0 警告 0 錯誤）
     ops\test.ps1 -Configuration Debug（基準 284 ＋ 你新增的，逐專案條數貼進報告）
     dev 環境真的把 Worker 起一次（必做 B-4；用新 shell、看 log；只起你自己那個行程、用完停掉）
     bash .dispatch/audit-dispatch.sh
     輸出貼進報告。

★ 模組清單只有一份：WorkerModules.AddWorkerModules；Program.cs 與架構測試都呼叫它。
★ 開機驗證要點名 IFulfillmentQuery（Lazy 相依光解析 handler 抓不到）；負向對照那條測試要真的 throw。
★ 不動 Admin／Storefront 的模組清單、不動 Ordering.Infra 的 Lazy、不動 Fulfillment、不動契約、不動 ops/、不碰正式機。
★ 一律 -Configuration Debug；dev 的 Host 不准停。
★ 派工書 §5 列了五個可能寫錯的地方：撞到就停下來寫進報告問，不要自己換做法。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3（ACTIVE.md 的 allow 是機械執行的那份）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## BE-45 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端（.NET 10 modular monolith）——但這一包只動 ops/ 底下三支 PowerShell：deploy.ps1、lib/Deployment.ps1、self-test.ps1。正式機 YC 是 Windows PowerShell 5.1，沒有 pwsh。

GG_PACKAGE=BE-45

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/41-後端第二十九波派工書.md    ★ 整份讀完：§0 事實（13:02 正式機怎麼死的、deploy.ps1 行號、YC 量到的 nssm 行為、watchdog 空窗）＋ §1 必做 A～D ＋ §2 不要做 ＋ §5 可能寫錯的地方
  ops/self-test.ps1                第 268-330 行：AST 把關與最小重現的樣板（新兩項照這個寫）
  ops/lib/Deployment.ps1           Clear-NssmAppParameters：注入 scriptblock 的樣板
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-45.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。要跑的是：
     powershell.exe -NoProfile -ExecutionPolicy Bypass -File ops\self-test.ps1   （5.1，20/20）
     pwsh -NoProfile -File ops\self-test.ps1                                      （7，20/20）
     bash .dispatch/audit-dispatch.sh
     輸出貼進報告。不跑 build.ps1、不起任何 Host、不碰 YC。

★ 成功條件只有 SCM 狀態 Running：nssm start 的 exit code 對「正在起」與「已在跑」都回非 0，不能當訊號；不准固定 sleep、不准字串比對當成功。
★ Resume-WatchdogTask 一定在 finally（失敗的部署也要把 watchdog 還回去）。
★ 不准 GetNewClosure()（self-test 第 18 項會擋）、不准 .NET Core 專屬 API；self-test 不准呼叫真的 *-ScheduledTask。
★ 派工書 §5 列了五個可能寫錯的地方：撞到就停下來寫進報告問，不要自己換做法。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3（ACTIVE.md 的 allow 是機械執行的那份）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## FE-28 的啟動 prompt（已撤包，保留供參考；第二輪是 Leader 用同一個 session 補的裁決，見 `.dispatch/reports/FE-28.md`）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray Platform 前端（Next.js 15 App Router、pnpm workspace：apps/storefront、apps/admin、packages/api-client、packages/ui）。這一包只動前台 apps/storefront 的商品詳情頁。

GG_PACKAGE=FE-28

開工前務必先讀：
  CLAUDE.md                        前端四條 ＋ 派工規則
  docs/31-前端第十七波派工書.md      ★ 整份讀完：§0 事實（AddToCartPanel／page.tsx 行號、PriceDisplay、規則原文、測試基礎）＋ §1 必做 A～E ＋ §2 不要做 ＋ §6 可能寫錯的地方
  docs/00-decisions.md             ADR-033（前端「不做金額運算」的唯一例外：商品頁小計預覽）
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-28.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。要跑的是：pnpm --recursive typecheck、pnpm --recursive test（基準 436 ＋ 你新增的，逐專案條數貼進報告）。build 由 Leader 停 dev server 後跑，你不跑。

★ 小計只准商品頁用（subtotalPreview 全 repo 一個呼叫端）；購物車／結帳／訂單頁的金額仍全部來自後端，一個字不動。
★ 不動契約、packages/ui、packages/api-client、admin app；page.tsx 的 SSR 內容不動（單價放 AddToCartPanel 頂端）。
★ 沒有 jsdom：「5 件 → NT$300」靠純函式 ＋ 純呈現元件（BottomBarSummary／UnitPriceBlock）用 renderToStaticMarkup 測。
★ dev server 由 Leader 管：不停、不起、不跑 next build。
★ 派工書 §6 列了三個可能寫錯的地方：撞到就停下來寫進報告問，不要自己換做法。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3（ACTIVE.md 的 allow 是機械執行的那份）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## FE-27 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray Platform 前端（Next.js 15 App Router、pnpm workspace：apps/storefront、apps/admin、packages/api-client、packages/ui）。這一包只動後台 app 與 api-client。

GG_PACKAGE=FE-27

開工前務必先讀：
  CLAUDE.md                        前端四條 ＋ 派工規則
  docs/30-前端第十六波派工書.md      ★ 整份讀完：§0 事實（商品頁、SkuEditDrawer、api-client 現況、契約新舊端點）＋ §1 必做 A～D ＋ §2 不要做 ＋ §6 可能寫錯的地方
  docs/00-decisions.md             ADR-032（修訂凍結契約：純新增 POST /v1/products/{productId}/skus；後台補新增 SKU 與批號進貨）
  docs/api/openapi.admin.yaml      ★ Leader 從後端樹複製來的，不要動；/v1/products/{productId}/skus（新）與 /v1/lots（M2 既有）
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-27.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。要跑的是：pnpm api:generate（貼 diff --stat：types.admin.ts 有、types.storefront.ts 零 diff）、
     pnpm --recursive typecheck、pnpm --recursive test（基準 410 ＋ 你新增的，逐專案條數貼進報告）。build 由 Leader 停 dev server 後跑，你不跑。

★ 型別一律從重生的 types.admin.ts 拿（S['AdminSkuInput']、S['AdminSku']、S['Lot']、paths['/v1/lots']['post'] 推導 body）；不准手寫請求型別。
★ 不改 docs/api/*.yaml、docs/05；不動 storefront app；不加側邊欄新頁面（進貨做在商品頁 SKU 列的抽屜）；不在前端算庫存或金額。
★ NT$ → amountMinor 的轉換照 SkuEditDrawer 現貨標價既有的 helper；冪等鍵照既有 createProduct／updateSku 呼叫端的 MutationOptions 做法。
★ dev server 由 Leader 管：不停、不起、不跑 next build；需要活體才能驗的事寫進報告交給 Leader。
★ 派工書 §6 列了三個可能寫錯的地方：撞到就停下來寫進報告問，不要自己換做法。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3（ACTIVE.md 的 allow 是機械執行的那份）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## BE-44 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端。.NET 10 模組化單體，14 個限界上下文，EF Core + Npgsql。

GG_PACKAGE=BE-44

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/40-後端第二十八波派工書.md    ★ 整份讀完：§0 事實（契約現況、模組 port、Host 樣板、OpenAPI 元件清單）＋ §1 四個必做 ＋ §2 不要做的事 ＋ §6 可能寫錯的地方
  docs/00-decisions.md             ADR-032（修訂凍結契約：純新增 POST /v1/products/{productId}/skus）
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-44.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。要跑的是：dotnet build -c Debug（0 警告 0 錯誤）、
     ops\test.ps1 -Configuration Debug（12 個專案全過；整套跑不動就前景逐一跑）、
     ops\check-openapi.ps1 -Configuration Debug（admin 從 29/29 變 30/30）、pwsh ops\self-test.ps1 exit 0。輸出貼進報告。

★ 一律 -Configuration Debug；不碰 Release bin（那是部署用的）。dev Host 目前沒在跑，不要自己起 Host。
★ 契約只准純新增一條 operation（派工書 §1 必做 1 的 YAML 逐字）；AdminSkuInput／AdminSku／AdminProductInput 一個字不動；docs/05 那張表加一列標 M1a。
★ Catalog 模組不動（CreateSkuAsync 早就在）；不動前端、ops/、db/、docs/00-decisions.md。
★ 404／422 的映射照既有 BffHttp.StatusFor；不改 BffHttp。不捏造預設重量／尺寸。
★ 派工書 §6 列了三個可能寫錯的地方：撞到就停下來寫進報告問，不要自己換做法。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## BE-43 的啟動 prompt（已撤包，保留供參考；第三～七輪是 Leader 用同一個 session 補的指示，見 `.dispatch/reports/BE-43.md`）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端。.NET 10 模組化單體，14 個限界上下文，EF Core + Npgsql。這一包只有 ops 腳本與文件，不碰 C#。

GG_PACKAGE=BE-43

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/39-後端第二十七波派工書.md    ★ 整份讀完：§0 事實（正式機現況、cloudflared 本機管理做法）＋ §1 四個必做 ＋ §2 不要做的事
  docs/00-decisions.md             ADR-031（拓樸與主機名稱：greygray.shop 根網域、admin.greygray.shop）
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-43.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。這一包沒有 C# 改動，不必跑 12 個測試專案；
     要跑的是：三支腳本 Parser::ParseFile（5.1 與 7 各一次）、
     powershell.exe -NoProfile -File ops\environment-self-test.ps1 OVERALL PASS、
     pwsh -NoProfile -File ops\self-test.ps1 exit 0（它會斷言正式機腳本有 BOM——install-tunnel.ps1 是正式機腳本，要有 UTF-8 BOM）。

★ 正式機只有 Windows PowerShell 5.1：不用 ??、?.、三元運算子、Start-Process -Environment、#Requires -Version 7。
★ 絕對不碰現有的 Cloudflared 服務、C:\ProgramData\cloudflared\token、CloudflaredWatchdog（使用者其他應用共用）。
★ install-tunnel.ps1 不做 tunnel login／create／route dns（Leader 手動，已完成：tunnel greygray、兩筆 CNAME）。
★ 不在開發機真的登記服務或跑 cloudflared；-ValidateOnly 印出將寫入的 config.yml。開發機沒裝 cloudflared——
  ingress validate 那一步在報告裡寫「開發機無 cloudflared，留給 Leader 在 YC 跑」，不要自己 winget install。
★ 不要動 deploy.ps1、src/、docs/api/、docs/00-decisions.md。

★ BOM：install-tunnel.ps1 要有 UTF-8 BOM（正式機腳本）；其他檔維持原本狀態。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## FE-26 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray Platform 前端。pnpm monorepo：apps/storefront、apps/admin（Next.js App Router）、packages/api-client（openapi-typescript 產生型別）。

GG_PACKAGE=FE-26

開工前務必先讀：
  CLAUDE.md                         四條鐵則 ＋ 派工規則
  docs/29-前端第十五波派工書.md      ★ 整份讀完：§0 事實 ＋ §1 四個必做 ＋ §2 不要做的事
  docs/00-decisions.md              ADR-030（規則的主人是後端，前端不補預設值）
  .dispatch/reports/README.md       ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-26.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。pnpm --recursive typecheck 與 pnpm --recursive test
     在前景跑完、貼原文（基準 376 條，總數只增不減）。
     ★ 前台／後台 dev server 正在跑（5002／5003）會 hot reload：不要停它們、不要跑 next build。

★ 必做 A：pnpm api:generate 重生型別；拿掉 checkout/page.tsx 第 152 行的 shippingPolicy!；
  types.admin.ts 預期零 diff，有 diff 就停下來回報。cartRules.ts 的判斷不動、不補預設值。
★ 必做 B：登出成功後 publishCart(null)。
★ 必做 C：/login、/register 帶 ?next= 時分頁列亮 next 所屬的分頁（用 safeNext），沒帶就亮「我的」；
  tabs.test.ts 既有斷言一條都不刪。
★ 必做 D：結帳頁 401 導向登入前把五個欄位存 sessionStorage（鍵含 cart id），回來時還原、送出成功就刪；
  純函式＋測試；不用 localStorage、不存整份購物車。

★ 不要改 docs/api/*.yaml、docs/05（Leader 從後端樹複製來的）、.env.local、packages/api-client 手寫的部分。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## BE-42 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端。.NET 10 模組化單體，14 個限界上下文，EF Core + Npgsql。

GG_PACKAGE=BE-42

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/38-後端第二十六波派工書.md    ★ 整份讀完：§0 事實 ＋ §1 四個必做 ＋ §2 不要做的事
  docs/00-decisions.md             ADR-031（拓樸與主機名稱是拍板過的，不要換）
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-42.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。12 個測試專案逐一在前景個別執行。
     不要用 dotnet test，用 ops\test.ps1。
     ★ dev 三個 Host ＋ 模擬器用 Release 跑著、使用者隨時會用：建置與測試一律
       -Configuration Debug，不要停掉或重啟任何 dev 行程，不要碰 D:\GreyGray\。

★ 必做 1：ReturnURL 抽成 BuildEcpayReturnUrl(configuration, request)——Storefront:PublicApiOrigin
  有設就用它（絕對 http(s)、去結尾斜線、壞值丟例外含鍵名），沒設維持 request 的 scheme/host。
  純函式測試放 tests/GreyGray.M1a.CheckoutOrdering.Tests/。start-dev-hosts.ps1 加選填 -StorefrontPublicApiOrigin。
★ 必做 2：deploy.ps1 加 Mandatory 的 -StorefrontPublicOrigin／-StorefrontPublicApiOrigin，只注給
  GreyGray-Storefront；-ValidateOnly 也驗；正式機是 Windows PowerShell 5.1，不准用 7 的語法。
★ 必做 3：build-frontends.ps1 加 -FrontendRoot／-StorefrontApiBaseUrl／-AdminApiBaseUrl，兩個 app 各自建、
  各自帶 NEXT_PUBLIC_API_BASE_URL 與 NEXT_PUBLIC_USE_MOCK=0，建完 grep artifact 確認吃到值且沒有 127.0.0.1；
  環境變數用完 Remove-Item Env:（不要用 $null 還原）。build.ps1 -Publish 傳下去。
★ 必做 4：docs/14 加「部署五個 app 服務」與「開發機怎麼產 artifact」。

★ 不要在任何一棵樹真的跑 next build；不要碰 cloudflared／通道；不要動 src/Modules/、src/Platform/、
  docs/api/、docs/05、docs/00-decisions.md、frontend/、install-*.ps1、stop-dev-environment.ps1、verify-environment.ps1。

★ BOM：維持每個檔案原本的狀態。用 Python 寫檔時不要用 encoding='utf-8-sig'。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §3。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## BE-41 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端。.NET 10 模組化單體，14 個限界上下文，EF Core + Npgsql。

GG_PACKAGE=BE-41

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/37-後端第二十五波派工書.md    ★ 整份讀完：§0 事實 ＋ §1 五個必做 ＋ §2 不要做的事
  docs/00-decisions.md             ADR-030（這一包的形狀是拍板過的，不要換）
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-41.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。12 個測試專案逐一在前景個別執行。
     不要用 dotnet test，用 ops\test.ps1。
     ★ dev 三個 Host 現在用 Release 跑著、使用者正在上面走旅程：建置與測試一律
       -Configuration Debug，不要停掉或重啟任何 dev 行程，不要碰 D:\GreyGray\。

★ 形狀是拍板過的（ADR-030）：規則的主人是後端。契約 shippingPolicy 改成
  「Cart.hasMixedModes 為 true 才必填，否則可省略或 null」；後端混合沒帶 → 422
  checkout.shipping-policy-required；單一模式忽略客人的值、依 line 組成推導
  （純現貨 ShipSeparately、純預購 HoldUntilComplete）。
  CheckoutCompleted.ShippingPolicy 與 Order.ShippingPolicy 維持不可為 null，
  Ordering 一行不動、不做 migration。

★ 壞掉的 request body：Development 與 Production 都要 400 + application/problem+json
  （platform.malformed-request），證明要走真管線（tests/GreyGray.EndToEnd.Tests 的 StartHost 模式）。

★ #36 後端側：/auth/logout 同時刪 gg_cart；GET /v1/cart 與 POST /v1/cart/lines 拿到
  checkout.cart-not-found 且帶著 cookie 就換新車（服務層的 not-found 語意不要改）。

★ #38：ops/start-dev-hosts.ps1 與 ops/start-dev-ecpay-simulator.ps1 改 Start-Process -Environment，
  刪掉改父行程再還原那一段，檔頭加 #Requires -Version 7.4。證明用用完即丟的腳本，不進 repo。

★ 不要改前端 worktree、src/Modules/Ordering/、src/Modules/Payment/、src/Platform/、
  Directory.Packages.props、ops/deploy.ps1、install-dev-environment.ps1、stop-dev-environment.ps1。
★ 不要新開測試專案。

★ BOM：維持每個檔案原本的狀態。用 Python 寫檔時不要用 encoding='utf-8-sig'。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。
  前幾包就是這樣擋下 Leader 寫錯的段落，而且每次都對。

檔案所有權：見派工書 §3。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## 上一波（第二十九波，已撤包）的啟動 prompt 保留在下面供參考格式

★ **BE-40**：使用者拍板「先做 dev 模擬付款，但要能隨時換回 adapter」。Leader 的裁決（ADR-029）是
**假的是綠界的伺服器**——獨立行程的模擬器，`EcpayGateway` 與回呼判斷一個位元組不動，dev 只把
`Payment:ECPay:CheckoutUrl`／`CreditDetailUrl` 指過去；正式碼唯一新增 `AllowNonEcpayEndpoints` 守衛。
派工前查證出真缺陷 **#33**（簽章沒有 `ClientBackURL`，付完款沒有路回商店），併入。

★ **FE-25**：「我的」一直沒有家（分頁與首頁頭像都指 `/orders`、`/wallet` 零入口、全站沒有登出），
登入後一律被丟到 `/orders`、結帳送出撞 401 沒有去登入的路。做 `/me`、`?next=` 回跳、付款結果頁有限次重查。
兩個追蹤項 Leader 查證後不用改碼（`GET /v1/cart` 不寫 DB；cookie HttpOnly 前端讀不到）。

★ 2026-09-02 使用者從前台測整段下單時撞到 **#30**：加完購物車之後沒有任何按鈕
回得去，只能按上一頁；首頁上連「購物車」三個字都沒有。根因是當初就沒排——
八包裡只有 FE-6「後台：殼」，**前台從來沒有這一包**。

★ **2026-09-02 使用者第二次撞到同一件事（#32）**：FE-23 撤包後接著測，問「昨天提到的
加入購物車後沒有按鈕可以返回首頁，這個沒優化嗎？」——答案是**沒有**。FE-23 修的是有分頁列
的頁面；使用者撞到的三頁（商品詳情、購物車、結帳）正好是分頁列刻意隱藏的三頁，一頁都沒修到。
Leader 的錯：只寫了「不要顯示」，沒給替代出口。**已由 FE-24 修掉（`41e9fd7`），子代理五個自主判斷全對。**

已通過整合驗收並撤包：

| 包 | 內容 | commit |
|---|---|---|
| BE-36 | `POST /v1/shipments` 加模組層冪等，堵掉重複出貨單（#23） | `e2b4bf1` |
| BE-37 | 冪等錯誤碼對齊契約（#24） | `f8e357f` |
| BE-38 | M2 批發進貨與批號列表，前台第一次有東西可以買 | `f191120` |
| FE-23 | 前台的殼：底部分頁列 ＋ 購物車徽章（#30），測試 150 → 248 條 | `5b2db68` |
| FE-24 | 三頁的頂部列 ＋ 提示加「查看購物車」＋ 修 #31（#32），測試 248 → 317 條 | `41e9fd7` |

★ **2026-09-01 真 Chrome 全站逐頁複驗查出四個新缺陷**（詳見 `GreyGray_PM/00-進度總表.md`）：

| # | 內容 | 在哪棵樹 |
|---|---|---|
| ~~#29~~ | ~~後台首頁的財務數字是寫死的假資料~~ | ✅ **FE-21 已修**（`ebe074c`），測試 135 → 146 條 |
| ~~#26~~ | ~~預購商品從商品路徑永遠買不到~~ | ✅ **BE-39 已修**（`d43dc1d`），測試 227 → 241 條 |
| ~~#27~~ | ~~商品卡收藏心點了會跳到商品頁~~ | ✅ **FE-22 已修**（`6f3f380`） |
| ~~#28~~ | ~~前台必填欄位只有視覺上的 `*`~~ | ✅ **FE-22 已修**（同上），前端測試 135 → 150 條 |

★ **2026-09-01 真 Chrome 複驗查出的四個缺陷已全數修正並驗收撤包。**
剩下的 **#25 仍未解**（卡 E3），是唯一還擋著「一條完整流程走得完」的技術缺陷。

★ **#25 仍未解**：任何會解析 Payment 模組的端點都在 DI 階段炸掉——
storefront `GET /v1/cart`、`POST /v1/cart/lines`，**以及 admin `GET /v1/orders/{orderId}`**
（後台訂單列表點得進去、點開任一張就 500，已在真瀏覽器裡驗證）。卡在 E3。

**這一波的派工書：後端 `docs/36-後端第二十四波派工書.md`（BE-40）、前端 `docs/28-前端第十四波派工書.md`（FE-25）。
下一份後端派工書是 `docs/37-後端第二十五波派工書.md`；下一份前端派工書是 `docs/29-前端第十五波派工書.md`。**

★ **`pnpm lint` 在這個 workspace 根本跑不起來**（沒裝 ESLint，`next lint` 已棄用且互動式；
對沒碰過的專案跑也是 exit 1，Leader 已用對照組確認）。**不要再把它列進任何自驗項。**

---

## BE-40 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端。.NET 10 模組化單體，14 個限界上下文，EF Core + Npgsql。

GG_PACKAGE=BE-40

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/36-後端第二十四波派工書.md    ★ 整份讀完：§0 事實 ＋ §1 五個必做 ＋ §2 不要做的事
  docs/00-decisions.md             ADR-029（這一包的形狀是拍板過的，不要換）
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-40.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。12 個測試專案逐一在前景個別執行。
     不要用 dotnet test，用 ops\test.ps1。
     ★ dev 環境開著時 Host 會鎖住 bin\Debug，建置與測試都用 -Configuration Release，
       不要停掉或重啟任何 dev 行程。

★ 形狀是拍板過的：假的是綠界的伺服器（獨立行程的模擬器），不是我們的 adapter。
  EcpayGateway 與回呼判斷一個位元組都不動；正式碼唯一新增的是
  Payment:ECPay:AllowNonEcpayEndpoints 守衛（預設 false）。

★ #33 併入：簽章加 ClientBackURL，由 Host 用 Storefront:PublicOrigin 組出
  {PublicOrigin}/payment/result?orderId=…。缺設定在付款端點明確炸，不要預設 localhost。
  不做 OrderResultURL。

★ 不要動 AllowSimulatedPaid、不要讓模擬器送 SimulatePaid=1。
★ 不要改 docs/api/*.yaml、docs/05、frontend/、Directory.Packages.props、ops/deploy.ps1。
★ 不要新開測試專案；測試放 tests/GreyGray.M1a.PaymentLedger.Tests/。
★ 模擬器只 ProjectReference Payment.Infra，簽章走 InternalsVisibleTo 重用，不要再抄一份。
★ 模擬器的 MerchantId 不是 DEVFAKE 開頭就拒絕啟動；TradeNo 以 DEVFAKE 開頭、共 20 字。

★ BOM：維持每個檔案原本的狀態。用 Python 寫檔時不要用 encoding='utf-8-sig'。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。
  前幾包就是這樣擋下 Leader 寫錯的段落，而且每次都對。

檔案所有權：見派工書 §4。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## FE-25 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray Platform 前端。Next.js 15 ＋ React 19 ＋ Tailwind 4，pnpm workspace。

GG_PACKAGE=FE-25

開工前務必先讀：
  CLAUDE.md                        前端四條 ＋ 派工規則
  docs/28-前端第十四波派工書.md      ★ 整份讀完，§0 事實 ＋ §1 A～E
  .dispatch/reports/FE-24.md       上一包的報告，尤其「我發現但沒做的事」④⑦
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-25.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。全部前景跑完再交付。

★ 三件事：A 新頁 /me（含登出）＋ B「我的」改指 /me ＋ C 登入後回到原頁（?next=，只收站內路徑）
  ＋ D 付款結果頁有限次自動重查。E 兩個追蹤項已由 Leader 查證為不成立／前端做不到，不改碼，只寫進報告。

★ tabs.test.ts 那兩段釘住「我的 → /orders」的斷言允許改（這一包就是要改它），總條數只能增不能減。
  基準 317 條，交付時必須變多。

★ 不要跑 next build——dev server 在跑，共用 .next，會把整站打成 500。typecheck 與 vitest 就夠。

★ 這個 workspace 沒有 jsdom／@testing-library 也沒安裝，不要為了測試加相依套件、
  不要動 pnpm-lock.yaml 或任何 package.json。測試放 apps/storefront。pnpm lint 跑不起來，不要列。

★ 不要動後端、docs/api/*.yaml、packages/*、globals.css、TAB_BAR_RULES、TOP_BAR_RULES、
  useCartItemCount.ts、StorefrontTabBar.tsx、PageTopBar.tsx。

★ 環境提示：dev 全開（前台 5002、後台 5003、API 5000／5001）。不要重啟或停掉任何 dev server。
  後端同時有另一包（BE-40）在另一棵樹做付款模擬器，跟你無關，不要等它。

檔案所有權：見派工書 §3。
docs/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

★ 如果你發現派工書裡有做不到、或方向錯誤的要求，停下來講清楚，不要硬做也不要假裝通過。
  前幾包就是這樣擋下 Leader 寫錯的段落，而且每次都對。這跟「不准自己擴大範圍」是兩件事。

你不可以自己宣告通過。交付完就停。
```

---

## FE-24 的啟動 prompt（已撤包，保留供參考）

三頁的頂部列（商品詳情 · 購物車 · 結帳）＋ 提示加「查看購物車」＋ 順手修 #31。
修「現在卡在哪」**#32**：FE-23 撤包後使用者接著測，問「昨天提到的加入購物車後沒有按鈕
可以返回首頁，這個沒優化嗎？」——答案是沒有。FE-23 修的是有分頁列的頁面，
使用者撞到的三頁正好是分頁列刻意隱藏的三頁。Leader 的錯：只寫了「不要顯示」，沒給替代出口。

★ 派工前已先 commit 閘門檔（這次有照做），子代理收工沒有再撞 stop gate。
★ 2026-09-02 已驗收撤包（`41e9fd7`）。子代理五個自主判斷全對（詳見 `.dispatch/ACTIVE.md` 的撤包註記與
  `.dispatch/reports/FE-24.md`「我發現但沒做的事」）；build 由 Leader 停掉 dev server 後跑，EXIT=0。

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray Platform 前端。Next.js 15 ＋ React 19 ＋ Tailwind 4，pnpm workspace。

GG_PACKAGE=FE-24

開工前務必先讀：
  CLAUDE.md                        前端四條 ＋ 派工規則
  docs/27-前端第十三波派工書.md      ★ 整份讀完，§0 事實 ＋ §1 要做什麼
  .dispatch/reports/FE-23.md       上一包的報告，尤其「我發現但沒做的事」①⑤
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-24.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。全部前景跑完再交付。

★ 使用者撞到的是：商品詳情、購物車、結帳這三頁沒有任何出口。修法是使用者拍板的
  「這三頁加一條頂部列」（左返回、中頁名、商品頁右邊帶徽章的購物車圖示），見 §1。

★ 最重要的一條結果：每一條路由恰好有一種殼（分頁列或頂部列），要用原始碼掃描的
  測試釘住（照 bottomActionBarCollision.test.ts 的做法），不要再抄一份清單。

★ 返回在直接打網址進來時也要有地方去（商品→/、購物車→/、結帳→/cart），用測試釘住。

★ 不要動 globals.css:88 那行 body 留白——上一包 Leader 寫錯、子代理擋下來的 144px 教訓。

★ 不要跑 next build——dev server 在跑，共用 .next，會把整站打成 500。typecheck 與 vitest 就夠。

★ 這個 workspace 沒有 jsdom／@testing-library 也沒安裝，不要為了測試加相依套件、
  不要動 pnpm-lock.yaml 或任何 package.json。測試放 apps/storefront（有 vitest），
  packages/ui 沒有，寫在那裡永遠不會被執行。基準 248 條，交付時條數必須變多。

★ pnpm lint 在這個 workspace 根本跑不起來（沒裝 ESLint），不要列進自驗、也不要假裝通過。

★ Toast 後台也在用，新的動作 prop 必須是選填、不改既有畫面。

★ 不要動後端、不要動 docs/api/*.yaml、不要做全站頂部頁首。

★ 環境提示：dev 全開（前台 5002、後台 5003、API 5000／5001）。
  Host 是用 D:\GreyGray\start-dev-hosts-with-fake-ecpay.ps1 起的（注入了開發用假綠界設定）。
  不要重啟或停掉任何 dev server。

檔案所有權：見派工書 §3。
docs/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

★ 如果你發現派工書裡有做不到、或方向錯誤的要求，停下來講清楚，不要硬做也不要假裝通過。
  上一包就是這樣擋下 Leader 寫錯的一段，而且是對的。這跟「不准自己擴大範圍」是兩件事。

你不可以自己宣告通過。交付完就停。
```

---

## FE-23 的啟動 prompt（已撤包，保留供參考）

補「前台的殼」——底部分頁列，修「現在卡在哪」**#30**。
使用者 2026-09-02 從前台測整段下單時撞到：加完購物車之後沒有任何按鈕回得去，
只能按上一頁；首頁上連「購物車」三個字都沒有。
根因是當初就沒排：八包裡只有 FE-6「後台：殼」，前台從來沒有這一包。

```
GG_PACKAGE=FE-23

專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray Platform 前端。Next.js 15 ＋ React 19 ＋ Tailwind 4，pnpm workspace。

開工前務必先讀：
  CLAUDE.md                        前端四條 ＋ 派工規則
  docs/26-前端第十二波派工書.md      ★ 整份讀完，§0 事實 ＋ §1 要做什麼
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

你要做的事：補「前台的殼」——底部分頁列（首頁 · 開團 · 購物車 · 我的），
購物車帶數量徽章。

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-23.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。全部前景跑完再交付。

★ 最容易做錯的是與既有固定底部列打架。BottomActionBar 是 fixed bottom-0、高 72px，
  已經有三頁在用：商品詳情的 AddToCartPanel、購物車頁、結帳頁。
  那三頁不要顯示分頁列，而且要有測試釘住這份清單。

★ 這個 workspace 沒有 jsdom／@testing-library 也沒安裝，不要為了測試加相依套件、
  不要動 pnpm-lock.yaml。把判斷抽成純函式來測。
  測試要放在 apps/storefront（有 vitest），packages/ui 沒有，寫在那裡永遠不會被執行。

★ pnpm lint 在這個 workspace 根本跑不起來，不要列進自驗、也不要假裝通過。

★ 徽章拿不到資料時不要顯示 0——「0 件」與「不知道幾件」是兩件事。

★ 加入購物車成功後徽章要立刻更新，不能等重新整理。

★ 不要動後端、不要動 docs/api/*.yaml、不要做頂部頁首。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

★ 如果你發現派工書裡有做不到、或方向錯誤的要求，停下來講清楚，不要硬做也不要假裝通過。

你不可以自己宣告通過。交付完就停。
```

### ★ 這一包的兩個裁決與一次「Leader 寫錯被子代理擋下」

1. **派工書 §1「要在 layout 加底部留白」是錯的**——`globals.css:88` 早就在 `<body>` 上
   放了同一個算式，照字面加會變成 144px。子代理查證後否決並換了做法（分頁列高度
   寫成與那一行逐字相同的算式，用測試讀 `globals.css` 比對釘住）。
   Leader 在真瀏覽器複驗：`body` padding-bottom 實測 **72px**，沒有加倍。**子代理是對的。**
2. **徽章取數量總和，不是品項數**（Leader 裁決）。決定性理由是子代理自己寫的可及名稱
   唸「購物車，X 件」——一行 quantity=5 唸成「1 件」是錯的。而且後端會把同一個 SKU
   併進同一行，取 `lines.length` 時「再加一次」徽章不會動，等於「按了沒反應」。
3. **順手要求檢查的 NaN 風險是真的**：裸 `reduce` 在 quantity 是 `undefined` 時產出 `NaN`、
   是字串 `'3'` 時字串串接成 `'03'`、是 `null` 時靜靜少算。已擋掉並補測試。

★ **這一包 Leader 沒有照第 316 行「派工前先 commit 閘門檔」做**，
結果子代理每一輪收工都被 stop gate 要求還原 `.dispatch/ACTIVE.md`（＝它自己的授權）。
它兩次都正確拒絕並舉證。**下次一定要先 commit。**

---

## BE-39 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端。.NET 10 模組化單體，14 個限界上下文，EF Core + Npgsql。

GG_PACKAGE=BE-39

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/35-後端第二十三波派工書.md    §0 事實 ＋ §1 要補什麼與那條規則
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-39.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。12 個測試專案逐一在前景個別執行。
     不要用 dotnet test，用 ops\test.ps1。
     ★ dev 環境開著時 Host 會鎖住 bin\Debug，用 -Configuration Release 繞開，
       不要去停掉任何 dev server。

★ 契約是對的、程式沒跟上——不要改 docs/api/*.yaml，不要動前端
  （AddToCartPanel 已照契約寫好，修好後端前端零行變更就會動）。

★ Sku.available 預購恆 0 是契約明文，不要改那段邏輯。

★ §1 那條「同一個 SKU 掛多個開著的團取哪一個」的規則要有專屬測試釘住；
  若發現規則與既有假設衝突，停下來寫進報告問，不要自己換一條。

★ 補測試釘住 campaignId／campaign／price／campaignOfferId 這四個欄位——
  #26 能活到現在正是因為全 repo 沒有任何測試斷言過它們。

★ BOM：維持每個檔案原本的狀態。用 Python 寫檔時不要用 encoding='utf-8-sig'。

檔案所有權：見派工書 §3。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## FE-22 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray Platform 前端。Next.js 15 ＋ React 19 ＋ Tailwind 4，pnpm workspace。

GG_PACKAGE=FE-22

開工前務必先讀：
  CLAUDE.md                        前端四條 ＋ 派工規則
  docs/25-前端第十一波派工書.md      §0（#27）＋ §1（#28）
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-22.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。

★ 這個 workspace 沒有 jsdom／@testing-library，也沒有安裝（Leader 已查證）。
  不要為了寫測試去加相依套件、不要動 pnpm-lock.yaml。
  #27 的迴歸保證改用「把 handler 抽成可單元測試的小函式」，見派工書 §0。

★ pnpm lint 在這個 workspace 根本跑不起來（沒裝 ESLint），不要列進自驗、
  也不要假裝通過。測試用 pnpm --recursive test（基準 146 條）。

★ #27 不要把 ProductCard 搬出 <Link>、不要改 ProductCardLink 的結構。

★ #28 只補標了 * 的欄位；register-email 與 register-referral-code 是選填，
  不要加 required。不改表單的送出行為——若補了 required 之後瀏覽器開始擋送出、
  使既有錯誤訊息路徑走不到，停下來寫進報告問。

★ 不要動後端、不要動 docs/api/*.yaml。

檔案所有權：見派工書 §3。
docs/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## FE-21 的啟動 prompt（已撤包，保留供參考）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform-fe
GreyGray Platform 前端。Next.js 15 ＋ React 19 ＋ Tailwind 4，pnpm workspace。

GG_PACKAGE=FE-21

開工前務必先讀：
  CLAUDE.md                        前端四條 ＋ 派工規則
  docs/24-前端第十波派工書.md        §0 事實 ＋ §1 照抄哪個範本
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/FE-21.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。

★ 照抄 (dash)/ledger/page.tsx 已經在用的形狀，不要自己發明另一套取數方式。
  那一頁 Leader 已在真瀏覽器裡確認顯示的是真資料。

★ 四個 KPI 卡（DASHBOARD_KPIS）維持「尚未提供」，不要動、不要猜，
  更不要用列表 API 在前端加總——列表有分頁，加出來只是這一頁的合計。

★ 載入中與失敗時絕對不准 fallback 回任何寫死的數字——那正是這個 bug 的成因。

★ 必做 4 的測試是這一包最重要的產出：#29 能活到現在，正是因為沒有任何測試
  斷言過「首頁顯示的數字來自 API」。沒有那條測試，改完還會再退化。

★ 不要動後端、不要動 docs/api/*.yaml、不要動 packages/api-client 的端點函式。

檔案所有權：見派工書 §3。
docs/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## BE-38 的啟動 prompt（已撤包，保留供下一包參考格式）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端。.NET 10 模組化單體，14 個限界上下文，EF Core + Npgsql。

GG_PACKAGE=BE-38

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/34-後端第二十二波派工書.md    §0 事實 ＋ §1 雙重入帳陷阱 ＋ §2 冪等
  docs/05-API契約.md                §4 冪等那一節
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-38.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」

  ② 不准把驗證丟背景、不准排程 wakeup。12 個測試專案逐一在前景個別執行。
     不要用 dotnet test，SDK 10.0.301 ＋ xunit.v3 走 VSTest 會直接報錯。
     用 ops\test.ps1。

★ §1 的雙重入帳陷阱是這一包最容易做錯的地方，先讀完再動手。
  新的 LotCreated 帳務 handler 只在 LocalWholesale 時入帳，
  OverseasPurchase 直接 return，而且要有專屬迴歸測試。

★ 不要動 docs/api/*.yaml（契約已經有這兩個端點）、不要動前端、
  不要順手實作 LotSource.CustomerReturn、不要動 BffHttp.StatusFor。

★ BOM：維持每個檔案原本的狀態。你會動的 .cs 都沒有 BOM，維持沒有。
  用 Python 寫檔時不要用 encoding='utf-8-sig'（寫一定加 BOM）。

★ 質疑被鼓勵，但不准自己改方向：派工書寫錯了就停下來寫進報告問，
  不要一邊照做一邊在報告裡抱怨，也不要自己換一個做法。

檔案所有權：見派工書 §4。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。

你不可以自己宣告通過。交付完就停。
```

---

## BE-37 的啟動 prompt（已撤包，保留供下一包參考格式）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端。.NET 10 模組化單體，14 個限界上下文，EF Core + Npgsql。

GG_PACKAGE=BE-37

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/33-後端第二十一波派工書.md    §0 事實 ＋ §1 修法（範圍很窄，照著做）
  docs/05-API契約.md                §4 冪等那一節（這是唯一的權威）
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-37.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」
     缺任何一個，Leader 收不了工，你的交付會被退回。

  ② 不准把驗證丟背景、不准排程 wakeup。12 個測試專案逐一在前景個別執行。
     不要用 dotnet test，SDK 10.0.301 ＋ xunit.v3 走 VSTest 會直接報錯。
     （已知：整套約 15 分鐘跑得完。）

★ 範圍很窄：三個檔案六個字串 ＋ 一份契約文件補一行 ＋ 一組迴歸測試
  ＋ 一條測試的 migration 上界改成推導（必做 4）。
  不要順手改別的錯誤碼、不要動 OpenAPI、不要動前端、不要重構 BffHttp。
  ★ 特別是不准動 BffHttp.StatusFor——理由在派工書 §1，那條路徑經由 HTTP 走不到，
    動它會波及所有走 Problem(error) 的呼叫點。發現了寫進報告，不要動手。

★ BOM 與行尾：維持每個檔案原本的狀態。你會動的三個 .cs 都沒有 BOM、都是 LF，
  維持原狀。用 Python 寫檔時 encoding 用 'utf-8'（不要 'utf-8-sig'，寫一定加 BOM），
  newline 參數要設對，不要把行尾換掉。交付前逐檔量「改前／改後」兩次，兩次要一樣。
  量 CR 不要用 grep -c $'\r'（那個寫法在這個 shell 裡回的是總行數，Leader 上一波
  就是被它騙過一次），用 python -c 直接數位元組。

檔案所有權：只准改「你這一包擁有」的路徑（見派工書 §3）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。
.dispatch/reports/BE-37.md 也寫得了（那是你的自驗報告）。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。stash stack 是跨 worktree 共用的。

★ 派工前 Leader 已經把閘門檔 commit 掉了，session 開始時 git status 應該是乾淨的。
  收工時若有 hook 要你「還原」你沒改過的檔案，先查證是不是 Leader 派工前寫的，
  把證據寫進報告，不要執行 git checkout --。

★ 如果你認為 §1 有錯，停下來寫進報告問，不要自己改方向。
  （上一波 BE-36 的子代理在行尾這件事上堅持不寫一句自己量不出來的斷言，
   最後證實是 Leader 的量法壞了——質疑是被鼓勵的，自己改方向不是。）

你不可以自己宣告通過。交付完就停。

工作包內容（完整版在 docs/33-後端第二十一波派工書.md §4）：

  必做 1　改六處字串（位置見派工書 §0 的表，訊息文字不動，只改 code）：
            request.idempotency-key-required  → platform.idempotency-key-required
            request.idempotency-in-flight     → platform.request-in-flight
            request.idempotency-key-reused    → platform.idempotency-key-reused
            request.idempotency-key-too-long  → platform.idempotency-key-too-long

  必做 2　docs/05-API契約.md §4 那張表底下補一句，說明
          platform.idempotency-key-too-long 是「key 超過 255 字元」的 400 子類。
          不要改表裡既有的三列。

  必做 3　迴歸測試（tests/GreyGray.Platform.Tests/，BE-35 建的
          BffHttpTwoPhaseIdempotencyTests.cs 可以參考）：四個情況各一條，
          斷言 code 字串逐字 ＋ HTTP 狀態碼（400／400／409／422）。
          這四條存在的理由就是「下次有人改字串會立刻紅」——這個缺陷活到現在，
          正是因為沒有任何測試斷言過這些字串。測試裡寫一行註解指回 docs/05。

  必做 4　tests/GreyGray.M1a.Migrations.Tests/M1aCoreMigrationTests.cs:296
          的 DisplayName「0001→0014」與底下寫死的上界 14，改成從
          db/migrations/*.sql 的實際檔案數推導，DisplayName 改成不帶編號的說法。
          如果推導會讓那條測試的語意改變，停下來寫進報告問。

  自驗　　12 個測試專案逐一前景執行全綠（基準 214 條）、build 0 warning 0 error、
          git diff --numstat 確認 docs/api/ 完全沒出現、
          全 repo grep "request.idempotency" 應該零命中。
```

---

## 前兩輪（已撤包，留著供追溯）

| 包 | 內容 | commit |
|---|---|---|
| BE-34 | 查證「現在卡在哪」#22（查證包，不含修法） | `cb0d2f0` |
| BE-35 | 修 #22 家族——副作用已 commit 就不准 abandon | `213e8a7` |
| BE-36 | `POST /v1/shipments` 加模組層冪等，堵掉重複出貨單（#23） | `e2b4bf1` |

`.dispatch/ACTIVE.md`「已經通過、不再生效的」清單與
`GreyGray_PM/00-進度總表.md` 有完整脈絡。

---

## ★ 派工前先 commit 閘門檔（BE-34 查出來的閘門盲點）

`stop-gate.sh` 用 `git diff` 對 HEAD 比對越界，**分不出「實作者改的」與
「session 開始前就已經髒的」**。整合者派工時寫的 `.dispatch/` 檔在子代理眼中
就是「未提交的變更」，收工時 stop gate 會要求子代理還原它們——而還原
`ACTIVE.md` 等於刪掉子代理自己的授權、還原 `.selftest-stamp` 會讓
`audit-dispatch.sh` 第 ⑩ 項由綠轉紅。

**BE-35、BE-36、BE-37 都已照做（派工前先 commit）。**

---

## 兩條量測與編碼的教訓（BE-34、BE-36 各踩過一次）

1. **BOM**：用 Python 改既有檔案時，`io.open(..., encoding='utf-8-sig')` **讀**的時候
   有沒有 BOM 都吃，**寫**的時候卻**一定加上 BOM**。這個 repo 的 `.editorconfig`
   是 `charset = utf-8`（無 BOM），但 `ops/` 底下 17 支腳本有 12 支**刻意帶 BOM**
   （CI 有一步用 Windows PowerShell 5.1 驗 M-1 腳本，5.1 讀無 BOM 的 UTF-8 會當成
   ANSI 碼頁，而那些腳本有中文字串）。**規則是「維持每個檔案原本的狀態」，
   不是「一律拿掉」。**
2. **量行尾不要用 `grep -c $'\r' <檔案>`**——那個寫法在這個 shell 裡回的是
   **總行數**而不是「含 CR 的行數」，所以每個檔看起來 CR 數都剛好等於行數。
   Leader 在 BE-36 複驗時被它騙過一次，還據此去「更正」一段本來正確的報告。
   **用 `python -c` 直接數位元組**（`d.count(b'\r')`）。
   附帶記錄現況：這棵樹的工作區與 blob 都是 **LF**，而 `.editorconfig` 寫的是
   `end_of_line = crlf`，三者不一致——那是既有狀態，`git diff` 印的
   「LF will be replaced by CRLF」是 `core.autocrlf=true` 的正常提示，不是缺陷。

---

## 下一波派工前

Leader 讀 `GreyGray_PM/00-進度總表.md`「下一步」一節決定要派什麼，
把新的工作包寫進對應樹的 `.dispatch/ACTIVE.md`，再把啟動 prompt 與工作包原文
寫回這個檔案（兩棵樹必須逐字同步，見 `.dispatch/reports/README.md` 與
`audit-dispatch.sh` 第 ⑦ 項）。
