# 啟動 prompt

**生效中的派工：BE-36（後端，2026-09-01 派出）。**
前端樹沒有生效包，仍是整合者模式。

派工書：`docs/32-後端第二十波派工書.md`
修的是「現在卡在哪」#23：`POST /v1/shipments` 會建出重複的出貨單。

---

## BE-36　`POST /v1/shipments` 加模組層冪等，堵掉重複出貨單（#23）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端。.NET 10 模組化單體，14 個限界上下文，EF Core + Npgsql。

GG_PACKAGE=BE-36

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/00-decisions.md             ADR
  docs/32-後端第二十波派工書.md      §0 背景 ＋ §1 定案修法 ＋ §2 順帶 ＋ §3 不在範圍
  .dispatch/reports/BE-34.md       ★ #23 的證據來源（必做 5 底下的「★ 重複風險」）
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-36.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」
     缺任何一個，Leader 收不了工，你的交付會被退回。

  ② 不准把驗證丟背景、不准排程 wakeup、不准說「等背景跑完再回報」然後結束。
     （已知：整套 ops/test.ps1 前景約 10 分鐘跑得完；背景執行在這個 harness 上
      不會存活。不要用 dotnet test，SDK 10.0.301 ＋ xunit.v3 走 VSTest 會直接報錯。）

★ 修法已經在 §1 定死，而且 Leader 明文否決了兩個方向：
  ① 不准用 (tenant_id, orderIds, method) 當自然鍵——會破壞契約保證的 N:M
  ② 不准把 POST /v1/shipments 改成 BE-35 的兩階段多載——會製造 24 小時 409
  不要重新設計、不要「順手改成更好的做法」。你認為 §1 有錯，停下來寫進報告問，
  不要自己改方向。

檔案所有權：只准改「你這一包擁有」的路徑（見派工書 §5）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。
.dispatch/reports/BE-36.md 也寫得了（那是你的自驗報告）。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。stash stack 是跨 worktree 共用的。

★ 如果收工時有 hook 要你「還原」你沒有改過的檔案，先查證那些檔案是不是
   整合者在派工前寫的。這一波 Leader 已經先把閘門檔 commit 掉了，
   照理不會再撞到；真的撞到就把證據寫進報告，不要執行 git checkout --。

★ 用 Python 改既有檔案時，io.open(..., encoding='utf-8-sig') 只能用來讀、
   不能用來寫（寫一定加 BOM）。這個 repo 的 .editorconfig 是 charset = utf-8。
   交付前用 head -c 3 <檔案> | xxd -p 確認不是 efbbbf。

你不可以自己宣告通過。交付完就停。

工作包內容（完整版在 docs/32-後端第二十波派工書.md §7）：

  必做 1　模組層冪等
          FulfillmentContracts / ShipmentAggregate / IShipmentRepository /
          FulfillmentRepository / FulfillmentApplicationService / FulfillmentDbContext。
          呼叫端傳冪等鍵進來，比照 Cart.CheckoutIdempotencyKey（可為 null ＋
          過濾式唯一索引）。★ 查冪等鍵要排在「訂單必須是 ReadyToShip」守衛之前，
          否則重播會被守衛擋成失敗，複製一次 #22 (A″) 的死路。

  必做 2　db/migrations/0016_fulfillment_shipment_idempotency.sql（新檔）
          照 0015 的骨架，可重跑是硬要求，結尾 owner 檢查不要漏。

  必做 3　M1bFulfillmentEndpoints.cs 只改 POST /v1/shipments 這一條，
          自己讀 Idempotency-Key header 傳進去（比照 CompleteCheckoutAsync）。
          不要動 BffHttp.cs。

  必做 4　測試六條（見派工書 §7 的表），其中兩條是這一波的核心證據：
          · #23 本體：同一把 key 兩次 → 同一張出貨單、shipment 只有 1 列，
            而且要先示範「拿掉冪等分支會變成兩張」（先紅後綠，輸出貼進報告）
          · N:M 沒被破壞：同一批訂單、同一個 DeliveryMethod、不同 key → 建得出第二張
            （既有 N:M 測試用兩個不同 method，蓋不到這條）

  必做 5　三個既有落差：ops 的 migration 清單 0014 → 0016（0015 從來沒補上）、
          verify-environment.ps1 的 expectedCount 14 → 16、
          install-dev-environment.ps1 裡那段已被 BE-24 修好卻沒更新的 0003 警告註解、
          OrderingPaymentConstraintTests.cs 的 LastMigration 15 → 16。
          只改清單、常數與註解，不要動腳本邏輯。

  自驗　　全套測試全綠（基準 203 條）、build 0/0、無 BOM、
          git diff --numstat 確認 BffHttp.cs 與 openapi.admin.yaml 完全沒出現。
```

---

## 前一輪（已撤包，留著供追溯）

| 包 | 內容 | commit |
|---|---|---|
| BE-34 | 查證「現在卡在哪」#22（查證包，不含修法） | `cb0d2f0` |
| BE-35 | 修 #22 家族——副作用已 commit 就不准 abandon | `213e8a7` |

`.dispatch/ACTIVE.md`「已經通過、不再生效的」清單與
`GreyGray_PM/00-進度總表.md` 有完整脈絡。

---

## ★ 派工前先 commit 閘門檔（BE-34 查出來的閘門盲點）

`stop-gate.sh` 用 `git diff` 對 HEAD 比對越界，**分不出「實作者改的」與
「session 開始前就已經髒的」**。整合者派工時寫的 `.dispatch/` 檔在子代理眼中
就是「未提交的變更」，收工時 stop gate 會要求子代理還原它們——而還原
`ACTIVE.md` 等於刪掉子代理自己的授權、還原 `.selftest-stamp` 會讓
`audit-dispatch.sh` 第 ⑩ 項由綠轉紅。

**BE-35 與 BE-36 都已照做（派工前先 commit）。**

---

## 另一條環境教訓（BE-34 踩過）

用 Python 改既有檔案時，`io.open(..., encoding='utf-8-sig')` **讀**的時候有沒有
BOM 都吃，**寫**的時候卻**一定加上 BOM**。這個 repo 的 `.editorconfig` 是
`charset = utf-8`（無 BOM），寫檔一律用 `utf-8`。交付前用
`head -c 3 <檔案> | xxd -p` 確認沒有 `efbbbf`。

---

## 下一波派工前

Leader 讀 `GreyGray_PM/00-進度總表.md`「下一步」一節決定要派什麼，
把新的工作包寫進對應樹的 `.dispatch/ACTIVE.md`，再把啟動 prompt 與工作包原文
寫回這個檔案（兩棵樹必須逐字同步，見 `.dispatch/reports/README.md` 與
`audit-dispatch.sh` 第 ⑦ 項）。
