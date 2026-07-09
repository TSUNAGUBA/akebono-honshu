# MIG-3: 既存生産管理システム CSV 取込戦略

> **プラットフォーム統合改修 (2026-07-09) 注記:** 本書は MIG-3 実施当時 (単一テナント・
> JST-naive スキーマ) の戦略記録である。本文中の SQL 断片 (`ON CONFLICT (code)` 等) は
> 旧スキーマ前提であり、現行の実行 SQL は `db/migration/mig-3-*.sql`
> (tenant_id / `ON CONFLICT (tenant_id, code)` 対応済) が SoT。実行手順は
> `db/migration/README.md` を参照。


> **対象:** Phase 7 Iteration 4 Hardening の「MIG-3 既存データ移行」。
> Iteration 2 で送り状を作成した移行課題 8 件 (iteration-plan.md §3 Iteration 4) を解決する。
>
> **データ:** 既存生産管理システム 商品マスタ CSV (1,288 行、138 列、SHIFT_JIS)。機密情報 (取引先名 / 単価) 含むため git 管理せず、オペレーター手元保管。Iteration 4 着手時に再提供して取込実施。

---

## 1. CSV データ実態 (2026-05-20 分析)

| 項目 | 値 |
|---|---|
| 全行数 | 1,288 (旧 SKU 単位、ヘッダ除く) |
| ユニーク 他品番 (= 新 `product_family` 候補) | 686 |
| ユニーク 商品 (= 旧 SKU) | 699 |
| 仕入先コード (旧 11 種) | `105, 181, 213, 336, 404, 411, 433, 434, 437, 801, 888` |
| カラーコード (旧 31 種) | `00, 10, 11, 12, 15, 20-90 系` |
| サイズ (旧 10 種) | `M, L, LL, S, 16.0, 18.0, 20.0, M-L, 3L4L, 00` |
| 商品分類 1 (ブランド/事業部候補) | `2, 4` (2 種のみ) |
| 商品分類 2 (商品タイプ候補) | `001, 002, 011, 016-028` 等 (10+ 種) |
| 税抜購買単価 データあり | 96 件 (7%) |
| 原価単価 データあり | 816 件 (63%) |
| 売価系単価 | 多様 (税抜販売 / 税込販売 / 上代 / 参考上代 / SKU 別単価) |

## 2. 新システムとの差分整理

| # | 課題 | 影響 | 対応方針 |
|---|---|---|---|
| 1 | 旧 SKU (FA2071F、7 文字) と新 11 桁体系の不一致 | `products.sku` の VARCHAR(11) 制約違反 | **`products.sku` を VARCHAR(11) → VARCHAR(16) に拡張**。旧 SKU はそのまま保持、新規企画 (Iter 2 以降) は 11 桁を継続 |
| 2 | 旧カラー 31 種 (11/30 等) vs 新 4 種 (030/040 等) | colors マスタが不足 | **マスタ自動補完**: CSV に出現する全カラーを `colors` テーブルに INSERT (`legacy_id` に旧 code 保存、`item_conversion_code` は旧 code を 2 桁ゼロ埋め) |
| 3 | 旧サイズ 10 種に "16.0"、"3L4L" 等 | サイズ name でゆるく検索可能、item_conversion_code が不定形 | **マスタ自動補完**: 既存 name で一致しないものを `sizes` に追加。`item_conversion_code` は仮値 (4 桁、不足は "_" でパディング) |
| 4 | 旧仕入先 11 種 vs 新 3 種 (Iter 1 Seed) | suppliers マスタが不足 | **マスタ自動補完**: 全 11 種を `suppliers` に INSERT。`country_id` は不明のため「日本」固定。`official_name` は code (例: `411`) を仮値、業務担当者が後で更新 |
| 5 | 旧「商品分類 1〜20」(20 種) と新マスタの不一致 | brand / function / department などへの mapping 不明 | **legacy_id 保存のみ**: 新マスタへの自動 mapping はしない。staging テーブルで保持し、業務担当者が後で UI でひも付け |
| 6 | 旧 単価種別 13 種 (税抜販売/購買/原価/上代/参考上代 + SKU 別) と新 1 種 | どの単価を仕入単価とするか | **「原価単価」(カラム 64) を仕入単価として採用**。0 または空のレコードは単価なしで取込 (status=Draft) |
| 7 | 旧 part1〜10 + 素材 + 混率 (10 種) と新 materials × 3 (甲皮/中底/底) | 部位の対応関係不明 | **甲皮/中底/底 は仮で「綿」(Iter 1 Seed の最初の素材) を割当**。業務担当者が UI で後で修正 |
| 8 | CSV 機密性 (取引先名 / 単価) | git コミット禁止 | **オペレーター手元保管**、`db/migration/` 内に staging テーブル定義のみ git 管理。CSV 本体は取込時に Backend サーバ上に配置 |

## 3. 取込戦略 (3 フェーズ)

### Phase 3.1: マスタ自動補完 (`mig-3-step-01-master-fill.sql`)

CSV のユニーク値を抽出して新マスタに INSERT。既存値とは衝突しない (`ON CONFLICT (code) DO NOTHING` で冪等)。

- カラー: 旧 code (例: "11") をゼロ埋めして新 code (例: "011") に正規化
- サイズ: name で既存と一致するものは引当、それ以外は新規追加
- 仕入先: 11 種すべて新規追加 (Iter 1 Seed の 3 種以外)
- 商品分類 1, 2 → brand / function は自動マッピングせず、`legacy_code_mappings` テーブルに保持

### Phase 3.2: Staging テーブル取込 (`mig-3-step-02-staging.sql`)

```sql
CREATE TABLE staging_legacy_products (
    row_no              SERIAL PRIMARY KEY,
    legacy_sku          VARCHAR(16),
    legacy_family_code  VARCHAR(16),
    product_name_1      VARCHAR(255),
    color_legacy        VARCHAR(8),
    size_legacy         VARCHAR(16),
    supplier_legacy     VARCHAR(8),
    cost_unit_price     NUMERIC(12,2),
    raw_classification  JSONB,  -- 分類 1〜20 を JSONB 保持 (後で参照可)
    raw_materials       JSONB,  -- 部位 1〜10 + 素材 + 混率
    imported_at         TIMESTAMP DEFAULT NOW()
);
```

CSV を `\copy staging_legacy_products FROM '/path/to/products.csv' CSV HEADER` で読込。

### Phase 3.3: 本テーブルへの取込 (`mig-3-step-03-import.sql`)

Staging から `product_families` + `products` + `product_supplier_prices` への INSERT:

```sql
-- 1. product_families に他品番ユニークで INSERT (legacy_id に旧 family code)
INSERT INTO product_families (
  planned_year_code, product_type_id, product_season_id, sequence_no,
  factory_supplier_id, brand_id, product_group_id,
  upper_material_id, insole_material_id, outsole_material_id,
  product_name_1, status,
  created_by_user_id, updated_by_user_id, legacy_id
)
SELECT DISTINCT
  'Z' as planned_year_code,  -- 旧データ識別フラグ
  (SELECT id FROM product_types LIMIT 1),  -- 仮値、業務担当者が後で修正
  (SELECT id FROM product_seasons LIMIT 1),
  LPAD(ROW_NUMBER() OVER (ORDER BY legacy_family_code)::text, 3, '0'),
  (SELECT id FROM suppliers WHERE legacy_id = supplier_legacy LIMIT 1),
  (SELECT id FROM brands LIMIT 1),
  (SELECT id FROM product_groups LIMIT 1),
  (SELECT id FROM materials WHERE code = '001'),  -- 綿 (仮)
  (SELECT id FROM materials WHERE code = '001'),
  (SELECT id FROM materials WHERE code = '001'),
  product_name_1, 0,  -- status=Draft
  1, 1,  -- owner (id=1)
  legacy_family_code
FROM staging_legacy_products
WHERE legacy_family_code IS NOT NULL
ON CONFLICT DO NOTHING;

-- 2. products に SKU 単位で INSERT
INSERT INTO products (product_family_id, color_id, size_id, sku, legacy_id, ...)
SELECT
  pf.id,
  c.id, s.id,
  sl.legacy_sku,  -- 旧 SKU をそのまま保存
  sl.legacy_sku
FROM staging_legacy_products sl
JOIN product_families pf ON pf.legacy_id = sl.legacy_family_code
JOIN colors c ON c.legacy_id = sl.color_legacy
JOIN sizes s ON s.legacy_id = sl.size_legacy
...

-- 3. product_supplier_prices に単価あり分のみ INSERT
INSERT INTO product_supplier_prices (...)
SELECT ...
FROM staging_legacy_products
WHERE cost_unit_price > 0;
```

## 4. 取込手順 (オペレーター作業)

1. **DB 拡張パッチを適用** (`mig-3-pre-patch.sql`):
   - `products.sku` を VARCHAR(11) → VARCHAR(16) に拡張
   - 各マスタ (`colors`, `sizes`, `suppliers`, `materials`, ...) の `legacy_id` カラム確認 (既に存在)
2. **CSV を Backend サーバに配置** (例: `/tmp/legacy_products_utf8.csv`、SHIFT_JIS → UTF-8 変換済み)
3. **Phase 3.1 マスタ補完 SQL** を pgAdmin4 で実行
4. **Phase 3.2 Staging テーブル + COPY コマンド** を実行
5. **Phase 3.3 本テーブル INSERT SQL** を実行
6. **取込件数検証**:
   ```sql
   SELECT 'product_families (新規)' AS t, COUNT(*) FROM product_families WHERE legacy_id IS NOT NULL
   UNION ALL SELECT 'products (新規)', COUNT(*) FROM products WHERE legacy_id IS NOT NULL
   UNION ALL SELECT 'product_supplier_prices', COUNT(*) FROM product_supplier_prices;
   ```
   期待: product_families 686 件 / products 1288 件 / supplier_prices 816 件
7. **業務担当者 UI 確認** (Frontend /products):
   - 旧データは `planned_year_code='Z'` 起点で識別
   - status=Draft のフィルタで未確定品番を抽出
   - 商品タイプ / 季節 / ブランド / 素材 が「仮値」のため、UI から正しい値に更新

## 5. 未解決事項 (業務担当者判断必要)

| # | 項目 | 判断者 | タイミング |
|---|---|---|---|
| 1 | 商品分類 1, 2 と新 brand / function / product_group / department の対応表 | 業務担当者 | 取込前に提供 (任意、未提供時は仮値割当) |
| 2 | 旧 部位 1〜10 と新 甲皮/中底/底 の集約方針 | 業務担当者 | 取込後 UI で個別更新 |
| 3 | 旧単価種別 13 種のどれを新 `unit_price` として採用するか | 業務担当者 | **「原価単価」を仮採用済**、変更要望あれば取込前に SQL 修正 |
| 4 | `planned_year_code = 'Z'` 識別の妥当性 (新規企画と区別) | プロダクトオーナー | 取込前 |
| 5 | status=Draft で取込んで業務担当者が UI で確定する運用フロー | プロダクトオーナー + 業務担当者 | 取込後の運用設計 |

## 6. 制約緩和パッチ (`mig-3-pre-patch.sql`)

```sql
-- products.sku を 11 桁 → 16 桁に拡張 (旧 7-8 桁 SKU 取込対応)
ALTER TABLE products ALTER COLUMN sku TYPE VARCHAR(16);
COMMENT ON COLUMN products.sku IS '11 桁 (新規企画) または旧 SKU (legacy import 時、最大 16 桁)';

-- product_families.legacy_id は既に VARCHAR(64) で投入済 (Iter 2 で確認)
-- colors / sizes / suppliers / materials の legacy_id も既存
```

## 7. ロールバック手順 (取込失敗時)

```sql
-- 取込済みデータを全削除 (legacy_id IS NOT NULL で識別)
BEGIN;
DELETE FROM product_supplier_prices WHERE product_family_id IN (
  SELECT id FROM product_families WHERE legacy_id IS NOT NULL
);
DELETE FROM products WHERE legacy_id IS NOT NULL;
DELETE FROM product_families WHERE legacy_id IS NOT NULL;
-- マスタ補完分は legacy_id IS NOT NULL で識別可能、要なら個別 DELETE
COMMIT;

-- Staging テーブルは DROP TABLE で削除
DROP TABLE IF EXISTS staging_legacy_products;
```

---

## 関連ドキュメント

- Phase 5 data-design.md §4 (商品関連)
- iteration-plan.md §3 Iteration 4 (MIG-3 既存データ移行)
- Iteration 2 で送り状を作成した 8 件の移行課題 (上記 §2 の表に展開)
