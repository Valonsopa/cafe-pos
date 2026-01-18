PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;

CREATE TABLE IF NOT EXISTS users (
  id TEXT PRIMARY KEY,
  username TEXT NOT NULL UNIQUE,
  display_name TEXT NOT NULL,
  role TEXT NOT NULL,
  pin_hash TEXT,
  password_hash TEXT,
  is_active INTEGER NOT NULL DEFAULT 1,
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL
);

CREATE TABLE IF NOT EXISTS tables (
  id TEXT PRIMARY KEY,
  label TEXT NOT NULL UNIQUE,
  is_active INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE IF NOT EXISTS orders (
  id TEXT PRIMARY KEY,
  order_number INTEGER NOT NULL UNIQUE,
  status TEXT NOT NULL,
  source TEXT NOT NULL,
  table_id TEXT,
  ticket_label TEXT,
  opened_by_user_id TEXT NOT NULL,
  opened_at INTEGER NOT NULL,
  closed_at INTEGER,
  notes TEXT,
  subtotal_cents INTEGER NOT NULL DEFAULT 0,
  tax_cents INTEGER NOT NULL DEFAULT 0,
  discount_cents INTEGER NOT NULL DEFAULT 0,
  total_cents INTEGER NOT NULL DEFAULT 0,
  paid_cents INTEGER NOT NULL DEFAULT 0,
  balance_cents INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_orders_status_opened ON orders(status, opened_at DESC);
CREATE INDEX IF NOT EXISTS idx_orders_table_status ON orders(table_id, status);

CREATE TABLE IF NOT EXISTS order_items (
  id TEXT PRIMARY KEY,
  order_id TEXT NOT NULL,
  menu_item_name TEXT NOT NULL,
  unit_price_cents INTEGER NOT NULL,
  qty INTEGER NOT NULL,
  tax_rate_bps INTEGER NOT NULL DEFAULT 0,
  note TEXT,
  status TEXT NOT NULL,
  created_by_user_id TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  voided_at INTEGER,
  voided_by_user_id TEXT,
  void_reason TEXT
);

CREATE INDEX IF NOT EXISTS idx_order_items_order ON order_items(order_id, created_at);

CREATE TABLE IF NOT EXISTS payments (
  id TEXT PRIMARY KEY,
  order_id TEXT NOT NULL,
  method TEXT NOT NULL,
  amount_cents INTEGER NOT NULL,
  tip_cents INTEGER NOT NULL DEFAULT 0,
  status TEXT NOT NULL,
  created_by_user_id TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  external_ref TEXT,
  reason TEXT
);

CREATE INDEX IF NOT EXISTS idx_payments_order ON payments(order_id, created_at);

CREATE TABLE IF NOT EXISTS receipts (
  id TEXT PRIMARY KEY,
  order_id TEXT NOT NULL,
  type TEXT NOT NULL,
  printer_name TEXT,
  content_text TEXT,
  created_at INTEGER NOT NULL,
  created_by_user_id TEXT NOT NULL,
  printed_at INTEGER,
  print_status TEXT NOT NULL,
  fail_reason TEXT,
  reprint_of_receipt_id TEXT
);

CREATE INDEX IF NOT EXISTS idx_receipts_order_type ON receipts(order_id, type, created_at DESC);

CREATE TABLE IF NOT EXISTS audit_log (
  id TEXT PRIMARY KEY,
  entity_type TEXT NOT NULL,
  entity_id TEXT NOT NULL,
  action TEXT NOT NULL,
  actor_user_id TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  details_json TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_audit_entity ON audit_log(entity_type, entity_id, created_at DESC);

CREATE TABLE IF NOT EXISTS idempotency_keys (
  key TEXT PRIMARY KEY,
  device_id TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  request_hash TEXT NOT NULL,
  response_json TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_idempotency_device_time ON idempotency_keys(device_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_orders_id_status ON orders(id, status);
