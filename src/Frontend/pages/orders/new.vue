<script setup lang="ts">
import type { MasterItem } from '~/composables/useMasters'
import type { CreateOrderPayload, CommunicationSuggestion } from '~/composables/useOrders'

const { user } = useAuth()
const canCreateOrder = computed(() => (user.value?.purchaseOrderCreatePermission ?? 0) >= 1)
const { list } = useMasters()
const { create, communicationSuggestions, priceSuggestion } = useOrders()
const { listFamiliesAll } = useProducts()
const { apiData } = useApi()

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

// フォーム
const today = todayJst() // 業務日付は JST 基準 (UTC 由来だと JST 00:00-08:59 に前日になる)
// 発注区分 (国内/海外) は入力項目の出し分けには使わない (§5 統一)。国内/海外で共通の入力項目とし、
// is_overseas は帳票の言語切替 (国内=日本語/海外=英語) と一覧の区分バッジのみに使う。
const form = ref({
  orderNo: '',            // 発注書番号 (§5)。従来は初回 Excel 出力時に採番だが、作成時に手入力も可能 (任意)。
  // ID 系は uuid 文字列 (第二段階契約)。未選択は '' で表し、送信前の必須チェックで弾く。
  supplierId: '',          // 発注先 (仕入先マスタ、Part6: 海外のみ表示・入力)
  customerRef: '',        // 得意先 / 受注先 (国内/海外共通)
  deliveryDestinationId: '', // 納品先
  departmentId: '',        // 発注事業部
  landingPlace: '',       // 荷揚地 (国内/海外共通)
  warehouseId: '',         // 納入倉庫1
  warehouse2Id: null as string | null, // 納入倉庫2 (国内/海外共通)
  warehouse3Id: null as string | null, // 納入倉庫3 (国内/海外共通)
  dueDate: today,         // 取引先納入日 (旧「納入日」、§5 名称変更)
  factoryShippingDate: '',        // 工場出荷日 (国内/海外共通)
  deliveryPlaceShippingDate: '',  // 検品場出荷日 (列 delivery_place_shipping_date、国内/海外共通)
  overseasDepartureDate: '',      // 海外出港日 (国内/海外共通)
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
const lines = ref<LineRow[]>([
  { productId: '', quantity: 1, unitPriceSnapshot: 0, currencyCodeSnapshot: 'JPY', packQuantity: null, remark: null, deliveryQtys: {} },
])

// 分納 (納期列マトリクス)。「分納入力」で納期列を追加し、SKU 明細行ごとに納期別の発注数を入力する。
// 列は全明細で共有 (2 枚目キャプチャの日付列)。列が 1 つ以上あるとき分納モード = 発注数は各列の合計。
interface DeliveryColumn { id: number; date: string }
const deliveryColumns = ref<DeliveryColumn[]>([])
const nextDeliveryColId = ref(1)
const deliveryMode = computed(() => deliveryColumns.value.length > 0)

const addDeliveryColumn = () => {
  const colId = nextDeliveryColId.value++
  const isFirst = deliveryColumns.value.length === 0
  // 既定の納期は取引先納入日。ユーザは列ヘッダで変更する。
  deliveryColumns.value.push({ id: colId, date: form.value.dueDate || '' })
  // 最初の納期列を追加したとき、各明細の現在の発注数をこの列に移す (合計を維持)。
  if (isFirst) {
    for (const l of lines.value) l.deliveryQtys[colId] = Number(l.quantity) || 0
  }
}
const removeDeliveryColumn = (colId: number) => {
  const willBeEmpty = deliveryColumns.value.length <= 1
  // 分納モードを抜けるときは各明細の単一発注数へ合計を戻す (入力を失わない)。
  if (willBeEmpty) {
    for (const l of lines.value) l.quantity = lineMatrixTotal(l)
  }
  deliveryColumns.value = deliveryColumns.value.filter((c) => c.id !== colId)
  for (const l of lines.value) delete l.deliveryQtys[colId]
}
// 明細の分納合計 (全納期列の数量合計)。
const lineMatrixTotal = (l: LineRow): number =>
  deliveryColumns.value.reduce((s, c) => s + (Number(l.deliveryQtys[c.id]) || 0), 0)

// 納期列が4つ以上になると左側の商品情報 (SKU〜備考) が横スクロールで隠れて操作不能になるため、
// その場合は左6列 (SKU/発注数/入数/仕入単価/通貨/備考) を固定 (position: sticky) して横スクロールさせる。
// 3列までは列固定せず従来どおり (テーブル幅が画面内に収まるため)。
const stickyMode = computed(() => deliveryColumns.value.length >= 4)
// 固定する左6列の幅 (px)。sticky の left は先行列の累積幅で計算する (auto 幅では left を確定できないため固定幅)。
const FROZEN_WIDTHS = [208, 96, 96, 112, 96, 152]
const frozenLeft = (i: number): number => FROZEN_WIDTHS.slice(0, i).reduce((s, w) => s + w, 0)
// 固定列 (th/td 共通) のインラインスタイル。stickyMode でないときは空 = 従来の auto 幅。
// i は左からの列インデックス (0=SKU 〜 5=備考)。z-index はスクロールする納期列より前面に出す。
const frozenColStyle = (i: number): Record<string, string> => {
  if (!stickyMode.value) return {}
  const w = `${FROZEN_WIDTHS[i]}px`
  const style: Record<string, string> = { position: 'sticky', left: `${frozenLeft(i)}px`, width: w, minWidth: w, maxWidth: w, zIndex: '2' }
  // 最右の固定列 (備考) に右影を付け、固定領域とスクロール領域の境界を視覚化する。
  if (i === FROZEN_WIDTHS.length - 1) style.boxShadow = '4px 0 6px -2px rgba(0,0,0,0.12)'
  return style
}

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

    // 初期値
    if (sup.length) form.value.supplierId = sup[0].id
    if (dest.length) form.value.deliveryDestinationId = dest[0].id
    if (dept.length) form.value.departmentId = dept[0].id
    if (wh.length) form.value.warehouseId = wh[0].id
    if (usrRes.length) {
      form.value.ordererUserId = usrRes[0].id
      form.value.managerUserId = usrRes[0].id
    }
    if (skus.value.length) lines.value[0].productId = skus.value[0].id
  } catch (e) {
    errorMessage.value = 'マスタ情報の取得に失敗しました'
  } finally {
    loading.value = false
  }
})

const addLine = () => {
  lines.value.push({
    productId: skus.value[0]?.id ?? '',
    quantity: 1,
    unitPriceSnapshot: 0,
    currencyCodeSnapshot: 'JPY',
    packQuantity: null,
    remark: null,
    deliveryQtys: {},
  })
  // 追加直後の明細にも現単価を補完する (reviewer M-1)。既定選択された SKU に対し size-aware に
  // サジェスト (force=true)。supplier 未選択や現単価なしなら applyPriceSuggestion 内で no-op。
  applyPriceSuggestion(lines.value.length - 1, true)
}

const removeLine = (idx: number) => {
  if (lines.value.length <= 1) return
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
  const idx = Number(optionValue)
  const tpl = commTemplates.value[idx]
  if (tpl && slot >= 0 && slot < form.value.communicationLines.length) {
    form.value.communicationLines[slot] = tpl.body
  }
}

// 明細の数量妥当性: 分納なしは単一 quantity > 0、分納あり (納期列) は各明細の合計 > 0。
const lineQuantityValid = (l: LineRow): boolean => lineQuantity(l) > 0

const canSubmit = computed(() =>
  // 発注先は海外のみ必須 (Part6: 国内は非表示・任意)。
  (!form.value.isOverseas || form.value.supplierId !== '') &&
  form.value.deliveryDestinationId !== '' &&
  form.value.departmentId !== '' &&
  form.value.warehouseId !== '' &&
  form.value.ordererUserId !== '' &&
  form.value.managerUserId !== '' &&
  lines.value.every((l) => l.productId !== '' && lineQuantityValid(l) && l.unitPriceSnapshot >= 0) &&
  !submitting.value)

const onSubmit = async () => {
  errorMessage.value = ''
  if (!canSubmit.value) {
    errorMessage.value = form.value.isOverseas
      ? '必須項目を入力してください (発注先 / 納品先 / 担当者 / 明細)'
      : '必須項目を入力してください (納品先 / 担当者 / 明細)'
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
  <main class="mx-auto max-w-screen-2xl px-4 py-4">
    <div v-if="!canCreateOrder" class="rounded border border-red-300 bg-red-50 p-4 text-red-700">
      発注書作成権限がありません。
      <div class="mt-2">
        <NuxtLink to="/orders" class="text-blue-600 underline">発注書一覧に戻る</NuxtLink>
      </div>
    </div>

    <template v-else>
      <header class="mb-4">
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

      <form v-else class="space-y-3" @submit.prevent="onSubmit">
        <!-- ① 発注区分 (海外/国内)。Part6: 並びは「海外」→「国内」、既定は「海外」。海外のみ表示する
             項目 (発注先/荷揚地/工場出荷日/納品所出荷日/海外出港日) を区分で出し分ける。区分は帳票の言語
             切替 (国内=日本語/海外=英語) と一覧の区分バッジにも使う。 -->
        <section class="rounded-lg border border-gray-200 bg-white p-3 shadow-sm">
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
        <section class="rounded-lg border border-gray-200 bg-white p-3 shadow-sm">
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
              <MasterSelect v-model="form.supplierId" :items="suppliers" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="font-medium">得意先</span>
              <input v-model="form.customerRef" type="text" maxlength="128" placeholder="得意先 / 受注先" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="font-medium">納品先 <span class="text-red-500">*</span></span>
              <MasterSelect v-model="form.deliveryDestinationId" :items="destinations" />
            </label>
          </div>

          <!-- 事業部 / 荷揚地 / 納入倉庫1〜3 -->
          <div class="mt-3 grid grid-cols-1 gap-x-3 gap-y-2 text-sm sm:grid-cols-2 lg:grid-cols-3">
            <label class="flex flex-col gap-1">
              <span class="font-medium">発注事業部 <span class="text-red-500">*</span></span>
              <MasterSelect v-model="form.departmentId" :items="departments" />
            </label>
            <!-- 荷揚地。Part6: 海外のみ表示。 -->
            <label v-if="form.isOverseas" class="flex flex-col gap-1">
              <span class="font-medium">荷揚地</span>
              <input v-model="form.landingPlace" type="text" maxlength="128" placeholder="Port of entry" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="font-medium">納入倉庫1 <span class="text-red-500">*</span></span>
              <MasterSelect v-model="form.warehouseId" :items="warehouses" />
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

          <!-- 日付: 取引先納入日 / 工場出荷日 / 検品場出荷日 / 海外出港日 -->
          <div class="mt-3 grid grid-cols-1 gap-x-3 gap-y-2 text-sm sm:grid-cols-2 lg:grid-cols-4">
            <label class="flex flex-col gap-1">
              <span class="font-medium">取引先納入日 <span class="text-red-500">*</span></span>
              <input v-model="form.dueDate" type="date" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
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
              <MasterSelect v-model="form.ordererUserId" :items="userOptions" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="font-medium">発注管理者 <span class="text-red-500">*</span></span>
              <MasterSelect v-model="form.managerUserId" :items="userOptions" />
            </label>
          </div>
        </section>

        <!-- ④ 明細。分納は「分納入力」で納期 (日付) 列を追加し、SKU 明細行ごとに納期別の発注数を
             入力する (2 枚目キャプチャの日付マトリクス)。納期列が無ければ単一の発注数を使う。
             合計は通貨ごとに集計して表示する (選択通貨と表示を一致させる)。 -->
        <!-- overflow-x-auto は「テーブルのみ」を包む内側 div に付ける。セクション自体には付けない
             ことで、見出し・ボタン (分納入力/明細追加)・説明文は横スクロールしても固定される
             (以前はセクション全体がスクロール領域となりボタン等も一緒に動いてしまっていた)。 -->
        <section class="rounded-lg border border-gray-200 bg-white p-3 shadow-sm">
          <div class="mb-3 flex items-center justify-between border-b border-gray-100 pb-2">
            <h2 class="font-semibold">④ 明細 ({{ lines.length }} 件、合計 {{ totalSummary }})</h2>
            <div class="flex items-center gap-2">
              <!-- 分納入力: 納期 (日付) 列を 1 つ追加する。列を 1 つ以上追加すると分納モードになり、
                   各 SKU の発注数は納期列の合計になる (「+ 明細追加」の左に配置)。 -->
              <button type="button" class="rounded-md border border-blue-300 bg-blue-50 px-3 py-1 text-sm text-blue-700 hover:bg-blue-100" @click="addDeliveryColumn">+ 分納入力</button>
              <button type="button" class="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50" @click="addLine">+ 明細追加</button>
            </div>
          </div>
          <div class="overflow-x-auto">
          <table class="w-full text-sm">
            <thead class="border-b border-gray-200 bg-gray-50">
              <!-- stickyMode (納期列4つ以上) では左6列を固定表示 (frozenColStyle + bg で背景を塗り、
                   スクロールする納期列がすり抜けて見えないようにする)。 -->
              <tr>
                <th class="px-2 py-1.5 text-left" :class="stickyMode && 'bg-gray-50'" :style="frozenColStyle(0)">SKU</th>
                <th class="px-2 py-1.5 text-right" :class="stickyMode && 'bg-gray-50'" :style="frozenColStyle(1)">発注数</th>
                <th class="px-2 py-1.5 text-right" :class="stickyMode && 'bg-gray-50'" :style="frozenColStyle(2)">入数</th>
                <th class="px-2 py-1.5 text-right" :class="stickyMode && 'bg-gray-50'" :style="frozenColStyle(3)">仕入単価</th>
                <th class="px-2 py-1.5 text-left" :class="stickyMode && 'bg-gray-50'" :style="frozenColStyle(4)">通貨</th>
                <th class="px-2 py-1.5 text-left" :class="stickyMode && 'bg-gray-50'" :style="frozenColStyle(5)">備考</th>
                <!-- 分納 納期列 (全明細共通)。各列ヘッダで納期の日付を編集、× でその列を削除する。 -->
                <th v-for="col in deliveryColumns" :key="col.id" class="px-2 py-1.5 text-center">
                  <div class="flex items-center justify-center gap-1">
                    <input v-model="col.date" type="date" class="rounded-md border border-gray-300 px-1.5 py-1 text-xs" />
                    <button type="button" class="text-base leading-none text-red-500 hover:text-red-700" title="この納期列を削除" @click="removeDeliveryColumn(col.id)">×</button>
                  </div>
                </th>
                <th class="px-2 py-1.5 text-right">小計</th>
                <th class="px-2 py-1.5 text-right"></th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(l, idx) in lines" :key="idx" class="border-b border-gray-100 last:border-0">
                <td class="px-2 py-1" :class="stickyMode && 'bg-white'" :style="frozenColStyle(0)">
                  <!-- サイズ別仕入単価 (PR2): SKU 選択時に単価をサジェスト補完 (onLineProductChange)。 -->
                  <MasterSelect :model-value="l.productId" :items="skuOptions" placeholder="SKU・品名で検索…" @update:model-value="(v) => onLineProductChange(idx, v)" />
                </td>
                <td class="px-2 py-1 text-right" :class="stickyMode && 'bg-white'" :style="frozenColStyle(1)">
                  <!-- 分納モード (納期列あり) は発注数 = 各納期列の合計を読取表示。分納なしは単一入力。 -->
                  <input v-if="!deliveryMode" v-model.number="l.quantity" type="number" min="1" class="h-8 w-full rounded-md border border-gray-300 px-2 text-right" />
                  <span v-else class="inline-block w-full px-2 text-right font-mono leading-8 text-gray-700" title="納期列の合計">{{ lineQuantity(l).toLocaleString() }}</span>
                </td>
                <td class="px-2 py-1 text-right" :class="stickyMode && 'bg-white'" :style="frozenColStyle(2)">
                  <input v-model.number="l.packQuantity" type="number" min="0" placeholder="—" class="h-8 w-full rounded-md border border-gray-300 px-2 text-right" />
                </td>
                <td class="px-2 py-1 text-right" :class="stickyMode && 'bg-white'" :style="frozenColStyle(3)">
                  <input v-model.number="l.unitPriceSnapshot" type="number" min="0" step="0.01" class="h-8 w-full rounded-md border border-gray-300 px-2 text-right" />
                </td>
                <td class="px-2 py-1" :class="stickyMode && 'bg-white'" :style="frozenColStyle(4)">
                  <AutoComplete :model-value="l.currencyCodeSnapshot" :options="[{ value: 'JPY', label: 'JPY' }, { value: 'USD', label: 'USD' }, { value: 'CNY', label: 'CNY' }]" :allow-empty="false" @update:model-value="(v) => l.currencyCodeSnapshot = v" />
                </td>
                <td class="px-2 py-1" :class="stickyMode && 'bg-white'" :style="frozenColStyle(5)">
                  <input v-model="l.remark" type="text" maxlength="255" placeholder="—" class="h-8 w-full rounded-md border border-gray-300 px-2" />
                </td>
                <!-- 納期列ごとの発注数セル (SKU × 納期のマトリクス)。空欄は 0 扱い。 -->
                <td v-for="col in deliveryColumns" :key="col.id" class="px-2 py-1 text-right">
                  <input v-model.number="l.deliveryQtys[col.id]" type="number" min="0" placeholder="0" class="h-8 w-16 rounded-md border border-gray-300 px-2 text-right" />
                </td>
                <!-- 小計は行の通貨を前置して表示 (選択通貨とサマリ表示を一致させる)。 -->
                <td class="whitespace-nowrap px-2 py-1 text-right font-mono">{{ l.currencyCodeSnapshot }} {{ lineSubtotal(l).toLocaleString() }}</td>
                <td class="px-2 py-1 text-right">
                  <button type="button" :disabled="lines.length <= 1" class="text-xs text-red-600 hover:underline disabled:opacity-30" @click="removeLine(idx)">削除</button>
                </td>
              </tr>
            </tbody>
          </table>
          </div>
          <p v-if="deliveryMode" class="mt-2 text-xs text-gray-500">
            分納モード: 各 SKU の発注数は納期列の合計です。納期列を全て × で削除すると単一発注数の入力に戻ります (入力済みの合計は発注数に引き継がれます)。
          </p>
        </section>

        <!-- 連絡文書 6 行 (構造化、PR6。旧 spec 発注明細 No.27-32「連絡文書01行〜06行」) -->
        <section class="rounded-lg border border-gray-200 bg-white p-3 shadow-sm">
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
          <button type="submit" :disabled="!canSubmit" class="rounded-md bg-blue-600 px-6 py-2 text-sm font-semibold text-white hover:bg-blue-700 disabled:opacity-50">
            {{ submitting ? '保存中…' : `登録 (合計 ${totalSummary})` }}
          </button>
        </div>
      </form>
    </template>
  </main>
</template>
