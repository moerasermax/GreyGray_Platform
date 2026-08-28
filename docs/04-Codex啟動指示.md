# Codex 啟動指示

貼給 Codex 的第一則訊息。**一次只給一個工作包**，做完驗收再給下一個——
一次丟八個包，它會在第三個包就開始亂編介面。

---

## 通用前綴（每次都帶）

```
專案：D:\WorkSpace\01_開發中_wip\daigou-platform
代購平台的後端。模組化單體，.NET 10，14 個限界上下文，硬邊界。

開工前務必先讀，不要跳過：
  README.md                 四條硬規則
  STATE.md                  現況與已知問題
  docs/00-decisions.md      14 條 ADR —— 已決定的不要重新討論，也不要「順手改成更好的做法」
  docs/01-系統結構.md        組件參考規則與 Contracts DAG
  docs/02-事件與狀態機.md     事件目錄、狀態機、補償路徑、帳務分錄
  docs/03-M0工作包.md        你要做的那一包在這裡

鐵則：
1. 金額一律用 Daigou.Shared.Kernel.Money（long 最小單位）。
   任何地方出現 decimal price 或 double amount 都是 bug，不管看起來多方便。
2. 時間一律經 IClock，不要直接 DateTimeOffset.UtcNow。
3. 模組只能參考別人的 *.Contracts，永遠不可以參考 *.Core。
4. 禁止跨 schema JOIN，沒有例外。
5. 可預期的業務失敗回 Result / Result<T>，例外留給「不該發生」的狀況。
6. 註解與 XML doc 用繁體中文，命名用英文。

每次改完都要跑（不要用 dotnet test，理由在 ops/test.ps1 的註解裡）：
  dotnet build .\Daigou.slnx
  .\ops\test.ps1
架構測試必須全綠。**如果它擋下你，那是它在做它該做的事，不要改測試去繞過。**
真的認為規則錯了，就停下來說明理由，不要自己改。
```

---

## 第一則：M0-1 Outbox

```
（貼上通用前綴）

這次做 docs/03-M0工作包.md 的 M0-1（Platform：Outbox 實作），只做這一包。

產出：
  src/Platform/PlatformDbContext.cs
  src/Platform/Outbox/ 底下的 IEventPublisher 與 IOutboxDispatcher 實作
  對應的整合測試（用 Testcontainers.PostgreSql，不要用 InMemory provider——
  它不會執行 CHECK constraint，而帳務的不變式正是靠 constraint 守的）

三個必須做對的地方，做錯了後面全部會壞：
  1. PublishAsync 只寫 outbox，不派送，而且必須參與呼叫端的交易
     （同一個 DbContext / 同一條連線，SaveChanges 一次寫掉業務資料與 outbox）
  2. Dispatcher 取批次用 SELECT ... FOR UPDATE SKIP LOCKED
  3. 派送前用 SET LOCAL app.tenant_id（LOCAL，不是 SET）

驗收（做完請自己跑過，並把結果貼出來）：
  - 交易 rollback 後 outbox 沒有那筆
  - 兩個 dispatcher 同時跑 100 則，每則只被派送一次
  - handler 丟例外 → attempts 遞增、next_attempt_at 往後推、訊息沒被標 processed

schema 已經寫好在 db/migrations/0002_platform.sql，欄位不要自己改。
如果你認為欄位不夠用，先說，不要直接改 SQL。
```

---

## 之後每一則

把「M0-1」換成下一個工作包編號，把「產出」「驗收」段落換成 `docs/03-M0工作包.md`
裡那一包寫的內容。順序建議照編號走，M0-1 是所有事情的前提。

## 收工時

要求它更新 `STATE.md`：完成了什麼、還沒做什麼、**踩到什麼坑**。
最後一項最有價值——半年後回來看，會慶幸當時有寫。
