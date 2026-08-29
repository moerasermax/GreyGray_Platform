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

**第七波已生效（2026-08-30）。** 三包同時開，`GG_PACKAGE` 不是可選的。

| 包 | 主題 | 派工書 |
|---|---|---|
| BE-17 | 綠界原路退款 API（ADR-024 前半）🔴 最擋 M1b | `docs/17` |
| BE-18 | Ordering：StoredValue 擋 ＋ 鑑賞期 Saga Timer（ADR-024 後半／ADR-025） | `docs/17` |
| BE-19 | 契約與事件形狀異動 ＋ 帶回→待出貨接線（ADR-027） | `docs/17` |

**啟動 prompt 在 `.dispatch/PROMPTS.md`**，Leader 用 ai-cli fan out。

> ⚠️ **三包都會跑 `ops/test.ps1`，要排開不要同時跑。**
>
> ⚠️ **BE-18 與 BE-19 都碰 Ordering，但檔案分開**：BE-18 是 `Ordering.Core` ＋
> `OrderingDbContext.cs`，BE-19 是 `Ordering.Infra` 的 `ModuleRegistration.cs` 與新 handler 檔。
> 兩邊都不准動對方的檔，詳見 `docs/17` §3。

---

派工 BE-17：綠界原路退款 API　·　docs/17-後端第七波派工書.md

package: BE-17
doc: docs/17-後端第七波派工書.md
allow: src/Modules/Payment/
allow: tests/

派工 BE-18：Ordering StoredValue 擋 ＋ 鑑賞期 Saga Timer　·　docs/17-後端第七波派工書.md

package: BE-18
doc: docs/17-後端第七波派工書.md
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Core/
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Infra/OrderingDbContext.cs
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Infra/OrderCompletionSaga
allow: db/migrations/0013_
allow: tests/

派工 BE-19：契約與事件形狀異動 ＋ 帶回接線　·　docs/17-後端第七波派工書.md

package: BE-19
doc: docs/17-後端第七波派工書.md
allow: docs/api/
allow: src/Modules/Procurement/
allow: src/Modules/Campaign/
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Infra/ModuleRegistration.cs
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Infra/GoodsReceivedOrderingHandler.cs
allow: db/migrations/0014_
allow: tests/

---

<!--
派工 BE-20：部分買到（ADR-026）　·　docs/17-後端第七波派工書.md
⏸ 等 BE-17／BE-18／BE-19 三包都通過整合驗收才啟用。
會跨 Procurement／Ordering／Ledger，所有權表等開工前再配。

package: BE-20
doc: docs/17-後端第七波派工書.md
allow: tests/
-->

> **`tests/` 給了三個包**，設了 `GG_PACKAGE` 也分不開。
> BE-17 只准動 `tests/**/Payment*`、BE-18 只准動 `tests/**/Ordering*`、
> BE-19 只准動 `tests/**/Procurement*`。這一條由總驗收第 1 條逐檔看 diff 把關。
>
> `GreyGray.slnx`、各 Host 的 `Program.cs`、`M1bFulfillmentEndpoints.cs`
> **不在任何 allow 清單裡**——`docs/17` §3 只授權「加一行／加一條 endpoint」，
> 那種變更請整合者代為套用，或由整合者臨時開一筆 package 留痕。

---

## 已經通過、不再生效的（保留軌跡）

BE-1～BE-8（M0）· BE-10（M-1 腳本化 `2c05c99`）· BE-12（M1b-3a `dbec9cd`）·
**第六波 BE-13／BE-9／BE-11／BE-14／BE-15（`de3a022`，157 條測試全綠）**
