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
2. **每個實作者 session**：開工前設好自己的包名。

```bash
GG_PACKAGE=FE-9 claude          # Claude Code
GG_PACKAGE=FE-9 codex           # Codex
```

**`GG_PACKAGE` 不是可選的。** 同時派三包而沒設它時，
`allow` 清單會變成三包的**聯集**——閘門仍擋得住「整波之外」，
但擋不住 FE-9 去寫 FE-10 的檔案，而那正是所有權表要防的事。
沒設的話 `SessionStart` 會明講這件事。包名拼錯則一律擋下（fail-closed），
不會退化成「什麼都能寫」。

---

（FE-9／FE-10／FE-11 三包已啟用，2026-08-29。）

派工 FE-9：admin 契約同步與缺貨退款金額　·　docs/12-前端第三波派工書.md

package: FE-9
doc: docs/12-前端第三波派工書.md
allow: frontend/packages/api-client/src/types.admin.ts
allow: frontend/packages/api-client/src/mock/fixtures.admin.ts
allow: frontend/packages/api-client/src/mock/handlers.admin.ts
allow: frontend/apps/admin/app/(dash)/orders/
allow: docs/api/openapi.admin.yaml

派工 FE-10：後台登入接上真 API　·　docs/12-前端第三波派工書.md

package: FE-10
doc: docs/12-前端第三波派工書.md
allow: frontend/apps/admin/app/login/
allow: frontend/apps/admin/app/_lib/
allow: frontend/apps/admin/middleware.ts
allow: frontend/packages/api-client/src/mock/handlers.admin.auth.ts
allow: frontend/packages/api-client/src/mock/handlers.admin.ts

派工 FE-11：後台採購清單與買到回報　·　docs/12-前端第三波派工書.md

package: FE-11
doc: docs/12-前端第三波派工書.md
allow: frontend/apps/admin/app/(dash)/procurement/
allow: frontend/apps/admin/app/(dash)/layout.tsx
allow: frontend/packages/api-client/src/mock/fixtures.admin.procurement.ts
allow: frontend/packages/api-client/src/mock/handlers.admin.procurement.ts
allow: frontend/packages/api-client/src/mock/handlers.admin.ts

> **三包都列了 `handlers.admin.ts`**，因為每包各被授權在 `adminHandlers` 陣列尾端加一行。
> 閘門擋不住「加了不只一行」——那一條由整合驗收時逐行看 diff 把關
> （`docs/12` §3 例外授權第 2 條）。
