# NextWork

接在 HANDOFF_1 之後。**照順序做**，M0-1 是其他所有事情的前提。

## 立刻

- [ ] **M0-1　Platform Outbox 實作**
      `docs/03-M0工作包.md` M0-1。三個必須做對的地方：
      `PublishAsync` 只寫 outbox 且參與呼叫端交易、`FOR UPDATE SKIP LOCKED` 取批次、
      派送前 `SET LOCAL app.tenant_id`（LOCAL 不是 SET）。
      驗收要跑整合測試（Testcontainers，**不要用 InMemory provider**）。

## 接著（有先後）

- [ ] M0-2　消費端冪等（`platform.processed_message`）
- [ ] M0-3　Idempotency 與 Saga Timer（advisory lock 1001/1002/1003）
- [ ] M0-4　OpenTelemetry 接線 —— 重點是 trace 不能在 outbox 斷掉
- [ ] M0-5　模組組合根樣板（先做 Identity 與 Catalog 兩個，形狀對了再複製）
- [ ] M0-6　**hello-world 端對端 —— 這就是 M0 完成的定義**
- [ ] M0-7　五個通路接縫定進 schema
- [ ] M0-8　CI 與部署腳本（部署腳本要防「假綠燈」：要求接手行程的啟動時間晚於本次重啟）

## 可完全並行（跟寫程式互不相干，但擋 M1 上線）

- [ ] **M-1 環境整備七件事**，正式機 YC 上一件都沒做：
      .NET 10 SDK + PostgreSQL 17（data 與 pg_wal **一定要 C 槽 NVMe**）+ Valkey ／
      專屬 Windows 帳號並設 ACL ／ Defender 排除 pg 目錄 ／ ngrok → cloudflared ／
      接有線網路 ／ 買 UPS ／ Windows Update 改手動加維護窗

## 擋著的決策

- [ ] **歷史會員與訂單要不要遷移**（`docs/00-decisions.md` 末段）
      租用平台看來已不給匯出，除非再付 NT$1,399。
      先問客服「這方案到底能不能匯出會員與訂單 CSV」，確認有再付。
      要在 M0 定業務 schema 前有答案——匯出檔的欄位會反過來決定 Identity 的欄位設計。

## 停損線

M0 若做超過三個月還跑不出 M0-6，把 assembly 分離與 DB role 分離降級成 namespace 邊界，
保留意圖、砍掉儀式（ADR-004）。**那不是你慢，是儀式太重。**
