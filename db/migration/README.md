# MIG-3: 既存生産管理システム CSV 取込 (実行手順)

> **目的:** Phase 7 Iteration 4 Hardening の MIG-3 タスク。
> 既存生産管理システム 商品マスタ CSV (1,288 行) を新システム DB に取込む。
>
> **戦略:** `docs/migration/mig-3-strategy.md` を先に読むこと。設計判断 5 件
> (旧 SKU 保持 / 原価単価採用 / planned_year='Z' / 仮値割当 / 商品分類は legacy 保持) は
> ユーザ承認済み (2026-05-20)。

## 前提条件

1. **CSV ファイル準備** (機密情報含むため git 管理外):
   - 元: SHIFT_JIS の `products.csv` (138 列、1,289 行 = ヘッダ + 1,288 データ行)
   - 変換: `iconv -f SHIFT_JIS -t UTF-8 products.csv > /tmp/legacy_products_utf8.csv`

2. **PostgreSQL 接続情報:**
   - Iteration 1 で構築した DB (`akebono` database、`postgres` ユーザ)
   - psql コマンドラインアクセスが必要 (`\copy` は pgAdmin4 Query Tool で実行不可)

3. **マスタ Seed 完了:** Iteration 1 の `02-masters.sql` 適用済

## 実行手順 (オペレーター作業)

### Step 0: バックアップ (推奨)

```bash
pg_dump -h localhost -U postgres -d akebono \
  -t product_families -t products -t product_supplier_prices \
  -t colors -t sizes -t suppliers \
  > backup_before_mig3_$(date +%Y%m%d_%H%M%S).sql
```

### Step 1: DB スキーマ拡張

```bash
psql -h localhost -U postgres -d akebono \
  -f db/migration/mig-3-pre-patch.sql
```

`products.sku` を VARCHAR(11) → VARCHAR(16) に拡張。冪等。

### Step 2: マスタ補完

```bash
psql -h localhost -U postgres -d akebono \
  -f db/migration/mig-3-step-01-master-fill.sql
```

- カラー: 旧 31 種を `L11`, `L40` 等の code で追加
- サイズ: 既存 5 種は `legacy_id` 紐付けのみ、新規 6 種追加
- 仕入先: 旧 11 種を追加 (既存 3 種は `legacy_id` 紐付け)

**期待結果:**
```
 master    | total | with_legacy
-----------+-------+-------------
 colors    |    35 |          31
 sizes     |    11 |          10
 suppliers |    11 |          11
```

### Step 3: Staging テーブル + CSV 取込

```bash
# CSV を psql 実行マシンから見える場所に配置 (例: /tmp/)
cp /path/to/legacy_products_utf8.csv /tmp/

psql -h localhost -U postgres -d akebono \
  -f db/migration/mig-3-step-02-staging.sql
```

**期待結果:**
```
 total_rows | family_count | rows_with_cost
------------+--------------+----------------
       1288 |          686 |            816
```

### Step 4: 本テーブル取込

```bash
psql -h localhost -U postgres -d akebono \
  -f db/migration/mig-3-step-03-import.sql
```

**期待結果 (NOTICE 出力):**
```
NOTICE:  ─── MIG-3 取込結果 ──────────────────────────
NOTICE:    product_families        : 686 件
NOTICE:    products                : 1288 件
NOTICE:    product_supplier_prices : 約 686 件 (family ごとに 1 単価)
```

### Step 5: UI 確認

1. Frontend `/products` に「年式: Z」フィルタで旧データ 686 件を確認
2. `status=Draft` フィルタで未確定品番を抽出
3. 業務担当者が商品タイプ / 季節 / ブランド / 素材を **UI から正しい値に更新**
4. 商品分類 (1〜20) は staging テーブルの c036〜c055 を参照しながら手動マッピング

## ロールバック手順 (取込失敗時)

```sql
BEGIN;
-- 取込済データを全削除 (planned_year_code='Z' で識別)
DELETE FROM product_supplier_prices
 WHERE product_family_id IN (SELECT id FROM product_families WHERE planned_year_code = 'Z');
DELETE FROM products
 WHERE product_family_id IN (SELECT id FROM product_families WHERE planned_year_code = 'Z');
DELETE FROM product_families WHERE planned_year_code = 'Z';

-- Staging テーブル削除
DROP TABLE IF EXISTS staging_legacy_products;

-- マスタ補完分を削除する場合 (取込前の状態に完全復元)
DELETE FROM colors    WHERE legacy_id IS NOT NULL AND code LIKE 'L%';
DELETE FROM sizes     WHERE legacy_id IS NOT NULL AND code LIKE 'L%';
DELETE FROM suppliers WHERE legacy_id IS NOT NULL AND code NOT IN ('336','404','437');
UPDATE sizes     SET legacy_id = NULL WHERE legacy_id IS NOT NULL;
UPDATE suppliers SET legacy_id = NULL WHERE code IN ('336','404','437');

COMMIT;

-- sku 拡張は元に戻さない (新規企画も影響受けるため、ロールバック対象外)
```

## トラブルシュート

| 症状 | 原因 | 対処 |
|---|---|---|
| `ERROR: invalid byte sequence` | CSV が UTF-8 ではない | iconv で SHIFT_JIS → UTF-8 変換 |
| `ERROR: value too long for type character varying(11)` | pre-patch 未適用 | Step 1 を実行 |
| `family_count < 686` | CSV の行が欠落 | `staging_legacy_products` の `c001 IS NULL` 行を確認 |
| `rows_with_cost < 700` | 原価単価が空の行が多い | 想定内 (CSV の 37% に単価データなし) |
| `フォールバック適用` 0 件以外 | マスタ補完漏れ | Step 2 を再実行、不足コードを補充 |

## 関連ドキュメント

- `docs/migration/mig-3-strategy.md` — 戦略 (差分整理 / 設計判断 / 未解決事項)
- `db/init/03-products.sql` — products 関連テーブル定義 (Iter 2 で作成)
- `db/init/02-masters.sql` — マスタテーブル定義 (Iter 1 Seed)
- `iteration-plan.md` §3 Iter 4 — MIG-3 のロードマップ位置付け
