# あけぼの SCM プラットフォーム統合改修記録

- 対象リポジトリ: akebono-honshu（メーカーサービス akebono-maker のリファレンス実装 / 最初のメーカーテナント）
- 準拠先 SoT: **akebono-scm-platform リポジトリ `docs/platform-design/`**（あけぼの SCM プラットフォーム設計一式）
- 実施日: 2026-07-09 ／ status: 実装済み（第一段階 + 第二段階）
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

第二段階（同日実施。§9 の未決事項のうち本リポジトリ内で完結するものを解消）:

9. **PK の uuid 化（W-A）**: 全業務テーブルの PK を BIGSERIAL から
   `id UUID PRIMARY KEY DEFAULT gen_random_uuid()` へ変更（AKB-DOC-13 §3 型規約。
   列名 `id` は継承ハウススタイルとして維持）。FK・EF エンティティ・API DTO・FE 型
   （エンティティ ID は文字列）まで全面追随。
10. **論理削除の deleted_at 統一（W-A）**: `delete_flag` / `is_deleted` を廃止し
    `deleted_at TIMESTAMPTZ NULL` へ統一（AKB-DOC-13 §5.2）。
11. **updated_at トリガ（W-A）**: `set_updated_at()` + 汎用配線
    （db/init/09-updated-at-triggers.sql、information_schema 走査で updated_at を持つ
    全テーブルに `trg_<table>_set_updated_at` を冪等作成）。
12. **監査ログの月次パーティション（W-A）**: `audit_logs` を `PARTITION BY RANGE (occurred_at)`
    化し、`ensure_audit_log_partitions(int)`（SECURITY DEFINER）+ 起動時/24h 周期の
    `AuditPartitionMaintenanceService` で先行作成（失敗は warning のみ = 非ブロッキング）。
13. **07 プロトタイプ層の正規化再設計（W-D）**: 旧 12 テーブル（日本語 VARCHAR ステータス・
    自然キー文字列参照）を単数形 12 テーブル + 導出 VIEW `accounts_receivable`（security_invoker）へ
    再設計。ステータスは text+CHECK の snake_case、記録系はデータ 3 分類に従い
    updated_at なし + UPDATE/DELETE 剥奪（追記専用）。FE のモック定数は正規化後スキーマと
    同形へ追随（DB 実データを返す API 連携は次段階 = ops 系画面は引き続きモック定数表示）。
14. **カーソルページング（W-B）**: 一覧 4 API（orders / products/families /
    production-instructions / material-orders）に `?limit=`（既定 50・上限 200）と
    不透明カーソルを導入（詳細は §7）。
15. **API 契約の残項目（W-B）**: `POST /products/families/complete` へ Idempotency-Key 拡大、
    業務 API の `X-Tenant-Id` ヘッダ必須化（欠如 400 AKB-SYS-003、認証系 `/auth/*` は適用除外）、
    レガシー取込のグローバル advisory lock 直列化（実行中は 409 AKB-SYS-006）。
16. **OpenAPI 公開 + CI 検証（W-C）**: `scripts/generate-openapi.sh` で
    `docs/api/openapi.json` を生成し、CI（openapi-check ジョブ）が実装との一致を diff 検証
    （AKB-DOC-12 §4-1）。

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
| `staging_legacy_products`（実行時作成） | レガシー取込の一次着地（テナント確定前 staging） | 取込確定時に GUC 経由で tenant_id が付与される。UI は owner 権限（process_record_permission）限定。全テナント共有の DROP→CREATE 方式のため、取込全体を**グローバル advisory lock で直列化**（実行中の再実行・他テナントの同時実行は 409 AKB-SYS-006。第二段階で対応）。並行取込を許すテナント別 staging への分離は将来課題（§9 未決 12） |

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
| （X-Tenant-Id 欠如 — 第二段階で必須化） | `AKB-SYS-003` | 400 |
| （ページングパラメータ不正: limit 範囲外・カーソル復号不能 — 第二段階で導入） | `AKB-SYS-011` | 400 |

> 補足: レガシー取込の「実行中につき拒否」（グローバル advisory lock 取得失敗）は
> `AKB-SYS-006`（409）を**暫定流用**する。台帳（AKB-DOC-12 §14.7）に「処理実行中・
> 直列化競合」のコードが存在せず、本アプリは新規採番をしない方針のため。台帳上の
> `AKB-SYS-006` の意味は「楽観ロック競合」であり厳密には一致しない（userAction で
> 「実行中の取込が完了してから再実行」と区別を明示）。専用コードの採番申請は §9 未決 14。

## 7. Idempotency-Key（作成系）とカーソルページング（一覧系）

### 7.1 Idempotency-Key

- 対象: `POST /api/maker/v1/orders`・`POST /api/maker/v1/production-instructions`・
  `POST /api/maker/v1/material-orders`・`POST /api/maker/v1/products/families/complete`
  （第二段階で追加。エンティティを新規発行する作成系 4 API）。
- 実装: ヘッダ必須（欠如 400 AKB-SYS-004）。対象テーブルに `idempotency_key` +
  `idempotency_payload_hash`（要求 DTO の SHA-256）を保存し、部分一意
  `UNIQUE(tenant_id, idempotency_key)`。同一キー・同一ハッシュの再送は既存リソースを再返却
  （新規作成しない。families は family/products/prices を初回応答と同形に再構築して返す）。
  同一キー・異なるハッシュは 409 AKB-SYS-005。
  なお「初回結果の再返却」は逐次再送に対する保証であり、**同時**二重送信は
  `UNIQUE(tenant_id, idempotency_key)` により 2 本目が 409（AKB-SYS-007）になる（最終防壁）。
- 適用除外（設計判断）:
  - マスタ等の設定系 POST は自然キー `UNIQUE(tenant_id, code)` による冪等が既に成立。
  - 状態遷移系 POST（cancel / mark-ordered / issue / complete / order / bulk-status 等）は
    状態機械の自然冪等（同状態への遷移 = no-op または 409）で二重実行が安全なためキー対象外。
- 保持期間 24h の失効処理は未実装（§9 未決 4）。
- 同一キー・同一ペイロードの**リプレイ応答**は「現行有効行からの再構築」
  （families は products を sku 順・prices を現行有効行のみ安定順で返す。
  リプレイまでに単価改定・論理削除が挟まった場合、初回応答との完全一致より
  現在の実データとの整合を優先する）。
- FE は `createIdempotencySession`（useApi）で**同一ペイロードの再試行に同じキーを使い回す**
  （タイムアウト後のユーザ再送で二重作成しない。成功後はキー破棄 = 意図的な同内容再作成は可能）。

### 7.2 カーソルページング（AKB-DOC-12 §7.1、第二段階で導入）

- 対象: 一覧 4 API（`GET /orders`・`GET /products/families`・`GET /production-instructions`・
  `GET /material-orders`）。
- リクエスト: `?limit=`（既定 50・上限 200。範囲外は 400 AKB-SYS-011）+ `?cursor=`
  （不透明トークン。復号不能は 400 AKB-SYS-011。`limit` が数値ですらない場合は
  バインド段階の 400 AKB-SYS-001）。
- レスポンス: `meta.page = {nextCursor, limit, hasMore}`。終端は `hasMore=false` /
  `nextCursor=null`。
- ソートは安定キー **(created_at, id) 降順**で確定。カーソルの中身は
  「createdAt の UTC Ticks | uuid」の base64url（クライアントは解釈しない）。
  キーセット条件のタイブレーカ `Guid.CompareTo` は Npgsql EF の uuid 比較への翻訳を
  実機検証済み（§8）。
- **products/families の並び順変更（破壊的）**: 旧 `updated_at DESC` は更新でページ間を
  行が移動しカーソルが不安定になるため、`created_at DESC` へ統一した。更新日順の
  並べ替えが必要な場合はフロント側で行う。
- FE は `limit=200` で取得し、`hasMore` の間「さらに読み込む」ボタンで続きを取得する
  （composables の `apiPaged` / `pageQuery` ヘルパー）。参照コピー元ピッカー等、
  全量が必要な画面はカーソルを終端まで辿る `listFamiliesAll` を使う。

## 8. 検証（実施済み）

第一段階:

- `dotnet build Akebono.sln`（TreatWarningsAsErrors=true、0 warning / 0 error）
- db/init 全 8 ファイルをローカル PostgreSQL 16.13 へ適用（02〜08 は冪等再適用も確認。
  01 は初回専用: initdb / reinit の DROP 後にのみ実行される設計）
- RLS スモークテスト `db/verify/rls-smoke.sql`（akebono_app 接続で実行）:
  自テナント可視 / GUC 空文字 0 行 / 他テナント 0 行 / 越境 INSERT 拒否 /
  GUC 未設定 INSERT 拒否 / audit_logs 追記専用 / tenant 読取専用
- Npgsql 挙動の実機検証: プール返却時のセッション GUC リセット、
  timestamptz への Kind=Unspecified 書込例外、DateOnly→date 書込

第二段階:

- `dotnet build`（0 warning / 0 error）・`vue-tsc --noEmit`（0 error）
- db/init 全 9 ファイル（09-updated-at-triggers.sql 追加後）を再初期化適用、
  RLS スモーク 10 チェック ALL PASSED（記録系 6 テーブルの追記専用・
  accounts_receivable VIEW 越しのフェイルクローズを追加検証）
- 実サービス経由のページング実機検証（ローカル DB）: 4 一覧のカーソル走査が
  全件クエリと完全一致（重複/欠落なし）、終端で nextCursor=null、
  `Guid.CompareTo` タイブレーカの uuid 翻訳
- product_families 冪等の実機検証: 同一キー再送で同一 family 再返却（1 行のみ）、
  異なるペイロードで 409 AKB-SYS-005
- advisory lock 直列化の実機検証: 接続 1 保持中は接続 2 が取得失敗、解放後に取得成功
- TenantResolutionMiddleware の直接駆動検証: ヘッダ欠如 400 AKB-SYS-003 /
  一致で通過 + ITenantContext 設定 / 不一致 403 AKB-TENANT-002 / auth/* 適用除外 /
  未認証素通り / suspended 403 AKB-TENANT-004 / API 外パス対象外
- PageCursor 検証: roundtrip（Kind=Utc 復元）、limit 0/-1/201 → 400 AKB-SYS-011、
  limit=200 許容、壊れたカーソル・中身不正カーソル → 400 AKB-SYS-011
- OpenAPI 生成の決定性（2 回生成で diff なし）と一覧 4 API への
  `limit`/`cursor` パラメータ反映

## 9. 未決事項（後続フェーズへの委譲）

第二段階で初版 13 件のうち 10 件（1・3・4・5・6・7・9・11・12・13）を解決した
（4・9・11・12 には残余があり、各項目の本文に明記）。未決のまま: 2・8・10。
第二段階で新たに 2 件（14・15）を登載した。番号は初版から維持する
（コード内コメント等が「§9 未決 N」で参照するため）。

1. ~~**PK の uuid 化**（AKB-DOC-14 §7）~~ → **第二段階で解決（W-A）**: 全業務テーブルを
   `id UUID DEFAULT gen_random_uuid()` 化し、FK・EF・API・FE 型まで追随（§2-9）。
2. **複数テナント所属**（available_tenants・確定トークン再発行・テナント選択 UI）:
   akebono-backoffice 接続後（AKB-DOC-12 §10.2）。**未決のまま（bko 依存）**。
3. ~~**論理削除の deleted_at 統一**~~ → **第二段階で解決（W-A）**: `delete_flag` /
   `is_deleted` を廃止し `deleted_at TIMESTAMPTZ NULL` へ統一（§2-10）。
4. **Idempotency-Key**: ~~全作成系 POST への拡大~~ → **第二段階で解決（W-B）**:
   エンティティ発行系 4 API へ拡大（families/complete 追加、§7.1）。マスタ等の設定系は
   自然キー冪等で対象外（設計判断として確定）。**保持期間 (24h) の失効処理のみ未決**
   （現状キーは無期限有効 = 期限切れ再送で新規作成される事故が起きない安全側。
   台帳量が問題になった時点でクリーンアップジョブを追加）。
5. ~~**カーソルページング**~~ → **第二段階で解決（W-B）**: 一覧 4 API + FE
   「さらに読み込む」（§7.2）。
6. ~~**07-ops-data プロトタイプ層の正規化再設計**~~ → **第二段階で解決（W-D）**:
   単数形 12 テーブル + 導出 VIEW、text+CHECK、記録系追記専用（§2-13）。
7. ~~**updated_at の DB トリガ強制**~~ → **第二段階で解決（W-A）**:
   09-updated-at-triggers.sql の汎用配線（§2-11）。
8. **経路A（自社アプリ）連携の実装**: core への恒等マッピング・CDC/イベント発行・
   mart (fact_order/fact_production) への写像はプラットフォーム Data Plane 構築後。
   **未決のまま（プラットフォーム依存）**。
9. **監査ログ**: ~~月次パーティション~~ → **第二段階で解決（W-A、§2-12）**。
   **S3 アーカイブ（古いパーティションの切離し・退避）のみ未決**（AKB-DOC-20。
   保持期間ポリシーの裁定待ち）。
10. **RLS コンテキスト注入方式の ADR 裁定**: AKB-DOC-20 §3.2 の標準形（SET LOCAL）に対する
    本実装（接続スコープ set_config + 明示クリア、§4）の逸脱承認をプラットフォーム側 ADR へ
    申請する（委譲先: akebono-scm-platform ADR 台帳）。**未決のまま（他リポジトリの裁定事項）**。
11. **OpenAPI 仕様の公開と実装一致の CI 検証** → **第二段階で大部分解決（W-C）**:
    `docs/api/openapi.json` + `scripts/generate-openapi.sh` + CI openapi-check（§2-16）。
    形式は AKB-DOC-12 §4-1 の趣旨（機械可読仕様の公開と実装一致検証）を JSON で満たし、
    必須ヘッダ（X-Tenant-Id / Idempotency-Key）と limit/cursor は operation filter で仕様へ反映済み。
    **残ギャップ**: エラー封筒スキーマ・エンドポイント別エラーコード例（§14.8）と
    応答スキーマ（現状 200 の型情報が薄い）の仕様反映は未対応。
12. **レガシー取込 staging**: ~~同時実行の破壊~~ → **第二段階でグローバル advisory lock により
    直列化（W-B、§5）**。並行取込を許す**テナント別 staging への分離のみ未決**
    （マルチテナント本格運用で取込頻度が上がった時点で対応）。
13. ~~**X-Tenant-Id ヘッダの必須化**~~ → **第二段階で解決（W-B）**: 業務 API で必須
    （欠如・空値は 400 AKB-SYS-003）。認証系 `/api/maker/v1/auth/*` はテナント解決前に呼ばれるため
    適用除外（§6）。
14. **「処理実行中・直列化競合」エラーコードの採番申請**（第二段階で発生した新規未決）:
    レガシー取込の実行中拒否に該当する SYS コードが台帳（AKB-DOC-12 §14.7）に存在しないため、
    暫定で `AKB-SYS-006` を流用中（§6 補足）。プラットフォーム側への採番申請と、
    裁定後の写像切替が残る（委譲先: akebono-scm-platform 台帳）。
15. **C3 機密（仕入単価・素材単価）の API レベルマスキング**（AKB-DOC-12 §12）:
    現状は認証済ユーザへ実値を返し、開示を監査ログで記録（金額は監査 note でマスク）する
    運用。既定マスク + reveal 権限（専用権限カラム）は未実装（旧計画の「段階 C 繰延」を
    本記録に正式登載）。

## 10. 運用への影響（オペレーター向け）

- **DB は再初期化が必要**（破壊的変更）: ローカルは `docker compose down -v && docker compose up -d`、
  RDS は `deploy/db/run-migrations.sh` の `ACTION=reinit CONFIRM_REINIT=yes`（稼働前環境専用）。
- **本番の akebono_app パスワード**: `APP_DB_PASSWORD` を repository secrets / Secrets Manager に
  追加する。**init / reinit では必須**（未設定はエラー終了 = 既知の既定値のまま本番構築される
  事故を防ぐゲート）。migrate では任意（設定時のみ ALTER ROLE）。
- **フロントの環境変数**: `NUXT_PUBLIC_API_BASE` を `.../api/maker/v1` へ更新。
- **第二段階も DB 再初期化で適用する**（uuid PK 化・deleted_at 統一・07 層再設計・
  product_families の冪等キー列はいずれも DDL 変更）。手順は第一段階と同じ
  （ローカル: `docker compose down -v && docker compose up -d`、RDS: reinit）。
  稼働前 MVP のためデータ移行パッチは作成しない（オペレーター合意済みの前提）。
- **API クライアントへの影響（第二段階）**: エンティティ ID が数値 → **uuid 文字列**に
  変わる（全 API の id / *_id フィールド。最大の破壊的変更）。一覧 4 API は封筒に
  `meta.page` が付き、既定で先頭 50 件のみ返す（全件が必要なら `nextCursor` を辿る）。
  `products/families` の並び順が `updated_at DESC` → `created_at DESC` に変わる。
  業務 API は `X-Tenant-Id` 必須（同梱 FE は送信済みのため影響なし。curl 等の
  手動疎通時はヘッダ付与が必要）。`POST /products/families/complete` は
  `Idempotency-Key` 必須。
- 詳細手順: RUNBOOK.md §0-P / deploy/README.md。

## 11. 改訂履歴

| 版 | 日付 | 変更 |
|---|---|---|
| 1.0 | 2026-07-09 | 初版（プラットフォーム統合 第一段階: マルチテナンシー / TIMESTAMPTZ / API 契約 / エラーコード / Idempotency-Key / FE 追随） |
| 2.0 | 2026-07-09 | 第二段階（W-A: uuid PK / deleted_at / トリガ / 監査パーティション、W-B: ページング / Idempotency 拡大 / X-Tenant-Id 必須 / 取込直列化、W-C: OpenAPI + CI、W-D: 07 層正規化）。§9 未決 13 件中 10 件を解決 (うち 4 件は残余あり)、新規 2 件 (14・15) を登載 |
