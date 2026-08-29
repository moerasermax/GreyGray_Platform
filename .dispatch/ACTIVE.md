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

## 開工的兩個步驟

1. **整合者**：把下面要派的那一段的 `<!--` 與 `-->` 兩行刪掉，讓它生效。
2. **每個實作者 session 要宣告自己是哪一包。** 兩條路，擇一：

**A. 自己開 terminal** —— 用環境變數：

```bash
GG_PACKAGE=FE-9 claude
```

**B. lead 用 ai-cli fan out 子 agent** —— 在子 agent 的 prompt 裡寫一行 `GG_PACKAGE=FE-9`。
`UserPromptSubmit` 會認出它，把包別綁到那個 session_id 上，之後的閘門就照那一包判斷。
多個子 agent 同時跑不會互相蓋掉，因為標記檔名就是各自的 session_id。

> 為什麼 B 不能用環境變數：ai-cli 的 `run` 只吃 `workFolder`／`prompt`／`model`／
> `reasoning_effort`／`session_id`，**沒有 env 參數**；而且它的子行程是
> `env: process.env`（`src/core/process-service.ts`），繼承的是 MCP server 自己的環境。
> 一個 server 行程 spawn 所有子 agent，行程層級的環境變數本質上帶不了
> 「每個子 agent 不同」的值。所以綁定只能走 session_id。

**宣告不是可選的。** 沒宣告時 `allow` 是所有生效包的**聯集**——閘門仍擋得住「整波之外」，
但擋不住一包去寫另一包的檔案，而那正是所有權表要防的事。
包名拼錯一律擋下（fail-closed）。一個 session 只認第一次宣告，之後想改包會被拒絕。

**`GG_PACKAGE` 不是可選的。** 同時派三包而沒設它時，
`allow` 清單會變成三包的**聯集**——閘門仍擋得住「整波之外」，
但擋不住 FE-9 去寫 FE-10 的檔案，而那正是所有權表要防的事。
沒設的話 `SessionStart` 會明講這件事。包名拼錯則一律擋下（fail-closed），
不會退化成「什麼都能寫」。

---

**第四波已生效（2026-08-30）。** 三包同時開，`GG_PACKAGE` 不是可選的。

| 包 | 主題 | 派工書 |
|---|---|---|
| FE-13 | 後台：出貨、交運與簽收 | `docs/15` |
| FE-14 | 後台：缺貨補償、漲價詢問 ＋ **退款去向回工**（ADR-023） | `docs/15` |
| FE-15 | 前台：客人看得到自己的品項缺貨與退款 | `docs/15` |

**啟動 prompt 在 `.dispatch/PROMPTS.md`**，直接複製貼上。

---

派工 FE-13：後台出貨、交運與簽收　·　docs/15-前端第四波派工書.md

package: FE-13
doc: docs/15-前端第四波派工書.md
allow: frontend/apps/admin/app/(dash)/shipments/
allow: frontend/apps/admin/app/(dash)/layout.tsx
allow: frontend/packages/api-client/src/mock/fixtures.admin.shipments.ts
allow: frontend/packages/api-client/src/mock/handlers.admin.shipments.ts
allow: frontend/packages/api-client/src/mock/handlers.admin.ts

派工 FE-14：後台缺貨補償、漲價詢問 ＋ 退款去向回工　·　docs/15-前端第四波派工書.md

package: FE-14
doc: docs/15-前端第四波派工書.md
allow: frontend/apps/admin/app/(dash)/orders/
allow: frontend/apps/admin/app/(dash)/procurement/
allow: frontend/packages/api-client/src/mock/fixtures.admin.compensation.ts
allow: frontend/packages/api-client/src/mock/handlers.admin.compensation.ts
allow: frontend/packages/api-client/src/mock/handlers.admin.ts

派工 FE-15：前台缺貨與退款的呈現　·　docs/15-前端第四波派工書.md

package: FE-15
doc: docs/15-前端第四波派工書.md
allow: frontend/apps/storefront/app/(account)/orders/
allow: frontend/packages/api-client/src/mock/fixtures.storefront.orderlines.ts
allow: frontend/packages/api-client/src/mock/handlers.storefront.orderlines.ts
allow: frontend/packages/api-client/src/mock/handlers.storefront.ts

---

<!--
派工 FE-12：關掉 mock、對真後端跑一遍　·　docs/15-前端第四波派工書.md
⏸ 等後端 BE-13（M-1 環境收尾）通過整合驗收才啟用。
解除條件：ops/verify-environment.ps1 全 PASS、兩個服務重開機後自動起得來。

package: FE-12
doc: docs/15-前端第四波派工書.md
allow: frontend/apps/admin/.env.local
allow: frontend/apps/storefront/.env.local
-->

> **FE-13 與 FE-14 都列了 `handlers.admin.ts`**、**FE-15 列了 `handlers.storefront.ts`**，
> 因為每包各被授權在對應的 handlers 陣列尾端**加一行**。
> 閘門擋不住「加了不只一行」——那一條由整合驗收時逐行看 diff 把關
> （`docs/15` §4 例外授權第 1 條）。
>
> **`(dash)/layout.tsx` 只給 FE-13**（加一個「出貨」選單項）。FE-14 不需要它，
> 因為 orders 與 procurement 的選單項已經存在。
>
> **FE-14 會改到第三波 FE-9 擁有的 `orders/`**，那是**跨波修改，不是違規**——
> 所有權表是同一波之內的邊界。

---

## 已經通過、不再生效的（保留軌跡）

FE-1～FE-8（前兩波 ＋ 技術債收尾 `631e7bc`）·
FE-9／FE-10／FE-11（第三波，`bbce5e1`，24 個變更檔零越界）
