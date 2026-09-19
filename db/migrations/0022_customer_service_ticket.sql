-- ============================================================================
-- 0022  客服工單模組 CustomerService（ADR-040）
--
-- 新開 schema／role，不是既有 0001 那 14 個之一——照 0001_schemas_and_roles.sql
-- 第 65-69 行（schema／role／grant）與第 108-111 行（platform 共用例外）的形狀
-- 自己補一份，不回頭改 0001。
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

-- ── customer_service schema 與 login role ──────────────────────────────────
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'greygray_customer_service') THEN
        CREATE ROLE greygray_customer_service LOGIN NOBYPASSRLS IN ROLE greygray_app;
    END IF;
END
$$;

CREATE SCHEMA IF NOT EXISTS customer_service AUTHORIZATION greygray_owner;

GRANT USAGE ON SCHEMA customer_service TO greygray_customer_service;
ALTER DEFAULT PRIVILEGES FOR ROLE greygray_owner IN SCHEMA customer_service
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO greygray_customer_service;
ALTER DEFAULT PRIVILEGES FOR ROLE greygray_owner IN SCHEMA customer_service
    GRANT USAGE, SELECT ON SEQUENCES TO greygray_customer_service;
REVOKE ALL ON SCHEMA customer_service FROM PUBLIC;

-- platform 是共用例外（業務寫入與 outbox 必須同一交易，見 0001 第 101-104 行的說明）。
GRANT USAGE ON SCHEMA platform TO greygray_customer_service;
ALTER DEFAULT PRIVILEGES FOR ROLE greygray_owner IN SCHEMA platform
    GRANT SELECT, INSERT, UPDATE ON TABLES TO greygray_customer_service;

-- ── 表 ───────────────────────────────────────────────────────────────────
SET ROLE greygray_owner;

CREATE TABLE IF NOT EXISTS customer_service.ticket (
    id           uuid        NOT NULL,
    tenant_id    uuid        NOT NULL,
    status       smallint    NOT NULL,
    contact_email text,
    contact_phone text,
    order_id     uuid,
    customer_id  uuid,
    created_at   timestamptz NOT NULL,
    resolved_at  timestamptz,
    resolved_by  uuid,
    staff_note   text,

    PRIMARY KEY (id),
    CONSTRAINT ticket_status_known CHECK (status IN (0, 1)),
    CONSTRAINT ticket_contact_required CHECK (
        contact_email IS NOT NULL OR contact_phone IS NOT NULL),
    CONSTRAINT ticket_resolution_consistent CHECK (
        (status = 0 AND resolved_at IS NULL AND resolved_by IS NULL)
        OR (status = 1 AND resolved_at IS NOT NULL AND resolved_by IS NOT NULL))
);

CREATE INDEX IF NOT EXISTS ix_ticket_tenant_status_created
    ON customer_service.ticket (tenant_id, status, created_at DESC, id DESC);

CREATE TABLE IF NOT EXISTS customer_service.ticket_message (
    id         uuid        NOT NULL,
    ticket_id  uuid        NOT NULL,
    body       text        NOT NULL,
    menu_path  jsonb       NOT NULL DEFAULT '[]'::jsonb,
    created_at timestamptz NOT NULL,

    PRIMARY KEY (id),
    CONSTRAINT ticket_message_ticket_fk
        FOREIGN KEY (ticket_id)
        REFERENCES customer_service.ticket (id)
        ON DELETE CASCADE,
    CONSTRAINT ticket_message_body_length CHECK (
        char_length(body) BETWEEN 1 AND 2000)
);

CREATE INDEX IF NOT EXISTS ix_ticket_message_ticket_created
    ON customer_service.ticket_message (ticket_id, created_at);

RESET ROLE;

DO $$
DECLARE
    wrong_owner text;
BEGIN
    SELECT string_agg(format('%s.%s (owner=%s)', schemaname, tablename, tableowner), ', ')
    INTO wrong_owner
    FROM pg_tables
    WHERE schemaname = 'customer_service'
      AND tableowner <> 'greygray_owner';

    IF wrong_owner IS NOT NULL THEN
        RAISE EXCEPTION
            'Table owner 不是 greygray_owner：%。migration 開頭少了 SET ROLE greygray_owner。',
            wrong_owner;
    END IF;
END
$$;

COMMIT;
