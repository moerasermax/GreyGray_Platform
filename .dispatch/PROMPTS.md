# 啟動 prompt

**生效中的派工：BE-34（後端樹，2026-09-01 派出）。**
前端樹目前沒有生效中的派工，仍是整合者模式，原始碼一律不准寫。

BE-34 是**查證包**，不是修正包——查「現在卡在哪」#22（`POST /cart/checkout`
三個獨立交易沒有補償 ＋ `BffHttp` 冪等包裝在副作用已 commit 的情況下仍
`AbandonAsync`）。交付物是證據與判斷依據，不是修法；修法方向由 Leader
看完報告再拍板，另開下一波。詳見 `docs/30-後端第十八波派工書.md`。

BE-33（訂單編號 GUID v7 撞號，現在卡在哪 #21，後端 `299b3d5`）已通過整合驗收
並撤包，詳見 `.dispatch/ACTIVE.md`「已經通過、不再生效的」清單與
`GreyGray_PM/00-進度總表.md`。

---

## BE-34 啟動 prompt（後端樹）

```
專案：D:\WorkSpace\01_開發中_wip\GreyGray\GreyGray_Platform
GreyGray Platform 後端。.NET 10 模組化單體，14 個限界上下文，EF Core + Npgsql。

GG_PACKAGE=BE-34

開工前務必先讀：
  CLAUDE.md                        六條鐵則 ＋ 派工規則
  docs/00-decisions.md             ADR
  docs/30-後端第十八波派工書.md      §0 完整背景 ＋ §1 既成事實
  .dispatch/reports/BE-32.md       「我發現但沒做的事②」＝這條線索的原始觀察
  .dispatch/reports/README.md      ★ 自驗報告的格式，缺標頭會被退回

★★ 兩條硬規則，機械檢查不是勸告：

  ① 自驗報告寫成檔案：.dispatch/reports/BE-34.md
     三個標頭一字不差：「## 指令與輸出」「## 逐條自驗」「## 我發現但沒做的事」
     缺任何一個，Leader 收不了工，你的交付會被退回。

  ② 不准把驗證丟背景、不准排程 wakeup、不准說「等背景跑完再回報」然後結束。

檔案所有權：只准改「你這一包擁有」的路徑（見派工書 §3）。
docs/、management/、STATE.md、CLAUDE.md、AGENTS.md 每一包都寫得了。
.dispatch/reports/BE-34.md 也寫得了（那是你的自驗報告）。

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。stash stack 是跨 worktree 共用的。

你不可以自己宣告通過。交付完就停。

★★★ 這一包是「查證包」，不是修正包：
   - 不准修 BffHttp.cs 的冪等邏輯
   - 不准修 checkout 三步驟的交易／補償行為
   - 不准改 Checkout／Ordering 模組的任何行為
   - 唯一准動的生產程式碼是「把 checkout 的 inline lambda 機械抽成靜態方法」
     （派工書 §5 必做 1），行為必須一模一樣
   修法方向由 Leader 看完你的報告再拍板。你的工作是給證據，不是給修法。

這一包要做的六件事（完整說明在 docs/30-後端第十八波派工書.md §5）：

必做 1  把 /cart/checkout 的 inline lambda 機械抽成靜態方法（比照 Admin Host
        已經抽出來的 M1aEndpoints.CancelOrderLineAsync 的形狀），讓它測得到。
        純搬移，不准改行為。抽完 187 條測試要全綠。

必做 2  ★ 把「同一把 Idempotency-Key 重試會不會建出第二張訂單」釘死。
        現行文件（00-進度總表.md #22、04-交接書.md 第四節）說「會」，
        Leader 2026-09-01 靜態讀 CheckoutApplicationService.cs:210-215 與
        OrderingApplicationService.cs:40-53 認為「不會」（兩個模組各自都
        綁在同一把 checkout 冪等鍵上做冪等）。用測試證實或推翻，
        測到什麼寫什麼，不要配合任何一方的說法。

必做 3  把 (B) 路徑釘死：第 ② 步失敗時，購物車已結案 commit 但訂單沒建立。

必做 4  把重現寫成留得下來的迴歸測試。確認為缺陷的用 [Skip] 斷言修後行為；
        確認為正確的寫成正常會綠的迴歸網。

必做 5  ★ 審全部 33 個 BffHttp.ExecuteIdempotentAsync 呼叫點（Admin 21、
        Storefront 12），做成分類表：安全／幽靈／★重複風險。
        「★重複風險」＝副作用已 commit ＋ 底層模組沒有自己的冪等 ＋
        key 被 abandon → 重試會把副作用做第二次。這一類如果存在，
        比 checkout 本身嚴重。只讀不改。

必做 6  修法選項分析（至少三個方向，各寫做法／影響範圍／代價／解掉哪幾條）。
        不准挑一個實作。
```

---

## 下一波派工前

Leader 讀 `GreyGray_PM/00-進度總表.md`「下一步」一節決定要派什麼，
把新的工作包寫進對應樹的 `.dispatch/ACTIVE.md`，再把啟動 prompt 與工作包原文
寫回這個檔案（兩棵樹必須逐字同步，見 `.dispatch/reports/README.md` 與
`audit-dispatch.sh` 第 ⑦ 項）。
