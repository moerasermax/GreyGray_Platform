-- ============================================================================
-- 0004  M0 hello-world：Identity Customer → Notification
--
-- 只建立 M0-6 端對端驗收需要的最小業務表。
-- 正式註冊的手機、密碼、加密聯絡資料與 session 屬 M1a，不在這裡偽裝完成。
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

SET ROLE greygray_owner;

CREATE TABLE IF NOT EXISTS iam.customer (
    id           uuid        PRIMARY KEY,
    tenant_id    uuid        NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001'::uuid,
    display_name text        NOT NULL,
    tier         smallint    NOT NULL DEFAULT 0,
    is_active    boolean     NOT NULL DEFAULT true,
    created_at   timestamptz NOT NULL,

    CONSTRAINT customer_id_not_empty CHECK (
        id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT customer_display_name_valid CHECK (
        btrim(display_name) <> '' AND char_length(display_name) <= 50),
    CONSTRAINT customer_tier_known CHECK (tier IN (0, 1, 2))
);

CREATE INDEX IF NOT EXISTS ix_customer_tenant_created
    ON iam.customer (tenant_id, created_at);

COMMENT ON TABLE iam.customer IS
    'M0 只驗證客戶聚合與 outbox 同交易；M1a 再加入正式註冊與加密聯絡資料。';


CREATE TABLE IF NOT EXISTS notify.notification (
    id              uuid        PRIMARY KEY,
    tenant_id       uuid        NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001'::uuid,
    customer_id     uuid        NOT NULL,
    channel         smallint    NOT NULL,
    template_code   text        NOT NULL,
    status          smallint    NOT NULL DEFAULT 0,
    created_at      timestamptz NOT NULL,
    trace_id        text        NOT NULL,
    handler_span_id text,

    CONSTRAINT notification_id_not_empty CHECK (
        id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT notification_customer_id_not_empty CHECK (
        customer_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT notification_channel_known CHECK (channel IN (1, 2, 3)),
    CONSTRAINT notification_status_known CHECK (status IN (0, 1, 2, 3)),
    CONSTRAINT notification_template_code_not_blank CHECK (btrim(template_code) <> ''),
    CONSTRAINT notification_trace_id_w3c CHECK (
        trace_id ~ '^[0-9a-f]{32}$' AND trace_id <> repeat('0', 32)),
    CONSTRAINT notification_handler_span_id_w3c CHECK (
        handler_span_id IS NULL
        OR (handler_span_id ~ '^[0-9a-f]{16}$' AND handler_span_id <> repeat('0', 16)))
);

-- 不建立跨 schema FK。Notification 只儲存事件帶來的穩定 CustomerId，
-- 不能藉 FK 或 JOIN 取得 iam schema 權限。
CREATE INDEX IF NOT EXISTS ix_notification_tenant_customer_created
    ON notify.notification (tenant_id, customer_id, created_at DESC);

COMMENT ON TABLE notify.notification IS
    'M0 只排入資料庫不真正送 LINE；processed_message marker 與這筆副作用必須同交易。';
COMMENT ON COLUMN notify.notification.trace_id IS
    '實際處理這筆通知的 W3C TraceId，用來驗證 API → outbox → Worker trace 連續。';


RESET ROLE;

DO $$
DECLARE
    wrong_owner text;
BEGIN
    SELECT string_agg(format('%s.%s (owner=%s)', schemaname, tablename, tableowner), ', ')
    INTO wrong_owner
    FROM pg_tables
    WHERE schemaname IN (
        'iam', 'catalog', 'campaign', 'pricing', 'inventory', 'checkout', 'ordering',
        'procurement', 'fulfillment', 'payment', 'ledger', 'notify', 'audit', 'reporting',
        'platform'
    )
      AND tableowner <> 'greygray_owner';

    IF wrong_owner IS NOT NULL THEN
        RAISE EXCEPTION
            'Table owner 不是 greygray_owner：%。ALTER DEFAULT PRIVILEGES 不會套到這些表上，'
            '模組 role 會拿不到任何權限。migration 開頭少了 SET ROLE greygray_owner。',
            wrong_owner;
    END IF;
END
$$;

COMMIT;
