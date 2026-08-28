# Codex 啟動 prompt

**直接複製下面整段貼給 Codex。** 一次只啟動一個波次，不要一口氣把四波都丟過去——
上一輪的經驗是一次給太多包，它會在第三包開始亂編介面。

> **2026-08-28 補**：第一波交付後它**沒有停**，自己往下做起 BE-2
> （`ProcessedMessage`、`IdempotentIntegrationEventHandler`）。交付本身沒被汙染
> （那些檔案還沒提交），但驗收得先把它們排除才算得準。
> 所以每一則 prompt 的結尾都要有「做完就停」那一段，不要只寫「只做這三包」——
> 「只做這三包」它理解成範圍，不理解成終止條件。
>
> **後來又發生三次**（第二波、第三＋四波、M1a），共四次。交付品質每次都沒問題，
> 但「整包退回、不做部分接受」這條規則**只有在東西還小到退得掉時才執行得了**。
> M1a 那次累積到 105 個未提交檔案才被發現。

---

## 第 0 則：後端主 agent 的啟動 prompt（**開 terminal 先貼這則**）

貼給後端那個 Codex terminal。它不自己寫全部的碼，它負責**指揮子代理 ＋ 自驗 ＋ 停**。

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray_Platform
你是 GreyGray Platform 的**後端主 agent**。前端在另一個工作區（GreyGray_Platform-fe）
平行進行，兩邊唯一的接觸面是已凍結的 OpenAPI 契約，你不會也不需要看到前端程式碼。

【先讀，全部讀完再動手】
  CLAUDE.md                  六條鐵則
  STATE.md                   現況、三波總驗收結果、已知問題
  docs/07-後端派工書.md        ← 主文件。§0 是稽核已改過的既成事實，
                             §2 檔案所有權表，§5 是總驗收的十條
  docs/00-decisions.md       19 條 ADR。已決定的不要重新討論，
                             也不要「順手改成更好的做法」
  docs/05-API契約.md          前後端唯一的邊界，已凍結。
                             **「凍結之下工具可以改什麼」那一節一定要讀**
  docs/01 / docs/02 / docs/03 結構、事件與狀態機、工作包

【四條鐵則】
1. **一次只做一個波次。做完就提交，然後停下來等指令。**
   不要因為看到 TODO 就順手做掉，不要因為下一包很簡單就先開始。
   已經連續四次沒停了，這條是現在最重要的一條。
2. **檔案所有權表是「同一波之內」的邊界**（docs/07 §2）。
   後續波次為接線而改前面波次的檔案是正常的；要擋的是同一波兩個子代理互蓋。
3. **契約凍結**。要動 docs/api/*.yaml 先看 docs/05 的「工具可以改什麼」：
   允許正規化格式、禁止改變語意，判準是重跑 codegen 後 TS 型別逐字節相同。
   要改語意 → 停下來回報，不要自己改。
4. **總驗收由人做，不是由你自己說了算**（docs/07 §5）。
   你可以寫「自驗結果」，不要寫「驗收結果」。

【每一包交付時自己要跑並貼出實際輸出】
  dotnet build .\GreyGray.slnx      必須 0 error 0 warning
  .\ops\test.ps1                    現有測試必須全綠，新增的另計
  git status --porcelain            確認沒有越出所有權表

**任何斷言「某件事不會發生」的測試，都要注入一次違規、看它變紅、再改回來，
並把紅色輸出貼出來。** 沒被注入驗證過的規則等於還沒有規則。
而且要注入**這一波新加的那條**，不要重複注入舊的。

【做完就停】
本波全部子代理交付、你自驗過、提交完成之後——**停**。
把「這一波做了什麼、自驗輸出、你認為有問題的地方」整理出來，然後等指令。
下一波什麼時候開始由人決定。
```

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

---

# M1a：BFF 端點實作

M0 程式已完成（`2bb1e7d`）。M1a 是照 `docs/api/openapi.storefront.yaml` 與
`openapi.admin.yaml` 把凍結契約的端點實作出來。

`docs/07` §6 原本寫「五包平行」，那是**錯的**——那五包之間有真的相依。
正確的波次依相依關係排：

| 波次 | 包 | 前提 | 為什麼 |
|---|---|---|---|
| M1a-1 | Identity · Catalog | 無 | 兩個都不依賴別人，而且是所有東西的底 |
| M1a-2 | Pricing · Campaign | Catalog | 運費試算要 SKU 的重量與尺寸；開團要掛商品 |
| M1a-3 | Checkout · Ordering | Catalog · Pricing · Campaign | 購物車報價要三者齊備；下單接在購物車後面 |
| M1a-4 | Payment（綠界）· Ledger | Ordering | 沒有訂單就沒有金流；分錄記的是訂單與收款 |

**Ledger 一定要排在最後**，不要為了「帳務很重要」而提前。

## M1a 的實際結果（2026-08-28）

Codex 沒照這張表走，**一次做完全部四波**並提交成 `6158077`
（120 檔案／17,591 行／8 個業務模組／6 個新測試專案）。

驗收結果：`build 0/0`、**108 條測試全綠**、十條裡九條過。
M1a 實質通過，真正缺的只有 `/v1/orders/{orderId}/lines/{lineId}/cancel` 一條端點，
以及一批「行為正確但 OpenAPI 沒宣告」的 metadata。

## 驗收第 9 條的正確做法（我上次做錯了）

`ops/check-openapi.ps1` 比對的是**整份凍結契約**，而那份契約
**涵蓋 M1a、M1b、M2 全部的端點**。直接拿 gate 的「缺少 paths」當成
這一波的缺漏，會把「還沒排到的工作」報成「少做的東西」。

我第一次驗 M1a 就這樣錯了：報「缺 12 個端點、M1a 實質未完成」，
還據此在這份文件加了一個不存在的 M1a-5 波次。實際上 12 條裡：

```
11 條是 M1b／M2   （purchase-items、shipments、trip-costs、inquiries reply、lots…）
 1 條是 M1a       （/v1/orders/{orderId}/lines/{lineId}/cancel）
```

**`docs/05-API契約.md` 的端點索引自己標了里程碑**，M1b 的在描述欄寫「（M1b）」、
storefront 那張表有里程碑欄位。驗收前先用它過濾出這一波該有的 paths，
再拿那個子集去比對 gate 的輸出。

## 連帶：gate 在 M2 之前永遠不會綠

既然它比對整份契約，那麼**在 M2 做完之前這一關必定 FAIL**。
這跟第一波留下的「CI 長期紅燈」是同一類問題，而且更久——
第一波那個在 M1a 端點接上後就會轉綠，這個要等到 M2。

**要嘛讓 gate 支援按里程碑過濾（比對時只取當前里程碑的 paths），
要嘛明確接受它到 M2 之前都是紅的。** 不處理的話「gate 紅」會變成
永久背景雜訊，真的契約漂移發生時沒有人會注意到。
