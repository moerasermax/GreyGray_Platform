# 啟動 prompt

**你只開一個 terminal，當 Leader。** 這一波只有一包。

這一波前端樹沒有生效包，純同步用。

---

## 你要做的只有這一步

開一個 terminal，貼下面這段。**就這樣。**

```
GG_ROLE=leader

你是 GreyGray Platform 的 Leader。讀 .dispatch/PROMPTS.md 與 .dispatch/ACTIVE.md，
把現在生效的 BE-31 用 mcp__ai-cli__run 派出去，然後等它回來做整合驗收。

派工規則：
  - 子代理的 prompt 用本檔案「工作包」那一節的原文，一字不改。
    開頭的 GG_PACKAGE=BE-31 那一行一定要留著——ai-cli 的 run 沒有 env 參數，
    包別只能靠 prompt 帶進去，UserPromptSubmit 會把它綁到那個子代理的 session_id。
  - workFolder：D:\WorkSpace\01_開發中_wip\GreyGray_Platform（後端樹）。
    這一波不涉及前端樹，不需要派任何前端子代理。
  - 開工前先確認 git log 看得到 BE-29／BE-30 的提交（`dc0ea1f`），且
    dotnet build／ops/test.ps1 在動手之前就是全綠的基準線，不成立就停下來回報。

★ 收子代理的回報時，先看 .dispatch/reports/BE-31.md 在不在、三個標頭齊不齊。
  不齊就用同一個 session_id 接回去要它補完——不要自己幫它補，
  也不要因為 exit code 是 0 就當成完成。

你自己不寫原始碼。你寫得了的是 .dispatch/、.claude/、.codex/、docs/ 與 GreyGray_PM。
子代理回來之後由你做整合驗收：自己重跑 build 與測試複驗，並且**親自對
POST /v1/orders/{orderId}/lines/{lineId}/refund-shortfall 送真請求**，走一次
「部分買到→退短缺款」的完整流程（docs/27 §5），不要只轉述自述。驗收完先
commit，再撤包——順序反過來會讓未提交的交付變成無主檔案，閘門會判成越界。
```

> **不要把 `GG_ROLE=leader` 放進子代理的 prompt。** 就算不小心放了也升不了級——
> `claim-package.sh` 讓包別優先於角色（已實測），但別依賴那道保險。

---

## 排程

```
只有一包：BE-31（相依 BE-29／BE-30 已提交，見 docs/27 §1）
```

---

## 兩條這一波開始機械檢查的規則

**① 自驗報告是檔案，不是對話。**
`.dispatch/reports/BE-31.md`，三個標頭一字不差：
`## 指令與輸出`、`## 逐條自驗`、`## 我發現但沒做的事`。
缺任何一個，`audit-dispatch.sh` 第 ⑧ 項會擋下 Leader 收工。

**② 不准把測試丟背景、不准排程 wakeup。**
`ops/test.ps1` 淨執行已超過工具單次前景呼叫的 10 分鐘上限，
分批前景跑完（照 `ops/test.ps1` 自己的迴圈，逐一跑每個測試專案的執行檔）。

---

## 工作包（以下這一段就是子代理的 prompt，原文照抄）

### BE-31　支援部分買到（ADR-026）

```
GG_PACKAGE=BE-31

你是 GreyGray Platform 的 BE-31。讀 docs/27-後端第十五波派工書.md，
§0 §1 全部要看，然後照 §5 的 BE-31 那一節做。
再讀 .dispatch/reports/README.md（自驗報告的格式）。

開工前先確認 git log 看得到 BE-29／BE-30 的提交（dc0ea1f），且 dotnet build／
ops/test.ps1 在你動手之前就是全綠的基準線，不成立就停下來回報。

這一波要做的事：ADR-026 支援部分買到。契約已拍板：買到的數量照常出貨，
短缺的數量退款。相依的 ADR-024（綠界退款 API）已完成。現在
PurchaseItemAggregate.MarkPurchased 與 Order.RecordItemPurchased 都刻意拒絕
部分買到（quantityPurchased != 需求／訂購數量一律回業務失敗），那是 M1b-1
當時正確的 fail-closed，現在退款去向已經拍板，要放寬。

設計方向（已經定死，不要重新設計，細節見 docs/27 §0）：
- Fulfillment／Shipment 完全不記錄數量，這一包不影響出貨模組，範圍只在
  Procurement 與 Ordering 兩邊，Ledger 不用新增任何程式碼——短缺的退款直接
  重用既有的 RefundRequested 事件與下游 Payment／Ledger 消費者。
- 沿用 M1b-2「決策延後」模式：標記買到當下不問退款去向，短缺數量掛著，
  之後另一個獨立端點問客人要退到哪裡才真的退款。
- Procurement 側：PurchaseItemAggregate.MarkPurchased 放寬部分買到即可，
  不需要新欄位。
- Ordering 側：OrderLine 新增 QuantityShortfall（int，migration 0015_），
  MarkPurchased 改收 quantityPurchased 參數並計算短缺；新增
  Order.RefundLineShortfallByAdmin(OrderLineId) 回傳 Result<Money>（比照既有
  CancelLineByAdmin 的形狀）；重用既有 RefundedAmountMinor／RefundedCurrency
  欄位記錄短缺退款金額，退款完成時才把 Quantity 減下去。
- 新增 IOrderingApplication.RefundLineShortfallAsync 介面方法與
  OrderingApplicationService 實作，重用既有 GuardStoredValueRefund
  （StoredValue 一樣要擋到 M3）。
- 新開檔案 src/Hosts/GreyGray.Api.Admin/M1bShortfallRefundEndpoints.cs
  （不要改 M1aEndpoints.cs 的既有端點邏輯，比照 M1bCompensationEndpoints.cs
  的寫法），新增 POST /v1/orders/{orderId}/lines/{lineId}/refund-shortfall，
  Program.cs 掛一行。
- 契約異動：openapi.admin.yaml 新增這個 operation（完全比照既有的
  /v1/orders/{orderId}/lines/{lineId}/cancel 抄一份）、AdminOrderLine 加
  quantityShortfall 欄位；OpenApiComponents.cs 的 IdempotentEndpoints 補一行；
  M1aEndpoints.cs 這一波只准動 AdminOrderLineResponse 這一個 DTO 與組裝它的
  那一行，其餘不准動——這個檔案 BE-30 剛提交過，先用 git log 確認你看到的是
  最新版本，行號自己重新搜尋。

明確不做：前端 UI（獨立下一波，不跨樹去改）、不新增 Procurement 契約欄位或
事件、不動 Fulfillment、不處理「退款之後反悔」這種情境。

你自己不能宣告這一包通過或修完收工，你只能做完並交付、寫自驗報告，
由 Leader 做整合驗收與最終判斷。

檔案所有權（只准改這些路徑，見 docs/27 §3）：
  src/Modules/Procurement/GreyGray.Modules.Procurement.Core/PurchaseItemAggregate.cs
  src/Modules/Ordering/GreyGray.Modules.Ordering.Core/Order.cs
  src/Modules/Ordering/GreyGray.Modules.Ordering.Contracts/OrderingContracts.cs
  src/Hosts/GreyGray.Api.Admin/M1bShortfallRefundEndpoints.cs（新檔）
  src/Hosts/GreyGray.Api.Admin/Program.cs（只加一行）
  src/Hosts/GreyGray.Api.Admin/OpenApiComponents.cs（只加一行）
  src/Hosts/GreyGray.Api.Admin/M1aEndpoints.cs（只准動 AdminOrderLineResponse
    這一個 DTO 與組裝它的那一行，其餘不准動）
  docs/api/openapi.admin.yaml
  db/migrations/0015_*.sql
  tests/
  .dispatch/reports/BE-31.md（你的自驗報告）
  docs/、management/、STATE.md、CLAUDE.md、AGENTS.md（任何一包都寫得了）

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。stash stack 是跨 worktree 共用的。

自驗要包含：build 0/0；ops/test.ps1 全綠且前景分批跑完（不准丟背景、不准
排程 wakeup）；端對端測試證明「5 件訂 3 件」的情境下 QuantityShortfall==2、
Quantity 暫時仍是 5（退款決定前）、退款後 Quantity 變 3 且 RefundedAmount
等於 2 件單價、Order 總額正確減少；冪等測試（同一筆 RecordItemPurchased
送兩次不重複、RefundLineShortfallAsync 對同一條 line 送兩次不重複退款）；
不同內容重送要回業務失敗不能覆寫；StoredValue 仍被擋在 M1b。

自驗完成後，把結果寫進 .dispatch/reports/BE-31.md，三個標頭一字不差：
## 指令與輸出
## 逐條自驗
## 我發現但沒做的事

寫完就停下來，等 Leader 做整合驗收。你不可以自己宣告通過。
```
