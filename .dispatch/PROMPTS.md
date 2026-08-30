# 啟動 prompt

**你只開一個 terminal，當 Leader。** 三包由 Leader 用 ai-cli fan out 出去。

兩棵 worktree 各一份、內容相同，Leader 開在哪一棵都能派全部三包
（ai-cli 的 `run` 有 `workFolder`）。

---

## 你要做的只有這一步

開一個 terminal，貼下面這段。**就這樣。**

```
GG_ROLE=leader

你是 GreyGray Platform 的 Leader。讀 .dispatch/PROMPTS.md 與 .dispatch/ACTIVE.md，
把現在生效的三包用 mcp__ai-cli__run 派出去，然後等它們回來做整合驗收。

派工規則：
  - 每個子代理的 prompt 用本檔案「工作包」那一節的原文，一字不改。
    每段開頭的 GG_PACKAGE=<包名> 那一行一定要留著——ai-cli 的 run 沒有 env 參數，
    包別只能靠 prompt 帶進去，UserPromptSubmit 會把它綁到那個子代理的 session_id。
  - workFolder 要指對：
      後端 BE-*  → D:\WorkSpace\01_開發中_wip\GreyGray_Platform
      前端 FE-*  → D:\WorkSpace\01_開發中_wip\GreyGray_Platform-fe
  - BE-23／BE-24 可以同時派，兩包不相交。
  - FE-12 開工前，先在後端樹跑
    `ops\start-dev-hosts.ps1 -InstallRoot 'D:\GreyGray' -Configuration 'Debug'`，
    確認三個 /health 都回 200；建議等 BE-23 先過，前台付款那一步才不會把
    「已知洞」跟「新發現」混在一起。

★ 收子代理的回報時，先看 .dispatch/reports/<包名>.md 在不在、三個標頭齊不齊。
  不齊就用同一個 session_id 接回去要它補完——不要自己幫它補，
  也不要因為 exit code 是 0 就當成完成（這件事已經誤判過 5 次）。

你自己不寫原始碼。你寫得了的是 .dispatch/、.claude/、.codex/、docs/ 與 GreyGray_PM。
子代理回來之後由你做整合驗收：自己重跑 build 與測試複驗，不要只轉述它們的自述。
驗收完先 commit，再撤包——順序反過來會讓未提交的交付變成無主檔案，閘門會判成越界。
```

> **不要把 `GG_ROLE=leader` 放進子代理的 prompt。** 就算不小心放了也升不了級——
> `claim-package.sh` 讓包別優先於角色（已實測），但別依賴那道保險。

---

## 排程

```
可同時開            BE-23   BE-24
兩包過了才開        FE-12（前端，關掉 mock 對真後端跑一遍）
```

---

## 兩條這一波開始機械檢查的規則

**① 自驗報告是檔案，不是對話。**
每包要寫 `.dispatch/reports/<包名>.md`，三個標頭一字不差：
`## 指令與輸出`、`## 逐條自驗`、`## 我發現但沒做的事`。
缺任何一個，`audit-dispatch.sh` 第 ⑧ 項會擋下 Leader 收工。

**② 不准把測試丟背景、不准排程 wakeup。**
`ops/test.ps1` 實測淨執行 **584 秒（9.7 分）**，前景跑得完。
headless 子代理的行程一結束就沒了，通知不會來而 exit code 還是 0——
這件事已經發生 **5 次**，其中 3 次是在規則寫進 prompt 之後。所以現在改成檔案檢查。

---

## 工作包（以下每一段就是子代理的 prompt，原文照抄）

### BE-23　`CapturePayment` 誤設 `RefundedCurrency` ＋ EF model 補約束　🔴

```
GG_PACKAGE=BE-23

你是 GreyGray Platform 的 BE-23。讀 docs/21-後端第九波派工書.md，
§1 §2 §3 全部要看，然後照 §5 的 BE-23 那一節做。
再讀 .dispatch/reports/README.md（自驗報告的格式）。

FE-12 對真後端跑的第一個結帳付款，現在必定在真的資料庫上炸 23514——
Order.cs:206 的 CapturePayment 誤設了 RefundedCurrency（RefundedAmountMinor
還是 0），違反 db/migrations/0006_m1a_core.sql:611-615 的
orders_refunded_consistent 約束。172 條測試沒有一條抓到，因為
OrderingDbContext.cs 的 ConfigureOrder 只宣告了 4 條 check constraint，
沒有 orders_currency_consistent／orders_paid_consistent／orders_refunded_consistent
這三條——EnsureCreatedAsync() 建的測試 schema 裡根本沒有它們。

★ 只刪 Order.cs:206 那一行，PaidAmountMinor／PaidCurrency 兩行是對的不要動。
★ 把缺的三條約束逐字補進 OrderingDbContext.cs，要跟 0006 的定義一致。
★ 至少一條測試要在真的套過 migration 的 schema 上完成付款，不能只靠
  EnsureCreatedAsync()——可以比照 GreyGray.M1a.Migrations.Tests 現成的
  ExecuteMigrationChainAsync helper 起 Testcontainers Postgres、套 0001~0006。
★ ops/test.ps1 要前景跑（淨執行約 10 分鐘），不准丟背景、不准排程 wakeup。
自驗寫進 .dispatch/reports/BE-23.md，三個標頭一字不差。
```

### BE-24　`0003_channel_seams.sql` 對 `ledger.account` 的 seed 非冪等

```
GG_PACKAGE=BE-24

你是 GreyGray Platform 的 BE-24。讀 docs/21-後端第九波派工書.md，
§1 §2 §3 全部要看，然後照 §5 的 BE-24 那一節做。
再讀 .dispatch/reports/README.md（自驗報告的格式）。

★ 修訂既有檔：這一包刻意改動已經在 HEAD 裡的 0003_channel_seams.sql
  本身（不是新增編號）。老闆已核准：專案還沒上線，沒有正式資料依賴
  舊版 0003 的行為。`.dispatch/ACTIVE.md` 的 block 裡已經標明「修訂既有檔」，
  audit-dispatch.sh 第②項認得這個標記，不會誤判撞號。

0003 對 ledger.account 的 seed 用 INSERT ... ON CONFLICT (tenant_id, code)
DO NOTHING，但 0005_m1a_payment_ledger.sql:143-145 後來把 id 欄位設成
NOT NULL 且無預設值。PostgreSQL 檢查 NOT NULL 在建構候選列時，早於
ON CONFLICT 判斷衝突，所以對已經套過 0005 的資料庫重放 0003 會噴
「null value in column "id"」。ops/invoke-migrations.ps1 沒有任何
「已套用就跳過」的追蹤，deploy.ps1 每次重新部署都可能把全套檔案再傳一次。

把 seed INSERT 改成 WHERE NOT EXISTS 導引的 INSERT ... SELECT，
只改這一段，0003 其他部分不要動。加一條迴歸測試：在
tests/GreyGray.M1a.Migrations.Tests/M1aCoreMigrationTests.cs 套完整鏈
0001~0014，再單獨重放一次 0003，斷言不噴 PostgresException（修之前要能
重現這個錯誤，修之後轉綠，兩次實際輸出都要貼在自驗報告）。

★ ops/test.ps1 要前景跑，不准丟背景、不准排程 wakeup。
自驗寫進 .dispatch/reports/BE-24.md，三個標頭一字不差。
```

### FE-12　關掉 mock，對真後端跑一遍　⏸ 等 BE-23／BE-24 跑完

```
GG_PACKAGE=FE-12

你是 GreyGray Platform 的 FE-12。讀 docs/18-前端第五波派工書.md §5，
§1 也要看（既成事實表）。再讀 .dispatch/reports/README.md（自驗報告的格式）。

開工前提：後端 BE-22 已通過整合驗收（開發機 D:\GreyGray 上 PG＋Garnet＋
三個 Host 都起得來），但那組環境會被整合驗收的 build 流程 stop 掉
（資料保留，只是 stop）。開工前先確認三個 Host 的 /health 都回 200；
沒回應就跟 Leader 回報，不要自己去裝環境，那是 BE-22 的事。

★ 已知會踩到、不是你要修的：後端 BE-23 修的是「結帳付款會炸 23514」。
  如果 BE-23 這時候還沒過，你在 §5 第 3 點「前台走一條完整的……→ 付款」
  會在第一次結帳付款就踩到——那是已知洞，照樣記下來，不要當成自己的
  新發現，也不要想辦法繞過去。

不要修後端。發現後端問題就記下來回報。不要為了畫面好看在前端補資料。
自驗寫進 .dispatch/reports/FE-12.md，三個標頭一字不差。
```

---

## 三包都適用的四件事

1. **子代理不可以自己宣告通過。** 自驗報告寫成檔案，貼**實際指令與實際輸出**。
   整合驗收是 Leader 的事，而且 Leader 要自己重跑複驗。
2. **遇到契約缺口或平台缺口就停下來回報**，不要自己補一個看起來合理的預設值。
3. **你只有這一輪。** 不准排程 wakeup，不准把工作丟背景後結束。
   `ops/test.ps1` 前景跑得完（584 秒）。無法完成就**明講做不到與原因**。
4. **不要碰整個工作區的 git 指令**（`git stash`／`reset --hard`／`clean`／
   `checkout -- .`／`commit`）。stash stack 是跨 worktree 共用的。
