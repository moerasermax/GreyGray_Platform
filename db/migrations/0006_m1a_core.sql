-- ============================================================================
-- 0006  M1a 核心：Identity、Catalog、Campaign、Pricing、Checkout、Ordering
--
-- 從 0003/0004 的 seam 與 hello-world 表演進，不重建 catalog.sku、
-- ordering.orders 或 iam.customer。跨模組只保存穩定 ID，不建立跨 schema FK；
-- 同一 schema 內含 tenant_id 的 parent/child 一律用複合 FK 鎖住租戶邊界。
-- 所有金額都是整數最小單位 bigint。
-- ============================================================================

\set ON_ERROR_STOP on

BEGIN;

SET ROLE greygray_owner;

-- ── Identity ───────────────────────────────────────────────────────────────
ALTER TABLE iam.customer
    ALTER COLUMN display_name TYPE varchar(50);

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'iam.customer'::regclass
          AND conname = 'customer_tenant_id_unique') THEN
        ALTER TABLE iam.customer
            ADD CONSTRAINT customer_tenant_id_unique UNIQUE (tenant_id, id);
    END IF;
END
$$;

CREATE TABLE IF NOT EXISTS iam.customer_credential (
    customer_id   uuid         PRIMARY KEY,
    tenant_id     uuid         NOT NULL,
    phone_lookup  varchar(64)  NOT NULL,
    phone_masked  varchar(10)  NOT NULL,
    password_hash varchar(256) NOT NULL,
    created_at    timestamptz  NOT NULL,

    CONSTRAINT customer_credential_customer_same_tenant_fk
        FOREIGN KEY (tenant_id, customer_id)
        REFERENCES iam.customer (tenant_id, id)
        ON DELETE CASCADE,
    CONSTRAINT customer_credential_phone_lookup_valid CHECK (
        phone_lookup ~ '^[0-9a-f]{64}$'),
    CONSTRAINT customer_credential_phone_masked_valid CHECK (
        phone_masked ~ '^09[0-9]{2}\*\*\*[0-9]{3}$'),
    CONSTRAINT customer_credential_password_hash_not_blank CHECK (
        btrim(password_hash) <> '')
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_customer_credential_tenant_phone
    ON iam.customer_credential (tenant_id, phone_lookup);

CREATE TABLE IF NOT EXISTS iam.customer_profile (
    customer_id     uuid    PRIMARY KEY,
    email_ciphertext text,
    line_linked      boolean NOT NULL DEFAULT false,

    CONSTRAINT customer_profile_customer_fk
        FOREIGN KEY (customer_id)
        REFERENCES iam.customer (id)
        ON DELETE CASCADE,
    CONSTRAINT customer_profile_email_ciphertext_not_blank CHECK (
        email_ciphertext IS NULL OR btrim(email_ciphertext) <> '')
);

CREATE TABLE IF NOT EXISTS iam.staff_account (
    id               uuid         PRIMARY KEY,
    tenant_id        uuid         NOT NULL,
    display_name     varchar(50)  NOT NULL,
    email_lookup     varchar(64)  NOT NULL,
    email_ciphertext text         NOT NULL,
    password_hash    varchar(256) NOT NULL,
    role             smallint     NOT NULL,
    is_active        boolean      NOT NULL DEFAULT true,
    created_at       timestamptz  NOT NULL,

    CONSTRAINT staff_account_id_not_empty CHECK (
        id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT staff_account_display_name_valid CHECK (
        btrim(display_name) <> '' AND char_length(display_name) <= 50),
    CONSTRAINT staff_account_email_lookup_valid CHECK (
        email_lookup ~ '^[0-9a-f]{64}$'),
    CONSTRAINT staff_account_ciphertext_not_blank CHECK (
        btrim(email_ciphertext) <> ''),
    CONSTRAINT staff_account_password_hash_not_blank CHECK (
        btrim(password_hash) <> ''),
    CONSTRAINT staff_account_role_known CHECK (role IN (0, 1, 2, 3)),
    CONSTRAINT staff_account_tenant_id_unique UNIQUE (tenant_id, id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_staff_account_tenant_email
    ON iam.staff_account (tenant_id, email_lookup);

CREATE TABLE IF NOT EXISTS iam.customer_address (
    id                         uuid        PRIMARY KEY,
    customer_id                uuid        NOT NULL,
    tenant_id                  uuid        NOT NULL,
    recipient_name_ciphertext  text        NOT NULL,
    phone_ciphertext           text        NOT NULL,
    postal_code_ciphertext     text        NOT NULL,
    city_ciphertext            text        NOT NULL,
    district_ciphertext        text        NOT NULL,
    street_address_ciphertext  text        NOT NULL,
    is_default                 boolean     NOT NULL DEFAULT false,
    created_at                 timestamptz NOT NULL,

    CONSTRAINT customer_address_customer_same_tenant_fk
        FOREIGN KEY (tenant_id, customer_id)
        REFERENCES iam.customer (tenant_id, id)
        ON DELETE CASCADE,
    CONSTRAINT customer_address_id_not_empty CHECK (
        id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT customer_address_ciphertexts_not_blank CHECK (
        btrim(recipient_name_ciphertext) <> ''
        AND btrim(phone_ciphertext) <> ''
        AND btrim(postal_code_ciphertext) <> ''
        AND btrim(city_ciphertext) <> ''
        AND btrim(district_ciphertext) <> ''
        AND btrim(street_address_ciphertext) <> '')
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_customer_address_default
    ON iam.customer_address (customer_id, is_default)
    WHERE is_default = true;
CREATE INDEX IF NOT EXISTS ix_customer_address_tenant_customer
    ON iam.customer_address (tenant_id, customer_id, created_at);

-- ── Catalog ────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS catalog.category (
    id         uuid          PRIMARY KEY,
    tenant_id  uuid          NOT NULL,
    name       varchar(50)   NOT NULL,
    image_url  varchar(2048),
    sort_order integer       NOT NULL,

    CONSTRAINT category_id_not_empty CHECK (
        id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT category_name_valid CHECK (btrim(name) <> ''),
    CONSTRAINT category_image_url_not_blank CHECK (
        image_url IS NULL OR btrim(image_url) <> ''),
    CONSTRAINT category_tenant_id_unique UNIQUE (tenant_id, id)
);

CREATE INDEX IF NOT EXISTS ix_category_tenant_sort
    ON catalog.category (tenant_id, sort_order, name);

CREATE TABLE IF NOT EXISTS catalog.product (
    id                uuid          PRIMARY KEY,
    tenant_id         uuid          NOT NULL,
    name              varchar(100)  NOT NULL,
    description       text,
    short_description varchar(100),
    category_id       uuid,
    mode              smallint      NOT NULL,
    is_active         boolean       NOT NULL DEFAULT true,
    created_at        timestamptz   NOT NULL,

    CONSTRAINT product_category_same_tenant_fk
        FOREIGN KEY (tenant_id, category_id)
        REFERENCES catalog.category (tenant_id, id)
        ON DELETE RESTRICT,
    CONSTRAINT product_id_not_empty CHECK (
        id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT product_name_valid CHECK (btrim(name) <> ''),
    CONSTRAINT product_mode_known CHECK (mode IN (0, 1)),
    CONSTRAINT product_tenant_id_unique UNIQUE (tenant_id, id)
);

CREATE INDEX IF NOT EXISTS ix_product_tenant_category_active
    ON catalog.product (tenant_id, category_id, is_active);

CREATE TABLE IF NOT EXISTS catalog.product_image (
    product_id uuid          NOT NULL,
    position   integer       NOT NULL,
    url        varchar(2048) NOT NULL,

    PRIMARY KEY (product_id, position),
    CONSTRAINT product_image_product_fk
        FOREIGN KEY (product_id)
        REFERENCES catalog.product (id)
        ON DELETE CASCADE,
    CONSTRAINT product_image_position_non_negative CHECK (position >= 0),
    CONSTRAINT product_image_url_not_blank CHECK (btrim(url) <> '')
);

ALTER TABLE catalog.sku
    ADD COLUMN IF NOT EXISTS product_id uuid,
    ADD COLUMN IF NOT EXISTS name varchar(100),
    ADD COLUMN IF NOT EXISTS variant_name varchar(50),
    ADD COLUMN IF NOT EXISTS weight_gram integer,
    ADD COLUMN IF NOT EXISTS length_cm integer,
    ADD COLUMN IF NOT EXISTS width_cm integer,
    ADD COLUMN IF NOT EXISTS height_cm integer,
    ADD COLUMN IF NOT EXISTS unit_of_measure varchar(30),
    ADD COLUMN IF NOT EXISTS unit_count integer,
    ADD COLUMN IF NOT EXISTS list_price_amount_minor bigint,
    ADD COLUMN IF NOT EXISTS list_price_currency varchar(3),
    ADD COLUMN IF NOT EXISTS is_active boolean,
    ADD COLUMN IF NOT EXISTS created_at timestamptz;

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM catalog.sku
        WHERE product_id IS NULL OR name IS NULL OR weight_gram IS NULL
           OR length_cm IS NULL OR width_cm IS NULL OR height_cm IS NULL
           OR is_active IS NULL OR created_at IS NULL) THEN
        RAISE EXCEPTION
            'catalog.sku 尚有只有 M0 seam 欄位的資料；必須先補 product/name/weight/size/active/created_at，不能捏造預設尺寸。';
    END IF;
END
$$;

ALTER TABLE catalog.sku
    ALTER COLUMN product_id SET NOT NULL,
    ALTER COLUMN name SET NOT NULL,
    ALTER COLUMN weight_gram SET NOT NULL,
    ALTER COLUMN length_cm SET NOT NULL,
    ALTER COLUMN width_cm SET NOT NULL,
    ALTER COLUMN height_cm SET NOT NULL,
    ALTER COLUMN is_active SET NOT NULL,
    ALTER COLUMN created_at SET NOT NULL;

ALTER TABLE catalog.sku
    DROP CONSTRAINT IF EXISTS sku_product_same_tenant_fk,
    DROP CONSTRAINT IF EXISTS sku_dimensions_non_negative,
    DROP CONSTRAINT IF EXISTS sku_unit_count_positive,
    DROP CONSTRAINT IF EXISTS sku_list_price_valid,
    DROP CONSTRAINT IF EXISTS sku_name_valid;
ALTER TABLE catalog.sku
    ADD CONSTRAINT sku_product_same_tenant_fk
        FOREIGN KEY (tenant_id, product_id)
        REFERENCES catalog.product (tenant_id, id)
        ON DELETE RESTRICT,
    ADD CONSTRAINT sku_dimensions_non_negative CHECK (
        weight_gram >= 0 AND length_cm >= 0 AND width_cm >= 0 AND height_cm >= 0),
    ADD CONSTRAINT sku_unit_count_positive CHECK (
        unit_count IS NULL OR unit_count > 0),
    ADD CONSTRAINT sku_list_price_valid CHECK (
        (list_price_amount_minor IS NULL AND list_price_currency IS NULL)
        OR (list_price_amount_minor IS NOT NULL
            AND list_price_currency IS NOT NULL
            AND list_price_amount_minor >= 0
            AND list_price_currency IN ('TWD', 'JPY', 'USD', 'KRW', 'EUR', 'HKD', 'CNY', 'THB', 'GBP', 'SGD'))),
    ADD CONSTRAINT sku_name_valid CHECK (btrim(name) <> '');

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'catalog.sku'::regclass
          AND conname = 'sku_tenant_id_unique') THEN
        ALTER TABLE catalog.sku
            ADD CONSTRAINT sku_tenant_id_unique UNIQUE (tenant_id, id);
    END IF;
END
$$;

CREATE INDEX IF NOT EXISTS ix_sku_tenant_product_active
    ON catalog.sku (tenant_id, product_id, is_active);

-- ── Inventory reservation ────────────────────────────────────────────────
-- 0003 已建立 lot 與 quantity_reserved/quantity_available seam。M1a 的
-- STOCK checkout 必須先建立可重放的 reservation，再於同一 transaction
-- 以鎖定的 lot 原子增加 quantity_reserved；單做 availability query 會超賣。
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'inventory.lot'::regclass
          AND conname = 'lot_tenant_id_sku_unique') THEN
        ALTER TABLE inventory.lot
            ADD CONSTRAINT lot_tenant_id_sku_unique UNIQUE (tenant_id, id, sku_id);
    END IF;
END
$$;

CREATE TABLE IF NOT EXISTS inventory.reservation (
    id               uuid          PRIMARY KEY,
    tenant_id        uuid          NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001'::uuid,
    reservation_key  varchar(255)  NOT NULL,
    status           smallint      NOT NULL DEFAULT 0,
    created_at       timestamptz   NOT NULL,
    released_at      timestamptz,

    CONSTRAINT reservation_id_not_empty CHECK (
        id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT reservation_key_not_blank CHECK (btrim(reservation_key) <> ''),
    CONSTRAINT reservation_status_known CHECK (status IN (0, 1)),
    CONSTRAINT reservation_release_consistent CHECK (
        (status = 0 AND released_at IS NULL)
        OR (status = 1 AND released_at IS NOT NULL AND released_at >= created_at)),
    CONSTRAINT reservation_tenant_id_unique UNIQUE (tenant_id, id),
    CONSTRAINT reservation_tenant_key_unique UNIQUE (tenant_id, reservation_key)
);

CREATE INDEX IF NOT EXISTS ix_reservation_tenant_active_created
    ON inventory.reservation (tenant_id, created_at)
    WHERE status = 0;

CREATE TABLE IF NOT EXISTS inventory.reservation_allocation (
    reservation_id uuid    NOT NULL,
    tenant_id      uuid    NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001'::uuid,
    lot_id         uuid    NOT NULL,
    sku_id         uuid    NOT NULL,
    quantity       integer NOT NULL,

    PRIMARY KEY (reservation_id, lot_id),
    CONSTRAINT reservation_allocation_reservation_same_tenant_fk
        FOREIGN KEY (tenant_id, reservation_id)
        REFERENCES inventory.reservation (tenant_id, id)
        ON DELETE CASCADE,
    CONSTRAINT reservation_allocation_lot_same_tenant_sku_fk
        FOREIGN KEY (tenant_id, lot_id, sku_id)
        REFERENCES inventory.lot (tenant_id, id, sku_id)
        ON DELETE RESTRICT,
    CONSTRAINT reservation_allocation_quantity_positive CHECK (quantity > 0)
);

CREATE INDEX IF NOT EXISTS ix_reservation_allocation_tenant_sku
    ON inventory.reservation_allocation (tenant_id, sku_id, reservation_id);

-- ── Campaign ───────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS campaign.campaign (
    id              uuid          PRIMARY KEY,
    tenant_id       uuid          NOT NULL,
    title           varchar(100)  NOT NULL,
    destination     varchar(50)   NOT NULL,
    depart_at       date          NOT NULL,
    return_at       date          NOT NULL,
    closes_at       timestamptz   NOT NULL,
    status          smallint      NOT NULL,
    description     text,
    cover_image_url text,
    created_at      timestamptz   NOT NULL,
    updated_at      timestamptz   NOT NULL,

    CONSTRAINT campaign_id_not_empty CHECK (
        id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT campaign_title_valid CHECK (btrim(title) <> ''),
    CONSTRAINT campaign_destination_valid CHECK (btrim(destination) <> ''),
    CONSTRAINT campaign_dates_valid CHECK (return_at >= depart_at),
    CONSTRAINT campaign_status_known CHECK (status IN (0, 1, 2, 3, 4, 5, 9)),
    CONSTRAINT campaign_updated_after_created CHECK (updated_at >= created_at),
    CONSTRAINT campaign_tenant_id_unique UNIQUE (tenant_id, id)
);

CREATE INDEX IF NOT EXISTS ix_campaign_tenant_status_created
    ON campaign.campaign (tenant_id, status, created_at DESC);

CREATE TABLE IF NOT EXISTS campaign.campaign_offer (
    id                           uuid        PRIMARY KEY,
    campaign_id                  uuid        NOT NULL,
    sku_id                       uuid        NOT NULL,
    selling_price_minor          bigint      NOT NULL,
    selling_price_currency       smallint    NOT NULL,
    target_purchase_price_minor  bigint,
    target_purchase_price_currency smallint,
    is_active                    boolean     NOT NULL DEFAULT true,
    created_at                   timestamptz NOT NULL,

    CONSTRAINT campaign_offer_campaign_fk
        FOREIGN KEY (campaign_id)
        REFERENCES campaign.campaign (id)
        ON DELETE CASCADE,
    CONSTRAINT campaign_offer_id_not_empty CHECK (
        id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT campaign_offer_sku_id_not_empty CHECK (
        sku_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT campaign_offer_selling_price_valid CHECK (
        selling_price_minor >= 0
        AND selling_price_currency IN (901, 392, 840, 410, 978, 344, 156, 764, 826, 702)),
    CONSTRAINT campaign_offer_target_price_valid CHECK (
        (target_purchase_price_minor IS NULL AND target_purchase_price_currency IS NULL)
        OR (target_purchase_price_minor IS NOT NULL
            AND target_purchase_price_currency IS NOT NULL
            AND target_purchase_price_minor >= 0
            AND target_purchase_price_currency = selling_price_currency)),
    CONSTRAINT uq_campaign_offer_campaign_sku UNIQUE (campaign_id, sku_id)
);

-- ── Pricing ────────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS pricing.pricing_snapshot (
    id                       uuid        PRIMARY KEY,
    tenant_id                uuid        NOT NULL,
    delivery_method          smallint    NOT NULL,
    actual_weight_gram       integer     NOT NULL,
    volumetric_weight_gram   integer     NOT NULL,
    billable_weight_gram     integer     NOT NULL,
    shipping_fee_minor       bigint      NOT NULL,
    shipping_fee_currency    smallint    NOT NULL,
    applied_rule_set_id      uuid        NOT NULL,
    applied_rule_id          uuid,
    applied_strategy         smallint    NOT NULL,
    explain                  jsonb       NOT NULL,
    created_at               timestamptz NOT NULL,

    CONSTRAINT pricing_snapshot_id_not_empty CHECK (
        id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT pricing_snapshot_delivery_method_known CHECK (delivery_method IN (1, 2, 3)),
    CONSTRAINT pricing_snapshot_weights_valid CHECK (
        actual_weight_gram >= 0
        AND volumetric_weight_gram >= 0
        AND billable_weight_gram >= actual_weight_gram
        AND billable_weight_gram >= volumetric_weight_gram),
    CONSTRAINT pricing_snapshot_shipping_fee_valid CHECK (
        shipping_fee_minor >= 0
        AND shipping_fee_currency IN (901, 392, 840, 410, 978, 344, 156, 764, 826, 702)),
    CONSTRAINT pricing_snapshot_strategy_known CHECK (applied_strategy IN (1, 2, 3)),
    CONSTRAINT pricing_snapshot_explain_array CHECK (jsonb_typeof(explain) = 'array'),
    CONSTRAINT pricing_snapshot_tenant_id_unique UNIQUE (tenant_id, id)
);

CREATE INDEX IF NOT EXISTS ix_pricing_snapshot_tenant_created
    ON pricing.pricing_snapshot (tenant_id, created_at DESC);

-- ── Checkout ───────────────────────────────────────────────────────────────
CREATE TABLE IF NOT EXISTS checkout.cart (
    id                       uuid          PRIMARY KEY,
    tenant_id                uuid          NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001'::uuid,
    customer_id              uuid,
    shipping_policy          smallint      NOT NULL,
    created_at               timestamptz   NOT NULL,
    updated_at               timestamptz   NOT NULL,
    completed_event_id       uuid,
    completed_at             timestamptz,
    checkout_idempotency_key varchar(255),
    pricing_snapshot_id      uuid,
    delivery_method          smallint,
    shipping_address_id      uuid,
    convenience_store_code   varchar(50),
    buyer_note               varchar(200),

    CONSTRAINT cart_id_not_empty CHECK (
        id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT cart_shipping_policy_known CHECK (shipping_policy IN (1, 2)),
    CONSTRAINT cart_delivery_method_known CHECK (
        delivery_method IS NULL OR delivery_method IN (1, 2, 3)),
    CONSTRAINT cart_updated_after_created CHECK (updated_at >= created_at),
    CONSTRAINT cart_completion_consistent CHECK (
        (completed_event_id IS NULL
            AND completed_at IS NULL
            AND checkout_idempotency_key IS NULL
            AND pricing_snapshot_id IS NULL
            AND delivery_method IS NULL)
        OR (completed_event_id IS NOT NULL
            AND completed_at IS NOT NULL
            AND customer_id IS NOT NULL
            AND checkout_idempotency_key IS NOT NULL
            AND btrim(checkout_idempotency_key) <> ''
            AND pricing_snapshot_id IS NOT NULL
            AND delivery_method IS NOT NULL)),
    CONSTRAINT cart_tenant_id_unique UNIQUE (tenant_id, id)
);

CREATE INDEX IF NOT EXISTS ix_cart_tenant_customer_updated
    ON checkout.cart (tenant_id, customer_id, updated_at);
CREATE UNIQUE INDEX IF NOT EXISTS ux_cart_tenant_checkout_key
    ON checkout.cart (tenant_id, checkout_idempotency_key)
    WHERE checkout_idempotency_key IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS ux_cart_completed_event
    ON checkout.cart (completed_event_id)
    WHERE completed_event_id IS NOT NULL;

CREATE TABLE IF NOT EXISTS checkout.cart_line (
    id                      uuid          PRIMARY KEY,
    cart_id                 uuid          NOT NULL,
    tenant_id               uuid          NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001'::uuid,
    sku_id                  uuid          NOT NULL,
    product_id              uuid          NOT NULL,
    name                    varchar(100)  NOT NULL,
    variant_name            varchar(50),
    fulfillment_mode        smallint      NOT NULL,
    campaign_id             uuid,
    campaign_offer_id       uuid,
    quantity                integer       NOT NULL,
    unit_price_amount_minor bigint        NOT NULL,
    unit_price_currency     varchar(3)    NOT NULL,

    CONSTRAINT cart_line_cart_same_tenant_fk
        FOREIGN KEY (tenant_id, cart_id)
        REFERENCES checkout.cart (tenant_id, id)
        ON DELETE CASCADE,
    CONSTRAINT cart_line_id_not_empty CHECK (
        id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT cart_line_name_valid CHECK (btrim(name) <> ''),
    CONSTRAINT cart_line_mode_known CHECK (fulfillment_mode IN (0, 1)),
    CONSTRAINT cart_line_campaign_consistent CHECK (
        (fulfillment_mode = 0 AND campaign_id IS NULL AND campaign_offer_id IS NULL)
        OR (fulfillment_mode = 1 AND campaign_id IS NOT NULL AND campaign_offer_id IS NOT NULL)),
    CONSTRAINT ck_cart_line_quantity CHECK (quantity BETWEEN 1 AND 999),
    CONSTRAINT ck_cart_line_unit_price CHECK (unit_price_amount_minor >= 0),
    CONSTRAINT cart_line_currency_known CHECK (
        unit_price_currency IN ('TWD', 'JPY', 'USD', 'KRW', 'EUR', 'HKD', 'CNY', 'THB', 'GBP', 'SGD'))
);

CREATE INDEX IF NOT EXISTS ix_cart_line_tenant_cart
    ON checkout.cart_line (tenant_id, cart_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_cart_line_identity
    ON checkout.cart_line (
        tenant_id, cart_id, sku_id, fulfillment_mode, campaign_offer_id)
    NULLS NOT DISTINCT;

-- ── Ordering：演進 0003 ordering.orders ──────────────────────────────────
ALTER TABLE ordering.orders
    ADD COLUMN IF NOT EXISTS checkout_event_id uuid,
    ADD COLUMN IF NOT EXISTS checkout_cart_id uuid,
    ADD COLUMN IF NOT EXISTS checkout_idempotency_key varchar(255),
    ADD COLUMN IF NOT EXISTS order_number varchar(32),
    ADD COLUMN IF NOT EXISTS customer_id uuid,
    ADD COLUMN IF NOT EXISTS status smallint,
    ADD COLUMN IF NOT EXISTS shipping_policy smallint,
    ADD COLUMN IF NOT EXISTS delivery_method smallint,
    ADD COLUMN IF NOT EXISTS shipping_address_id uuid,
    ADD COLUMN IF NOT EXISTS convenience_store_code varchar(50),
    ADD COLUMN IF NOT EXISTS buyer_note varchar(200),
    ADD COLUMN IF NOT EXISTS pricing_snapshot_id uuid,
    ADD COLUMN IF NOT EXISTS goods_total_amount_minor bigint,
    ADD COLUMN IF NOT EXISTS goods_total_currency varchar(3),
    ADD COLUMN IF NOT EXISTS shipping_fee_amount_minor bigint,
    ADD COLUMN IF NOT EXISTS shipping_fee_currency varchar(3),
    ADD COLUMN IF NOT EXISTS grand_total_amount_minor bigint,
    ADD COLUMN IF NOT EXISTS grand_total_currency varchar(3),
    ADD COLUMN IF NOT EXISTS paid_amount_minor bigint,
    ADD COLUMN IF NOT EXISTS paid_currency varchar(3),
    ADD COLUMN IF NOT EXISTS refunded_amount_minor bigint DEFAULT 0,
    ADD COLUMN IF NOT EXISTS refunded_currency varchar(3),
    ADD COLUMN IF NOT EXISTS quote_explain jsonb,
    ADD COLUMN IF NOT EXISTS placed_at timestamptz,
    ADD COLUMN IF NOT EXISTS payment_due_at timestamptz,
    ADD COLUMN IF NOT EXISTS cancelled_at timestamptz,
    ADD COLUMN IF NOT EXISTS cancellation_reason varchar(200),
    ADD COLUMN IF NOT EXISTS last_payment_failure_code varchar(100);

DO $$
BEGIN
    IF EXISTS (
        SELECT 1 FROM ordering.orders
        WHERE checkout_event_id IS NULL OR checkout_cart_id IS NULL
           OR checkout_idempotency_key IS NULL OR order_number IS NULL
           OR customer_id IS NULL OR status IS NULL OR shipping_policy IS NULL
           OR delivery_method IS NULL OR pricing_snapshot_id IS NULL
           OR goods_total_amount_minor IS NULL OR goods_total_currency IS NULL
           OR shipping_fee_amount_minor IS NULL OR shipping_fee_currency IS NULL
           OR grand_total_amount_minor IS NULL OR grand_total_currency IS NULL
           OR refunded_amount_minor IS NULL OR quote_explain IS NULL OR placed_at IS NULL) THEN
        RAISE EXCEPTION
            'ordering.orders 尚有只有 M0 seam 欄位的資料；必須先補 checkout/order/amount snapshot，不能捏造歷史訂單。';
    END IF;
END
$$;

ALTER TABLE ordering.orders
    ALTER COLUMN checkout_event_id SET NOT NULL,
    ALTER COLUMN checkout_cart_id SET NOT NULL,
    ALTER COLUMN checkout_idempotency_key SET NOT NULL,
    ALTER COLUMN order_number SET NOT NULL,
    ALTER COLUMN customer_id SET NOT NULL,
    ALTER COLUMN status SET NOT NULL,
    ALTER COLUMN shipping_policy SET NOT NULL,
    ALTER COLUMN delivery_method SET NOT NULL,
    ALTER COLUMN pricing_snapshot_id SET NOT NULL,
    ALTER COLUMN goods_total_amount_minor SET NOT NULL,
    ALTER COLUMN goods_total_currency SET NOT NULL,
    ALTER COLUMN shipping_fee_amount_minor SET NOT NULL,
    ALTER COLUMN shipping_fee_currency SET NOT NULL,
    ALTER COLUMN grand_total_amount_minor SET NOT NULL,
    ALTER COLUMN grand_total_currency SET NOT NULL,
    ALTER COLUMN refunded_amount_minor SET NOT NULL,
    ALTER COLUMN quote_explain SET NOT NULL,
    ALTER COLUMN placed_at SET NOT NULL;

ALTER TABLE ordering.orders
    DROP CONSTRAINT IF EXISTS ck_orders_totals_non_negative,
    DROP CONSTRAINT IF EXISTS ck_orders_paid_non_negative,
    DROP CONSTRAINT IF EXISTS ck_orders_refunded_non_negative,
    DROP CONSTRAINT IF EXISTS orders_id_not_empty,
    DROP CONSTRAINT IF EXISTS orders_status_known,
    DROP CONSTRAINT IF EXISTS orders_shipping_policy_known,
    DROP CONSTRAINT IF EXISTS orders_delivery_method_known,
    DROP CONSTRAINT IF EXISTS orders_currency_consistent,
    DROP CONSTRAINT IF EXISTS orders_paid_consistent,
    DROP CONSTRAINT IF EXISTS orders_refunded_consistent,
    DROP CONSTRAINT IF EXISTS orders_quote_explain_array;
ALTER TABLE ordering.orders
    ADD CONSTRAINT orders_id_not_empty CHECK (
        id <> '00000000-0000-0000-0000-000000000000'::uuid),
    ADD CONSTRAINT orders_status_known CHECK (status IN (0, 1, 2, 3, 4, 5, 6, 7, 9)),
    ADD CONSTRAINT orders_shipping_policy_known CHECK (shipping_policy IN (1, 2)),
    ADD CONSTRAINT orders_delivery_method_known CHECK (delivery_method IN (1, 2, 3)),
    ADD CONSTRAINT ck_orders_totals_non_negative CHECK (
        goods_total_amount_minor >= 0
        AND shipping_fee_amount_minor >= 0
        AND grand_total_amount_minor >= 0
        AND grand_total_amount_minor = goods_total_amount_minor + shipping_fee_amount_minor),
    ADD CONSTRAINT orders_currency_consistent CHECK (
        goods_total_currency = shipping_fee_currency
        AND goods_total_currency = grand_total_currency
        AND goods_total_currency IN ('TWD', 'JPY', 'USD', 'KRW', 'EUR', 'HKD', 'CNY', 'THB', 'GBP', 'SGD')),
    ADD CONSTRAINT ck_orders_paid_non_negative CHECK (
        paid_amount_minor IS NULL OR paid_amount_minor >= 0),
    ADD CONSTRAINT orders_paid_consistent CHECK (
        (paid_amount_minor IS NULL AND paid_currency IS NULL)
        OR (paid_amount_minor IS NOT NULL
            AND paid_currency IS NOT NULL
            AND paid_amount_minor >= 0
            AND paid_currency = grand_total_currency)),
    ADD CONSTRAINT ck_orders_refunded_non_negative CHECK (refunded_amount_minor >= 0),
    ADD CONSTRAINT orders_refunded_consistent CHECK (
        (refunded_amount_minor = 0 AND refunded_currency IS NULL)
        OR (refunded_amount_minor > 0
            AND refunded_currency IS NOT NULL
            AND refunded_currency = grand_total_currency)),
    ADD CONSTRAINT orders_quote_explain_array CHECK (jsonb_typeof(quote_explain) = 'array');

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE conrelid = 'ordering.orders'::regclass
          AND conname = 'orders_tenant_id_unique') THEN
        ALTER TABLE ordering.orders
            ADD CONSTRAINT orders_tenant_id_unique UNIQUE (tenant_id, id);
    END IF;
END
$$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_orders_tenant_checkout_cart
    ON ordering.orders (tenant_id, checkout_cart_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_orders_checkout_event
    ON ordering.orders (checkout_event_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_orders_tenant_checkout_key
    ON ordering.orders (tenant_id, checkout_idempotency_key);
CREATE UNIQUE INDEX IF NOT EXISTS ux_orders_tenant_number
    ON ordering.orders (tenant_id, order_number);
CREATE INDEX IF NOT EXISTS ix_orders_tenant_customer_placed
    ON ordering.orders (tenant_id, customer_id, placed_at);
CREATE INDEX IF NOT EXISTS ix_orders_tenant_status_placed
    ON ordering.orders (tenant_id, status, placed_at);

CREATE TABLE IF NOT EXISTS ordering.order_line (
    id                       uuid        PRIMARY KEY,
    order_id                 uuid        NOT NULL,
    tenant_id                uuid        NOT NULL DEFAULT '00000000-0000-0000-0000-000000000001'::uuid,
    sku_id                   uuid        NOT NULL,
    fulfillment_mode         smallint    NOT NULL,
    status                   smallint    NOT NULL,
    quantity                 integer     NOT NULL,
    unit_price_amount_minor  bigint      NOT NULL,
    unit_price_currency      varchar(3)  NOT NULL,
    campaign_id              uuid,
    campaign_offer_id        uuid,
    consumed_lot_id          uuid,
    refunded_amount_minor    bigint,
    refunded_currency        varchar(3),

    CONSTRAINT order_line_order_same_tenant_fk
        FOREIGN KEY (tenant_id, order_id)
        REFERENCES ordering.orders (tenant_id, id)
        ON DELETE CASCADE,
    CONSTRAINT order_line_id_not_empty CHECK (
        id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT order_line_mode_known CHECK (fulfillment_mode IN (0, 1)),
    CONSTRAINT order_line_status_known CHECK (status IN (0, 1, 2, 3, 4, 5, 9)),
    CONSTRAINT ck_order_line_quantity CHECK (quantity BETWEEN 1 AND 999),
    CONSTRAINT ck_order_line_unit_price CHECK (unit_price_amount_minor >= 0),
    CONSTRAINT order_line_currency_known CHECK (
        unit_price_currency IN ('TWD', 'JPY', 'USD', 'KRW', 'EUR', 'HKD', 'CNY', 'THB', 'GBP', 'SGD')),
    CONSTRAINT order_line_campaign_consistent CHECK (
        (fulfillment_mode = 0 AND campaign_id IS NULL AND campaign_offer_id IS NULL)
        OR (fulfillment_mode = 1 AND campaign_id IS NOT NULL AND campaign_offer_id IS NOT NULL)),
    CONSTRAINT order_line_refund_consistent CHECK (
        (refunded_amount_minor IS NULL AND refunded_currency IS NULL)
        OR (refunded_amount_minor IS NOT NULL
            AND refunded_currency IS NOT NULL
            AND refunded_amount_minor >= 0
            AND refunded_currency = unit_price_currency))
);

CREATE INDEX IF NOT EXISTS ix_order_line_tenant_order
    ON ordering.order_line (tenant_id, order_id);
CREATE INDEX IF NOT EXISTS ix_order_line_tenant_campaign_order
    ON ordering.order_line (tenant_id, campaign_id, order_id);

-- ── 註解與 owner 斷言 ────────────────────────────────────────────────────
COMMENT ON COLUMN iam.customer_credential.password_hash IS
    '只允許 one-way salted password hash；不得存明文或可逆密碼。';
COMMENT ON COLUMN catalog.sku.weight_gram IS
    'M1a 起必填；與 length/width/height 一起為日後材積重計價保留真實資料。';
COMMENT ON TABLE inventory.reservation IS
    'IStockReservation 的冪等根；reservation_key 在 tenant 內唯一。status: 0=Active, 1=Released。建立 allocation 與增加 lot.quantity_reserved 必須同交易。';
COMMENT ON TABLE inventory.reservation_allocation IS
    '實際 lot 配額；複合 FK 同時鎖定 tenant 與 SKU，Release 時據此扣回 quantity_reserved。';
COMMENT ON TABLE pricing.pricing_snapshot IS
    '下單時凍結的不可變報價快照；所有 amount 都是 bigint 最小單位。';
COMMENT ON TABLE checkout.cart IS
    '購物車完成事實與 CheckoutCompleted outbox 由同一 CheckoutDbContext SaveChanges 寫入。';
COMMENT ON TABLE ordering.orders IS
    '由 CheckoutCompleted 冪等建單；金額與報價說明是成立當下的不可變快照。';

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
