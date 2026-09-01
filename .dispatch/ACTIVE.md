# 生效中的派工

**這個檔案是閘門的唯一輸入。** 只有寫在這裡的 `allow:` 路徑才准寫入原始碼。
由**整合者**維護；實作者模式下閘門會擋住對這個檔案本身的寫入，
所以實作者不能自己擴大授權。

格式：

```
package: <包名>
doc: <派工書路徑>
allow: <相對 repo 根的路徑前綴，一行一個>
```

沒有任何 `package:` 行 = 整合者模式，原始碼一律不准寫。
**註解掉（`<!-- -->`）與圍欄（```）裡的內容不會被讀進來**，可以安心放範例。

---

## 開工：你只開一個 terminal

**Leader 模型。** 使用者開一個 terminal 當 Leader，Leader 用 ai-cli fan out 子代理，
一包一個子代理。使用者不必一包一包開 terminal，也不必自己設 `GG_PACKAGE`。

**Leader 的啟動 prompt 與八包的原文都在 `.dispatch/PROMPTS.md`。**

### 三種身分，閘門分得出來

| 身分 | 怎麼宣告 | 寫得了什麼 |
|---|---|---|
| **Leader** | prompt 開頭 `GG_ROLE=leader`，或 `GG_ROLE=leader claude` | 閘門檔（`.dispatch/`、`.claude/`、`.codex/`）、`docs/`、`GreyGray_PM`。**不寫原始碼** |
| **實作者** | prompt 開頭 `GG_PACKAGE=<包名>`，或 `GG_PACKAGE=<包名> codex` | 只有該包 `allow:` 的路徑，加上全域放行的 `docs/` 等 |
| **身分不明** | 沒宣告 | **什麼都寫不了**（有派工生效時）。這是刻意的 fail-closed |

第三列是重點：**「忘記宣告的實作者」與「Leader」從外面看一模一樣**，
所以不能用「沒綁包別」推定是 Leader——那會讓忘記宣告的人擁有改閘門的權力。
Leader 要明講。

> **包別優先於角色。** 子代理的 prompt 一定帶 `GG_PACKAGE=`；
> 萬一同一段文字裡也混進 `GG_ROLE=leader`（例如 Leader 把整份說明貼過去），
> 子代理仍然只會被綁成實作者，不會升級。已實測。

> 為什麼子代理不能用環境變數：ai-cli 的 `run` 只吃 `workFolder`／`prompt`／`model`／
> `reasoning_effort`／`session_id`，**沒有 env 參數**；而且它的子行程是
> `env: process.env`（`src/core/process-service.ts`），繼承的是 MCP server 自己的環境。
> 一個 server 行程 spawn 所有子代理，行程層級的環境變數本質上帶不了
> 「每個子代理不同」的值。所以綁定只能走 prompt ＋ session_id。

一個 session 只認第一次宣告，之後想改包或改角色都會被拒絕。
包名拼錯一律擋下（fail-closed）。

---

派工 FE-21：後台首頁接上真的帳務端點（#29）　·　docs/24-前端第十波派工書.md

2026-09-01 使用者登入後台後，Leader 用真 Chrome 逐頁測，查出**後台首頁的財務數字與
最近分錄是寫死的假資料**：首頁顯示「客戶負債 NT$1,280,000／現金 NT$860,000、
資料時間 2026年8月28日」並跳紅字警示「正在用還沒交貨的錢過日子」，
但同一份資料在帳務頁與 API 是「客戶負債 NT$0／現金 -NT$1,920、即時」；
首頁「最近分錄」列 6 筆，而 `GET /v1/ledger/entries` **只有 1 筆**。
**兩頁互相矛盾，而老闆會看首頁做經營判斷。**

★ Leader 已查證並裁決：
① 根因是 `(dash)/_lib/dashboardMock.ts` 的兩個 fixture 被 `(dash)/page.tsx` 直接使用。
   **那個檔案自己的註解就寫著要換成 `GET /v1/ledger/liability-vs-cash` 與
   `GET /v1/ledger/entries`**，而這兩個端點現在都存在也都正常，
   `packages/api-client` 的 `getLiabilityVsCash`／`listLedgerEntries` 也早就有。
② **不要重新設計**：`(dash)/ledger/page.tsx` 已經正確地做完這件事
   （Leader 在真瀏覽器裡確認它顯示真資料），照抄那個形狀。
③ **四個 KPI 卡維持「尚未提供」**——那是誠實的空值，不是假數字；
   更不准用列表 API 在前端加總（列表有分頁，加出來只是這一頁的合計）。
④ 必須補測試釘住「首頁顯示的數字來自 API」。**#29 能活到現在，
   正是因為沒有任何測試斷言過這件事**；沒有那條測試，改完還會再退化。
⑤ 載入中與失敗時**絕對不准 fallback 回任何寫死的數字**——那正是這個 bug 的成因。

package: FE-21
doc: docs/24-前端第十波派工書.md
allow: frontend/apps/admin/app/(dash)/page.tsx
allow: frontend/apps/admin/app/(dash)/_lib/
allow: frontend/apps/admin/app/(dash)/_components/LiabilityVsCashCard.tsx
allow: frontend/apps/admin/app/(dash)/__tests__/

---

<!--
★ 2026-08-31 已通過整合驗收並提交（前端 31ce3b1），撤包。原文保留供追溯。
跟進後端第十五波（BE-31，ADR-026）已凍結進契約的 quantityShortfall 欄位與
refund-shortfall 端點。子代理過程中撞到 ai-cli MCP 連線中斷，Leader 改用
ListAgents／SendMessage 直接聯繫同一個子代理 session 續完；審查時發現子代理
留了一段除錯用的假資料 fixture 誤寫進 page.tsx 的初始 state（會讓正式頁面
短暫顯示假訂單），已要求子代理自行改回並補完自驗報告，Leader 複驗通過。
即時驗證受限於這個環境沒有瀏覽器自動化工具、(dash) 版面 SSR 恆回傳空殼、
dev DB 目前沒有任何 order 資料，只做到型別／邏輯層級驗證，詳見
.dispatch/reports/FE-20.md 與 GreyGray_PM/03-驗收紀錄.md。

派工 FE-20：訂單品項顯示短缺數量＋新增「退短缺款」操作入口　·　docs/23-前端第九波派工書.md

package: FE-20
doc: docs/23-前端第九波派工書.md
allow: frontend/packages/api-client/src/types.admin.ts
allow: frontend/packages/api-client/src/endpoints/admin.ts
allow: frontend/apps/admin/app/(dash)/orders/_components/RefundShortfallDialog.tsx
allow: frontend/apps/admin/app/(dash)/orders/[orderId]/page.tsx
allow: frontend/apps/admin/app/(dash)/orders/_lib/labels.ts
-->

---

<!--
★ 2026-08-31 已通過整合驗收並提交（前端 ebdd36d），撤包。原文保留供追溯。
修法（middleware 讀 x-forwarded-host／host 手動組 origin）即時驗證證實生效，
見 .dispatch/reports/FE-19.md。

派工 FE-19：middleware 改讀反向代理 header 手動組 origin，取代失敗的 trustHostHeader　·　docs/22-前端第八波派工書.md

package: FE-19
doc: docs/22-前端第八波派工書.md
allow: frontend/apps/admin/next.config.ts
allow: frontend/apps/admin/middleware.ts
-->

---

<!--
★ 2026-08-31 已通過整合驗收並提交（前端 83d2fda），撤包。原文保留供追溯。
診斷性交付：修法對目前部署拓樸沒有效果，見 .dispatch/reports/FE-18.md。
下一波 FE-19（docs/22）改用 middleware 讀反向代理 header 的做法接手。

派工 FE-18：修 admin 正式機導向永遠指向 localhost　·　docs/21-前端第七波派工書.md

package: FE-18
doc: docs/21-前端第七波派工書.md
allow: frontend/apps/admin/next.config.ts
-->

---

<!--
★ 2026-08-30 已執行並提交（前端 d48acbe），撤包。原文保留供追溯。
FE-12 沒有把 §5 全部六條走完——不是前端沒做完，是後端環境缺
Identity:DataProtectionKey／Payment:ECPay:MerchantId 兩項設定、
資料庫是空的且無法建立員工帳號、admin BFF 三組端點永久不回應。
FE-12 正確地只記錄不修改，詳見 .dispatch/reports/FE-12.md
與 GreyGray_PM/03-驗收紀錄.md。

派工 FE-12：關掉 mock、對真後端跑一遍　·　docs/18-前端第五波派工書.md

package: FE-12
doc: docs/18-前端第五波派工書.md
allow: frontend/apps/admin/.env.local
allow: frontend/apps/storefront/.env.local
-->

---

<!--
★ 2026-08-30 已通過整合驗收並提交（前端 50c314c），撤包。原文保留供追溯。

派工 FE-17：前台運費文案改從契約來　·　docs/20-前端第六波派工書.md

package: FE-17
doc: docs/20-前端第六波派工書.md
allow: frontend/apps/storefront/app/(checkout)/
-->

---

## 已經通過、不再生效的（保留軌跡）

- **FE-20** 訂單品項顯示短缺數量＋新增「退短缺款」操作入口　·　2026-08-31 通過　·　`31ce3b1`　·
  跟進 BE-31（ADR-026）契約；即時驗證受限於環境（無瀏覽器自動化、dev DB 無 order 資料），
  只做到型別／邏輯層級驗證，見 `.dispatch/reports/FE-20.md`
- **FE-19** middleware 改讀反向代理 header 手動組 origin，取代失敗的 trustHostHeader　·　2026-08-31 通過　·　`ebdd36d`　·
  修法生效，接手 FE-18 沒解決的部分，見 `.dispatch/reports/FE-19.md`
- **FE-18** 修 admin 正式機導向永遠指向 localhost（診斷性交付）　·　2026-08-31 通過　·　`83d2fda`　·
  修法（`experimental.trustHostHeader`）對目前部署拓樸沒有效果，見 `.dispatch/reports/FE-18.md`；
  下一波 FE-19 改用 middleware 讀反向代理 header 接手
- **FE-12** 關掉 mock、對真後端跑一遍　·　2026-08-30 執行　·　`d48acbe`　·
  §5 部分完成（mock 關閉、build/test/typecheck 全過），完整流程被後端環境缺口擋住，見自驗報告
- **FE-17** 前台運費文案改從契約來　·　2026-08-30 通過　·　`50c314c`

FE-1～FE-8（前兩波 ＋ 技術債收尾 `631e7bc`）· FE-9／FE-10／FE-11（第三波 `bbce5e1`）·
第四波 FE-13／FE-14／FE-15（`e8156e6`）·
第五波 FE-16（`6165419`）
