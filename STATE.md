# 現況

**最後更新**：2026-08-28（後端第一波：BE-1 Outbox ＋ BE-4 可觀測性 ＋ BE-8 CI／部署）

## 一句話

後端第一波基礎設施已落地：Outbox、事件型別登錄、source-generated JSON、OTel 與安全部署工具都有實作與測試；**業務模組與 `/v1` API 端點仍未實作**。因此即時 OpenAPI gate 目前會正確 fail-fast，不可宣稱整條 CI 已全綠。

## 已完成

| 項目 | 狀態 |
|---|---|
| Solution（51 專案） | ✅ `dotnet build` 0 error 0 warning |
| 模組硬邊界 | ✅ **12 條**架構測試全綠（新加的兩條都注入違規驗證過會紅） |
| 線上格式與事件目錄 | ✅ **16 條**契約測試全綠（`tests/GreyGray.Contracts.Tests`，同樣注入驗證過） |
| Platform 基礎設施 | ✅ Outbox publisher／dispatcher、44 事件 registry、`PlatformDbContext`、OTel、clock／correlation context |
| Platform 整合測試 | ✅ **6 條**全綠；真 PostgreSQL 17 Testcontainers，含 rollback、雙 dispatcher、retry |
| 14 個模組的 Contracts（ID／DTO／介面／44 個事件） | ✅ 可編譯 |
| Shared.Kernel（Money、Currency、Result、IClock、Dimensions、**JSON**） | ✅ |
| Platform.Abstractions（事件、Outbox、Idempotency、Saga、**事件型別登錄、IAuditWriter**） | ✅ 介面 |
| DB schema 與 role 的 migration | ✅ **已在 PG 17.11 容器上實跑並驗證**（15 schema／17 role／4 表，owner 與權限都對）|
| **API 契約（`docs/05` ＋ 兩份 OpenAPI）** | ✅ v1.0 已凍結，24 ＋ 29 個端點 |
| **前端 workspace（Next.js ×2 ＋ token ＋ api-client）** | ✅ `pnpm build` 兩個 app 都過 |
| **設計 token（Soft Seoul ＋ Admin）** | ✅ 對比度實際量過，都達 AA |
| 三個 Host 的 `Program.cs` | ⚠️ OTel 與 `/health` 已接；仍沒有 `/v1` 端點或業務模組接線 |
| CI／Windows 部署工具 | ⚠️ build、tests、PS 5 self-test、versioned release、NSSM、watchdog 已完成；即時 OpenAPI gate 等 `/v1` 端點後才能綠 |

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

**後端**：BE-1 與 BE-4 已完成；BE-8 的工具與 fail-closed 門檻已完成，但其 live OpenAPI 驗收依賴 M0-6 的 `/v1` 端點。尚待 BE-2（inbox）、BE-3（idempotency ＋ Saga Timer）、BE-5（Identity／Catalog 組合根）、BE-7（audit／通路 schema）、BE-6（hello-world 垂直切片與 API 接線）。詳見 `docs/03-M0工作包.md` 與 `docs/07-後端派工書.md`。

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

### 即時 OpenAPI gate 現在必定紅
`ops/check-openapi.ps1` 會啟動剛建好的 Storefront／Admin Host 並抓 `/openapi/v1.json`。
兩個 Host 現在都只有 `/health`，所以 gate 會以 exit 1 fail-fast；這是防止空 schema 假綠，
不是工具故障。`docs/08` 把 BE-8 排在第一波，但 `docs/03` 的 M0-8 又依賴 M0-6，
完整驗收只能在 API 端點接線後完成。

### 多個 source-generated `JsonSerializerContext` 不可共用同一個 options instance
實測會拋出 `InvalidOperationException`：options 被第一個 context 封裝後不能再修改。
各 Contracts context 必須各呼叫 `GreyGrayJson.CreateOptions()`，設定來源仍只有 ADR-018 那一份。

### YAML flow mapping 內含冒號的文字必須加引號
Storefront frozen OpenAPI 曾有兩個 `1:1` description 未加引號，嚴格 YAML parser 會判定語法錯誤。
目前只修正 quoting，沒有改變 API shape。

### 前端套件版本還沒釘死
`frontend/*/package.json` 目前用 caret 範圍，靠 `pnpm-lock.yaml` 保證重現。
第一輪整合完成後應改成精確版本，與 `Directory.Packages.props` 的做法一致。

## 下一步

1. **後端第二波**：BE-2（inbox）與 BE-3（idempotency ＋ Saga Timer）平行
2. 接著 BE-5（Identity／Catalog 組合根）與 BE-7（audit／通路 schema），再由 BE-6 做 hello-world 垂直切片與 `/v1` API 接線
3. BE-6 完成後重跑 strict live OpenAPI gate，處理真實 schema drift，完成 M0-8 驗收
4. **前端**：FE-1（型別 ＋ mock）、FE-2（Soft Seoul 元件庫）、FE-6（後台殼）可平行
5. M-1 環境整備可以完全並行

## 待決策

**歷史會員與訂單要不要從租用平台遷移**。租用平台看起來已不給匯出，除非再付 NT$1,399。
這題要在 M0 定業務 schema 前有答案，因為匯出檔的實際欄位會反過來決定 Identity 的欄位設計。

**已經有連帶影響了**：`docs/api/openapi.storefront.yaml` 的 `RegisterRequest`
與 `docs/05-API契約.md` §2 都標了「欄位暫定」，前端 FE-5 也被要求把註冊表單的欄位
集中在一個 schema 檔。這題拖越久，要改的地方越多。

## 相關資源

- [後端藍圖 v1.0](https://claude.ai/code/artifact/9a61eb42-df39-418e-9789-38b8ffa8a6f2)（23 張圖，本 repo 的規格來源）
- [前台五套風格樣板](https://claude.ai/code/artifact/334496ce-48ac-4fd9-805f-19f64f66ac1b)（已選 Soft Seoul）
