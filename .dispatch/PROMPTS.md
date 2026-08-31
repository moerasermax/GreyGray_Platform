# 啟動 prompt

**生效中的派工：BE-35（後端樹，2026-09-01 派出）。**
前端樹目前沒有生效中的派工，仍是整合者模式，原始碼一律不准寫。

BE-35 是**修正包**，修「現在卡在哪」#22 家族：副作用已經 commit、之後才在
「組回應」那一步失敗，於是 `BffHttp` 把冪等鍵 `Abandon` 掉、客人拿到錯誤。
五個端點同款形狀（checkout ＋ BE-34 新找到的四個）。修法已由 Leader 定死，
見 `docs/31-後端第十九波派工書.md` §1。

BE-34（查證包，後端 `cb0d2f0`）已通過整合驗收並撤包，它是這一波的全部證據來源。

---

## ★ 派工前先 commit 閘門檔（BE-34 查出來的閘門盲點）

`stop-gate.sh` 用 `git diff` 對 HEAD 比對越界，**分不出「實作者改的」與
「session 開始前就已經髒的」**。整合者派工時寫的 `.dispatch/` 檔在子代理眼中
就是「未提交的變更」，收工時 stop gate 會要求子代理還原它們——而還原
`ACTIVE.md` 等於刪掉子代理自己的授權、還原 `.selftest-stamp` 會讓
`audit-dispatch.sh` 第 ⑩ 項由綠轉紅。BE-34 的子代理兩次都正確拒絕了。

**BE-35 這一波已經照做：閘門檔在派工前先 commit。**

---

## BE-35 啟動 prompt（後端樹）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端。.NET 10 模組化單體，14 個限界上下文，EF Core + Npgsql。

GG_PACKAGE=BE-35

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/00-decisions.md             ADR
  docs/31-後端第十九波派工書.md      §0 背景 ＋ §1 定案修法 ＋ §2 不在範圍
  .dispatch/reports/BE-34.md       ★ 這一波的全部證據來源，尤其必做 5 的兩張表
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-35.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」
     缺任何一個，Leader 收不了工，你的交付會被退回。

  ② 不准把驗證丟背景、不准排程 wakeup、不准說「等背景跑完再回報」然後結束。
     （整套測試前景約 10 分鐘跑得完；背景執行在這個 harness 上不會存活。
      不要用 dotnet test，SDK 10.0.301 ＋ xunit.v3 走 VSTest 會直接報錯。）

檔案所有權：只准改「你這一包擁有」的路徑：
  src/Platform/Http/BffHttp.cs
  src/Hosts/GreyGray.Api.Storefront/M1aEndpoints.cs
  src/Hosts/GreyGray.Api.Admin/M1aEndpoints.cs
  src/Hosts/GreyGray.Api.Admin/M1bShortfallRefundEndpoints.cs
  tests/GreyGray.M1a.CheckoutOrdering.Tests/
  tests/GreyGray.Platform.Tests/
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。
.dispatch/reports/BE-35.md 也寫得了（那是你的自驗報告）。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。stash stack 是跨 worktree 共用的。

你不可以自己宣告通過。交付完就停。

★ 根因與修法已經在派工書 §0 §1 定死（兩階段 work/render 多載 ＋ 退化回應
   不改契約 ＋ 不准靜默）。不要重新調查根因、不要重新設計修法、不要擴大到
   §2 明文排除的範圍（(B)、★ POST /v1/shipments、A8／A9）。

這一包要做的五件事（完整說明在 docs/31-後端第十九波派工書.md §6）：

必做 1  BffHttp 加兩階段多載：work（Func<..., Task<Result<TState>>>，失敗時
        abandon 是安全的）＋ render（Func<TState, ..., Task<TResponse>>，
        回傳型別不是 Result<T>，所以「組回應失敗」在型別上表達不出來）。
        work 成功之後絕不 Abandon；render 丟例外也要先 Complete 再往上丟。
        ★ 舊多載一個字都不准動——33 個呼叫點裡有 28 個要繼續用它。

必做 2  五個端點改用新多載：S11 POST /v1/cart/checkout、
        S12 POST /v1/orders/{id}/cancel、A3 Admin 同名端點、
        A4 POST /v1/orders/{id}/lines/{lineId}/cancel、
        A20 POST /v1/orders/{id}/lines/{lineId}/refund-shortfall。
        ToOrderAsync／ToAdminOrderAsync 改成不會失敗的版本，退化值兩條硬性
        要求：① 不准改契約（docs/05-API契約.md 已凍結，退化回應仍要符合現有
        schema，不准塞 null 到不可為 null 的欄位）② 不准靜默（每次退化都要
        留可觀測痕跡）。
        ★ BE-34 已經留下的 [Skip] 測試
        Committed_must_not_be_reported_as_a_failure 要拿掉 Skip 並轉綠——
        那就是這一波成功的定義。另一條 (B) 的 [Skip] 維持跳過（不在範圍），
        只更新 Skip 理由字串。

必做 3  另外四個端點各補一條迴歸測試，斷言「副作用已產生時回應是成功狀態碼，
        且冪等鍵不是 Abandoned」。S12／A3 若還是 inline lambda，比照 BE-34
        必做 1 機械抽取先抽出來（純搬移，不准改行為）。

必做 4  POST /v1/webhooks/ecpay（Storefront M1aEndpoints.cs:777 起）手寫了
        一模一樣的 abandon 形狀，BE-34 分類為安全（底層自己有冪等）。
        這一波只加註解說明它為什麼維持現狀，不改行為。

必做 5  tests/GreyGray.Platform.Tests/ 補上新多載本身的四條語意測試。

★ 如果收工時有 hook 要你「還原」你沒有改過的檔案，先查證那些檔案是不是
   整合者在派工前寫的，把證據寫進報告，不要執行 git checkout --。
   （這一波閘門檔已經先 commit 過，理論上不會再撞到。）
```

---

## 下一波派工前

Leader 讀 `GreyGray_PM/00-進度總表.md`「下一步」一節決定要派什麼，
把新的工作包寫進對應樹的 `.dispatch/ACTIVE.md`，再把啟動 prompt 與工作包原文
寫回這個檔案（兩棵樹必須逐字同步，見 `.dispatch/reports/README.md` 與
`audit-dispatch.sh` 第 ⑦ 項）。
