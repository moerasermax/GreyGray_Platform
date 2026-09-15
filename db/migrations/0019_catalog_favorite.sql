-- ============================================================================
-- 0019  客戶最愛清單（ADR-036）
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

SET ROLE greygray_owner;

CREATE TABLE IF NOT EXISTS catalog.favorite (
    tenant_id   uuid        NOT NULL,
    customer_id uuid        NOT NULL,
    product_id  uuid        NOT NULL,
    created_at  timestamptz NOT NULL,

    PRIMARY KEY (tenant_id, customer_id, product_id),
    CONSTRAINT favorite_product_same_tenant_fk
        FOREIGN KEY (tenant_id, product_id)
        REFERENCES catalog.product (tenant_id, id)
        ON DELETE CASCADE,
    CONSTRAINT favorite_customer_id_not_empty CHECK (
        customer_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT favorite_product_id_not_empty CHECK (
        product_id <> '00000000-0000-0000-0000-000000000000'::uuid)
);

CREATE INDEX IF NOT EXISTS ix_favorite_tenant_customer_created_product
    ON catalog.favorite (tenant_id, customer_id, created_at DESC, product_id DESC);

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
END
$$;

COMMIT;
