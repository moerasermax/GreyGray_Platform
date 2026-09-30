-- ============================================================================
-- 0023  商品分類固定兩層（ADR-041）
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

SET ROLE greygray_owner;

ALTER TABLE catalog.category
    ADD COLUMN IF NOT EXISTS parent_id uuid;

ALTER TABLE catalog.category
    DROP CONSTRAINT IF EXISTS category_parent_same_tenant_fk;

ALTER TABLE catalog.category
    ADD CONSTRAINT category_parent_same_tenant_fk
        FOREIGN KEY (tenant_id, parent_id)
        REFERENCES catalog.category (tenant_id, id)
        ON DELETE RESTRICT;

ALTER TABLE catalog.category
    DROP CONSTRAINT IF EXISTS category_parent_not_self;

ALTER TABLE catalog.category
    ADD CONSTRAINT category_parent_not_self CHECK (
        parent_id IS NULL OR parent_id <> id);

CREATE INDEX IF NOT EXISTS ix_category_tenant_parent
    ON catalog.category (tenant_id, parent_id);

CREATE OR REPLACE FUNCTION catalog.category_two_level_guard()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    parent_parent_id uuid;
BEGIN
    -- 同一租戶的階層寫入必須排成一列，避免兩筆交易同時建立 A→B、B→A。
    PERFORM pg_advisory_xact_lock(
        hashtextextended('catalog.category-tree:' || NEW.tenant_id::text, 0));

    IF NEW.parent_id IS NULL THEN
        RETURN NEW;
    END IF;

    SELECT parent_id
    INTO parent_parent_id
    FROM catalog.category
    WHERE tenant_id = NEW.tenant_id
      AND id = NEW.parent_id;

    -- 查不到上層時交給複合外鍵，以 23503／constraint 名稱表達。
    IF NOT FOUND THEN
        RETURN NEW;
    END IF;

    IF parent_parent_id IS NOT NULL
       OR EXISTS (
            SELECT 1
            FROM catalog.category
            WHERE tenant_id = NEW.tenant_id
              AND parent_id = NEW.id) THEN
        RAISE EXCEPTION '商品分類只允許根分類與直接子分類兩層。'
            USING ERRCODE = 'check_violation',
                  CONSTRAINT = 'category_two_level';
    END IF;

    RETURN NEW;
END
$$;

DROP TRIGGER IF EXISTS category_two_level_guard ON catalog.category;
CREATE TRIGGER category_two_level_guard
    BEFORE INSERT OR UPDATE OF parent_id ON catalog.category
    FOR EACH ROW EXECUTE FUNCTION catalog.category_two_level_guard();

RESET ROLE;

DO $$
DECLARE
    wrong_owner text;
BEGIN
    SELECT string_agg(format('%s.%s (owner=%s)', schemaname, tablename, tableowner), ', ')
    INTO wrong_owner
    FROM pg_tables
    WHERE schemaname = 'catalog'
      AND tableowner <> 'greygray_owner';

    IF wrong_owner IS NOT NULL THEN
        RAISE EXCEPTION
            'Table owner 不是 greygray_owner：%。migration 開頭少了 SET ROLE greygray_owner。',
            wrong_owner;
    END IF;

    SELECT string_agg(format('%s (owner=%s)', proname, pg_get_userbyid(proowner)), ', ')
    INTO wrong_owner
    FROM pg_proc
    WHERE pronamespace = 'catalog'::regnamespace
      AND proowner <> 'greygray_owner'::regrole;

    IF wrong_owner IS NOT NULL THEN
        RAISE EXCEPTION
            'Function owner 不是 greygray_owner：%。函式必須建在 SET ROLE greygray_owner 之內。',
            wrong_owner;
    END IF;
END
$$;

COMMIT;
