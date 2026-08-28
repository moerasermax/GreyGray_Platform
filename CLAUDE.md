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
