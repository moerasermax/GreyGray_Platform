# 啟動 prompt

**你只開一個 terminal，當 Leader。** 這一波只有一包，由 Leader 用 ai-cli fan out 出去。

這一波前端樹沒有生效包，純同步用。

---

## 你要做的只有這一步

開一個 terminal，貼下面這段。**就這樣。**

```
GG_ROLE=leader

你是 GreyGray Platform 的 Leader。讀 .dispatch/PROMPTS.md 與 .dispatch/ACTIVE.md，
把現在生效的 BE-26 用 mcp__ai-cli__run 派出去，然後等它回來做整合驗收。

派工規則：
  - 子代理的 prompt 用本檔案「工作包」那一節的原文，一字不改。
    開頭的 GG_PACKAGE=BE-26 那一行一定要留著——ai-cli 的 run 沒有 env 參數，
    包別只能靠 prompt 帶進去，UserPromptSubmit 會把它綁到那個子代理的 session_id。
  - workFolder：D:\WorkSpace\01_開發中_wip\GreyGray_Platform（後端樹）。
    這一波只動 src/Tools/、GreyGray.slnx、架構測試那一行、ops/seed*，
    不涉及前端樹，不需要派任何前端子代理。

★ 收子代理的回報時，先看 .dispatch/reports/BE-26.md 在不在、三個標頭齊不齊。
  不齊就用同一個 session_id 接回去要它補完——不要自己幫它補，
  也不要因為 exit code 是 0 就當成完成。

你自己不寫原始碼。你寫得了的是 .dispatch/、.claude/、.codex/、docs/ 與 GreyGray_PM。
子代理回來之後由你做整合驗收：自己重跑 build 與測試複驗，並且**親自跑一次開發環境
端對端驗證**（docs/23 §6 自驗第 4 點——真的對種下去的帳號送登入請求），
不要只轉述它的自述。驗收完先 commit，再撤包——順序反過來會讓未提交的交付變成
無主檔案，閘門會判成越界。
```

> **不要把 `GG_ROLE=leader` 放進子代理的 prompt。** 就算不小心放了也升不了級——
> `claim-package.sh` 讓包別優先於角色（已實測），但別依賴那道保險。

---

## 排程

```
只有一包：BE-26（不拆包——bootstrap 工具跟種子清單是同一個問題的兩面）
```

---

## 兩條這一波開始機械檢查的規則

**① 自驗報告是檔案，不是對話。**
`.dispatch/reports/BE-26.md`，三個標頭一字不差：
`## 指令與輸出`、`## 逐條自驗`、`## 我發現但沒做的事`。
缺任何一個，`audit-dispatch.sh` 第 ⑧ 項會擋下 Leader 收工。

**② 不准把測試丟背景、不准排程 wakeup。**
`ops/test.ps1` 淨執行已超過工具單次前景呼叫的 10 分鐘上限，
分批前景跑完（照 `ops/test.ps1` 自己的迴圈，逐一跑每個測試專案的執行檔）。

---

## 工作包（以下這一段就是子代理的 prompt，原文照抄）

### BE-26　員工帳號 bootstrap 工具 ＋ 開發環境最小種子資料

```
GG_PACKAGE=BE-26

你是 GreyGray Platform 的 BE-26。讀 docs/23-後端第十一波派工書.md，
§0 §1 全部要看，然後照 §5 的 BE-26 那一節做。
再讀 .dispatch/reports/README.md（自驗報告的格式）。

這一波要解的問題：FE-12（第九波）發現 iam.staff_account 等表全部 0 筆，
沒有任何路徑能建出第一個員工帳號——第十波 BE-25 把登入端點的 500 修成 401 之後，
401 本身就是终點，因為沒有帳號可登，401 永遠是 401。

★ IStaffAccounts.CreateAsync（src/Modules/Identity/GreyGray.Modules.Identity.Core/
  IdentityServices.cs:436-475）已經實作好、已經被測過，你不是要重新設計建帳號的
  邏輯，是要寫一支呼叫它的獨立 console 工具。AddIdentityModule(configuration) ＋
  AddGreyGrayRuntimeContext() 已經把整條 DI 鏈接好，組合順序照
  src/Hosts/GreyGray.Worker/Program.cs 的形狀（那是你的範本），只留 Identity
  這一個模組，不要拉其他 12 個模組的 Infra。

★ 這是獨立的 console 工具，不是 HTTP 端點——docs/05-API契約.md 已凍結，
  不要在任何 Host 的 Program.cs 加路由，三個 Host 的 Program.cs 不在你的允許
  路徑內。

★ 密碼產生用 C# 自己的 RandomNumberGenerator，跟 docs/22（Identity:DataProtectionKey
  那個「解碼後必須正好 32 bytes」的限制）是不同的東西——StaffAccountService.CreateAsync
  只把 Password 當一般字串雜湊，不 base64-decode，不要被那個坑誤導成也要處理
  字元替換。

★ 新專案要放進 GreyGray.slnx（新增 /src/Tools/ 資料夾）才會被 dotnet build 涵蓋；
  架構測試不需要你手動加 ProjectReference（ProjectGraph.Load() 掃全 repo 找
  *.csproj），你只需要在 ModuleBoundaryTests.cs 的 Hosts 陣列加一行
  "GreyGray.Tools.StaffBootstrap"。

五個路徑：src/Tools/（新目錄）、GreyGray.slnx、
tests/GreyGray.Architecture.Tests/ModuleBoundaryTests.cs（只加一行）、
ops/seed/（新目錄，種子清單）、ops/seed-dev-staff.ps1（新檔）。
詳細怎麼做、每一步的理由都在 docs/23 §5，照著做，不要自己另外設計一套。

種子清單四筆，對應 StaffRole 四個角色（Owner／Operator／Accountant／ReadOnly），
給 FE-12 §5「四角色登入」用，格式在 docs/23 §5 必做 2。

驗收核心是「真的跑起來」：跑 ops\seed-dev-staff.ps1 對 D:\GreyGray 種出 4 筆帳號，
查 iam.staff_account 真的是 4 筆；重跑一次證明冪等（還是 4 筆，不是 8 筆）；
啟動三個 Host 之後對其中一個種子帳號送登入請求，貼出成功登入的回應
（200，帶出 StaffProfile）。密碼不要貼進報告，只在自己終端機裡看。

ops/test.ps1 要前景跑（超過 10 分鐘上限就分批跑），不准丟背景、不准排程 wakeup。
自驗寫進 .dispatch/reports/BE-26.md，三個標頭一字不差。
```

---

## 這一包適用的四件事

1. **子代理不可以自己宣告通過。** 自驗報告寫成檔案，貼**實際指令與實際輸出**。
   整合驗收是 Leader 的事，而且 Leader 要自己重跑複驗。
2. **遇到契約缺口或平台缺口就停下來回報**，不要自己補一個看起來合理的預設值。
3. **你只有這一輪。** 不准排程 wakeup，不准把工作丟背景後結束。
   無法完成就**明講做不到與原因**。
4. **不要碰整個工作區的 git 指令**（`git stash`／`reset --hard`／`clean`／
   `checkout -- .`／`commit`）。stash stack 是跨 worktree 共用的。
