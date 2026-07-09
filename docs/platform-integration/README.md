# あけぼの SCM プラットフォーム統合改修記録

- 対象リポジトリ: akebono-honshu（メーカーサービス akebono-maker のリファレンス実装 / 最初のメーカーテナント）
- 準拠先 SoT: **akebono-scm-platform リポジトリ `docs/platform-design/`**（あけぼの SCM プラットフォーム設計一式）
- 実施日: 2026-07-09 ／ status: 実装済み（第一段階）
- 前提: 本アプリは稼働前 MVP のため過去データ考慮は不要とし、破壊的変更（DDL 直接改修・API 契約変更・再初期化前提）を採用した（オペレーター指示）。

## 1. 本書の位置づけ

本書は、akebono-honshu をあけぼの SCM プラットフォーム（提供主体: TSUNAGUBA）の基盤上で
展開するために実施した改修の内容・設計判断・残課題を記録する。プラットフォーム側の規約
（マルチテナンシー・API 共通規約・エラーコード体系・DB 共通規約）の SoT は akebono-scm-platform
の各設計書であり、本書はそれらを**参照するが再定義しない**。

| 参照先 (akebono-scm-platform) | 本改修で準拠した内容 |
|---|---|
| AKB-DOC-12 API共通規約とエラーコード台帳 | パス規約 `/api/{service}/v1`、封筒 `{data, meta}` / `{error}`、`X-Tenant-Id` 突合、`Idempotency-Key`、エラーコード `AKB-<AREA>-<NNN>` |
| AKB-DOC-13 §10.3 / AKB-DOC-14 §7 | 移行 3 大差分 M1 (tenant_id 導入)・M2 (UNIQUE 差替)・M3 (TIMESTAMPTZ/UTC 化) |
| AKB-DOC-20 非機能・セキュリティ・マルチテナンシー | RLS 標準形（USING + WITH CHECK / FORCE / fail-closed / SET LOCAL 相当）、多層防御 |
| AKB-DOC-05 メーカーサービス | `AKB-MAKER-*` エラーコード（本アプリは新規採番せず既存割当のみ使用）、採番フロー（advisory lock キーに tenant_id） |
| AKB-DOC-09 バックオフィス | テナント/Claims の SoT（認証=Firebase、テナント台帳=akebono-backoffice、RDS 先行 → Claims 後追い） |

> **旧称に関する注意:** 本リポジトリ `docs/platform-design/`（全 31 本）はプラットフォーム設計の
> 旧版であり、コード名 **SCIP は deprecated**（正式名称: あけぼの SCM プラットフォーム）。
> プラットフォーム設計の現行 SoT は akebono-scm-platform リポジトリ側にある。

## 2. 概要（変更サマリ）

1. **マルチテナンシー導入（M1/M2）**: 全 47 業務テーブルに `tenant_id uuid NOT NULL` を追加し、
   一意制約を `UNIQUE(tenant_id, ...)` へ差替。`tenant` テーブル（レジストリ投影）を新設。
2. **RLS 配線**: テナントスコープ 45 テーブルに `tenant_isolation` ポリシー
   （USING + WITH CHECK / FORCE ROW LEVEL SECURITY / fail-closed）。アプリ接続は非特権ロール
   `akebono_app`。適用除外 3 テーブルは §5 参照。
3. **TIMESTAMPTZ/UTC 化（M3）**: 全 timestamp 列を `TIMESTAMPTZ` に変更し、
   `Npgsql.EnableLegacyTimestampBehavior` を廃止。格納は `SystemTime.UtcNow`、
   表示・帳票・採番年度判定は `SystemTime.JstNow`。
4. **API 契約整合**: パス `/api/v1` → `/api/maker/v1`、成功封筒 `{data, meta}`、
   エラー封筒 `{error: {code, message, userAction?, traceId, details[]}}`、
   `X-Tenant-Id` 突合（不一致 403 AKB-TENANT-002）、テナントステータス判定
   （suspended/terminated は 403 AKB-TENANT-004）、作成系 3 API に `Idempotency-Key` 必須。
5. **エラーコード**: 旧 `DOMAIN-NNN` 埋め込み方式を廃止し `AKB-<AREA>-<NNN>` へ写像（§6）。
6. **採番の直列化**: 全 4 採番（mgmt_no / instruction_no / order_no / sequence_no）を
   トランザクション内 advisory lock + **テナントを含むロックキー**で統一。
7. **フロントエンド追随**: apiBase・封筒解釈・`X-Tenant-Id`・`Idempotency-Key`・エラー形式・
   UTC 時刻の JST 表示。
8. **認証フロー拡張**: `OnTokenValidated` で tenant を解決し `akebono_tenant_id` クレームを付与。
   一次ソースは Firebase Custom Claims の `tenant_id`（SoT = akebono-backoffice）、
   MVP 暫定フォールバックは `users.tenant_id`。

## 3. テナントモデルと SoT 宣言（CLAUDE.md 原則 6）

| データ | SoT | 本アプリでの持ち方 |
|---|---|---|
| テナントのライフサイクル（契約・プラン・ステータス） | プラットフォーム akebono-backoffice | `tenant` テーブル = 投影（キャッシュ）。アプリからは読取専用（DB GRANT で書込剥奪）。プロビジョニング接続までは init シードの Honshu テナント 1 件 |
| テナント既定値 (Honshu) | 本リポジトリ db/init | 固定 UUID `00000000-0000-4000-8000-000000000001` / tenant_code `honshu` |
| 認証 (Email/PW・UID) | Firebase Authentication | 変更なし |
| 業務ユーザ・権限 4 カテゴリ | 本アプリ RDS `users` | 変更なし（tenant_id 列を追加） |
| ユーザの所属テナント | MVP: `users.tenant_id`（RDS 先行）→ 接続後: Firebase Custom Claims `tenant_id`（bko 発行のキャッシュ） | トークンに claim があれば claim 優先、なければ RDS 値 |

- **書込順序**: テナント確定（クレーム）→ `ITenantContext` → 接続オープン時に
  `set_config('app.tenant_id', ...)` → RLS。SaveChanges で TenantId 自動スタンプ
  （未確定は AKB-TENANT-005 で拒否 = フェイルクローズ）。
- **MVP 制約（設計判断）**: 1 Firebase アカウント = 1 テナント所属
  （`users.firebase_uid` はグローバル一意のまま）。複数所属 (`available_tenants`)・
  確定トークン再発行は bko 接続後の段階へ委譲（§9 未決 2）。

## 4. マルチテナンシー実装の要点

- **RLS ポリシー標準形**（db/init/08-tenancy-rls.sql）:
  `tenant_id = (NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid` を USING / WITH CHECK 両方に適用。
  `NULLIF` は Npgsql がプール返却時にセッションをリセットした後、`current_setting` が
  **NULL ではなく空文字を返す**ケースの実測（2026-07-09、Npgsql 8.0.5 + PostgreSQL 16.13）に
  基づく。空文字を `::uuid` キャストするとエラーになるため、NULL へ正規化して「0 行」で閉じる。
- **GUC 伝搬**: EF Core の `DbConnectionInterceptor`（TenantSessionInterceptor）が接続オープン
  ごとに `set_config(..., is_local: false)` を発行する。
  - **AKB-DOC-20 §3.2 の標準形（`SET LOCAL` / `is_local=true` 必須）からの逸脱**である。
    EF Core の読取クエリはトランザクションを張らないため、トランザクションスコープの
    SET LOCAL では RLS コンテキストを伝搬できない（技術的制約）。
  - 標準形が防ごうとする「プール再利用によるコンテキスト残留」は次の三重で防止する:
    ①論理クローズ時にインターセプタが GUC を明示クリア（ConnectionClosing）、
    ②Npgsql が物理接続のプール返却時にセッションをリセット（実測済み 2026-07-09。
    リセット後の `current_setting` は空文字のため、ポリシー側 `NULLIF(...,'')` で 0 行）、
    ③テナント未確定のオープンでは GUC を空にする。
  - 本逸脱のプラットフォーム側 ADR 裁定は未取得（§9 未決 10。裁定までは本節が設計判断の記録）。
    外部プーラー（pgbouncer transaction pooling 等）を挟む構成は本方式の前提外であり導入前に再評価する。
- **多層防御**: ①認証クレーム → ②X-Tenant-Id 突合 → ③EF グローバルクエリフィルタ
  （アプリ層 WHERE）→ ④RLS → ⑤SaveChanges の TenantId スタンプ/検証 → ⑥監査ログ。
- **tenant_id 列の DEFAULT**: `(NULLIF(current_setting('app.tenant_id', TRUE), ''))::uuid`。
  EF は常に明示値を書くため、DEFAULT はシード投入・レガシー取込（raw SQL）用の補助経路。
  GUC 未設定なら NOT NULL 違反で失敗する（フェイルクローズ）。
- **接続ロール**: `akebono_app`（NOSUPERUSER / NOBYPASSRLS）。docker の `POSTGRES_USER`
  (akebono_honshu) はスーパーユーザで RLS を素通しするため、アプリ接続に使用しない。
  本番パスワードは `deploy/db/run-migrations.sh` の `APP_DB_PASSWORD` で上書きする。

## 5. RLS 適用除外テーブル（設計判断として文書化）

| テーブル | 除外理由 | 代替の防御 |
|---|---|---|
| `tenant` | テナントレジストリ投影（横断参照） | アプリロールは SELECT のみ（INSERT/UPDATE/DELETE 剥奪） |
| `users` | 認証エントリポイント: `firebase_uid → tenant_id` の解決が RLS のテナントコンテキスト確立**前**に必要（鶏卵問題） | EF グローバルクエリフィルタ（認証経路のみ `IgnoreQueryFilters` を明示）+ tenant_id NOT NULL |
| `audit_logs` | 認証拒否イベント（UidUnboundProbe 等）はテナント未確定で記録される | INSERT 専用（UPDATE/DELETE を GRANT 剥奪）・tenant_id は確定時に自動付与 |
| `staging_legacy_products`（実行時作成） | レガシー取込の一次着地（テナント確定前 staging） | 取込確定時に GUC 経由で tenant_id が付与される。UI は owner 権限（process_record_permission）限定。DROP→CREATE 方式のため**複数テナントの同時取込は非対応**（§9 未決 12） |

## 6. エラーコード写像表（旧 → 新）

旧体系（メッセージ末尾埋め込みの `DOMAIN-NNN`）は廃止。新コードは `error.code` フィールドで返す。
写像は 1:1 ではない（AKB-DOC-12 §15 の方針どおり粒度差を吸収）。

| 旧コード | 新コード | HTTP |
|---|---|---|
| PINST-001/002/003, MORD-002, ORDER-001/015/016, ORDER-LINE-DLV-001〜003, PROD-002, SETC-001/002, PRICE-002, BOM-001, EXR-001〜003, BULK-001〜004（検証系） | `AKB-SYS-002` | 422 |
| PINST-004（生産指示の状態遷移違反） | `AKB-MAKER-020` | 409 |
| PINST-005, MORD-004（採番衝突） | `AKB-MAKER-002` | 409 |
| MORD-001（BOM 未登録） | `AKB-MAKER-011` | 422 |
| MORD-003, ORDER-003/004/005/007/009/011/013/014（発注系の状態遷移違反） | `AKB-MAKER-031` | 409 |
| ORDER-012, EXR-004, BOM-002, PROD-003, PROD-004（重複・一意制約） | `AKB-SYS-007` | 409 |
| （401 認証トークン欠如／不正／期限切れ） | `AKB-AUTH-001` / `AKB-AUTH-002` / `AKB-AUTH-003` | 401 |
| （403 権限不足） | `AKB-AUTH-010` | 403 |
| （403 未紐付け・無効化ユーザ） | `AKB-AUTH-005` | 403 |
| （404 リソース不在・越境秘匿） | `AKB-TENANT-010` | 404 |
| （X-Tenant-Id 不一致） | `AKB-TENANT-002` | 403 |
| （テナントステータス不許可） | `AKB-TENANT-004` | 403 |
| （テナントコンテキスト内部安全違反） | `AKB-TENANT-005` | 500 |
| （Idempotency-Key 欠如 / 競合） | `AKB-SYS-004` / `AKB-SYS-005` | 400 / 409 |
| （楽観競合 / DB 一意制約 / 内部エラー） | `AKB-SYS-006` / `AKB-SYS-007` / `AKB-SYS-020` | 409 / 409 / 500 |

## 7. Idempotency-Key（作成系）

- 対象: `POST /api/maker/v1/orders`・`POST /api/maker/v1/production-instructions`・
  `POST /api/maker/v1/material-orders`（トランザクションを新規発行する作成系）。
- 実装: ヘッダ必須（欠如 400 AKB-SYS-004）。対象テーブルに `idempotency_key` +
  `idempotency_payload_hash`（要求 DTO の SHA-256）を保存し、部分一意
  `UNIQUE(tenant_id, idempotency_key)`。同一キー・同一ハッシュの再送は既存リソースを再返却
  （新規作成しない）。同一キー・異なるハッシュは 409 AKB-SYS-005。
  なお「初回結果の再返却」は逐次再送に対する保証であり、**同時**二重送信は
  `UNIQUE(tenant_id, idempotency_key)` により 2 本目が 409（AKB-SYS-007）になる（最終防壁）。
- マスタ等の設定系 POST は自然キー `UNIQUE(tenant_id, code)` による冪等が既に成立しているため
  第一段階では対象外（§9 未決 4）。

## 8. 検証（実施済み）

- `dotnet build Akebono.sln`（TreatWarningsAsErrors=true、0 warning / 0 error）
- db/init 全 8 ファイルをローカル PostgreSQL 16.13 へ適用（02〜08 は冪等再適用も確認。
  01 は初回専用: initdb / reinit の DROP 後にのみ実行される設計）
- RLS スモークテスト `db/verify/rls-smoke.sql`（akebono_app 接続で実行）:
  自テナント可視 / GUC 空文字 0 行 / 他テナント 0 行 / 越境 INSERT 拒否 /
  GUC 未設定 INSERT 拒否 / audit_logs 追記専用 / tenant 読取専用
- Npgsql 挙動の実機検証: プール返却時のセッション GUC リセット、
  timestamptz への Kind=Unspecified 書込例外、DateOnly→date 書込

## 9. 未決事項（後続フェーズへの委譲）

1. **PK の uuid 化**（AKB-DOC-14 §7）: BIGSERIAL のまま。app_maker 一般化スキーマへの
   写像時に実施（全エンティティ・全 API・FE 型に波及するため第一段階から除外）。
2. **複数テナント所属**（available_tenants・確定トークン再発行・テナント選択 UI）:
   akebono-backoffice 接続後（AKB-DOC-12 §10.2）。
3. **論理削除の deleted_at 統一**: 継承マスタの `delete_flag` / 取引系の `is_deleted` は
   継承容認（AKB-DOC-13 §5.2 の段階移行方針）。app_maker 一般化時に `deleted_at` へ。
4. **Idempotency-Key の全作成系 POST への拡大**と保持期間 (24h) の失効処理。
5. **カーソルページング**（limit 50 / 上限 200）: 現状は全件返却。MVP データ量では実害が
   ないため、FE のページング UI と併せて後続対応。
6. **07-ops-data プロトタイプ層の正規化再設計**（日本語 VARCHAR ステータス → text+CHECK、
   sales_order 等の再モデリング）: AKB-DOC-14 の app_maker 正規化層で置換予定
   （現状は tenant_id + RLS のみ適用済み・FE はモック表示のまま）。
7. **updated_at の DB トリガ (set_updated_at) 強制**: 現状はアプリ責務（EF サービス層）。
   一般化スキーマ移行時に導入。
8. **経路A（自社アプリ）連携の実装**: core への恒等マッピング・CDC/イベント発行・
   mart (fact_order/fact_production) への写像はプラットフォーム Data Plane 構築後。
9. **監査ログの月次パーティション・S3 アーカイブ**（AKB-DOC-20）。
10. **RLS コンテキスト注入方式の ADR 裁定**: AKB-DOC-20 §3.2 の標準形（SET LOCAL）に対する
    本実装（接続スコープ set_config + 明示クリア、§4）の逸脱承認をプラットフォーム側 ADR へ
    申請する（委譲先: akebono-scm-platform ADR 台帳）。
11. **OpenAPI 仕様（openapi.yaml）の公開と実装一致の CI 検証**（AKB-DOC-12 §4-1）。
12. **レガシー取込 staging の複数テナント同時実行対応**（現状 DROP→CREATE 方式の単一実行前提。
    テナント別 staging またはロックで直列化）。
13. **X-Tenant-Id ヘッダの必須化**（現状はヘッダ送信時のみ突合。クレーム単独でも安全に確定するため
    MVP では任意。FE は全リクエストで送信済み）。

## 10. 運用への影響（オペレーター向け）

- **DB は再初期化が必要**（破壊的変更）: ローカルは `docker compose down -v && docker compose up -d`、
  RDS は `deploy/db/run-migrations.sh` の `ACTION=reinit CONFIRM_REINIT=yes`（稼働前環境専用）。
- **本番の akebono_app パスワード**: `APP_DB_PASSWORD` を repository secrets / Secrets Manager に
  追加する。**init / reinit では必須**（未設定はエラー終了 = 既知の既定値のまま本番構築される
  事故を防ぐゲート）。migrate では任意（設定時のみ ALTER ROLE）。
- **フロントの環境変数**: `NUXT_PUBLIC_API_BASE` を `.../api/maker/v1` へ更新。
- 詳細手順: RUNBOOK.md §0-P / deploy/README.md。

## 11. 改訂履歴

| 版 | 日付 | 変更 |
|---|---|---|
| 1.0 | 2026-07-09 | 初版（プラットフォーム統合 第一段階: マルチテナンシー / TIMESTAMPTZ / API 契約 / エラーコード / Idempotency-Key / FE 追随） |
