-- ============================================================================
-- 09-updated-at-triggers.sql — updated_at DB トリガの一括配線
-- ----------------------------------------------------------------------------
-- 目的: updated_at 列の更新をアプリ任せにせず DB 側で強制する (プラットフォーム
--       標準 AKB-DOC-13)。共通トリガ関数 set_updated_at() は 01-schema.sql で定義済。
--       本ファイルは information_schema を走査し、public スキーマの updated_at 列を
--       持つ全 BASE TABLE (VIEW は除外) へ trg_<table>_set_updated_at
--       (BEFORE UPDATE FOR EACH ROW) を冪等に配線する。
--       テーブルを列挙しない汎用ループのため、07-ops-data.sql / 08-tenancy-rls.sql
--       など他ファイルで追加されるテーブルも (本ファイルが最後に実行される限り)
--       自動的にカバーされる。新テーブル追加時の配線漏れを構造的に防ぐ。
-- 冪等性: DROP TRIGGER IF EXISTS → CREATE TRIGGER のため再実行安全 (原則2)。
--         トリガは設定系オブジェクトであり、再作成しても記録系データは巻き戻らない。
-- プラットフォーム統合 第二段階: uuid PK / deleted_at 統一 / 監査パーティション
-- ============================================================================

DO $$
DECLARE
    r RECORD;
BEGIN
    FOR r IN
        SELECT c.table_name
        FROM information_schema.columns c
        JOIN information_schema.tables t
          ON  t.table_schema = c.table_schema
          AND t.table_name   = c.table_name
        WHERE c.table_schema = 'public'
          AND c.column_name  = 'updated_at'
          AND t.table_type   = 'BASE TABLE'   -- VIEW 除外
        ORDER BY c.table_name
    LOOP
        EXECUTE format('DROP TRIGGER IF EXISTS %I ON public.%I',
                       'trg_' || r.table_name || '_set_updated_at', r.table_name);
        EXECUTE format('CREATE TRIGGER %I BEFORE UPDATE ON public.%I FOR EACH ROW EXECUTE FUNCTION set_updated_at()',
                       'trg_' || r.table_name || '_set_updated_at', r.table_name);
    END LOOP;
END $$;
