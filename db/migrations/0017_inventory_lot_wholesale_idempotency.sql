-- ============================================================================
-- 0017  批號的模組層冪等（M2 批發進貨）
--
-- POST /v1/lots 是建立型端點，副作用會 commit，而重複建立的後果是
-- 幽靈庫存 ＋ 一筆多出來的存貨分錄——跟 #23 重複出貨單同一個形狀，只是這次是錢。
--
-- BE-34 審完 33 個 BffHttp.ExecuteIdempotentAsync 呼叫點的結論是：BFF 那一層的冪等
-- 擋不住「key 被 abandon 之後同鍵重試」，底層一定要有自己的冪等。修法比照
-- fulfillment.shipment.creation_idempotency_key（0016）與 checkout.cart.checkout_idempotency_key：
-- 呼叫端把 Idempotency-Key 傳進模組，存在批號上，同一把鍵重送回原本那一張。
--
-- ★ 刻意不用 (tenant_id, sku_id, quantity, unit_cost) 這種自然鍵：
-- 同一個 SKU 一週進三次貨、每次數量成本都一樣，是日常。跟 0016 否決自然鍵同一個理由，
-- 只是這次更明顯——批號本來就是「同一個 SKU 的多批貨各自帶自己的成本」這件事的載體。
--
-- 欄位可為 NULL：這個欄位出現之前建立的批號（M1a 既有 STOCK 批號、M1b 帶回入庫）
-- 都沒有鍵，所以唯一索引是過濾式的（WHERE creation_idempotency_key IS NOT NULL），
-- 不然舊列會互相撞在一起。
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

SET ROLE greygray_owner;

ALTER TABLE inventory.lot
    ADD COLUMN IF NOT EXISTS creation_idempotency_key varchar(255);

CREATE UNIQUE INDEX IF NOT EXISTS ux_lot_tenant_creation_key
    ON inventory.lot (tenant_id, creation_idempotency_key)
    WHERE creation_idempotency_key IS NOT NULL;

COMMENT ON COLUMN inventory.lot.creation_idempotency_key IS
    '批發進貨建立這個批號時呼叫端帶進來的 Idempotency-Key；同一把鍵重送回原本那一張。'
    'NULL 表示這一列建立於本欄位存在之前，或不是走 POST /v1/lots 建的'
    '（帶回入庫走 GoodsReceived，冪等由 platform.processed_message 保證），'
    '所以唯一索引是過濾式的。'
    '這不是「同一個 SKU 只能進一次貨」——同一個 SKU 分多批進貨正是批號存在的理由。';

RESET ROLE;

DO $$
DECLARE
    wrong_owner text;
BEGIN
    SELECT string_agg(format('%s.%s (owner=%s)', schemaname, tablename, tableowner), ', ')
    INTO wrong_owner
    FROM pg_tables
    WHERE schemaname IN ('inventory', 'platform')
      AND tableowner <> 'greygray_owner';

    IF wrong_owner IS NOT NULL THEN
        RAISE EXCEPTION
            'Table owner 不是 greygray_owner：%。migration 開頭少了 SET ROLE greygray_owner。',
            wrong_owner;
    END IF;
END
$$;

COMMIT;
