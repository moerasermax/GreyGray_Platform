-- ============================================================================
-- 0020  超商取貨門市名稱與地址快照（ADR-038）
--
-- 舊資料只有門市代號，所以新欄位保持可空且不回填。部署會重跑全部 migration，
-- 因此欄位新增必須冪等。
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

SET ROLE greygray_owner;

ALTER TABLE checkout.cart
    ADD COLUMN IF NOT EXISTS convenience_store_name varchar(50),
    ADD COLUMN IF NOT EXISTS convenience_store_address varchar(200);

ALTER TABLE ordering.orders
    ADD COLUMN IF NOT EXISTS convenience_store_name varchar(50),
    ADD COLUMN IF NOT EXISTS convenience_store_address varchar(200);

RESET ROLE;

DO $$
DECLARE
    wrong_owner text;
BEGIN
    SELECT string_agg(format('%s.%s (owner=%s)', schemaname, tablename, tableowner), ', ')
    INTO wrong_owner
    FROM pg_tables
    WHERE schemaname IN ('checkout', 'ordering')
      AND tableowner <> 'greygray_owner';

    IF wrong_owner IS NOT NULL THEN
        RAISE EXCEPTION
            'Table owner 不是 greygray_owner：%。migration 開頭少了 SET ROLE greygray_owner。',
            wrong_owner;
    END IF;
END
$$;

COMMIT;
