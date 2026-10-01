-- ============================================================================
-- 0005  M1a 綠界付款與雙式帳本
--
-- 演進 0003 預留的 payment.payment / ledger.account，不重建歷史接縫。
-- Ledger 三條不變式下沉 DB：同租戶、借貸平衡、posted 後不可改。
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

SET ROLE greygray_owner;

-- ── Payment ────────────────────────────────────────────────────────────────
ALTER TABLE payment.payment
    ADD COLUMN IF NOT EXISTS order_id uuid,
    ADD COLUMN IF NOT EXISTS status smallint,
    ADD COLUMN IF NOT EXISTS goods_amount_minor bigint,
    ADD COLUMN IF NOT EXISTS shipping_amount_minor bigint,
    ADD COLUMN IF NOT EXISTS fee_amount_minor bigint,
    ADD COLUMN IF NOT EXISTS merchant_trade_no text,
    ADD COLUMN IF NOT EXISTS provider_transaction_id text,
    ADD COLUMN IF NOT EXISTS created_at timestamptz,
    ADD COLUMN IF NOT EXISTS expires_at timestamptz,
    ADD COLUMN IF NOT EXISTS captured_at timestamptz,
    ADD COLUMN IF NOT EXISTS settled_at timestamptz;

DO $$
DECLARE
    incomplete_count bigint;
BEGIN
    SELECT count(*)
    INTO incomplete_count
    FROM payment.payment
    WHERE order_id IS NULL
       OR status IS NULL
       OR goods_amount_minor IS NULL
       OR shipping_amount_minor IS NULL
       OR merchant_trade_no IS NULL
       OR created_at IS NULL
       OR expires_at IS NULL;

    IF incomplete_count > 0 THEN
        RAISE EXCEPTION
            'payment.payment 有 % 筆舊 seam 資料缺少 M1a 必填付款快照；無法安全推導 order/status/amount/trade/date，請先人工補齊再重跑 0005。',
            incomplete_count;
    END IF;
END
$$;

ALTER TABLE payment.payment
    ALTER COLUMN order_id SET NOT NULL,
    ALTER COLUMN status SET NOT NULL,
    ALTER COLUMN goods_amount_minor SET NOT NULL,
    ALTER COLUMN shipping_amount_minor SET NOT NULL,
    ALTER COLUMN merchant_trade_no SET NOT NULL,
    ALTER COLUMN created_at SET NOT NULL,
    ALTER COLUMN expires_at SET NOT NULL;

ALTER TABLE payment.payment
    DROP CONSTRAINT IF EXISTS payment_status_known,
    DROP CONSTRAINT IF EXISTS payment_amounts_valid,
    DROP CONSTRAINT IF EXISTS payment_merchant_trade_no_valid,
    DROP CONSTRAINT IF EXISTS payment_provider_transaction_id_valid,
    DROP CONSTRAINT IF EXISTS payment_dates_valid;

ALTER TABLE payment.payment
    ADD CONSTRAINT payment_status_known
        CHECK (status IN (0, 1, 2, 3, 4, 5)),
    ADD CONSTRAINT payment_amounts_valid
        CHECK (goods_amount_minor >= 0
            AND shipping_amount_minor >= 0
            AND goods_amount_minor + shipping_amount_minor > 0
            AND (fee_amount_minor IS NULL OR fee_amount_minor >= 0)),
    ADD CONSTRAINT payment_merchant_trade_no_valid
        CHECK (btrim(merchant_trade_no) <> '' AND char_length(merchant_trade_no) <= 20),
    ADD CONSTRAINT payment_provider_transaction_id_valid
        CHECK (provider_transaction_id IS NULL OR char_length(provider_transaction_id) <= 32),
    ADD CONSTRAINT payment_dates_valid
        CHECK (expires_at > created_at);

CREATE UNIQUE INDEX IF NOT EXISTS ux_payment_merchant_trade_no
    ON payment.payment (merchant_trade_no);
CREATE INDEX IF NOT EXISTS ix_payment_tenant_order
    ON payment.payment (tenant_id, order_id);

DO $$
DECLARE
    duplicate_key text;
BEGIN
    SELECT format('%s/%s', tenant_id, order_id)
    INTO duplicate_key
    FROM payment.payment
    WHERE status IN (0, 1, 4)
    GROUP BY tenant_id, order_id
    HAVING count(*) > 1
    LIMIT 1;

    IF duplicate_key IS NOT NULL THEN
        RAISE EXCEPTION
            'payment.payment 已存在同 tenant/order 的多筆 active payment（%）；請先人工對帳收斂，0005 不會自行判定哪筆有效。',
            duplicate_key;
    END IF;
END
$$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_payment_tenant_order_active
    ON payment.payment (tenant_id, order_id)
    WHERE status IN (0, 1, 4);

COMMENT ON COLUMN payment.payment.merchant_trade_no IS
    '綠界 MerchantTradeNo，最多 20 字元且永不重用；回呼以此找付款並去重。';
COMMENT ON COLUMN payment.payment.provider_transaction_id IS
    '綠界 TradeNo；成功回呼後保存，重送必須帶相同值。';

-- ── Ledger 科目表：把 0003 的自然鍵演進成穩定 AccountId ────────────────
ALTER TABLE ledger.account
    ADD COLUMN IF NOT EXISTS id uuid,
    ADD COLUMN IF NOT EXISTS type smallint;

INSERT INTO ledger.account (tenant_id, code, name, category, is_active, id, type)
VALUES
    ('00000000-0000-0000-0000-000000000001', '1100', '現金／銀行存款', 'ASSET', true, '10000000-0000-0000-0000-000000001100', 1),
    ('00000000-0000-0000-0000-000000000001', '1151', '綠界在途',       'ASSET', true, '10000000-0000-0000-0000-000000001151', 1),
    ('00000000-0000-0000-0000-000000000001', '1152', '藍新在途',       'ASSET', false,'10000000-0000-0000-0000-000000001152', 1),
    ('00000000-0000-0000-0000-000000000001', '1153', 'LINE Pay 在途',  'ASSET', false,'10000000-0000-0000-0000-000000001153', 1),
    ('00000000-0000-0000-0000-000000000001', '1200', '通路應收帳款',    'ASSET', false,'10000000-0000-0000-0000-000000001200', 1),
    ('00000000-0000-0000-0000-000000000001', '1300', '存貨',           'ASSET', true, '10000000-0000-0000-0000-000000001300', 1),
    ('00000000-0000-0000-0000-000000000001', '2110', '預收貨款',        'LIABILITY', true, '10000000-0000-0000-0000-000000002110', 2),
    ('00000000-0000-0000-0000-000000000001', '2120', '預收運費',        'LIABILITY', true, '10000000-0000-0000-0000-000000002120', 2),
    ('00000000-0000-0000-0000-000000000001', '2130', '客戶儲值金',      'LIABILITY', true, '10000000-0000-0000-0000-000000002130', 2),
    ('00000000-0000-0000-0000-000000000001', '4100', '銷貨收入',        'REVENUE', true, '10000000-0000-0000-0000-000000004100', 3),
    ('00000000-0000-0000-0000-000000000001', '4200', '運費收入',        'REVENUE', true, '10000000-0000-0000-0000-000000004200', 3),
    ('00000000-0000-0000-0000-000000000001', '5100', '銷貨成本',        'EXPENSE', true, '10000000-0000-0000-0000-000000005100', 4),
    ('00000000-0000-0000-0000-000000000001', '5200', '運費成本',        'EXPENSE', true, '10000000-0000-0000-0000-000000005200', 4),
    ('00000000-0000-0000-0000-000000000001', '5300', '旅程成本',        'EXPENSE', true, '10000000-0000-0000-0000-000000005300', 4),
    ('00000000-0000-0000-0000-000000000001', '5400', '金流手續費',      'EXPENSE', true, '10000000-0000-0000-0000-000000005400', 4)
ON CONFLICT (tenant_id, code) DO UPDATE SET
    id = EXCLUDED.id,
    type = EXCLUDED.type,
    name = EXCLUDED.name;

ALTER TABLE ledger.account
    ALTER COLUMN id SET NOT NULL,
    ALTER COLUMN type SET NOT NULL;

DO $$
DECLARE
    primary_columns text;
BEGIN
    SELECT pg_get_constraintdef(oid)
    INTO primary_columns
    FROM pg_constraint
    WHERE conrelid = 'ledger.account'::regclass AND contype = 'p';

    IF primary_columns LIKE '%tenant_id, code%' THEN
        ALTER TABLE ledger.account DROP CONSTRAINT account_pkey;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ledger.account'::regclass AND contype = 'p') THEN
        ALTER TABLE ledger.account ADD CONSTRAINT account_pkey PRIMARY KEY (id);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ledger.account'::regclass
          AND conname = 'account_tenant_id_unique') THEN
        ALTER TABLE ledger.account
            ADD CONSTRAINT account_tenant_id_unique UNIQUE (tenant_id, id);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ledger.account'::regclass
          AND conname = 'account_tenant_code_unique') THEN
        ALTER TABLE ledger.account
            ADD CONSTRAINT account_tenant_code_unique UNIQUE (tenant_id, code);
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ledger.account'::regclass
          AND conname = 'account_type_known') THEN
        ALTER TABLE ledger.account
            ADD CONSTRAINT account_type_known CHECK (type IN (1, 2, 3, 4, 5));
    END IF;
END
$$;

-- ── 不可變雙式分錄 ───────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS ledger.journal_entry (
    id            uuid        PRIMARY KEY,
    tenant_id     uuid        NOT NULL,
    occurred_at   timestamptz NOT NULL,
    posted_at     timestamptz NOT NULL,
    source_module text        NOT NULL,
    source_ref    text        NOT NULL,
    memo          text        NOT NULL,
    is_posted     boolean     NOT NULL DEFAULT false,

    CONSTRAINT journal_entry_id_not_empty CHECK (
        id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT journal_source_valid CHECK (
        btrim(source_module) <> '' AND char_length(source_module) <= 64
        AND btrim(source_ref) <> '' AND char_length(source_ref) <= 128),
    CONSTRAINT journal_memo_valid CHECK (char_length(memo) <= 500),
    CONSTRAINT journal_entry_tenant_id_unique UNIQUE (tenant_id, id),
    CONSTRAINT journal_entry_source_unique UNIQUE (tenant_id, source_module, source_ref)
);

CREATE INDEX IF NOT EXISTS ix_journal_tenant_posted
    ON ledger.journal_entry (tenant_id, posted_at DESC, id DESC);

CREATE TABLE IF NOT EXISTS ledger.journal_line (
    id             uuid     PRIMARY KEY,
    tenant_id      uuid     NOT NULL,
    entry_id       uuid     NOT NULL,
    account_id     uuid     NOT NULL,
    account_code   text     NOT NULL,
    direction      smallint NOT NULL,
    amount_minor   bigint   NOT NULL,
    currency       text     NOT NULL,
    campaign_id    uuid,
    customer_id    uuid,

    CONSTRAINT journal_line_entry_same_tenant_fk
        FOREIGN KEY (tenant_id, entry_id)
        REFERENCES ledger.journal_entry (tenant_id, id)
        ON DELETE RESTRICT,
    CONSTRAINT journal_line_account_same_tenant_fk
        FOREIGN KEY (tenant_id, account_id)
        REFERENCES ledger.account (tenant_id, id)
        ON DELETE RESTRICT,
    CONSTRAINT journal_line_id_not_empty CHECK (
        id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT journal_line_direction_known CHECK (direction IN (1, 2)),
    CONSTRAINT journal_line_amount_positive CHECK (amount_minor > 0),
    CONSTRAINT journal_line_currency_m1a_twd CHECK (currency = 'TWD'),
    CONSTRAINT journal_line_account_code_valid CHECK (btrim(account_code) <> '')
);

CREATE INDEX IF NOT EXISTS ix_journal_line_entry
    ON ledger.journal_line (tenant_id, entry_id);
CREATE INDEX IF NOT EXISTS ix_journal_line_campaign_account
    ON ledger.journal_line (campaign_id, account_code);
CREATE INDEX IF NOT EXISTS ix_journal_line_customer_account
    ON ledger.journal_line (customer_id, account_code);

ALTER TABLE ledger.journal_entry
    ADD COLUMN IF NOT EXISTS is_posted boolean NOT NULL DEFAULT false;

-- 舊版 0005 只擋 UPDATE/DELETE，先移除舊 trigger，驗證既有資料後再封存。
DROP TRIGGER IF EXISTS journal_entry_immutable ON ledger.journal_entry;
DROP TRIGGER IF EXISTS journal_line_immutable ON ledger.journal_line;
DROP TRIGGER IF EXISTS journal_line_balanced ON ledger.journal_line;
DROP TRIGGER IF EXISTS journal_entry_finalize ON ledger.journal_entry;

DO $$
DECLARE
    invalid_entry uuid;
BEGIN
    SELECT entry.id
    INTO invalid_entry
    FROM ledger.journal_entry entry
    LEFT JOIN ledger.journal_line line ON line.entry_id = entry.id
    GROUP BY entry.id
    HAVING count(line.id) < 2
    LIMIT 1;

    IF invalid_entry IS NOT NULL THEN
        RAISE EXCEPTION
            'ledger.journal_entry % 少於兩條 line；既有資料不符合雙式分錄，無法安全封存。',
            invalid_entry;
    END IF;

    SELECT totals.entry_id
    INTO invalid_entry
    FROM (
        SELECT entry_id,
               currency,
               sum(amount_minor) FILTER (WHERE direction = 1) AS debit_total,
               sum(amount_minor) FILTER (WHERE direction = 2) AS credit_total
        FROM ledger.journal_line
        GROUP BY entry_id, currency
    ) totals
    WHERE totals.debit_total IS DISTINCT FROM totals.credit_total
    LIMIT 1;

    IF invalid_entry IS NOT NULL THEN
        RAISE EXCEPTION
            'ledger.journal_entry % 借貸不平衡；既有資料無法安全封存。',
            invalid_entry;
    END IF;

    UPDATE ledger.journal_entry
    SET is_posted = true
    WHERE NOT is_posted;
END
$$;

CREATE OR REPLACE FUNCTION ledger.reject_posted_journal_mutation()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    target_entry uuid;
    parent_posted boolean;
BEGIN
    IF TG_TABLE_NAME = 'journal_entry' THEN
        IF OLD.is_posted THEN
            RAISE EXCEPTION 'ledger journal 已封存；更正只能建立反向分錄';
        END IF;

        IF TG_OP = 'DELETE' THEN
            RETURN OLD;
        END IF;

        RETURN NEW;
    END IF;

    target_entry := CASE WHEN TG_OP = 'INSERT' THEN NEW.entry_id ELSE OLD.entry_id END;
    SELECT is_posted
    INTO parent_posted
    FROM ledger.journal_entry
    WHERE id = target_entry;

    IF parent_posted THEN
        RAISE EXCEPTION 'ledger journal % 已封存；不可追加、修改或刪除 line', target_entry;
    END IF;

    IF TG_OP = 'DELETE' THEN
        RETURN OLD;
    END IF;

    RETURN NEW;
END
$$;

DROP TRIGGER IF EXISTS journal_entry_immutable ON ledger.journal_entry;
CREATE TRIGGER journal_entry_immutable
    BEFORE UPDATE OR DELETE ON ledger.journal_entry
    FOR EACH ROW EXECUTE FUNCTION ledger.reject_posted_journal_mutation();
CREATE TRIGGER journal_line_immutable
    BEFORE INSERT OR UPDATE OR DELETE ON ledger.journal_line
    FOR EACH ROW EXECUTE FUNCTION ledger.reject_posted_journal_mutation();

DROP FUNCTION IF EXISTS ledger.assert_journal_balanced();

CREATE OR REPLACE FUNCTION ledger.finalize_journal_entry()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    line_count integer;
    imbalance text;
BEGIN
    SELECT count(*) INTO line_count
    FROM ledger.journal_line
    WHERE entry_id = NEW.id;

    IF line_count < 2 THEN
        RAISE EXCEPTION 'ledger journal % 至少需要兩條 line', NEW.id;
    END IF;

    SELECT string_agg(
        format('%s DR=%s CR=%s', currency, debit_total, credit_total), ', ')
    INTO imbalance
    FROM (
        SELECT currency,
               sum(amount_minor) FILTER (WHERE direction = 1) AS debit_total,
               sum(amount_minor) FILTER (WHERE direction = 2) AS credit_total
        FROM ledger.journal_line
        WHERE entry_id = NEW.id
        GROUP BY currency
    ) totals
    WHERE debit_total IS DISTINCT FROM credit_total;

    IF imbalance IS NOT NULL THEN
        RAISE EXCEPTION 'ledger journal % 借貸不平衡：%', NEW.id, imbalance;
    END IF;

    UPDATE ledger.journal_entry
    SET is_posted = true
    WHERE id = NEW.id AND NOT is_posted;

    RETURN NULL;
END
$$;

CREATE CONSTRAINT TRIGGER journal_entry_finalize
    AFTER INSERT ON ledger.journal_entry
    DEFERRABLE INITIALLY DEFERRED
    FOR EACH ROW EXECUTE FUNCTION ledger.finalize_journal_entry();

COMMENT ON TABLE ledger.journal_entry IS
    'posted 即不可變；更正只能開反向分錄。source_module + source_ref 可追回事實來源。';
COMMENT ON COLUMN ledger.journal_entry.is_posted IS
    '同一交易建立 entry+lines，deferred trigger 驗證至少兩行且逐幣平衡後封存；封存後 line 不可追加或異動。';
COMMENT ON CONSTRAINT journal_line_account_same_tenant_fk ON ledger.journal_line IS
    '禁止把某租戶的分錄掛到另一租戶的科目。';

RESET ROLE;

DO $$
DECLARE
    wrong_owner text;
BEGIN
    SELECT string_agg(format('%s.%s (owner=%s)', schemaname, tablename, tableowner), ', ')
    INTO wrong_owner
    FROM pg_tables
    WHERE schemaname IN (
        'iam', 'catalog', 'campaign', 'pricing', 'inventory', 'checkout', 'ordering',
        'procurement', 'fulfillment', 'payment', 'ledger', 'notify', 'audit', 'reporting',
        'platform'
    )
      AND tableowner <> 'greygray_owner';

    IF wrong_owner IS NOT NULL THEN
        RAISE EXCEPTION
            'Table owner 不是 greygray_owner：%。migration 開頭少了 SET ROLE greygray_owner。',
            wrong_owner;
    END IF;
END
$$;

COMMIT;
