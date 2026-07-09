-- RLS スモークテスト (プラットフォーム統合改修の検証用)
--
-- 実行方法 (akebono_app ロールで接続すること。スーパーユーザは RLS をバイパスするため不可):
--   psql "host=localhost dbname=akebono_honshu user=akebono_app password=localdev" -f db/verify/rls-smoke.sql
--
-- 期待結果: 最後に「RLS smoke test: ALL PASSED」が表示される。
-- 途中で EXCEPTION が発生した場合は RLS 配線に問題がある。

\set ON_ERROR_STOP 1

DO $$
DECLARE
    honshu_tenant  UUID := '00000000-0000-4000-8000-000000000001';
    other_tenant   UUID := '00000000-0000-4000-8000-000000000002';
    cnt            BIGINT;
    t              TEXT;
BEGIN
    -- 前提: シード済みの brands にはデータが存在する
    PERFORM set_config('app.tenant_id', honshu_tenant::text, TRUE);
    SELECT count(*) INTO cnt FROM brands;
    IF cnt = 0 THEN
        RAISE EXCEPTION 'FAIL: 自テナントのコンテキストでシード行が見えない (RLS ポリシー過剰)';
    END IF;
    RAISE NOTICE 'PASS: 自テナントコンテキストで brands % 行が見える', cnt;

    -- 1. GUC 未設定 (空文字) → 0 行 (フェイルクローズ)
    PERFORM set_config('app.tenant_id', '', TRUE);
    SELECT count(*) INTO cnt FROM brands;
    IF cnt <> 0 THEN
        RAISE EXCEPTION 'FAIL: GUC 空文字で % 行見えた (フェイルクローズ違反)', cnt;
    END IF;
    RAISE NOTICE 'PASS: GUC 空文字で 0 行 (フェイルクローズ)';

    -- 2. 他テナントのコンテキスト → 0 行
    PERFORM set_config('app.tenant_id', other_tenant::text, TRUE);
    SELECT count(*) INTO cnt FROM brands;
    IF cnt <> 0 THEN
        RAISE EXCEPTION 'FAIL: 他テナントコンテキストで % 行見えた (越境)', cnt;
    END IF;
    RAISE NOTICE 'PASS: 他テナントコンテキストで 0 行';

    -- 3. 他テナント ID を偽装した INSERT → WITH CHECK 違反で拒否される
    PERFORM set_config('app.tenant_id', honshu_tenant::text, TRUE);
    BEGIN
        INSERT INTO brands (tenant_id, code, name, created_by_user_id, updated_by_user_id)
        VALUES (other_tenant, '998', 'RLS違反テスト',
                (SELECT id FROM users WHERE login_id = 'owner'),
                (SELECT id FROM users WHERE login_id = 'owner'));
        RAISE EXCEPTION 'FAIL: 他テナント tenant_id の INSERT が成功してしまった (WITH CHECK 違反)';
    EXCEPTION
        WHEN insufficient_privilege OR check_violation THEN
            RAISE NOTICE 'PASS: 他テナント tenant_id の INSERT は拒否された (%)', SQLERRM;
    END;

    -- 4. GUC 未設定での INSERT (DEFAULT 経由) → NOT NULL 違反で拒否される
    --    (users.id は UUID のため監査 FK は users から引く。users は RLS 適用除外)
    PERFORM set_config('app.tenant_id', '', TRUE);
    BEGIN
        INSERT INTO brands (code, name, created_by_user_id, updated_by_user_id)
        VALUES ('997', 'フェイルクローズテスト',
                (SELECT id FROM users WHERE login_id = 'owner'),
                (SELECT id FROM users WHERE login_id = 'owner'));
        RAISE EXCEPTION 'FAIL: GUC 未設定の INSERT が成功してしまった';
    EXCEPTION
        WHEN not_null_violation OR insufficient_privilege OR check_violation THEN
            RAISE NOTICE 'PASS: GUC 未設定の INSERT は拒否された (%)', SQLERRM;
    END;

    -- 5. 記録系保護: audit_logs の UPDATE/DELETE 権限がないこと
    IF has_table_privilege('akebono_app', 'audit_logs', 'UPDATE')
       OR has_table_privilege('akebono_app', 'audit_logs', 'DELETE') THEN
        RAISE EXCEPTION 'FAIL: akebono_app が audit_logs の UPDATE/DELETE 権限を持っている';
    END IF;
    RAISE NOTICE 'PASS: audit_logs は INSERT 専用 (UPDATE/DELETE 剥奪済み)';

    -- 6. tenant レジストリ投影は読取専用
    IF has_table_privilege('akebono_app', 'tenant', 'INSERT')
       OR has_table_privilege('akebono_app', 'tenant', 'UPDATE')
       OR has_table_privilege('akebono_app', 'tenant', 'DELETE') THEN
        RAISE EXCEPTION 'FAIL: akebono_app が tenant への書込権限を持っている';
    END IF;
    RAISE NOTICE 'PASS: tenant はアプリロールから読取専用';

    -- 7. 07-ops-data の記録系テーブルは追記専用 (UPDATE/DELETE 剥奪済み)
    FOREACH t IN ARRAY ARRAY[
        'payment_receipt', 'payment_allocation', 'shipping_receipt',
        'report_output', 'asn_transmission', 'inventory_movement'
    ]
    LOOP
        IF has_table_privilege('akebono_app', t, 'UPDATE')
           OR has_table_privilege('akebono_app', t, 'DELETE') THEN
            RAISE EXCEPTION 'FAIL: akebono_app が記録系 % の UPDATE/DELETE 権限を持っている', t;
        END IF;
    END LOOP;
    RAISE NOTICE 'PASS: 記録系 6 テーブルは INSERT 専用 (UPDATE/DELETE 剥奪済み)';

    -- 8. accounts_receivable (security_invoker VIEW) に基底 RLS が呼出し側適用される
    PERFORM set_config('app.tenant_id', honshu_tenant::text, TRUE);
    SELECT count(*) INTO cnt FROM accounts_receivable;
    IF cnt = 0 THEN
        RAISE EXCEPTION 'FAIL: 自テナントコンテキストで accounts_receivable が 0 行 (シード欠落または RLS 過剰)';
    END IF;
    RAISE NOTICE 'PASS: 自テナントコンテキストで accounts_receivable % 行が見える', cnt;

    PERFORM set_config('app.tenant_id', '', TRUE);
    SELECT count(*) INTO cnt FROM accounts_receivable;
    IF cnt <> 0 THEN
        RAISE EXCEPTION 'FAIL: GUC 空文字で accounts_receivable が % 行見えた (VIEW 経由のフェイルクローズ違反)', cnt;
    END IF;
    RAISE NOTICE 'PASS: GUC 空文字で accounts_receivable は 0 行 (基底 RLS が VIEW 越しに有効)';

    RAISE NOTICE 'RLS smoke test: ALL PASSED';
END $$;
