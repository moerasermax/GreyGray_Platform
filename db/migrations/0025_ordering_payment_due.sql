-- ============================================================================
-- 0025  Ordering 繳費期限、逾期取消與樂觀鎖（ADR-044）
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

SET ROLE greygray_owner;

ALTER TABLE ordering.orders
    ADD COLUMN IF NOT EXISTS payment_auto_cancel_at timestamptz,
    ADD COLUMN IF NOT EXISTS cancellation_source smallint;

ALTER TABLE ordering.orders
    DROP CONSTRAINT IF EXISTS orders_cancellation_source_known;

ALTER TABLE ordering.orders
    ADD CONSTRAINT orders_cancellation_source_known CHECK (
        cancellation_source IS NULL OR cancellation_source IN (1, 2, 3));

WITH b AS (
    UPDATE ordering.orders
    SET payment_due_at = COALESCE(payment_due_at, placed_at + interval '24 hours'),
        payment_auto_cancel_at = COALESCE(payment_due_at, placed_at + interval '24 hours')
    WHERE status = 0
      AND payment_auto_cancel_at IS NULL
    RETURNING id, tenant_id, payment_auto_cancel_at
)
INSERT INTO platform.saga_timer
    (id, tenant_id, saga_type, saga_id, fire_at, payload, created_at)
SELECT gen_random_uuid(),
       tenant_id,
       'ordering.payment-due',
       replace(id::text, '-', ''),
       GREATEST(payment_auto_cancel_at, now() + interval '1 hour'),
       '{}'::jsonb,
       now()
FROM b;

RESET ROLE;

COMMIT;
