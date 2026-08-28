# 現況

**最後更新**：2026-08-28

## 一句話

骨架完成並可編譯：48 個專案、14 個模組的完整契約、10 條架構測試全綠（且實測會擋下違規）。
業務邏輯一行都還沒寫——那是 M0-1 之後的事，工作包在 `docs/03-M0工作包.md`。

## 已完成

| 項目 | 狀態 |
|---|---|
| Solution 骨架（48 專案） | ✅ `dotnet build` 0 error 0 warning |
| 模組硬邊界（assembly 分離） | ✅ 10 條架構測試全綠（且實測會擋下違規） |
| 14 個模組的 Contracts（ID／DTO／介面／事件） | ✅ 可編譯 |
| Shared.Kernel（Money、Currency、Result、IClock、Dimensions） | ✅ |
| Platform.Abstractions（事件、Outbox、Idempotency、Saga Timer 介面） | ✅ |
| DB schema 與 role 的 migration | ✅ SQL 已寫，**尚未在任何資料庫上執行過** |
| platform 三張表的 migration | ✅ SQL 已寫，**尚未執行** |
| 三個 Host 的 `Program.cs` | ⚠️ 只有 `/health`，模組尚未接線 |

## 未完成（M0 剩下的）

見 `docs/03-M0工作包.md` 的 M0-1 到 M0-8。**Platform 的實作一行都還沒有**——
`Daigou.Platform` 目前只有 `OutboxMessage` 的 POCO 與 `IModuleRegistration` 介面。

## 環境現況

| 項目 | 狀態 |
|---|---|
| 開發機 dotnet | ✅ 10.0.301 已裝 |
| 開發機 PostgreSQL | ❓ 未確認 |
| 正式機 YC：dotnet / PostgreSQL / Valkey / cloudflared | ❌ 四項都還沒裝（M-1） |
| ngrok → cloudflared 遷移 | ❌ 未開始（另有 planner 提醒，截止 8/30 12:00） |
| 有線網路、UPS、Defender 排除、專屬 Windows 帳號 | ❌ 全部未做（M-1） |

**M-1 環境整備一件都還沒做。** 它不擋現在的開發（骨架與業務邏輯都能在開發機上寫完），
但擋 M1 上線。

## 已知問題

### `dotnet test` 在 SDK 10.0.301 上跑不起來
`dotnet test` 會走 VSTest 路徑並直接報錯
（`Microsoft.Testing.Platform.MSBuild.targets(320,5)`）。
`dotnet.config` 的 `[dotnet.test:runner]` 與 `TestingPlatformDotnetTestSupport` 兩種 opt-in
在這個 SDK 版本上都試過，都沒生效（2026-08-28 實測）。

**繞法**：`.\ops\test.ps1`，直接跑 xunit.v3 產出的執行檔。結果一樣。
等 SDK 升上去之後回頭再試一次 `dotnet test`，能通就把腳本簡化掉。

### NuGet 版本是 2026-08-28 對照 nuget.org 的當日最新
`Directory.Build.props` 把 NuGetAudit 升成 error，所以有已知弱點的套件會讓 build 直接失敗。
`Microsoft.OpenApi` 特別註記：pin 在 2.12.2，**不要跳到 3.x**——
`Microsoft.AspNetCore.OpenApi` 的 source generator 產的程式碼依賴 2.x 的 API 形狀，
跳過去會 build fail。

## 下一步

1. **M0-1 Outbox 實作**（沒有它，其他模組沒辦法互相溝通，所有事情都卡在這）
2. M0-2、M0-3、M0-4 可以與 M0-1 並行推進的部分不多，建議照順序
3. M-1 環境整備可以完全並行——它跟寫程式互不相干

## 待決策

**歷史會員與訂單要不要從租用平台遷移**（`docs/00-decisions.md` 末段）。
租用平台看起來已不給匯出，除非再付 NT$1,399。這題要在 M0 定業務 schema 前有答案，
因為匯出檔的實際欄位會反過來決定 Identity 的欄位設計。

## 相關資源

- [後端藍圖 v1.0](https://claude.ai/code/artifact/9a61eb42-df39-418e-9789-38b8ffa8a6f2)（23 張圖，本 repo 的規格來源）
- [前台五套風格樣板](https://claude.ai/code/artifact/334496ce-48ac-4fd9-805f-19f64f66ac1b)（已選 Soft Seoul）
- planner 提醒 `6a903636d5aa101443055aa9`：代購平台週日前動工
