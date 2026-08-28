# HANDOFF_6：後端第四波 M0 hello-world 本機驗收候選

**日期：** 2026-08-28｜**版本：** v6.0｜**狀態：** BE-6 本機程式完成；正式 M0 驗收未完成；未部署 YC

## 里程碑概述

第四波完成 `Identity → outbox → Worker → Notification` 的真實 PostgreSQL 17 垂直切片，
並把它鎖進永久 E2E。這一波沒有偽造 M1a 的手機／密碼／session 註冊，也沒有放寬
fail-closed OpenAPI gate。

目前可誠實稱為「M0 本機功能驗收候選」：程式路徑、交易、冪等、trace context、migration、
架構邊界與三個 Host 的停止後再啟動都已驗證；但 YC 尚未執行 NSSM＋BootTrigger 重開機，
也尚未把 trace 送進可查詢的 OTLP backend，因此不能宣稱完整 M0 或正式部署完成。

## 垂直切片

- Storefront 在 **Development only** 提供 `POST /v1/customers` M0 test hook；只接受
  `displayName`，且排除 OpenAPI。Production 永久測試確認回 404。
- `ICustomerProvisioning` 明確不是 M1a registration。它以同一個 `IdentityDbContext` 加入
  `iam.customer` 與 `CustomerRegistered` outbox，最後只做一次 `SaveChangesAsync`。
- Worker 使用 `greygray_platform` role 掃描 outbox；Notification handler 使用
  `greygray_notify` role，在同一 transaction 寫 `notify.notification` 與
  `platform.processed_message`。
- Worker 每批建立獨立 async scope；at-least-once 重送同一 event 後，notification 與
  processed marker 都維持一筆。
- Worker 同時接上獨立的 Saga Timer loop（advisory lock `1002`），並在 `RunAsync` 前 eager
  建立 event registry、outbox dispatcher 與 saga dispatcher；重複 event type／handler 等
  組態錯誤會中止啟動，不會被背景重試吞掉。

## Migration 0004

`db/migrations/0004_hello_world.sql` 在 transaction 內 `SET ROLE greygray_owner`，建立 M0 最小的
`iam.customer` 與 `notify.notification`。通知紀錄保留 W3C `trace_id` 與 handler span id，
不建立跨 schema foreign key。

永久 E2E 實際執行 `0001 → 0002 → 0003 → 0004 → 0004`，確認可重跑、兩張表 owner、
三條應用 role 連線與約束。另注入 `iam.owner_assertion_probe` 後重跑 `0004`，確實得到
`P0001` 且 migration rollback，證明 owner 斷言不是只存在於文字中。

## 永久 E2E 證據

`GreyGray.EndToEnd.Tests` 使用 Testcontainers PostgreSQL 17 與建置後的真實 Host executable：

1. 固定 `traceparent` 呼叫 Storefront，收到 201；customer 與 `CustomerRegistered` outbox 同時出現。
2. 啟動 Worker，notification、processed marker 與 outbox processed 狀態完成。
3. notification 的 trace id 與 API trace 相同，handler span id 非空。
4. 停 Worker、重排同一 outbox、再啟動 Worker，副作用仍只有一份。
5. Storefront、Admin 與 Worker 都做停止後再啟動；兩個 API health 恢復。
6. Production Storefront 對 M0 `/v1/customers` 回 404。

這只能證明 executable 可重啟，不等同 NSSM、S4U BootTrigger watchdog 或 Windows reboot 驗收。

## 架構負向驗證

暫時加入 `Identity.Core → Notification.Core` ProjectReference 後，Architecture.Tests 精準紅兩條：

- `Core 不得參考其他模組的 Core`
- `業務模組不得依賴支撐模組`

移除違規參考後恢復 14/14。Notification Infra 也納入「唯一 exported composition root」規則。

## 驗證結果

| 驗證 | 結果 |
|---|---|
| Release solution build（52 projects） | ✅ 0 error／0 warning |
| Architecture.Tests | ✅ 14/14；負向注入會紅 |
| Contracts.Tests | ✅ 16/16；44 events 未漂移 |
| EndToEnd.Tests | ✅ 1/1；真 PG 17＋真 Host processes |
| Platform.Tests | ✅ 19/19；真 PG 17 Testcontainers |
| 測試總數 | ✅ 50/50 |
| `ops/self-test.ps1 -Configuration Release` | ✅ 五服務、process ownership、Node／pnpm fail-fast、`0001..0004`、OpenAPI comparator 全綠 |
| live AddOpenApi gate | ⛔ 預期 fail-closed；Storefront／Admin 公開文件仍只有 `/health` |
| YC NSSM＋BootTrigger reboot | ⏸️ 未部署、未驗收；M-1／Node 尚未完成 |

## OpenAPI 時序矛盾與處置

M0-6 工作包指定 `/v1/customers`，但 v1.0 凍結公開契約從 M1a 的
`POST /v1/auth/register` 開始，後者還包含手機、密碼、session 與完整 idempotency 語意。

本波選擇保留兩者的真實邊界：M0 route 是 Development-only 且 OpenAPI-excluded；
不把它偽裝成 `/v1/auth/register`，也不一次假做 53 個 frozen endpoints。實跑
`ops/check-openapi.ps1 -Configuration Release -NoBuild` 後 Storefront／Admin 都因沒有公開 `/v1`
而 exit 1，這是目前的預期未完成，不是 gate 故障。

## 尚未完成，不能越線宣稱

1. 在 YC 完成 M-1，部署五服務，實際驗 NSSM auto-start、S4U BootTrigger watchdog、
   新 StartTime 接手與 Windows reboot。
2. 接上可查詢 OTLP receiver，以真實 exporter 證明 API → producer／consumer → handler 父子鏈。
3. M1a 實作 frozen `/v1/auth/register` 與其餘公開 endpoints 後，讓 strict live OpenAPI
   comparison 真正歸零。不可 static serve YAML 或為 M0 加豁免。

## 下一階段

1. 後端程式可轉入 M1a Identity 正式註冊；不要沿用 M0 provisioning 介面冒充認證。
2. M-1／正式機部署可與 M1a 平行；完成後補回本文件列出的 live restart／OTLP 證據。
3. 前端照 FE-1／FE-2／FE-6 第一波平行推進。

## 接手入口

`CLAUDE.md` → `STATE.md` → `management/history/HANDOFF_6.md` →
`docs/07-後端派工書.md` → `docs/03-M0工作包.md`。測試統一跑
`ops/test.ps1 -Configuration Release -ValidateOps`。

---
**簽署人：** Codex｜**存檔路徑：** `management/history/HANDOFF_6.md`
