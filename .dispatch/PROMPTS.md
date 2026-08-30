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
  - 先派 BE-22——它解的是 D 階段的阻塞，而且 FE-12 在等它。
  - BE-21 會跑 ops/test.ps1（淨執行約 10 分鐘），BE-22 不會，兩者不搶 build。

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
可同時開            BE-22   BE-21   FE-17
BE-22 過了才開      FE-12（前端，關掉 mock 對真後端跑一遍）
BE-21 過了才開      BE-20（部分買到）
                    （FE-12／BE-20 目前在 ACTIVE.md 裡都是註解掉的）
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

### BE-22　本機開發環境（`D:\GreyGray`）　🔴 先派這包

```
GG_PACKAGE=BE-22

你是 GreyGray Platform 的 BE-22。讀 docs/19-後端第八波派工書.md，
§1 §2 §3 全部要看，然後照 §5 的 BE-22 那一節做。
再讀 .dispatch/reports/README.md（自驗報告的格式）。

D 階段 0/7，而 FE-12（前端對真後端跑一遍）是它的第一步，
FE-12 從第三波等到現在還是開不了工——因為 YC 上的服務只綁 loopback
（那對正式機是正確的設定），開發機連不到，而三個 API Host 從沒部署上去。
老闆拍板：在開發機自建一組。

★ 裝在 D:\GreyGray，不要用 C 槽。install-environment.ps1 的
InstallRoot／PostgreSqlDataRoot／PostgreSqlWalRoot 本來就是參數，指過去就好。
不要改腳本的預設值——正式機 YC 仍然用 C:\GreyGray，那是對的。
如果腳本裡還有別的地方把 C:\GreyGray 寫死（沒走參數），那就是 bug，
修它並在自驗報告列出改了哪幾行。

要跑到：PG 17 與 Garnet 起來、migrations 0001_~0014_ 全套上、
三個 Host 的 /health 回得了。第三件是 FE-12 能不能開工的判準。

不要動正式機 YC 的任何設定。不要把密碼放進命令列。
自驗寫進 .dispatch/reports/BE-22.md，三個標頭一字不差。
```

### BE-21　帶回→待出貨接線 ＋ `OrderLineId` 改必填

```
GG_PACKAGE=BE-21

你是 GreyGray Platform 的 BE-21。讀 docs/19-後端第八波派工書.md，
§1 §2 §3 全部要看，然後照 §5 的 BE-21 那一節做。
再讀 .dispatch/reports/README.md（自驗報告的格式）。

兩件事，都很小，但第二件是帳務相關的：

一、接線。第六波驗收發現「帶回入庫後訂單轉待出貨」這條線是斷的，
    第七波 BE-19 已經讓 GoodsReceived.v1 帶出 OrderLineId，但沒有人訂閱它。
    IOrderingGoodsReceipt.RecordGoodsReceivedAsync 已存在、已實作、已 DI 註冊，
    全 repo 沒有任何呼叫點。在 Ordering.Infra 加一個
    IIntegrationEventHandler<GoodsReceived> 呼叫既有的 port，
    比照 Inventory.Infra/ModuleRegistration.cs 的 AddIdempotentIntegrationEventHandler。
    不必改 Ordering.Core——那個 port 已經夠用，而且 Core 不在你的 allow。

二、GoodsReceived.OrderLineId 從可選參數（= default）改成必填。
    現在漏傳會靜默送空值而且照樣編得過。這個事件從沒上過正式機，
    現在改沒有相容性成本，之後才改就要發 v2。
    ★ 這是帳務相關的欄位：漏傳的後果是訂單永遠不會轉待出貨，而且不會報錯。
    改成必填之後編譯器會列出所有建構點，逐一補上。

★ ops/test.ps1 要前景跑（淨執行約 10 分鐘），不准丟背景、不准排程 wakeup。
自驗寫進 .dispatch/reports/BE-21.md，三個標頭一字不差。
端對端測試要用真 PostgreSQL（Testcontainers），而且要有
「兩條預購 line 只帶回一條時不轉待出貨」那條。
```

### FE-17　前台運費文案改從契約來

```
GG_PACKAGE=FE-17

你是 GreyGray Platform 的 FE-17。讀 docs/20-前端第六波派工書.md，
§1 §2 §3 全部要看，然後照 §5 的 FE-17 那一節做。
再讀 .dispatch/reports/README.md（自驗報告的格式）。

apps/storefront/app/(checkout)/_lib/labels.ts:13-14 把運費寫死成
「一口價 NT$60／NT$120」。那是 ADR-010 的舊硬編碼、第二波就有了，
FE-16 回報但不在它的所有權。

運費金額一旦調整，畫面會跟實際收的不一致而且不會報錯；
M3 的運費規則引擎一上來這兩行就會變成陳年錯誤，而到時候沒人記得它在那裡。

改成從契約來：shippingFee 後端會回，用 formatMoney() 渲染。
★ 不要自己算、不要自己拼字串。formatMoney() 現在對 TWD 已經會輸出 NT$
（ADR-028 已落地），所以不要再手動加前綴。
「超商／宅配」的方法說明可以是靜態文案，金額不行。

契約沒有回運費的地方（例如結帳前的購物車）不要自己猜 60／120——
那就是契約缺口，停下來回報。

驗收條件之一：grep -rn 'NT[$]' apps/storefront 應為 0 筆（前綴只准在 money.ts 裡加）。
自驗寫進 .dispatch/reports/FE-17.md，三個標頭一字不差。
```

---

## 三包都適用的四件事

1. **子代理不可以自己宣告通過。** 自驗報告寫成檔案，貼**實際指令與實際輸出**。
   整合驗收是 Leader 的事，而且 Leader 要自己重跑複驗。
2. **遇到契約缺口或平台缺口就停下來回報**，不要自己補一個看起來合理的預設值。
   這個專案已經有八次這樣的回報，八次都對，六次直接變成 ADR。
3. **你只有這一輪。** 不准排程 wakeup、不准把工作丟背景後結束。
   `ops/test.ps1` 前景跑得完（584 秒）。無法完成就**明講做不到與原因**。
4. **不要碰整個工作區的 git 指令**（`git stash`／`reset --hard`／`clean`／
   `checkout -- .`／`commit`）。stash stack 是跨 worktree 共用的。
