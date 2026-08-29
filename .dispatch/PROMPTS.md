# 啟動 prompt

**你只開一個 terminal，當 Leader。** 八包由 Leader 用 ai-cli fan out 出去，
你不用一個一個開 terminal，也不用自己設 `GG_PACKAGE`。

這個檔案兩棵 worktree 各一份、內容相同，所以 Leader 開在哪一棵都能派全部八包
（ai-cli 的 `run` 有 `workFolder`，跨樹派工靠它）。

---

## 你要做的只有這一步

開一個 terminal，貼下面這段。**就這樣。**

```
GG_ROLE=leader

你是 GreyGray Platform 的 Leader。讀 .dispatch/PROMPTS.md 與 .dispatch/ACTIVE.md，
把現在生效的八包用 mcp__ai-cli__run 派出去，然後等它們回來做整合驗收。

派工規則：
  - 每個子代理的 prompt 用本檔案「工作包」那一節的原文，一字不改。
    每段開頭的 GG_PACKAGE=<包名> 那一行一定要留著——ai-cli 的 run 沒有 env 參數，
    包別只能靠 prompt 帶進去，UserPromptSubmit 會把它綁到那個子代理的 session_id。
  - workFolder 要指對：
      後端 BE-*  → D:\WorkSpace\01_開發中_wip\GreyGray_Platform
      前端 FE-*  → D:\WorkSpace\01_開發中_wip\GreyGray_Platform-fe
  - 先派 BE-13，它擋住整個 D 階段。其餘七包可以同時派。
  - BE-9／BE-11／BE-14 三包都會跑 ops/test.ps1，要排開不要同時跑
    （同時 build 會搶 NuGet 資料夾，症狀是 NuGet.targets(198,5) 檔案已存在，那是競態）。

你自己不寫原始碼。你寫得了的是 .dispatch/、.claude/、.codex/、docs/ 與 GreyGray_PM。
子代理回來之後由你做整合驗收：自己重跑 build 與測試複驗，不要只轉述它們的自述。
```

`SessionStart` 會確認你的身分並把生效中的包送進 context。
之後 Leader 自己去讀下面的工作包原文，派出去。

> **不要把 `GG_ROLE=leader` 放進子代理的 prompt。** 就算不小心放了也升不了級——
> `claim-package.sh` 讓包別優先於角色（已實測），但別依賴那道保險。

---

## 排程

```
先派，它擋住所有人      BE-13
接著可以同時派          BE-9   BE-11   BE-14   BE-15   FE-13   FE-14   FE-15
四包後端通過後才派      BE-16（目前在 ACTIVE.md 裡是註解掉的）
BE-13 通過後才派        FE-12（同上）
```

---

## 工作包（以下每一段就是子代理的 prompt，原文照抄）

### BE-13　M-1 環境收尾　🔴 先派這包

```
GG_PACKAGE=BE-13

你是 GreyGray Platform 的 BE-13。讀 docs/16-後端第六波派工書.md，
§1 §2 全部要看，然後照 §5 的 BE-13 那一節做。

這一包不寫任何 C#。YC 正式機上 PostgreSQL 17.11 與 Garnet 1.0.83 都裝好設好了，
但兩個 NSSM 服務 sc start 回錯誤 5（存取被拒，不是 1069 登入失敗），
而 ops/ 的兩支腳本還寫著 Valkey 與 postgresql-x64-17。把這台機器修到能用。

動手前先讀 docs/14 §8 的四個 SSH 坑，那些在自己機器上永遠測不出來。
重開機後兩個服務要自動起得來——這一條沒做等於整包沒做。

自驗照 §5 逐條貼實際指令與實際輸出，然後停下來等整合驗收。
```

### BE-9　M1b-3b 帶回入庫與旅程成本

```
GG_PACKAGE=BE-9

你是 GreyGray Platform 的 BE-9。讀 docs/13-後端第五波派工書.md，
§0 §1 §2 全部要看，然後照 §4 的 BE-9 那一節做。
再看 docs/16 §1 的既成事實表——BE-12 已經通過了。

你當初停工是對的：那三個上游事件當時沒有人發。現在有了，BE-12 已通過驗收（dbec9cd），
procurement.GoodsReceived.v1、campaign.TripCostRecorded.v1、ordering.OrderReadyToShip.v1
各自由擁有它的模組發出。你直接消費它們，不要自己再發一次。

你的 migration 編號是 0010_（不是 0008_，第一版派工書撞號了，已更正）。
一個編號只能有一個檔。

自驗照 §6 逐條貼實際指令與實際輸出，然後停下來等整合驗收。
```

### BE-11　M1b-2 缺貨補償與現場漲價詢問

```
GG_PACKAGE=BE-11

你是 GreyGray Platform 的 BE-11。讀 docs/13-後端第五波派工書.md，
§0 §1 §2 §5 全部要看，然後照 §4 的 BE-11 那一節做。

你等的那兩個決策已經拍板了，是 ADR-023（docs/00-decisions.md）：
  一、缺貨退款去向由「客人」選，不是營運代選。
  二、M1b 只開原路退款。RefundDestination.StoredValue 要回可預期的業務失敗
      （Result，不是例外），訊息要說清楚「儲值金要到 M3 才開放」。
  三、不得默認任何一種去向。客人選出來之前，RefundRequested 不發。

refundTo 已經在凍結契約裡而且是 required，所以不要以為要加欄位。
契約的 description 由 BE-15 改，你不要動 docs/api/。

你的 migration 編號是 0011_。自驗照 §6，然後停下來等整合驗收。
```

### BE-14　M1b-4 出貨、交運與簽收

```
GG_PACKAGE=BE-14

你是 GreyGray Platform 的 BE-14。讀 docs/16-後端第六波派工書.md，
§1 §2 全部要看，然後照 §5 的 BE-14 那一節做。

契約已凍結且完整，/v1/shipments 四個端點都在 docs/api/openapi.admin.yaml 裡。
兩件契約明文寫、不要做錯的事：

  一、Order 與 Shipment 是 N:M。一張訂單可拆多包、一包可含同客人多張訂單。
      契約 description 明寫「這是日常，不是邊緣案例」。不要做成 1:N。
  二、carrierCost 是你付給物流商的成本，不是向客人收的運費（那是訂單的 shippingFee）。
      兩個是獨立的數字，混在一起就永遠算不出物流的真實損益。

你的 migration 編號是 0012_。簽收後轉 Order 為 Completed 要透過 Ordering 的
input port，不要跨 schema 改狀態——那正是 BE-9 當初停下來的理由。

自驗照 §5，然後停下來等整合驗收。
```

### BE-15　`refundTo` 語意的契約異動

```
GG_PACKAGE=BE-15

你是 GreyGray Platform 的 BE-15。讀 docs/16-後端第六波派工書.md 的
§1 §2 與 §5 的 BE-15 那一節，再讀 docs/00-decisions.md 的 ADR-023
與 docs/05-API契約.md 的異動流程。

這一包不寫程式，只改 docs/api/*.yaml 的 description，而且只准改 description。
ADR-023 把 refundTo 的語意從「營運代選」改成「客人自己選」，
並且 M1b 期間 StoredValue 不開放。已查證：refundTo 已經在凍結契約裡而且是 required，
RefundDestination 的兩個 enum 值也都在，所以這是純語意變更、schema 不動。

驗收判準是 docs/05 的硬規則：重跑 codegen 後 TS 型別必須逐字節相同，
diff 只准出現在 JSDoc 註解。不通過就是改到語意了，停下來回報。

不要新增「客人選退款去向」的前台端點——M1b 只有一個合法值，沒有東西可選。
```

### FE-13　後台：出貨、交運與簽收

```
GG_PACKAGE=FE-13

你是 GreyGray Platform 前端第四波的 FE-13。讀 docs/15-前端第四波派工書.md，
§0 §1 §2 §3 全部要看，然後照 §6 的 FE-13 那一節做。

後台目前走到「買到回報」就斷了，契約裡的四個 fulfillment 端點一個畫面都沒有。
你要做出貨單列表、建立、交運、簽收。

兩件契約明文寫、不要做錯的事：
  一、Order 與 Shipment 是 N:M。契約 description 明寫「這是日常，不是邊緣案例」，
      所以建立出貨單的 UI 必須能一次勾選多張訂單，不是「在訂單頁按出貨」。
  二、carrierCost 是付給物流商的成本，shippingFee 是向客人收的運費。
      畫面上這兩個數字不可以混、不可以相加，label 要讓營運看得懂差別。

自驗照 §6 逐條貼實際指令與實際輸出（含截圖），然後停下來等整合驗收。
```

### FE-14　後台：缺貨補償、漲價詢問 ＋ 退款去向回工

```
GG_PACKAGE=FE-14

你是 GreyGray Platform 前端第四波的 FE-14。讀 docs/15-前端第四波派工書.md，
§0 §1 §2 §3 全部要看，然後照 §6 的 FE-14 那一節做。
再讀 docs/00-decisions.md 的 ADR-023。

這一包有兩件事，第二件是回工：

一、缺貨（unavailable）與漲價（price-changed）兩個端點目前沒有 UI。
    漲價詢問有「到這個時間還沒回覆就自動視為照買」的欄位，畫面要顯示它，
    而且不要做成「等待中」的樣子——藍圖強調系統不會因為等客人而卡住。

二、★ 取消對話框現在是錯的。CancelOrderDialog.tsx 與 CancelOrderLineDialog.tsx
    目前寫著 useState<RefundDestination>('StoredValue')，違反 ADR-023 兩條：
    去向該由客人選（不是營運代選），而且 StoredValue 在 M1b 會被後端明確拒絕
    ——它現在是預設值，營運不改就會送出一個必定失敗的請求。
    改成：不得有預設值、沒選不能送出；StoredValue 顯示但停用並寫「M3 才開放」
    （不要從選單移除）；後端回的業務失敗訊息要原樣顯示，不要吞掉換成通用錯誤。
    共用元件是 RefundDestinationFields.tsx，兩個對話框都在用，先讀它再動手。

驗收條件之一：grep -rn "useState.*StoredValue" apps/admin/ 必須是 0 筆。
自驗照 §6，然後停下來等整合驗收。
```

### FE-15　前台：客人看得到自己的品項缺貨與退款

```
GG_PACKAGE=FE-15

你是 GreyGray Platform 前端第四波的 FE-15。讀 docs/15-前端第四波派工書.md，
§0 §1 §2 §3 全部要看，然後照 §6 的 FE-15 那一節做。

後台已經可以把品項標成缺貨了，但客人在前台看不到這件事——訂單詳情頁不認得
Unavailable 這個狀態，退款金額也沒有呈現。客人只會看到金額對不上而不知道為什麼。

最容易做錯的一件事：讓客人以為整張單被取消了。
契約 description 明寫「該 line 取消並退款，其餘 line 續行，訂單不整張作廢」，
畫面要把「其餘品項照常出貨」講清楚。

先讀既有的訂單詳情頁，這一包是補呈現不是重做頁面。
退款金額用 formatMoney()，不要自己算「原價減退款」——總額後端會回。

不要做「客人選退款去向」的畫面：前台契約沒有那個端點，而且 M1b 只開原路。
自驗照 §6，然後停下來等整合驗收。
```

---

## 八包都適用的三件事（已經寫在派工書裡，這裡只是提醒）

1. **子代理不可以自己宣告通過。** 只做自驗，逐條貼出**實際指令與實際輸出**。
   整合驗收是 Leader 的事，而且 Leader 要自己重跑複驗，不採信自述。
2. **遇到契約缺口或平台缺口就停下來回報**，不要自己補一個看起來合理的預設值。
   這個專案已經有六次這樣的回報，六次都對，三次直接變成 ADR。
3. **不要碰整個工作區的 git 指令**（`git stash`／`reset --hard`／`clean`／
   `checkout -- .`／`commit`）。stash stack 是跨 worktree 共用的，
   FE-10 曾經用 `git stash` 把 FE-9 未提交的交付整個掃走。
