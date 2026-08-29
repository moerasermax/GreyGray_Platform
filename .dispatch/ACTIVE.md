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

**第六波已生效（2026-08-30）。** 五包同時開，`GG_PACKAGE` 不是可選的。

| 包 | 主題 | 派工書 |
|---|---|---|
| BE-13 | M-1 環境收尾 🔴 最高優先，不寫 C# | `docs/16` |
| BE-9 | M1b-3b 帶回入庫與旅程成本 | `docs/13` |
| BE-11 | M1b-2 缺貨補償與現場漲價詢問 | `docs/13` ＋ ADR-023 |
| BE-14 | M1b-4 出貨、交運與簽收 | `docs/16` |
| BE-15 | `refundTo` 語意的契約異動（只改 description） | `docs/16` |

**啟動 prompt 在 `.dispatch/PROMPTS.md`**，直接複製貼上。

> ⚠️ **BE-9／BE-11／BE-14 三包都會跑 `ops/test.ps1`，要排開不要同時跑。**
> 兩包同時 build 會搶 obj/bin 與 NuGet 全域資料夾，
> 症狀是 `NuGet.targets(198,5)` 的「當檔案已存在時，無法建立該檔案」——那是競態不是你的程式壞了。

---

派工 BE-13：M-1 環境收尾　·　docs/16-後端第六波派工書.md

package: BE-13
doc: docs/16-後端第六波派工書.md
allow: ops/
allow: .github/workflows/

派工 BE-9：M1b-3b 帶回入庫與旅程成本　·　docs/13-後端第五波派工書.md

package: BE-9
doc: docs/13-後端第五波派工書.md
allow: src/Modules/Inventory/
allow: src/Modules/Ledger/
allow: db/migrations/0010_
allow: src/Hosts/GreyGray.Api.Admin/M1bInventoryEndpoints.cs
allow: tests/

派工 BE-11：M1b-2 缺貨補償與現場漲價詢問　·　docs/13-後端第五波派工書.md
（ADR-023 已拍板，決策阻塞解除）

package: BE-11
doc: docs/13-後端第五波派工書.md
allow: src/Modules/Procurement/
allow: src/Modules/Notification/
allow: db/migrations/0011_
allow: src/Hosts/GreyGray.Api.Admin/M1bCompensationEndpoints.cs
allow: tests/

派工 BE-14：M1b-4 出貨、交運與簽收　·　docs/16-後端第六波派工書.md

package: BE-14
doc: docs/16-後端第六波派工書.md
allow: src/Modules/Fulfillment/
allow: db/migrations/0012_
allow: src/Hosts/GreyGray.Api.Admin/M1bFulfillmentEndpoints.cs
allow: tests/

派工 BE-15：refundTo 語意的契約異動　·　docs/16-後端第六波派工書.md

package: BE-15
doc: docs/16-後端第六波派工書.md
allow: docs/api/

---

<!--
派工 BE-16：端對端煙霧腳本　·　docs/16-後端第六波派工書.md
⏸ 等 BE-9／BE-11／BE-13／BE-14 四包全部通過整合驗收才啟用。

package: BE-16
doc: docs/16-後端第六波派工書.md
allow: ops/e2e/
allow: tests/
-->

> **`tests/` 給了三個包**（BE-9／BE-11／BE-14），因為三邊都要加自己的測試專案。
> 設了 `GG_PACKAGE` 也分不開——BE-9 只准動 `tests/**/Inventory*`、
> BE-11 只准動 `tests/**/Procurement*`、BE-14 只准動 `tests/**/Fulfillment*`，
> 這一條由總驗收第 1 條逐檔看 diff 把關。
>
> **`ops/` 給了 BE-13**，`ops/e2e/` 留給 BE-16——BE-13 不要去建那個目錄。
>
> `GreyGray.slnx` 與各 Host 的 `Program.cs` **不在任何 allow 清單裡**，這是刻意的：
> `docs/16` §3 只授權「加一行」，那種一行的變更請整合者代為套用，
> 或由整合者臨時在這裡開一筆 package 留下軌跡。

---

## 已經通過、不再生效的（保留軌跡）

BE-1～BE-8（M0，各自的 commit 見 `GreyGray_PM/00-進度總表.md`）·
BE-10（M-1 腳本化，`2c05c99`）· BE-12（M1b-3a 三個上游接縫，`dbec9cd`）
