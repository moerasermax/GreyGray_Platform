-- ============================================================================
-- 0014  第七波：漲價詢問逾時每團可設（ADR-027）
--
-- 原本是 ProcurementApplicationService 裡的技術預設值（2 小時），
-- 日本藥妝店與精品店現場的節奏不一樣，改成每團可設。
-- 沒填（NULL）就沿用 2 小時的技術預設值，程式端在 ProcurementApplicationService 讀取。
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

SET ROLE greygray_owner;

ALTER TABLE campaign.campaign
    ADD COLUMN IF NOT EXISTS price_inquiry_timeout_minutes integer;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'campaign.campaign'::regclass
          AND conname = 'campaign_price_inquiry_timeout_positive') THEN
        ALTER TABLE campaign.campaign
            ADD CONSTRAINT campaign_price_inquiry_timeout_positive CHECK (
                price_inquiry_timeout_minutes IS NULL OR price_inquiry_timeout_minutes > 0);
    END IF;
END
$$;

COMMENT ON COLUMN campaign.campaign.price_inquiry_timeout_minutes IS
    '現場漲價詢問的逾時分鐘數（ADR-027，每團可設）。NULL 表示套用技術預設值 2 小時。';

RESET ROLE;

DO $$
DECLARE
    wrong_owner text;
BEGIN
    SELECT string_agg(format('%s.%s (owner=%s)', schemaname, tablename, tableowner), ', ')
    INTO wrong_owner
    FROM pg_tables
    WHERE schemaname IN ('campaign', 'platform')
      AND tableowner <> 'greygray_owner';

    IF wrong_owner IS NOT NULL THEN
        RAISE EXCEPTION
            'Table owner 不是 greygray_owner：%。migration 開頭少了 SET ROLE greygray_owner。',
            wrong_owner;
    END IF;
END
$$;

COMMIT;
