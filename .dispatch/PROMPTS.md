# 啟動 prompt

**沒有生效中的派工（2026-09-01）。** 兩棵樹都是整合者模式，原始碼一律不准寫。

這一輪跑了兩波，都已通過整合驗收並撤包：

| 包 | 內容 | commit |
|---|---|---|
| BE-36 | `POST /v1/shipments` 加模組層冪等，堵掉重複出貨單（#23） | `e2b4bf1` |
| BE-37 | 冪等錯誤碼對齊契約（#24） | `f8e357f` |

**「現在卡在哪」表上已經沒有任何生效中的內部項目**——剩下的全是外部輸入
（E3 綠界憑證、E2 拓樸）或需要使用者拍板的 dev-only 模擬付款開關。
下一步的判斷見 `GreyGray_PM/04-交接書.md` 第五節。
**下一份後端派工書是 `docs/34-後端第二十二波派工書.md`。**

---

## BE-37 的啟動 prompt（已撤包，保留供下一包參考格式）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端。.NET 10 模組化單體，14 個限界上下文，EF Core + Npgsql。

GG_PACKAGE=BE-37

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/33-後端第二十一波派工書.md    §0 事實 ＋ §1 修法（範圍很窄，照著做）
  docs/05-API契約.md                §4 冪等那一節（這是唯一的權威）
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-37.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」
     缺任何一個，Leader 收不了工，你的交付會被退回。

  ② 不准把驗證丟背景、不准排程 wakeup。12 個測試專案逐一在前景個別執行。
     不要用 dotnet test，SDK 10.0.301 ＋ xunit.v3 走 VSTest 會直接報錯。
     （已知：整套約 15 分鐘跑得完。）

★ 範圍很窄：三個檔案六個字串 ＋ 一份契約文件補一行 ＋ 一組迴歸測試
  ＋ 一條測試的 migration 上界改成推導（必做 4）。
  不要順手改別的錯誤碼、不要動 OpenAPI、不要動前端、不要重構 BffHttp。
  ★ 特別是不准動 BffHttp.StatusFor——理由在派工書 §1，那條路徑經由 HTTP 走不到，
    動它會波及所有走 Problem(error) 的呼叫點。發現了寫進報告，不要動手。

★ BOM 與行尾：維持每個檔案原本的狀態。你會動的三個 .cs 都沒有 BOM、都是 LF，
  維持原狀。用 Python 寫檔時 encoding 用 'utf-8'（不要 'utf-8-sig'，寫一定加 BOM），
  newline 參數要設對，不要把行尾換掉。交付前逐檔量「改前／改後」兩次，兩次要一樣。
  量 CR 不要用 grep -c $'\r'（那個寫法在這個 shell 裡回的是總行數，Leader 上一波
  就是被它騙過一次），用 python -c 直接數位元組。

檔案所有權：只准改「你這一包擁有」的路徑（見派工書 §3）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。
.dispatch/reports/BE-37.md 也寫得了（那是你的自驗報告）。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。stash stack 是跨 worktree 共用的。

★ 派工前 Leader 已經把閘門檔 commit 掉了，session 開始時 git status 應該是乾淨的。
  收工時若有 hook 要你「還原」你沒改過的檔案，先查證是不是 Leader 派工前寫的，
  把證據寫進報告，不要執行 git checkout --。

★ 如果你認為 §1 有錯，停下來寫進報告問，不要自己改方向。
  （上一波 BE-36 的子代理在行尾這件事上堅持不寫一句自己量不出來的斷言，
   最後證實是 Leader 的量法壞了——質疑是被鼓勵的，自己改方向不是。）

你不可以自己宣告通過。交付完就停。

工作包內容（完整版在 docs/33-後端第二十一波派工書.md §4）：

  必做 1　改六處字串（位置見派工書 §0 的表，訊息文字不動，只改 code）：
            request.idempotency-key-required  → platform.idempotency-key-required
            request.idempotency-in-flight     → platform.request-in-flight
            request.idempotency-key-reused    → platform.idempotency-key-reused
            request.idempotency-key-too-long  → platform.idempotency-key-too-long

  必做 2　docs/05-API契約.md §4 那張表底下補一句，說明
          platform.idempotency-key-too-long 是「key 超過 255 字元」的 400 子類。
          不要改表裡既有的三列。

  必做 3　迴歸測試（tests/GreyGray.Platform.Tests/，BE-35 建的
          BffHttpTwoPhaseIdempotencyTests.cs 可以參考）：四個情況各一條，
          斷言 code 字串逐字 ＋ HTTP 狀態碼（400／400／409／422）。
          這四條存在的理由就是「下次有人改字串會立刻紅」——這個缺陷活到現在，
          正是因為沒有任何測試斷言過這些字串。測試裡寫一行註解指回 docs/05。

  必做 4　tests/GreyGray.M1a.Migrations.Tests/M1aCoreMigrationTests.cs:296
          的 DisplayName「0001→0014」與底下寫死的上界 14，改成從
          db/migrations/*.sql 的實際檔案數推導，DisplayName 改成不帶編號的說法。
          如果推導會讓那條測試的語意改變，停下來寫進報告問。

  自驗　　12 個測試專案逐一前景執行全綠（基準 214 條）、build 0 warning 0 error、
          git diff --numstat 確認 docs/api/ 完全沒出現、
          全 repo grep "request.idempotency" 應該零命中。
```

---

## 前兩輪（已撤包，留著供追溯）

| 包 | 內容 | commit |
|---|---|---|
| BE-34 | 查證「現在卡在哪」#22（查證包，不含修法） | `cb0d2f0` |
| BE-35 | 修 #22 家族——副作用已 commit 就不准 abandon | `213e8a7` |
| BE-36 | `POST /v1/shipments` 加模組層冪等，堵掉重複出貨單（#23） | `e2b4bf1` |

`.dispatch/ACTIVE.md`「已經通過、不再生效的」清單與
`GreyGray_PM/00-進度總表.md` 有完整脈絡。

---

## ★ 派工前先 commit 閘門檔（BE-34 查出來的閘門盲點）

`stop-gate.sh` 用 `git diff` 對 HEAD 比對越界，**分不出「實作者改的」與
「session 開始前就已經髒的」**。整合者派工時寫的 `.dispatch/` 檔在子代理眼中
就是「未提交的變更」，收工時 stop gate 會要求子代理還原它們——而還原
`ACTIVE.md` 等於刪掉子代理自己的授權、還原 `.selftest-stamp` 會讓
`audit-dispatch.sh` 第 ⑩ 項由綠轉紅。

**BE-35、BE-36、BE-37 都已照做（派工前先 commit）。**

---

## 兩條量測與編碼的教訓（BE-34、BE-36 各踩過一次）

1. **BOM**：用 Python 改既有檔案時，`io.open(..., encoding='utf-8-sig')` **讀**的時候
   有沒有 BOM 都吃，**寫**的時候卻**一定加上 BOM**。這個 repo 的 `.editorconfig`
   是 `charset = utf-8`（無 BOM），但 `ops/` 底下 17 支腳本有 12 支**刻意帶 BOM**
   （CI 有一步用 Windows PowerShell 5.1 驗 M-1 腳本，5.1 讀無 BOM 的 UTF-8 會當成
   ANSI 碼頁，而那些腳本有中文字串）。**規則是「維持每個檔案原本的狀態」，
   不是「一律拿掉」。**
2. **量行尾不要用 `grep -c $'\r' <檔案>`**——那個寫法在這個 shell 裡回的是
   **總行數**而不是「含 CR 的行數」，所以每個檔看起來 CR 數都剛好等於行數。
   Leader 在 BE-36 複驗時被它騙過一次，還據此去「更正」一段本來正確的報告。
   **用 `python -c` 直接數位元組**（`d.count(b'\r')`）。
   附帶記錄現況：這棵樹的工作區與 blob 都是 **LF**，而 `.editorconfig` 寫的是
   `end_of_line = crlf`，三者不一致——那是既有狀態，`git diff` 印的
   「LF will be replaced by CRLF」是 `core.autocrlf=true` 的正常提示，不是缺陷。

---

## 下一波派工前

Leader 讀 `GreyGray_PM/00-進度總表.md`「下一步」一節決定要派什麼，
把新的工作包寫進對應樹的 `.dispatch/ACTIVE.md`，再把啟動 prompt 與工作包原文
寫回這個檔案（兩棵樹必須逐字同步，見 `.dispatch/reports/README.md` 與
`audit-dispatch.sh` 第 ⑦ 項）。
