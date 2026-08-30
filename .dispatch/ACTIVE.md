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

**FE-12 已生效（2026-08-30）。** 配合後端第九波（`docs/21`，在後端樹）一起派。

| 包 | 主題 | 派工書 |
|---|---|---|
| FE-12 | 關掉 mock 對真後端跑一遍 | `docs/18` §5（開工前提已更正，環境已就緒） |

**啟動 prompt 在 `.dispatch/PROMPTS.md`**，Leader 用 ai-cli fan out。

> 開工前提已滿足：後端 BE-22 已於第八波通過整合驗收（開發機 D:\GreyGray
> 上 PG＋Garnet＋三個 Host 都起得來）。但那組環境目前是停著的——
> 第八波整合驗收為了解開 build 的檔案鎖用 stop-dev-environment.ps1 收掉了
> （資料保留，只是 stop，沒有 rm）。派這包之前，要先在後端樹跑
> ops\start-dev-hosts.ps1 -InstallRoot 'D:\GreyGray' -Configuration 'Debug'，
> 確認三個 /health 都回 200，FE-12 才真的開得了工。

> 已知會踩到、不是 FE-12 要修的：後端 BE-23／BE-24（docs/21，同一輪在
> 後端樹跑）修的是「結帳付款會炸 23514」與「migration 重放非冪等」兩個既有
> bug。建議兩包先跑完再開 FE-12，或至少確認 BE-23 已通過再走到前台付款那
> 一步——不然 FE-12 §5 第 3 點「前台走一條完整的：…→ 付款 →…」在第一次
> 結帳付款就會踩到那個已知洞。

> 自驗報告是檔案：`.dispatch/reports/<包名>.md`，三個標頭缺一不可，
> `audit-dispatch.sh` 第 ⑧ 項會擋。規格見 `.dispatch/reports/README.md`。
> 不准把工作丟背景後結束。

---

派工 FE-17：前台運費文案改從契約來　·　docs/20-前端第六波派工書.md

<!--
★ 2026-08-30 已通過整合驗收並提交（前端 50c314c），撤包。原文保留供追溯。

package: FE-17
doc: docs/20-前端第六波派工書.md
allow: frontend/apps/storefront/app/(checkout)/
-->

---

派工 FE-12：關掉 mock、對真後端跑一遍　·　docs/18-前端第五波派工書.md

package: FE-12
doc: docs/18-前端第五波派工書.md
allow: frontend/apps/admin/.env.local
allow: frontend/apps/storefront/.env.local

---

## 已經通過、不再生效的（保留軌跡）

- **FE-17** 前台運費文案改從契約來　·　2026-08-30 通過　·　`50c314c`

FE-1～FE-8（前兩波 ＋ 技術債收尾 `631e7bc`）· FE-9／FE-10／FE-11（第三波 `bbce5e1`）·
第四波 FE-13／FE-14／FE-15（`e8156e6`）·
**第五波 FE-16（`6165419`）——同一波的 FE-12 未完成，環境前提不成立**
