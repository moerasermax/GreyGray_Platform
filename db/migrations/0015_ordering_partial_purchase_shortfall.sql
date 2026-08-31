-- ============================================================================
-- 0015  ADR-026：支援部分買到
--
-- 買到的數量照常出貨，短缺的數量退款。標記買到的當下不問退款去向
-- （比照 M1b-2 的「決策延後」模式），短缺數量先掛在 order_line 上，
-- 之後由 POST /v1/orders/{orderId}/lines/{lineId}/refund-shortfall 才真的退款。
--
-- 退款完成時 quantity 會減去 quantity_shortfall，quantity_shortfall 本身保留
-- 不歸零——「短缺過幾件」是歷史事實；「有沒有退過」由 refunded_amount_minor
-- 是否為 NULL 判斷，不另外再開一個旗標欄位。
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

SET ROLE greygray_owner;

ALTER TABLE ordering.order_line
    ADD COLUMN IF NOT EXISTS quantity_shortfall integer NOT NULL DEFAULT 0;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ordering.order_line'::regclass
          AND conname = 'ck_order_line_quantity_shortfall') THEN
        ALTER TABLE ordering.order_line
            ADD CONSTRAINT ck_order_line_quantity_shortfall CHECK (
                quantity_shortfall BETWEEN 0 AND 999);
    END IF;
END
$$;

COMMENT ON COLUMN ordering.order_line.quantity_shortfall IS
    '部分買到時短缺的數量（訂購數量 - 實際買到數量）；0 表示沒有短缺。'
    '退款完成後 quantity 已扣掉這個數量，但本欄位保留原值供追溯，'
    '「短缺是否已經退過款」看 refunded_amount_minor 是否為 NULL。';

RESET ROLE;

DO $$
DECLARE
    wrong_owner text;
BEGIN
    SELECT string_agg(format('%s.%s (owner=%s)', schemaname, tablename, tableowner), ', ')
    INTO wrong_owner
    FROM pg_tables
    WHERE schemaname IN ('ordering', 'platform')
      AND tableowner <> 'greygray_owner';

    IF wrong_owner IS NOT NULL THEN
        RAISE EXCEPTION
            'Table owner 不是 greygray_owner：%。migration 開頭少了 SET ROLE greygray_owner。',
            wrong_owner;
    END IF;
END
$$;

COMMIT;
