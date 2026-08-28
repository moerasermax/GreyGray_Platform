# 🏁 HANDOFF_1: M0 骨架落地 ＋ 專案定名 GreyGray Platform

**日期:** 2026-08-28 | **版本:** v1.0 | **狀態:** 骨架可編譯、架構測試會擋違規、業務邏輯未開始

## 📌 里程碑概述

專案從「只活在兩個 Claude artifact 裡的藍圖」變成「可編譯、有守門機制的 repo」。
系統結構切出來了，14 個模組的介面以**可編譯的 C# 契約**落地（不是圖，是編譯器會檢查的東西）。
同日定名 **GreyGray Platform**，舊名 daigou 全面汰換。

業務邏輯一行都還沒寫，這是刻意的——M0 的定義是地基，不是功能。

## 🚀 核心戰果

1. **Solution 骨架（49 專案）**
   - `Shared.Kernel` / `Platform.Abstractions` / `Platform` / 14 模組 ×(Contracts,Core,Infra) /
     3 個 Host / `Architecture.Tests`
   - `dotnet build GreyGray.slnx` → **0 error 0 warning**

2. **14 個模組的完整 Contracts**
   - 強型別 ID、DTO、同步介面、**43 個整合事件**，全部帶繁中註解說明「為什麼是這樣」
   - `Money` 用 `long` 最小單位；`AllocateByWeights` 保證分帳合計等於原金額

3. **硬邊界真的被強制（10 條架構測試）**
   - 斷言的是 `.csproj` 的 `ProjectReference`，**不是**編譯後的組件參考
   - 已實測會擋下違規（見下方「踩到的坑」）

4. **資料庫地基**
   - `0001_schemas_and_roles.sql`：15 個 schema、15 個 role、audit 只給 INSERT/SELECT
   - `0002_platform.sql`：outbox / processed_message / idempotency_key / saga_timer
   - **兩份都還沒在任何資料庫上執行過**

5. **交付給 Codex 的工作包**
   - `docs/03-M0工作包.md`：M0-1 ～ M0-8，每包有產出、做法要點、驗收、明確的不要做什麼
   - `docs/04-Codex啟動指示.md`：貼上即用的 prompt，設計成**一次只給一個工作包**

6. **改名 GreyGray Platform（ADR-015）**
   - 目錄、方案檔、組件與命名空間、Postgres 角色、連線字串鍵、知識庫命名空間全部換過
   - 121 個檔案異動，改名後**重跑違規注入驗證**確認守門機制沒被弄壞

## 📈 技術指標

- **專案數**: 49 | **版控檔案**: 98
- **編譯狀態**: ✅ 0 Error / 0 Warning
- **架構測試**: ✅ 10 / 10（且注入違規會紅）
- **ADR**: 15 條已定案，1 題待決
- **git**: 5 commits，工作區乾淨

## ⚠️ 踩到的坑（這段最有價值）

1. **架構測試的空跑陷阱** —— 第一版用 `Assembly.GetReferencedAssemblies()` 寫，
   故意注入 `Ordering.Core → Ledger.Core` 之後**8/8 仍然全綠**。
   原因：編譯器會把「csproj 有宣告、程式碼沒用到」的參考從組件 manifest 裡裁掉，
   而此時所有 Core 都是空的。NetArchTest 的型別規則有同樣問題。
   **修法**：解析 `.csproj`。**教訓**：任何斷言「某件事不存在」的測試都要注入違規驗過一次。

2. **`dotnet test` 在 SDK 10.0.301 上跑不動 xunit.v3** ——
   `dotnet.config` 與 `TestingPlatformDotnetTestSupport` 兩種 opt-in 都試過，都沒生效。
   繞法是直接跑 xunit 產出的執行檔，包成 `ops/test.ps1`。**不要用 `dotnet test`。**

3. **`Microsoft.OpenApi` 不能跳 3.x** —— 釘在 2.12.2。
   `Microsoft.AspNetCore.OpenApi` 的 source generator 產的碼依賴 2.x 的 API 形狀。

4. **`planner_project_register` 寫錯地方** —— 它把 `.planner-id` 寫進 MCP 的當下工作目錄
   （`D:\WorkSpace`），不是 repo。做法是註冊完再把檔案搬進 repo。
   `.planner-id` 要進版控，否則 fresh clone 會生出第二個 project 條目。

## 🎯 下一階段規劃 (NextWork)

1. **[M0-1] Platform Outbox 實作** —— 最優先。沒有它模組之間無法溝通，所有事情都卡在那。
2. **[M0-2 ～ M0-4]** 消費端冪等、Idempotency 與 Saga Timer、OTel 接線
3. **[M-1] 環境整備七件事** —— 可完全並行，跟寫程式互不相干，但**擋 M1 上線**
4. **[待決策]** 歷史會員與訂單要不要從租用平台遷移（NT$1,399 那題）

## 📚 開工前必讀

`README.md`（四條硬規則）→ `STATE.md`（現況與已知問題）→ `docs/00-decisions.md`（15 條 ADR，
**已決定的不要重新討論**）→ `docs/01-系統結構.md` → `docs/03-M0工作包.md`

規格來源是兩個 artifact，不在 repo 裡：
[後端藍圖 v1.0](https://claude.ai/code/artifact/9a61eb42-df39-418e-9789-38b8ffa8a6f2)（23 張圖）、
[前台五套樣板](https://claude.ai/code/artifact/334496ce-48ac-4fd9-805f-19f64f66ac1b)（已選 Soft Seoul）。

---
**簽署人:** Claude Opus 5 | **存檔路徑:** management/history/HANDOFF_1.md
