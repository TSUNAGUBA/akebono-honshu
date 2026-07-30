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
>
> `iter30-attendance.sql`（勤怠管理・タイムカード、**akebono-office からの移植**）もスキーマ系のため
> `action=migrate` で自動適用されます。ただし**テナントスコープ表（`leave_types`）へのシードを含む**ため
> テナントコンテキストが必要で、その扱いが `mig-3-*.sql` と異なります。詳細は本書
> 「勤怠マイグレーション (`iter30-attendance.sql`) の適用」を参照してください。

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

## 勤怠マイグレーション (`iter30-attendance.sql`) の適用

> **本節の位置づけ:** 本書の主題である MIG-3 (CSV データ取込) とは別系統の**スキーマ系マイグレーション**です。
> 通常は自動適用されるため手動操作は不要ですが、**テナントスコープ表へのシードを含む**点が
> `mig-3-*.sql` と異なるため、その扱いを明記します。
>
> **移植元:** **akebono-office** の勤怠管理・タイムカード機能 (打刻・勤怠集計・36 協定アラート・
> 打刻修正申請・休暇) を akebono-honshu へ移植したもの (Iteration 30)。

### 何が入るか

`db/init/10-attendance.sql`（新規 DB 初期化用）と**等価**の内容を、既に初期化済みの DB へ追加適用します。

| 節 | 内容 |
|---|---|
| §1 | `users` への勤怠列 6 本 (`attendance_permission` / `punch_required` / `attendance_rule_id` / `hire_date` / `weekly_days` / `weekly_hours`) + CHECK 制約 |
| §2 | 6 テーブル作成 (`attendance_rules` / `punch_records` / `attendance_fix_requests` / `leave_types` / `leave_grants` / `leave_requests`) |
| §3 | `users.attendance_rule_id → attendance_rules(id)` の FK + 部分インデックス |
| §4 | **法定有給 (`有給休暇`) の休暇種別シード（全テナント）** |
| §5 | アプリロール `akebono_app` への権限付与（`punch_records` は追記のみのため `UPDATE` / `DELETE` を REVOKE）|
| §6 | RLS ポリシー配線（標準形: `USING` + `WITH CHECK` + `FORCE ROW LEVEL SECURITY`）|
| §7 | `updated_at` トリガ配線（`punch_records` は `updated_at` 列を持たないため対象外）|

テーブル定義の詳細は `.ai-native/outputs/phase5/data-design.md §14` を参照。

> **後続のスキーマ系マイグレーション `iter31-fix-target-punch.sql`（2026-07-28 / C-2）:**
> `attendance_fix_requests` に `target_punch_id UUID NULL` を追加します（打刻修正で「どの打刻を直すか」を
> 指定する列。NULL=同種の先頭 1 件へフォールバック）。`ADD COLUMN IF NOT EXISTS` で冪等、`db/init/10-attendance.sql`
> にも**末尾列として**反映済み（init 経路と migration 経路の `pg_dump -s` 一致を検証済み）。下記「適用手順（推奨: 自動）」で
> `iter30` と同様に `run-migrations.sh` が自動適用します（本ファイル固有の追加操作はありません）。未適用のまま
> コードを起動すると打刻修正申請だけが `column does not exist` で失敗しますが、起動時スキーマガードが検知して起動を中断します。

> **後続のスキーマ系マイグレーション `iter33-attendance-approval-routing.sql`（2026-07-30 / Iteration 33・akebono-office からの移植）:**
> 勤怠管理に**承認経路**（`attendance_routes` / `attendance_route_steps`）と**直行/直帰申請**（`direct_requests`）、
> および申請時に凍結する経路スナップショット（`attendance_request_steps`）を追加し、打刻修正申請を経路による
> **多段承認**へ拡張します（`attendance_fix_requests` に `current_step` / `direct_request_id` を追加、`status` の
> CHECK を 0..2 → 0..3 へ拡張）。併せて `users.title`（役職。承認経路の承認者を役職で指定するため）を追加します。
> すべて追加列・制約緩和のみで**下位互換**（経路未設定の区分はオーナー 1 名の単段承認へフォールバック = 従来挙動）。
> `db/init/11-attendance-approval-routing.sql` に同内容を反映済み（fresh-init 経路。init 経路と migration 経路を
> 差分検証で等価に保つ）。RLS / `updated_at` トリガ / `akebono_app` への GRANT は本ファイル自身が配線します
> （`08-tenancy-rls.sql` より後に実行されるため）。`ADD COLUMN IF NOT EXISTS` / `CREATE TABLE IF NOT EXISTS` /
> `pg_constraint` ガードで冪等。未適用のままコードを起動すると `users.title` を map する経路（ログイン `/auth/sync`）と
> 承認経路機能が `column does not exist` で失敗しますが、起動時スキーマガードが検知して起動を中断します。
> `iter33` は `find | sort` の辞書順で `iter30`〜`iter32` の直後（`iter4-*` より前）に並び、依存元の `iter30`（勤怠 6 テーブル）
> より後に適用されます。

### 適用手順（推奨: 自動）

GitHub Actions **「DB Init / Migrate (RDS)」を `action=migrate`** で実行します。
`run-migrations.sh` が `db/migration/*.sql`（`mig-3-*` を除く）を `find | sort` で探索し、
`schema_migrations` 台帳に基づき未適用のものだけを順に適用します（**前進専用・二重適用防止**）。
手順は `deploy/README.md §3.2` と同一で、本ファイル固有の追加操作はありません。

- **新規 DB（`action=init` / docker 初回起動）では不要**です。`db/init/10-attendance.sql` が同じ内容を適用し、
  `init` は全投入後に現行のスキーママイグレーションを「適用済み」として `schema_migrations` に記録するため、
  後から `migrate` を流しても二重適用されません。
- **適用順序の注意:** `find | sort` は辞書順のため `iter30-attendance.sql` は `iter4-*`〜`iter9-*` より**前**に
  並びます。本ファイルが依存するのは `01-schema.sql` の `tenant` / `users` と、**同じく `01-schema.sql` で
  定義される**共通トリガ関数 `set_updated_at()` のみで、`iter4`〜`iter29` とは独立しているため、
  この並び順で問題ありません。
  （`09-updated-at-triggers.sql` は既存テーブルへのトリガ配線のみを行うファイルであり、
  `set_updated_at()` の定義元ではありません。適用が `function set_updated_at() does not exist` で
  失敗した場合に確認すべきは `01-schema.sql` の投入状況です。）

### テナントコンテキスト（`SET app.tenant_id`）の扱い

本書冒頭の注意にあるとおり、**テナントスコープ表のデータを操作する migration は
`SET app.tenant_id` が必要**です（`FORCE ROW LEVEL SECURITY` によりテーブル所有者にも RLS が適用され、
`tenant_id` 列の DEFAULT もこの GUC から解決されるため）。`iter30-attendance.sql` は §4 で
`leave_types` にシードを投入するため、この条件に該当します。

**ただし本ファイルは、テナントコンテキストを自ら設定します。** シードブロックが
`tenant` 表を走査し、テナントごとに `PERFORM set_config('app.tenant_id', t.tenant_id::text, true)` を
実行してから INSERT します。

```sql
FOR t IN SELECT tenant_id FROM tenant LOOP
    PERFORM set_config('app.tenant_id', t.tenant_id::text, true);
    INSERT INTO leave_types (...) SELECT ... WHERE NOT EXISTS (...);
END LOOP;
```

したがって:

- **実行前に手動で `SET app.tenant_id` を行う必要はありません**（`mig-3-*.sql` との違い。
  `mig-3-*.sql` はアプリ経由実行でも使う共有 SQL のためテナント GUC を埋め込まず、psql 実行時は
  セッション冒頭での手動設定が必須でした）。
- 単一テナントだけでなく **`tenant` 表の全テナントに**シードが入ります（マルチテナント対応）。
- `set_config` の第 3 引数が `true`（トランザクションローカル）で、ファイル全体が `BEGIN` 〜 `COMMIT` で
  囲まれているため、**COMMIT 後にセッションの GUC が汚れません**。
- §2 のテーブル作成時点では GUC が未設定でも問題ありません（`tenant_id` の DEFAULT が評価されるのは
  行を INSERT するときのみで、DDL では評価されないため）。

### フォールバック: psql / GUI から直接適用

`\ir` 等の psql メタコマンドを使っていないため、`psql` でも pgAdmin 等の GUI クライアントでも
そのまま実行できます。ファイル全体が単一トランザクションです。

```bash
psql -v ON_ERROR_STOP=1 -h <host> -U <user> -d akebono_honshu \
     -f db/migration/iter30-attendance.sql
```

手動実行した場合は `schema_migrations` 台帳に記録されないため、後続の `action=migrate` が
同ファイルを再実行します。全処理が冪等なので**再実行しても安全**です
（記録が必要な場合は台帳へ手動 INSERT するか、そのまま再実行させてください）。

### 冪等性・下位互換（CLAUDE.md 原則 2 / 原則 7）

- `ADD COLUMN IF NOT EXISTS` / `CREATE TABLE IF NOT EXISTS` / `CREATE INDEX IF NOT EXISTS` /
  `DROP POLICY IF EXISTS` → `CREATE POLICY` / `DROP TRIGGER IF EXISTS` → `CREATE TRIGGER`。
  CHECK 制約と FK は `pg_constraint` を確認して未作成のときだけ追加します。
- シードは `NOT EXISTS` ガード付き INSERT。**再実行しても既存の休暇種別を更新・削除しません。**
- **記録系（`punch_records` / `leave_grants`）への UPDATE・DELETE は一切行いません。**
- `users` への追加列はすべて**末尾追加 + NOT NULL DEFAULT** のため、既存行は DEFAULT で自動的に妥当な値になり、
  **データ更新パッチは不要**です。`attendance_permission=1` / `punch_required=TRUE` により、
  既存ユーザは適用直後からそのまま打刻できます。既存テーブル・既存列・既存データは一切変更しません。

### ロールバック

**提供しません**（前進専用）。本ファイルは追加のみで既存データを変更しないため、適用によって
既存業務が壊れることはありません。稼働後に勤怠テーブルを削除すると打刻・休暇付与という
**記録系データを失う**ため、巻き戻しは行わない前提です。勤怠機能を止める必要が生じた場合は、
テーブル削除ではなく利用者の `attendance_permission = 0` で運用停止してください。

### 移植の除外スコープ

| 除外した office の機能 | 理由 |
|---|---|
| 祝日マスタ・営業日計算 | 翌営業日計算専用であり、honshu には翌営業日計算を使う機能が無い。法定休日は `attendance_rules.legal_holiday_weekday`（曜日）で判定するため不要 |
| AI 参照範囲・チャットボット・日報連携・通知・エスカレーション | honshu に対応機能が無い |
| 権限ルールマトリクスエンジン | honshu の 4+1 権限カテゴリ方式に置換（`users.attendance_permission` + オーナー権限 `process_record_permission`）|
| 雇用区分（`employment_type`）| 勤怠ルールは既定ルール方式、有給付与は `users.weekly_days` / `weekly_hours` で判定するため不要 |

### 未検証事項

- 本改修を行った環境では **.NET SDK を取得できず、バックエンドをローカルでコンパイル検証できていません**。
- `docs/api/openapi.json` は**未再生成**です。CI の `regen-openapi` ワークフロー
  （main 向け PR で自動再生成し、生成物を head ブランチへ自動コミットする）に委ねます。
- 本 SQL 自体は PostgreSQL 上での実行検証を行っていないため、初回適用はステージング環境で
  実施し、`\d+ punch_records` 等で列・制約・RLS ポリシー・トリガの作成結果を確認してください。

---

## 関連ドキュメント

- `docs/migration/mig-3-strategy.md` — 戦略 (差分 8 件 / 設計判断 5 件 / 未解決事項 5 件)
- `db/init/03-products.sql` — products 関連テーブル定義 (Iter 2)
- `db/init/02-masters.sql` — マスタテーブル定義 (Iter 1 Seed)
- `iteration-plan.md` §3 Iter 4 — MIG-3 のロードマップ位置付け
