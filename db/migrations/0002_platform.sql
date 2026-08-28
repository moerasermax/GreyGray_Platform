-- ============================================================================
-- 0002  Platform：Outbox、Idempotency、Saga Timer
--
-- 可靠性三件套。這三張表決定了模組之間的非同步溝通會不會掉訊息，
-- 也決定了長流程能不能被時間驅動。M0 的核心產出就是它們能跑起來。
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

-- ── 1. Transactional Outbox ────────────────────────────────────────────
-- 業務寫入與事件發布同一個交易，要嘛都成功，要嘛都不發生。
CREATE TABLE IF NOT EXISTS platform.outbox_message (
    id              uuid        PRIMARY KEY,

    -- 多租戶：M0 只留欄位、不做隔離，但這一欄現在就要有。
    -- outbox dispatcher 是背景程序，不在任何請求裡，沒有使用者上下文可推。
    -- 這張表一旦上線帶了資料，之後補欄位還得回填歷史訊息。
    tenant_id       uuid        NOT NULL,

    aggregate_type  text        NOT NULL,
    aggregate_id    text        NOT NULL,
    event_type      text        NOT NULL,
    payload         jsonb       NOT NULL,

    occurred_at     timestamptz NOT NULL,
    correlation_id  text        NOT NULL,
    causation_id    text,

    processed_at    timestamptz,
    attempts        int         NOT NULL DEFAULT 0,
    next_attempt_at timestamptz NOT NULL DEFAULT now(),
    last_error      text,
    dead_lettered   boolean     NOT NULL DEFAULT false,

    CONSTRAINT outbox_attempts_non_negative CHECK (attempts >= 0)
);

-- 派送用的部分索引：只掃還沒處理、還沒進死信、且已到重試時間的。
CREATE INDEX IF NOT EXISTS ix_outbox_pending
    ON platform.outbox_message (next_attempt_at)
    WHERE processed_at IS NULL AND dead_lettered = false;

-- 「這張單發過哪些事件」——查問題時第一個會用到的。
CREATE INDEX IF NOT EXISTS ix_outbox_aggregate
    ON platform.outbox_message (aggregate_type, aggregate_id, occurred_at DESC);

-- 死信告警用。
CREATE INDEX IF NOT EXISTS ix_outbox_dead_letter
    ON platform.outbox_message (occurred_at DESC)
    WHERE dead_lettered = true;

COMMENT ON TABLE platform.outbox_message IS
    '語意是 at-least-once。所有 consumer 必須冪等，這是硬性契約不是建議。'
    '派送用 SELECT ... FOR UPDATE SKIP LOCKED 取批次；'
    '設定租戶用 SET LOCAL（不是 SET）——pgBouncer transaction pooling 會讓 session 變數跨交易洩漏。';


-- ── 2. 消費端去重 ──────────────────────────────────────────────────────
-- at-least-once 代表同一則訊息會被送兩次。每個 handler 用這張表擋。
CREATE TABLE IF NOT EXISTS platform.processed_message (
    event_id     uuid        NOT NULL,
    handler_name text        NOT NULL,
    processed_at timestamptz NOT NULL DEFAULT now(),

    PRIMARY KEY (event_id, handler_name)
);

COMMENT ON TABLE platform.processed_message IS
    '「冪等」不是叫每個 handler 自己想辦法，而是給它一張表。'
    'handler 開頭先 INSERT ... ON CONFLICT DO NOTHING，插不進去就代表處理過了，直接 ACK。';


-- ── 3. Idempotency：對外 API 與第三方 webhook 的重放防護 ────────────────
CREATE TABLE IF NOT EXISTS platform.idempotency_key (
    key               text        NOT NULL,
    scope             text        NOT NULL,
    request_hash      text        NOT NULL,
    status            text        NOT NULL,
    response_snapshot jsonb,
    created_at        timestamptz NOT NULL DEFAULT now(),
    expires_at        timestamptz NOT NULL,

    PRIMARY KEY (key, scope),
    CONSTRAINT idempotency_status_valid
        CHECK (status IN ('IN_FLIGHT', 'COMPLETED', 'ABANDONED'))
);

CREATE INDEX IF NOT EXISTS ix_idempotency_expiry
    ON platform.idempotency_key (expires_at);

COMMENT ON TABLE platform.idempotency_key IS
    '兩處一定要用：客人送出訂單（可能連點兩次）、金流商 webhook（綠界會重送）。'
    'webhook 另外還要驗簽與時戳容忍窗，三者缺一不可。';


-- ── 4. Saga Timer：長流程的時間驅動轉移 ────────────────────────────────
CREATE TABLE IF NOT EXISTS platform.saga_timer (
    id           uuid        PRIMARY KEY,
    tenant_id    uuid        NOT NULL,
    saga_type    text        NOT NULL,
    saga_id      text        NOT NULL,
    fire_at      timestamptz NOT NULL,
    payload      jsonb       NOT NULL,
    fired_at     timestamptz,
    cancelled_at timestamptz,
    created_at   timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_saga_timer_pending
    ON platform.saga_timer (fire_at)
    WHERE fired_at IS NULL AND cancelled_at IS NULL;

CREATE INDEX IF NOT EXISTS ix_saga_timer_saga
    ON platform.saga_timer (saga_type, saga_id)
    WHERE fired_at IS NULL AND cancelled_at IS NULL;

COMMENT ON TABLE platform.saga_timer IS
    '本專案的三個時間驅動規則：逾期未付款自動取消、截團、現場漲價逾時視為照買。'
    '第三個是關鍵——它讓「問客人」從阻塞式同步等待降級成非阻塞的通知加軌跡記錄。'
    'Worker 用 pg_advisory_lock 確保單一實例掃描，避免部署時新舊兩份同時觸發。';


-- ── 5. 給 Worker 用的 advisory lock 編號 ───────────────────────────────
-- 用固定常數，不要用字串 hash——hash 碰撞的除錯成本高得離譜。
--   1001 = outbox dispatcher
--   1002 = saga timer scanner
--   1003 = 每日對帳與告警


COMMIT;
