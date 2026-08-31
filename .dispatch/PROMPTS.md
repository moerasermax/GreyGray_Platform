# 啟動 prompt

**你只開一個 terminal，當 Leader。** 這一波有兩包，檔案所有權互不重疊，
由 Leader 用 ai-cli 同時 fan out 出去。

這一波前端樹沒有生效包，純同步用。

---

## 你要做的只有這一步

開一個 terminal，貼下面這段。**就這樣。**

```
GG_ROLE=leader

你是 GreyGray Platform 的 Leader。讀 .dispatch/PROMPTS.md 與 .dispatch/ACTIVE.md，
把現在生效的 BE-29、BE-30 用 mcp__ai-cli__run 同時派出去（兩包檔案所有權
互不重疊，可以平行跑），然後等它們都回來做整合驗收。

派工規則：
  - 每個子代理的 prompt 用本檔案「工作包」那一節對應的原文，一字不改。
    開頭的 GG_PACKAGE=BE-29／GG_PACKAGE=BE-30 那一行一定要留著——ai-cli 的
    run 沒有 env 參數，包別只能靠 prompt 帶進去，UserPromptSubmit 會把它
    綁到那個子代理的 session_id。
  - workFolder：兩包都是 D:\WorkSpace\01_開發中_wip\GreyGray_Platform（後端樹）。
    這一波不涉及前端樹，不需要派任何前端子代理。

★ 收子代理的回報時，先看 .dispatch/reports/BE-29.md、.dispatch/reports/BE-30.md
  在不在、三個標頭齊不齊。不齊就用同一個 session_id 接回去要它補完——
  不要自己幫它補，也不要因為 exit code 是 0 就當成完成。

你自己不寫原始碼。你寫得了的是 .dispatch/、.claude/、.codex/、docs/ 與 GreyGray_PM。
子代理回來之後由你做整合驗收：自己重跑 build 與測試複驗，並且**親自啟動
D:\GreyGray 的三個 Host、用種子帳號登入**：
  - 對 GET /v1/orders 與 GET /v1/shipments 各送一次請求，確認從 500 變成 200/401
    （docs/26 §5 BE-29）
  - 對已付款訂單送一次「取消＋原路退款」的請求，確認不再是 422
    （docs/26 §5 BE-30）
不要只轉述自述。驗收完先 commit，再撤包——順序反過來會讓未提交的交付變成
無主檔案，閘門會判成越界。兩包驗收獨立進行，其中一包先過就先 commit 那一包，
不需要等另一包。
```

> **不要把 `GG_ROLE=leader` 放進子代理的 prompt。** 就算不小心放了也升不了級——
> `claim-package.sh` 讓包別優先於角色（已實測），但別依賴那道保險。

---

## 排程

```
BE-29 與 BE-30 同時派出，互不相依，檔案所有權互不重疊（見 docs/26 §3）。
```

---

## 兩條這一波開始機械檢查的規則

**① 自驗報告是檔案，不是對話。**
`.dispatch/reports/<包名>.md`，三個標頭一字不差：
`## 指令與輸出`、`## 逐條自驗`、`## 我發現但沒做的事`。
缺任何一個，`audit-dispatch.sh` 第 ⑧ 項會擋下 Leader 收工。

**② 不准把測試丟背景、不准排程 wakeup。**
`ops/test.ps1` 淨執行已超過工具單次前景呼叫的 10 分鐘上限，
分批前景跑完（照 `ops/test.ps1` 自己的迴圈，逐一跑每個測試專案的執行檔）。

---

## 工作包（以下兩段就是各自子代理的 prompt，原文照抄）

### BE-29　修正三處 `.ThenBy(x => x.Id.Value)` 導致的 admin 列表端點 500

```
GG_PACKAGE=BE-29

你是 GreyGray Platform 的 BE-29。讀 docs/26-後端第十四波派工書.md，
§0.1 §1 全部要看，然後照 §5 的 BE-29 那一節做。
再讀 .dispatch/reports/README.md（自驗報告的格式）。

這一波要解的問題：admin 後台的訂單列表（GET /v1/orders）與出貨列表
（GET /v1/shipments）對真 Postgres 資料庫全部回 500。Leader 這一輪第一次
用瀏覽器＋種子帳號完整走過真後端登入流程才發現——之前每一波要嘛卡在
更前面的環境問題，要嘛用 fake repository 測試，從沒有人真的對一個有資料的
Postgres 打過這兩個端點。

根因：三個檔案裡各有一行同一個手誤——
`OrderingRepository.cs:128`：`.ThenByDescending(order => order.Id.Value)`
`FulfillmentRepository.cs:67`：`.ThenByDescending(shipment => shipment.Id.Value)`
`ProcurementRepository.cs:36`：`.ThenBy(item => item.Id.Value)`
（Procurement 這一處這一輪沒有真資料可以端到端重現，但程式碼形狀跟另外
兩個已經實測壞掉的一模一樣，高度懷疑同樣會壞，你的驗收要用真資料親自證實。）

用 `.Value` 拆開強型別 ID（OrderId／ShipmentId／PurchaseItemId 這種 wrapper）
再排序，EF Core 的值轉換器在 ThenBy 這個位置不知道怎麼翻譯成 SQL。
`CampaignRepository.cs:56` 與 `LedgerQuery.cs:134` 已經證明「直接排序整個
ID 型別，不要 .Value」這條路是通的。

修法：三處都拿掉 `.Value`。這是完整修法，不要另外發明 client-evaluation
（AsEnumerable／ToList 提前物化）繞過去，那會讓分頁在資料庫層失去效果。

★ 每一處都要補一條用 testcontainers 起真 Postgres 的迴歸測試，塞 2 筆以上
  讓主排序鍵刻意相同、逼查詢真的用到 ThenBy 的第二鍵，先紅後綠。

★ 順手 `grep -rn "\.Id\.Value)" src/` 掃一次全 repo，逐一判斷是不是對
  IQueryable 做 OrderBy/ThenBy/Where。CatalogServices.cs:389 與
  StockReservationPlan.cs:46 這兩處 Leader 初步看像是 LINQ-to-Objects
  （不會有 SQL 翻譯問題），但你要自己確認，不要照抄 Leader 的判斷。

你自己不能宣告這一包通過或修完收工，你只能做完並交付、寫自驗報告，
由 Leader 做整合驗收與最終判斷。

檔案所有權（只准改這些路徑，見 docs/26 §3）：
  src/Modules/Ordering/GreyGray.Modules.Ordering.Infra/OrderingRepository.cs
  src/Modules/Fulfillment/GreyGray.Modules.Fulfillment.Infra/FulfillmentRepository.cs
  src/Modules/Procurement/GreyGray.Modules.Procurement.Infra/ProcurementRepository.cs
  tests/GreyGray.M1a.CheckoutOrdering.Tests/（僅限新增排序相關測試，
    不要動 AdminCancelLineEndpointTests.cs，那是另一個同時在跑的
    BE-30 的所有權，不要碰）
  tests/GreyGray.M1b.Fulfillment.Tests/
  tests/GreyGray.M1b.Procurement.Tests/
  .dispatch/reports/BE-29.md（你的自驗報告）
  docs/、management/、STATE.md、CLAUDE.md、AGENTS.md（任何一包都寫得了）

★ 這一波同時有另一個子代理在跑 BE-30，改的是
  src/Hosts/GreyGray.Api.Admin/M1aEndpoints.cs 與
  tests/GreyGray.M1a.CheckoutOrdering.Tests/AdminCancelLineEndpointTests.cs，
  跟你的所有權不重疊，兩邊互不相依，不用互相等待。dotnet build／ops/test.ps1
  如果出現看不懂的短暫錯誤先重跑一次再回報，不要立刻假設是自己的程式碼錯了。

如果 grep 掃描發現所有權範圍以外的檔案也有同一個手誤，把發現的檔案清單
寫進自驗報告的「我發現但沒做的事」，不要自己擴大所有權去改。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。stash stack 是跨 worktree 共用的。

自驗完成後，把結果寫進 .dispatch/reports/BE-29.md，三個標頭一字不差：
## 指令與輸出
## 逐條自驗
## 我發現但沒做的事

寫完就停下來，等 Leader 做整合驗收。你不可以自己宣告通過。
```

### BE-30　拿掉兩處過期守衛，讓已付款訂單的「原路退款」真的打得到

```
GG_PACKAGE=BE-30

你是 GreyGray Platform 的 BE-30。讀 docs/26-後端第十四波派工書.md，
§0.2 §1 全部要看，然後照 §5 的 BE-30 那一節做。
再讀 .dispatch/reports/README.md（自驗報告的格式）。

這一波要解的問題：master 進度表認為「已付款訂單取消不了」已經被 BE-17 解決，
但那句話只對了一半。BE-17／BE-18 把「呼叫綠界真退刷 API」「儲值金退款擋到
M3」在 OrderingApplicationService 這一層做完、測完了（該檔案 :29-30 的註解
自己寫著「整條退款流程已經實作完成」），但沒有人拿掉 admin HTTP 端點更早、
更舊的一層守衛——那個守衛寫在 M1a-6（比 BE-17 早好幾波，當時綠界退刷 API
真的還沒做，是合理的 fail-closed），現在變成一個沒人記得要拆的路障。

src/Hosts/GreyGray.Api.Admin/M1aEndpoints.cs 兩處一模一樣（:204-210 整張
訂單取消、:290-296 單一品項取消）：

  if (input.RefundTo == RefundDestination.OriginalPaymentMethod &&
      existing.Value.PaidAmount is { IsZero: false })
  {
      return Result<AdminOrderResponse>.Failure(
          "payment.original-refund-not-configured",
          "綠界原路退款尚未完成 provider API 設定，訂單未取消；可改選退款至儲值金。");
  }

兩處都在呼叫 ordering.CancelAdminAsync／ordering.CancelLineAsync 之前，只要
訂單有付款金額、退款去向選原路退款，就直接回 422，根本不會呼叫到 application
service，BE-17 蓋好的綠界退款路徑完全沒有機會被觸發。

有一條既有測試明確鎖死這個行為：
tests/GreyGray.M1a.CheckoutOrdering.Tests/AdminCancelLineEndpointTests.cs:66-87
建一筆已付款訂單，用 OriginalPaymentMethod 呼叫取消，斷言回應是 422 且
ordering.CancelLineCalls.ShouldBe(0)。176 條測試全過的原因是這條測試本身
就在幫這個過期行為背書，這條測試要照新行為改寫，不是刪掉。

必做：
1. 拿掉 M1aEndpoints.cs 兩處守衛。不要另外加任何新守衛去「補償」——
   StoredValue 已經由 GuardStoredValueRefund 在 application service 層
   擋到 M3，那一層不用你動，也不要動。
2. 改寫 AdminCancelLineEndpointTests.cs:66-87，驗證新行為：
   ordering.CancelLineCalls 應該變成 1（真的呼叫到 application service），
   照這個檔案既有的 fake 慣例寫，不要新增一整套 mock framework。
3. 真的對 D:\GreyGray 開發環境跑一次原路退款驗證路徑通了；如果環境裡沒有
   現成已付款訂單，退而求其次直接呼叫 OrderingApplicationService.CancelAdminAsync
   或寫一條新整合測試證明會發布 RefundRequested 事件，在自驗報告寫清楚
   你用的是哪一種驗證方式。

你自己不能宣告這一包通過或修完收工，你只能做完並交付、寫自驗報告，
由 Leader 做整合驗收與最終判斷。

檔案所有權（只准改這些路徑，見 docs/26 §3）：
  src/Hosts/GreyGray.Api.Admin/M1aEndpoints.cs
  tests/GreyGray.M1a.CheckoutOrdering.Tests/AdminCancelLineEndpointTests.cs
  .dispatch/reports/BE-30.md（你的自驗報告）
  docs/、management/、STATE.md、CLAUDE.md、AGENTS.md（任何一包都寫得了）

★ 這一波同時有另一個子代理在跑 BE-29，改的是三個模組的 Repository 檔案，
  跟你的所有權不重疊，兩邊互不相依，不用互相等待。dotnet build／ops/test.ps1
  如果出現看不懂的短暫錯誤先重跑一次再回報，不要立刻假設是自己的程式碼錯了。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。stash stack 是跨 worktree 共用的。

自驗完成後，把結果寫進 .dispatch/reports/BE-30.md，三個標頭一字不差：
## 指令與輸出
## 逐條自驗
## 我發現但沒做的事

寫完就停下來，等 Leader 做整合驗收。你不可以自己宣告通過。
```
