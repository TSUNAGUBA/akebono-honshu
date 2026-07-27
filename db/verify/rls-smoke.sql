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
    other_tenant   UUID;   -- 実在しない値を実行時に導出する (下記 BEGIN 冒頭)
    cnt            BIGINT;
    t              TEXT;
    ref_qual       TEXT;   -- §8b: 既知の正としての brands.tenant_isolation の USING 式
    ref_check      TEXT;   -- 同 WITH CHECK 式
    probed         INT;    -- §8c: 実データで分離を実証できた勤怠テーブル数
    skipped        TEXT;   -- §8c: 行が無く実証できなかった勤怠テーブル
BEGIN
    -- 越境テストに使うセンチネルテナント ID は **実在しない値を実行時に選ぶ**。
    -- 固定値をハードコードすると、その UUID が実際に登録された DB で「他テナントの行が見える」
    -- ことになり、健全な DB で偽 FAIL する (10-attendance.sql §2 が全テナントに leave_types を
    -- 保証するため、衝突すると確実に踏む)。
    LOOP
        other_tenant := gen_random_uuid();
        EXIT WHEN NOT EXISTS (SELECT 1 FROM tenant WHERE tenant_id = other_tenant);
    END LOOP;

    -- §8b で使う参照式を先に取る (brands は §1〜§4 で分離が実証済みの代表表)
    SELECT qual, with_check INTO ref_qual, ref_check
      FROM pg_policies
     WHERE schemaname = 'public' AND tablename = 'brands' AND policyname = 'tenant_isolation';
    IF ref_qual IS NULL THEN
        RAISE EXCEPTION 'FAIL: 参照元 brands の tenant_isolation ポリシーが取得できない';
    END IF;

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

    -- 8. 記録系保護 (勤怠): punch_records の UPDATE/DELETE 権限がないこと
    --    打刻は追記のみ (訂正は source=2(Fix) の追記による論理置換) で、誤登録された打刻を
    --    削除して復旧する手段は無い。権限が復活すると記録系保護の前提が崩れるため恒久検証する。
    IF has_table_privilege('akebono_app', 'punch_records', 'UPDATE')
       OR has_table_privilege('akebono_app', 'punch_records', 'DELETE') THEN
        RAISE EXCEPTION 'FAIL: akebono_app が punch_records の UPDATE/DELETE 権限を持っている';
    END IF;
    RAISE NOTICE 'PASS: punch_records は INSERT 専用 (UPDATE/DELETE 剥奪済み)';

    -- 8b. 勤怠 6 テーブルの RLS 配線 (ポリシー + FORCE) が存在すること
    --     既存 47 表と違い、勤怠は 08-tenancy-rls.sql の「2b」が to_regclass NULL で
    --     条件付きスキップするため、init 経路では 10-attendance.sql §4 が唯一の配線経路になる。
    --     10 の投入漏れは「勤怠画面が全滅する」運用上の分岐点 (RUNBOOK §2) だが、
    --     §1〜§4 は brands 1 表を代表に使うため配線漏れを検出できない。ここで全 6 表を明示検証する。
    FOREACH t IN ARRAY ARRAY[
        'attendance_rules', 'punch_records', 'attendance_fix_requests',
        'leave_types', 'leave_grants', 'leave_requests'
    ]
    LOOP
        IF to_regclass(t) IS NULL THEN
            RAISE EXCEPTION 'FAIL: 勤怠テーブル % が存在しない (10-attendance.sql の投入漏れ)', t;
        END IF;
        IF NOT EXISTS (
            SELECT 1 FROM pg_policies
             WHERE schemaname = 'public' AND tablename = t AND policyname = 'tenant_isolation'
        ) THEN
            RAISE EXCEPTION 'FAIL: 勤怠テーブル % に tenant_isolation ポリシーが無い', t;
        END IF;
        IF NOT EXISTS (
            SELECT 1 FROM pg_class
             WHERE oid = to_regclass(t) AND relrowsecurity AND relforcerowsecurity
        ) THEN
            RAISE EXCEPTION 'FAIL: 勤怠テーブル % の ROW LEVEL SECURITY が未有効または FORCE でない', t;
        END IF;

        -- USING / WITH CHECK の中身が既存 47 表と同一式であること。
        -- 存在チェックだけでは USING (true) のような無効化を見逃す。勤怠 6 表は初期状態で
        -- ほぼ空 (leave_types のシード 1 行のみ) のため、行を使う検証 (§8c) では空の 5 表を
        -- 実証できない。式そのものを既知の正 (brands) と突き合わせて、その穴を埋める。
        IF NOT EXISTS (
            SELECT 1 FROM pg_policies p
             WHERE p.schemaname = 'public' AND p.tablename = t AND p.policyname = 'tenant_isolation'
               AND p.qual       IS NOT DISTINCT FROM ref_qual
               AND p.with_check IS NOT DISTINCT FROM ref_check
        ) THEN
            RAISE EXCEPTION 'FAIL: 勤怠テーブル % の tenant_isolation が brands と異なる式 (USING/WITH CHECK の書換または無効化)', t;
        END IF;

        -- ポリシーは permissive なら OR で合成されるため、tenant_isolation を残したまま
        -- USING (true) の別ポリシーを **追加** するだけで分離は完全に無効化できる。
        -- 式の一致だけを見ると「置換」は検出できても「追加」は見逃す。総数で塞ぐ。
        SELECT count(*) INTO cnt
          FROM pg_policies WHERE schemaname = 'public' AND tablename = t;
        IF cnt <> 1 THEN
            RAISE EXCEPTION 'FAIL: 勤怠テーブル % のポリシーが % 件ある (tenant_isolation の 1 件のみが正。USING(true) 等の追加を疑う)', t, cnt;
        END IF;
    END LOOP;
    RAISE NOTICE 'PASS: 勤怠 6 テーブルに tenant_isolation + FORCE ROW LEVEL SECURITY が配線済み (式も brands と一致)';

    -- 8c. 勤怠テーブルのテナント分離が実データで効く (フェイルクローズ含む)
    --     §8b は式の一致までしか見ない。ここは実際に行が見えないことを確認する。
    --     ただし **行が 1 件も無い表では「0 行」という結果に意味が無い** (分離を無効化しても 0 行)。
    --     そのため自テナントで行が見える表だけを実証済みとして数え、空の表は実証できていないことを
    --     NOTICE で明示する (原則4: 検証していないものを PASS と断定しない)。
    probed := 0;
    skipped := '';
    FOREACH t IN ARRAY ARRAY[
        'attendance_rules', 'punch_records', 'attendance_fix_requests',
        'leave_types', 'leave_grants', 'leave_requests'
    ]
    LOOP
        PERFORM set_config('app.tenant_id', honshu_tenant::text, TRUE);
        EXECUTE format('SELECT count(*) FROM %I', t) INTO cnt;
        -- leave_types だけは §2 のシードにより全テナントで必ず 1 行以上ある。ここが 0 なら
        -- 「空だから実証できない」ではなく **シード欠落かポリシー/GRANT の異常** なので SKIP させない
        -- (式が正しくても TO postgres 等へ付け替えるとアプリからは 0 行になり、§8b は roles を見ない)。
        IF t = 'leave_types' AND cnt = 0 THEN
            RAISE EXCEPTION 'FAIL: leave_types が自テナントで 0 行 (シード欠落、またはポリシー・GRANT の異常)';
        END IF;
        IF cnt = 0 THEN
            skipped := skipped || t || ' ';
            CONTINUE;
        END IF;

        PERFORM set_config('app.tenant_id', other_tenant::text, TRUE);
        EXECUTE format('SELECT count(*) FROM %I', t) INTO cnt;
        IF cnt <> 0 THEN
            RAISE EXCEPTION 'FAIL: 他テナントコンテキストで % が % 行見えている', t, cnt;
        END IF;

        PERFORM set_config('app.tenant_id', '', TRUE);
        EXECUTE format('SELECT count(*) FROM %I', t) INTO cnt;
        IF cnt <> 0 THEN
            RAISE EXCEPTION 'FAIL: GUC 未設定で % が % 行見えている (フェイルクローズ違反)', t, cnt;
        END IF;

        probed := probed + 1;
    END LOOP;

    IF skipped <> '' THEN
        RAISE NOTICE 'SKIP: 行が無く実データで実証できない勤怠テーブル: % (配線と式は §8b で検証済み)', rtrim(skipped);
    END IF;
    IF probed = 0 THEN
        RAISE NOTICE 'PASS(限定): 勤怠テーブルは全て空のため、分離は §8b の式一致でのみ担保 (実データ未実証)';
    ELSE
        RAISE NOTICE 'PASS: 勤怠 % テーブルで他テナント / GUC 未設定のいずれも 0 行を実データで確認 (フェイルクローズ)', probed;
    END IF;

    -- 9. accounts_receivable (security_invoker VIEW) に基底 RLS が呼出し側適用される
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
