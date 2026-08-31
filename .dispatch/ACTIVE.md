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

## 生效中

派工 BE-31：支援部分買到（ADR-026）　·　docs/27-後端第十五波派工書.md

package: BE-31
doc: docs/27-後端第十五波派工書.md
allow: src/Modules/Procurement/GreyGray.Modules.Procurement.Core/PurchaseItemAggregate.cs
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Core/Order.cs
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Contracts/OrderingContracts.cs
allow: src/Hosts/GreyGray.Api.Admin/M1bShortfallRefundEndpoints.cs
allow: src/Hosts/GreyGray.Api.Admin/Program.cs
allow: src/Hosts/GreyGray.Api.Admin/OpenApiComponents.cs
allow: src/Hosts/GreyGray.Api.Admin/M1aEndpoints.cs
allow: docs/api/openapi.admin.yaml
allow: db/migrations/0015_
allow: tests/

**啟動 prompt 在 `.dispatch/PROMPTS.md`**。

---

<!--
★ 2026-08-31 已通過整合驗收並提交（後端 dc0ea1f），撤包。原文保留供追溯。

派工 BE-29：修正三處 `.ThenBy(x => x.Id.Value)` 導致的 admin 列表端點 500　·　docs/26-後端第十四波派工書.md

package: BE-29
doc: docs/26-後端第十四波派工書.md
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Infra/OrderingRepository.cs
allow: src/Modules/Fulfillment/GreyGray.Modules.Fulfillment.Infra/FulfillmentRepository.cs
allow: src/Modules/Procurement/GreyGray.Modules.Procurement.Infra/ProcurementRepository.cs
allow: tests/GreyGray.M1a.CheckoutOrdering.Tests/
allow: tests/GreyGray.M1b.Fulfillment.Tests/
allow: tests/GreyGray.M1b.Procurement.Tests/

派工 BE-30：拿掉兩處過期守衛，讓已付款訂單的「原路退款」真的打得到　·　docs/26-後端第十四波派工書.md

package: BE-30
doc: docs/26-後端第十四波派工書.md
allow: src/Hosts/GreyGray.Api.Admin/M1aEndpoints.cs
allow: tests/GreyGray.M1a.CheckoutOrdering.Tests/AdminCancelLineEndpointTests.cs
-->

---

<!--
★ 2026-08-31 已通過整合驗收並提交（後端 0c4f83e），撤包。原文保留供追溯。

派工 BE-28：storefront Host 補上 CORS（比照 admin Host 的 ADR-021 模式）　·　docs/25-後端第十三波派工書.md

package: BE-28
doc: docs/25-後端第十三波派工書.md
allow: src/Hosts/GreyGray.Api.Storefront/Program.cs
allow: tests/
-->

---

<!--
★ 2026-08-30 已通過整合驗收並提交（後端 c9d3646），撤包。原文保留供追溯。

派工 BE-27：修 Ordering ↔ Fulfillment 循環相依，解除 admin BFF 三組端點永久掛住　·　docs/24-後端第十二波派工書.md

package: BE-27
doc: docs/24-後端第十二波派工書.md
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Infra/ModuleRegistration.cs
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Core/OrderingApplicationService.cs
allow: src/Modules/Fulfillment/GreyGray.Modules.Fulfillment.Infra/ModuleRegistration.cs
allow: src/Modules/Fulfillment/GreyGray.Modules.Fulfillment.Core/FulfillmentApplicationService.cs
allow: tests/GreyGray.M1b.Fulfillment.Tests/
allow: tests/GreyGray.M1a.CheckoutOrdering.Tests/
-->

---

<!--
★ 2026-08-30 已通過整合驗收並提交（後端 5950a10），撤包。原文保留供追溯。

派工 BE-26：員工帳號 bootstrap 工具 ＋ 開發環境最小種子資料　·　docs/23-後端第十一波派工書.md

package: BE-26
doc: docs/23-後端第十一波派工書.md
allow: src/Tools/
allow: GreyGray.slnx
allow: tests/GreyGray.Architecture.Tests/ModuleBoundaryTests.cs
allow: ops/seed/
allow: ops/seed-dev-staff.ps1
-->

---

<!--
★ 2026-08-30 已通過整合驗收並提交（後端 bd616ea），撤包。原文保留供追溯。

派工 BE-25：開發環境與正式機的機密投遞機制　·　docs/22-後端第十波派工書.md

package: BE-25
doc: docs/22-後端第十波派工書.md
allow: ops/lib/Secrets.ps1
allow: ops/install-dev-environment.ps1
allow: ops/start-dev-hosts.ps1
allow: ops/deploy.ps1
-->

---

<!--
★ 2026-08-30 已通過整合驗收並提交（後端 22061a4），撤包。原文保留供追溯。

派工 BE-23：`CapturePayment` 誤設 `RefundedCurrency` ＋ EF model 補約束　·　docs/21-後端第九波派工書.md

package: BE-23
doc: docs/21-後端第九波派工書.md
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Core/Order.cs
allow: src/Modules/Ordering/GreyGray.Modules.Ordering.Infra/OrderingDbContext.cs
allow: tests/GreyGray.M1a.CheckoutOrdering.Tests/
allow: tests/GreyGray.M1a.Migrations.Tests/OrderingAppraisalMigrationTests.cs

派工 BE-24：`0003_channel_seams.sql` 的 `ledger.account` seed 非冪等　·　docs/21-後端第九波派工書.md

package: BE-24
doc: docs/21-後端第九波派工書.md
note: 修訂既有檔——`0003_channel_seams.sql` 已在 HEAD 裡，這一包刻意改動它本身
  （不是新增編號）。老闆已核准：專案還沒上線，沒有正式資料依賴舊版 `0003` 的行為，
  理由與範圍見 `docs/21` §5 BE-24。
allow: db/migrations/0003_channel_seams.sql
allow: tests/GreyGray.M1a.Migrations.Tests/M1aCoreMigrationTests.cs

-->

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
★ 從未啟用，被下面的 BE-31（docs/27）取代。原文保留供追溯。

派工 BE-20：部分買到（ADR-026）　·　docs/19-後端第八波派工書.md
⏸ 等 BE-21 通過整合驗收才啟用（兩包會撞 Procurement 與 Ordering）。
BE-21 通過之後這一包一直沒有真的排進派工，範圍等 2026-08-31 由 Leader
重新設計成 BE-31（docs/27-後端第十五波派工書.md），不再用這份舊的粗略範圍。

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

- **BE-30** 拿掉兩處過期守衛，讓已付款訂單的「原路退款」真的打得到　·　2026-08-31 通過　·　`dc0ea1f`
- **BE-29** 修正三處 `.ThenBy(x => x.Id.Value)` 導致的 admin 列表端點 500　·　2026-08-31 通過　·　`dc0ea1f`
- **BE-28** storefront Host 補上 CORS（比照 admin Host 的 ADR-021 模式）　·　2026-08-31 通過　·　`0c4f83e`
- **BE-27** 修 Ordering ↔ Fulfillment 循環相依，解除 admin BFF 三組端點永久掛住　·　2026-08-30 通過　·　`c9d3646`
- **BE-26** 員工帳號 bootstrap 工具 ＋ 開發環境最小種子資料　·　2026-08-30 通過　·　`5950a10`
- **BE-25** 開發環境與正式機的機密投遞機制　·　2026-08-30 通過　·　`bd616ea`
- **BE-24** `0003_channel_seams.sql` 的 `ledger.account` seed 非冪等　·　2026-08-30 通過　·　`22061a4`
- **BE-23** `CapturePayment` 誤設 `RefundedCurrency` ＋ EF model 補約束　·　2026-08-30 通過　·　`22061a4`
- **BE-22** 本機開發環境（`D:\GreyGray`）　·　2026-08-30 通過　·　`ad72f69`
- **BE-21** 帶回→待出貨接線 ＋ `OrderLineId` 改必填　·　2026-08-30 通過　·　`ad72f69`

BE-1～BE-8（M0）· BE-10（`2c05c99`）· BE-12（`dbec9cd`）·
第六波 BE-13／BE-9／BE-11／BE-14／BE-15（`de3a022`，157 條）·
**第七波 BE-17／BE-18／BE-19（`afd82f8`，171 條全綠）**
