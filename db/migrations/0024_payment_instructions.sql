-- ============================================================================
-- 0024  Payment 已取號待繳費狀態與取號資訊（ADR-044）
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

SET ROLE greygray_owner;

ALTER TABLE payment.payment
    ADD COLUMN IF NOT EXISTS method smallint,
    ADD COLUMN IF NOT EXISTS bank_code text,
    ADD COLUMN IF NOT EXISTS virtual_account text,
    ADD COLUMN IF NOT EXISTS payment_no text,
    ADD COLUMN IF NOT EXISTS barcode_1 text,
    ADD COLUMN IF NOT EXISTS barcode_2 text,
    ADD COLUMN IF NOT EXISTS barcode_3 text,
    ADD COLUMN IF NOT EXISTS provider_expires_at timestamptz,
    ADD COLUMN IF NOT EXISTS instructions_issued_at timestamptz,
    ADD COLUMN IF NOT EXISTS late_captured_at timestamptz,
    ADD COLUMN IF NOT EXISTS late_capture_trade_no text;

DROP INDEX IF EXISTS payment.ux_payment_tenant_order_active;
CREATE UNIQUE INDEX ux_payment_tenant_order_active
    ON payment.payment (tenant_id, order_id)
    WHERE status IN (0, 1, 4, 5);

ALTER TABLE payment.payment
    DROP CONSTRAINT IF EXISTS payment_method_known,
    DROP CONSTRAINT IF EXISTS payment_instructions_consistent,
    DROP CONSTRAINT IF EXISTS payment_instruction_lengths,
    DROP CONSTRAINT IF EXISTS payment_late_capture_consistent;

ALTER TABLE payment.payment
    ADD CONSTRAINT payment_method_known CHECK (
        method IS NULL OR method IN (0, 1, 2, 3)),
    ADD CONSTRAINT payment_instructions_consistent CHECK (
        status <> 5 OR (
            method IN (1, 2, 3)
            AND provider_expires_at IS NOT NULL
            AND instructions_issued_at IS NOT NULL
            AND provider_transaction_id IS NOT NULL
            AND (
                (method = 1
                    AND bank_code IS NOT NULL AND btrim(bank_code) <> ''
                    AND virtual_account IS NOT NULL AND btrim(virtual_account) <> ''
                    AND payment_no IS NULL
                    AND barcode_1 IS NULL AND barcode_2 IS NULL AND barcode_3 IS NULL)
                OR (method = 2
                    AND bank_code IS NULL AND virtual_account IS NULL
                    AND payment_no IS NOT NULL AND btrim(payment_no) <> ''
                    AND barcode_1 IS NULL AND barcode_2 IS NULL AND barcode_3 IS NULL)
                OR (method = 3
                    AND bank_code IS NULL AND virtual_account IS NULL AND payment_no IS NULL
                    AND barcode_1 IS NOT NULL AND btrim(barcode_1) <> ''
                    AND barcode_2 IS NOT NULL AND btrim(barcode_2) <> ''
                    AND barcode_3 IS NOT NULL AND btrim(barcode_3) <> '')
            ))),
    -- 官方存檔頁目前列 3／16／14／20 字元；保留到 32，避免把供應商擴充誤擋成 500。
    ADD CONSTRAINT payment_instruction_lengths CHECK (
        (bank_code IS NULL OR char_length(bank_code) <= 32)
        AND (virtual_account IS NULL OR char_length(virtual_account) <= 32)
        AND (payment_no IS NULL OR char_length(payment_no) <= 32)
        AND (barcode_1 IS NULL OR char_length(barcode_1) <= 32)
        AND (barcode_2 IS NULL OR char_length(barcode_2) <= 32)
        AND (barcode_3 IS NULL OR char_length(barcode_3) <= 32)),
    ADD CONSTRAINT payment_late_capture_consistent CHECK (
        (late_capture_trade_no IS NULL AND late_captured_at IS NULL)
        OR (late_capture_trade_no IS NOT NULL AND late_captured_at IS NOT NULL AND status = 2));

RESET ROLE;

COMMIT;
