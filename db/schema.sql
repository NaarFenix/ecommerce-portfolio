-- ==================================================
-- E-Commerce Portfolio App — Schema (PostgreSQL 16+)
-- Authoritative source of truth. Do not modify.
-- ==================================================

CREATE EXTENSION IF NOT EXISTS pgcrypto; -- for gen_random_uuid()

-- ==================================================
-- CATEGORIES
-- ==================================================
CREATE TABLE categories (
    id           BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name         VARCHAR(120) NOT NULL,
    slug         VARCHAR(140) NOT NULL UNIQUE,
    description  TEXT,
    is_active    BOOLEAN NOT NULL DEFAULT TRUE,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ==================================================
-- PRODUCTS
-- ==================================================
CREATE TABLE products (
    id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    category_id   BIGINT NOT NULL REFERENCES categories(id) ON DELETE RESTRICT,
    sku           VARCHAR(64) NOT NULL UNIQUE,
    name          VARCHAR(200) NOT NULL,
    slug          VARCHAR(220) NOT NULL UNIQUE,
    description   TEXT NOT NULL DEFAULT '',
    price         NUMERIC(10,2) NOT NULL CHECK (price >= 0),
    compare_at_price NUMERIC(10,2) CHECK (compare_at_price IS NULL OR compare_at_price >= 0),
    stock         INTEGER NOT NULL DEFAULT 0 CHECK (stock >= 0),
    is_active     BOOLEAN NOT NULL DEFAULT TRUE,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_products_category ON products(category_id);
CREATE INDEX idx_products_active ON products(is_active) WHERE is_active = TRUE;
CREATE INDEX idx_products_search ON products USING GIN (to_tsvector('english', name || ' ' || description));

-- ==================================================
-- PRODUCT IMAGES
-- ==================================================
CREATE TABLE product_images (
    id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    product_id  BIGINT NOT NULL REFERENCES products(id) ON DELETE CASCADE,
    url         VARCHAR(500) NOT NULL,
    alt_text    VARCHAR(200) NOT NULL DEFAULT '',
    sort_order  INTEGER NOT NULL DEFAULT 0,
    is_primary  BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE INDEX idx_product_images_product ON product_images(product_id);

-- ==================================================
-- ADMIN USERS
-- ==================================================
CREATE TABLE admin_users (
    id                  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    username            VARCHAR(64) NOT NULL UNIQUE,
    email               VARCHAR(256) NOT NULL UNIQUE,
    password_hash       VARCHAR(500) NOT NULL,
    totp_secret         VARCHAR(200),
    is_2fa_enabled      BOOLEAN NOT NULL DEFAULT FALSE,
    role                VARCHAR(32) NOT NULL DEFAULT 'Admin',
    is_active           BOOLEAN NOT NULL DEFAULT TRUE,
    failed_login_count  INTEGER NOT NULL DEFAULT 0,
    locked_until        TIMESTAMPTZ,
    last_login_at       TIMESTAMPTZ,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ==================================================
-- CUSTOMERS
-- ==================================================
CREATE TABLE customers (
    id              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    email           VARCHAR(256) NOT NULL UNIQUE,
    password_hash   VARCHAR(500),
    full_name       VARCHAR(200) NOT NULL,
    phone           VARCHAR(32),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- ==================================================
-- CARTS
-- ==================================================
CREATE TABLE carts (
    id             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    customer_id    BIGINT REFERENCES customers(id) ON DELETE SET NULL,
    session_token  UUID NOT NULL DEFAULT gen_random_uuid(),
    created_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX idx_carts_session_token ON carts(session_token);
CREATE INDEX idx_carts_customer ON carts(customer_id);

CREATE TABLE cart_items (
    id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    cart_id     BIGINT NOT NULL REFERENCES carts(id) ON DELETE CASCADE,
    product_id  BIGINT NOT NULL REFERENCES products(id) ON DELETE CASCADE,
    quantity    INTEGER NOT NULL CHECK (quantity > 0),
    UNIQUE (cart_id, product_id)
);

-- ==================================================
-- ORDERS
-- ==================================================
CREATE TABLE orders (
    id               BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    order_number     VARCHAR(20) NOT NULL UNIQUE,
    customer_id      BIGINT REFERENCES customers(id) ON DELETE SET NULL,
    email            VARCHAR(256) NOT NULL,
    full_name        VARCHAR(200) NOT NULL,
    phone            VARCHAR(32),
    shipping_address VARCHAR(300) NOT NULL,
    city             VARCHAR(120) NOT NULL,
    postal_code      VARCHAR(20),
    country          VARCHAR(80) NOT NULL,
    subtotal         NUMERIC(10,2) NOT NULL,
    shipping_fee     NUMERIC(10,2) NOT NULL DEFAULT 0,
    total            NUMERIC(10,2) NOT NULL,
    status           VARCHAR(20) NOT NULL DEFAULT 'pending'
                       CHECK (status IN ('pending','paid','processing','shipped','delivered','cancelled','refunded')),
    created_at       TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_orders_status ON orders(status);
CREATE INDEX idx_orders_customer ON orders(customer_id);

CREATE TABLE order_items (
    id                    BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    order_id              BIGINT NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    product_id            BIGINT REFERENCES products(id) ON DELETE SET NULL,
    product_name_snapshot VARCHAR(200) NOT NULL,
    unit_price_snapshot   NUMERIC(10,2) NOT NULL,
    quantity              INTEGER NOT NULL CHECK (quantity > 0),
    line_total            NUMERIC(10,2) NOT NULL
);

CREATE INDEX idx_order_items_order ON order_items(order_id);

-- ==================================================
-- PAYMENTS
-- ==================================================
CREATE TABLE payments (
    id                  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    order_id            BIGINT NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    provider            VARCHAR(30) NOT NULL DEFAULT 'stripe',
    provider_payment_id VARCHAR(200) NOT NULL,
    amount              NUMERIC(10,2) NOT NULL,
    currency            VARCHAR(3) NOT NULL DEFAULT 'USD',
    status              VARCHAR(20) NOT NULL DEFAULT 'pending'
                          CHECK (status IN ('pending','succeeded','failed','refunded')),
    raw_response_json   JSONB,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX idx_payments_provider_id ON payments(provider, provider_payment_id);
CREATE INDEX idx_payments_order ON payments(order_id);

-- ==================================================
-- AUDIT LOG
-- ==================================================
CREATE TABLE audit_logs (
    id             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    admin_user_id  BIGINT REFERENCES admin_users(id) ON DELETE SET NULL,
    action         VARCHAR(80) NOT NULL,
    entity_type    VARCHAR(60),
    entity_id      BIGINT,
    details_json   JSONB,
    ip_address     VARCHAR(64),
    created_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_audit_logs_admin ON audit_logs(admin_user_id);
CREATE INDEX idx_audit_logs_created ON audit_logs(created_at);