# GreyGray Platform

代購業務系統（出國採購開團 ＋ 本地批發現貨）。模組化單體，.NET 10，14 個限界上下文。

**現在的狀態（2026-08-29）**：M0 地基完成、M1a 通過獨立驗收、M1b-1 已交付待驗；
前端 FE-1～FE-8 八包全部通過並收完技術債。
**程式走了一大半，但整合與測試（D）與上線（E）幾乎是空的**——程式寫完不等於能上線。

跨 worktree 的單一事實來源是 `GreyGray_PM/00-進度總表.md`，**刻意放在 worktree 之外**。
`STATE.md` 曾在兩棵樹上分岔、前端那份落後四波，所以不要只信它。

## 開工前讀這些（照順序，不要跳）

1. `.dispatch/ACTIVE.md` —— **現在派了哪幾包、你能動哪些路徑。沒有派工就不准寫原始碼。**
2. `GreyGray_PM/00-進度總表.md` —— 整個專案現在在哪一段
3. `docs/00-decisions.md` —— **21 條 ADR。已決定的不要重新討論，也不要「順手改成更好的做法」。**
4. `docs/05-API契約.md` —— **前後端唯一的邊界，已凍結。要改回來提，不得單方面改。**
5. 你做後端 → `docs/13-後端第五波派工書.md`（在後端 worktree，**§0 是既成事實，不要重做**）
   你做前端 → `docs/12-前端第三波派工書.md`（在前端 worktree）＋ `frontend/README.md`

`STATE.md`、`management/history/`、以及 `docs/03`／`06`／`07`／`10`／`11`
是**歷史脈絡**，要追「為什麼變成這樣」時才讀，不是現在的待辦。

規格來源不在 repo 裡，是兩個 Claude artifact：
[後端藍圖 v1.0](https://claude.ai/code/artifact/9a61eb42-df39-418e-9789-38b8ffa8a6f2)（23 張圖）、
[前台五套樣板](https://claude.ai/code/artifact/334496ce-48ac-4fd9-805f-19f64f66ac1b)（已選 Soft Seoul）。
需要圖或完整理由時才去讀，不必每次開啟。

## 六條鐵則

1. **金額一律用 `GreyGray.Shared.Kernel.Money`（`long` 最小單位）。**
   任何地方出現 `decimal price` 或 `double amount` 都是 bug，不管看起來多方便。
2. **時間一律經 `IClock`**，不要直接 `DateTimeOffset.UtcNow`——
   截團、逾期未付、詢價逾時放行這三條規則測不起來。
3. **模組只能參考別人的 `*.Contracts`，永遠不可以參考 `*.Core`。**
4. **禁止跨 schema JOIN，沒有例外。**
5. 可預期的業務失敗回 `Result` / `Result<T>`；例外留給「不該發生」的狀況。
6. 註解與 XML doc 用繁體中文，命名用英文。

前端另有四條（`frontend/README.md`）：只從 token 取顏色與尺寸、不做金額運算、
不猜業務規則、不直接 `fetch`。

## 建置與測試

```powershell
dotnet build .\GreyGray.slnx
.\ops\test.ps1
```

前端：

```bash
cd frontend
pnpm install
pnpm typecheck && pnpm build
pnpm api:generate     # 契約改了要重跑
```

**不要用 `dotnet test`。** SDK 10.0.301 ＋ xunit.v3 的組合下它會走 VSTest 路徑直接報錯，
兩種官方 opt-in 都試過沒生效（2026-08-28 實測）。`ops/test.ps1` 直接跑測試執行檔，結果一樣。

**架構測試擋下你的時候，那是它在做它該做的事。不要改測試去繞過。**
真的認為規則錯了，停下來說明理由，不要自己改。


### 不要碰整個工作區的 git 指令

`git stash`、`git reset --hard`、`git clean`、`git checkout -- .`、`git commit`
**一律不准**。同一棵 worktree 裡有別包在平行工作，他們的交付在被整合驗收之前都還沒 commit，
這些指令會把別人的東西一起處理掉；而且 stash stack 是**跨 worktree 共用**的，
別棵樹的 session 也會被波及。提交是整合者的事。

閘門（`PreToolUse`）會擋下這些指令，但你本來就不該試。
這不是假設性風險——FE-10 曾為了「取得乾淨的驗證基準」跑 `git stash`，
把 FE-9 已完成、還沒提交的交付與整合者正在改的文件整個掃走，
導致整合者當下那個 commit 只記錄到三個檔案裡的一個。

要乾淨的比較基準請用不動工作區的方式：`git diff`、`git diff --stat`。

## 驗收完就要更新進度表

**做完一次完整的整合驗收，必須同步 `GreyGray_PM/`。**
那個資料夾刻意不在任何 worktree 裡——`STATE.md` 曾在兩棵樹上分岔，前端那份落後四波，
在不同視窗看到不同的「現況」是最容易失去方向的一種故障。

1. `03-驗收紀錄.md` —— 誰驗的、怎麼驗的、結論、發現什麼
2. `00-進度總表.md` —— 階段狀態、基準 commit、「現在卡在哪」
3. `web/dashboard.html` 最上面的 `DATA`，改完重新發布

標 ✅ 之前一定要寫得出「怎麼驗的」：哪個指令、什麼輸出。**交付方不能自己標 ✅。**
`.claude/hooks/stop-gate.sh` 在整合者模式下會比對這三個檔的修改時間，
沒同步就擋下收工。誤判時 `touch` 較舊的那個檔案即可。

## 只能照派工書開工

**沒有派工書就不准寫原始碼。** 生效中的派工寫在 `.dispatch/ACTIVE.md`，
由整合者維護，格式與範例都在那個檔案裡。四個 hook 一起守這條規則：

| Hook | 做什麼 |
|---|---|
| `UserPromptSubmit` → `claim-package.sh` | 認出 prompt 裡的 `GG_PACKAGE=<包名>` 或 `GG_ROLE=leader`，綁到這個 session_id（給 ai-cli fan out 的子 agent 用） |
| `SessionStart` → `session-brief.sh` | 一開場就把「你這一包能動哪些路徑」送進 context |
| `PreToolUse`（Write／Edit）→ `dispatch-guard.sh` | 即時擋下派工範圍外的寫入 |
| `Stop` → `stop-gate.sh` | 實作者：用 `git diff` 再查一次越界——**這一層不能省**，因為用 Bash（`sed -i`、heredoc、重導向）寫的檔案繞得過 `PreToolUse`，但繞不過 git。整合者：越界 ＋ **每個生效中的包都要有啟動 prompt** ＋ `GreyGray_PM` 同步 |

**派工書寫完就要給得出啟動 prompt。** 放在 `.dispatch/PROMPTS.md`，一包一段，
可以直接複製貼上，不要讓人自己回去讀派工書再拼一段出來。
`ACTIVE.md` 裡每個生效的 `package:` 都必須在 `PROMPTS.md` 找得到
`GG_PACKAGE=<包名>`，否則 `Stop` 會擋下整合者收工。

**閘門自己的檔案（`.dispatch/`、`.claude/`、`.codex/`）只有「沒有綁定包別」的 session 寫得了。**
判準不是「有沒有派工生效」——那會讓閘門在派工期間變成沒人能維護，
而那正是要加派工、改 prompt、驗收後撤包的時機。實作者一定綁了包別
（prompt 都帶 `GG_PACKAGE=`），所以自我擴權那條路仍然堵死。

`docs/`、`.claude/`、`management/`、`STATE.md`、`CLAUDE.md` 不受限——那是整合與 PM 的工作，不是「開工」。
`ACTIVE.md` 沒有任何 `package:` 時是**整合者模式**：原始碼一律不准寫。
整合者自己要動原始碼，也要先在 `ACTIVE.md` 開一筆 `package:` 留下軌跡。

**只開一個 terminal 當 Leader。** Leader 用 ai-cli fan out 子代理，一包一個，
不必一包一個 terminal。Leader 的啟動 prompt 與每包的原文都在 `.dispatch/PROMPTS.md`。

**每個 session 都要宣告身分**，閘門分三種：

| 身分 | 怎麼宣告 | 寫得了什麼 |
|---|---|---|
| **Leader** | prompt 開頭 `GG_ROLE=leader`（或 `GG_ROLE=leader <cli>`） | 閘門檔、`docs/`、`GreyGray_PM`。**不寫原始碼** |
| **實作者** | prompt 開頭 `GG_PACKAGE=<包名>`（或 `GG_PACKAGE=<包名> <cli>`） | 只有該包 `allow:` 的路徑 |
| **身分不明** | 沒宣告 | 有派工生效時**什麼都寫不了** |

第三列是刻意的 fail-closed：**「忘記宣告的實作者」與「Leader」從外面看一模一樣**，
不能用「沒綁包別」推定是 Leader，否則忘記宣告的人就擁有改閘門的權力。

子代理只能靠 prompt 帶包別——ai-cli 的 `run` 沒有 env 參數，
子行程繼承的是 MCP server 自己的環境，一個 server 行程 spawn 所有子代理，
行程層級的環境變數本質上帶不了「每個子代理不同」的值，所以綁定走 session_id。
**包別優先於角色**：子代理的 prompt 就算混進 `GG_ROLE=leader` 也升不了級（已實測）。
包名拼錯一律擋下（fail-closed）。

**Codex 也受同一套閘門管。** 它不讀 `.claude/`，讀的是 `.codex/hooks.json` 與 `AGENTS.md`，
但判斷邏輯共用 `.dispatch/lib.sh`、狀態共用 `.dispatch/ACTIVE.md`——**派工狀態只有一份**。
兩邊的差別只在介面：Codex 的 payload 從 argv 或 stdin 進來、寫檔走 `apply_patch`
（路徑藏在 patch 內文的 `*** Add/Update/Delete File:` 標記裡，不是 `file_path`）、
deny 是 `{"decision":"deny"}` 加 exit 2。

派工書：前端 `docs/12-前端第三波派工書.md`、後端 `docs/13-後端第五波派工書.md`（在後端 worktree）。
**子代理只做自己那包的「自驗」，不能自己宣告通過**；整合驗收是整合者的事。

## 三個會咬人的地方

- **`Microsoft.OpenApi` 釘在 2.12.2，不要跳 3.x。**
  `Microsoft.AspNetCore.OpenApi` 的 source generator 產的碼依賴 2.x 的 API 形狀，跳過去會 build fail。
- **NuGetAudit 被升成 error**（`Directory.Build.props`），有已知弱點的套件會讓 build 直接失敗。
  這是刻意的，不要關掉；升級靠 Renovate 開 PR。
- **測試要用 Testcontainers.PostgreSql，不要用 EF 的 InMemory provider。**
  它不會執行 CHECK constraint，而帳務的不變式正是靠 constraint 守的。

## 已經定了、不要再提案的

技術棧 .NET 10（ADR-001）／自架 YC 不上雲（ADR-002）／Native ＋ NSSM 不用 Docker（ADR-003）／
硬邊界維持（ADR-004）／前台 Soft Seoul（ADR-009）／運費超商 60 宅配 120（ADR-010）／
M1a 只做綠界（ADR-011）／多租戶只留欄位（ADR-006）／Payment 不記帳（ADR-008）／
`platform` schema 是共用例外，outbox 由模組自己的 DbContext 寫（ADR-016）／
支撐模組不被業務模組依賴、支撐之間可以（ADR-017）／JSON 線上格式（ADR-018）。

歷史會員與訂單不遷移，客人重新註冊、之後補 Google 串接（ADR-019）。

## 沒有系統會提醒你的那一件

**這一段 2026-08-30 已經解除警報，但保留下來記住為什麼曾經緊張。**

原本寫的是：舊平台的儲值金餘額不會因為決定不遷移就消失，那是欠客人的負債，
要在租約到期前搬過來或補償，**先把到期日寫下來**。

兩件事讓它落地了：

- ADR-019 更正（`0add0dc`）：老闆確認舊平台**從來沒啟用過儲值金，餘額全為 0**，沒有要搬的負債。
- 2026-08-30：老闆確認**舊平台已經停用過期**，沒有到期日這回事。E5 取消。

所以註冊頁與上線公告**不要**寫「舊訂單請到原平台查詢，期限到 ____」——
那個管道已經不存在，要明講「舊平台已停用，歷史訂單無法查詢」。詳見 `docs/00-decisions.md` 的 ADR-019 更正。
