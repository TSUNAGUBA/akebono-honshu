/**
 * 17 マスタのスキーマ定義 (Phase 5 data-design.md §3.1-3.17)。
 * Frontend の動的フォーム生成 + 一覧表示列に使用。
 */

export type FieldType =
  | 'text'
  | 'number'
  | 'decimal'
  | 'textarea'
  | 'checkbox'
  | 'select-master'

export interface FieldDef {
  key: string
  label: string
  type: FieldType
  required?: boolean
  maxLength?: number
  placeholder?: string
  /** select-master 用 — 参照先マスタの slug (例: 'countries') */
  master?: string
  /** select-master 用 — 表示時に Entity のどのプロパティを参照するか (例: 'countryName') */
  displayKey?: string
  help?: string
}

export interface MasterDef {
  /** URL slug + API パス (例: 'brands' → /api/v1/masters/brands) */
  slug: string
  /** 画面表示名 (日本語) */
  label: string
  /** 補足説明 (画面ヘッダに表示) */
  description?: string
  /** 拡張フィールド定義 (id / code / name 以外、code/name は全マスタ共通で先頭に追加) */
  extensionFields: FieldDef[]
  /** 一覧テーブルに表示する追加列 (code/name は固定で表示) */
  extensionColumns?: { key: string; label: string }[]
}

const codeField: FieldDef = { key: 'code', label: 'コード', type: 'text', required: true, maxLength: 3, placeholder: '001', help: '3 桁固定 (000-999)' }
const nameField: FieldDef = { key: 'name', label: '名称', type: 'text', required: true, maxLength: 255 }

export const masterDefinitions: MasterDef[] = [
  {
    slug: 'brands',
    label: 'ブランド',
    description: 'ブランド名のマスタ。商品企画レベルで紐付け。',
    extensionFields: [],
  },
  {
    slug: 'sizes',
    label: 'サイズ',
    description: '商品サイズマスタ。品番末尾2桁の生成元コードを持つ。',
    extensionFields: [
      { key: 'itemConversionCode', label: '品番変換コード', type: 'text', required: true, maxLength: 4, placeholder: '110S', help: '品番末尾2桁の生成元 (例: 110S, AS)' },
    ],
    extensionColumns: [{ key: 'itemConversionCode', label: '品番変換コード' }],
  },
  {
    slug: 'functions',
    label: '機能',
    description: '商品機能マスタ (防水・防臭等)。',
    extensionFields: [],
  },
  {
    slug: 'countries',
    label: '国',
    description: '原産国マスタ。仕入先と紐付け。',
    extensionFields: [],
  },
  {
    slug: 'suppliers',
    label: '仕入先 (工場)',
    description: 'M-04 仕入先マスタ。official_name は発注書 Excel 帳票の宛名印字に使用 (F-22)。',
    extensionFields: [
      { key: 'officialName', label: '法的書面用正式名', type: 'text', maxLength: 255, placeholder: 'DEPARTURES', help: '発注書 Excel 帳票の宛名 (英字スペル可)' },
      { key: 'itemConversionCode', label: '品番変換コード', type: 'text', required: true, maxLength: 1, placeholder: 'A', help: '11桁品番の 7桁目 (工場コード)' },
      { key: 'countryId', label: '国', type: 'select-master', required: true, master: 'countries', displayKey: 'countryName' },
      { key: 'supplierType', label: '区分', type: 'number', required: true, help: '0=国内, 1=海外' },
      { key: 'alertTarget', label: 'アラート対象', type: 'number', help: '0=対象外, 1=対象' },
      { key: 'currencyCode', label: '適用通貨', type: 'text', maxLength: 3, placeholder: 'JPY', help: '仕入単価に適用する通貨 (JPY/USD/CNY 等)。商品⑤の為替・円換算に使用 (§2f)' },
      { key: 'drayageCost', label: 'ドレー代', type: 'decimal', help: '仕入先ごとのドレー代。商品⑤仕入単価へ自動反映 (§2i)' },
    ],
    extensionColumns: [
      { key: 'officialName', label: '法的正式名' },
      { key: 'itemConversionCode', label: '工場コード' },
      { key: 'countryName', label: '国' },
      { key: 'supplierType', label: '区分' },
      { key: 'currencyCode', label: '通貨' },
      { key: 'drayageCost', label: 'ドレー代' },
    ],
  },
  {
    slug: 'departments',
    label: '事業部',
    extensionFields: [],
  },
  {
    slug: 'product-types',
    label: '商品タイプ',
    description: '商品タイプ × 性別の組合せマスタ。',
    extensionFields: [
      { key: 'itemConversionCode', label: '品番変換コード', type: 'text', required: true, maxLength: 1, placeholder: 'A', help: '11桁品番 2桁目' },
      { key: 'sizeDemographicCode', label: '性別コード', type: 'text', required: true, maxLength: 1, placeholder: 'R', help: 'R=Women, M=Men, J=Junior' },
    ],
    extensionColumns: [
      { key: 'itemConversionCode', label: 'タイプ' },
      { key: 'sizeDemographicCode', label: '性別' },
    ],
  },
  {
    slug: 'product-seasons',
    label: '商品季節',
    extensionFields: [
      { key: 'itemConversionCode', label: '品番変換コード', type: 'text', required: true, maxLength: 1, placeholder: '1', help: '11桁品番 3桁目' },
      { key: 'conversionOrder', label: '変換順', type: 'text', maxLength: 64, placeholder: '1,6,7,8,9', help: 'カンマ区切りで関連コードを記述' },
    ],
    extensionColumns: [
      { key: 'itemConversionCode', label: 'コード' },
      { key: 'conversionOrder', label: '変換順' },
    ],
  },
  {
    slug: 'product-groups',
    label: '商品群',
    extensionFields: [
      { key: 'planningFee', label: '企画費', type: 'decimal', required: true, help: '商品群あたりの企画費 (数値)' },
    ],
    extensionColumns: [{ key: 'planningFee', label: '企画費' }],
  },
  {
    slug: 'colors',
    label: '色',
    extensionFields: [
      { key: 'itemConversionCode', label: '品番変換コード', type: 'text', required: true, maxLength: 2, placeholder: '30', help: '11桁品番 8-9桁目' },
    ],
    extensionColumns: [{ key: 'itemConversionCode', label: 'コード' }],
  },
  {
    slug: 'materials',
    label: '素材',
    extensionFields: [
      { key: 'materialClassificationId', label: '素材分類', type: 'select-master', required: true, master: 'material-classifications' },
    ],
  },
  {
    slug: 'material-classifications',
    label: '素材分類',
    extensionFields: [],
  },
  {
    slug: 'warehouses',
    label: '倉庫',
    extensionFields: [],
  },
  {
    slug: 'delivery-destinations',
    label: '納品先',
    description: 'customer_name は内部識別用 (取引先名)。発注書帳票には使用しない。',
    extensionFields: [
      { key: 'customerName', label: '取引先名', type: 'text', maxLength: 255, help: 'しまむら / AEON 等 (内部識別用)' },
      { key: 'remark1', label: '物流発送先住所', type: 'text', maxLength: 255 },
      { key: 'remark2', label: '電話番号', type: 'text', maxLength: 255 },
      { key: 'remark3', label: 'FAX 番号', type: 'text', maxLength: 255 },
    ],
    extensionColumns: [{ key: 'customerName', label: '取引先名' }],
  },
  {
    slug: 'document-template-purchases',
    label: '連絡文書定型・発注書',
    description: 'M-05 発注書用の定型文書 (本文付き)。',
    extensionFields: [
      { key: 'body', label: '本文', type: 'textarea', required: true },
    ],
  },
  {
    slug: 'document-template-confirmations',
    label: '連絡文章・確認表',
    description: 'M-05 確認表用の定型文書。',
    extensionFields: [
      { key: 'body', label: '本文', type: 'textarea', required: true },
      { key: 'standardPrintFlag', label: '標準印字', type: 'checkbox', help: '帳票に標準で印字するか' },
    ],
    extensionColumns: [{ key: 'standardPrintFlag', label: '標準印字' }],
  },
  {
    slug: 'document-text-purchases',
    label: '連絡文章・発注書',
    description: 'M-05 発注書用の連絡文章 (注意書き等)。',
    extensionFields: [
      { key: 'body', label: '本文', type: 'textarea', required: true },
      { key: 'standardPrintFlag', label: '標準印字', type: 'checkbox' },
    ],
    extensionColumns: [{ key: 'standardPrintFlag', label: '標準印字' }],
  },
]

export const allFields = (def: MasterDef): FieldDef[] => [codeField, nameField, ...def.extensionFields]

export const findMasterBySlug = (slug: string): MasterDef | undefined =>
  masterDefinitions.find((m) => m.slug === slug)
