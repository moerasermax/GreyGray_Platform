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
GG_PACKAGE=BE-9 codex
```

**B. lead 用 ai-cli fan out 子 agent** —— 在子 agent 的 prompt 裡寫一行 `GG_PACKAGE=BE-9`。
`UserPromptSubmit` 會認出它，把包別綁到那個 session_id 上，之後的閘門就照那一包判斷。
多個子 agent 同時跑不會互相蓋掉，因為標記檔名就是各自的 session_id。

> 為什麼 B 不能用環境變數：ai-cli 的 `run` 只吃 `workFolder`／`prompt`／`model`／
> `reasoning_effort`／`session_id`，**沒有 env 參數**；而且它的子行程是
> `env: process.env`（`src/core/process-service.ts`），繼承的是 MCP server 自己的環境。
> 一個 server 行程 spawn 所有子 agent，行程層級的環境變數本質上帶不了
> 「每個子 agent 不同」的值。所以綁定只能走 session_id。

**宣告不是可選的。** 沒宣告時 `allow` 是所有生效包的**聯集**——閘門仍擋得住「整波之外」，
但擋不住 BE-9 去寫 BE-11 的檔案，而那正是所有權表要防的事。
包名拼錯一律擋下（fail-closed）。一個 session 只認第一次宣告，之後想改包會被拒絕。

---

（目前沒有生效中的派工。BE-10 已於 2026-08-29 通過驗收（2c05c99）；BE-9 在寫碼前正確停工——派工缺三個上游接縫，改由 BE-12 先補，見 docs/13。）

<!--
派工 BE-9：M1b-3 帶回入庫與旅程成本　·　docs/13-後端第五波派工書.md

package: BE-9
doc: docs/13-後端第五波派工書.md
allow: src/Modules/Inventory/
allow: src/Modules/Ledger/
allow: db/migrations/0008_
allow: src/Hosts/GreyGray.Api.Admin/M1bInventoryEndpoints.cs
allow: tests/
-->

<!--
派工 BE-10：M-1 環境整備腳本化　·　docs/13-後端第五波派工書.md

package: BE-10
doc: docs/13-後端第五波派工書.md
allow: ops/
allow: .github/workflows/
-->

<!--
派工 BE-12：M1b-3 的三個上游接縫（BE-9 的前置）　·　docs/13-後端第五波派工書.md

package: BE-12
doc: docs/13-後端第五波派工書.md
allow: src/Modules/Procurement/
allow: src/Modules/Campaign/
allow: src/Modules/Ordering/
allow: db/migrations/0008_
allow: tests/
-->

<!--
派工 BE-11：M1b-2 缺貨補償與現場漲價詢問
🔴 docs/13 §5 的兩個決策沒有答案之前不准啟用這一段。

package: BE-11
doc: docs/13-後端第五波派工書.md
allow: src/Modules/Procurement/
allow: src/Modules/Notification/
allow: db/migrations/0009_
allow: src/Hosts/GreyGray.Api.Admin/M1bCompensationEndpoints.cs
allow: tests/
-->

> **`tests/` 給了兩個包**，因為兩邊都要加自己的測試專案。設了 `GG_PACKAGE` 也分不開
> （兩包的 `allow` 都寫 `tests/`）——BE-9 只准動 `tests/**/Inventory*`，
> BE-11 只准動 `tests/**/Procurement*`，這一條由總驗收第 1 條逐檔看 diff 把關。
>
> `GreyGray.slnx` 與各 Host 的 `Program.cs` **不在任何 allow 清單裡**，這是刻意的：
> `docs/13` §2 只授權「加一行」，那種一行的變更請整合者代為套用，
> 或由整合者臨時在這裡開一筆 package 留下軌跡。
