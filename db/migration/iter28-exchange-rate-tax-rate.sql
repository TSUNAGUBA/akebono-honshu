-- ════════════════════════════════════════════════════════════════════
-- Iteration 28: 為替マスタ (exchange_rates) に税率 (tax_rate) 列を追加 (Part5)
-- ════════════════════════════════════════════════════════════════════
-- 背景 (なぜ必要か):
--   商品⑤仕入単価の税率を、仕入先の適用通貨 × 登録時点の年月で為替マスタから自動反映する
--   ため、為替マスタ (年月 × 通貨) に税率(%) 列を持たせる。税率は通貨(=調達国)・年月ごとに
--   異なりうるため、対円レートと同じ (年月, 通貨) キーで解決するのが自然 (壁打ち結果)。
--   ※ 税区分マスタ (tax_rates, iter27) とは別。あちらは税区分(標準/軽減/非課税)の正規管理、
--     本列は「通貨×年月で解決する仕入税率」であり用途が異なる。
--
--   db/init/02-masters.sql (exchange_rates 定義) に反映済だが、init は空 DB でのみ適用される
--   ため、既に init 済の本番 RDS には本マイグレーション (action=migrate) で追加適用する。
--
-- 冪等性 (CLAUDE.md 原則 2):
--   ADD COLUMN IF NOT EXISTS でガード。再実行しても列追加のみ。
--
-- 下位互換 (原則 7):
--   tax_rate は NULL 許容 (既定 NULL)。既存の為替レート行は税率未設定 (画面で「—」表示・
--   商品⑤では「税率未登録」フォールバック) となり、既存データ・既存 I/F を破壊しない。
--   CHECK 制約は「NULL または 0 以上」。ADD CONSTRAINT は存在チェックしてから追加する。
--
-- 適用方法 (自動・推奨): GitHub Actions「DB Init / Migrate (RDS)」を action=migrate で実行。
-- ════════════════════════════════════════════════════════════════════

BEGIN;

ALTER TABLE exchange_rates ADD COLUMN IF NOT EXISTS tax_rate NUMERIC(5,2) NULL; -- 税率(%) (Part5)

-- CHECK 制約 (tax_rate は NULL または 0 以上)。IF NOT EXISTS 相当を pg_constraint 存在確認で実現。
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint WHERE conname = 'chk_exr_tax_rate' AND conrelid = 'exchange_rates'::regclass
    ) THEN
        ALTER TABLE exchange_rates ADD CONSTRAINT chk_exr_tax_rate CHECK (tax_rate IS NULL OR tax_rate >= 0);
    END IF;
END $$;

COMMIT;
