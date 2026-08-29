# 啟動 prompt

**你只開一個 terminal，當 Leader。** 五包由 Leader 用 ai-cli fan out 出去。

這個檔案兩棵 worktree 各一份、內容相同，所以 Leader 開在哪一棵都能派全部五包
（ai-cli 的 `run` 有 `workFolder`，跨樹派工靠它）。

---

## 你要做的只有這一步

開一個 terminal，貼下面這段。**就這樣。**

```
GG_ROLE=leader

你是 GreyGray Platform 的 Leader。讀 .dispatch/PROMPTS.md 與 .dispatch/ACTIVE.md，
把現在生效的五包用 mcp__ai-cli__run 派出去，然後等它們回來做整合驗收。

派工規則：
  - 每個子代理的 prompt 用本檔案「工作包」那一節的原文，一字不改。
    每段開頭的 GG_PACKAGE=<包名> 那一行一定要留著——ai-cli 的 run 沒有 env 參數，
    包別只能靠 prompt 帶進去，UserPromptSubmit 會把它綁到那個子代理的 session_id。
  - workFolder 要指對：
      後端 BE-*  → D:\WorkSpace\01_開發中_wip\GreyGray_Platform
      前端 FE-*  → D:\WorkSpace\01_開發中_wip\GreyGray_Platform-fe
  - 先派 FE-12——環境剛通，它是 D 階段的第一步，而且它的產出會決定後面做什麼。
  - BE-17／BE-18／BE-19 三包都會跑 ops/test.ps1，要排開不要同時跑
    （同時 build 會搶 NuGet 資料夾，症狀是 NuGet.targets(198,5) 檔案已存在，那是競態）。

你自己不寫原始碼。你寫得了的是 .dispatch/、.claude/、.codex/、docs/ 與 GreyGray_PM。
子代理回來之後由你做整合驗收：自己重跑 build 與測試複驗，不要只轉述它們的自述。
```

> **不要把 `GG_ROLE=leader` 放進子代理的 prompt。** 就算不小心放了也升不了級——
> `claim-package.sh` 讓包別優先於角色（已實測），但別依賴那道保險。

---

## 排程

```
先派，產出會決定後面      FE-12
接著可以同時派            BE-17   BE-18   BE-19   FE-16
三包後端通過後才派        BE-20（目前在 ACTIVE.md 裡是註解掉的）
```

---

## 工作包（以下每一段就是子代理的 prompt，原文照抄）

### FE-12　關掉 mock，對真後端跑一遍　🔴 先派這包

```
GG_PACKAGE=FE-12

你是 GreyGray Platform 的 FE-12。讀 docs/18-前端第五波派工書.md，
§1 §2 §3 全部要看，然後照 §5 的 FE-12 那一節做。

前端十五包全部通過了，但沒有一行前端程式對真後端跑過——「在 mock 下是對的」
證明過很多次，「接上真後端是對的」一次都沒有。D 階段現在 0/7，你是第一步。

環境已就緒：2026-08-30 BE-13 修好 YC 上的 sc start 錯誤 5（根因是 nssm.exe 的 ACL），
PostgreSQL 與 Garnet 重開機後 9 秒自動起來。

這一包不是寫功能是驗證。預期產出多半是「發現了什麼」而不是新程式碼——
如果你交出一堆新程式碼，多半代表你在修不該你修的東西。不要修後端。

兩件已知缺口，遇到就記下來，不是你要修的：
  一、已付款訂單的取消退款現在必定失敗（ADR-024，綠界退款 API 還沒做）
  二、訂單簽收後不會變成 Completed（ADR-025，鑑賞期 Timer 還沒做）

主要產出是一份「哪些頁對真後端是好的、哪些壞了、壞在哪」的清單。
自驗照 §5 逐條貼實際指令與實際輸出，然後停下來等整合驗收。
```

### BE-17　綠界原路退款 API

```
GG_PACKAGE=BE-17

你是 GreyGray Platform 的 BE-17。讀 docs/17-後端第七波派工書.md，
§1 §2 全部要看，然後照 §5 的 BE-17 那一節做。再讀 docs/00-decisions.md 的 ADR-024。

這是現在最擋 M1b 的一件。已付款訂單的取消退款走不通——
Payment/OrderingEventHandlers.cs:43 對 OriginalPaymentMethod 直接
throw NotSupportedException「不得把退款要求標成成功」。

★ 那個 throw 是對的，不要只是把它拿掉。拿掉而沒有真的退款，系統會把「錢已退」
記進 Ledger 而客人根本沒收到錢——那比現在的失敗嚴重得多。
要做的是讓它真的能退，然後才把 throw 換成真實結果。

地基已經在了：EcpayGateway.cs 有 CheckMacValue 與 Payment:ECPay:* 組態，
你是擴充它，不是從零寫綠界整合。退款要冪等，重送不可重複退。

★ E3（正式商店代號與金鑰）老闆還在辦。這一包用綠界測試環境做到能實際發動一次退款
並貼出回應；不可以宣稱「可上線」，交付說明要明寫「已對測試環境驗證，正式憑證未到位」。
如果測試環境不支援退款，停下來回報，不要用 mock 假裝驗過。

自驗照 §5，然後停下來等整合驗收。
```

### BE-18　Ordering：StoredValue 擋 ＋ 鑑賞期 Saga Timer

```
GG_PACKAGE=BE-18

你是 GreyGray Platform 的 BE-18。讀 docs/17-後端第七波派工書.md，
§1 §2 §3 全部要看，然後照 §5 的 BE-18 那一節做。
再讀 docs/00-decisions.md 的 ADR-024 與 ADR-025。

兩件事，都在 Ordering：

一、StoredValue 在 M1b 要回可預期的業務失敗（Result，不是例外），訊息說
    「儲值金退款要到 M3 才開放」。這條 ADR-023 決定二整個 repo 沒有任何地方實作，
    前端只擋在 UI，API 層仍收得下。加在 CancelAdminAsync／CancelLineAsync。
    不得默認任何一種去向。M3 要開時只需拿掉守衛，所以用組態旗標或明確常數。

二、鑑賞期 Saga Timer。訂閱 fulfillment.ShipmentDelivered.v1，簽收後排 timer，
    7 天（做成組態不要寫死）屆滿轉 Completed。
    用 ISagaTimerScheduler<OrderingDbContext> 泛型版，讓 timer 與業務資料同交易。
    ★ 一張訂單可能對應多個出貨單（N:M），全部簽收之後才起算鑑賞期，
      不是第一個簽收就起算。這條容易做錯。

★ BE-19 也會碰 Ordering，但只碰 Infra 的 ModuleRegistration.cs 與一個新 handler 檔。
你擁有 Ordering.Core 與 OrderingDbContext.cs，兩邊不准動對方的檔。

你的 migration 編號是 0013_。自驗照 §5，然後停下來等整合驗收。
```

### BE-19　契約與事件形狀異動 ＋ 帶回→待出貨接線

```
GG_PACKAGE=BE-19

你是 GreyGray Platform 的 BE-19。讀 docs/17-後端第七波派工書.md，
§1 §2 §3 全部要看，然後照 §5 的 BE-19 那一節做。
再讀 docs/00-decisions.md 的 ADR-027。

三個契約異動 ＋ 一個接線，一起做因為共用同一次 codegen：

一、GoodsReceived.v1 加 OrderLineId。第六波驗收發現「帶回入庫後訂單轉待出貨」
    這條線是斷的——事件只帶 CampaignId+SkuId+Quantity+UnitCost+Source，
    而 IOrderingGoodsReceipt 已經存在、已實作、已 DI 註冊卻沒有任何呼叫點。
    事件是一個採購品項發一則，PurchaseItem 本來就帶 OrderLineId，值就在手上。
    ★ 這是改 v1 wire shape，一般 ADR-018 不允許，但這個事件是第六波才新增、
      從沒上過正式機，現在改沒有相容性成本。老闆已拍板改 v1。
二、新增 GET /v1/shipments/{id}。現在前端撈 limit:100 列表再過濾，
    出貨單超過 100 張之後點詳情會白頁而且不報錯。
三、AdminCampaignInput 加漲價詢問逾時欄位（ADR-027，每團可設）。
    沒填保留 2 小時當預設，但要在契約 description 寫明那是預設值。
四、接線：Ordering.Infra 加一個 IIntegrationEventHandler<GoodsReceived>，
    呼叫既有的 IOrderingGoodsReceipt。不要改 Ordering.Core——那是 BE-18 的。

★ BE-18 擁有 Ordering.Core 與 OrderingDbContext.cs，你只碰 Infra 的
ModuleRegistration.cs 與新的 handler 檔。兩邊不准動對方的檔。

你的 migration 編號是 0014_。接線要用真 PostgreSQL 做端對端測試，
而且要有「兩條預購 line 只帶回一條時不轉待出貨」的測試。
自驗照 §5，然後停下來等整合驗收。
```

### FE-16　金額 `NT$`、拿掉 KPI 假數字、開團逾時欄位、出貨詳情端點

```
GG_PACKAGE=FE-16

你是 GreyGray Platform 的 FE-16。讀 docs/18-前端第五波派工書.md，
§1 §2 §3 全部要看，然後照 §5 的 FE-16 那一節做。
再讀 docs/00-decisions.md 的 ADR-027 與 ADR-028。

四件事，前兩件現在就能做，後兩件等後端 BE-19 的契約落地：

① 金額顯示統一 NT$（ADR-028）。只改 packages/api-client/src/money.ts 一處——
   那個檔的註解早就寫了「改這裡一處即可，不要在呼叫端自己加前綴」。
   外幣維持 ICU 既有行為（US$、HK$）不要動。
   改完跑全部測試看哪些斷言紅了：你所有權內的自己修，
   別包的測試檔列清單回報，不要自己改。
② 後台首頁 KPI 拿掉假數字。常數在 (dash)/_lib/dashboardMock.ts:49。
   營運會相信後台上的數字，佔位值放在正式環境比沒有更危險。
   不要自己發明彙總端點，也不要用列表 API 在前端加總——列表有分頁，
   加出來的是「這一頁的合計」而且違反鐵則 2。
③ ⏸ 開團表單加漲價詢問逾時欄位。等 BE-19。沒填預設 2 小時，
   文案要讓營運看得懂後果：逾時就自動視為照買，花的是客人的錢。
④ ⏸ 出貨詳情改打 GET /v1/shipments/{id}。等 BE-19。

③④ 若因為 BE-19 未落地而沒做，明講「未做，等 BE-19」，不要假裝做了。
自驗照 §5，然後停下來等整合驗收。
```

---

## 五包都適用的四件事

1. **子代理不可以自己宣告通過。** 只做自驗，逐條貼出**實際指令與實際輸出**。
   整合驗收是 Leader 的事，而且 Leader 要自己重跑複驗，不採信自述。
2. **遇到契約缺口或平台缺口就停下來回報**，不要自己補一個看起來合理的預設值。
   這個專案已經有七次這樣的回報，七次都對，五次直接變成 ADR。
3. **你只有這一輪。** 不要排程 wakeup、不要說「等背景跑完再回報」然後結束——
   headless 子代理的行程一結束就沒了，那個通知**不會來**，而 exit code 還是 0，
   從外面看像成功。需要等待就在這一輪內輪詢等待；真的無法在這一輪完成，
   就**明講做不到與原因**，不要說「稍後繼續」。
   （2026-08-30 實測：八包裡 BE-13／BE-9／BE-11 三包都踩到，都沒交出自驗報告，
   由整合者接回 session 才補完。BE-13 差一點就以「未驗證重開機」的狀態被當成通過，
   而重開機正是那一包寫明「沒做等於整包沒做」的判準。）
4. **不要碰整個工作區的 git 指令**（`git stash`／`reset --hard`／`clean`／
   `checkout -- .`／`commit`）。stash stack 是跨 worktree 共用的，
   FE-10 曾經用 `git stash` 把 FE-9 未提交的交付整個掃走。
