# GreyGray Platform — 前端

代購業務系統的前台（`apps/storefront`）與後台（`apps/admin`）。Next.js ＋ pnpm workspace。

**這個檔案給 Codex。** 規則的正本是 `CLAUDE.md`（兩邊內容一致，不要只讀這一份就動手）。
**2026-09-06 新增**：在此之前這棵樹沒有 `AGENTS.md`——Codex 派到前端樹時有閘門、有 hook 的簡報，
但收不到常駐規則。新工作流讓實作者可能是 Codex，所以補上這一份。

---

## 只能照派工書開工

**沒有派工書就不准寫原始碼。** 生效中的派工寫在 `.dispatch/ACTIVE.md`，由整合者維護。

開工前先看那個檔案：

- 有 `package:` 且是你這一包 → 只准改該包 `allow:` 列出的路徑
- 沒有 `package:` → **沒有人派工給你，不要自己挑一件事做**，回報並等派工

**你要做的那一包、派工書是哪一份，一律以 `.dispatch/ACTIVE.md` 裡你那一包的 `doc:` 那一行為準**
（不要照抄任何文件裡寫死的派工書編號，那種寫法一定會過期）。

你的 session 會用兩種方式之一標明包別：環境變數 `GG_PACKAGE=<包名> codex`，
或是 prompt 裡的一行 `GG_PACKAGE=<包名>`（Leader 用 ai-cli 派你時走這條）。
`SessionStart` 會告訴你受管的是哪一包、能寫哪些路徑——**以它說的為準**。
如果它說「沒有設 GG_PACKAGE」，代表閘門只擋得住整波之外，擋不住你去寫別包的檔案：
那時請嚴格照派工書的所有權表自律，並在回報時提一句。

這條有四個 hook 在守（`.codex/hooks.json`）：

| 事件 | 做什麼 |
|---|---|
| `UserPromptSubmit` | 認出 prompt 裡的 `GG_PACKAGE=<包名>`，把包別綁到這個 session |
| `SessionStart` | 一開場就告訴你能動哪些路徑 |
| `PreToolUse` | `apply_patch` 碰到範圍外的檔案就擋下 |
| `Stop` | 收工前用 `git diff` 再查一次——**用 shell 寫的檔案繞得過 `apply_patch` 閘門，但繞不過 git** |

`docs/`、`.dispatch/`、`.claude/`、`.codex/`、`management/`、`STATE.md`、`CLAUDE.md`、`AGENTS.md`
不受限，那是整合與文件工作，不是「開工」。

### 不要碰整個工作區的 git 指令

`git stash`、`git reset --hard`、`git clean`、`git checkout -- .`、`git commit`
**一律不准**。同一棵 worktree 裡可能有別包在平行工作，他們的交付在被整合驗收之前都還沒 commit；
而且 stash stack 是**跨 worktree 共用**的，另一棵樹的 session 也會被波及。提交是整合者的事。

這不是假設性風險——FE-10 曾為了「取得乾淨的驗證基準」跑 `git stash`，
把 FE-9 已完成、還沒提交的交付與整合者正在改的文件整個掃走。

要乾淨的比較基準請用不動工作區的方式：`git diff`、`git diff --stat`。

## 交付完就停

**自驗全過之後，你的工作就結束了。**

- 不要往下做下一包、不要越波次、不要「順手」修別的東西
- **不能自己宣告通過**，也不能自己在文件裡標 ✅

自驗要**逐條貼出實際指令與實際輸出**，不是「已完成」四個字。
任何斷言「某件事不存在／不會發生」的測試，都要注入一次違規、看它變紅、再改回來。

## 前端四條（正本在 `frontend/README.md`）

1. **顏色與尺寸只從 token 來。** 元件裡不准出現 raw hex，不准出現 `rounded-[18px]` 這種任意值。
2. **不做金額運算。** 只呼叫 `formatMoney()`；加總、分攤、含運總額由後端回傳。
   **唯一例外**：商品頁小計預覽（`subtotalPreview`，ADR-033）——只用於顯示，購物車一律以後端為準。
3. **不猜業務規則。** 「這個團還能不能下單」讀 `isAcceptingOrders`，不要自己拿 `closesAt` 跟現在時間比。
4. **不直接 `fetch`。** 一律走 `@greygray/api-client`——session cookie、冪等鍵、problem+json 都在那一層。

## 設計準則與工作流（2026-09-06 新增）

`docs/45-開發工作流與設計準則.md`（兩棵樹共用）：
SOLID 與設計模式是**審查準則**——新增抽象要寫得出「解決什麼具體問題／
更簡單的做法為何不夠／維護代價」，寫不出就不要加；
**不得以「改成 Clean Architecture」為理由提重構案**（ADR-034）；
「接手機」在拍板之前不是可以寫進派工書的需求（ADR-035）；
**自驗是必要不充分條件**，你不能自己宣告通過；
派工書的驗收有邊界測試六類（重複與併發、上下限、非法狀態轉移、權限、事件重放、部分失敗），
不適用的要註明理由，不要靜靜跳過。

## 建置與測試

```powershell
pnpm --recursive typecheck        # 全綠
pnpm --recursive test             # 逐專案條數貼進報告
```

- **沒有 jsdom**：判斷抽成純函式測，呈現用 `renderToStaticMarkup` 驗文案。
- **build 由 Leader 跑**（除非派工書明講要你跑）。**不要起或停任何 dev server。**
- 不要把驗證丟背景、不要排程 wakeup——行程一結束那一輪就沒了，而且 exit code 還是 0。

## 型別是產生出來的，契約是凍結的

`packages/api-client/src/types.storefront.ts` 與 `types.admin.ts` 由 `pnpm api:generate`
從 `docs/api/openapi.*.yaml` 產生，**不要手改**。

`docs/05-API契約.md` 與 `docs/api/openapi.*.yaml` 是前後端唯一的邊界，已凍結 v1.0。
要改回來提，**不得單方面在實作裡改形狀**——那是前後端平行最貴的一種技術債。
契約檔由 Leader 在兩棵樹之間同步，你不要自己動。
