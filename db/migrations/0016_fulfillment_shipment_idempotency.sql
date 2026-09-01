-- ============================================================================
-- 0016  出貨單的模組層冪等（「現在卡在哪」#23）
--
-- POST /v1/shipments 原本一層冪等都沒有：CreateAsync 每次 ShipmentId.New()、
-- 不發事件、不改訂單狀態，訂單建完仍是 ReadyToShip，所以「同一批訂單重送」
-- 會通過同一個守衛建出第二張；既有的唯一鍵
-- ux_shipment_order_tenant_shipment_order 是 (tenant_id, shipment_id, order_id)，
-- 擋不到這件事。重複那張會被撿貨、被交運，物流成本重複入帳。
--
-- 修法比照 checkout.cart.checkout_idempotency_key：呼叫端把 Idempotency-Key
-- 傳進模組，存在出貨單上，同一把鍵重送回原本那一張。
--
-- ★ 刻意不用 (tenant_id, order_ids, method) 這種自然鍵：Order 與 Shipment 是 N:M，
-- 「同一批訂單、同一個配送方式建成兩張」就是「一張訂單拆兩箱」這個日常情境本身
-- （見 0012 的 COMMENT ON TABLE fulfillment.shipment_order）。
--
-- 欄位可為 NULL：這個欄位出現之前建立的出貨單沒有鍵，所以唯一索引是過濾式的
-- （WHERE creation_idempotency_key IS NOT NULL），不然舊列會互相撞在一起。
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

SET ROLE greygray_owner;

ALTER TABLE fulfillment.shipment
    ADD COLUMN IF NOT EXISTS creation_idempotency_key varchar(255);

CREATE UNIQUE INDEX IF NOT EXISTS ux_shipment_tenant_creation_key
    ON fulfillment.shipment (tenant_id, creation_idempotency_key)
    WHERE creation_idempotency_key IS NOT NULL;

COMMENT ON COLUMN fulfillment.shipment.creation_idempotency_key IS
    '建立這張出貨單時呼叫端帶進來的 Idempotency-Key；同一把鍵重送回原本那一張。'
    'NULL 表示這一列建立於本欄位存在之前，所以唯一索引是過濾式的。'
    '這不是「同一批訂單只能出一張」——Order 與 Shipment 是 N:M，拆包裹是日常。';

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
