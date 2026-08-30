# 啟動 prompt

**你只開一個 terminal，當 Leader。** 這一波只有一包，由 Leader 用 ai-cli fan out 出去。

這一波前端樹沒有生效包，純同步用。

---

## 你要做的只有這一步

開一個 terminal，貼下面這段。**就這樣。**

```
GG_ROLE=leader

你是 GreyGray Platform 的 Leader。讀 .dispatch/PROMPTS.md 與 .dispatch/ACTIVE.md，
把現在生效的 BE-27 用 mcp__ai-cli__run 派出去，然後等它回來做整合驗收。

派工規則：
  - 子代理的 prompt 用本檔案「工作包」那一節的原文，一字不改。
    開頭的 GG_PACKAGE=BE-27 那一行一定要留著——ai-cli 的 run 沒有 env 參數，
    包別只能靠 prompt 帶進去，UserPromptSubmit 會把它綁到那個子代理的 session_id。
  - workFolder：D:\WorkSpace\01_開發中_wip\GreyGray_Platform（後端樹）。
    這一波只動 Ordering／Fulfillment 兩個模組與對應測試，不涉及前端樹，
    不需要派任何前端子代理。

★ 收子代理的回報時，先看 .dispatch/reports/BE-27.md 在不在、三個標頭齊不齊。
  不齊就用同一個 session_id 接回去要它補完——不要自己幫它補，
  也不要因為 exit code 是 0 就當成完成。

你自己不寫原始碼。你寫得了的是 .dispatch/、.claude/、.codex/、docs/ 與 GreyGray_PM。
子代理回來之後由你做整合驗收：自己重跑 build 與測試複驗，並且**親自對
admin Host 的 /v1/orders、/v1/campaigns、/v1/shipments 三個端點各送一次請求**
（docs/24 §6），確認真的不再掛住，不要只轉述它的自述。驗收完先 commit，
再撤包——順序反過來會讓未提交的交付變成無主檔案，閘門會判成越界。
```

> **不要把 `GG_ROLE=leader` 放進子代理的 prompt。** 就算不小心放了也升不了級——
> `claim-package.sh` 讓包別優先於角色（已實測），但別依賴那道保險。

---

## 排程

```
只有一包：BE-27（不拆包——Ordering 與 Fulfillment 兩邊的修法互相牽制）
```

---

## 兩條這一波開始機械檢查的規則

**① 自驗報告是檔案，不是對話。**
`.dispatch/reports/BE-27.md`，三個標頭一字不差：
`## 指令與輸出`、`## 逐條自驗`、`## 我發現但沒做的事`。
缺任何一個，`audit-dispatch.sh` 第 ⑧ 項會擋下 Leader 收工。

**② 不准把測試丟背景、不准排程 wakeup。**
`ops/test.ps1` 淨執行已超過工具單次前景呼叫的 10 分鐘上限，
分批前景跑完（照 `ops/test.ps1` 自己的迴圈，逐一跑每個測試專案的執行檔）。

---

## 工作包（以下這一段就是子代理的 prompt，原文照抄）

### BE-27　修 Ordering ↔ Fulfillment 循環相依，解除 admin BFF 三組端點永久掛住

```
GG_PACKAGE=BE-27

你是 GreyGray Platform 的 BE-27。讀 docs/24-後端第十二波派工書.md，
§0 §1 全部要看，然後照 §5 的 BE-27 那一節做。
再讀 .dispatch/reports/README.md（自驗報告的格式）。

這一波要解的問題：admin BFF 三組端點（/v1/orders、/v1/campaigns、/v1/shipments）
永久掛住不回應。Leader 這一輪已經用 dotnet-dump 對 D:\GreyGray 的 admin Host
抓過三次記憶體傾印，確診根因：OrderingApplicationService 的工廠
（Ordering.Infra/ModuleRegistration.cs:80）用 GetService<IFulfillmentQuery>()
解析 Fulfillment，而 FulfillmentApplicationService 的工廠
（Fulfillment.Infra/ModuleRegistration.cs:63）用 GetRequiredService<IOrderQuery>()
解析回 Ordering——兩邊都用工廠委派手動呼叫 GetRequiredService／GetService，
不是建構式自動注入，.NET DI 內建的循環偵測看不到，形成建構時期的循環相依。
只有 admin Host 同時掛 Ordering 與 Fulfillment 兩個模組才會觸發，storefront／
Worker 都不掛 Fulfillment 模組所以沒事。dotnet-dump 證實：三個執行緒卡在
DI 容器內部的 Monitor.Enter_Slowpath，同時查到 346～714 個 OrderingRepository
執行個體（正常一個請求最多 1 個）——工廠被重複呼叫，不是單純卡住等待。

★ 不是資料庫、不是 Redis／Garnet、不是認證、不是併發競態——這四個 Leader
  這一輪都已經實測排除（詳見 docs/24 §0 的排除過程表），不要重查。

★ 兩邊的用途都是真的業務需求，不能刪：OrderingApplicationService.RecordShipmentDeliveredAsync
  要問 Fulfillment「這張訂單的出貨單是不是全部簽收了」才能起算鑑賞期 Saga Timer；
  FulfillmentApplicationService.CreateShipmentAsync 要問 Ordering 拿訂單明細建出貨單。
  要改的是「建構時機」，不是刪依賴。

★ 建議修法（docs/24 §1 有完整理由）：把 OrderingApplicationService 對
  IFulfillmentQuery 的依賴改成 System.Lazy<T> 延遲解析，只改 Ordering 這一側，
  Fulfillment 那一側的 IOrderQuery（急切）不用動——因為只要有一側延遲，
  繞回起點時那個型別已經解析完成並快取在同一個 scope，不會觸發第二次工廠呼叫。
  不要用 IServiceProvider 直接注入進 Core 專案（那會把 DI 容器套件拉進 Core，
  違反模組邊界）。這是建議方向，不是唯一解——如果你發現更好的作法，只要
  不違反六條鐵則、且用 §6 自驗證明三個端點真的不再掛住，就可以改用。

四個路徑：Ordering.Infra/ModuleRegistration.cs、Ordering.Core/OrderingApplicationService.cs、
Fulfillment.Infra/ModuleRegistration.cs、Fulfillment.Core/FulfillmentApplicationService.cs
（後兩個給你是留彈性，不代表一定要動——先驗證只改 Ordering 側夠不夠），
加上 tests/GreyGray.M1b.Fulfillment.Tests/ 與 tests/GreyGray.M1a.CheckoutOrdering.Tests/。
詳細怎麼做、每一步的理由都在 docs/24 §5，照著做，不要自己另外設計一套。

必做 3 是重點：全 repo 現在沒有任何測試同時用真的 DI 容器組出 Ordering ＋
Fulfillment 這個組合（只有 admin Host 這樣組），所以這個 bug 完全沒被
175 條測試涵蓋到。你要新增一條這樣的迴歸測試，用有上限的 timeout 包住
解析呼叫（例如 5 秒），超時要讓測試明確失敗，不能讓它卡死整個測試套件。

驗收核心是「真的把 admin Host 跑起來，對三個端點送請求」，不是只跑單元測試——
D:\GreyGray 的三個 Host、Postgres、Garnet 應該已經在跑（不用重新 install），
對 http://127.0.0.1:5001/v1/orders、/v1/campaigns、/v1/shipments 各送一次請求
（不帶 cookie，未認證應該回 401，耗時要是毫秒等級），貼出實際狀態碼與耗時。

ops/test.ps1 要前景跑（超過 10 分鐘上限就分批跑），不准丟背景、不准排程 wakeup。
自驗寫進 .dispatch/reports/BE-27.md，三個標頭一字不差。
```

---

## 這一包適用的四件事

1. **子代理不可以自己宣告通過。** 自驗報告寫成檔案，貼**實際指令與實際輸出**。
   整合驗收是 Leader 的事，而且 Leader 要自己重跑複驗。
2. **遇到契約缺口或平台缺口就停下來回報**，不要自己補一個看起來合理的預設值。
3. **你只有這一輪。** 不准排程 wakeup，不准把工作丟背景後結束。
   無法完成就**明講做不到與原因**。
4. **不要碰整個工作區的 git 指令**（`git stash`／`reset --hard`／`clean`／
   `checkout -- .`／`commit`）。stash stack 是跨 worktree 共用的。
