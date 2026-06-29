using Akebono.Domain.Orders;

namespace Akebono.Application.Orders;

// ─────────────────────────────────────────────────
// 新規作成 (POST /api/v1/orders、O-01)
// ─────────────────────────────────────────────────
public record CreateOrderRequest(
    long SupplierId,
    long DeliveryDestinationId,
    long DepartmentId,
    long WarehouseId,
    DateOnly DueDate,
    long OrdererUserId,
    long ManagerUserId,
    long? SubOrderer1UserId,
    long? SubOrderer2UserId,
    long? SubOrderer3UserId,
    long? SubOrderer4UserId,
    long? SubOrderer5UserId,
    long? SubOrderer6UserId,
    string? CommunicationText,
    List<OrderLineInput> Lines,
    // 旧 発注書 国内/海外 項目 (Phase B、is_overseas 以外任意)
    bool IsOverseas = false,
    string? LandingPlace = null,
    string? CustomerRef = null,
    DateOnly? FactoryShippingDate = null,
    DateOnly? DeliveryPlaceShippingDate = null,
    DateOnly? OverseasDepartureDate = null,
    long? Warehouse2Id = null,
    long? Warehouse3Id = null,
    // 連絡文書 6 行 (構造化、PR6、末尾・nullable = 下位互換)。旧 spec 発注明細 No.27-32。
    // 新フローは本 6 列を保存する (SoT)。旧クライアントが未指定なら全 NULL で保存される。
    string? CommunicationLine1 = null,
    string? CommunicationLine2 = null,
    string? CommunicationLine3 = null,
    string? CommunicationLine4 = null,
    string? CommunicationLine5 = null,
    string? CommunicationLine6 = null);

public record OrderLineInput(
    long ProductId,
    int Quantity,
    decimal UnitPriceSnapshot,
    string CurrencyCodeSnapshot,
    // 旧 発注明細 項目 (Phase B、任意)。仮番号は商品 family からコピーするため入力には含めない。
    int? PackQuantity = null,
    decimal? EstimateUnitPrice = null,
    // 発注明細 備考 (spec 明細 No.26、任意)。末尾追加で下位互換。
    string? Remark = null,
    // 分納×倉庫の多次元明細 (PR5b、末尾・nullable = 下位互換)。null/空 = 分納なし (単一明細、従来挙動)。
    // 1 件以上あれば、その明細は分納で構成され、line.Quantity = Deliveries の Quantity 合計 (SUM) になる。
    List<OrderLineDeliveryInput>? Deliveries = null);

// 分納×倉庫の多次元明細 1 行の入力 (PR5b、旧 spec 発注明細 No.6 発注明細日 / No.7-17 発注明細数 /
// No.18-23 倉庫入数/発注数)。WarehouseId / DeliveryDate は NULL 許容 (倉庫未指定 / 発注明細日未指定)。
// Quantity は正の整数 (CHECK quantity > 0)。PackQuantity は倉庫別入数 (任意)。
public record OrderLineDeliveryInput(
    long? WarehouseId,
    DateOnly? DeliveryDate,
    int Quantity,
    int? PackQuantity = null);

// ─────────────────────────────────────────────────
// 一覧 (GET /api/v1/orders、O-03)
// ─────────────────────────────────────────────────
public record OrderListItem(
    long Id,
    string MgmtNo,
    string? OrderNo,
    short Status,
    string SupplierCode,
    string SupplierName,
    string DeliveryDestinationName,
    DateOnly DueDate,
    string? OrdererName,
    int LineCount,
    decimal TotalAmount,
    string CurrencyCode,
    DateTime? FirstExportedAt,
    DateTime? LastExportedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    // 発注区分 国内/海外 (Phase B、is_overseas)。一覧でのタブ絞込・区分バッジ表示用。
    bool IsOverseas = false,
    // 発注状態 5 値モデル (#3a)。DeliveredAt/IsDeleted から納品完了/発注削除 を導出 (フロント側)。
    DateTime? DeliveredAt = null,
    bool IsDeleted = false,
    // 一覧 SPLIT フィルタ (#3a) 用フィールド。発注先/発注者/得意先/単価未決定で client-side 絞込。
    long SupplierId = 0,
    long OrdererUserId = 0,
    string? CustomerName = null,
    // 明細に単価未決定 (unit_price_snapshot <= 0) を含むか (クエリで EXISTS 集計)。
    bool HasUndecidedPrice = false);

// ─────────────────────────────────────────────────
// 詳細 (GET /api/v1/orders/{id}、O-04 編集画面ベース)
// ─────────────────────────────────────────────────
public record OrderDetail(
    long Id,
    string MgmtNo,
    string? OrderNo,
    // 帳票出力フォーム 手入力項目 (発注日 / 出荷指示番号)。出力フォームの初期表示に使う。
    DateOnly? OrderDate,
    string? ShippingInstructionNo,
    short Status,
    DateTime? CancelledAt,
    string? CancelReason,
    long SupplierId,
    string SupplierCode,
    string SupplierName,
    string? SupplierOfficialNameSnapshot,
    string? SupplierCodeSnapshot,
    long DeliveryDestinationId,
    string DeliveryDestinationName,
    string? CustomerNameSnapshot,
    long DepartmentId,
    string DepartmentName,
    long WarehouseId,
    string WarehouseName,
    DateOnly DueDate,
    long OrdererUserId,
    string OrdererName,
    long ManagerUserId,
    string ManagerName,
    long? SubOrderer1UserId,
    long? SubOrderer2UserId,
    long? SubOrderer3UserId,
    long? SubOrderer4UserId,
    long? SubOrderer5UserId,
    long? SubOrderer6UserId,
    string? CommunicationText,
    DateTime? FirstExportedAt,
    DateTime? LastExportedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    List<OrderLineDetail> Lines,
    // 旧 発注書 国内/海外 項目 (Phase B)。納入倉庫2/3 は表示用に名前も解決して返す (未設定時 null)。
    bool IsOverseas = false,
    string? LandingPlace = null,
    string? CustomerRef = null,
    DateOnly? FactoryShippingDate = null,
    DateOnly? DeliveryPlaceShippingDate = null,
    DateOnly? OverseasDepartureDate = null,
    long? Warehouse2Id = null,
    string? Warehouse2Name = null,
    long? Warehouse3Id = null,
    string? Warehouse3Name = null,
    // 発注状態 5 値モデル (#3a)。納品完了/発注削除 の状態表示・操作可否判定に使う。
    // 操作者名は cancelled_by と同じく詳細では非表示 (日時のみ表示)。
    DateTime? DeliveredAt = null,
    bool IsDeleted = false,
    DateTime? DeletedAt = null,
    // 連絡文書 6 行 (構造化、PR6、末尾追加 = 下位互換)。新フローの SoT を返す。フロントは 6 列を各
    // スロットへ載せる。6 列が全て空でも CommunicationText がある旧発注は、フロント側で改行分割して
    // ブリッジ表示する (本 DTO は 6 列と CommunicationText の両方をそのまま返すだけ)。
    string? CommunicationLine1 = null,
    string? CommunicationLine2 = null,
    string? CommunicationLine3 = null,
    string? CommunicationLine4 = null,
    string? CommunicationLine5 = null,
    string? CommunicationLine6 = null);

public record OrderLineDetail(
    long Id,
    short LineNo,
    long ProductId,
    string Sku,
    string ProductName,
    string ColorName,
    string SizeName,
    int Quantity,
    decimal UnitPriceSnapshot,
    string CurrencyCodeSnapshot,
    decimal Subtotal,
    // 旧 発注明細 項目 (Phase B、任意)
    int? PackQuantity = null,
    decimal? EstimateUnitPrice = null,
    string? ProvisionalNumberSnapshot = null,
    // 発注明細 備考 (spec 明細 No.26、任意)。末尾追加で下位互換。
    string? Remark = null,
    // 分納×倉庫の多次元明細 (PR5b、末尾追加 = 下位互換。既定は空リスト = 分納なし = 単一明細)。
    List<OrderLineDeliverySummary>? Deliveries = null);

// 分納×倉庫の多次元明細 1 行の返却 (PR5b、旧 spec 発注明細 No.6/No.7-17/No.18-23)。
// Seq は表示順 (配列順で採番)。WarehouseName は倉庫名 (未指定時 null)。
public record OrderLineDeliverySummary(
    long Id,
    long? WarehouseId,
    string? WarehouseName,
    DateOnly? DeliveryDate,
    int Quantity,
    int? PackQuantity,
    short Seq);

// ─────────────────────────────────────────────────
// 更新 (PATCH /api/v1/orders/{id}、O-04、edit_reason 必須 F-16)
// ─────────────────────────────────────────────────
public record UpdateOrderRequest(
    EditReason EditReason,
    string? EditNote,
    long SupplierId,
    long DeliveryDestinationId,
    long DepartmentId,
    long WarehouseId,
    DateOnly DueDate,
    long OrdererUserId,
    long ManagerUserId,
    long? SubOrderer1UserId,
    long? SubOrderer2UserId,
    long? SubOrderer3UserId,
    long? SubOrderer4UserId,
    long? SubOrderer5UserId,
    long? SubOrderer6UserId,
    string? CommunicationText,
    List<UpdateLineInput> Lines,
    // 旧 発注書 国内/海外 項目 (Phase B、is_overseas 以外任意)
    bool IsOverseas = false,
    string? LandingPlace = null,
    string? CustomerRef = null,
    DateOnly? FactoryShippingDate = null,
    DateOnly? DeliveryPlaceShippingDate = null,
    DateOnly? OverseasDepartureDate = null,
    long? Warehouse2Id = null,
    long? Warehouse3Id = null,
    // 連絡文書 6 行 (構造化、PR6、末尾・nullable = 下位互換)。旧 spec 発注明細 No.27-32。
    // 新フローは本 6 列で上書き保存する (SoT)。CommunicationText は新フローで書かない (旧データのみ保持)。
    string? CommunicationLine1 = null,
    string? CommunicationLine2 = null,
    string? CommunicationLine3 = null,
    string? CommunicationLine4 = null,
    string? CommunicationLine5 = null,
    string? CommunicationLine6 = null);

public record UpdateLineInput(
    long? Id,
    long ProductId,
    int Quantity,
    decimal UnitPriceSnapshot,
    string CurrencyCodeSnapshot,
    // 旧 発注明細 項目 (Phase B、任意)。仮番号は商品 family からコピーするため入力には含めない。
    int? PackQuantity = null,
    decimal? EstimateUnitPrice = null,
    // 発注明細 備考 (spec 明細 No.26、任意)。末尾追加で下位互換。
    string? Remark = null,
    // 分納×倉庫の多次元明細 (PR5b、末尾・nullable = 下位互換)。null/空 = 分納なし (単一明細、従来挙動)。
    // 編集時は全置換 (既存分納削除→再挿入、line.Quantity 再計算)。明細は元々全削除→再 INSERT 方式のため、
    // 親明細削除に伴い分納も CASCADE で消えてから本入力で再構築される。
    List<OrderLineDeliveryInput>? Deliveries = null);

// ─────────────────────────────────────────────────
// 中止 (POST /api/v1/orders/{id}/cancel、O-05)
// ─────────────────────────────────────────────────
public record CancelOrderRequest(string CancelReason);

// ─────────────────────────────────────────────────
// 帳票出力フォーム (POST /api/v1/orders/{id}/export、旧システム「発注書出力」画面相当)
//   旧画面と同様に「発注日」「出荷指示番号」「発注番号」を手入力し、Excel 帳票に反映して出力する。
//   Format = 出力帳票選択 (order=発注書のみ / management=管理表のみ / both=発注書+管理表)。
//   入力した 3 項目は発注に保存し (order_no は未入力なら既存値/自動採番を維持)、再出力時に初期表示する。
// ─────────────────────────────────────────────────
public record ExportOrderRequest(
    DateOnly? OrderDate,
    string? ShippingInstructionNo,
    string? OrderNo,
    string Format);

// ─────────────────────────────────────────────────
// 連絡文章 (O-07、テンプレ複写)
// ─────────────────────────────────────────────────
public record CommunicationTextSuggestion(string Body, bool StandardPrintFlag, string SourceLabel);

// ─────────────────────────────────────────────────
// 単価サジェスト (PR2、size-aware)。発注明細の unit_price_snapshot 入力補助。
// GET /api/v1/orders/price-suggestion?productId=&supplierId=
//   SKU (productId) の size に対応する現単価を「(family, supplier, SKUのsize) の現単価 →
//   無ければ (…, NULL-size 既定) の現単価」のフォールバックで解決して返す。
//   現単価が一切無ければ Found=false (フロントは従来どおり手入力)。
//   注: snapshot 書込は従来どおりクライアント入力値を verbatim 保存する (本サジェストは入力補助のみで、
//   サーバ側で snapshot を上書きしない = 下位互換。「単価未決定」状態 unit_price<=0 も維持される)。
// ─────────────────────────────────────────────────
public record SupplierPriceSuggestion(
    bool Found,
    decimal? UnitPrice,
    string? CurrencyCode,
    decimal? ExchangeRate,
    // 解決に使われた行が size 専用か全サイズ既定か (UI 表示・デバッグ用)。
    long? ResolvedSizeId,
    bool IsSizeSpecific);
