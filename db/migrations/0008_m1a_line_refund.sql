-- ============================================================================
-- 0008  M1a 單一品項缺貨退款
--
-- Payment 必須累計部分退款，才能在多個 OrderLine 依序缺貨時判斷
-- PartiallyRefunded / Refunded，且累計永遠不得超過原收款。
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

SET ROLE greygray_owner;

ALTER TABLE payment.payment
    ADD COLUMN IF NOT EXISTS refunded_amount_minor bigint NOT NULL DEFAULT 0;

-- 舊版本只有全額退款；這個狀態可以由原付款總額無歧義回填。
UPDATE payment.payment
SET refunded_amount_minor = goods_amount_minor + shipping_amount_minor
WHERE status = 3
  AND refunded_amount_minor = 0;

DO $$
DECLARE
    ambiguous_count bigint;
BEGIN
    SELECT count(*)
    INTO ambiguous_count
    FROM payment.payment
    WHERE status = 4
      AND refunded_amount_minor = 0;

    IF ambiguous_count > 0 THEN
        RAISE EXCEPTION
            'payment.payment 有 % 筆舊 PartiallyRefunded 資料無退款累計；無法安全推導，請先人工補齊再重跑 0008。',
            ambiguous_count;
    END IF;
END
$$;

ALTER TABLE payment.payment
    DROP CONSTRAINT IF EXISTS payment_refund_amount_valid,
    DROP CONSTRAINT IF EXISTS payment_refund_status_consistent;

ALTER TABLE payment.payment
    ADD CONSTRAINT payment_refund_amount_valid CHECK (
        refunded_amount_minor >= 0
        AND refunded_amount_minor <= goods_amount_minor + shipping_amount_minor),
    ADD CONSTRAINT payment_refund_status_consistent CHECK (
        (status IN (0, 1, 2) AND refunded_amount_minor = 0)
        OR (status = 3 AND refunded_amount_minor = goods_amount_minor + shipping_amount_minor)
        OR (status = 4
            AND refunded_amount_minor > 0
            AND refunded_amount_minor < goods_amount_minor + shipping_amount_minor));

COMMENT ON COLUMN payment.payment.refunded_amount_minor IS
    '累計已完成退款（最小貨幣單位）；部分退款不得只靠 status 推測金額。';

RESET ROLE;

COMMIT;
