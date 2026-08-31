# 啟動 prompt

**你只開一個 terminal，當 Leader。** 這一波只有一包。

這一波後端樹沒有生效包，純同步用。

---

## 你要做的只有這一步

開一個 terminal，貼下面這段。**就這樣。**

```
GG_ROLE=leader

你是 GreyGray Platform 的 Leader。讀 .dispatch/PROMPTS.md 與 .dispatch/ACTIVE.md，
把現在生效的 FE-20 用 mcp__ai-cli__run 派出去，然後等它回來做整合驗收。

派工規則：
  - 子代理的 prompt 用本檔案「工作包」那一節的原文，一字不改。
    開頭的 GG_PACKAGE=FE-20 那一行一定要留著——ai-cli 的 run 沒有 env 參數，
    包別只能靠 prompt 帶進去，UserPromptSubmit 會把它綁到那個子代理的 session_id。
  - workFolder：D:\WorkSpace\01_開發中_wip\GreyGray_Platform-fe（前端樹）。
    這一波不涉及後端樹，不需要派任何後端子代理。
  - 開工前先確認 git log 看得到 f8e18b1（契約同步：admin openapi.yaml 對齊
    BE-31）與 e322271（閘門歸零），且 pnpm typecheck／pnpm build 在動手之前
    就是全綠的基準線，不成立就停下來回報。

★ 收子代理的回報時，先看 .dispatch/reports/FE-20.md 在不在、三個標頭齊不齊。
  不齊就用同一個 session_id 接回去要它補完——不要自己幫它補，
  也不要因為 exit code 是 0 就當成完成。

你自己不寫原始碼。你寫得了的是 .dispatch/、.claude/、.codex/、docs/ 與 GreyGray_PM。
子代理回來之後由你做整合驗收：自己重跑 typecheck／build 複驗，並且**親自確認
訂單品項頁的短缺數量顯示與「退短缺款」按鈕出現條件正確**（docs/23 §5），
不要只轉述自述。驗收完先 commit，再撤包——順序反過來會讓未提交的交付變成
無主檔案，閘門會判成越界。
```

> **不要把 `GG_ROLE=leader` 放進子代理的 prompt。** 就算不小心放了也升不了級——
> `claim-package.sh` 讓包別優先於角色（已實測），但別依賴那道保險。

---

## 排程

```
只有一包：FE-20
```

---

## 兩條這一波開始機械檢查的規則

**① 自驗報告是檔案，不是對話。**
`.dispatch/reports/FE-20.md`，三個標頭一字不差：
`## 指令與輸出`、`## 逐條自驗`、`## 我發現但沒做的事`。
缺任何一個，`audit-dispatch.sh` 第 ⑧ 項會擋下 Leader 收工。

**② 不准把驗證丟背景、不准排程 wakeup。**
`pnpm build` 兩個 app 都跑完再回報，不要說「等背景跑完再回報」然後結束。

---

## 工作包（以下這一段就是子代理的 prompt，原文照抄）

### FE-20　訂單品項顯示短缺數量＋新增「退短缺款」操作入口

```
GG_PACKAGE=FE-20

你是 GreyGray Platform 前端的 FE-20。讀 docs/23-前端第九波派工書.md，
§0 §1 全部要看，然後照 §5 的 FE-20 那一節做。
再讀 .dispatch/reports/README.md（自驗報告的格式）。

開工前先確認 git log 看得到契約同步的提交（f8e18b1），且 pnpm typecheck／
pnpm build 在你動手之前就是全綠的基準線，不成立就停下來回報。

這一波要做的事：跟進後端第十五波（BE-31，ADR-026）已經凍結進契約的
「部分買到、短缺數量退款」流程。契約已經有 AdminOrderLine.quantityShortfall
欄位與 POST /v1/orders/{orderId}/lines/{lineId}/refund-shortfall 端點
（docs/api/openapi.admin.yaml 第 599-629 行、第 1290-1319 行），這一波純粹
是前端跟進顯示與操作入口，不重新設計任何業務邏輯。

設計方向（已經定死，不要重新設計，細節見 docs/23 §0 §1）：
- 第一步先跑 pnpm api:generate，把型別補齊（目前 types.admin.ts 還沒有
  quantityShortfall 與 refund-shortfall 這兩樣東西）。
- admin.ts 新增一個比照 cancelOrderLine 形狀的 API 函式，呼叫
  .../refund-shortfall，body 直接沿用既有的 CancelAdminOrderRequest 型別，
  不新增介面。
- 新增 RefundShortfallDialog.tsx，複製 CancelOrderLineDialog.tsx 的結構，
  文案換成「退短缺款」語境，額外顯示短缺件數。
- [orderId]/page.tsx 接線：新增 state／handler（比照 cancelLine／
  handleCancelLine 的模式，idempotency payload 的 kind 要跟現有的 'line'
  區分開，避免 key 撞在一起）；refundedAmount 欄旁邊顯示短缺數量；
  action 欄新增「退短缺款」按鈕，出現條件是
  (line.quantityShortfall ?? 0) > 0 && line.refundedAmount == null
  （這是契約文件自己寫的判斷式，照抄，不要自己重新設計）；按鈕要能跟既有
  的「取消此品項」按鈕同時出現，兩者不互斥。

明確不做：不改後端契約檔案（docs/api/*.yaml 不在這一包的 allow 清單裡，
覺得契約有問題就在自驗報告記錄，不要動手改）、不重新設計「短缺退款」的
業務邏輯或判斷條件、不處理 storefront（這一波只影響 admin）。

你自己不能宣告這一包通過或修完收工，你只能做完並交付、寫自驗報告，
由 Leader 做整合驗收與最終判斷。

檔案所有權（只准改這些路徑，見 docs/23 §3）：
  frontend/packages/api-client/src/types.admin.ts（由 pnpm api:generate 產生）
  frontend/packages/api-client/src/endpoints/admin.ts
  frontend/apps/admin/app/(dash)/orders/_components/RefundShortfallDialog.tsx（新檔）
  frontend/apps/admin/app/(dash)/orders/[orderId]/page.tsx
  frontend/apps/admin/app/(dash)/orders/_lib/labels.ts
  .dispatch/reports/FE-20.md（你的自驗報告）
  docs/、management/、STATE.md、CLAUDE.md、AGENTS.md（任何一包都寫得了）

不要碰整個工作區的 git 指令：git stash、git reset --hard、git clean、
git checkout -- .、以及 git commit。stash stack 是跨 worktree 共用的。

自驗要包含：pnpm api:generate 執行成功且型別看得到新增內容；pnpm typecheck
全過；pnpm build exit 0（admin 與 storefront 都跑）；git diff --name-only
只有允許的路徑；即時驗證短缺數量顯示與按鈕出現條件正確（如果資料庫沒有
現成的短缺訂單資料，用元件層級或替代資料的方式驗證，並在報告如實記錄
用的是替代驗證方式）；確認新對話框跟舊對話框可以同時掛在同一頁面 DOM 上
不互相干擾。

自驗完成後，把結果寫進 .dispatch/reports/FE-20.md，三個標頭一字不差：
## 指令與輸出
## 逐條自驗
## 我發現但沒做的事

寫完就停下來，等 Leader 做整合驗收。你不可以自己宣告通過。
```
