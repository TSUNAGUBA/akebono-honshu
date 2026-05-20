# Phase 5 成果物: アーキテクチャ設計

> **作成日:** 2026-05-19
> **状態:** ドラフト v1（オペレーターレビュー前）
> **依存:** Phase 4 確定スタック（AWS Tokyo + Firebase Auth/Hosting + .NET 8 + Nuxt 3 + RDS PostgreSQL 16 + EF Core 8）
> **次成果物:** `data-design.md` → `api-design.md` → `screen-design.md`
> **方針:** Phase 4 のスタック選定を前提に、コンポーネント責務・データフロー・SoT 境界・横断的関心事を確定する。詳細スキーマは `data-design.md`、API 形式は `api-design.md`、UI は `screen-design.md` で扱う。

---

## 1. システム俯瞰

### 1.1 デプロイメント構成

```
┌─────────────────────────────────────────────────────────────────────┐
│                    業務 LAN PC（Chrome / Edge 最新）                 │
└───────────────┬───────────────────────────────────┬─────────────────┘
                │ HTTPS                             │ HTTPS
                ▼                                   ▼
┌───────────────────────────────┐  ┌────────────────────────────────┐
│   Firebase Hosting (CDN)      │  │  Firebase Authentication       │
│   - Nuxt 3 SPA (nuxt gen)     │  │  - Email/Password Provider     │
│   - 静的アセット配信          │  │  - ID Token 発行 (JWT 1h)      │
│   - PR Preview Channel        │  │  - Refresh Token 自動更新       │
└───────────────────────────────┘  │  - Custom Claims (role, perms) │
                                   └────────────────────────────────┘
                │                                   │
                │ XHR + Bearer: <Firebase ID Token>
                ▼
┌─────────────────────────────────────────────────────────────────────┐
│            AWS Tokyo (ap-northeast-1)                               │
│ ┌─────────────────────────────────────────────────────────────────┐ │
│ │  AWS App Runner (1 vCPU / 2GB, min=1 / max=2)                   │ │
│ │  ┌───────────────────────────────────────────────────────────┐  │ │
│ │  │  ASP.NET Core 8 Web API (C#)                              │  │ │
│ │  │  ├─ Firebase JWKS + JwtBearer Middleware (Token 検証)     │  │ │
│ │  │  ├─ Authorization Policies (Custom Claims → 4 権限評価)   │  │ │
│ │  │  ├─ EF Core 8 + Npgsql (DbContext, Migration)             │  │ │
│ │  │  ├─ AWS SDK (S3, Secrets Manager, CloudWatch)             │  │ │
│ │  │  ├─ Firebase Admin SDK (User 管理 + setCustomUserClaims)  │  │ │
│ │  │  ├─ Serilog (構造化ログ → CloudWatch Logs)                 │  │ │
│ │  │  ├─ FluentValidation (リクエスト検証)                      │  │ │
│ │  │  └─ ClosedXML (Excel 出力 O-06)                           │  │ │
│ │  └───────────────────────────────────────────────────────────┘  │ │
│ │              │ VPC コネクタ          │ S3 API     │ Secrets API │ │
│ └──────────────┼──────────────────────┼────────────┼─────────────┘ │
│                ▼                      ▼            ▼               │
│ ┌──────────────────────┐ ┌────────────────┐ ┌──────────────────┐  │
│ │  RDS for PostgreSQL  │ │   Amazon S3    │ │ Secrets Manager  │  │
│ │  16 Multi-AZ         │ │  - 商品画像 5GB │ │  + KMS (CMK)     │  │
│ │  - db.t4g.small      │ │  - 監査アーカイブ│ │  - DB 接続文字列  │  │
│ │  - ap-northeast-1a/c │ │    (Glacier IR) │ │  - Firebase SA鍵 │  │
│ │  - 35日 PITR         │ │  - SSE-S3       │ │                  │  │
│ └──────────────────────┘ └────────────────┘ └──────────────────┘  │
│                                                                     │
│ ┌───────────────────────────────────────────────────────────────┐  │
│ │  横断: CloudWatch Logs / Metrics / Alarms + X-Ray + SNS       │  │
│ └───────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘

CI/CD:
  GitHub → GitHub Actions
    ├─ Build/Test → ECR Push → App Runner Deploy (AWS OIDC)
    └─ Build → firebase deploy (Service Account)
```

### 1.2 リクエスト経路と SoT 境界

| データ種別 | SoT | キャッシュ / 派生 | 補足 |
|---|---|---|---|
| ユーザ ID / Email / パスワードハッシュ | Firebase Authentication | - | Google 管理、認証情報は SoT 一元化 |
| ユーザ業務情報 / 権限ロール / 所属 | RDS `users` | Firebase Auth Custom Claims（権限のみ） | 権限変更時に RDS 先行 → Custom Claims 後追い同期 |
| 商品マスタ / 仕入先 / 取引先 / 発注 / 価格 | RDS PostgreSQL | - | 業務データ本体は AWS Tokyo に閉じ込め |
| 商品画像バイナリ | S3 | RDS `product_images.s3_key` で参照 | Pre-signed URL 配信 |
| 監査ログ（直近 3ヶ月）| RDS `audit_logs` | - | INSERT 専用、UPDATE/DELETE REVOKE |
| 監査ログ（3ヶ月超 / 3年保管）| S3 Glacier IR | - | Object Lock で不変化 |

---

## 2. レイヤー構造（バックエンド: .NET 8）

ASP.NET Core モノリス、機能カテゴリで Vertical Slice + 軽量レイヤード。

```
src/
├─ AkebonoHonshu.Api/                # Web API エントリ
│   ├─ Program.cs                    # DI 登録、Middleware、JwtBearer 設定
│   ├─ Endpoints/                    # Minimal API Endpoint 群（機能別フォルダ）
│   │   ├─ Auth/                     # POST /auth/sync, etc.
│   │   ├─ Products/                 # /products, /products/{id}/images
│   │   ├─ Suppliers/                # /suppliers
│   │   ├─ PurchaseOrders/           # /purchase-orders, /purchase-orders/{id}/lines
│   │   ├─ Outputs/                  # /outputs/excel, /outputs/labels
│   │   └─ ...
│   ├─ Authorization/                # Authorization Policies (4 権限)
│   ├─ Middleware/                   # GlobalExceptionHandler, RequestLogging
│   └─ appsettings.{env}.json        # AWS Secrets Manager 経由で実際の値注入
│
├─ AkebonoHonshu.Application/        # ユースケース層（Handler / Service）
│   ├─ Products/
│   │   ├─ Commands/                 # CreateProduct, UpdateProduct, ...
│   │   ├─ Queries/                  # GetProductList, GetProductDetail
│   │   └─ Dto/                      # ProductDto, ProductDetailDto
│   ├─ Validators/                   # FluentValidation
│   └─ Mappers/                      # Mapster Profile
│
├─ AkebonoHonshu.Domain/             # ドメインモデル
│   ├─ Entities/                     # Product, Supplier, Order, AuditLog, User, ...
│   ├─ ValueObjects/                 # Money, JanCode, OrderStatus, ...
│   ├─ Enums/                        # PermissionCategory, PermissionLevel
│   └─ Exceptions/                   # DomainException, NotFoundException, ...
│
├─ AkebonoHonshu.Infrastructure/     # 外部依存
│   ├─ Persistence/                  # DbContext, EF Configurations, Migrations
│   │   ├─ AppDbContext.cs
│   │   ├─ Configurations/           # IEntityTypeConfiguration<T>
│   │   └─ Migrations/
│   ├─ Auth/                         # IAuthService → FirebaseAuthService (R-12 抽象化)
│   ├─ Storage/                      # IObjectStorage → S3Storage (Pre-signed URL)
│   ├─ Secrets/                      # AwsSecretsManagerProvider
│   └─ Audit/                        # AuditLogInterceptor (DbContext SaveChanges Hook)
│
└─ AkebonoHonshu.Shared/             # 共有定数
    └─ ErrorCodes.cs                 # AUTH-001, PROD-002, ... (Phase 3 §10 完全準拠)

test/
├─ AkebonoHonshu.UnitTests/
└─ AkebonoHonshu.IntegrationTests/   # Testcontainers で PostgreSQL 起動
```

**依存方向:** Api → Application → Domain ← Infrastructure（Domain は純粋、Infrastructure は Domain の Interface を実装）。

### 2.1 認証ミドルウェアパイプライン順序

`Program.cs` での順序（CLAUDE.md .NET 注意点: Middleware パイプライン順序が登録順依存）:

```
UseSerilogRequestLogging
  → UseExceptionHandler (GlobalExceptionHandler)
  → UseCors (Firebase Hosting ドメインを Allow Origin)
  → UseAuthentication (JwtBearer + Firebase JWKS)
  → UseAuthorization (Custom Claims → Policy 評価)
  → UseEndpoints (Minimal API)
```

---

## 3. レイヤー構造（フロントエンド: Nuxt 3）

```
src/
├─ app.vue                            # ルートレイアウト
├─ nuxt.config.ts                     # ssr: false, modules
├─ plugins/
│   └─ firebase.client.ts             # Firebase JS SDK 初期化（client only）
├─ middleware/
│   └─ auth.global.ts                 # 全ルート前で ID Token 検証、未認証は /login へ
├─ composables/
│   ├─ useAuth.ts                     # signIn / signOut / onAuthStateChanged
│   ├─ useApi.ts                      # $fetch ラッパ + Bearer 自動付与 + エラーコード → トースト
│   ├─ useIdle.ts                     # 8時間アイドル監視（SEC-05 実装）
│   └─ usePermission.ts               # Custom Claims から 4 権限評価
├─ stores/                            # Pinia
│   ├─ auth.ts                        # 現在ユーザ・Custom Claims
│   └─ ui.ts                          # トースト・モーダル等のグローバル UI 状態
├─ pages/                             # ファイルベースルーティング（21画面）
│   ├─ login.vue
│   ├─ products/
│   │   ├─ index.vue                  # P-04 商品一覧
│   │   └─ [id].vue                   # P-04 商品詳細
│   ├─ orders/                        # フロント URL は短縮形 `/orders/*` (UI 表現)、API 側は `/api/v1/purchase-orders/*` (技術名)
│   ├─ suppliers/
│   └─ ...
├─ components/
│   ├─ ui/                            # Reka UI ラッパ（Button, Modal, DataTable, ...）
│   ├─ forms/                         # 共通フォーム部品
│   └─ domain/                        # ドメイン固有部品（ProductCard, OrderLineRow, ...）
└─ types/                             # OpenAPI 生成型（openapi-typescript）

public/
└─ assets/                            # アイコン・フォント等

test/
├─ unit/                              # Vitest
└─ e2e/                               # Playwright (Post-MVP)
```

### 3.1 認証フロー（フロント側）

> **CLAUDE.md Nuxt 注意点:** `.client.ts` プラグインは SSR 時にサーバで実行されない → Phase 4 で SPA モード確定（`ssr: false`）のため問題なし。

```
1. /login で signInWithEmailAndPassword(email, password)
2. Firebase が ID Token + Refresh Token を返却
3. firebase JS SDK が自動で localStorage に格納、定期更新
4. middleware/auth.global.ts が onAuthStateChanged を購読
5. ID Token 取得 → useAuth で Pinia store に格納
6. useApi が全 XHR に Authorization: Bearer <Token> を付与
7. バックエンドが JWKS で署名検証 + Custom Claims を読み取り
8. usePermission がフロント側でも 4 権限チェック（UI 表示制御のみ、最終判定はサーバ側）
```

---

## 4. データフロー: 主要シナリオ 5 件のドキュメントトレース検証

Phase 5 ゲート「全データフローが I/F レベルで矛盾なく通る」の充足のため、Phase 2 主要ユースケースから 5 シナリオを抜粋し、エンドツーエンドでトレースする。詳細 API/DB は後続成果物で確定するが、本セクションで I/F 整合の前提を確立する。

### 4.1 シナリオ A: 商品マスタ登録（P-04, UC-PROD-01）

```
[1] User: /products/new で SKU・色・サイズ・取引先・基本情報入力
[2] Frontend: useApi.post('/api/v1/products', payload)
                 → Authorization: Bearer <ID Token>
[3] App Runner: JwtBearer Middleware が Firebase JWKS で署名検証
                 → 失敗時: 401 AUTH-002 (Phase 3 §10)
[4] App Runner: AuthorizationPolicy "Product.Write" 評価
                 → Custom Claims.permissions に "product:write" 必要
                 → 失敗時: 403 AUTH-005
[5] App Runner: FluentValidation でリクエスト検証
                 → 失敗時: 422 PROD-002 (必須項目欠落)
[6] Application: CreateProductCommandHandler が ProductService.Create を呼ぶ
[7] Domain: Product エンティティ生成（SKU 一意性は Domain ルール）
[8] Infrastructure: DbContext.Products.Add(product); SaveChangesAsync()
                     ├─ EF Core が INSERT INTO products
                     └─ AuditLogInterceptor が SaveChanges Hook で audit_logs INSERT
                         (entity=Product, action=Create, user_id=Firebase UID,
                          changed_fields=[全列], occurred_at=now)
[9] Domain: SKU 重複時は PostgreSQL UNIQUE 制約 → DbUpdateException
[10] Middleware: GlobalExceptionHandler が PROD-001 (SKU 重複) に変換
[11] Frontend: 201 Created + 商品 ID 受信、一覧画面へ遷移、トースト表示
```

**SoT チェック:** 商品=RDS、認証=Firebase（順序: 認証検証 → RDS 書込 → 監査ログ）。
**冪等性:** SKU 一意制約で 2 重登録は DB 層で拒否（PROD-001）。

### 4.2 シナリオ B: 仕入先 × 商品 × 仕入単価設定（P-05, UC-PRICE-01）

```
[1] User: 商品詳細画面 → 「仕入先と単価を追加」モーダル
[2] Frontend: POST /api/v1/products/{productId}/supplier-prices
              { supplierId, unitPrice, effectiveFrom, currency }
[3] App Runner: 認証・認可 (Product.Write && Price.Write の AND 評価)
                 → Phase 3 §6 機密度「中-高」のため Price.Write は 4 権限の上位レベル必要
[4] Application: AddSupplierPriceCommand
                 ├─ Product 存在チェック (FK 整合)
                 ├─ Supplier 存在チェック
                 ├─ 既存 (productId, supplierId, effectiveFrom) との重複チェック → PRICE-001
                 └─ unitPrice > 0 検証 (FluentValidation)
[5] Infrastructure: product_supplier_prices INSERT
                    + audit_logs INSERT (action=PriceSet, value=暗号化された平文ではなくマスクログ "***")
                    ※ 監査ログには金額をマスクして格納（不正アクセス検知用のメタデータのみ）
[6] Frontend: 一覧再取得、現在有効な単価をハイライト
```

**SoT チェック:** 価格=RDS、Phase 4 §4 暗号化方針 A 採用（KMS 保存時暗号化 + アクセス制御）。
**機密度配慮:** 監査ログには金額本体を残さず、操作メタデータのみ保管（Phase 3 §6.2 中-高に対する追加保護）。

### 4.3 シナリオ C: 発注書作成（O-03, UC-ORDER-01）

```
[1] User: /orders/new で取引先・納期・明細（商品 × 数量 × 価格）入力
[2] Frontend: POST /api/v1/purchase-orders { customerId, dueDate, lines: [...] }
[3] App Runner: 認証・認可 (Order.Write)
[4] Application: CreateOrderCommand
                 ├─ Customer 存在チェック
                 ├─ 各 line.product_id 存在チェック (一括 IN クエリ、N+1 回避)
                 ├─ 各 line.quantity > 0
                 ├─ 同一 product_id 重複チェック (F-14 対応、ORDER-006)
                 └─ 在庫チェック (Phase 2 では未定 → Phase 3 で「在庫管理は MVP 対象外」確定)
[5] Infrastructure: トランザクション開始
                    ├─ purchase_orders INSERT (status=Active, first_exported_at=NULL)
                    ├─ purchase_order_lines バルク INSERT
                    └─ audit_logs INSERT (action=Order.Create, purchase_order_id)
                    トランザクションコミット
[6] Frontend: 201 Created + Purchase Order ID 受信、詳細画面へ遷移
[7] User: 「Excel 出力」ボタンクリック（Phase 6 簡素化、発注確定操作は廃止）
[8] Frontend: GET /api/v1/purchase-orders/{id}/excel
[9] App Runner: 初回時のみ order_no 採番 + first_exported_at SET、毎回 last_exported_at 更新 + purchase_order_export_logs INSERT + audit_logs
[10] Frontend: 出力バッジ更新（未出力 → 初回出力済 YYYY-MM-DD）
```

**SoT チェック:** 発注=RDS、トランザクション境界で purchase_orders + purchase_order_lines + audit_logs を一体化。
**冪等性:** Excel 出力を 2 回呼んでも初回採番（first_exported_at）は冪等。Idempotency-Key で初回採番の二重実行を防止。

### 4.4 シナリオ D: 発注書 Excel 出力（O-06, UC-OUT-01）

```
[1] User: 発注書詳細画面で「Excel 出力」ボタン
[2] Frontend: GET /api/v1/purchase-orders/{id}/excel
              → Authorization Bearer
[3] App Runner: 認証・認可 (Order.Read)
[4] Application: GenerateOrderExcelQuery
                 ├─ 発注情報・明細・取引先・商品マスタを Include で一括取得（N+1 回避）
                 ├─ ClosedXML でテンプレート流し込み
                 └─ ストリームで MemoryStream 返却
[5] App Runner: Response Content-Type=application/vnd.openxmlformats-...
                 + Content-Disposition: attachment; filename="order-{id}.xlsx"
                 + 5秒以内に応答（NFR §1.1 50明細）
[6] Frontend: Blob ダウンロード、ブラウザ標準保存ダイアログ
[7] Audit: GET でも閲覧扱いとして audit_logs INSERT (action=ExcelExported)
```

**SoT チェック:** Excel テンプレートは Application 層に固定（DB に持たない、MVP では）。**MVP は ① 国内用テンプレートのみ実装**（Phase 6 オペレーター確認で確定）。
**Post-MVP 計画:** ② 海外用、③ 海外用＋管理表 の 2 種類を追加予定。テンプレ種別は発注書の業務区分（国内/海外）から自動選択する設計を Phase 7 以降で導入（MVP では切替不要のため `templates/purchase-order-domestic.xlsx` 1 ファイル固定）。
**性能:** Phase 3 NFR §1.1 = 5秒以内、Phase 5 でテンプレート最適化方針確定。

### 4.5 シナリオ E: 権限変更時の Firebase Custom Claims 同期（管理機能 + R-11 緩和）

> **実装ステータス (2026-05-20 時点):** 段階 B では未実装。RBAC は OnTokenValidated で users.firebase_uid 引当 → 各 endpoint の `CheckMasterEditAsync` / `CheckOrderEditAsync` で RDS の権限カラム (`product_ledger_permission` 等) を毎リクエスト評価する RDS 直読方式。Custom Claims 同期 + Reconciler バッチは段階 C (本番デプロイ + シナリオ E + R-11 緩和) で実装予定。
> **Reconciler のタイムゾーン:** Iter 4 段階 B で DB を TIMESTAMP (JST naive) に統一したため、Reconciler バッチも `Akebono.Domain.Common.SystemTime.Now` を基準に running window 判定する (コンテナ標準 TZ=UTC との混在事故を防ぐ)。
> **IMemoryCache の段階 C スケーリング:** 段階 B では OnTokenValidated 内で 60s `IMemoryCache` を使い RDS への UID lookup を抑制している。段階 C で App Runner を 2 instance 以上に水平拡張すると **プロセスローカル cache のためインスタンス間で最大 60s 不整合** が発生 (firebase_uid 紐付け変更/論理削除がインスタンス毎に伝播)。許容範囲なら維持、許容しなければ ElastiCache (Redis) 等で共有 cache に置換する判断を段階 C 着手前に行う。dev → prod 切替 (§4.2.2bis) の初回 UID 再紐付け直後は **App Runner を 1 instance に絞るか全 instance を再起動** して cache を flush するのが安全。

```
[1] Admin User: /users/{uid}/permissions で権限ロール変更
[2] Frontend: PATCH /api/v1/users/{uid}/permissions
              { permissions: ["product:write", "order:read"] }
[3] App Runner: 認証・認可 (User.Admin)
[4] Application: UpdateUserPermissionsCommand
                 ├─ Step 1: RDS users.permissions UPDATE  ← SoT 書込先行（原則6）
                 ├─ Step 2: Firebase Admin SDK
                 │           .setCustomUserClaims(uid, { permissions: [...] })
                 │           ← キャッシュ更新は後追い
                 ├─ audit_logs INSERT (action=PermissionsChanged)
                 └─ 失敗時:
                    - Step 1 失敗: 全体ロールバック (DB Transaction)
                    - Step 2 失敗: RDS は確定済、Firebase 側が古いまま
                                   → 警告ログ + Reconciler バッチ日次照合で復旧
                                   → 影響: 既存 Token は最大 1 時間古い権限、Token 更新後に正常化
[5] Frontend: 成功時 200 OK、失敗時は USR-004 (Custom Claims 同期失敗、業務継続可)
```

**SoT チェック:** RDS = 権限 SoT、Firebase Custom Claims = キャッシュ。失敗時の挙動が CLAUDE.md 原則 4（非ブロッキング）整合。
**R-11 緩和:** Reconciler バッチ（夜間）で RDS と Firebase Custom Claims を全件 diff、不整合があれば自動修復 + アラート。

---

## 5. 横断的関心事

### 5.1 認証・認可

| 関心事 | 実装 | 該当要件 | 実装ステータス |
|---|---|---|---|
| ログイン | Firebase Auth `signInWithEmailAndPassword` | AUTH-001 (UC-AUTH-01) | ✅ 段階 B 完了 |
| 削除済ユーザ拒否 | Firebase Auth `disabled=true` + RDS `is_active=false` | AUTH-003 / SEC-12 | ✅ 段階 B 完了 (OnTokenValidated は `!IsDeleted` で引当、各 endpoint で IsActive 評価) |
| パスワードハッシュ | Firebase 標準 scrypt | SEC-04 | ✅ Firebase 標準 |
| ブルートフォース | Firebase 標準レートリミット | SEC-06 | ✅ Firebase 標準 |
| アイドル切断 8h | フロント `useIdle` + `signOut` | SEC-05 | ⏳ 段階 C 着手後実装 |
| トークン検証 | JwtBearer + Firebase JWKS | SEC-08 | ✅ 段階 B 完了 |
| 4 権限ポリシー | RDS 直読 (`CheckMasterEditAsync` / `CheckOrderEditAsync`)。段階 C 以降 Custom Claims + AuthorizationPolicy を追加してサーバ最終判定を二重化 | SEC-11 / C-02 | ⏳ RDS 直読のみ段階 B 完了、Custom Claims は段階 C |
| 権限変更同期 | RDS 先行 → setCustomUserClaims（§4.5）| 原則6 | ⏳ 段階 C 着手後実装 (シナリオ E) |

### 5.2 エラーハンドリング

| 関心事 | 実装 |
|---|---|
| エラーコード体系 | Phase 3 §10 を `AkebonoHonshu.Shared/ErrorCodes.cs` に定数化、全例外に必須付与 |
| グローバル例外ハンドラ | `Middleware/GlobalExceptionHandler` で例外 → ProblemDetails JSON 変換 |
| 構造化ログ | Serilog + `LogContext.PushProperty("ErrorCode", "...")` で検索性確保 |
| クライアント表示 | `useApi` がエラーコード → ユーザ向け日本語メッセージ変換、トースト表示 |
| 非ブロッキング | 補助処理（監査ログ書込失敗、通知失敗）は警告ログのみで主処理継続（原則4）|

### 5.3 監査ログ

| 関心事 | 実装 |
|---|---|
| 記録範囲 | 全業務エンティティの C/U/D + 価格閲覧 + ログイン/ログアウト + Excel 出力（SEC-13）|
| 記録方式 | `AuditLogInterceptor` を EF Core SaveChanges Hook に登録、Entity 変更を自動検出 |
| 改竄防止 | PostgreSQL ロール権限で `audit_logs` の UPDATE / DELETE を REVOKE（SEC-17）|
| 保管期間 | 直近 3ヶ月 = RDS、3ヶ月超 = S3 Glacier IR (Object Lock)、3年で物理削除 |
| アーカイブ | 月次 Lambda が `audit_logs` を S3 にエクスポート + DB から削除 |
| 機密度配慮 | 仕入単価等の機密値は本体ではなくマスク（"***"）または FK のみ記録 |

### 5.4 ログ・監視

| 関心事 | 実装 |
|---|---|
| アプリログ | Serilog → CloudWatch Logs（構造化、TraceId/UserId 自動付与）|
| メトリクス | CloudWatch Metrics（App Runner CPU/メモリ、RDS 接続数、HTTP 5xx 率）|
| 分散トレース | AWS X-Ray + AWS Distro for OpenTelemetry（EF Core クエリも可視化、R-4 N+1 早期検知）|
| アラート | CloudWatch Alarms → SNS → Slack/メール（5xx 率、レスポンスタイム、課金上限）|
| Auth/Hosting | Firebase Console（ログイン失敗率、配信トラフィック）|

### 5.5 シークレット管理

| シークレット | 格納 | 取得経路 |
|---|---|---|
| RDS 接続文字列 | AWS Secrets Manager | App Runner サービスロール |
| Firebase Admin SDK サービスアカウント JSON | AWS Secrets Manager（暗号化 KMS CMK）| App Runner サービスロール |
| S3 Pre-signed URL 署名鍵 | App Runner サービスロール（IAM ベース）| AssumeRole で一時クレデンシャル |
| Firebase Web SDK 設定（apiKey 等）| Nuxt の publicRuntimeConfig（Web SDK の公開鍵は安全）| ビルド時に注入 |
| GitHub Actions → AWS | OIDC（長期鍵なし）| `aws-actions/configure-aws-credentials` |
| GitHub Actions → Firebase | Service Account JSON を GitHub Secret 格納（Workload Identity Federation を Phase 5 後半で検討）| `firebase deploy` |

### 5.6 CORS / CSP

- **CORS:** App Runner の ASP.NET Core で `UseCors` 設定。Allow Origin = Firebase Hosting 本番ドメイン + プレビューチャネル URL パターン（`*.web.app` / `*.firebaseapp.com`）+ 開発時 `localhost:3000`
- **CSP:** Nuxt の HTTP ヘッダで `Content-Security-Policy` 設定。`connect-src 'self' <App Runner ドメイン> https://*.googleapis.com https://identitytoolkit.googleapis.com`、`script-src 'self' https://www.gstatic.com`

### 5.7 国際化・地域化

MVP では日本語のみ（Phase 2 確定、国内のみ）。Nuxt i18n 等は導入しない（YAGNI）。

---

## 6. デプロイ・環境

| 環境 | フロント | バック | DB | 用途 |
|---|---|---|---|---|
| dev (ローカル) | `nuxt dev` (localhost:3000) | `dotnet run` (localhost:5000) | Docker PostgreSQL 16 | 開発者ローカル |
| preview | Firebase Hosting プレビューチャネル (PR ごと自動) | App Runner プレビュー（Phase 5 後半検討） | 共有 dev DB | PR レビュー |
| stg | Firebase Hosting (stg.akebono.web.app) | App Runner stg サービス | RDS stg インスタンス | UAT・Phase 6 |
| prod | Firebase Hosting (本番ドメイン) | App Runner prod サービス | RDS prod Multi-AZ | 本番運用 |

> **dev 環境の認証:** Firebase Authentication の Emulator Suite をローカル起動。Firebase 本体への接続不要、CI でも利用可。

---

## 7. I/F 設計 6 視点チェック（アーキテクチャ層）

Phase 5 方法論の「I/F 設計 6 視点」を architecture.md レベルで実施。詳細 I/F は後続成果物で再評価。

| # | 視点 | チェック結果 |
|---|---|---|
| 1 | 技術スタック制約 | ✅ Phase 4 確定スタック（Firebase Auth / RDS PostgreSQL / S3 / EF Core 8）と整合。Firebase Admin SDK for .NET は公式提供、JWKS 検証は標準ライブラリで対応可能 |
| 2 | ユースケース | ✅ Phase 2 主要 UC（商品登録・価格設定・発注・出力）を §4 で 5 シナリオ全トレース、I/F 不整合なし |
| 3 | ユーザビリティ | ✅ Firebase Auth の自動 Token 更新でユーザ操作中の切断防止、エラーコード体系で一貫表示。詳細は `screen-design.md` |
| 4 | データ設計上の都合 | ✅ 17 マスタ + トランザクション + 監査ログの分離、SoT 境界明確。詳細は `data-design.md` |
| 5 | 型の継承関係 | ✅ Domain 層 Entity → DTO → API DTO の写像は Mapster で集約、ドメイン例外と HTTP エラーコードの 1:1 マッピングを `ErrorCodes.cs` で定義 |
| 6 | データフロー整合性 | ✅ §4 で 5 シナリオの起点 I/F → 派生 I/F を一気通貫トレース、SoT 順序（RDS 先行 → Firebase Custom Claims 後追い）厳守 |

---

## 8. ゲート条件チェック（architecture.md 部分）

| Phase 5 ゲート条件 | 本ドキュメントでのカバー | 状態 |
|---|---|---|
| サイトマップが作成されている | `screen-design.md` で対応 | ⏭ 後続 |
| 画面ごとの機能定義 | `screen-design.md` で対応 | ⏭ 後続 |
| I/F 設計が 6 視点チェック済み | §7 でアーキテクチャ層は完了、API/データレベルは `api-design.md` / `data-design.md` で実施 | ◐ 部分完了 |
| データ設計が正規化の原則に従っている | `data-design.md` で対応 | ⏭ 後続 |
| API 設計に癒着がない | `api-design.md` で対応 | ⏭ 後続 |
| プロトタイプがダミーデータで動作 | §4 ドキュメントトレース 5 シナリオで代替（オペレーターレビュー #Phase5-1 で合意）| ✅ |
| 全データフローが I/F レベルで検証済み | §4 で 5 シナリオ × 6 視点トレース完了、追加シナリオは後続成果物で補完 | ◐ 部分完了 |

---

## 9. 次の論点（オペレーターレビュー Phase5-Arch）

| # | 論点 | 推奨案 |
|---|---|---|
| Arch-1 | バックエンドのアーキテクチャパターン（Vertical Slice + 軽量レイヤード）採否 | 採用（21 機能規模に対し DDD フル装備は過剰、Vertical Slice で機能ごと独立進化）|
| Arch-2 | EF Core の DbContext スコープ（per-request）| ASP.NET Core 標準 Scoped（CLAUDE.md .NET 注意点参照）|
| Arch-3 | フロントの状態管理粒度（Pinia 集約 vs Composable 分散）| 認証・UI グローバル状態は Pinia、画面ローカル状態は Composable + ref で分散（YAGNI）|
| Arch-4 | プロトタイプ検証シナリオ数（現状 5 件） | 5 件で十分（4.1〜4.5 で全 21 機能の代表パターンをカバー）+ data-design / api-design で個別 I/F を網羅検証 |
| Arch-5 | preview 環境のバック側（App Runner プレビューを PR ごとに払い出すか）| Phase 5 後半 or Phase 7 で確定。MVP は共有 stg バックで十分とも判断可能 |
| Arch-6 | アーキテクチャ図のフォーマット（ASCII vs Mermaid）| 現状 ASCII。Mermaid に変換するかは README 化時に判断 |

---

## 10. 変更履歴

| 日付 | 内容 |
|---|---|
| 2026-05-19 | 初版作成（Phase 4 確定スタック準拠、5 シナリオドキュメントトレース）|
