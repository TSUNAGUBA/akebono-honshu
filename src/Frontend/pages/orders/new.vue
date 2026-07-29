<script setup lang="ts">
import type { MasterItem } from '~/composables/useMasters'
import type { CreateOrderPayload, CommunicationSuggestion } from '~/composables/useOrders'

const { user } = useAuth()
const canCreateOrder = computed(() => (user.value?.purchaseOrderCreatePermission ?? 0) === 1)
const { list } = useMasters()
const { create, communicationSuggestions, priceSuggestion } = useOrders()
const { listFamiliesAll } = useProducts()
const { apiData } = useApi()
// スナックバー通知（必須未入力の押下時アラート）。共通の通知基盤 (composables/useSnackbar)。
const { showError } = useSnackbar()

// マスタ参照
const suppliers = ref<MasterItem[]>([])
const destinations = ref<MasterItem[]>([])
const departments = ref<MasterItem[]>([])
const warehouses = ref<MasterItem[]>([])

// SKU 候補 (products テーブル全件、簡素実装)
interface SkuOption {
  id: string
  sku: string
  productName: string
  colorName: string
  sizeName: string
}
const skus = ref<SkuOption[]>([])

// ユーザ候補 (発注担当者・管理者・副担当者用)
interface UserOption { id: string; loginId: string; displayName: string }
const users = ref<UserOption[]>([])

// 連絡文章テンプレ
const commTemplates = ref<CommunicationSuggestion[]>([])

const loading = ref(true)
const submitting = ref(false)
const errorMessage = ref('')

// 必須項目バリデーション状態（要件: 登録ボタン押下時に未入力項目を赤枠＋メッセージで明示）。
// キーはフォーム項目名。true = 未入力エラー。onSubmit の validate() で毎回再構築する。
const fieldErrors = ref<Record<string, boolean>>({})
// プレーン input の枠色（未入力なら赤）。MasterSelect / AutoComplete は :error prop で赤枠にする。
const errCls = (key: string): string => (fieldErrors.value[key] ? 'border-red-400' : 'border-gray-300')
// 明細行（複数行）の項目別エラー。行 index → { product?, quantity? }。validate() で再構築する。
const lineErrors = ref<Record<number, { product?: boolean; quantity?: boolean }>>({})

// フォーム
const today = todayJst() // 業務日付は JST 基準 (UTC 由来だと JST 00:00-08:59 に前日になる)
// 発注区分 (国内/海外)。Part6: 既定は海外。海外のみ表示・入力する項目 (発注先/荷揚地/工場出荷日/
// 納品所出荷日/海外出港日) を is_overseas で出し分ける。is_overseas は帳票の言語切替・一覧バッジにも使う。
const form = ref({
  orderNo: '',            // 発注書番号 (§5)。従来は初回 Excel 出力時に採番だが、作成時に手入力も可能 (任意)。
  // ID 系は uuid 文字列 (第二段階契約)。未選択は '' で表し、送信前の必須チェックで弾く。
  supplierId: '',          // 発注先 (仕入先マスタ、Part6: 海外のみ表示・入力)
  customerRef: '',        // 得意先 / 受注先 (国内/海外共通)
  deliveryDestinationId: '', // 納品先
  departmentId: '',        // 発注事業部
  landingPlace: '',       // 荷揚地 (Part6: 海外のみ)
  warehouseId: '',         // 納入倉庫1
  warehouse2Id: null as string | null, // 納入倉庫2 (国内/海外共通)
  warehouse3Id: null as string | null, // 納入倉庫3 (国内/海外共通)
  dueDate: today,         // 取引先納入日 (旧「納入日」、§5 名称変更)
  factoryShippingDate: '',        // 工場出荷日 (Part6: 海外のみ)
  deliveryPlaceShippingDate: '',  // 納品所出荷日 (列 delivery_place_shipping_date、Part6: 海外のみ)
  overseasDepartureDate: '',      // 海外出港日 (Part6: 海外のみ)
  ordererUserId: '',       // 発注担当者
  managerUserId: '',       // 発注管理者
  // 連絡文書 6 行 (構造化、PR6)。旧 spec 発注明細 No.27-32「連絡文書01行〜06行」。各行はテンプレ
  // 選択式 (連絡文書サジェスト再利用) + 自由編集可。新フローは本 6 列を SoT として送る (communicationText
  // は新フロントから書かない)。固定長 6 の配列で各スロットを保持する。
  communicationLines: ['', '', '', '', '', ''] as string[],
  // 発注区分 (国内=false/海外=true)。Part6: 既定は「海外」。海外のみ表示する項目 (発注先/荷揚地/
  // 工場出荷日/納品所出荷日/海外出港日) を発注区分で出し分ける。帳票の言語切替・区分バッジにも使う。
  isOverseas: true,
})

// 分納×倉庫の多次元明細 (PR5b)。1 明細を「(倉庫 × 納期) の分納行」の集合で多次元化する。
// 倉庫 / 納期 は任意 (null 許容)。数量は正の整数、入数は任意。
interface LineRow {
  productId: string
  quantity: number
  unitPriceSnapshot: number
  currencyCodeSnapshot: string
  // 旧 発注明細 項目 (Phase B、任意)。見積単価は §5 で発注書作成フォームから除外。
  packQuantity: number | null
  // 発注明細 備考 (spec 明細 No.26、任意)
  remark: string | null
  // 分納 (納期列マトリクス)。列 id → その納期の発注数。分納列が無ければ空 = 単一発注数 (quantity) を使う。
  deliveryQtys: Record<number, number | null>
}
// 明細は空から始める (§改修 2026-07)。ユーザが「+ 明細追加」で明示的に行を追加する。
const lines = ref<LineRow[]>([])

// 分納 (納期列マトリクス)。「分納設定」モーダルで納期 (日付) 列を管理し、SKU 明細行ごとに
// 納期別の発注数を入力する。列は全明細で共有。列が 1 つ以上あるとき分納モード = 発注数は各列の合計。
interface DeliveryColumn { id: number; date: string }
const deliveryColumns = ref<DeliveryColumn[]>([])
const nextDeliveryColId = ref(1)
const deliveryMode = computed(() => deliveryColumns.value.length > 0)

// 納期列を 1 列追加する。date 省略時は空 (ユーザがモーダルの列ヘッダで日付を選ぶ)。
const addDeliveryColumn = (date = '') => {
  const colId = nextDeliveryColId.value++
  const isFirst = deliveryColumns.value.length === 0
  deliveryColumns.value.push({ id: colId, date })
  // 最初の納期列を追加したとき、各明細の現在の発注数をこの列に移す (合計を維持)。
  if (isFirst) {
    for (const l of lines.value) l.deliveryQtys[colId] = Number(l.quantity) || 0
  }
}
// 納期列を 1 列削除する。入力済みの数量は失わない (原則2/7):
// 残り列があれば先頭の残存列へ数量を合算、最後の 1 列なら合計を単一発注数へ戻して分納解除。
const removeDeliveryColumn = (colId: number) => {
  const remaining = deliveryColumns.value.filter((c) => c.id !== colId)
  if (remaining.length === 0) {
    clearDeliveryColumns()
    return
  }
  const fallbackId = remaining[0].id
  for (const l of lines.value) {
    const q = Number(l.deliveryQtys[colId]) || 0
    if (q > 0) l.deliveryQtys[fallbackId] = (Number(l.deliveryQtys[fallbackId]) || 0) + q
    delete l.deliveryQtys[colId]
  }
  deliveryColumns.value = remaining
}
// 分納を解除する。全納期列を削除し、各明細の合計を単一発注数へ戻す (入力を失わない)。
const clearDeliveryColumns = () => {
  for (const l of lines.value) {
    l.quantity = lineMatrixTotal(l)
    l.deliveryQtys = {}
  }
  deliveryColumns.value = []
  showDeliveryModal.value = false
}
// 明細の分納合計 (全納期列の数量合計)。
const lineMatrixTotal = (l: LineRow): number =>
  deliveryColumns.value.reduce((s, c) => s + (Number(l.deliveryQtys[c.id]) || 0), 0)

// --- 分納設定モーダル (§改修 2026-07) ---
// 分納は④明細のインライン列ではなくモーダルで設定する。品番/色/サイズ/品名を固定列 (sticky) で
// 表示し、日付列は横スクロール領域に置く。初回オープン時は日付列 5 列をデフォルト配置する。
const DELIVERY_DEFAULT_COLS = 5
const showDeliveryModal = ref(false)
const openDeliveryModal = () => {
  if (lines.value.length === 0) return
  if (deliveryColumns.value.length === 0) {
    // 先頭列は取引先納入日を既定にし (従来挙動)、残りは空 = ユーザが日付を選ぶ。
    // 最初の列追加で各明細の発注数が先頭列へ移り、合計は維持される (addDeliveryColumn)。
    for (let i = 0; i < DELIVERY_DEFAULT_COLS; i++) {
      addDeliveryColumn(i === 0 ? (form.value.dueDate || '') : '')
    }
  }
  showDeliveryModal.value = true
}
// 末尾の日付列を削除する (モーダルの「− 列を削除」)。最後の 1 列はボタンから消さない
// (分納の解除は「分納を解除」で明示的に行う)。
const removeLastDeliveryColumn = () => {
  if (deliveryColumns.value.length <= 1) return
  removeDeliveryColumn(deliveryColumns.value[deliveryColumns.value.length - 1].id)
}
// 固定表示する明細の識別列 (品番/色/サイズ/品名)。lg 未満では sticky を外して通常の横スクロールに
// する (狭幅で固定すると日付入力列が画面外になるため。原則8)。left は先行列の累積幅。
const DELIVERY_FIXED_COLS = [
  { key: 'sku', label: '品番', cls: 'w-[124px] min-w-[124px] lg:sticky lg:left-0' },
  { key: 'color', label: '色', cls: 'w-[88px] min-w-[88px] lg:sticky lg:left-[124px]' },
  { key: 'size', label: 'サイズ', cls: 'w-[72px] min-w-[72px] lg:sticky lg:left-[212px]' },
  { key: 'name', label: '品名', cls: 'w-[168px] min-w-[168px] lg:sticky lg:left-[284px]' },
] as const
// SKU 参照は明細行 × 固定列 4 つ分だけ引くため、線形探索を避けて Map で解決する。
const skuMap = computed(() => new Map(skus.value.map((s) => [s.id, s])))
// 固定列のセル表示値。SKU 未選択の明細は「—」(モーダルからは SKU を選ばせない)。
const deliveryFixedText = (l: LineRow, key: string): string => {
  const s = skuMap.value.get(l.productId)
  if (!s) return '—'
  if (key === 'sku') return s.sku
  if (key === 'color') return s.colorName
  if (key === 'size') return s.sizeName
  return s.productName
}
// 日付列ごとの数量合計 (モーダルのフッタ集計)。
const deliveryColumnTotal = (colId: number): number =>
  lines.value.reduce((s, l) => s + (Number(l.deliveryQtys[colId]) || 0), 0)
// 数量が入っているのに納期日付が未設定の列。送信時 deliveryDate=null になるため注意喚起する。
const deliveryColumnsMissingDate = computed(() =>
  deliveryColumns.value.filter((c) => !c.date && deliveryColumnTotal(c.id) > 0),
)
// モーダル表示中は Escape で閉じられるようにする (U-5 アクセシビリティ)。表示中のみリスナを張る。
const onDeliveryModalKeydown = (e: KeyboardEvent) => {
  if (e.key === 'Escape') showDeliveryModal.value = false
}
watch(showDeliveryModal, (open) => {
  if (import.meta.server) return
  if (open) window.addEventListener('keydown', onDeliveryModalKeydown)
  else window.removeEventListener('keydown', onDeliveryModalKeydown)
})
onBeforeUnmount(() => {
  if (import.meta.server) return
  window.removeEventListener('keydown', onDeliveryModalKeydown)
})

// --- オートコンプリート選択肢（マスタ参照を部分一致検索可能に） ---
const userOptions = computed(() => users.value.map((u) => ({ id: u.id, label: `${u.displayName} (${u.loginId})` })))
const skuOptions = computed(() => skus.value.map((s) => ({ id: s.id, label: `${s.sku} - ${s.productName} (${s.colorName}/${s.sizeName})`, code: s.sku })))
// 副担当者 1〜6 は発注書作成フォームから除外 (§5)。DTO 列は後方互換のため残すが、作成時は常に null を送る。

onMounted(async () => {
  try {
    const [sup, dest, dept, wh, comm, family, usrRes] = await Promise.all([
      list('suppliers'),
      list('delivery-destinations'),
      list('departments'),
      list('warehouses'),
      communicationSuggestions(),
      // 商品企画から全 SKU を引いてくる。一覧 API はページングされるため、
      // 候補の全量が必要な本画面はカーソルを終端まで辿る listFamiliesAll を使う (§7.2)。
      listFamiliesAll(false),
      apiData<UserOption[]>('/users'),
    ])
    suppliers.value = sup
    destinations.value = dest
    departments.value = dept
    warehouses.value = wh
    commTemplates.value = comm
    users.value = usrRes

    // 各 family の詳細を並列取得して SKU リスト構築
    const skuLists = await Promise.all(
      family.map((f) =>
        apiData<{ products: { id: string; sku: string; colorName: string; sizeName: string }[] }>(`/products/families/${f.id}`)
          .then((d) =>
            d.products.map((p) => ({
              id: p.id,
              sku: p.sku,
              productName: f.productName1,
              colorName: p.colorName,
              sizeName: p.sizeName,
            })),
          ),
      ),
    )
    skus.value = skuLists.flat()

    // 初期値設定: 必須項目（発注先/納品先/発注事業部/納入倉庫1/発注担当者/発注管理者）は
    // デフォルト未選択とする（要件: 共通の新規登録フォーム方針）。以前は各マスタの先頭を自動選択して
    // いたが、利用者が意図せず登録する事故を避けるため空のままにし、登録ボタン押下時の validate() で
    // 未選択を赤枠＋スナックバーで明示する。
    // 明細は空から始める (§改修 2026-07)。SKU の自動選択・分納列の自動作成は行わない。
    // 分納はユーザが「分納設定」モーダルを開いたときに日付列 5 列をデフォルト配置する。
  } catch (e) {
    errorMessage.value = 'マスタ情報の取得に失敗しました'
  } finally {
    loading.value = false
  }
})

const addLine = () => {
  // 分納モード (納期列あり) では新規明細もすぐ有効 (発注数>0) になるよう、先頭納期列に既定数量 1 を入れる。
  // (deliveryQtys が空だと分納合計 0 で lineQuantityValid=false となり、追加直後は送信不可になるため。
  //  分納なしモードでは quantity=1 が既定で有効。)
  const deliveryQtys: Record<number, number | null> = {}
  if (deliveryColumns.value.length > 0) deliveryQtys[deliveryColumns.value[0].id] = 1
  // SKU は自動選択しない (§改修 2026-07: 明細は空から始め、ユーザが明示的に選ぶ)。
  // 単価サジェストは SKU 選択時 (onLineProductChange) に行う。
  lines.value.push({
    productId: '',
    quantity: 1,
    unitPriceSnapshot: 0,
    currencyCodeSnapshot: 'JPY',
    packQuantity: null,
    remark: null,
    deliveryQtys,
  })
}

// 明細は 0 件まで削除できる (§改修 2026-07: 初期状態が空のため、最後の 1 行の削除も許可)。
const removeLine = (idx: number) => {
  lines.value.splice(idx, 1)
}

// 明細の発注数。分納モード (納期列あり) は各納期列の合計、無ければ単一の quantity。
// サーバ側も line.Quantity = 分納数量の SUM に再計算するため、UI 表示とサーバ保存が一致する。
const lineQuantity = (l: LineRow): number =>
  deliveryMode.value ? lineMatrixTotal(l) : Number(l.quantity) || 0

// サイズ別仕入単価 (PR2)。SKU の size に対応する現単価を発注先からサジェストし、明細の単価/通貨を
// 自動補完する (入力補助)。フォールバック: (family, supplier, SKUのsize) → 無ければ (…, 全サイズ既定)。
// 非ブロッキング (CLAUDE.md 原則4): 取得失敗・現単価なしの場合は既存値を保持し、ユーザは手入力できる。
// サーバ側で snapshot を上書きしないため、ここで補完した値も保存前にユーザが自由に編集可能。
//
// 非破壊サジェスト (reviewer 指摘 Minor 対応): 単価が未入力相当 (<=0、「単価未決定」の既定値) の明細
// にのみ補完する。ユーザが手入力済の単価は SKU/発注先を変えても上書きしない。force=true (SKU 明示選択時)
// は新しい SKU の現単価を反映するため上書きを許す。
const applyPriceSuggestion = async (idx: number, force = false) => {
  const line = lines.value[idx]
  if (!line || !line.productId || !form.value.supplierId) return
  // 発注先変更時 (force=false) は手入力済 (>0) の単価を保護する。
  if (!force && line.unitPriceSnapshot > 0) return
  try {
    const sug = await priceSuggestion(line.productId, form.value.supplierId)
    if (sug.found && sug.unitPrice != null) {
      line.unitPriceSnapshot = sug.unitPrice
      if (sug.currencyCode) line.currencyCodeSnapshot = sug.currencyCode
    }
  } catch (e) {
    // 単価サジェストの失敗は主要フロー (発注作成) を止めない。手入力で続行可能。
    console.error('単価サジェスト取得に失敗しました (手入力で続行可能)', e)
  }
}

// SKU 選択変更時に単価を再サジェスト (明示選択なので force=true で新 SKU の現単価を反映)。
const onLineProductChange = (idx: number, v: string | null) => {
  lines.value[idx].productId = v ?? ''
  applyPriceSuggestion(idx, true)
}

// 発注先 (ヘッダ) 変更時は全明細の単価を再サジェスト (supplier 由来の現単価が変わるため)。
// 手入力済 (>0) の明細は保護する (force=false)。
watch(() => form.value.supplierId, () => {
  lines.value.forEach((_, idx) => applyPriceSuggestion(idx))
})

// 小計 = 数量 × 単価。数量は分納合計 (lineQuantity) を使うため、分納展開しても合計は整合する。
const lineSubtotal = (l: LineRow) => lineQuantity(l) * l.unitPriceSnapshot
// 通貨ごとの合計。明細は行ごとに通貨が異なりうるため、通貨単位で集計する
// (「合計 X 円」固定表示だと USD 等の外貨と表示が一致しない問題の修正)。
const totalsByCurrency = computed<Record<string, number>>(() => {
  const m: Record<string, number> = {}
  for (const l of lines.value) {
    const cur = l.currencyCodeSnapshot || 'JPY'
    m[cur] = (m[cur] ?? 0) + lineSubtotal(l)
  }
  return m
})
// サマリ文字列 (例: "USD 150" / 混在時 "USD 150 / JPY 1,000")。
const totalSummary = computed(() => {
  const parts = Object.entries(totalsByCurrency.value)
    .filter(([, v]) => v !== 0)
    .map(([cur, v]) => `${cur} ${v.toLocaleString()}`)
  return parts.length > 0 ? parts.join(' / ') : 'JPY 0'
})

// 連絡文書 6 行 (PR6): テンプレ選択肢を AutoComplete 用に変換 (value=本文、label=出典ラベル)。
// 同一本文の重複は value 重複になるため出典ラベルを付して一意化する。
const commTemplateOptions = computed(() =>
  commTemplates.value.map((t, i) => ({ value: `${i}`, label: t.sourceLabel, searchText: t.body })),
)
// テンプレを指定スロットに適用する。AutoComplete は index 文字列を value にするため body を引き直す。
const applyTemplateToLine = (slot: number, optionValue: string) => {
  // 「（選択しない）」= 空値は no-op（AutoComplete が '' を emit する）。
  // Number('') === 0 で commTemplates[0] を引いてしまうと、手入力済みの本文を
  // 「テンプレを適用しないつもり」の操作で無言上書きする（データ消失・OD-2）。
  if (optionValue === '') return
  const idx = Number(optionValue)
  const tpl = commTemplates.value[idx]
  if (tpl && slot >= 0 && slot < form.value.communicationLines.length) {
    form.value.communicationLines[slot] = tpl.body
  }
}

// 明細の数量妥当性: 分納なしは単一 quantity > 0、分納あり (納期列) は各明細の合計 > 0。
const lineQuantityValid = (l: LineRow): boolean => lineQuantity(l) > 0

// 必須項目バリデーション。未入力項目に赤枠フラグを立て、未入力ラベルの一覧を返す。
// 返り値が空なら送信可能。押下時に呼ぶ（必須未入力でボタンを無効化しない要件のため）。
const validate = (): string[] => {
  const errs: Record<string, boolean> = {}
  const missing: string[] = []
  const req = (empty: boolean, key: string, label: string) => {
    if (empty) { errs[key] = true; missing.push(label) }
  }
  // 発注先は海外のみ必須 (Part6: 国内は非表示・任意)。
  if (form.value.isOverseas) req(form.value.supplierId === '', 'supplierId', '発注先')
  req(form.value.deliveryDestinationId === '', 'deliveryDestinationId', '納品先')
  req(form.value.departmentId === '', 'departmentId', '発注事業部')
  req(form.value.warehouseId === '', 'warehouseId', '納入倉庫1')
  req(form.value.dueDate === '', 'dueDate', '取引先納入日')
  req(form.value.ordererUserId === '', 'ordererUserId', '発注担当者')
  req(form.value.managerUserId === '', 'managerUserId', '発注管理者')
  // 明細は空から始まるため 1 件以上を必須にする。各行は SKU 選択 + 発注数>0 を検査し、行別に赤枠を立てる。
  const rowErrs: Record<number, { product?: boolean; quantity?: boolean }> = {}
  req(lines.value.length === 0, 'lines', '明細')
  let anyLineMissing = false
  lines.value.forEach((l, idx) => {
    const e: { product?: boolean; quantity?: boolean } = {}
    if (l.productId === '') { e.product = true; anyLineMissing = true }
    if (!lineQuantityValid(l)) { e.quantity = true; anyLineMissing = true }
    if (e.product || e.quantity) rowErrs[idx] = e
  })
  if (anyLineMissing) missing.push('明細')
  fieldErrors.value = errs
  lineErrors.value = rowErrs
  return missing
}

const onSubmit = async () => {
  errorMessage.value = ''
  const missing = validate()
  if (missing.length > 0) {
    const msg = `必須項目が未入力です（${missing.join(' / ')}）`
    errorMessage.value = msg
    showError(msg) // スナックバーでも警告（要件: 押下時アラート）
    return
  }
  submitting.value = true
  try {
    const payload: CreateOrderPayload = {
      // 発注書番号 (§5)。空欄は null (初回 Excel 出力時に自動採番される従来挙動にフォールバック)。
      orderNo: form.value.orderNo.trim() || null,
      // 発注先は海外のみ (Part6)。国内は null を送る (発注先=仕入先マスタは任意)。
      supplierId: form.value.isOverseas ? (form.value.supplierId || null) : null,
      deliveryDestinationId: form.value.deliveryDestinationId,
      departmentId: form.value.departmentId,
      warehouseId: form.value.warehouseId,
      dueDate: form.value.dueDate,
      ordererUserId: form.value.ordererUserId,
      managerUserId: form.value.managerUserId,
      // 副担当者 1〜6 は作成フォームから除外 (§5)。後方互換のため列は残すが常に null を送る。
      subOrderer1UserId: null,
      subOrderer2UserId: null,
      subOrderer3UserId: null,
      subOrderer4UserId: null,
      subOrderer5UserId: null,
      subOrderer6UserId: null,
      // 連絡文書 6 行 (構造化、PR6)。新フローは本 6 列を SoT として送る (空欄は null)。
      // communicationText は新規作成では書かない (旧データ専用フォールバック列のため null)。
      communicationText: null,
      communicationLine1: form.value.communicationLines[0].trim() || null,
      communicationLine2: form.value.communicationLines[1].trim() || null,
      communicationLine3: form.value.communicationLines[2].trim() || null,
      communicationLine4: form.value.communicationLines[3].trim() || null,
      communicationLine5: form.value.communicationLines[4].trim() || null,
      communicationLine6: form.value.communicationLines[5].trim() || null,
      lines: lines.value.map((l) => ({
        productId: l.productId,
        // 分納あり明細は quantity = 分納合計 (lineQuantity)。サーバも SUM に再計算するが、整合のため
        // 合計値を送る。分納なしは単一 quantity をそのまま送る (従来挙動)。
        quantity: lineQuantity(l),
        unitPriceSnapshot: Number(l.unitPriceSnapshot),
        currencyCodeSnapshot: l.currencyCodeSnapshot,
        packQuantity: l.packQuantity != null ? Number(l.packQuantity) : null,
        // 見積単価は発注書作成フォームから除外 (§5)。後方互換のため列は残すが常に null を送る。
        estimateUnitPrice: null,
        // 発注明細 備考 (spec 明細 No.26)。空欄は null で送る。
        remark: l.remark?.trim() || null,
        // 分納 (納期列マトリクス)。分納モードなら数量>0 の納期列を分納行として送る (倉庫はヘッダ管理のため null)。
        // 分納なし (納期列 0) は null (単一明細、従来挙動)。
        deliveries: deliveryMode.value
          ? deliveryColumns.value
              .map((c) => ({
                warehouseId: null as string | null,
                deliveryDate: c.date || null,
                quantity: Number(l.deliveryQtys[c.id]) || 0,
                packQuantity: null as number | null,
              }))
              .filter((d) => d.quantity > 0)
          : null,
      })),
      // 発注区分 (国内/海外)。§5 で入力項目は国内/海外共通に統一したため、以下の項目は区分に関わらず常に送る。
      // is_overseas は帳票の言語切替・区分バッジのみに使う。
      isOverseas: form.value.isOverseas,
      // 荷揚地/工場出荷日/納品所出荷日/海外出港日は海外のみ (Part6)。国内は null 送信。
      // 得意先 (customerRef) は海外/国内共通のため常に送る。
      landingPlace: form.value.isOverseas ? (form.value.landingPlace.trim() || null) : null,
      customerRef: form.value.customerRef.trim() || null,
      factoryShippingDate: form.value.isOverseas ? (form.value.factoryShippingDate || null) : null,
      deliveryPlaceShippingDate: form.value.isOverseas ? (form.value.deliveryPlaceShippingDate || null) : null,
      overseasDepartureDate: form.value.isOverseas ? (form.value.overseasDepartureDate || null) : null,
      warehouse2Id: form.value.warehouse2Id,
      warehouse3Id: form.value.warehouse3Id,
    }
    const res = await create(payload)
    await navigateTo(`/orders/${res.id}`)
  } catch (e) {
    errorMessage.value = getApiErrorMessage(e, '発注書作成に失敗しました')
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <main class="mx-auto max-w-screen-2xl px-3 py-3">
    <div v-if="!canCreateOrder" class="rounded border border-red-300 bg-red-50 p-4 text-red-700">
      発注書作成権限がありません。
      <div class="mt-2">
        <NuxtLink to="/orders" class="text-blue-600 underline">発注書一覧に戻る</NuxtLink>
      </div>
    </div>

    <template v-else>
      <header class="mb-3">
        <div class="text-xs text-gray-500">
          <NuxtLink to="/orders" class="hover:underline">発注書</NuxtLink>
          <span class="mx-1">/</span>
          <span>新規作成</span>
        </div>
        <h1 class="text-2xl font-bold">新規発注書 (O-01)</h1>
        <p class="mt-1 text-sm text-gray-500">
          管理番号は保存時に自動採番、発注番号は初回 Excel 出力時に採番されます (BR-03)。
        </p>
      </header>

      <div v-if="loading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500">
        マスタ情報を読み込み中…
      </div>

      <form v-else class="space-y-2.5" @submit.prevent="onSubmit">
        <!-- ① 発注区分 (海外/国内)。Part6: 並びは「海外」→「国内」、既定は「海外」。海外のみ表示する
             項目 (発注先/荷揚地/工場出荷日/納品所出荷日/海外出港日) を区分で出し分ける。区分は帳票の言語
             切替 (国内=日本語/海外=英語) と一覧の区分バッジにも使う。 -->
        <section class="rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
          <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <h2 class="font-semibold">① 発注区分 <span class="text-red-500">*</span></h2>
              <p class="mt-0.5 text-xs text-gray-500">
                海外 / 国内 を選択してください。海外のみ「発注先・荷揚地・工場出荷日・納品所出荷日・海外出港日」を入力します。帳票の言語 (国内=日本語 / 海外=英語) と一覧の区分にも使われます。
              </p>
            </div>
            <!-- 発注区分 海外/国内 セグメントトグル (is_overseas、二択モードスイッチ、海外を先頭に) -->
            <div class="inline-flex self-start overflow-hidden rounded-md border border-gray-300 text-sm">
              <button
                type="button"
                class="px-6 py-1.5 font-medium transition-colors"
                :class="form.isOverseas ? 'bg-blue-600 text-white' : 'bg-white text-gray-600 hover:bg-gray-50'"
                @click="form.isOverseas = true"
              >海外</button>
              <button
                type="button"
                class="border-l border-gray-300 px-6 py-1.5 font-medium transition-colors"
                :class="!form.isOverseas ? 'bg-blue-600 text-white' : 'bg-white text-gray-600 hover:bg-gray-50'"
                @click="form.isOverseas = false"
              >国内</button>
            </div>
          </div>
        </section>

        <!-- ② 発注書ヘッダ (国内/海外共通、§5)。発注先/得意先/納品先・荷揚地・納入倉庫1〜3・各出荷日を統一入力。 -->
        <section class="rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
          <div class="mb-3 flex flex-col gap-3 border-b border-gray-100 pb-3 sm:flex-row sm:items-center sm:justify-between">
            <h2 class="font-semibold">② 発注書ヘッダ</h2>
          </div>

          <!-- 取引先系: 発注書番号 / 発注先 / 得意先 / 納品先 -->
          <div class="grid grid-cols-1 gap-x-3 gap-y-2 text-sm sm:grid-cols-2 lg:grid-cols-3">
            <label class="flex flex-col gap-1">
              <span class="font-medium">発注書番号</span>
              <input v-model="form.orderNo" type="text" maxlength="16" placeholder="例: S3858 (未入力なら初回出力時に自動採番)" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
            </label>
            <!-- 発注先 (仕入先マスタ)。Part6: 海外のみ表示・必須。国内は非表示 (supplierId は null 送信)。 -->
            <label v-if="form.isOverseas" class="flex flex-col gap-1">
              <span class="font-medium">発注先 <span class="text-red-500">*</span></span>
              <MasterSelect v-model="form.supplierId" :items="suppliers" :error="fieldErrors.supplierId" />
              <span v-if="fieldErrors.supplierId" class="text-xs text-red-600">発注先を選択してください</span>
            </label>
            <label class="flex flex-col gap-1">
              <span class="font-medium">得意先</span>
              <input v-model="form.customerRef" type="text" maxlength="128" placeholder="得意先 / 受注先" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="font-medium">納品先 <span class="text-red-500">*</span></span>
              <MasterSelect v-model="form.deliveryDestinationId" :items="destinations" :error="fieldErrors.deliveryDestinationId" />
              <span v-if="fieldErrors.deliveryDestinationId" class="text-xs text-red-600">納品先を選択してください</span>
            </label>
          </div>

          <!-- 事業部 / 荷揚地 / 納入倉庫1〜3 -->
          <div class="mt-3 grid grid-cols-1 gap-x-3 gap-y-2 text-sm sm:grid-cols-2 lg:grid-cols-3">
            <label class="flex flex-col gap-1">
              <span class="font-medium">発注事業部 <span class="text-red-500">*</span></span>
              <MasterSelect v-model="form.departmentId" :items="departments" :error="fieldErrors.departmentId" />
              <span v-if="fieldErrors.departmentId" class="text-xs text-red-600">発注事業部を選択してください</span>
            </label>
            <!-- 荷揚地。Part6: 海外のみ表示。 -->
            <label v-if="form.isOverseas" class="flex flex-col gap-1">
              <span class="font-medium">荷揚地</span>
              <input v-model="form.landingPlace" type="text" maxlength="128" placeholder="Port of entry" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="font-medium">納入倉庫1 <span class="text-red-500">*</span></span>
              <MasterSelect v-model="form.warehouseId" :items="warehouses" :error="fieldErrors.warehouseId" />
              <span v-if="fieldErrors.warehouseId" class="text-xs text-red-600">納入倉庫1を選択してください</span>
            </label>
            <label class="flex flex-col gap-1">
              <span class="font-medium">納入倉庫2</span>
              <MasterSelect v-model="form.warehouse2Id" :items="warehouses" allow-empty empty-label="（なし）" placeholder="（任意）" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="font-medium">納入倉庫3</span>
              <MasterSelect v-model="form.warehouse3Id" :items="warehouses" allow-empty empty-label="（なし）" placeholder="（任意）" />
            </label>
          </div>

          <!-- 日付: 取引先納入日 / 工場出荷日 / 納品所出荷日 / 海外出港日 (工場出荷日以降は Part6: 海外のみ) -->
          <div class="mt-3 grid grid-cols-1 gap-x-3 gap-y-2 text-sm sm:grid-cols-2 lg:grid-cols-4">
            <label class="flex flex-col gap-1">
              <span class="font-medium">取引先納入日 <span class="text-red-500">*</span></span>
              <input v-model="form.dueDate" type="date" class="rounded-md border px-2.5 py-1.5" :class="errCls('dueDate')" />
              <span v-if="fieldErrors.dueDate" class="text-xs text-red-600">取引先納入日を入力してください</span>
            </label>
            <!-- 工場出荷日 / 納品所出荷日 / 海外出港日。Part6: 海外のみ表示。 -->
            <label v-if="form.isOverseas" class="flex flex-col gap-1">
              <span class="font-medium">工場出荷日</span>
              <input v-model="form.factoryShippingDate" type="date" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
            </label>
            <label v-if="form.isOverseas" class="flex flex-col gap-1">
              <span class="font-medium">納品所出荷日</span>
              <input v-model="form.deliveryPlaceShippingDate" type="date" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
            </label>
            <label v-if="form.isOverseas" class="flex flex-col gap-1">
              <span class="font-medium">海外出港日</span>
              <input v-model="form.overseasDepartureDate" type="date" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
            </label>
          </div>

          <!-- 担当: 発注担当者 / 発注管理者 (副担当者1〜6 は §5 で除外) -->
          <div class="mt-3 grid grid-cols-1 gap-x-3 gap-y-2 text-sm sm:grid-cols-2 lg:grid-cols-3">
            <label class="flex flex-col gap-1">
              <span class="font-medium">発注担当者 <span class="text-red-500">*</span></span>
              <MasterSelect v-model="form.ordererUserId" :items="userOptions" :error="fieldErrors.ordererUserId" />
              <span v-if="fieldErrors.ordererUserId" class="text-xs text-red-600">発注担当者を選択してください</span>
            </label>
            <label class="flex flex-col gap-1">
              <span class="font-medium">発注管理者 <span class="text-red-500">*</span></span>
              <MasterSelect v-model="form.managerUserId" :items="userOptions" :error="fieldErrors.managerUserId" />
              <span v-if="fieldErrors.managerUserId" class="text-xs text-red-600">発注管理者を選択してください</span>
            </label>
          </div>
        </section>

        <!-- ④ 明細。初期状態は空で「+ 明細追加」から行を追加する (§改修 2026-07)。
             分納は「分納設定」モーダルで納期 (日付) 列を管理し、SKU × 納期の数量を入力する。
             分納設定済みのとき発注数は納期列の合計 (読取表示)。納期列が無ければ単一の発注数を使う。
             合計は通貨ごとに集計して表示する (選択通貨と表示を一致させる)。 -->
        <!-- overflow-x-auto は「テーブルのみ」を包む内側 div に付ける。セクション自体には付けない
             ことで、見出し・ボタン (分納設定/明細追加)・説明文は横スクロールしても固定される。 -->
        <section class="rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
          <div class="mb-3 flex items-center justify-between border-b border-gray-100 pb-2">
            <h2 class="font-semibold">④ 明細 ({{ lines.length }} 件、合計 {{ totalSummary }})</h2>
            <div class="flex items-center gap-2">
              <!-- 分納設定: モーダルを開いて納期 (日付) 列と SKU × 納期の数量を入力する。
                   明細が無いときは設定対象が無いため非活性 (「+ 明細追加」の左に配置)。 -->
              <button
                type="button"
                :disabled="lines.length === 0"
                :title="lines.length === 0 ? '先に明細を追加してください' : undefined"
                class="rounded-md border border-blue-300 bg-blue-50 px-3 py-1 text-sm text-blue-700 hover:bg-blue-100 disabled:opacity-40"
                @click="openDeliveryModal"
              >分納設定{{ deliveryMode ? ` (${deliveryColumns.length} 列)` : '' }}</button>
              <button type="button" class="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50" @click="addLine">+ 明細追加</button>
            </div>
          </div>
          <!-- スプレッドシート風の高密度明細 (Part7)。枠線をセル境界のグリッド線に集約し、各セルの入力は
               枠なしでセルを埋める (Excel/表計算のような入力感)。フォーカス時のみ淡い青背景で編集セルを示す。
               outlined な個別コントロールをやめることで、業務入力としての見やすさ・視認性を上げる。 -->
          <div class="overflow-x-auto rounded-md border border-gray-200">
          <table class="w-full border-separate border-spacing-0 text-sm">
            <thead class="bg-gray-50 text-gray-600">
              <tr>
                <th class="border-b border-r border-gray-200 px-2 py-1 text-left font-semibold">SKU</th>
                <th class="border-b border-r border-gray-200 px-2 py-1 text-right font-semibold">発注数</th>
                <th class="border-b border-r border-gray-200 px-2 py-1 text-right font-semibold">入数</th>
                <th class="border-b border-r border-gray-200 px-2 py-1 text-right font-semibold">仕入単価</th>
                <th class="border-b border-r border-gray-200 px-2 py-1 text-left font-semibold">通貨</th>
                <th class="border-b border-r border-gray-200 px-2 py-1 text-left font-semibold">備考</th>
                <th class="border-b border-r border-gray-200 px-2 py-1 text-right font-semibold">小計</th>
                <th class="border-b border-r border-gray-200 px-2 py-1 text-right font-semibold"></th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(l, idx) in lines" :key="idx">
                <td class="border-b border-r border-gray-200 p-0">
                  <!-- サイズ別仕入単価 (PR2): SKU 選択時に単価をサジェスト補完 (onLineProductChange)。 -->
                  <MasterSelect :model-value="l.productId" :items="skuOptions" borderless :error="lineErrors[idx]?.product" placeholder="SKU・品名で検索…" @update:model-value="(v) => onLineProductChange(idx, v)" />
                </td>
                <td class="border-b border-r border-gray-200 p-0 text-right">
                  <!-- 分納設定済み (納期列あり) は発注数 = 各納期列の合計を読取表示。分納なしは単一入力。 -->
                  <input v-if="!deliveryMode" v-model.number="l.quantity" type="number" min="1" class="sheet-input h-8 text-right" :class="lineErrors[idx]?.quantity ? 'ring-1 ring-inset ring-red-400' : ''" />
                  <span v-else class="block w-full px-2 text-right font-mono leading-8 text-gray-700" title="納期列の合計 (変更は「分納設定」から)">{{ lineQuantity(l).toLocaleString() }}</span>
                </td>
                <td class="border-b border-r border-gray-200 p-0 text-right">
                  <input v-model.number="l.packQuantity" type="number" min="0" placeholder="—" class="sheet-input h-8 text-right" />
                </td>
                <td class="border-b border-r border-gray-200 p-0 text-right">
                  <input v-model.number="l.unitPriceSnapshot" type="number" min="0" step="0.01" class="sheet-input h-8 text-right" />
                </td>
                <td class="border-b border-r border-gray-200 p-0">
                  <AutoComplete :model-value="l.currencyCodeSnapshot" :options="[{ value: 'JPY', label: 'JPY' }, { value: 'USD', label: 'USD' }, { value: 'CNY', label: 'CNY' }]" :allow-empty="false" borderless @update:model-value="(v) => l.currencyCodeSnapshot = v" />
                </td>
                <td class="border-b border-r border-gray-200 p-0">
                  <input v-model="l.remark" type="text" maxlength="255" placeholder="—" class="sheet-input h-8" />
                </td>
                <!-- 小計は行の通貨を前置して表示 (選択通貨とサマリ表示を一致させる)。 -->
                <td class="whitespace-nowrap border-b border-r border-gray-200 px-2 py-1 text-right font-mono">{{ l.currencyCodeSnapshot }} {{ lineSubtotal(l).toLocaleString() }}</td>
                <td class="border-b border-r border-gray-200 px-2 py-1 text-center">
                  <button type="button" class="text-xs text-red-600 hover:underline" @click="removeLine(idx)">削除</button>
                </td>
              </tr>
              <!-- 空状態 (初期表示)。明細は空から始まるため、追加導線を明示する。
                   登録押下で未入力 (明細 0 件) が検出された場合は赤字で警告する。 -->
              <tr v-if="lines.length === 0">
                <td colspan="8" class="px-2 py-6 text-center" :class="fieldErrors.lines ? 'text-red-600' : 'text-gray-500'">
                  明細がありません。「+ 明細追加」で SKU を追加してください。
                </td>
              </tr>
            </tbody>
          </table>
          </div>
          <p v-if="Object.keys(lineErrors).length > 0" class="mt-2 text-xs text-red-600">
            未入力の明細があります（赤枠の SKU 未選択、または発注数が 0 の行）。SKU を選択し発注数を入力してください。
          </p>
          <p v-if="deliveryMode" class="mt-2 text-xs text-gray-500">
            分納設定済み: 各 SKU の発注数は納期列の合計です。数量・納期の変更は「分納設定」から行います。「分納を解除」すると単一発注数の入力に戻ります (入力済みの合計は発注数に引き継がれます)。
          </p>
        </section>

        <!-- 連絡文書 6 行 (構造化、PR6。旧 spec 発注明細 No.27-32「連絡文書01行〜06行」) -->
        <section class="rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
          <div class="mb-3 flex flex-col gap-1 border-b border-gray-100 pb-2 sm:flex-row sm:items-center sm:justify-between">
            <h2 class="font-semibold">⑤ 連絡文書 (O-07、6 行)</h2>
            <p class="text-xs text-gray-500">各行はテンプレ選択 + 自由編集できます (空行は出力されません)。</p>
          </div>
          <!-- 6 行スロット。各行: テンプレ選択 (AutoComplete) + テキスト入力。モバイルは縦積み (原則8)。 -->
          <div class="space-y-2">
            <div
              v-for="(_, i) in form.communicationLines"
              :key="i"
              class="flex flex-col gap-2 sm:flex-row sm:items-center"
            >
              <span class="w-12 shrink-0 text-xs font-medium text-gray-500">{{ (i + 1).toString().padStart(2, '0') }} 行</span>
              <input
                v-model="form.communicationLines[i]"
                type="text"
                :placeholder="`連絡文書 ${(i + 1).toString().padStart(2, '0')} 行目...`"
                class="flex-1 rounded-md border border-gray-300 px-2.5 py-1.5 text-sm"
              >
              <div v-if="commTemplates.length > 0" class="sm:w-64">
                <AutoComplete
                  :model-value="''"
                  :options="commTemplateOptions"
                  placeholder="テンプレから選択…"
                  empty-label="（選択しない）"
                  @update:model-value="(v) => applyTemplateToLine(i, v)"
                />
              </div>
            </div>
          </div>
        </section>

        <div v-if="errorMessage" class="rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
          {{ errorMessage }}
        </div>

        <div class="flex justify-end gap-2">
          <NuxtLink to="/orders" class="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm hover:bg-gray-50">キャンセル</NuxtLink>
          <button type="submit" :disabled="submitting" class="rounded-md bg-blue-600 px-6 py-2 text-sm font-semibold text-white hover:bg-blue-700 disabled:opacity-50">
            {{ submitting ? '保存中…' : `登録 (合計 ${totalSummary})` }}
          </button>
        </div>
      </form>

      <!-- 分納設定モーダル (§改修 2026-07)。日付ごとの数量を入力する。
           品番/色/サイズ/品名は固定表示 (lg 以上で sticky)、日付列は横スクロール。
           列の増減はヘッダのボタン (+ 列を追加 / − 列を削除) と各列の × で行う。
           MasterEditDialog と同じ Teleport + ヘッダ/フッタ固定・本文スクロールの構成。 -->
      <Teleport to="body">
        <div
          v-if="showDeliveryModal"
          class="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4 py-6"
          role="dialog"
          aria-modal="true"
          aria-labelledby="delivery-modal-title"
          @click.self="showDeliveryModal = false"
        >
          <div class="flex max-h-[calc(100vh-3rem)] w-full max-w-6xl flex-col rounded-lg bg-white shadow-xl">
            <header class="shrink-0 border-b border-gray-200 px-5 py-3">
              <div class="flex items-start justify-between gap-3">
                <div>
                  <h2 id="delivery-modal-title" class="text-base font-semibold">分納設定</h2>
                  <p class="mt-0.5 text-xs text-gray-500">
                    納期 (日付) ごとの発注数を入力します。品番・色・サイズ・品名は固定表示、日付列は横スクロールします。明細の発注数は各日付列の合計になります。
                  </p>
                </div>
                <button type="button" class="shrink-0 rounded-md border border-gray-300 bg-white px-2.5 py-1 text-sm hover:bg-gray-50" @click="showDeliveryModal = false">閉じる</button>
              </div>
              <div class="mt-2 flex flex-wrap items-center justify-between gap-2">
                <span class="text-xs text-gray-500">日付列 {{ deliveryColumns.length }} 列 (既定 {{ DELIVERY_DEFAULT_COLS }} 列)</span>
                <div class="flex items-center gap-2">
                  <button
                    type="button"
                    :disabled="deliveryColumns.length <= 1"
                    title="末尾の日付列を削除します (数量は先頭の列へ移します)"
                    class="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50 disabled:opacity-40"
                    @click="removeLastDeliveryColumn"
                  >− 列を削除</button>
                  <button
                    type="button"
                    class="rounded-md border border-blue-300 bg-blue-50 px-3 py-1 text-sm text-blue-700 hover:bg-blue-100"
                    @click="addDeliveryColumn()"
                  >+ 列を追加</button>
                </div>
              </div>
            </header>

            <div class="flex-1 overflow-auto px-5 py-3">
              <table class="w-full border-separate border-spacing-0 text-sm">
                <thead class="bg-gray-50 text-gray-600">
                  <tr>
                    <!-- 固定表示列 (品番/色/サイズ/品名)。lg 以上で sticky、背景を塗って日付列がすり抜けないようにする。 -->
                    <th
                      v-for="c in DELIVERY_FIXED_COLS"
                      :key="c.key"
                      class="border-b border-r border-gray-200 bg-gray-50 px-2 py-1 text-left font-semibold lg:z-[2]"
                      :class="c.cls"
                    >{{ c.label }}</th>
                    <!-- 日付列。ヘッダで納期を編集、× でその列を削除する。 -->
                    <th
                      v-for="col in deliveryColumns"
                      :key="col.id"
                      class="w-[128px] min-w-[128px] border-b border-r border-gray-200 px-1 py-1 text-center font-semibold"
                    >
                      <div class="flex items-center justify-center gap-1">
                        <input v-model="col.date" type="date" class="w-full rounded border border-gray-300 bg-white px-1 text-xs" :aria-label="`納期 ${deliveryColumns.indexOf(col) + 1} 列目`" />
                        <button
                          type="button"
                          class="shrink-0 text-base leading-none text-red-500 hover:text-red-700"
                          :title="deliveryColumns.length <= 1
                            ? 'この列を削除して分納を解除します (合計は発注数に引き継がれます)'
                            : 'この日付列を削除します (入力済みの数量は先頭の列へ移します)'"
                          @click="removeDeliveryColumn(col.id)"
                        >×</button>
                      </div>
                    </th>
                    <th class="w-[88px] min-w-[88px] border-b border-gray-200 px-2 py-1 text-right font-semibold">合計</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="(l, idx) in lines" :key="idx">
                    <td
                      v-for="c in DELIVERY_FIXED_COLS"
                      :key="c.key"
                      class="truncate border-b border-r border-gray-200 bg-white px-2 py-1 lg:z-[2]"
                      :class="c.cls"
                      :title="deliveryFixedText(l, c.key)"
                    >{{ deliveryFixedText(l, c.key) }}</td>
                    <!-- 日付列ごとの発注数セル (SKU × 納期のマトリクス)。空欄は 0 扱い。 -->
                    <td v-for="col in deliveryColumns" :key="col.id" class="border-b border-r border-gray-200 p-0 text-right">
                      <input
                        v-model.number="l.deliveryQtys[col.id]"
                        type="number"
                        min="0"
                        placeholder="0"
                        class="sheet-input h-8 text-right"
                        :aria-label="`${deliveryFixedText(l, 'sku')} の ${col.date || '納期未設定'} の発注数`"
                      />
                    </td>
                    <td class="border-b border-gray-200 px-2 py-1 text-right font-mono">{{ lineMatrixTotal(l).toLocaleString() }}</td>
                  </tr>
                </tbody>
                <tfoot class="bg-gray-50 text-gray-600">
                  <tr>
                    <td
                      v-for="(c, i) in DELIVERY_FIXED_COLS"
                      :key="c.key"
                      class="border-t border-r border-gray-200 bg-gray-50 px-2 py-1 font-semibold lg:z-[2]"
                      :class="c.cls"
                    >{{ i === 0 ? '列合計' : '' }}</td>
                    <td v-for="col in deliveryColumns" :key="col.id" class="border-t border-r border-gray-200 px-2 py-1 text-right font-mono">
                      {{ deliveryColumnTotal(col.id).toLocaleString() }}
                    </td>
                    <td class="border-t border-gray-200 px-2 py-1 text-right font-mono font-semibold">
                      {{ lines.reduce((s, l) => s + lineMatrixTotal(l), 0).toLocaleString() }}
                    </td>
                  </tr>
                </tfoot>
              </table>
            </div>

            <footer class="shrink-0 border-t border-gray-200 px-5 py-3">
              <p v-if="deliveryColumnsMissingDate.length > 0" class="mb-2 rounded border border-amber-300 bg-amber-50 px-3 py-2 text-xs text-amber-800">
                数量が入力されているのに納期が未設定の列が {{ deliveryColumnsMissingDate.length }} 列あります。納期なしのまま登録すると、その分納行は納期未定として保存されます。
              </p>
              <div class="flex flex-wrap items-center justify-between gap-2">
                <button
                  type="button"
                  class="rounded-md border border-red-300 bg-white px-3 py-1.5 text-sm text-red-600 hover:bg-red-50"
                  title="全ての日付列を削除し、明細の発注数を単一入力に戻します (合計は引き継がれます)"
                  @click="clearDeliveryColumns"
                >分納を解除</button>
                <button type="button" class="rounded-md bg-blue-600 px-5 py-1.5 text-sm font-semibold text-white hover:bg-blue-700" @click="showDeliveryModal = false">完了</button>
              </div>
            </footer>
          </div>
        </div>
      </Teleport>
    </template>
  </main>
</template>
