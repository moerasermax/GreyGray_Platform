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
GG_PACKAGE=BE-9 codex           # Codex
GG_PACKAGE=BE-9 claude          # Claude Code
```

**`GG_PACKAGE` 不是可選的。** 同時派兩包而沒設它時，
`allow` 清單會變成兩包的**聯集**——閘門仍擋得住「整波之外」，
但擋不住 BE-9 去寫 BE-11 的檔案，而那正是所有權表要防的事。
沒設的話 `SessionStart` 會明講這件事。包名拼錯則一律擋下（fail-closed）。

---

（BE-9／BE-10 已啟用，2026-08-29。BE-11 仍鎖著，等 docs/13 §5 兩個決策。）

派工 BE-9：M1b-3 帶回入庫與旅程成本　·　docs/13-後端第五波派工書.md

package: BE-9
doc: docs/13-後端第五波派工書.md
allow: src/Modules/Inventory/
allow: src/Modules/Ledger/
allow: db/migrations/0008_
allow: src/Hosts/GreyGray.Api.Admin/M1bInventoryEndpoints.cs
allow: tests/

派工 BE-10：M-1 環境整備腳本化　·　docs/13-後端第五波派工書.md

package: BE-10
doc: docs/13-後端第五波派工書.md
allow: ops/
allow: .github/workflows/

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
