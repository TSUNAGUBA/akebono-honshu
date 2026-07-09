# MIG-3: 既存生産管理システム CSV 取込 (実行手順)

> **目的:** Phase 7 Iteration 4 Hardening の MIG-3 タスク。
> 既存生産管理システム 商品マスタ CSV (1,288 行) を新システム DB に取込む。
>
> **戦略:** `docs/migration/mig-3-strategy.md` を先に読むこと。設計判断 5 件は
> ユーザ承認済み (2026-05-20)。

> **プラットフォーム統合改修 (2026-07-09) による注意:**
> - db/init は tenant_id (uuid) + RLS + TIMESTAMPTZ(UTC) を含む形へ破壊的に更新済み。
>   稼働前 MVP のため **既存 DB は再初期化** (`run-migrations.sh` の `ACTION=reinit CONFIRM_REINIT=yes`、
>   ローカルは `docker compose down -v`) を前提とし、旧スキーマからの追従パッチは提供しない。
>   `iter4`〜`iter24` は旧スキーマ時代の履歴として保存 (再初期化後は baseline 記録のみ)。
> - **以後の新規 migration でテナントスコープ表のデータを操作する場合**、冒頭で
>   `SET app.tenant_id = '<uuid>';` を行うこと (FORCE ROW LEVEL SECURITY によりテーブル
>   所有者にも RLS が適用される。tenant_id 列の DEFAULT もこの GUC から解決される)。
> - `mig-3-*.sql` (本書の取込 SQL 群) は新スキーマ対応済み (`ON CONFLICT (tenant_id, ...)` 等)。
>   UI 経由 (LegacyImportService) の実行はアプリのテナントコンテキストが GUC を設定するため
>   追加操作は不要。

> **本書の対象範囲:** 本書は **MIG-3（既存 CSV データ取込）専用**の手順書です。
> スキーマ系マイグレーション（`iter4-*` / `iter5-*` 等、`mig-3-*` 以外の `*.sql`）は
> GitHub Actions「DB Init / Migrate (RDS)」を `action=migrate` で実行すれば
> `run-migrations.sh` が `schema_migrations` 台帳に基づき自動適用します
> （前進専用・二重適用防止）。手順は `deploy/README.md` §3.2 を参照。
>
> なお `iter6-demo-data.sql` は **リアルなデモ業務データ**（商品・付属情報・発注・生産）を
> 既存(稼働中)DB へ反映するシードです（`db/init/06-demo-data.sql` を `\ir` で取り込む単一 SoT、
> 全 INSERT 冪等）。スキーマ変更ではありませんが、`init` は既存 DB で中止されるため、
> 同じく `action=migrate` で適用されます。`iter7-ops-data.sql`（業務拡張モジュール: 販売管理/出荷/在庫管理のテーブル + サンプルデータ、`db/init/07-ops-data.sql` を `\ir` で取り込む）も同方式・冪等です。

---

## 推奨: 画面から取込 (1 操作)

オーナー権限 (`process_record_permission = 1`) でログインし、画面ナビの
**「データ移行」** → `/admin/legacy-import` から実施します。

### 手順

1. **バックアップ取得** (推奨):
   ```bash
   pg_dump -h localhost -U postgres -d akebono_honshu > backup_before_mig3.sql
   ```

2. **画面アクセス:** ログイン後、上部ナビの「データ移行」をクリック

3. **CSV 添付:** 既存システムから出力した CSV ファイルをそのまま選択
   - Shift_JIS / UTF-8 自動判定 (事前変換不要)
   - 最大 50 MB

4. **「取込実行」ボタン:** 確認ダイアログ → 「取込実行」
   - 内部処理 (Backend が自動実行):
     1. `products.sku` VARCHAR(11) → VARCHAR(16) 拡張
     2. マスタ補完 (色 31 / サイズ 10 / 仕入先 11)
     3. Staging テーブル DROP & CREATE
     4. CSV パース + Staging へバルク INSERT
     5. 主要列抽出 (c001-c138 → 名前付き)
     6. 本テーブル取込 (product_families / products / supplier_prices)

5. **結果確認:**
   - product_families: **686 件** (期待)
   - products: **1,288 件** (期待)
   - supplier_prices: 約 **686 件**
   - フォールバック適用: 全 0 件 (理想)
   - 警告一覧 (エンコーディング検出結果等)

6. **次のステップ (業務担当者作業):**
   - 商品一覧 (`/products`) で「年式: Z」「ステータス: Draft」フィルタを使い 686 件確認
   - 商品タイプ / 季節 / ブランド / 素材を UI から正しい値に更新
   - 旧「商品分類 1〜20」(staging.c036〜c055) を新マスタにマッピング

### エラー時の挙動

- DB 接続失敗 / Pre-patch 失敗: 500 エラー、UI に詳細表示
- CSV パースエラー: 422 エラー、警告に行番号
- 権限不足 (Owner 以外): 403 エラー、ナビに「データ移行」リンクは表示されない

---

## フォールバック: psql コマンドラインから取込

Backend 経由ではなく、DBA が直接 psql で取込みたい場合の手順。

### 前提

- PostgreSQL クライアント (`psql`) が利用可
- CSV を Shift_JIS → UTF-8 に事前変換:
  ```bash
  iconv -f SHIFT_JIS -t UTF-8 products.csv > /tmp/legacy_products_utf8.csv
  ```
- **UI 取込との排他** (プラットフォーム統合 第二段階): アプリ経由の取込はグローバル
  advisory lock (`hashtext('akebono:legacy-import')`) で直列化されるが、psql 直接実行は
  その対象外。**アプリを停止して実施する**か、下記手順 0 で同じロックを取得してから行う
  (取得できない場合は UI 取込が実行中 = 完了を待つ)。
- **テナントコンテキスト**: FORCE RLS + `tenant_id` DEFAULT が GUC から解決されるため、
  セッション冒頭の `SET app.tenant_id` が必須 (未設定は NOT NULL 違反で失敗 = フェイルクローズ)。

### 実行 (単一 psql セッションで 0 → 5 を順に流す)

advisory lock (セッションレベル) と `app.tenant_id` (セッション GUC) はどちらも
**同一セッション内でのみ有効**のため、psql を対話起動して `\i` で順に実行する
(ステップごとに psql を起動し直すとロックもテナントコンテキストも失われる)。
mig-3-*.sql 自体はテナント GUC を埋め込まない (アプリ経由の取込では認証テナントの
GUC が使われる共有 SQL のため)。

```
psql -h localhost -U postgres -d akebono_honshu

-- 0. 排他ロック取得 + テナントコンテキスト設定
--    (ロックが取れない場合は UI 取込が実行中。完了を待つ)
SELECT pg_advisory_lock(hashtext('akebono:legacy-import')::bigint);
SET app.tenant_id = '00000000-0000-4000-8000-000000000001';  -- Honshu 既定テナント

-- 1. DB スキーマ拡張
\i db/migration/mig-3-pre-patch.sql

-- 2. マスタ補完
\i db/migration/mig-3-step-01-master-fill.sql

-- 3. Staging テーブル作成
\i db/migration/mig-3-step-02-staging.sql

-- 4. CSV を Staging に取込 (\copy + 主要列抽出)
\i db/migration/mig-3-step-02-staging-load.sql

-- 5. 本テーブル取込
\i db/migration/mig-3-step-03-import.sql

-- 6. ロック解放 (セッション終了でも自動解放される)
SELECT pg_advisory_unlock(hashtext('akebono:legacy-import')::bigint);
```

各ステップで `RAISE NOTICE` や検証 SELECT が件数を出力します。

---

## ロールバック手順 (取込失敗時)

画面 / psql どちらの方法で取込んだ場合も共通。

```sql
BEGIN;
DELETE FROM product_supplier_prices
 WHERE product_family_id IN (SELECT id FROM product_families WHERE planned_year_code = 'Z');
DELETE FROM products
 WHERE product_family_id IN (SELECT id FROM product_families WHERE planned_year_code = 'Z');
DELETE FROM product_families WHERE planned_year_code = 'Z';

DROP TABLE IF EXISTS staging_legacy_products;

-- マスタ補完分も削除する場合 (完全に取込前の状態に復元)
DELETE FROM colors    WHERE legacy_id IS NOT NULL AND code LIKE 'L%';
DELETE FROM sizes     WHERE legacy_id IS NOT NULL AND code LIKE 'L%';
DELETE FROM suppliers WHERE legacy_id IS NOT NULL AND code NOT IN ('336','404','437');
UPDATE sizes     SET legacy_id = NULL WHERE legacy_id IS NOT NULL;
UPDATE suppliers SET legacy_id = NULL WHERE code IN ('336','404','437');

COMMIT;

-- sku 列拡張は元に戻さない (新規企画も影響受けるため、ロールバック対象外)
```

---

## ファイル構成

| ファイル | 役割 | 利用元 |
|---|---|---|
| `mig-3-pre-patch.sql` | DB 拡張 (sku VARCHAR(16)) | 画面 + psql |
| `mig-3-step-01-master-fill.sql` | マスタ補完 (色 31 / サイズ 10 / 仕入先 11) | 画面 + psql |
| `mig-3-step-02-staging.sql` | Staging テーブル DDL | 画面 + psql |
| `mig-3-step-02-staging-load.sql` | \copy + 主要列抽出 (psql 専用) | psql のみ |
| `mig-3-step-03-import.sql` | Staging → 本テーブル取込 (PL/pgSQL) | 画面 + psql |
| `README.md` | 本ファイル | – |

Backend (`src/Backend/Application/Migration/LegacyImportService.cs`) は
`mig-3-pre-patch.sql` / `step-01` / `step-02` / `step-03` を **Embedded Resource** として
参照し、`db/migration/` を Single Source of Truth として 1 元管理しています。

---

## 関連ドキュメント

- `docs/migration/mig-3-strategy.md` — 戦略 (差分 8 件 / 設計判断 5 件 / 未解決事項 5 件)
- `db/init/03-products.sql` — products 関連テーブル定義 (Iter 2)
- `db/init/02-masters.sql` — マスタテーブル定義 (Iter 1 Seed)
- `iteration-plan.md` §3 Iter 4 — MIG-3 のロードマップ位置付け
