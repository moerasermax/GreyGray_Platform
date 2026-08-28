# 🏁 HANDOFF_3：後端第一波基礎設施

**日期：** 2026-08-28｜**版本：** v3.0｜**狀態：** BE-1／BE-4／BE-8 實作完成；業務模組與 `/v1` API 尚未開始

## 📌 里程碑概述

這一輪把 HANDOFF_2 留下的後端骨架，往前推到第一批可執行、可測試、可部署的
Platform 基礎設施：Outbox 與事件還原、W3C trace、CI，以及 Windows 原生部署守門。

完成的是「後端地基第一波」，不是整個後端。14 個業務模組仍沒有業務邏輯，
Storefront／Admin 仍只有 `/health`，尚未提供 frozen API 契約中的 `/v1` 端點。

## 🚀 本輪完成

### BE-1：Outbox ＋ 事件型別登錄

- `OutboxEventPublisher` 與業務寫入共用同一個 DbContext transaction；publisher 只 `Add`，不偷做 `SaveChanges`
- `OutboxDispatcher` 使用 PostgreSQL `FOR UPDATE SKIP LOCKED`，並以
  `SET LOCAL app.tenant_id` 設定租戶 context
- dispatcher 支援 retry、attempt counter、錯誤保留與成功後 `processed_at`
- `EventTypeRegistry` 在啟動時掃描 Contracts assembly，44 個 EventType 必須精確且唯一
- 14 個 Contracts assembly 各自提供 source-generated `IntegrationEventJsonContext`
- runtime payload serialization／deserialization 使用 generated `JsonTypeInfo`，不退回 reflection
- `PlatformDbContext` 與 `AddPlatformTables()` 已落地，並預留 BE-2／BE-3 的 partial hook

### BE-4：OTel ＋ IClock ＋ ICorrelationContext

- API Hosts：ASP.NET Core instrumentation、HttpClient instrumentation、OTLP exporter
- Worker：HttpClient instrumentation、OTLP exporter
- API response 回傳 W3C `traceparent`
- `correlation_id` = `Activity.TraceId`（32 hex）
- `causation_id` = `Activity.SpanId`（16 hex）
- Worker correlation override 使用 `AsyncLocal` scope，避免並行訊息互相污染
- `SystemClock` 是 repo 唯一直接呼叫 `DateTimeOffset.UtcNow` 的實作

### BE-8：CI ＋ Windows 原生部署

- GitHub Actions：Ubuntu build／完整 Testcontainers tests；Windows build／PS 5 self-test／OpenAPI gate
- win-x64 self-contained、versioned release、NSSM 三服務、log 與 restart policy
- 部署會保存舊 PID／StartTime／path，確認舊 process 與 port 真正釋放後才接手
- 新 process 必須來自本次 release 且 StartTime 晚於 restart boundary；HTTP 200 本身不算證據
- migration 密碼只傳給 `psql` child 的 `PGPASSWORD`，不寫入 NSSM、檔案或 argv
- BootTrigger ＋每五分鐘 watchdog；process path 不在 install root 時拒絕強殺
- strict OpenAPI gate 會啟動本次 build 的 Hosts，抓 `AddOpenApi()` 實際產物再做語意比對

## 📈 驗證結果

| 驗證 | 結果 |
|---|---|
| Release solution build（51 projects） | ✅ 0 error／0 warning |
| Architecture.Tests | ✅ 12/12 |
| Contracts.Tests | ✅ 16/16 |
| Platform.Tests | ✅ 6/6；PostgreSQL 17 Testcontainers |
| 測試總數 | ✅ 34/34 |
| ops PowerShell AST／manifest／process takeover／deploy／migration self-test | ✅ 全部通過 |
| OpenAPI comparator 自測 | ✅ 同契約通過；注入 `info.version` drift 確實紅燈 |
| live AddOpenApi gate | ⏸️ 兩個 Host 尚無 `/v1`，以 exit 1 正確 fail-fast |

Platform 的 6 條測試覆蓋：44 事件精確登錄、重複 EventType fail-fast、source-generated
JSON roundtrip、業務寫入與 outbox rollback、兩個 dispatcher 競爭 100 則訊息仍只處理一次、
handler 失敗後 attempts／retry／processed 狀態正確。

## 🔍 九項總驗收

| # | 項目 | 結果 |
|---|---|---|
| 1 | 檔案所有權 | ✅ 三包路徑隔離；Contracts contexts、Worker csproj、frozen YAML quoting 由 root 審核整合 |
| 2 | `decimal`／`double` 金額 | ✅ 無新增金額欄位；既有 `FxSnapshot.Rate` 與 `Money.OfMajor(decimal)` 不是儲存金額 |
| 3 | `DateTimeOffset.UtcNow` | ✅ 只在 `SystemClock` 實作 |
| 4 | `new JsonSerializerOptions` | ✅ 只在 `GreyGrayJson` |
| 5 | 新 migration 的 role／owner 斷言 | ✅ 本輪沒有新增 migration |
| 6 | dispatcher tenant 設定 | ✅ 使用 `SET LOCAL`，不是 session-wide `SET` |
| 7 | 架構測試故障注入 | ✅ 注入 `Ordering.Core → Ledger.Core` 後紅燈；撤回後 12/12 |
| 8 | KnownEventTypes 與事件目錄 | ✅ 永久測試逐條比對 44 個事件 |
| 9 | live AddOpenApi 與 frozen YAML | ⏸️ 尚無 `/v1` endpoint，gate 正確拒絕空 schema |

第 9 項揭露既有文件的排程循環：`docs/08` 把 BE-8 排在第一波，但 `docs/03` 的
M0-8 live 驗收依賴 M0-6 endpoints；M0-6 又排在第二、三波之後。不能為了假綠而放寬 gate，
也不能在第一波偷做 BE-6。裁定是保留 fail-closed gate，繼續第二、三波；BE-6 接線後完成
live schema drift 驗收。

## ⚠️ 本輪踩到的坑

1. **多個 `JsonSerializerContext` 不能共用同一個 `JsonSerializerOptions` instance**

   第一個 context 會封裝 options；建立第二個時會拋 `InvalidOperationException`。
   解法是每個 Contracts context 各呼叫 `GreyGrayJson.CreateOptions()`，設定來源仍只有 ADR-018
   一份。不要以 Platform 直接參考 14 個 Contracts，也不要退回 reflection serialization。

2. **frozen YAML 自己也可能是壞的**

   Storefront OpenAPI 有兩個 flow mapping description 含未加引號的 `1:1`，嚴格 parser 直接失敗。
   本輪只補 quoting，沒有改 API shape。修正後 frozen 自比對為 storefront 24 paths、admin 32 paths。

3. **Windows 上 service stop、HTTP 200 都不足以證明新版本接手**

   舊 child process 可能還活著並回應 health。驗收必須同時核對 executable path、StartTime、
   舊 PID token 與 port holder；只允許終止已證明位於 install root 的程序。

4. **`dotnet test` 在 SDK 10.0.301／xUnit v3 會誤走 VSTest**

   正式入口仍是 `ops/test.ps1`，它直接執行 xUnit v3 產物，並比對測試專案數與實際執行數，
   防止新增測試專案後靜默漏跑。

## 🎯 下一階段

1. 第二波平行：BE-2 消費端冪等、BE-3 API idempotency ＋ Saga Timer
2. 第三波：BE-5 Identity＋Catalog 組合根、BE-7 audit／通路 schema
3. 第四波：BE-6 hello-world 垂直切片與 `/v1` endpoints
4. BE-6 完成後重跑 strict live OpenAPI gate；通過才算 M0-8 完整驗收

前端與 M-1 正式機整備仍可平行；歷史會員與訂單是否遷移仍是未決業務決策。

## 📚 接手入口

`CLAUDE.md` → `STATE.md` → `docs/00-decisions.md` → `docs/07-後端派工書.md`
→ `docs/03-M0工作包.md`。測試統一跑 `ops/test.ps1 -Configuration Release -ValidateOps`。

---
**簽署人：** Codex｜**存檔路徑：** `management/history/HANDOFF_3.md`
