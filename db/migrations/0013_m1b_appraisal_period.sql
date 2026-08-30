-- ============================================================================
-- 0013  M1b-5：鑑賞期 Saga Timer（ADR-025）
--
-- 出貨單全部簽收後記錄鑑賞期到期時間；到期由 Saga Timer 轉 Completed。
-- StoredValue 退款守衛（ADR-023 決定二／ADR-024 後半）純屬應用層邏輯，不動 schema。
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

SET ROLE greygray_owner;

ALTER TABLE ordering.orders
    ADD COLUMN IF NOT EXISTS appraisal_due_at timestamptz;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ordering.orders'::regclass
          AND conname = 'orders_appraisal_due_requires_shipped') THEN
        ALTER TABLE ordering.orders
            ADD CONSTRAINT orders_appraisal_due_requires_shipped CHECK (
                appraisal_due_at IS NULL OR status IN (6, 7, 9));
    END IF;
END
$$;

COMMENT ON COLUMN ordering.orders.appraisal_due_at IS
    '訂單掛的出貨單全部簽收後設定；鑑賞期（ADR-025）屆滿由 Saga Timer 轉 Completed。'
    '一張訂單可能對應多個出貨單，全部簽收才會非空，不是第一張簽收就起算。';

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
