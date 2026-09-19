-- ============================================================================
-- 0021  訂單收件人姓名、手機與宅配地址快照（ADR-039）
--
-- 宅配從地址簿抄一份、超商由客人自己填，兩者都在下單當下凍結，之後改地址或刪地址
-- 都不影響已成立的訂單（#56）。舊資料沒有這幾個值，所以欄位可空且不回填。
--
-- recipient_address 是宅配地址的「完整單行字串」（郵遞區號 空格 縣市鄉鎮市區街道），
-- 超商取貨為 null（門市看 convenience_store_* 三欄）。刻意不拆結構化欄位：
-- 後台出貨只需要印出來貼在包裹上的地址，拆開反而要在前端再拼一次。
-- 部署會重跑全部 migration，因此欄位新增必須冪等。
--
-- 明文不加密：ADR-039 決定後台全員直接看得到，而同一份資料也會隨 CheckoutCompleted
-- 進到 platform.outbox_message 的 payload——只加密 orders 而不加密 outbox 是自欺。
-- 對應的保存期限由 Platform/Outbox 的清理機制負責（#57）。
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

SET ROLE greygray_owner;

ALTER TABLE checkout.cart
    ADD COLUMN IF NOT EXISTS recipient_name varchar(50),
    ADD COLUMN IF NOT EXISTS recipient_phone varchar(20),
    ADD COLUMN IF NOT EXISTS recipient_address varchar(200);

ALTER TABLE ordering.orders
    ADD COLUMN IF NOT EXISTS recipient_name varchar(50),
    ADD COLUMN IF NOT EXISTS recipient_phone varchar(20),
    ADD COLUMN IF NOT EXISTS recipient_address varchar(200);

RESET ROLE;

DO $$
DECLARE
    wrong_owner text;
BEGIN
    SELECT string_agg(format('%s.%s (owner=%s)', schemaname, tablename, tableowner), ', ')
    INTO wrong_owner
    FROM pg_tables
    WHERE schemaname IN ('checkout', 'ordering')
      AND tableowner <> 'greygray_owner';

    IF wrong_owner IS NOT NULL THEN
        RAISE EXCEPTION
            'Table owner 不是 greygray_owner：%。migration 開頭少了 SET ROLE greygray_owner。',
            wrong_owner;
    END IF;
END
$$;

COMMIT;
