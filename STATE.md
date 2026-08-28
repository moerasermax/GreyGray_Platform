# 現況

**最後更新**：2026-08-28（第二輪：邊界稽核 ＋ API 契約凍結 ＋ 前端骨架）

## 一句話

後端骨架、前端骨架、已凍結的 API 契約三者到位，**業務邏輯一行都還沒寫**。
前後端從這裡開始平行：後端走 `docs/07-後端派工書.md`，前端走 `docs/06-前端工作包.md`。

## 已完成

| 項目 | 狀態 |
|---|---|
| Solution 骨架（50 專案） | ✅ `dotnet build` 0 error 0 warning |
| 模組硬邊界 | ✅ **12 條**架構測試全綠（新加的兩條都注入違規驗證過會紅） |
| 線上格式與事件目錄 | ✅ **16 條**契約測試全綠（`tests/GreyGray.Contracts.Tests`，同樣注入驗證過） |
| 14 個模組的 Contracts（ID／DTO／介面／44 個事件） | ✅ 可編譯 |
| Shared.Kernel（Money、Currency、Result、IClock、Dimensions、**JSON**） | ✅ |
| Platform.Abstractions（事件、Outbox、Idempotency、Saga、**事件型別登錄、IAuditWriter**） | ✅ 介面 |
| DB schema 與 role 的 migration | ✅ **已在 PG 17.11 容器上實跑並驗證**（15 schema／17 role／4 表，owner 與權限都對）|
| **API 契約（`docs/05` ＋ 兩份 OpenAPI）** | ✅ v1.0 已凍結，24 ＋ 29 個端點 |
| **前端 workspace（Next.js ×2 ＋ token ＋ api-client）** | ✅ `pnpm build` 兩個 app 都過 |
| **設計 token（Soft Seoul ＋ Admin）** | ✅ 對比度實際量過，都達 AA |
| 三個 Host 的 `Program.cs` | ⚠️ 只有 `/health`，模組尚未接線 |

## 2026-08-28 第二輪：邊界稽核修掉的六類問題

派工前做了一次完整稽核，發現的都不是小事——每一條都會在平行開發時被放大。

| # | 問題 | 處置 |
|---|---|---|
| A1 | `outbox_message.aggregate_type/id` 是 NOT NULL，但 `PublishAsync` 拿不到值 | `IIntegrationEvent` 加兩個 abstract 成員，44 個事件全部填好，**編譯器強制** |
| A2 | `static abstract EventType` 只能正向，dispatcher 反查不回 CLR 型別 | 新增 `IIntegrationEventTypeRegistry` |
| A3 | 「outbox 同交易」與「每模組一個 DbContext、不 map 別人的表」互斥 | ADR-016：`platform` schema 是刻意的共用例外 |
| B | `ALTER DEFAULT PRIVILEGES FOR ROLE greygray_owner` 只對 owner 建的物件生效，`0002` 沒 `SET ROLE` → **所有模組 role 對所有表零權限** | 加 `SET ROLE` ＋ 結尾 owner 斷言。**已在真的 PG 17 上三種情境各跑一次驗證**（見下） |
| C | 5 條訂閱關係缺 `ProjectReference`，含 `Ledger → procurement.GoodsReceived`（DR 存貨/CR 現金，關鍵路徑） | 全部補上 |
| D | 沒有測試阻止業務模組依賴支撐模組；Contracts 可用 PackageReference 繞過 | 架構測試 10 → 12 條，兩條都注入驗證過 |
| E | 44 個事件的 JSON 形狀沒定案，outbox payload 一旦有資料就改不動 | ADR-018 ＋ `GreyGrayJson.Options` 為唯一來源 |
| F | Storefront（公開行程）參考全部 14 個 Infra | 砍掉 Notification 與 Reporting |

**連帶**：`IAuditWriter` 與 `AuditCategory` 從 `Audit.Contracts` 移到
`Platform.Abstractions.Audit`（ADR-017）——個資存取留痕是同步的，
留在 Audit.Contracts 會逼 `Identity.Core` 依賴支撐模組。

### B 的實測（2026-08-28，PostgreSQL 17.11 容器）

| 情境 | migration 回報 | 實際結果 |
|---|---|---|
| 修正前：無 `SET ROLE`、無斷言 | **成功** | 14 個模組 role 對 `outbox_message` **零權限**——靜默失敗 |
| 只忘 `SET ROLE`（斷言在）| **失敗並整個 rollback** | platform schema 一張表都沒建，錯誤訊息直指原因 |
| 修正後 | 成功 | 15 個 role 權限正確、4 張表 owner 都是 `greygray_owner`、無 BYPASSRLS |

第一列就是這條沒被抓到的話會發生的事：部署腳本一片綠，
然後服務啟動時報 `permission denied for table`，而訊息完全不指向真正的原因。

## 未完成

**後端**：M0-1 ～ M0-8，見 `docs/03-M0工作包.md` 與 `docs/07-後端派工書.md`。
`GreyGray.Platform` 目前只有 `OutboxMessage` 的 POCO 與 `IModuleRegistration` 介面，
**實作一行都還沒有**。

**前端**：FE-1 ～ FE-8，見 `docs/06-前端工作包.md`。
目前只有骨架與 token，兩個 app 各只有一頁佔位。

## 環境現況

| 項目 | 狀態 |
|---|---|
| 開發機 dotnet 10.0.301 / Node 24.15 / pnpm 11.5 | ✅ |
| 開發機 Docker Desktop 4.87（Testcontainers 用）| ✅ `postgres:17-alpine` 已預先拉好 |
| 開發機 PostgreSQL（原生安裝）| ❌ 沒有，也不需要——測試走容器 |
| 正式機 YC：dotnet / PostgreSQL / Valkey / cloudflared | ❌ 四項都還沒裝（M-1） |
| ngrok → cloudflared 遷移 | ❌ 未開始 |
| 有線網路、UPS、Defender 排除、專屬 Windows 帳號 | ❌ 全部未做（M-1） |

**M-1 環境整備一件都還沒做。** 它不擋現在的開發，但擋 M1 上線。
前端多了兩個 native process（:5002 storefront、:5003 admin），要一併登記進 prod-monitor。

## 已知問題

### `dotnet test` 在 SDK 10.0.301 上跑不起來
`dotnet test` 會走 VSTest 路徑並直接報錯。`dotnet.config` 的 `[dotnet.test:runner]` 與
`TestingPlatformDotnetTestSupport` 兩種 opt-in 都試過，都沒生效（2026-08-28 實測）。
**繞法**：`.\ops\test.ps1`，直接跑 xunit.v3 產出的執行檔。

### `Microsoft.OpenApi` pin 在 2.12.2，不要跳 3.x
`Microsoft.AspNetCore.OpenApi` 的 source generator 產的碼依賴 2.x 的 API 形狀。

### 前端套件版本還沒釘死
`frontend/*/package.json` 目前用 caret 範圍，靠 `pnpm-lock.yaml` 保證重現。
第一輪整合完成後應改成精確版本，與 `Directory.Packages.props` 的做法一致。

## 下一步

1. **後端**：把 `docs/08-Codex啟動prompt.md` 的第一則貼給 Codex（BE-1 / BE-4 / BE-8 三包平行）
2. **前端**：FE-1（型別 ＋ mock）、FE-2（Soft Seoul 元件庫）、FE-6（後台殼）三包平行
3. M-1 環境整備可以完全並行

## 待決策

**歷史會員與訂單要不要從租用平台遷移**。租用平台看起來已不給匯出，除非再付 NT$1,399。
這題要在 M0 定業務 schema 前有答案，因為匯出檔的實際欄位會反過來決定 Identity 的欄位設計。

**已經有連帶影響了**：`docs/api/openapi.storefront.yaml` 的 `RegisterRequest`
與 `docs/05-API契約.md` §2 都標了「欄位暫定」，前端 FE-5 也被要求把註冊表單的欄位
集中在一個 schema 檔。這題拖越久，要改的地方越多。

## 相關資源

- [後端藍圖 v1.0](https://claude.ai/code/artifact/9a61eb42-df39-418e-9789-38b8ffa8a6f2)（23 張圖，本 repo 的規格來源）
- [前台五套風格樣板](https://claude.ai/code/artifact/334496ce-48ac-4fd9-805f-19f64f66ac1b)（已選 Soft Seoul）
