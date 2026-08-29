-- ============================================================================
-- 0012  M1b-4：出貨、交運與簽收
--
-- Order 與 Shipment 是 N:M：shipment_order 是連接表，不是外鍵指到單一訂單。
-- order_id 只存穩定 ID，不建立跨 schema FK（禁止跨 schema JOIN，沒有例外）。
-- Shipment 與 ShipmentOrder outbox 由同一 FulfillmentDbContext 交易寫入。
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

SET ROLE greygray_owner;

CREATE TABLE IF NOT EXISTS fulfillment.shipment (
    id                         uuid        PRIMARY KEY,
    tenant_id                  uuid        NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001'::uuid,
    method                     smallint    NOT NULL,
    status                     smallint    NOT NULL,
    tracking_number            varchar(100),
    carrier_cost_amount_minor  bigint,
    carrier_cost_currency      varchar(3),
    created_at                 timestamptz NOT NULL,
    dispatched_at              timestamptz,
    delivered_at               timestamptz,

    CONSTRAINT shipment_id_not_empty CHECK (
        id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT shipment_method_known CHECK (method IN (1, 2, 3)),
    CONSTRAINT shipment_status_known CHECK (status IN (0, 1, 2, 3, 4, 5, 6, 7)),
    CONSTRAINT ck_shipment_carrier_cost_non_negative CHECK (
        carrier_cost_amount_minor IS NULL OR carrier_cost_amount_minor >= 0),
    CONSTRAINT shipment_carrier_cost_complete CHECK (
        (carrier_cost_amount_minor IS NULL AND carrier_cost_currency IS NULL)
        OR (carrier_cost_amount_minor IS NOT NULL
            AND carrier_cost_currency IN ('TWD', 'JPY', 'USD', 'KRW', 'EUR', 'HKD', 'CNY', 'THB', 'GBP', 'SGD'))),
    -- 子表要用 (tenant_id, shipment_id) 複合 FK，PRIMARY KEY (id) 不夠——
    -- Postgres 的 FK 目標欄位必須自己先有唯一約束，所以另外補一個 (tenant_id, id)。
    CONSTRAINT shipment_tenant_id_unique UNIQUE (tenant_id, id)
);

CREATE INDEX IF NOT EXISTS ix_shipment_tenant_status_created
    ON fulfillment.shipment (tenant_id, status, created_at);

CREATE TABLE IF NOT EXISTS fulfillment.shipment_order (
    id                         uuid        PRIMARY KEY,
    tenant_id                  uuid        NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001'::uuid,
    shipment_id                uuid        NOT NULL,
    order_id                   uuid        NOT NULL,

    CONSTRAINT shipment_order_shipment_same_tenant_fk
        FOREIGN KEY (tenant_id, shipment_id)
        REFERENCES fulfillment.shipment (tenant_id, id)
        ON DELETE CASCADE,
    CONSTRAINT shipment_order_id_not_empty CHECK (
        id <> '00000000-0000-0000-0000-000000000000'::uuid)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_shipment_order_tenant_shipment_order
    ON fulfillment.shipment_order (tenant_id, shipment_id, order_id);
CREATE INDEX IF NOT EXISTS ix_shipment_order_tenant_order
    ON fulfillment.shipment_order (tenant_id, order_id);

COMMENT ON TABLE fulfillment.shipment_order IS
    'Order 與 Shipment 是 N:M——一張訂單可拆多個包裹，一個包裹可含同客人多張訂單，這是日常不是邊緣案例。';
COMMENT ON COLUMN fulfillment.shipment.carrier_cost_amount_minor IS
    '付給物流商的成本，不是向客人收的運費（那是訂單的 shipping_fee）；兩者是獨立的數字。';
COMMENT ON COLUMN fulfillment.shipment_order.order_id IS
    'Ordering 模組的穩定 ID，不建立跨 schema FK——跨 schema JOIN 沒有例外。';

RESET ROLE;

DO $$
DECLARE
    wrong_owner text;
BEGIN
    SELECT string_agg(format('%s.%s (owner=%s)', schemaname, tablename, tableowner), ', ')
    INTO wrong_owner
    FROM pg_tables
    WHERE schemaname IN ('fulfillment', 'platform')
      AND tableowner <> 'greygray_owner';

    IF wrong_owner IS NOT NULL THEN
        RAISE EXCEPTION
            'Table owner 不是 greygray_owner：%。migration 開頭少了 SET ROLE greygray_owner。',
            wrong_owner;
    END IF;
END
$$;

COMMIT;
