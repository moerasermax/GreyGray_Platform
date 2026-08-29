-- ============================================================================
-- 0008  M1b-3：帶回入庫、旅程成本與待出貨接縫
--
-- 三個 command 的業務資料與 outbox 都由各自模組的 DbContext 同交易寫入。
-- 跨模組只保存穩定 ID；Campaign 子表使用 tenant composite FK 鎖住租戶邊界。
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

SET ROLE greygray_owner;

ALTER TABLE procurement.purchase_item
    ADD COLUMN IF NOT EXISTS received_at timestamptz;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'procurement.purchase_item'::regclass
          AND conname = 'purchase_item_receipt_consistent') THEN
        ALTER TABLE procurement.purchase_item
            ADD CONSTRAINT purchase_item_receipt_consistent CHECK (
                received_at IS NULL OR status = 1);
    END IF;
END
$$;

CREATE TABLE IF NOT EXISTS campaign.trip_cost (
    id           uuid         PRIMARY KEY,
    tenant_id    uuid         NOT NULL,
    campaign_id  uuid         NOT NULL,
    kind         smallint     NOT NULL,
    amount_minor bigint       NOT NULL,
    currency     varchar(3)   NOT NULL,
    memo         varchar(200) NOT NULL DEFAULT '',
    recorded_at  timestamptz  NOT NULL,

    CONSTRAINT trip_cost_campaign_same_tenant_fk
        FOREIGN KEY (tenant_id, campaign_id)
        REFERENCES campaign.campaign (tenant_id, id)
        ON DELETE CASCADE,
    CONSTRAINT trip_cost_id_not_empty CHECK (
        id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT trip_cost_kind_known CHECK (kind IN (1, 2, 3, 4, 5, 99)),
    CONSTRAINT ck_trip_cost_amount_non_negative CHECK (amount_minor >= 0),
    CONSTRAINT trip_cost_currency_known CHECK (
        currency IN ('TWD', 'JPY', 'USD', 'KRW', 'EUR', 'HKD', 'CNY', 'THB', 'GBP', 'SGD')),
    CONSTRAINT trip_cost_tenant_id_unique UNIQUE (tenant_id, id)
);

CREATE INDEX IF NOT EXISTS ix_trip_cost_tenant_campaign_recorded
    ON campaign.trip_cost (tenant_id, campaign_id, recorded_at);

ALTER TABLE ordering.order_line
    ADD COLUMN IF NOT EXISTS goods_received_at timestamptz;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ordering.order_line'::regclass
          AND conname = 'order_line_receipt_consistent') THEN
        ALTER TABLE ordering.order_line
            ADD CONSTRAINT order_line_receipt_consistent CHECK (
                goods_received_at IS NULL
                OR (fulfillment_mode = 1 AND status IN (2, 4, 5, 9)));
    END IF;
END
$$;

COMMENT ON COLUMN procurement.purchase_item.received_at IS
    '已買到品項完成帶回的時間；非空即是 GoodsReceived command 的冪等事實。';
COMMENT ON TABLE campaign.trip_cost IS
    '一趟旅程對應一個 Campaign，成本直接掛團；不做跨團攤分，金額為 bigint 最小單位。';
COMMENT ON COLUMN ordering.order_line.goods_received_at IS
    '預購品項完成帶回的時間；最後一條有效預購 line 收貨後訂單進 ReadyToShip。';

RESET ROLE;

DO $$
DECLARE
    wrong_owner text;
BEGIN
    SELECT string_agg(format('%s.%s (owner=%s)', schemaname, tablename, tableowner), ', ')
    INTO wrong_owner
    FROM pg_tables
    WHERE schemaname IN ('campaign', 'procurement', 'ordering', 'platform')
      AND tableowner <> 'greygray_owner';

    IF wrong_owner IS NOT NULL THEN
        RAISE EXCEPTION
            'Table owner 不是 greygray_owner：%。migration 開頭少了 SET ROLE greygray_owner。',
            wrong_owner;
    END IF;
END
$$;

COMMIT;
