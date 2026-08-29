-- ============================================================================
-- 0010  M1b-3b：帶回入庫的批號成本欄位
--
-- procurement.GoodsReceived.v1 → Inventory 開新批號、Ledger 記 DR 存貨 / CR 現金。
-- 這裡只補 inventory.lot 需要的成本／來源／團別欄位；M1a 既有的 STOCK 批號
-- 沒有走這個路徑，全部留空，用一條 group-consistency constraint 守住「要嘛全有要嘛全無」。
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

SET ROLE greygray_owner;

ALTER TABLE inventory.lot
    ADD COLUMN IF NOT EXISTS unit_cost_amount_minor bigint,
    ADD COLUMN IF NOT EXISTS unit_cost_currency varchar(3),
    ADD COLUMN IF NOT EXISTS source smallint,
    ADD COLUMN IF NOT EXISTS from_campaign_id uuid,
    ADD COLUMN IF NOT EXISTS batch_code varchar(64),
    ADD COLUMN IF NOT EXISTS received_at timestamptz;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'inventory.lot'::regclass
          AND conname = 'lot_receipt_fields_consistent') THEN
        ALTER TABLE inventory.lot
            ADD CONSTRAINT lot_receipt_fields_consistent CHECK (
                (unit_cost_amount_minor IS NULL AND unit_cost_currency IS NULL
                    AND source IS NULL AND received_at IS NULL)
                OR (unit_cost_amount_minor IS NOT NULL AND unit_cost_currency IS NOT NULL
                    AND source IS NOT NULL AND received_at IS NOT NULL));
    END IF;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'inventory.lot'::regclass
          AND conname = 'lot_unit_cost_non_negative') THEN
        ALTER TABLE inventory.lot
            ADD CONSTRAINT lot_unit_cost_non_negative CHECK (
                unit_cost_amount_minor IS NULL OR unit_cost_amount_minor >= 0);
    END IF;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'inventory.lot'::regclass
          AND conname = 'lot_unit_cost_currency_known') THEN
        ALTER TABLE inventory.lot
            ADD CONSTRAINT lot_unit_cost_currency_known CHECK (
                unit_cost_currency IS NULL
                OR unit_cost_currency IN ('TWD', 'JPY', 'USD', 'KRW', 'EUR', 'HKD', 'CNY', 'THB', 'GBP', 'SGD'));
    END IF;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'inventory.lot'::regclass
          AND conname = 'lot_source_known') THEN
        ALTER TABLE inventory.lot
            ADD CONSTRAINT lot_source_known CHECK (source IS NULL OR source IN (1, 2, 3));
    END IF;
END
$$;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'inventory.lot'::regclass
          AND conname = 'lot_campaign_same_tenant_fk') THEN
        ALTER TABLE inventory.lot
            ADD CONSTRAINT lot_campaign_same_tenant_fk
                FOREIGN KEY (tenant_id, from_campaign_id)
                REFERENCES campaign.campaign (tenant_id, id)
                ON DELETE RESTRICT;
    END IF;
END
$$;

COMMENT ON COLUMN inventory.lot.source IS
    '批號來源：1=本地批發（M2）、2=出國現場採購帶回（M1b-3）、3=客人拒收退回轉現貨（M1b-2）。';
COMMENT ON COLUMN inventory.lot.unit_cost_amount_minor IS
    '批號別實際成本，出貨時從指定批號結轉銷貨成本；M1a 既有 STOCK 批號沒有這個欄位。';
COMMENT ON COLUMN inventory.lot.from_campaign_id IS
    '帶回入庫批號的來源團；本地批發（M2）等沒有團別的批號留空。';
COMMENT ON COLUMN inventory.lot.received_at IS
    '批號建立（帶回入庫或批發進貨）的時間。';

RESET ROLE;

DO $$
DECLARE
    wrong_owner text;
BEGIN
    SELECT string_agg(format('%s.%s (owner=%s)', schemaname, tablename, tableowner), ', ')
    INTO wrong_owner
    FROM pg_tables
    WHERE schemaname IN ('inventory', 'ledger', 'platform')
      AND tableowner <> 'greygray_owner';

    IF wrong_owner IS NOT NULL THEN
        RAISE EXCEPTION
            'Table owner 不是 greygray_owner：%。migration 開頭少了 SET ROLE greygray_owner。',
            wrong_owner;
    END IF;
END
$$;

COMMIT;
