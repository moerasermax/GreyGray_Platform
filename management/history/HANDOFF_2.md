# 🏁 HANDOFF_2: 邊界稽核 ＋ API 契約凍結 ＋ 前端骨架

**日期:** 2026-08-28 | **版本:** v2.0 | **狀態:** 前後端可平行開工，業務邏輯仍未開始

## 📌 里程碑概述

上一輪（HANDOFF_1）交出的是「可編譯、有守門機制的 repo」。這一輪要決定的是
**怎麼把它交給多個平行的 agent 而不會爛掉**。

團隊定案：Claude 做前端（多個 Sonnet 平行）、Codex 做後端（多個子代理平行）。
但在派工之前先做了一件事——**完整的邊界稽核**。

結果是這輪最重要的產出：稽核發現的六類問題，每一條放著不管都會在
「前後端平行 ＋ 後端再開三個子代理」的情境下被放大成合併衝突或執行期錯誤。
其中一條（migration 權限）已經用真的 PostgreSQL 證實：**修正前 migration 會回報成功，
但 14 個模組 role 對 outbox 一個權限都沒有。**

業務邏輯依然一行都沒寫，這仍然是刻意的。

## 🚀 核心戰果

1. **邊界稽核修掉六類問題（A–F）**

   | | 問題 | 為什麼致命 |
   |---|---|---|
   | A1 | 事件缺聚合識別 | `outbox_message` 兩個 NOT NULL 欄位，`PublishAsync` 拿不到值——**寫不出合法的 INSERT** |
   | A2 | 事件型別反查無登錄 | `static abstract EventType` 只能正向，dispatcher 從 DB 讀回字串**還原不了 CLR 型別** |
   | A3 | outbox 交易三份文件互相矛盾 | 「同交易」「每模組一個 DbContext」「獨立 PlatformDbContext」三句不可能同時成立 |
   | B | migration 權限會整組失效 | `ALTER DEFAULT PRIVILEGES` 只對 owner 建的物件生效，`0002` 沒 `SET ROLE` |
   | C | 5 條訂閱關係缺 `ProjectReference` | 含 `Ledger → procurement.GoodsReceived`（DR 存貨／CR 現金，關鍵路徑）|
   | D | 規則有洞 | **沒有任何測試阻止業務模組依賴支撐模組**；Contracts 可用 PackageReference 繞過 |
   | E | JSON 形狀未定案 | 44 個事件多人平行寫會分歧，而 outbox payload 一旦有正式資料就改不動 |
   | F | Storefront 參考全部 14 個 Infra | 公開行程持有 14 個 schema 的 DB 登入，ADR-004 被抵銷一半 |

   新增 ADR-016（`platform` 是刻意的共用例外）、ADR-017（支撐模組的精確規則）、
   ADR-018（JSON 線上格式唯一來源）。

2. **API 契約 v1.0 凍結** —— 稽核發現的最要命一條：repo 裡**完全沒有 API 契約**。
   要前後端平行，這就是唯一真正的邊界，而它不存在。

   - `docs/05-API契約.md`：認證、RFC 9457 錯誤、冪等、游標分頁、資料形狀，
     外加「前端不可以做的四件事／後端不可以做的三件事」
   - `docs/api/openapi.storefront.yaml`（28 操作 / 35 schema）
   - `docs/api/openapi.admin.yaml`（40 操作 / 43 schema）
   - 實測產得出 **3,882 行 TS 型別**

3. **前端骨架（實際 build 過，不是空資料夾）**

   ```
   frontend/  pnpm workspace 五專案
     apps/storefront      Soft Seoul · :5002 → BFF :5000
     apps/admin           中性密集 · :5003 → BFF :5001 · 有深色模式
     packages/ui          設計 token —— 顏色與尺寸的唯一來源
     packages/api-client  HTTP · Money · Problem Details · 冪等鍵 · 產生的型別
   ```

   設計層用 ui-ux-pro-max 產出，但兩處按實測改掉：工具建議的 `#EC4899` 當白字按鈕底
   只有 **3.53:1 過不了 AA**，粉紅因此拆成三個不可互換的角色；建議的 Nunito + DM Sans
   對繁體中文無效，改成 Nunito 打頭、中文落到 Noto Sans TC。

4. **測試 10 → 28 條**
   - 架構測試 10 → 12（業務模組不得依賴支撐模組、Contracts 不得宣告基礎設施套件）
   - **新增 `tests/GreyGray.Contracts.Tests`（16 條）**守 ADR-018 的線上格式與事件目錄
   - `ops/test.ps1` 從「寫死只跑 Architecture.Tests」改成掃描全部並比對數量

5. **migration 在真的 PostgreSQL 17.11 上跑過並驗證**（見下方「踩到的坑」）

6. **兩份派工書 ＋ 檔案所有權表**
   `docs/06-前端工作包.md`（FE-1~8，兩波）、`docs/07-後端派工書.md`（BE-1~8，四波）、
   `docs/08-Codex啟動prompt.md`（貼上即用）。
   `docs/04-Codex啟動指示.md` 標記作廢。

## 📈 技術指標

- **專案數**: 50（+1，新增契約測試）| **版控檔案**: 152
- **編譯狀態**: ✅ 0 Error / 0 Warning
- **測試**: ✅ 28/28（架構 12 ＋ 契約 16），**新規則全部注入違規驗證過會紅**
- **前端**: ✅ `pnpm -r typecheck` 4 專案全過、兩個 app 都 build 成功
- **ADR**: 18 條已定案，1 題待決
- **git**: 7 commits 在 `feat/contract-freeze-and-frontend-scaffold`，84 檔案 +13,024 行

## ⚠️ 踩到的坑（這段最有價值）

1. **「回報成功但權限是零」的 migration** ——
   `ALTER DEFAULT PRIVILEGES FOR ROLE greygray_owner` 只對**由 greygray_owner 建立的物件**
   生效。`0002` 沒有 `SET ROLE`，表會屬於執行 migration 的部署帳號。

   在 PostgreSQL 17.11 容器上實跑三種情境：

   | 情境 | migration 回報 | 實際結果 |
   |---|---|---|
   | 修正前（無 `SET ROLE`、無斷言）| **成功** | 14 個模組 role 對 outbox **零權限** |
   | 只忘 `SET ROLE`（斷言在）| **失敗並整個 rollback** | 一張表都沒建，訊息直指原因 |
   | 修正後 | 成功 | 15 schema／17 role／4 表，owner 全對 |

   **教訓**：`CREATE SCHEMA ... AUTHORIZATION` 只決定 schema 的 owner，救不了表的 owner。
   而症狀是執行期的 `permission denied for table`，訊息完全不指向真正的原因。
   所以在 migration 結尾放了一條 owner 斷言——忘了就在 COMMIT 前爆。

2. **同一個 payload 裡兩種 GUID 格式** ——
   覆驗時實測抓到：`orderId` 是無連字號，`eventId` 帶連字號（STJ 對裸 `Guid` 的預設行為）。
   而 `eventId` 正是 `processed_message` 的去重 key。
   一邊 `Guid.Parse` 比、一邊字串比，就會「同一則訊息處理兩次」——
   **而那只會在正式環境的重送路徑上出現。**

   **教訓**：訂了規則（ADR-018）不等於規則成立。寫一支拋棄式 console 實際印出來看，
   五分鐘就抓到了。

3. **`ops/test.ps1` 寫死只跑一個測試專案** ——
   新增 `Contracts.Tests` 之後，它會靜默地永遠不被執行，而畫面上仍然一片綠。
   這正是 HANDOFF_1 記的「架構測試的空跑陷阱」換了個形狀又出現一次。

   **教訓**：任何「跑測試」的入口都要能回答「我少跑了什麼嗎」。
   現在它比對專案數與實際執行數，對不上直接 throw。

4. **文件會自相矛盾，而矛盾比錯誤更難發現** ——
   稽核抓到三處：「支撐群沒有出箭頭」與事件目錄自己列的三條出箭頭衝突；
   `Hosts_must_not_reference_core` 的 DisplayName 寫「含遞移」但實作只檢查直接參考；
   我自己寫派工書時也犯了一次（「禁止跨 schema JOIN，沒有例外」後面接「platform 是例外」）。

   **教訓**：規則文件要能被腳本檢查。索引表原本用 `{id}` 簡寫又把多個方法擠一行，
   結果與 YAML 對不齊也查不出來——改成一行一端點之後，28/28 與 40/40 用腳本一秒驗完。

5. **讀 `.env.example` 當成實際設定** ——
   回答「planner 資料寫到哪」時讀了範本檔，答成「本機 MongoDB」。
   實際的 `.env.local` 第一行就寫著 `This machine is a planner CLIENT, not the server`，
   資料是送到正式機的 `https://planner.tkflyc.com`。使用者指正才發現。

   **教訓**：`.env.example` 是給人抄的，不是現況。要看現況就看實際載入的那份。

## 🎯 下一階段規劃 (NextWork)

**後端（Codex）** —— 照 `docs/08`，一次一波，驗收過再給下一波
1. 第一波：BE-1 Outbox ＋ 事件型別登錄 · BE-4 OTel ＋ IClock · BE-8 CI 與部署
2. 第二波：BE-2 消費端冪等 · BE-3 Idempotency 與 Saga Timer
3. 第三波：BE-5 組合根樣板（**先只做 Identity 與 Catalog**）· BE-7 通路接縫進 schema
4. 第四波：BE-6 hello-world 端對端 —— **這一包通過就是 M0 完成**

**前端（多個 agent）** —— 照 `docs/06`，檔案所有權表是邊界
1. 第一波：FE-1 型別 ＋ mock · FE-2 Soft Seoul 元件庫 · FE-6 後台殼
2. 第二波：FE-3 逛與找 · FE-4 買 · FE-5 我的 · FE-7 商品與開團 · FE-8 訂單與帳務

**可完全並行**：M-1 環境整備七件事（正式機 YC 一件都沒做），
另外要裝 Node 並把 5002/5003 登記進 prod-monitor 的 port 指紋。

**擋著的決策**：歷史會員與訂單要不要遷移。**已經開始有連帶成本**——
`RegisterRequest` 標了「欄位暫定」、FE-5 被要求把註冊表單欄位集中在一個 schema 檔。

## 📚 開工前必讀

`CLAUDE.md` → `STATE.md` → `docs/00-decisions.md`（18 條 ADR，**已決定的不要重新討論**）
→ `docs/05-API契約.md`（**前後端唯一的邊界，已凍結**）
→ 做後端看 `docs/07`（**§0 是稽核已改過的東西**）／做前端看 `docs/06` 與 `frontend/README.md`

規格來源是兩個 artifact，不在 repo 裡：
[後端藍圖 v1.0](https://claude.ai/code/artifact/9a61eb42-df39-418e-9789-38b8ffa8a6f2)（23 張圖）、
[前台五套樣板](https://claude.ai/code/artifact/334496ce-48ac-4fd9-805f-19f64f66ac1b)（已選 Soft Seoul）。

---
**簽署人:** Claude Opus 5 | **存檔路徑:** management/history/HANDOFF_2.md
