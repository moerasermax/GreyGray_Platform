-- ============================================================================
-- 0018  庫存保留的第三個狀態：已出庫（#43 交運出庫並結轉銷貨成本）
--
-- 交運（fulfillment.ShipmentDispatched.v1）時貨真的離開倉庫：lot 的
-- quantity_on_hand 與 quantity_reserved 各扣掉 allocation 的數量，同時逐筆發
-- inventory.StockCostAllocated.v1 讓 Ledger 開 DR 銷貨成本 / CR 存貨。
-- 這件事必須是冪等的，而且不能只靠 platform.processed_message：
-- 它的去重範圍是 (event, handler)，擋得住「同一個 ShipmentDispatched 重放」，
-- 但擋不住「兩張不同的出貨單都帶到同一個 orderId」（一張訂單可以拆進多張出貨單）。
-- 真正的守衛是 reservation 自己的狀態，所以要有一個「已出庫」。
--
-- ★ 刻意不挪用 released_at：
-- 「釋放」是訂單取消、把貨還回可賣；「出庫」是貨離開倉庫、再也回不來。
-- 兩件事對庫存的方向相反，共用一個時間欄位以後，看帳的人分不出這批貨是被退回
-- 還是被寄走了。而 A-4 要求「已釋放的保留又收到交運 → 失敗並講清楚」，
-- 那條守衛的前提就是這兩個狀態分得開。
--
-- ★ 這份 migration 必須可以重跑：
-- 部署腳本每次都把 0001 到最新一份全部重跑一遍（正式機 log 全是
-- "already exists, skipping"）。所以 constraint 一律 DROP ... IF EXISTS 之後再 ADD。
--
-- 部分索引 ix_reservation_tenant_active_created ... WHERE status = 0 語意不變
-- （「還在保留中的」仍然只有 status = 0），不用動。
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

SET ROLE greygray_owner;

ALTER TABLE inventory.reservation
    ADD COLUMN IF NOT EXISTS consumed_at timestamptz;

ALTER TABLE inventory.reservation
    DROP CONSTRAINT IF EXISTS reservation_status_known;

ALTER TABLE inventory.reservation
    ADD CONSTRAINT reservation_status_known CHECK (status IN (0, 1, 2));

ALTER TABLE inventory.reservation
    DROP CONSTRAINT IF EXISTS reservation_release_consistent;

-- 三個狀態各自對應哪一個時間欄位有值，一條 constraint 講完：
--   0 = 保留中  兩個時間都是 NULL
--   1 = 已釋放  released_at 有值且不早於建立時間，consumed_at 是 NULL
--   2 = 已出庫  consumed_at 有值且不早於建立時間，released_at 是 NULL
ALTER TABLE inventory.reservation
    ADD CONSTRAINT reservation_release_consistent CHECK (
        (status = 0 AND released_at IS NULL AND consumed_at IS NULL)
        OR (status = 1
            AND released_at IS NOT NULL AND released_at >= created_at
            AND consumed_at IS NULL)
        OR (status = 2
            AND consumed_at IS NOT NULL AND consumed_at >= created_at
            AND released_at IS NULL));

COMMENT ON COLUMN inventory.reservation.consumed_at IS
    '這筆保留出庫的時間（交運，ShipmentDispatched）。'
    '與 released_at 互斥：released_at 是「訂單取消、貨還回可賣」，'
    'consumed_at 是「貨離開倉庫、quantity_on_hand 也扣掉了」，方向相反，不共用欄位。';

COMMENT ON TABLE inventory.reservation IS
    'IStockReservation 的冪等根；reservation_key 在 tenant 內唯一。'
    'status: 0=Active, 1=Released, 2=Consumed。'
    '建立 allocation 與增加 lot.quantity_reserved 必須同交易；'
    '出庫（2）與扣 lot.quantity_on_hand / quantity_reserved 也必須同交易。';

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
