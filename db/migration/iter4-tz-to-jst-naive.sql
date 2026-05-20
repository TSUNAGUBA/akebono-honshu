-- ════════════════════════════════════════════════════════════════════
-- Iter 4 段階 B 後続: TIMESTAMPTZ → TIMESTAMP (without time zone) 移行
-- ════════════════════════════════════════════════════════════════════
-- 目的: pgAdmin 表示で +09:00 オフセットを廃し、生の JST naive datetime で
--       保存・参照する設計に統一する (Phase 5 §6.1 改訂)。
-- 対象: public スキーマ内の全 timestamp with time zone カラムを動的に走査し、
--       Asia/Tokyo に変換した naive 値に in-place ALTER COLUMN する。
-- 実行: pgAdmin4 で本ファイルを 1 回だけ実行。新規 docker 立ち上げの場合は
--       既に db/init/*.sql の TIMESTAMPTZ → TIMESTAMP に置換済のため本ファイル不要。
-- 冪等: 1 回適用後は対象カラムが残らないため再実行しても LOOP は no-op。
-- 注意: ALTER DATABASE は同一 DB に接続中でも実行可能だが、適用は次回接続から。
--       本トランザクション内では SET TIMEZONE で同等のセッション設定を行う。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

-- セッション内 timezone を明示 (USING 句の AT TIME ZONE と暗黙 NOW() の基準を JST に固定)
SET TIMEZONE = 'Asia/Tokyo';

-- 全 timestamp with time zone カラムを動的に列挙して TIMESTAMP に変換。
-- USING 句で 'AT TIME ZONE Asia/Tokyo' を適用し、UTC で格納されていた値を
-- JST naive datetime に変換する (例: 2026-05-20 00:01:12+00 → 2026-05-20 09:01:12)。
DO $$
DECLARE
    r record;
BEGIN
    FOR r IN
        SELECT table_name, column_name
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND data_type = 'timestamp with time zone'
        ORDER BY table_name, column_name
    LOOP
        RAISE NOTICE 'Converting %.% : TIMESTAMPTZ → TIMESTAMP (Asia/Tokyo)', r.table_name, r.column_name;
        EXECUTE format(
            'ALTER TABLE %I ALTER COLUMN %I TYPE TIMESTAMP USING %I AT TIME ZONE ''Asia/Tokyo''',
            r.table_name, r.column_name, r.column_name);
    END LOOP;
END $$;

COMMIT;

-- DB レベルの timezone を永続化 (次回以降の接続全てに適用)。
-- ALTER DATABASE はトランザクション外で実行する必要があるため COMMIT 後に置く。
ALTER DATABASE akebono_honshu SET timezone TO 'Asia/Tokyo';

-- 検証クエリ (実行不要、参考):
--   SELECT table_name, column_name, data_type
--   FROM information_schema.columns
--   WHERE table_schema='public' AND data_type LIKE 'timestamp%';
-- → data_type が全て 'timestamp without time zone' になっていれば成功
