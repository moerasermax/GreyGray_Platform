-- ============================================================================
-- 0001  Schema 與資料庫角色
--
-- 每個模組一個 schema、一個 role，role 只 GRANT 自己的 schema。
-- 邊界違規在資料庫層就會失敗，不只是被 reviewer 抓到。
--
-- 這一份要用「有 DDL 權限的部署帳號」執行，執行後即撤回。
-- 線上服務帳號永遠沒有 DDL 權限（藍圖 §08）。
--
-- 密碼不要寫在這裡。用 psql 變數帶進來：
--   psql -v mod_pw="$(pass show daigou/db/module)" -f 0001_schemas_and_roles.sql
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

-- ── 應用程式的共同群組角色 ─────────────────────────────────────────────
-- 注意：這些 role 必須「不是」表的 owner，也不得有 BYPASSRLS。
-- Table owner 與 superuser 預設繞過 RLS——那是最常見也最災難性的配置錯誤：
-- 你以為 RLS 開了，其實整條防線根本沒生效。M0 還沒開 RLS，但角色現在就要建對。
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'daigou_app') THEN
        CREATE ROLE daigou_app NOLOGIN NOBYPASSRLS;
    END IF;

    -- schema 的 owner，只給 migration 用
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'daigou_owner') THEN
        CREATE ROLE daigou_owner NOLOGIN NOBYPASSRLS;
    END IF;
END
$$;

-- ── 14 個模組 schema ＋ 每個一個 login role ────────────────────────────
DO $$
DECLARE
    module_schema text;
    role_name     text;
    schemas       text[] := ARRAY[
        'iam', 'catalog', 'campaign', 'pricing', 'inventory', 'checkout', 'ordering',
        'procurement', 'fulfillment', 'payment', 'ledger', 'notify', 'audit', 'reporting',
        'platform'
    ];
BEGIN
    FOREACH module_schema IN ARRAY schemas LOOP
        EXECUTE format('CREATE SCHEMA IF NOT EXISTS %I AUTHORIZATION daigou_owner', module_schema);

        role_name := 'daigou_' || module_schema;
        IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = role_name) THEN
            EXECUTE format('CREATE ROLE %I LOGIN NOBYPASSRLS IN ROLE daigou_app', role_name);
        END IF;

        -- 只給自己的 schema
        EXECUTE format('GRANT USAGE ON SCHEMA %I TO %I', module_schema, role_name);
        EXECUTE format(
            'ALTER DEFAULT PRIVILEGES FOR ROLE daigou_owner IN SCHEMA %I '
            'GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO %I',
            module_schema, role_name);
        EXECUTE format(
            'ALTER DEFAULT PRIVILEGES FOR ROLE daigou_owner IN SCHEMA %I '
            'GRANT USAGE, SELECT ON SEQUENCES TO %I',
            module_schema, role_name);

        -- 明確撤掉 PUBLIC，否則「只 GRANT 自己的 schema」是假的
        EXECUTE format('REVOKE ALL ON SCHEMA %I FROM PUBLIC', module_schema);
    END LOOP;
END
$$;

-- ── audit 是唯一的例外：不可竄改追溯 ───────────────────────────────────
-- 它的 role 只能 INSERT 與 SELECT，沒有 UPDATE／DELETE。
ALTER DEFAULT PRIVILEGES FOR ROLE daigou_owner IN SCHEMA audit
    REVOKE UPDATE, DELETE ON TABLES FROM daigou_audit;

-- ── platform schema 是共用的 ───────────────────────────────────────────
-- Outbox 與 Idempotency 由每個模組在自己的交易裡寫入，
-- 所以所有模組 role 都要能讀寫 platform。這是刻意的例外，不是漏洞：
-- 業務寫入與事件發布必須在同一個交易裡，否則事件會掉。
DO $$
DECLARE
    role_name text;
    schemas   text[] := ARRAY[
        'iam', 'catalog', 'campaign', 'pricing', 'inventory', 'checkout', 'ordering',
        'procurement', 'fulfillment', 'payment', 'ledger', 'notify', 'audit', 'reporting'
    ];
    module_schema text;
BEGIN
    FOREACH module_schema IN ARRAY schemas LOOP
        role_name := 'daigou_' || module_schema;
        EXECUTE format('GRANT USAGE ON SCHEMA platform TO %I', role_name);
        EXECUTE format(
            'ALTER DEFAULT PRIVILEGES FOR ROLE daigou_owner IN SCHEMA platform '
            'GRANT SELECT, INSERT, UPDATE ON TABLES TO %I',
            role_name);
    END LOOP;
END
$$;

COMMIT;
