-- ============================================================================
-- 0003  五個通路擴充接縫
--
-- 只建立 M0-7 要求、日後有歷史資料就難以回填的五個 schema seam。
-- 不在這裡提前實作 M1 的訂單、商品、庫存、付款或總帳聚合。
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

-- 0001 的 default privileges 是 FOR ROLE greygray_owner；少了這行，模組 role 會零權限。
SET ROLE greygray_owner;

-- ── 1. 訂單來源通路 ────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS ordering.orders (
    id             uuid     PRIMARY KEY,
    tenant_id      uuid     NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001'::uuid,
    source_channel smallint NOT NULL DEFAULT 0,

    -- M1 只有 OWN=0。M4 接外部通路時由新 migration 擴充此 constraint，不能先偷放行。
    CONSTRAINT orders_source_channel_m0_own_only CHECK (source_channel = 0)
);

CREATE INDEX IF NOT EXISTS ix_orders_tenant_source
    ON ordering.orders (tenant_id, source_channel);

COMMENT ON COLUMN ordering.orders.source_channel IS
    'Ordering.Contracts SourceChannel 的穩定數值；M0/M1 只允許 OWN=0。'
    '欄位現在就存在，避免外部訂單進場後才回填歷史來源。';


-- ── 2. SKU 的穩定內部 ID ───────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS catalog.sku (
    -- 由應用程式用 SkuId.New() 產生 UUIDv7；不可拿商品編號、條碼、名稱或外部 ID 當主鍵。
    id        uuid PRIMARY KEY,
    tenant_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001'::uuid,

    CONSTRAINT sku_id_not_empty CHECK (id <> '00000000-0000-0000-0000-000000000000'::uuid)
);

CREATE INDEX IF NOT EXISTS ix_sku_tenant
    ON catalog.sku (tenant_id, id);

COMMENT ON COLUMN catalog.sku.id IS
    '一經產生不可變的內部 SkuId；外部通路識別只能另存 mapping，不得取代主鍵。';


-- ── 3. Ledger 預留通路應收帳款 1200 ───────────────────────────────────
CREATE TABLE IF NOT EXISTS ledger.account (
    tenant_id uuid    NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001'::uuid,
    code      text    NOT NULL,
    name      text    NOT NULL,
    category  text    NOT NULL,
    is_active boolean NOT NULL DEFAULT false,

    PRIMARY KEY (tenant_id, code),
    CONSTRAINT account_code_not_blank CHECK (btrim(code) <> ''),
    CONSTRAINT account_name_not_blank CHECK (btrim(name) <> ''),
    CONSTRAINT account_category_known CHECK (
        category IN ('ASSET', 'LIABILITY', 'EQUITY', 'REVENUE', 'EXPENSE'))
);

INSERT INTO ledger.account (tenant_id, code, name, category, is_active)
VALUES (
    '00000000-0000-0000-0000-000000000001'::uuid,
    '1200',
    '通路應收帳款',
    'ASSET',
    false)
ON CONFLICT (tenant_id, code) DO NOTHING;

COMMENT ON TABLE ledger.account IS
    'M0 只初始化通路擴充需要的科目接縫；完整科目表與分錄規則屬於 M1。';
COMMENT ON COLUMN ledger.account.code IS
    '必須與 Ledger.Contracts.AccountCodes 一致；1200 = ChannelReceivable。';


-- ── 4. 可用量 = 總量 − 保留 − 通路配額 ─────────────────────────────────
CREATE TABLE IF NOT EXISTS inventory.lot (
    id                         uuid PRIMARY KEY,
    tenant_id                  uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001'::uuid,
    sku_id                     uuid NOT NULL,
    quantity_on_hand           int  NOT NULL DEFAULT 0,
    quantity_reserved          int  NOT NULL DEFAULT 0,
    quantity_channel_allocated int  NOT NULL DEFAULT 0,
    quantity_available         int  GENERATED ALWAYS AS (
        quantity_on_hand - quantity_reserved - quantity_channel_allocated
    ) STORED,

    CONSTRAINT lot_id_not_empty CHECK (id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT lot_sku_id_not_empty CHECK (sku_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT lot_quantities_non_negative CHECK (
        quantity_on_hand >= 0
        AND quantity_reserved >= 0
        AND quantity_channel_allocated >= 0),
    -- M5 開啟多通路配額時，由新 migration 拆掉這條並保留下方總量上限。
    CONSTRAINT lot_channel_allocation_m0_zero CHECK (quantity_channel_allocated = 0),
    CONSTRAINT lot_allocations_within_on_hand CHECK (
        quantity_reserved + quantity_channel_allocated <= quantity_on_hand)
);

CREATE INDEX IF NOT EXISTS ix_lot_tenant_sku
    ON inventory.lot (tenant_id, sku_id);

COMMENT ON COLUMN inventory.lot.quantity_channel_allocated IS
    'M1a-M4 一律為 0；M5 多通路配額啟用後才會是非零。';
COMMENT ON COLUMN inventory.lot.quantity_available IS
    '永遠由 DB 計算：on_hand - reserved - channel_allocated，避免通路進場後超賣。';


-- ── 5. PaymentProvider 預留 ExternalSettled=9 ──────────────────────────
CREATE TABLE IF NOT EXISTS payment.payment (
    id        uuid     PRIMARY KEY,
    tenant_id uuid     NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001'::uuid,
    provider  smallint NOT NULL,

    -- 數值與 Payment.Contracts.PaymentProvider 固定對齊。
    CONSTRAINT payment_provider_known CHECK (provider IN (1, 2, 3, 9)),
    CONSTRAINT payment_id_not_empty CHECK (id <> '00000000-0000-0000-0000-000000000000'::uuid)
);

CREATE INDEX IF NOT EXISTS ix_payment_tenant_provider
    ON payment.payment (tenant_id, provider);

COMMENT ON COLUMN payment.payment.provider IS
    '1=ECPay、2=NewebPay、3=LinePay、9=ExternalSettled；9 代表外部通路已代收結算。';


RESET ROLE;

-- ── Owner 斷言：忘記 SET ROLE 時必須在同一個 transaction 內整包失敗 ────
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
            'Table owner 不是 greygray_owner：%。ALTER DEFAULT PRIVILEGES 不會套到這些表上，'
            '模組 role 會拿不到任何權限。migration 開頭少了 SET ROLE greygray_owner。',
            wrong_owner;
    END IF;
END
$$;

COMMIT;
