# 啟動 prompt

**你只開一個 terminal，當 Leader。** 這一波只有一包，由 Leader 用 ai-cli fan out 出去。

兩棵 worktree 各一份、內容相同（這一波前端樹沒有生效包，純同步用）。

---

## 你要做的只有這一步

開一個 terminal，貼下面這段。**就這樣。**

```
GG_ROLE=leader

你是 GreyGray Platform 的 Leader。讀 .dispatch/PROMPTS.md 與 .dispatch/ACTIVE.md，
把現在生效的 BE-25 用 mcp__ai-cli__run 派出去，然後等它回來做整合驗收。

派工規則：
  - 子代理的 prompt 用本檔案「工作包」那一節的原文，一字不改。
    開頭的 GG_PACKAGE=BE-25 那一行一定要留著——ai-cli 的 run 沒有 env 參數，
    包別只能靠 prompt 帶進去，UserPromptSubmit 會把它綁到那個子代理的 session_id。
  - workFolder：D:\WorkSpace\01_開發中_wip\GreyGray_Platform（後端樹）。
    這一波只動 ops/，不涉及前端樹，不需要派任何前端子代理。

★ 收子代理的回報時，先看 .dispatch/reports/BE-25.md 在不在、三個標頭齊不齊。
  不齊就用同一個 session_id 接回去要它補完——不要自己幫它補，
  也不要因為 exit code 是 0 就當成完成。

你自己不寫原始碼。你寫得了的是 .dispatch/、.claude/、.codex/、docs/ 與 GreyGray_PM。
子代理回來之後由你做整合驗收：自己重跑 build 與測試複驗，並且**親自跑一次開發環境
端對端驗證**（docs/22 §5 自驗第 4 點），不要只轉述它的自述。
驗收完先 commit，再撤包——順序反過來會讓未提交的交付變成無主檔案，閘門會判成越界。
```

> **不要把 `GG_ROLE=leader` 放進子代理的 prompt。** 就算不小心放了也升不了級——
> `claim-package.sh` 讓包別優先於角色（已實測），但別依賴那道保險。

---

## 排程

```
只有一包：BE-25（不拆包——機密投遞的設計要一次想清楚）
```

---

## 兩條這一波開始機械檢查的規則

**① 自驗報告是檔案，不是對話。**
`.dispatch/reports/BE-25.md`，三個標頭一字不差：
`## 指令與輸出`、`## 逐條自驗`、`## 我發現但沒做的事`。
缺任何一個，`audit-dispatch.sh` 第 ⑧ 項會擋下 Leader 收工。

**② 不准把測試丟背景、不准排程 wakeup。**
`ops/test.ps1` 淨執行已超過工具單次前景呼叫的 10 分鐘上限（BE-24 實測約 13 分鐘），
分批前景跑完（照 `ops/test.ps1` 自己的迴圈，逐一跑每個測試專案的執行檔）。

---

## 工作包（以下這一段就是子代理的 prompt，原文照抄）

### BE-25　開發環境與正式機的機密投遞機制

```
GG_PACKAGE=BE-25

你是 GreyGray Platform 的 BE-25。讀 docs/22-後端第十波派工書.md，
§0 §1 全部要看，然後照 §5 的 BE-25 那一節做。
再讀 .dispatch/reports/README.md（自驗報告的格式）。

這一波要解兩個問題：① FE-12（第九波）發現開發環境缺 Identity:DataProtectionKey，
登入／註冊／購物車全部 500；② 追查後發現範圍更大——ops/deploy.ps1 對正式機
完全沒有投遞任何 ConnectionStrings 或應用層機密的機制，不只是綠界那五個值。

★ 開發環境已經有一套可行的機密模式在用（ops/install-dev-environment.ps1 的
  $InstallRoot\secrets\ 目錄 ＋ ACL 收緊 ＋ 明文檔案），這一波是把同一套模式
  延伸到正式機，不是發明新機制。

★ New-RandomPassword（install-dev-environment.ps1:54-59）不能拿來生
  Identity:DataProtectionKey——它會替換 Base64 字元，IdentityDataProtector
  要求解碼後正好 32 bytes（AES-256）。要另外寫一個不做字元替換的產生函式，
  派工書 §5 必做 1 有形狀示意。

★ 綠界（ECPay）的正式與測試憑證都沒有現成的值——不要自己編一組填進去。
  這一波只把投遞管道蓋好（正式機那半要接讀取邏輯，格式定義出來；
  開發環境那半連讀取邏輯都不用寫，因為沒有真實值可以驗證讀不讀得到）。

★ ops/deploy.ps1:17 的 -DatabaseName 預設值 'greygray' 是既有 bug（全 repo
  沒有 CREATE DATABASE，實際用的是內建的 postgres 資料庫），這一波順手修正。

四個檔案：ops/lib/Secrets.ps1（新檔）、ops/install-dev-environment.ps1、
ops/start-dev-hosts.ps1、ops/deploy.ps1。詳細怎麼改、每一步的理由都在
docs/22 §5，照著做，不要自己另外設計一套。

正式機那半（ops/deploy.ps1 的新邏輯）沒辦法在這個開發沙盒裡對真的 YC 機器
端對端驗證——這是真實的覆蓋缺口，自驗報告要明講，不要寫得像已經驗證過。
開發環境那半（install-dev-environment.ps1 ＋ start-dev-hosts.ps1）可以真的
跑起來驗證，而且要驗：跑完之後對 http://127.0.0.1:5001/v1/auth/login 送請求，
「缺少 Identity 個資保護金鑰」那個 500 要真的消失。

ops/test.ps1 要前景跑（超過 10 分鐘上限就分批跑），不准丟背景、不准排程 wakeup。
自驗寫進 .dispatch/reports/BE-25.md，三個標頭一字不差。
```

---

## 這一包適用的四件事

1. **子代理不可以自己宣告通過。** 自驗報告寫成檔案，貼**實際指令與實際輸出**。
   整合驗收是 Leader 的事，而且 Leader 要自己重跑複驗。
2. **遇到契約缺口或平台缺口就停下來回報**，不要自己補一個看起來合理的預設值。
   這一包尤其不要自己編造 ECPay 憑證值。
3. **你只有這一輪。** 不准排程 wakeup，不准把工作丟背景後結束。
   無法完成就**明講做不到與原因**。
4. **不要碰整個工作區的 git 指令**（`git stash`／`reset --hard`／`clean`／
   `checkout -- .`／`commit`）。stash stack 是跨 worktree 共用的。
