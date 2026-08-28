# Codex 啟動 prompt

**直接複製下面整段貼給 Codex。** 一次只啟動一個波次，不要一口氣把四波都丟過去——
上一輪的經驗是一次給太多包，它會在第三包開始亂編介面。

> **2026-08-28 補**：第一波交付後它**沒有停**，自己往下做起 BE-2
> （`ProcessedMessage`、`IdempotentIntegrationEventHandler`）。交付本身沒被汙染
> （那些檔案還沒提交），但驗收得先把它們排除才算得準。
> 所以每一則 prompt 的結尾都要有「做完就停」那一段，不要只寫「只做這三包」——
> 「只做這三包」它理解成範圍，不理解成終止條件。

---

## 第一則：第一波（BE-1 / BE-4 / BE-8）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray_Platform
GreyGray Platform 後端。模組化單體，.NET 10，14 個限界上下文，硬邊界。
你負責後端。前端由另一組人同時進行，兩邊唯一的接觸面是已凍結的 OpenAPI 契約，
你不會也不需要看到任何前端程式碼。

【先讀，不要跳過，讀完再動手】
  CLAUDE.md                  六條鐵則
  STATE.md                   現況與已知問題
  docs/07-後端派工書.md        ← 主文件。§0 是「稽核已經改過的東西」，那些是既成事實
  docs/00-decisions.md       18 條 ADR。已決定的不要重新討論，
                             也不要「順手改成更好的做法」
  docs/01-系統結構.md         組件參考規則與 Contracts DAG
  docs/02-事件與狀態機.md      事件目錄、狀態機、補償路徑、帳務分錄
  docs/03-M0工作包.md         每一包的產出與驗收主體在這裡
  docs/05-API契約.md          前後端唯一的邊界，已凍結

【這次的範圍】
docs/07-後端派工書.md 的**第一波**，三包：
  BE-1  Outbox ＋ 事件型別登錄       ← 最重要，做錯後面全部會壞
  BE-4  OTel ＋ IClock ＋ ICorrelationContext
  BE-8  CI 與部署腳本

**只做這三包。** 第二波（BE-2 / BE-3）等這一波驗收過再說。
看到 BE-5、BE-6、BE-7 或任何 M1a 的東西都不要碰。

【怎麼做】
這三包彼此不相干，請開三個子代理平行處理，一包一個。
檔案所有權表在 docs/07-後端派工書.md §2——**只准改自己那一包擁有的路徑**。
需要改共用檔（Directory.Packages.props、Platform.Abstractions/**、Shared.Kernel/**、
任何 *.Contracts、Architecture.Tests、0001/0002 migration、docs/**）就停下來回報，
不要自己改，那些檔案別的子代理也在動。

【六條鐵則】
1. 金額一律 GreyGray.Shared.Kernel.Money（long 最小單位）。
   任何地方出現 decimal price 或 double amount 都是 bug，不管看起來多方便。
2. 時間一律經 IClock，不要直接 DateTimeOffset.UtcNow。
3. 模組只能參考別人的 *.Contracts，永遠不可以參考 *.Core。
4. 禁止跨 schema JOIN，沒有例外。ADR-016 講的是 outbox 表要 map 進模組的
   DbContext——那是同一交易寫兩張表，不是 JOIN，不要拿它當放寬這條的理由。
5. 可預期的業務失敗回 Result / Result<T>，例外留給「不該發生」的狀況。
6. 註解與 XML doc 用繁體中文，命名用英文。

【三個最容易做錯的地方，做錯了後面全部要重來】
1. PublishAsync 只寫 outbox 不派送，而且**必須參與呼叫端的交易**——
   同一個 DbContext、同一條連線，SaveChanges 一次寫掉業務資料與 outbox。
   做法是 ADR-016：Platform 提供 modelBuilder.AddPlatformTables()，每個模組的
   DbContext 都套用。**不要**把 outbox 關在獨立的 PlatformDbContext 裡，
   那是另一條連線、另一個交易，原子性直接失效。
2. Dispatcher 取批次用 SELECT ... FOR UPDATE SKIP LOCKED。
3. 派送前用 SET LOCAL app.tenant_id —— **LOCAL 不是 SET**。
   pgBouncer 的 transaction pooling 會讓 session 變數跨交易洩漏，
   症狀是隨機的跨租戶讀取，在測試環境幾乎不可能重現。

【每包都要自驗，而且要貼出實際輸出】
  dotnet build .\GreyGray.slnx      必須 0 error 0 warning
  .\ops\test.ps1                    現有 28 條必須全綠（架構 12 ＋ 契約 16）
  （不要用 dotnet test，理由寫在 ops/test.ps1 的註解裡）

外加 docs/07-後端派工書.md 裡該包「自驗」段列的每一條，**逐條貼出實際輸出**，
不是回報「已完成」四個字。

**特別要求**：任何斷言「某件事不存在／不會發生」的測試，都要注入一次違規、
看它變紅、再改回來，並把紅的那次輸出也貼出來。
上一輪就是在這裡踩過坑——第一版架構測試用組件參考寫，故意注入
Ordering.Core → Ledger.Core 之後 8/8 仍然全綠，因為編譯器把沒用到的參考裁掉了。
沒有注入驗過的「綠」不算數。

【交付時要給我】
1. 每包動過的檔案清單（我要驗證沒有越界）
2. build 與 test 的完整輸出
3. 每一條自驗的實際輸出
4. **你認為規格有問題的地方**——不要默默繞過。
   架構測試擋下你的時候，那是它在做它該做的事，不要改測試去繞過；
   真的認為規則錯了就停下來說明理由。
5. 更新 STATE.md：完成了什麼、還沒做什麼、**踩到什麼坑**。
   最後一項最有價值，半年後回來看會慶幸當時有寫。

我收到之後會做總驗收（docs/07-後端派工書.md §5 的十條），
任何一條不過就整包退回，不做部分接受。
```

---

## 第二則起

把「這次的範圍」換成下一波，其餘不動：

| 波次 | 包 | 前提 |
|---|---|---|
| 第二波 | BE-2 消費端冪等 · BE-3 Idempotency 與 Saga Timer | BE-1 驗收過 |
| 第三波 | BE-5 模組組合根樣板 · BE-7 通路接縫進 schema | BE-2 / BE-3 驗收過 |
| 第四波 | BE-6 hello-world 端對端 | 前三波全過。**這一包通過就是 M0 完成** |

第三波的 BE-5 要特別交代一句：

```
BE-5 先只做 Identity 與 Catalog 兩個模組的組合根，做完就停下來給驗收。
不要一口氣做完 14 個——形狀錯了要改 14 遍。
```
