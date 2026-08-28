# 🏁 HANDOFF_4：後端第二波冪等、Saga 與五服務部署

**日期：** 2026-08-28｜**版本：** v4.0｜**狀態：** BE-2／BE-3 完成；BE-8 前端服務缺口補齊；尚未部署 YC

## 📌 里程碑概述

這輪完成後端第二波的兩個可靠性核心：消費端 processed-message 去重，以及 API
idempotency／Saga Timer。同時補上第一波總驗收後新增的部署要求，讓正式服務清單從三個
.NET process 擴成三個 .NET ＋兩個 Next standalone process。

這仍不是整個後端完成。Identity／Catalog 組合根、通路 schema、hello-world 垂直切片與
`/v1` API 尚待第三、四波；因此 live OpenAPI gate 仍會誠實紅燈。

## 🚀 本輪完成

### BE-2：消費端冪等

- `platform.processed_message` 的 key 精確為 `(event_id, handler_name)`，同事件的不同 handler
  彼此獨立。
- decorator 自己擁有模組 DbContext transaction；processed marker、handler 副作用與
  `SaveChanges` 一起 commit 或一起 rollback。
- `INSERT ... ON CONFLICT DO NOTHING` 插入 0 筆時直接 ACK，不重做副作用。
- 已存在外層 transaction 時明確拒絕，避免 savepoint rollback 後 EF ChangeTracker 留下假狀態。
- 模組連線自行設定參數化、transaction-local tenant context，不依賴 Platform 另一條連線。

### BE-3：API Idempotency

- 完成四種 `IdempotencyOutcome`、24 小時 retention、同 key 不同 payload 拒絕與 abandon／expiry reclaim。
- response snapshot 以 JSON scalar 原樣保存；上層可在 envelope 內保留首次 HTTP status，
  `AlreadyCompleted` 不會被固定成 200。
- 並行 `TryBegin` 只有一個 Proceed。
- 因既有介面沒有 lease token，scoped store 使用資料庫 `created_at` 當 fencing token；reclaim
  會產生單調遞增 token，stale Complete 不能覆蓋新 lease。
- `TryBegin → Complete／Abandon` 必須使用同一個 scoped store instance。

### BE-3：Saga Timer

- 完成 schedule、cancel、cancel-all 與到期派送。
- dispatcher 使用固定 PostgreSQL session advisory lock `1002`，並在 finally 明確 unlock。
- 兩個 worker 同時掃描時只有一個能派送；handler 失敗會 rollback timer，鎖也會釋放，
  後續可安全 retry。
- timer tenant 進入 `CorrelationContext` scope，資料庫交易使用參數化
  `set_config('app.tenant_id', ..., true)`。

### BE-8：五服務與 Next standalone artifact

- service manifest：
  - `GreyGray-Storefront`：.NET，5000
  - `GreyGray-Admin`：.NET，5001
  - `GreyGray-Worker`：.NET，無 listener
  - `GreyGray-Web-Storefront`：Node，5002
  - `GreyGray-Web-Admin`：Node，5003
- 前端正式服務直接執行可信 `node.exe + server.js`，不經 npm／pnpm shim。
- Node／pnpm 缺失會明確 M-1 fail-fast；Node 最低版本為 20。
- Node process ownership 同時核對 executable path 與精確 release `server.js` command line；
  不會因為機器上另一個 node process 使用相同 port 就誤殺。
- CI 會建置兩個 Next standalone artifacts；部署仍保留舊 PID／path／StartTime／port 與
  HTTP health 的完整接手證據。

## 📈 驗證結果

| 驗證 | 結果 |
|---|---|
| Release solution build（51 projects） | ✅ 0 error／0 warning |
| Architecture.Tests | ✅ 12/12 |
| Contracts.Tests | ✅ 16/16 |
| Platform.Tests | ✅ 18/18；PostgreSQL 17 Testcontainers |
| 測試總數 | ✅ 46/46 |
| `ops/self-test.ps1 -Configuration Release` | ✅ AST、五服務、ownership、Node／pnpm fail-fast、migration、OpenAPI comparator 全綠 |
| 真實前端建置 | ✅ Node 24.15／pnpm 11.5；兩個 Next app build 成功 |
| artifact portability | ✅ Storefront／Admin 各解參考 20 個 link；兩份皆 0 reparse point |
| artifact runtime | ✅ 兩份均從 workspace 外直接啟動，`GET /` 回 HTTP 200；測試 PID 與 port 已清理 |
| live AddOpenApi gate | ⏸️ Hosts 尚無 `/v1`，以 exit 1 正確 fail-fast |

Platform 新增的 12 條永久測試覆蓋：同 handler 重送、不同 handler、兩 scope 並行競爭、
handler 已寫入後失敗 rollback／retry、snapshot 與狀態碼、不同 payload、並行 Begin、stale lease
fencing、abandon retry、Saga cancel no-op、雙 worker 單次派送與失敗後重試／釋鎖。

## 🔍 十項總驗收

| # | 項目 | 結果 |
|---|---|---|
| 1 | 檔案所有權 | ✅ BE-2／BE-3／BE-8 各守專屬路徑；root 只整合 Outbox tenant SQL 與歸檔文件 |
| 2 | `decimal`／`double` 金額 | ✅ 無新增金額浮點欄位；既有命中仍只有換算入口、匯率與註解 |
| 3 | `DateTimeOffset.UtcNow` | ✅ 實作命中只在 `SystemClock`；另一處為規則註解 |
| 4 | `new JsonSerializerOptions` | ✅ 只在 `GreyGrayJson` |
| 5 | migration role／owner | ✅ 本輪沒有新增 migration；沿用已驗證的 `0002_platform.sql` tables |
| 6 | dispatcher tenant | ✅ 三處均為參數化 `set_config(..., true)`，等價 `SET LOCAL` |
| 7 | 架構故障注入 | ✅ 第一波已驗證會紅；本輪未改任何模組 ProjectReference，12/12 保持全綠 |
| 8 | KnownEventTypes | ✅ 16 條契約測試含 44 事件逐條 SetEquals |
| 9 | live OpenAPI | ⏸️ 時序依賴 BE-6；維持 fail-closed，不製造空 schema 假綠 |
| 10 | 前端服務與 Node 缺失 | ✅ 5002／5003 進 manifest；Node 缺失永久負向測試明確失敗 |

判定仍沿用第一波裁決：第 9 項是文件已知的 BE-8 ↔ BE-6 時序依賴，不放寬 gate；其餘
實作與驗收通過，可以進第三波。不能宣稱整條 CI 已全綠。

## ⚠️ 本輪踩到的坑

1. **Next + pnpm 的 Windows standalone 不能直接複製**

   `.next/standalone` 含 file-type symlink 指向目錄；`Copy-Item` 與 `robocopy` 在一般權限下
   會 Access denied／EPERM。只展開 symlink 又會遺失 pnpm virtual-store realpath context，
   實跑時缺 `styled-jsx`。現在由 Node materializer 限制 link 不得逃出 artifact root，並將
   package dependency context 實體化；正式 artifact 不依賴 symlink 權限或 build workspace。

2. **Idempotency 沒有 fencing token 的介面會讓 stale request 覆蓋新結果**

   單純把 expired row 改回 `IN_FLIGHT` 不夠；舊 request 仍可能晚到並 Complete。既有介面不宜
   在第二波擴張，因此使用 DB `created_at` 作 scoped fencing token，Complete 必須同時命中 token。

3. **模組 DbContext 的 tenant state 不能借用 Platform 連線**

   processed-message decorator 的 marker 與模組副作用在模組 transaction；它必須在同一條
   connection 設定 transaction-local tenant。Outbox／processed-message／Saga 均統一用參數化
   `set_config(..., true)`，避免留下可被字串參數照抄的 SQL injection 形狀。

## 🎯 下一階段

1. 第三波平行：BE-5（只做 Identity／Catalog 組合根樣板）與 BE-7（audit／通路 schema）
2. 第四波：BE-6 hello-world 垂直切片與 `/v1` endpoints
3. BE-6 完成後重跑 strict live OpenAPI gate；通過才算 M0-8 完整驗收
4. M-1 正式機仍需安裝 Node 20+ 等環境；本輪沒有部署 YC

## 📚 接手入口

`CLAUDE.md` → `STATE.md` → `docs/00-decisions.md` → `docs/07-後端派工書.md`
→ `docs/03-M0工作包.md`。測試統一跑 `ops/test.ps1 -Configuration Release -ValidateOps`。

---
**簽署人：** Codex｜**存檔路徑：** `management/history/HANDOFF_4.md`
