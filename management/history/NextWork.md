# NextWork

接在第六輪（後端第四波 M0 hello-world 本機驗收候選）之後。
**前後端從這裡開始平行**，兩條線互不擋。

## 後端（Codex）

照 `docs/08-Codex啟動prompt.md`，**一次一波，驗收過再給下一波**。

- [x] 第一波：BE-1 Outbox ＋ 事件型別登錄 · BE-4 OTel ＋ IClock · BE-8 CI 與部署
      實作、34/34 tests 與 ops self-test 已完成。BE-8 的 live OpenAPI gate 保持 fail-closed，
      待 BE-6 提供 `/v1` endpoints 後完成最終 schema drift 驗收（見 `HANDOFF_3.md`）。
- [x] 第二波：BE-2 消費端冪等 · BE-3 Idempotency 與 Saga Timer
      46/46 tests 全綠（Platform 18 條）；processed-message transaction、並行去重、
      API idempotency lease fencing、Saga advisory lock／rollback／tenant 傳遞均已驗證。
      同輪補齊 BE-8 五服務 manifest 與兩個可攜式 Next standalone artifacts（見 `HANDOFF_4.md`）。
- [x] 第三波：BE-5 模組組合根樣板（Identity 與 Catalog）· BE-7 通路接縫進 schema
      49/49 tests 全綠（Architecture 14／Contracts 16／Platform 19）；三個 Host 已接入兩個模組，
      `0003_channel_seams.sql` 已在 PostgreSQL 17 實跑、重跑、權限與故障注入驗證（見 `HANDOFF_5.md`）。
- [ ] 第四波：BE-6 hello-world 端對端
      本機程式與永久 E2E 已完成：Identity／outbox 同交易、Worker→Notification 去重、
      trace context、架構負向測試、三個 executable 停止後再啟動都通過（見 `HANDOFF_6.md`）。
      尚差 YC 的 NSSM＋BootTrigger reboot 與可查詢 OTLP trace，故完整 M0 仍不得勾選。
- [ ] M1a：依 frozen contract 實作 `/v1/auth/register` 與其餘正式 endpoints，讓 strict live
      OpenAPI comparison 歸零；M0 `/v1/customers` 是 Development-only test hook，不可冒充。

每包交付後由我做總驗收（`docs/07-後端派工書.md` §5 的十條），
任何一條不過就整包退回，不做部分接受。

## 前端（多個 agent 平行）

照 `docs/06-前端工作包.md`，檔案所有權表是邊界。

**工作在 `GreyGray_Platform-fe` 的 `feat/frontend-wave-1` 分支，不在這棵樹上。**

- [x] 第一波：FE-1 型別 ＋ mock ＋ 端點層 · FE-2 Soft Seoul 元件庫 · FE-6 後台殼與儀表板
      驗收通過（`f67e944`）。msw 的 SSR 與 browser 兩端都實測過，
      `NEXT_PUBLIC_USE_MOCK=1` 可用，**第二波完全不需要後端**。
      驗收紀錄在 `docs/09-前端第一波派工prompt.md` 末尾。
- [ ] 第二波：FE-3 逛與找 · FE-4 買 · FE-5 我的 · FE-7 商品與開團 · FE-8 訂單與帳務
      五則子 agent prompt 已備妥（`6c56e52`），派工前已先拆掉路由衝突
      並建立共用的 `app/_lib/apiClient.ts`。**進行中。**

## 可完全並行（跟寫程式互不相干，但擋 M1 上線）

- [ ] **M-1 環境整備七件事**，正式機 YC 上一件都沒做：
      .NET 10 SDK ＋ PostgreSQL 17（data 與 `pg_wal` **一定要 C 槽 NVMe**）＋ Valkey ／
      專屬 Windows 帳號並設 ACL ／ Defender 排除 pg 目錄 ／ ngrok → cloudflared ／
      接有線網路 ／ 買 UPS ／ Windows Update 改手動加維護窗
- [ ] Node 22+ 也要裝上 YC（前端兩個 process），並登記進 prod-monitor 的 port 指紋（5002 / 5003）

## 擋著的決策

**沒有了。** 歷史會員與訂單於 2026-08-28 定案為**不遷移**（ADR-019）——
匯出要年繳才 NT$1,399、非年繳 NT$2,599，為了搬資料綁一年租約不划算。
客人重新註冊，Google 帳號串接排在 M1a 之後降低摩擦。

## 這題留下來的一件雜事

- [ ] **查舊平台的租約到期日**，填進上線公告與註冊頁的
      「舊訂單請到原平台查詢，期限到 ____」

儲值金沒有遺留問題——舊平台從來沒啟用過，餘額全是 0（2026-08-28 老闆確認）。
新系統的儲值金期初一律從 0 開始。

## 契約變更流程（新增）

`docs/05-API契約.md` 與 `docs/api/openapi.*.yaml` 是 **v1.0 凍結**的。
任何一方要改：

1. 回來改那兩份文件並說明理由
2. 前端跑 `pnpm api:generate`
3. 後端 CI 會比對 `AddOpenApi()` 的產出與 YAML，不一致就 fail

**不得單方面在實作裡改形狀。** 那是前後端平行最貴的一種技術債。

## 停損線

M0 若做超過三個月還跑不出 BE-6，把 assembly 分離與 DB role 分離降級成 namespace 邊界，
保留意圖、砍掉儀式（ADR-004）。**那不是你慢，是儀式太重。**
