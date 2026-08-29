-- ============================================================================
-- 0011  M1b-2：缺貨補償與現場漲價詢問
--
-- 「標記缺貨」只記procurement 端的事實，不承載退款去向；
-- 客人選出來之前不觸發任何退款（ADR-023）。現場漲價開一輪詢問，
-- 逾時視為照買由 Worker 的 Saga timer 負責，這裡只加 inquiry 表。
-- Inquiry 與 PurchaseItem 業務資料、outbox 都由同一個 ProcurementDbContext 交易寫入。
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

SET ROLE greygray_owner;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'procurement.purchase_item'::regclass
          AND conname = 'purchase_item_tenant_id_unique') THEN
        ALTER TABLE procurement.purchase_item
            ADD CONSTRAINT purchase_item_tenant_id_unique UNIQUE (tenant_id, id);
    END IF;
END
$$;

CREATE TABLE IF NOT EXISTS procurement.inquiry (
    id                           uuid        PRIMARY KEY,
    tenant_id                    uuid        NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001'::uuid,
    purchase_item_id             uuid        NOT NULL,
    original_price_amount_minor  bigint      NOT NULL,
    original_price_currency      varchar(3)  NOT NULL,
    new_price_amount_minor       bigint      NOT NULL,
    new_price_currency           varchar(3)  NOT NULL,
    asked_at                     timestamptz NOT NULL,
    timeout_at                   timestamptz NOT NULL,
    replied_at                   timestamptz,
    outcome                      smallint,
    reply_text                   varchar(200),

    CONSTRAINT inquiry_id_not_empty CHECK (
        id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT inquiry_purchase_item_same_tenant_fk
        FOREIGN KEY (tenant_id, purchase_item_id)
        REFERENCES procurement.purchase_item (tenant_id, id)
        ON DELETE CASCADE,
    CONSTRAINT inquiry_original_price_non_negative CHECK (
        original_price_amount_minor >= 0),
    CONSTRAINT inquiry_new_price_non_negative CHECK (
        new_price_amount_minor >= 0),
    CONSTRAINT inquiry_original_price_currency_known CHECK (
        original_price_currency IN ('TWD', 'JPY', 'USD', 'KRW', 'EUR', 'HKD', 'CNY', 'THB', 'GBP', 'SGD')),
    CONSTRAINT inquiry_new_price_currency_known CHECK (
        new_price_currency IN ('TWD', 'JPY', 'USD', 'KRW', 'EUR', 'HKD', 'CNY', 'THB', 'GBP', 'SGD')),
    CONSTRAINT inquiry_outcome_known CHECK (outcome IS NULL OR outcome IN (1, 2, 3)),
    CONSTRAINT inquiry_resolution_consistent CHECK (
        (replied_at IS NULL AND outcome IS NULL)
        OR (replied_at IS NOT NULL AND outcome IS NOT NULL))
);

CREATE INDEX IF NOT EXISTS ix_inquiry_tenant_purchase_item
    ON procurement.inquiry (tenant_id, purchase_item_id);

-- 同一採購品項同一時間最多一輪未解決的詢問；系統不阻塞現場動作，
-- 但兩輪同時開著會讓「逾時視為照買」不知道該放行哪一輪報價。
CREATE UNIQUE INDEX IF NOT EXISTS ux_inquiry_purchase_item_open
    ON procurement.inquiry (purchase_item_id)
    WHERE replied_at IS NULL;

COMMENT ON TABLE procurement.inquiry IS
    '現場詢價的軌跡。糾紛時這份軌跡就是證據：問了、幾點問的、客人有沒有回。';
COMMENT ON COLUMN procurement.inquiry.outcome IS
    '1=ConfirmedByCustomer 2=DeclinedByCustomer 3=AutoApprovedOnTimeout。';

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
