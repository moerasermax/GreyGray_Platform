# HANDOFF_5：後端第三波模組組合根與通路 schema

**日期：** 2026-08-28｜**版本：** v5.0｜**狀態：** BE-5／BE-7 完成；未部署 YC

## 里程碑概述

第三波完成兩個後續模組可複製的組合根樣板，並將 M0-7 五個日後難以回填的
通路擴充接縫落到 PostgreSQL schema。這一波不包含業務垂直切片；`/v1` endpoints、
Worker 實際消費與連續 trace 仍屬 BE-6，所以 M0 還沒有完成。

## BE-5：Identity／Catalog 組合根樣板

- Identity 與 Catalog Infra 各只對外暴露一個 `Add*Module` 組合根；模組描述與
  `DbContext` 都是 internal。
- 兩個 context 分別使用 `iam` 與 `catalog` 預設 schema，並且都呼叫
  `AddPlatformTables()`，保留業務寫入與 outbox 同交易的 ADR-016 例外。
- 連線字串 key 固定為 `ConnectionStrings:GreyGray_iam` 與
  `ConnectionStrings:GreyGray_catalog`。檢查延後到真正解析 context 時，因此尚未掛 DB 的
  Host 仍能啟動 `/health`；真正使用模組時會以含完整 key 的錯誤訊息 fail-fast。
- Storefront、Admin、Worker 三個 Host 都已註冊兩個模組。無連線字串時的實際啟動
  smoke test：兩個 `/health` 回 HTTP 200，Worker 保持存活；測試 PID 與 port 都已清理。
- 組合根不註冊全域 `IEventPublisher`。兩個模組同時存在時，全域註冊會變成
  last-registration-wins，把 publisher 綁到錯的 context。

## BE-7：五個通路擴充接縫

`db/migrations/0003_channel_seams.sql` 只建立 M0-7 要求的最小資料形狀，不提前做 M1 聚合：

1. `ordering.orders.source_channel` 預設 `OWN=0`，並由 constraint 拒絕其他通路。
2. `catalog.sku.id` 是非零 UUID primary key，不使用商品編號、條碼或外部 ID。
3. `ledger.account` 初始化 `1200 / ChannelReceivable / ASSET`，狀態為 inactive。
4. `inventory.lot.quantity_available` 是 DB generated column：
   `on_hand - reserved - channel_allocated`；M0 的通路配額預設且強制為 0。
5. `payment.payment.provider` 預留 `ExternalSettled=9`，數值與 Contracts 固定對齊。

Migration 在 transaction 內先 `SET ROLE greygray_owner`，結尾 `RESET ROLE` 後掃描全模組表 owner；
任何表不屬於 `greygray_owner` 就在 commit 前整包 rollback。部署自測的明確 migration 清單
也已加入 `0003`，不會只驗到前兩份。

## 驗證結果

| 驗證 | 結果 |
|---|---|
| Release solution build（51 projects） | ✅ 0 error／0 warning |
| Architecture.Tests | ✅ 14/14 |
| Contracts.Tests | ✅ 16/16 |
| Platform.Tests | ✅ 19/19；PostgreSQL 17 Testcontainers |
| 測試總數 | ✅ 49/49 |
| `ops/self-test.ps1 -Configuration Release` | ✅ 五服務、process ownership、Node／pnpm fail-fast、`0001..0003` 路徑、OpenAPI comparator 全綠 |
| 組合根故障注入 | ✅ 暫時暴露多餘 public type 後測試精確變紅；移除後 2/2 回綠 |
| migration 故障注入 | ✅ 放入非 owner 表後 `0003` 擲出 `P0001` 並 rollback |
| live AddOpenApi gate | ⏸️ 尚無 `/v1` endpoints，維持 fail-closed |

`ChannelSeamsMigrationTests` 實際執行 `0001 → 0002 → 0003 → 0003`，驗證可重跑、
五張表 owner、模組自有／跨 schema 權限、tenant default、五個業務約束與 owner 斷言。

## 十項總驗收自檢

| # | 項目 | 結果 |
|---|---|---|
| 1 | 檔案所有權 | ✅ BE-5 限 Identity／Catalog Infra；BE-7 限 `0003`；Host、測試、ops 與歸檔為整合必要變更 |
| 2 | `decimal`／`double` 金額 | ✅ 本波無新增金額浮點欄位 |
| 3 | `DateTimeOffset.UtcNow` | ✅ 本波無新增呼叫 |
| 4 | `new JsonSerializerOptions` | ✅ 本波無新增 |
| 5 | migration role／owner | ✅ 真 PG 17 權限、重跑、owner 與故障注入都通過 |
| 6 | dispatcher tenant | ✅ 本波未改派送路徑；仍是參數化 transaction-local `set_config(..., true)` |
| 7 | 架構故障注入 | ✅ 新 public-type 規則已實測會紅 |
| 8 | KnownEventTypes | ✅ 16 條契約測試全綠，44 事件目錄未漂移 |
| 9 | live OpenAPI | ⏸️ 依賴 BE-6；不用空 schema 製造假綠 |
| 10 | 前端服務與 Node 缺失 | ✅ 五服務 manifest 與負向測試保持全綠 |

## 下一階段

1. 第四波只做 BE-6 hello-world 端對端，這包通過才是 M0 完成。
2. 要實際驗證 `POST /v1/customers` 同交易寫入 `iam.customer` 與 outbox，Worker 去重消費後寫入 notification。
3. 驗收 API → outbox → handler 的連續 trace、架構負向測試、三個服務重開機自動起來。
4. 端點接線後重跑 strict live OpenAPI gate；差異歸零才完成 M0-8。
5. 正式機 M-1 環境整備仍全部未做；本波沒有 push 或部署 YC。

## 接手入口

`CLAUDE.md` → `STATE.md` → `docs/00-decisions.md` → `docs/07-後端派工書.md`
→ `docs/03-M0工作包.md`。測試統一跑 `ops/test.ps1 -Configuration Release -ValidateOps`。

---
**簽署人：** Codex｜**存檔路徑：** `management/history/HANDOFF_5.md`
