-- プラットフォーム統合改修: テナント分離 (RLS) とアプリケーション DB ロールの配線
--
-- あけぼの SCM プラットフォーム (akebono-scm-platform) の規約に準拠:
--   - RLS 標準形: USING + WITH CHECK 両方 / FORCE ROW LEVEL SECURITY /
--     current_setting('app.tenant_id', true) / 未設定はフェイルクローズ (AKB-DOC-20)
--   - GUC は接続プール返却時に Npgsql がリセットするため、リセット後は空文字が
--     返るケースがある。NULLIF(..., '') で NULL に正規化し「NULL 比較 = 0 行」で
--     フェイルクローズを成立させる (空文字のまま ::uuid キャストするとエラーになるため)
--   - BYPASSRLS はアプリロールに与えない
--
-- 実行順: 01〜07 のテーブル作成・シード投入の後 (シードは RLS 有効化前に完了させる)。
-- 冪等: 再実行しても既存データ・権限・ポリシーを壊さない (DROP POLICY IF EXISTS → 再作成)。

SET TIMEZONE = 'UTC';

-- ─────────────────────────────────────────────────
-- 1. アプリケーション接続ロール akebono_app
--
-- docker-compose の POSTGRES_USER (akebono_honshu) はコンテナ内スーパーユーザで
-- あり RLS を素通しするため、バックエンドは必ず本ロールで接続する。
-- パスワード既定値 'localdev' はローカル開発用。本番 (RDS) では
-- deploy/db/run-migrations.sh が APP_DB_PASSWORD 環境変数から ALTER ROLE で
-- 上書きする (シークレットは Secrets Manager 管理)。
-- ─────────────────────────────────────────────────
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'akebono_app') THEN
        CREATE ROLE akebono_app LOGIN PASSWORD 'localdev' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
    END IF;
END $$;

DO $$
BEGIN
    EXECUTE format('GRANT CONNECT ON DATABASE %I TO akebono_app', current_database());
END $$;

GRANT USAGE ON SCHEMA public TO akebono_app;
-- レガシー取込 (staging_legacy_products) をアプリが実行時に作成するため CREATE を許可
GRANT CREATE ON SCHEMA public TO akebono_app;

GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO akebono_app;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO akebono_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO akebono_app;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO akebono_app;

-- 記録系の保護 (CLAUDE.md 原則2 / AKB-DOC-20 監査規約):
--   audit_logs は INSERT 専用 (追記のみ)。アプリロールから UPDATE/DELETE を剥奪。
REVOKE UPDATE, DELETE ON audit_logs FROM akebono_app;
--   tenant はレジストリ投影 (SoT = akebono-backoffice)。アプリは読取のみ。
REVOKE INSERT, UPDATE, DELETE ON tenant FROM akebono_app;

--   07-ops-data の記録系 (データ3分類) も audit_logs と同方針で追記専用化。
--   updated_at を持たない追記専用テーブル。訂正は逆仕訳/逆符号行の追記で行う
--   (各テーブルの COMMENT ON TABLE 参照)。
REVOKE UPDATE, DELETE ON payment_receipt    FROM akebono_app;
REVOKE UPDATE, DELETE ON payment_allocation FROM akebono_app;
REVOKE UPDATE, DELETE ON shipping_receipt   FROM akebono_app;
REVOKE UPDATE, DELETE ON report_output      FROM akebono_app;
REVOKE UPDATE, DELETE ON asn_transmission   FROM akebono_app;
REVOKE UPDATE, DELETE ON inventory_movement FROM akebono_app;

--   accounts_receivable は導出 VIEW (集約ビューのため元より自動更新不可だが、
--   権限面でも読取専用を明示)。
REVOKE INSERT, UPDATE, DELETE ON accounts_receivable FROM akebono_app;

-- 監査ログ月次パーティションの先行作成関数 (01-schema.sql / SECURITY DEFINER)。
-- アプリの起動時/日次ジョブが呼び出すため EXECUTE を付与する。
GRANT EXECUTE ON FUNCTION ensure_audit_log_partitions(int) TO akebono_app;

-- ─────────────────────────────────────────────────
-- 2. RLS 配線 (テナントスコープ業務テーブル 45 本)
--    (02: 19 / 03: 5 / 04: 4 / 05: 5 / 07: 12。07 は VIEW を除く 12 テーブル)
--
-- 適用除外 (理由は各テーブルの定義コメント参照):
--   - tenant      : テナントレジストリ投影 (横断参照。読取専用 GRANT で保護)
--   - users       : 認証エントリポイント (firebase_uid → tenant 解決が RLS 起動前に必要。
--                   アプリ層のグローバルクエリフィルタで分離)
--   - audit_logs  : テナント未確定イベント (認証拒否等) の記録が必要 (tenant_id NULL 許容。
--                   INSERT 専用 GRANT で保護)
--   - staging_legacy_products : レガシー取込の一次着地 (実行時作成・テナント確定前 staging)
--   - accounts_receivable : VIEW のためポリシー対象外 (RLS はテーブル専用)。
--                   security_invoker = true により基底テーブル (billing_invoice /
--                   payment_allocation / customer) の RLS が呼出し側コンテキストで効く
-- ─────────────────────────────────────────────────
DO $$
DECLARE
    t TEXT;
BEGIN
    FOREACH t IN ARRAY ARRAY[
        -- 02-masters (19)
        'sizes', 'brands', 'functions', 'countries', 'currencies', 'suppliers',
        'departments', 'product_types', 'product_seasons', 'product_groups',
        'colors', 'material_classifications', 'materials', 'warehouses',
        'delivery_destinations', 'document_template_purchases',
        'document_template_confirmations', 'document_text_purchases', 'exchange_rates',
        -- 03-products (5)
        'product_families', 'products', 'product_images', 'product_supplier_prices',
        'product_set_components',
        -- 04-orders (4)
        'purchase_orders', 'purchase_order_lines', 'purchase_order_line_deliveries',
        'purchase_order_export_logs',
        -- 05-production (5)
        'product_materials', 'production_instructions', 'production_instruction_lines',
        'material_orders', 'material_order_lines',
        -- 07-ops-data (12。accounts_receivable は VIEW のため対象外 — ヘッダの適用除外参照)
        'customer', 'sales_order', 'sales_order_line', 'billing_invoice',
        'payment_receipt', 'payment_allocation', 'shipping_receipt', 'picking_list',
        'report_output', 'asn_transmission', 'inventory_movement', 'inventory_balance'
    ]
    LOOP
        EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', t);
        EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', t);
        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I', t);
        EXECUTE format(
            'CREATE POLICY tenant_isolation ON %I '
            'USING (tenant_id = (NULLIF(current_setting(''app.tenant_id'', TRUE), ''''))::uuid) '
            'WITH CHECK (tenant_id = (NULLIF(current_setting(''app.tenant_id'', TRUE), ''''))::uuid)',
            t);
    END LOOP;
END $$;

-- 注意 (運用):
--   - FORCE ROW LEVEL SECURITY によりテーブル所有者にも RLS が適用される。
--     以後の migration スクリプトでテナントスコープ表のデータを操作する場合は
--     冒頭で SET app.tenant_id = '<uuid>' を行うこと (db/migration/README.md 参照)。
--   - スーパーユーザ (docker の POSTGRES_USER) は PostgreSQL 仕様上 RLS を常に
--     バイパスする。分離の検証は akebono_app ロールで接続して行うこと
--     (検証手順: db/verify/rls-smoke.sql)。
