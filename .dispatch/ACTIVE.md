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

**第八波已生效（2026-08-30）。** 兩包同時開，另兩包排在後面。

| 包 | 主題 | 派工書 |
|---|---|---|
| BE-22 | **本機開發環境（裝在 `D:\GreyGray`，不壓 C 槽）** 🔴 解 FE-12 的阻塞 | `docs/19` |
| BE-21 | 帶回→待出貨接線 ＋ `OrderLineId` 改必填 | `docs/19` |
| FE-17 | 前台運費文案改從契約來（拿掉寫死的 `NT$60`／`NT$120`） | `docs/20`（前端樹） |
| FE-12 | 關掉 mock 對真後端跑一遍 ⏸ **等 BE-22** | `docs/18`（前端樹） |
| BE-20 | 部分買到（ADR-026）⏸ **等 BE-21** | `docs/19` |

**啟動 prompt 在 `.dispatch/PROMPTS.md`**，Leader 用 ai-cli fan out。

> ⚠️ **BE-20 為什麼不能跟 BE-21 平行**：BE-20 放寬部分買到幾乎必然要改
> `ProcurementContracts`（帶短缺數量），而那正是 BE-21 要動的檔；
> BE-21 把 `GoodsReceived.OrderLineId` 改必填也會逼所有建構點跟著改。
> 檔案層級看起來不重疊，模組層級會撞。

> ⚠️ **自驗報告是檔案，不是對話。** 每包要寫 `.dispatch/reports/<包名>.md`，
> 三個固定標頭缺一不可，`audit-dispatch.sh` 第 ⑧ 項會擋。
> 規格見 `.dispatch/reports/README.md`。
>
> ⚠️ **不准把測試丟背景。** `ops/test.ps1` 實測淨執行 584 秒（9.7 分），
> 前景跑得完。用長 timeout 同步跑。

---

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

BE-1～BE-8（M0）· BE-10（`2c05c99`）· BE-12（`dbec9cd`）·
第六波 BE-13／BE-9／BE-11／BE-14／BE-15（`de3a022`，157 條）·
**第七波 BE-17／BE-18／BE-19（`afd82f8`，171 條全綠）**
