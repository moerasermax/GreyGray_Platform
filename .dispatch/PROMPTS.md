# 啟動 prompt（後端第六波）

**直接複製貼上，不用二次加工。** `ACTIVE.md` 裡這五包已經生效。

開 terminal 的方式（後端用 Codex）：

```bash
GG_PACKAGE=BE-13 codex
```

**環境變數與 prompt 裡的那一行都要有。** 環境變數給自己開的 terminal 用；
prompt 裡的 `GG_PACKAGE=` 那一行是給 ai-cli fan out 子 agent 用的
（ai-cli 的 `run` 沒有 env 參數，只能走 session_id 綁定）。
兩個都寫，同一份 prompt 兩種開法都能用。

`SessionStart` 會自動把「你這一包能動哪些路徑」送進 context，**所以 prompt 不必重複那些**。

---

## 排程

```
先跑，它擋住所有人      BE-13
可與 BE-13 同時開       BE-9   BE-11   BE-14   BE-15
四包通過後才開          BE-16（目前在 ACTIVE.md 裡是註解掉的）
```

> ⚠️ **BE-9／BE-11／BE-14 三包都會跑 `ops/test.ps1`，要排開不要同時跑。**
> 兩包同時 build 會搶 obj/bin 與 NuGet 全域資料夾，
> 症狀是 `NuGet.targets(198,5)` 的「當檔案已存在時，無法建立該檔案」。
> 那是競態，不是程式壞了。
>
> BE-13 與 BE-15 不寫 C#，不受這條限制。

---

## BE-13　M-1 環境收尾　🔴 先跑這包

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

---

## BE-9　M1b-3b 帶回入庫與旅程成本

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

---

## BE-11　M1b-2 缺貨補償與現場漲價詢問

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

---

## BE-14　M1b-4 出貨、交運與簽收

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

---

## BE-15　`refundTo` 語意的契約異動

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

---

## 通用：五包都適用的三件事

1. **你不可以自己宣告通過。** 只做自驗，逐條貼出**實際指令與實際輸出**，
   不是「已完成」四個字。整合驗收是整合者的事。
2. **遇到契約缺口或平台缺口就停下來回報**，不要自己補一個看起來合理的預設值。
   這不會被當成沒完成——這個專案已經有六次這樣的回報，六次都對，三次直接變成 ADR。
   `docs/16` §6 有那張表。
3. **不要碰整個工作區的 git 指令**（`git stash`／`reset --hard`／`clean`／
   `checkout -- .`／`commit`）。同一棵 worktree 有別包在平行工作，
   而且 stash stack 是跨 worktree 共用的。閘門會擋，但你本來就不該試。
