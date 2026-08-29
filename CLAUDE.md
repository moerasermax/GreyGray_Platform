# GreyGray Platform

代購業務系統（出國採購開團 ＋ 本地批發現貨）。模組化單體，.NET 10，14 個限界上下文。

**現在的狀態：後端骨架 ＋ 前端骨架 ＋ 已凍結的 API 契約，業務邏輯一行都還沒寫。**
前後端平行開發中：後端交給 Codex（`docs/07`），前端由多個 agent 分包（`docs/06`）。
後端的下一件事是 **M0-1 Platform Outbox 實作**——沒有它模組之間無法溝通，其他都卡在那。

## 開工前讀這些（照順序，不要跳）

1. `STATE.md` —— 現況、已知問題、下一步
2. `management/history/HANDOFF_1.md` —— 上一輪做了什麼、踩到哪些坑
3. `management/history/NextWork.md` —— 待辦順序
4. `docs/00-decisions.md` —— **18 條 ADR。已決定的不要重新討論，也不要「順手改成更好的做法」。**
5. `docs/05-API契約.md` —— **前後端唯一的邊界，已凍結。要改回來提，不得單方面改。**
6. 你做後端 → `docs/03-M0工作包.md` ＋ `docs/07-後端派工書.md`（**§0 是稽核已改過的東西**）
   你做前端 → `docs/06-前端工作包.md` ＋ `frontend/README.md`

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

## 只能照派工書開工

**沒有派工書就不准寫原始碼。** 生效中的派工寫在 `.dispatch/ACTIVE.md`，
由整合者維護，格式與範例都在那個檔案裡。四個 hook 一起守這條規則：

| Hook | 做什麼 |
|---|---|
| `UserPromptSubmit` → `claim-package.sh` | 認出 prompt 裡的 `GG_PACKAGE=<包名>`，把包別綁到這個 session_id（給 ai-cli fan out 的子 agent 用） |
| `SessionStart` → `session-brief.sh` | 一開場就把「你這一包能動哪些路徑」送進 context |
| `PreToolUse`（Write／Edit）→ `dispatch-guard.sh` | 即時擋下派工範圍外的寫入 |
| `Stop` → `stop-gate.sh` | 收工前用 `git diff` 再查一次越界——**這一層不能省**，因為用 Bash（`sed -i`、heredoc、重導向）寫的檔案繞得過 `PreToolUse`，但繞不過 git |

`docs/`、`management/`、`STATE.md`、`CLAUDE.md`、`AGENTS.md` 不受限——那是整合與 PM 的工作，不是「開工」。
`ACTIVE.md` 沒有任何 `package:` 時是**整合者模式**：原始碼一律不准寫。
閘門自己的檔案（`.dispatch/`、`.claude/`、`.codex/`）只有整合者模式能寫——
實作者能改 `ACTIVE.md` 的話，他就能自我擴權，那閘門只是建議。

**每個實作者 session 要宣告自己是哪一包**，兩條路擇一：
自己開 terminal 就 `GG_PACKAGE=BE-9 codex`；
lead 用 ai-cli fan out 子 agent 時，在子 agent 的 prompt 裡寫一行 `GG_PACKAGE=BE-9`，
`UserPromptSubmit` 會把包別綁到那個 session_id 上（ai-cli 的 `run` 沒有 env 參數，
子行程繼承的是 MCP server 自己的環境，所以只能走 session_id）。
不宣告的話 `allow` 會變成聯集——擋得住整波之外，擋不住 BE-9 去寫 BE-11 的檔案。
包名拼錯一律擋下（fail-closed）。

**Codex 也受同一套閘門管。** 它不讀 `.claude/`，讀的是 `.codex/hooks.json` 與 `AGENTS.md`，
但判斷邏輯共用 `.dispatch/lib.sh`、狀態共用 `.dispatch/ACTIVE.md`——派工狀態只有一份。

這一條要擋的是踩過的坑：**交付後不停手、自己往下做下一波**。
子代理只做自己那包的「自驗」，逐條貼出實際指令與輸出，
**不能自己宣告通過**；總驗收是整合者的事（`docs/13` §6 那十條）。

現在的派工書：`docs/13-後端第五波派工書.md`（前端的在 `-fe` worktree 的 `docs/12`）。

## 驗收完就要更新進度表

**做完一次完整的整合驗收，必須同步 `GreyGray_PM/`。**
那個資料夾刻意不在任何 worktree 裡——`STATE.md` 曾在兩棵樹上分岔，前端那份落後四波，
在不同視窗看到不同的「現況」是最容易失去方向的一種故障。

1. `03-驗收紀錄.md` —— 誰驗的、怎麼驗的、結論、發現什麼
2. `00-進度總表.md` —— 階段狀態、基準 commit、「現在卡在哪」
3. `web/dashboard.html` 最上面的 `DATA`，改完重新發布

標 ✅ 之前一定要寫得出「怎麼驗的」：哪個指令、什麼輸出。**交付方不能自己標 ✅。**
`stop-gate.sh` 在整合者模式下會比對這三個檔的修改時間，沒同步就擋下收工。

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
**舊平台沒有啟用過儲值金，餘額全是 0，沒有要遷的負債**——新系統的儲值金
期初一律從 0 開始，不要自己假設要做遷移或對帳。
