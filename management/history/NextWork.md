# NextWork

接在第三輪（後端第一波基礎設施）之後。
**前後端從這裡開始平行**，兩條線互不擋。

## 後端（Codex）

照 `docs/08-Codex啟動prompt.md`，**一次一波，驗收過再給下一波**。

- [x] 第一波：BE-1 Outbox ＋ 事件型別登錄 · BE-4 OTel ＋ IClock · BE-8 CI 與部署
      實作、34/34 tests 與 ops self-test 已完成。BE-8 的 live OpenAPI gate 保持 fail-closed，
      待 BE-6 提供 `/v1` endpoints 後完成最終 schema drift 驗收（見 `HANDOFF_3.md`）。
- [ ] 第二波：BE-2 消費端冪等 · BE-3 Idempotency 與 Saga Timer
- [ ] 第三波：BE-5 模組組合根樣板（**先只做 Identity 與 Catalog**）· BE-7 通路接縫進 schema
- [ ] 第四波：BE-6 hello-world 端對端 —— **這一包通過就是 M0 完成**

每包交付後由我做總驗收（`docs/07-後端派工書.md` §5 的九條），
任何一條不過就整包退回，不做部分接受。

## 前端（多個 agent 平行）

照 `docs/06-前端工作包.md`，檔案所有權表是邊界。

- [ ] 第一波：FE-1 型別 ＋ mock ＋ 端點層 · FE-2 Soft Seoul 元件庫 · FE-6 後台殼與儀表板
      FE-1 的 mock 是全部人的資料來源，**它最先要好**。
- [ ] 第二波：FE-3 逛與找 · FE-4 買 · FE-5 我的 · FE-7 商品與開團 · FE-8 訂單與帳務

## 可完全並行（跟寫程式互不相干，但擋 M1 上線）

- [ ] **M-1 環境整備七件事**，正式機 YC 上一件都沒做：
      .NET 10 SDK ＋ PostgreSQL 17（data 與 `pg_wal` **一定要 C 槽 NVMe**）＋ Valkey ／
      專屬 Windows 帳號並設 ACL ／ Defender 排除 pg 目錄 ／ ngrok → cloudflared ／
      接有線網路 ／ 買 UPS ／ Windows Update 改手動加維護窗
- [ ] Node 20+ 也要裝上 YC（前端兩個 process），並登記進 prod-monitor 的 port 指紋（5002 / 5003）

## 擋著的決策

- [ ] **歷史會員與訂單要不要遷移**（`docs/00-decisions.md` 末段）
      先問客服「那個 NT$1,399 的方案到底能不能匯出會員與訂單 CSV」，確認有再付。
      **已經有連帶影響**：`RegisterRequest` 的欄位標了「暫定」，
      前端 FE-5 被要求把註冊表單欄位集中在一個 schema 檔。拖越久要改的地方越多。

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
