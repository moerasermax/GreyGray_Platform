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

**第五波已生效（2026-08-30）。** 兩包同時開，`GG_PACKAGE` 不是可選的。

| 包 | 主題 | 派工書 |
|---|---|---|
| FE-12 | **關掉 mock，對真後端跑一遍** 🔴 D 階段的第一步 | `docs/18` |
| FE-16 | 老闆拍板的四件前端跟進（ADR-027／028 等） | `docs/18` |

**啟動 prompt 在 `.dispatch/PROMPTS.md`**，Leader 用 ai-cli fan out。

> **FE-12 從第三波等到現在**，等的就是後端環境。2026-08-30 BE-13 修好
> `sc start` 錯誤 5（根因是 `nssm.exe` 的 ACL），YC 上兩個服務重開機後 9 秒自動起來，
> **這一包終於解除阻塞**。
>
> **FE-16 的後兩件（開團逾時欄位、出貨詳情端點）等後端 BE-19 的契約落地**，
> 前兩件（`NT$`、拿掉 KPI 假數字）現在就能做。

---

派工 FE-12：關掉 mock、對真後端跑一遍　·　docs/18-前端第五波派工書.md

package: FE-12
doc: docs/18-前端第五波派工書.md
allow: frontend/apps/admin/.env.local
allow: frontend/apps/storefront/.env.local

派工 FE-16：金額 NT$、拿掉 KPI 假數字、開團逾時欄位、出貨詳情端點　·　docs/18-前端第五波派工書.md

package: FE-16
doc: docs/18-前端第五波派工書.md
allow: frontend/packages/api-client/src/money.ts
allow: frontend/packages/api-client/src/__tests__/
allow: frontend/apps/admin/app/(dash)/page.tsx
allow: frontend/apps/admin/app/(dash)/_lib/dashboardMock.ts
allow: frontend/apps/admin/app/(dash)/campaigns/new/
allow: frontend/apps/admin/app/(dash)/shipments/_lib/api.ts

---

> **`money.ts` 是例外授權。** 那個檔平常在「沒有人擁有」的清單裡，
> 這次明確劃給 FE-16——因為 ADR-028 的正確做法**就是只改這一處**，
> 在呼叫端各自加前綴會變成散在幾十個元件裡的字串拼接。
>
> 改 `formatMoney()` 會影響**每一個顯示金額的地方**，包含別包的測試斷言。
> FE-16 只准修自己所有權內的測試；**別包的測試檔要列清單回報，不要自己改**。

---

## 已經通過、不再生效的（保留軌跡）

FE-1～FE-8（前兩波 ＋ 技術債收尾 `631e7bc`）· FE-9／FE-10／FE-11（第三波 `bbce5e1`）·
**第四波 FE-13／FE-14／FE-15（`e8156e6`，admin 60 條＋storefront 44 條全過）**
