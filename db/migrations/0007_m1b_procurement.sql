-- ============================================================================
-- 0007  M1b-1：截團採購清單與買到回報
--
-- Procurement 只保存其他模組的穩定 ID，不建立跨 schema FK。
-- PurchaseItem 與 ItemPurchased outbox 由同一 ProcurementDbContext 交易寫入。
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

SET ROLE greygray_owner;

CREATE TABLE IF NOT EXISTS procurement.purchase_item (
    id                                uuid        PRIMARY KEY,
    tenant_id                         uuid        NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001'::uuid,
    campaign_id                       uuid        NOT NULL,
    sku_id                            uuid        NOT NULL,
    order_line_id                     uuid        NOT NULL,
    quantity_requested                integer     NOT NULL,
    quantity_purchased                integer     NOT NULL DEFAULT 0,
    target_price_amount_minor         bigint,
    target_price_currency             varchar(3),
    actual_paid_original_amount_minor bigint,
    actual_paid_original_currency     varchar(3),
    actual_paid_booking_amount_minor  bigint,
    actual_paid_booking_currency      varchar(3),
    actual_paid_fx_snapshot_id        uuid,
    status                            smallint    NOT NULL,
    created_at                        timestamptz NOT NULL,
    decided_at                        timestamptz,

    CONSTRAINT purchase_item_id_not_empty CHECK (
        id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT purchase_item_status_known CHECK (status IN (0, 1, 2, 3)),
    CONSTRAINT ck_purchase_item_quantity_requested CHECK (
        quantity_requested BETWEEN 1 AND 999),
    CONSTRAINT ck_purchase_item_quantity_purchased CHECK (
        quantity_purchased BETWEEN 0 AND quantity_requested),
    CONSTRAINT ck_purchase_item_target_price_non_negative CHECK (
        target_price_amount_minor IS NULL OR target_price_amount_minor >= 0),
    CONSTRAINT purchase_item_target_price_complete CHECK (
        (target_price_amount_minor IS NULL AND target_price_currency IS NULL)
        OR (target_price_amount_minor IS NOT NULL
            AND target_price_currency IN ('TWD', 'JPY', 'USD', 'KRW', 'EUR', 'HKD', 'CNY', 'THB', 'GBP', 'SGD'))),
    CONSTRAINT ck_purchase_item_actual_paid_complete CHECK (
        (actual_paid_original_amount_minor IS NULL
            AND actual_paid_original_currency IS NULL
            AND actual_paid_booking_amount_minor IS NULL
            AND actual_paid_booking_currency IS NULL
            AND actual_paid_fx_snapshot_id IS NULL)
        OR (actual_paid_original_amount_minor >= 0
            AND actual_paid_original_currency IN ('TWD', 'JPY', 'USD', 'KRW', 'EUR', 'HKD', 'CNY', 'THB', 'GBP', 'SGD')
            AND actual_paid_booking_amount_minor >= 0
            AND actual_paid_booking_currency = 'TWD')),
    CONSTRAINT purchase_item_decision_consistent CHECK (
        (status IN (0, 2) AND decided_at IS NULL)
        OR (status IN (1, 3) AND decided_at IS NOT NULL)),
    CONSTRAINT purchase_item_purchase_consistent CHECK (
        (status <> 1 AND quantity_purchased = 0)
        OR (status = 1
            AND quantity_purchased > 0
            AND actual_paid_original_amount_minor IS NOT NULL
            AND actual_paid_booking_amount_minor IS NOT NULL))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_purchase_item_tenant_order_line
    ON procurement.purchase_item (tenant_id, order_line_id);
CREATE INDEX IF NOT EXISTS ix_purchase_item_tenant_campaign_status
    ON procurement.purchase_item (tenant_id, campaign_id, status);

COMMENT ON TABLE procurement.purchase_item IS
    'CampaignClosed 依已付款預購 OrderLine 產生；tenant/order_line 唯一，事件重送不得重複建立。';
COMMENT ON COLUMN procurement.purchase_item.actual_paid_original_amount_minor IS
    '當地原幣資訊欄位，不產生分錄。';
COMMENT ON COLUMN procurement.purchase_item.actual_paid_booking_amount_minor IS
    'TWD 記帳成本；ItemPurchased 後續入庫時以此為成本依據。';

RESET ROLE;

DO $$
DECLARE
    wrong_owner text;
BEGIN
    SELECT string_agg(format('%s.%s (owner=%s)', schemaname, tablename, tableowner), ', ')
    INTO wrong_owner
    FROM pg_tables
    WHERE schemaname IN ('procurement', 'platform')
      AND tableowner <> 'greygray_owner';

    IF wrong_owner IS NOT NULL THEN
        RAISE EXCEPTION
            'Table owner 不是 greygray_owner：%。migration 開頭少了 SET ROLE greygray_owner。',
            wrong_owner;
    END IF;
END
$$;

COMMIT;
