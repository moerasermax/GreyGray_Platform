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

**第九波已生效（2026-08-30）。** 兩包同時開，都是修 bug，不涉及新決定。

| 包 | 主題 | 派工書 |
|---|---|---|
| BE-23 | `Order.cs:206` 誤設 `RefundedCurrency` ＋ EF model 補三條 `orders_*` 約束 🔴 FE-12 付款會踩到 | `docs/21` |
| BE-24 | `0003_channel_seams.sql` 對 `ledger.account` 的 seed 重放非冪等 | `docs/21` |
| FE-12 | 關掉 mock 對真後端跑一遍（開工前先跑 `ops/start-dev-hosts.ps1`） | `docs/18`（前端樹） |

**啟動 prompt 在 `.dispatch/PROMPTS.md`**，Leader 用 ai-cli fan out。

> ⚠️ **為什麼這兩包跟上一波無關卻要現在修**：兩個都是 BE-21／BE-22
> 在自己的自驗過程中、在自己 allow 之外撞到的既有 bug，當時正確地沒有動它們。
> 兩個都不需要新決定，是機械可派的修正。

> ⚠️ **自驗報告是檔案，不是對話。** 每包要寫 `.dispatch/reports/<包名>.md`，
> 三個固定標頭缺一不可，`audit-dispatch.sh` 第 ⑧ 項會擋。
> 規格見 `.dispatch/reports/README.md`。
>
> ⚠️ **不准把測試丟背景。** `ops/test.ps1` 實測淨執行 584 秒（9.7 分），
> 前景跑得完。用長 timeout 同步跑。

---

派工 BE-23：`CapturePayment` 誤設 `RefundedCurrency` ＋ EF model 補約束　·　docs/21-後端第九波派工書.md

package: BE-23
doc: docs/21-後端第九波派工書.md
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Core/Order.cs
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Infra/OrderingDbContext.cs
allow: tests/GreyGray.M1a.CheckoutOrdering.Tests/
allow: tests/GreyGray.M1a.Migrations.Tests/OrderingAppraisalMigrationTests.cs

---

派工 BE-24：`0003_channel_seams.sql` 的 `ledger.account` seed 非冪等　·　docs/21-後端第九波派工書.md

package: BE-24
doc: docs/21-後端第九波派工書.md
note: 修訂既有檔——`0003_channel_seams.sql` 已在 HEAD 裡，這一包刻意改動它本身
  （不是新增編號）。老闆已核准：專案還沒上線，沒有正式資料依賴舊版 `0003` 的行為，
  理由與範圍見 `docs/21` §5 BE-24。
allow: db/migrations/0003_channel_seams.sql
allow: tests/GreyGray.M1a.Migrations.Tests/M1aCoreMigrationTests.cs

---

<!--
★ 2026-08-30 已通過整合驗收並提交（後端 ad72f69），撤包。原文保留供追溯。

派工 BE-22：本機開發環境（D:\GreyGray）　·　docs/19-後端第八波派工書.md

package: BE-22
doc: docs/19-後端第八波派工書.md
allow: ops/
allow: .github/workflows/

派工 BE-21：帶回→待出貨接線 ＋ OrderLineId 改必填　·　docs/19-後端第八波派工書.md

package: BE-21
doc: docs/19-後端第八波派工書.md
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Infra/
allow: src/Modules/Procurement/GreyGray.Modules.Procurement.Contracts/
allow: src/Modules/Procurement/GreyGray.Modules.Procurement.Core/
allow: tests/

-->

---

<!--
派工 BE-20：部分買到（ADR-026）　·　docs/19-後端第八波派工書.md
⏸ 等 BE-21 通過整合驗收才啟用（兩包會撞 Procurement 與 Ordering）。

package: BE-20
doc: docs/19-後端第八波派工書.md
allow: src/Modules/Procurement/
allow: src/Modules/Ordering/
allow: src/Modules/Ledger/
allow: db/migrations/0015_
allow: tests/
-->

> **`tests/` 給了兩個包**：BE-22 不寫測試，BE-21 只准動 `tests/**/Ordering*`
> 與 `tests/**/Procurement*`。由總驗收逐檔看 diff 把關。
>
> `GreyGray.slnx` 與各 Host 的 `Program.cs` 不在任何 allow 裡——
> 那種一行的變更請整合者代為套用，或臨時開一筆 package 留痕。

---

## 已經通過、不再生效的（保留軌跡）

- **BE-22** 本機開發環境（`D:\GreyGray`）　·　2026-08-30 通過　·　`ad72f69`
- **BE-21** 帶回→待出貨接線 ＋ `OrderLineId` 改必填　·　2026-08-30 通過　·　`ad72f69`

BE-1～BE-8（M0）· BE-10（`2c05c99`）· BE-12（`dbec9cd`）·
第六波 BE-13／BE-9／BE-11／BE-14／BE-15（`de3a022`，157 條）·
**第七波 BE-17／BE-18／BE-19（`afd82f8`，171 條全綠）**
