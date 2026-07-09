<script setup lang="ts">
import type { OrderDetail, EditReason, CommunicationSuggestion, OrderExportFormat } from '~/composables/useOrders'
import { editReasonLabel, deriveOrderState, orderStateLabel, orderStateBadgeClass } from '~/composables/useOrders'

const route = useRoute()
const id = computed(() => Number(route.params.id))
const { user } = useAuth()
const canEditOrder = computed(() => (user.value?.purchaseOrderCreatePermission ?? 0) >= 1)

const { get, update, cancel, markOrdered, unmarkOrdered, softDelete, exportOrder, communicationSuggestions } = useOrders()

const detail = ref<OrderDetail | null>(null)
const loading = ref(true)
const errorMessage = ref('')
const successMessage = ref('')
const downloading = ref(false)

// 編集モード
const editing = ref(false)
// 分納×倉庫の多次元明細 (PR5b)。1 明細を「(倉庫 × 納期) の分納行」の集合で多次元化する。
interface EditDeliveryRow {
  warehouseId: number | null
  deliveryDate: string
  quantity: number
  packQuantity: number | null
}
interface EditLineRow {
  id: number | null
  productId: number
  sku: string
  productName: string
  quantity: number
  unitPriceSnapshot: number
  currencyCodeSnapshot: string
  packQuantity: number | null
  estimateUnitPrice: number | null
  provisionalNumberSnapshot: string | null
  remark: string | null
  // 分納×倉庫の多次元明細 (PR5b、任意)。空 = 分納なし (単一明細、従来挙動)。
  deliveries: EditDeliveryRow[]
  showDeliveries: boolean
}
const editLines = ref<EditLineRow[]>([])
const editReason = ref<EditReason>('quantity')
const editNote = ref('')

// 旧 発注書 国内/海外 項目 (Phase B) の編集状態。reload 時に detail から初期化する。
const editHeader = ref({
  isOverseas: false,
  landingPlace: '' as string,
  customerRef: '' as string,
  factoryShippingDate: '' as string,
  deliveryPlaceShippingDate: '' as string,
  overseasDepartureDate: '' as string,
  warehouse2Id: null as number | null,
  warehouse3Id: null as number | null,
})
// 納入倉庫2/3 編集用にマスタを読み込む
const { list: listMasters } = useMasters()
const warehouses = ref<{ id: number; code?: string | null; name?: string | null }[]>([])

// 連絡文書 6 行 (構造化、PR6)。編集用の固定長 6 スロット + テンプレ候補。reload() で detail から初期化する。
const editCommLines = ref<string[]>(['', '', '', '', '', ''])
const commTemplates = ref<CommunicationSuggestion[]>([])
const commTemplateOptions = computed(() =>
  commTemplates.value.map((t, i) => ({ value: `${i}`, label: t.sourceLabel, searchText: t.body })),
)
// テンプレを指定スロットに適用する (AutoComplete は index 文字列を value にするため body を引き直す)。
const applyTemplateToLine = (slot: number, optionValue: string) => {
  const idx = Number(optionValue)
  const tpl = commTemplates.value[idx]
  if (tpl && slot >= 0 && slot < editCommLines.value.length) {
    editCommLines.value[slot] = tpl.body
  }
}
// 連絡文書 6 行 (PR6) の編集ロード。新フローは 6 列を各スロットへ。6 列が全て空でも communicationText が
// ある旧発注は、communicationText を改行分割して 6 スロットへブリッジする (最大6行、超過は6行目に集約)。
const buildEditCommLines = (d: OrderDetail): string[] => {
  const cols = [
    d.communicationLine1, d.communicationLine2, d.communicationLine3,
    d.communicationLine4, d.communicationLine5, d.communicationLine6,
  ]
  const hasStructured = cols.some((c) => c != null && c.trim() !== '')
  if (hasStructured) {
    // 新データ: 6 列をそのままスロットへ (null → 空文字に正規化)。
    return cols.map((c) => c ?? '')
  }
  // 旧データ: communicationText を改行分割。先頭5行はそのまま、6行目以降は残り全部を結合して集約。
  const text = d.communicationText ?? ''
  if (text.trim() === '') return ['', '', '', '', '', '']
  const parts = text.split('\n')
  const slots = parts.slice(0, 5)
  if (parts.length > 5) slots.push(parts.slice(5).join('\n'))
  else slots.push(parts[5] ?? '')
  while (slots.length < 6) slots.push('')
  return slots.slice(0, 6)
}
// 連絡文書 6 行 (PR6) の読取表示用。新データは 6 列、旧データは communicationText を表示する。
// 表示は read-only セクション (編集モード外) で使う。
const displayCommLines = computed<string[]>(() => {
  if (!detail.value) return []
  const cols = [
    detail.value.communicationLine1, detail.value.communicationLine2, detail.value.communicationLine3,
    detail.value.communicationLine4, detail.value.communicationLine5, detail.value.communicationLine6,
  ].filter((c): c is string => c != null && c.trim() !== '')
  return cols
})

// 中止モーダル
const showCancelForm = ref(false)
const cancelReason = ref('')

const reload = async () => {
  loading.value = true
  errorMessage.value = ''
  try {
    detail.value = await get(id.value)
    if (detail.value) {
      editLines.value = detail.value.lines.map((l) => ({
        id: l.id, productId: l.productId, sku: l.sku, productName: l.productName,
        quantity: l.quantity, unitPriceSnapshot: l.unitPriceSnapshot, currencyCodeSnapshot: l.currencyCodeSnapshot,
        packQuantity: l.packQuantity, estimateUnitPrice: l.estimateUnitPrice, provisionalNumberSnapshot: l.provisionalNumberSnapshot,
        remark: l.remark,
        // 分納×倉庫の多次元明細 (PR5b)。seq 昇順は API 側で保証済。deliveryDate は null → 空文字に正規化。
        deliveries: (l.deliveries ?? []).map((d) => ({
          warehouseId: d.warehouseId,
          deliveryDate: d.deliveryDate ?? '',
          quantity: d.quantity,
          packQuantity: d.packQuantity,
        })),
        // 既存分納がある明細は最初から開いておく (編集しやすさ)。
        showDeliveries: (l.deliveries?.length ?? 0) > 0,
      }))
      // 旧 発注書 国内/海外 項目 (Phase B) の編集状態を detail から初期化 (null → 空文字に正規化)
      editHeader.value = {
        isOverseas: detail.value.isOverseas,
        landingPlace: detail.value.landingPlace ?? '',
        customerRef: detail.value.customerRef ?? '',
        factoryShippingDate: detail.value.factoryShippingDate ?? '',
        deliveryPlaceShippingDate: detail.value.deliveryPlaceShippingDate ?? '',
        overseasDepartureDate: detail.value.overseasDepartureDate ?? '',
        warehouse2Id: detail.value.warehouse2Id,
        warehouse3Id: detail.value.warehouse3Id,
      }
      // 連絡文書 6 行 (PR6) の編集スロットを初期化 (6 列優先、旧データは communicationText を改行ブリッジ)。
      editCommLines.value = buildEditCommLines(detail.value)
    }
  } catch (e) {
    const err = e as { statusCode?: number }
    errorMessage.value = err.statusCode === 404 ? '発注書が見つかりません' : '取得に失敗しました'
  } finally {
    loading.value = false
  }
}

onMounted(async () => {
  // 納入倉庫2/3 編集用マスタ + 連絡文書テンプレ候補を読み込む
  // (失敗しても本体表示は継続、原則 4 非ブロッキング)。
  try {
    warehouses.value = await listMasters('warehouses')
  } catch {
    warehouses.value = []
  }
  try {
    commTemplates.value = await communicationSuggestions()
  } catch {
    commTemplates.value = []
  }
  await reload()
})

const totalAmount = computed(() => detail.value?.lines.reduce((s, l) => s + l.subtotal, 0) ?? 0)

const onStartEdit = () => {
  editing.value = true
  editReason.value = 'quantity'
  editNote.value = ''
  successMessage.value = ''
  errorMessage.value = ''
}

const onCancelEdit = () => {
  editing.value = false
  reload()
}

// 分納×倉庫の多次元明細 (PR5b)。行追加/削除 + 数量合計算出 (new.vue と同じパターン)。
const addDelivery = (lineIdx: number) => {
  const l = editLines.value[lineIdx]
  l.showDeliveries = true
  l.deliveries.push({ warehouseId: detail.value?.warehouseId ?? null, deliveryDate: '', quantity: 1, packQuantity: null })
}
const removeDelivery = (lineIdx: number, dIdx: number) => {
  editLines.value[lineIdx].deliveries.splice(dIdx, 1)
}
// 分納が 1 件以上ある明細の数量は分納数量の合計 (SUM)。単一数量入力は分納なし時のみ。
const editLineQuantity = (l: EditLineRow): number =>
  l.deliveries.length > 0 ? l.deliveries.reduce((s, d) => s + (Number(d.quantity) || 0), 0) : Number(l.quantity) || 0
const editLineSubtotal = (l: EditLineRow): number => editLineQuantity(l) * l.unitPriceSnapshot
// 明細の数量妥当性: 分納なしは単一 quantity > 0、分納ありは全分納 quantity > 0 かつ合計 > 0。
const editLineQuantityValid = (l: EditLineRow): boolean =>
  l.deliveries.length > 0
    ? l.deliveries.every((d) => (Number(d.quantity) || 0) > 0) && editLineQuantity(l) > 0
    : (Number(l.quantity) || 0) > 0
const canSaveEdit = computed(() =>
  editLines.value.length > 0 && editLines.value.every((l) => l.productId > 0 && editLineQuantityValid(l) && l.unitPriceSnapshot >= 0))

const onSaveEdit = async () => {
  if (!detail.value) return
  errorMessage.value = ''
  // 分納あり明細は各分納数量 > 0 が必須。クライアント側でも弾く (サーバは ORDER-LINE-DLV-001 で 422)。
  if (!canSaveEdit.value) {
    errorMessage.value = '明細の数量を確認してください (分納行は各数量が 1 以上必要です)'
    return
  }
  try {
    await update(id.value, {
      editReason: editReason.value,
      editNote: editNote.value.trim() || null,
      supplierId: detail.value.supplierId,
      deliveryDestinationId: detail.value.deliveryDestinationId,
      departmentId: detail.value.departmentId,
      warehouseId: detail.value.warehouseId,
      dueDate: detail.value.dueDate,
      ordererUserId: detail.value.ordererUserId,
      managerUserId: detail.value.managerUserId,
      subOrderer1UserId: detail.value.subOrderer1UserId,
      subOrderer2UserId: detail.value.subOrderer2UserId,
      subOrderer3UserId: detail.value.subOrderer3UserId,
      subOrderer4UserId: detail.value.subOrderer4UserId,
      subOrderer5UserId: detail.value.subOrderer5UserId,
      subOrderer6UserId: detail.value.subOrderer6UserId,
      // 連絡文書 6 行 (構造化、PR6)。新フローは本 6 列で上書き保存する (SoT、空欄は null)。
      // communicationText は新フローで書かないが、旧データを温存するため従来値をそのまま送り返す
      // (サーバは受理値を保存。6 列が埋まれば Excel/編集ロードは 6 列を優先する)。
      communicationText: detail.value.communicationText,
      communicationLine1: editCommLines.value[0].trim() || null,
      communicationLine2: editCommLines.value[1].trim() || null,
      communicationLine3: editCommLines.value[2].trim() || null,
      communicationLine4: editCommLines.value[3].trim() || null,
      communicationLine5: editCommLines.value[4].trim() || null,
      communicationLine6: editCommLines.value[5].trim() || null,
      lines: editLines.value.map((l) => ({
        productId: l.productId,
        // 分納あり明細は quantity = 分納合計 (editLineQuantity)。サーバも SUM に再計算する。
        quantity: editLineQuantity(l),
        unitPriceSnapshot: Number(l.unitPriceSnapshot),
        currencyCodeSnapshot: l.currencyCodeSnapshot,
        packQuantity: l.packQuantity != null ? Number(l.packQuantity) : null,
        estimateUnitPrice: l.estimateUnitPrice != null ? Number(l.estimateUnitPrice) : null,
        // 発注明細 備考 (spec 明細 No.26)。空欄は null で送る。
        remark: l.remark?.trim() || null,
        // 分納×倉庫の多次元明細 (PR5b)。空 = null (分納なし、従来挙動)。1 件以上あれば全置換で送る。
        deliveries: l.deliveries.length > 0
          ? l.deliveries.map((d) => ({
              warehouseId: d.warehouseId,
              deliveryDate: d.deliveryDate || null,
              quantity: Number(d.quantity) || 0,
              packQuantity: d.packQuantity != null ? Number(d.packQuantity) : null,
            }))
          : null,
      })),
      // 発注書 国内/海外 項目 (§5 統一)。区分に関わらず常に送る (国内でも値を保持するため区分での null 化はしない)。
      // is_overseas は帳票の言語切替・区分バッジのみに使う。
      isOverseas: editHeader.value.isOverseas,
      landingPlace: editHeader.value.landingPlace.trim() || null,
      customerRef: editHeader.value.customerRef.trim() || null,
      factoryShippingDate: editHeader.value.factoryShippingDate || null,
      deliveryPlaceShippingDate: editHeader.value.deliveryPlaceShippingDate || null,
      overseasDepartureDate: editHeader.value.overseasDepartureDate || null,
      warehouse2Id: editHeader.value.warehouse2Id,
      warehouse3Id: editHeader.value.warehouse3Id,
    })
    successMessage.value = '更新しました'
    editing.value = false
    await reload()
  } catch (e) {
    const err = e as { statusCode?: number }
    errorMessage.value = err.statusCode === 409
      ? '中止済みの発注書は編集できません'
      : getApiErrorMessage(e, '更新に失敗しました')
  }
}

const onCancelOrder = async () => {
  if (!cancelReason.value.trim()) {
    errorMessage.value = '中止理由を入力してください'
    return
  }
  try {
    await cancel(id.value, cancelReason.value.trim())
    showCancelForm.value = false
    cancelReason.value = ''
    successMessage.value = '発注書を中止しました'
    await reload()
  } catch {
    errorMessage.value = '中止操作に失敗しました'
  }
}

// 発注状態 4 値 (§3b) を共通ヘルパーで導出 (一覧 index.vue と同一ロジック)。
const orderState = computed(() =>
  detail.value
    ? deriveOrderState({
        status: detail.value.status,
        orderedAt: detail.value.orderedAt,
        isDeleted: detail.value.isDeleted,
      })
    : null,
)

// 削除確認モーダル
const showDeleteForm = ref(false)

// 発注済にする (§3b)。未発注 (notOrdered) のときのみボタン表示。ダウンロードとは独立したユーザー操作。
const onMarkOrdered = async () => {
  errorMessage.value = ''
  try {
    await markOrdered(id.value)
    successMessage.value = '発注済にしました'
    await reload()
  } catch (e) {
    const err = e as { statusCode?: number }
    errorMessage.value = err.statusCode === 409
      ? getApiErrorMessage(e, '現在の状態では発注済にできません')
      : '発注済操作に失敗しました'
  }
}

// 未発注に戻す (§3b)。発注済 (ordered) のときのみボタン表示。
const onUnmarkOrdered = async () => {
  errorMessage.value = ''
  try {
    await unmarkOrdered(id.value)
    successMessage.value = '未発注に戻しました'
    await reload()
  } catch (e) {
    const err = e as { statusCode?: number }
    errorMessage.value = err.statusCode === 409
      ? getApiErrorMessage(e, '現在の状態では未発注に戻せません')
      : '未発注に戻す操作に失敗しました'
  }
}

// 発注削除 (#3a)。論理削除。確認モーダル経由。
const onSoftDelete = async () => {
  errorMessage.value = ''
  try {
    await softDelete(id.value)
    showDeleteForm.value = false
    successMessage.value = '発注書を削除しました'
    await reload()
  } catch {
    errorMessage.value = '削除操作に失敗しました'
  }
}

// ─── 帳票出力フォーム (旧システム「発注書出力」画面相当) ───────────────
// 「発注日」「出荷指示番号」「発注番号」を手入力し、出力帳票 (発注書 / 管理表 / 発注書+管理表) を
// 選んで出力する。入力 3 項目はサーバ側で発注に保存され、再出力時に初期表示される。
const showExportForm = ref(false)
const exportForm = ref<{ orderDate: string; shippingInstructionNo: string; orderNo: string; format: OrderExportFormat }>({
  orderDate: '', shippingInstructionNo: '', orderNo: '', format: 'order',
})
const exportFormats: { key: OrderExportFormat; label: string }[] = [
  { key: 'order', label: '発注書のみ' },
  { key: 'management', label: '管理表のみ' },
  { key: 'both', label: '発注書+管理表' },
]

// 本日のローカル日付を YYYY-MM-DD で返す (toISOString は UTC のため JST 深夜に前日へずれる)。
const todayLocal = (): string => {
  const d = new Date()
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

const openExportForm = () => {
  if (!detail.value) return
  // 既存の保存値をフォームへ初期表示する (発注日は未保存なら本日)。
  exportForm.value = {
    orderDate: detail.value.orderDate ?? todayLocal(),
    shippingInstructionNo: detail.value.shippingInstructionNo ?? '',
    orderNo: detail.value.orderNo ?? '',
    format: 'order',
  }
  errorMessage.value = ''
  successMessage.value = ''
  showExportForm.value = true
}

const onSubmitExport = async () => {
  if (!detail.value || downloading.value) return
  downloading.value = true
  errorMessage.value = ''
  try {
    await exportOrder(id.value, {
      orderDate: exportForm.value.orderDate || null,
      shippingInstructionNo: exportForm.value.shippingInstructionNo.trim() || null,
      orderNo: exportForm.value.orderNo.trim() || null,
      format: exportForm.value.format,
    })
    successMessage.value = '帳票を出力しました'
    showExportForm.value = false
    await reload()
  } catch (e) {
    const err = e as { statusCode?: number }
    errorMessage.value = getApiErrorMessage(
      e,
      err.statusCode === 409 ? '発注番号が重複しています' : '帳票出力に失敗しました',
    )
  } finally {
    downloading.value = false
  }
}

const exportBadge = computed(() => {
  if (!detail.value) return null
  if (!detail.value.firstExportedAt) return { label: '未出力', cls: 'bg-gray-100 text-gray-600' }
  const dt = formatJstDateTime(detail.value.firstExportedAt)
  return { label: `初回出力済 (${dt})`, cls: 'bg-blue-100 text-blue-700' }
})

const editReasonOptions: EditReason[] = ['quantity', 'deadline', 'supplier', 'typo', 'other']
</script>

<template>
  <main class="mx-auto max-w-screen-2xl px-4 py-4">
    <div v-if="loading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500">読み込み中…</div>

    <div v-else-if="!detail" class="rounded border border-red-300 bg-red-50 p-4 text-red-700">
      {{ errorMessage || '発注書が見つかりません' }}
      <div class="mt-2"><NuxtLink to="/orders" class="text-blue-600 underline">発注書一覧に戻る</NuxtLink></div>
    </div>

    <template v-else>
      <header class="mb-4 flex items-start justify-between gap-4">
        <div>
          <div class="text-xs text-gray-500">
            <NuxtLink to="/orders" class="hover:underline">発注書</NuxtLink>
            <span class="mx-1">/</span>
            <span class="font-mono">{{ detail.mgmtNo }}</span>
          </div>
          <h1 class="text-2xl font-bold">
            発注書 {{ detail.mgmtNo }}
            <span v-if="detail.orderNo" class="ml-2 text-base font-mono text-gray-500">/ 発注番号 {{ detail.orderNo }}</span>
          </h1>
          <div class="mt-1 flex items-center gap-2 text-sm">
            <!-- 発注状態 4 値バッジ (§3b)。未発注/発注済/発注中止/発注削除 -->
            <span v-if="orderState" :class="orderStateBadgeClass(orderState)"
                  class="inline-block rounded-full px-2 py-0.5 text-xs font-medium">
              {{ orderStateLabel(orderState) }}
            </span>
            <span v-if="exportBadge" :class="exportBadge.cls" class="inline-block rounded-full px-2 py-0.5 text-xs">
              {{ exportBadge.label }}
            </span>
          </div>
        </div>
        <div class="flex gap-2">
          <button
            v-if="canEditOrder"
            type="button"
            :disabled="downloading || editing"
            class="rounded-md bg-green-600 px-3 py-1.5 text-sm text-white hover:bg-green-700 disabled:opacity-50"
            @click="openExportForm"
          >
            {{ downloading ? '出力中…' : '📥 帳票出力' }}
          </button>
          <!-- 編集/中止 は終端状態 (発注中止/発注削除) では不可。未発注/発注済 のみ (§3b)。
               バックエンド UpdateAsync/CancelAsync も同じガードを持つ (UI は操作可否の早期表示)。 -->
          <button
            v-if="canEditOrder && (orderState === 'notOrdered' || orderState === 'ordered') && !editing"
            type="button"
            class="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-sm hover:bg-gray-50"
            @click="onStartEdit"
          >編集</button>
          <!-- 発注済にする (§3b)。未発注 (notOrdered) のときのみ表示。ダウンロードとは独立したユーザー操作。 -->
          <button
            v-if="canEditOrder && orderState === 'notOrdered' && !editing"
            type="button"
            class="rounded-md bg-green-600 px-3 py-1.5 text-sm text-white hover:bg-green-700"
            @click="onMarkOrdered"
          >発注済にする</button>
          <!-- 未発注に戻す (§3b)。発注済 (ordered) のときのみ表示。 -->
          <button
            v-if="canEditOrder && orderState === 'ordered' && !editing"
            type="button"
            class="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-sm hover:bg-gray-50"
            @click="onUnmarkOrdered"
          >未発注に戻す</button>
          <button
            v-if="canEditOrder && (orderState === 'notOrdered' || orderState === 'ordered') && !editing"
            type="button"
            class="rounded-md border border-red-300 bg-white px-3 py-1.5 text-sm text-red-600 hover:bg-red-50"
            @click="showCancelForm = true"
          >中止</button>
          <!-- 発注削除 (§3b)。未削除のときのみ表示。論理削除なので確認モーダル経由。 -->
          <button
            v-if="canEditOrder && !detail.isDeleted && !editing"
            type="button"
            class="rounded-md border border-red-300 bg-white px-3 py-1.5 text-sm text-red-600 hover:bg-red-50"
            @click="showDeleteForm = true"
          >削除</button>
        </div>
      </header>

      <div v-if="errorMessage" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">{{ errorMessage }}</div>
      <div v-if="successMessage" class="mb-3 rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-700">{{ successMessage }}</div>

      <!-- 中止フォーム -->
      <div v-if="showCancelForm" class="mb-4 rounded-lg border border-orange-300 bg-orange-50 p-4">
        <div class="mb-2 font-semibold text-orange-800">発注書を中止</div>
        <input v-model="cancelReason" type="text" placeholder="中止理由を入力 (必須)" class="mb-2 w-full rounded-md border border-gray-300 px-2.5 py-1.5 text-sm" />
        <div class="flex justify-end gap-2">
          <button type="button" class="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50" @click="showCancelForm = false">キャンセル</button>
          <button type="button" class="rounded-md bg-orange-600 px-3 py-1 text-sm text-white hover:bg-orange-700" @click="onCancelOrder">中止する</button>
        </div>
      </div>

      <!-- 削除確認フォーム (#3a)。論理削除のため取り消し前提の文言。 -->
      <div v-if="showDeleteForm" class="mb-4 rounded-lg border border-red-300 bg-red-50 p-4">
        <div class="mb-2 font-semibold text-red-800">発注書を削除</div>
        <p class="mb-2 text-sm text-red-700">この発注書を削除 (論理削除) します。一覧では既定で非表示になります。よろしいですか？</p>
        <div class="flex justify-end gap-2">
          <button type="button" class="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50" @click="showDeleteForm = false">キャンセル</button>
          <button type="button" class="rounded-md bg-red-600 px-3 py-1 text-sm text-white hover:bg-red-700" @click="onSoftDelete">削除する</button>
        </div>
      </div>

      <!-- 帳票出力フォーム (旧システム「発注書出力」画面相当)。発注日 / 出荷指示番号 / 発注番号 を
           手入力し、出力帳票 (発注書 / 管理表 / 発注書+管理表) を選んで出力する。モバイルは縦積み (原則8)。 -->
      <div v-if="showExportForm" class="fixed inset-0 z-40 flex items-start justify-center overflow-y-auto bg-black/40 p-4 sm:items-center">
        <div class="w-full max-w-lg rounded-lg border border-gray-200 bg-white p-5 shadow-xl">
          <div class="mb-3 flex items-center justify-between border-b border-gray-100 pb-2">
            <h2 class="font-semibold text-gray-800">帳票出力</h2>
            <button type="button" class="text-gray-400 hover:text-gray-600" aria-label="閉じる" @click="showExportForm = false">✕</button>
          </div>
          <p class="mb-3 text-xs text-gray-500">
            発注日・出荷指示番号・発注番号を入力して出力します。入力内容は発注に保存され、次回出力時に初期表示されます。
          </p>
          <div class="grid grid-cols-1 gap-x-3 gap-y-2 text-sm sm:grid-cols-2">
            <label class="flex flex-col gap-1">
              <span class="font-medium">発注日</span>
              <input v-model="exportForm.orderDate" type="date" class="rounded-md border border-gray-300 px-2.5 py-1.5 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500" />
            </label>
            <label class="flex flex-col gap-1">
              <span class="font-medium">発注番号</span>
              <input v-model="exportForm.orderNo" type="text" maxlength="16" placeholder="例: S3858" class="rounded-md border border-gray-300 px-2.5 py-1.5 font-mono focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500" />
            </label>
            <label class="flex flex-col gap-1 sm:col-span-2">
              <span class="font-medium">出荷指示番号</span>
              <input v-model="exportForm.shippingInstructionNo" type="text" maxlength="32" placeholder="（任意）" class="rounded-md border border-gray-300 px-2.5 py-1.5 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500" />
            </label>
          </div>

          <!-- 出力帳票選択 (発注書のみ / 管理表のみ / 発注書+管理表) -->
          <fieldset class="mt-4">
            <legend class="mb-2 text-sm font-medium text-gray-700">出力帳票選択</legend>
            <div class="flex flex-col gap-2 sm:flex-row sm:flex-wrap sm:gap-4">
              <label v-for="f in exportFormats" :key="f.key" class="inline-flex items-center gap-2 text-sm">
                <input v-model="exportForm.format" type="radio" :value="f.key" class="h-4 w-4 border-gray-300" />
                <span>{{ f.label }}</span>
              </label>
            </div>
          </fieldset>

          <div class="mt-5 flex justify-end gap-2">
            <button type="button" class="rounded-md border border-gray-300 bg-white px-4 py-1.5 text-sm hover:bg-gray-50" @click="showExportForm = false">キャンセル</button>
            <button
              type="button"
              :disabled="downloading"
              class="rounded-md bg-green-600 px-4 py-1.5 text-sm text-white hover:bg-green-700 disabled:opacity-50"
              @click="onSubmitExport"
            >
              {{ downloading ? '出力中…' : '出力' }}
            </button>
          </div>
        </div>
      </div>

      <!-- ヘッダ情報 -->
      <section class="mb-4 rounded-lg border border-gray-200 bg-white p-3 shadow-sm">
        <div class="mb-3 flex items-center justify-between border-b border-gray-100 pb-2">
          <h2 class="font-semibold">発注書情報</h2>
          <!-- 発注区分 国内/海外 バッジ (Phase B、is_overseas) -->
          <span
            :class="detail.isOverseas ? 'bg-indigo-100 text-indigo-700' : 'bg-gray-100 text-gray-600'"
            class="inline-block rounded-full px-2.5 py-0.5 text-xs font-medium"
          >{{ detail.isOverseas ? '海外' : '国内' }}</span>
        </div>
        <div class="grid grid-cols-1 gap-x-6 gap-y-2 text-sm sm:grid-cols-2 lg:grid-cols-4">
          <div><span class="text-gray-500">発注先:</span> {{ detail.supplierCode }} {{ detail.supplierName }}</div>
          <div><span class="text-gray-500">納品先:</span> {{ detail.deliveryDestinationName }}</div>
          <div><span class="text-gray-500">発注事業部:</span> {{ detail.departmentName }}</div>
          <div><span class="text-gray-500">納入倉庫1:</span> {{ detail.warehouseName }}</div>
          <div><span class="text-gray-500">取引先納入日:</span> {{ detail.dueDate }}</div>
          <div><span class="text-gray-500">発注担当者:</span> {{ detail.ordererName }}</div>
          <div><span class="text-gray-500">発注管理者:</span> {{ detail.managerName }}</div>
          <div><span class="text-gray-500">作成日:</span> {{ formatJstDateTime(detail.createdAt) }}</div>
          <!-- 帳票出力フォーム 手入力項目 (発注日 / 出荷指示番号)。出力時に保存された値を表示。 -->
          <div><span class="text-gray-500">発注日:</span> {{ detail.orderDate || '—' }}</div>
          <div><span class="text-gray-500">出荷指示番号:</span> {{ detail.shippingInstructionNo || '—' }}</div>
        </div>

        <!-- 発注情報 (国内/海外共通、§5)。荷揚地・得意先・各出荷日・納入倉庫2/3 は区分に関わらず表示する。 -->
        <div class="mt-3 rounded-md border border-gray-200 bg-gray-50 px-3 py-2">
          <div class="mb-1 text-xs font-semibold text-gray-700">発注情報</div>
          <div class="grid grid-cols-1 gap-x-6 gap-y-1 text-sm sm:grid-cols-2 lg:grid-cols-3">
            <div><span class="text-gray-500">荷揚地:</span> {{ detail.landingPlace || '—' }}</div>
            <div><span class="text-gray-500">得意先:</span> {{ detail.customerRef || '—' }}</div>
            <div><span class="text-gray-500">工場出荷日:</span> {{ detail.factoryShippingDate || '—' }}</div>
            <div><span class="text-gray-500">検品場出荷日:</span> {{ detail.deliveryPlaceShippingDate || '—' }}</div>
            <div><span class="text-gray-500">海外出港日:</span> {{ detail.overseasDepartureDate || '—' }}</div>
            <div><span class="text-gray-500">納入倉庫2:</span> {{ detail.warehouse2Name || '—' }}</div>
            <div><span class="text-gray-500">納入倉庫3:</span> {{ detail.warehouse3Name || '—' }}</div>
          </div>
        </div>
        <div v-if="detail.firstExportedAt" class="mt-3 rounded-md border border-blue-200 bg-blue-50 px-3 py-2 text-xs">
          <div><strong>初回 Excel 出力:</strong> {{ formatJstDateTime(detail.firstExportedAt) }}</div>
          <div><strong>最終出力:</strong> {{ detail.lastExportedAt ? formatJstDateTime(detail.lastExportedAt) : '—' }}</div>
          <div class="mt-1 text-gray-600">
            <strong>帳票宛名 (F-22 snapshot 凍結):</strong>
            「{{ detail.supplierOfficialNameSnapshot ?? '?' }} 御中 {{ detail.supplierCodeSnapshot ?? '?' }}」
          </div>
          <div v-if="detail.customerNameSnapshot" class="text-gray-600">
            <strong>取引先 snapshot (内部識別):</strong> {{ detail.customerNameSnapshot }}
          </div>
        </div>
        <div v-if="detail.status === 1" class="mt-3 rounded-md border border-orange-200 bg-orange-50 px-3 py-2 text-xs">
          <div><strong>中止日時:</strong> {{ detail.cancelledAt ? formatJstDateTime(detail.cancelledAt) : '—' }}</div>
          <div><strong>中止理由:</strong> {{ detail.cancelReason ?? '—' }}</div>
        </div>
        <!-- 発注済情報 (§3b)。ユーザー操作で発注済にした日時。ダウンロードとは独立。 -->
        <div v-if="detail.orderedAt" class="mt-3 rounded-md border border-green-200 bg-green-50 px-3 py-2 text-xs">
          <div><strong>発注済日時:</strong> {{ formatJstDateTime(detail.orderedAt) }}</div>
        </div>
        <!-- 発注削除情報 (§3b、論理削除) -->
        <div v-if="detail.isDeleted" class="mt-3 rounded-md border border-red-200 bg-red-50 px-3 py-2 text-xs">
          <div><strong>削除日時:</strong> {{ detail.deletedAt ? formatJstDateTime(detail.deletedAt) : '—' }}</div>
          <div class="text-gray-600">この発注書は削除済み (論理削除) です。一覧では既定で非表示になります。</div>
        </div>
      </section>

      <!-- 明細 -->
      <section class="mb-4 overflow-x-auto rounded-lg border border-gray-200 bg-white p-3 shadow-sm">
        <h2 class="mb-3 border-b border-gray-100 pb-2 font-semibold">
          明細 ({{ detail.lines.length }} 件、合計 {{ detail.lines[0]?.currencyCodeSnapshot ?? 'JPY' }} {{ totalAmount.toLocaleString() }})
        </h2>

        <table v-if="!editing" class="w-full text-sm">
          <thead class="border-b border-gray-200 bg-gray-50">
            <tr>
              <th class="px-2 py-1.5 text-left">No</th>
              <th class="px-2 py-1.5 text-left">品番</th>
              <th class="px-2 py-1.5 text-left">SKU</th>
              <th class="px-2 py-1.5 text-left">商品名 / 色 / サイズ</th>
              <th class="px-2 py-1.5 text-left">仮番号</th>
              <th class="px-2 py-1.5 text-right">発注数</th>
              <th class="px-2 py-1.5 text-right">入数</th>
              <th class="px-2 py-1.5 text-right">仕入単価</th>
              <th class="px-2 py-1.5 text-right">見積単価</th>
              <th class="px-2 py-1.5 text-left">備考</th>
              <th class="px-2 py-1.5 text-right">小計</th>
            </tr>
          </thead>
          <tbody>
            <template v-for="l in detail.lines" :key="l.id">
            <tr class="border-b border-gray-100" :class="l.deliveries.length > 0 ? '' : 'last:border-0'">
              <td class="px-2 py-1.5 font-mono">{{ l.lineNo }}</td>
              <td class="px-2 py-1.5 font-mono">{{ l.sku.slice(0, 7) }}</td>
              <td class="px-2 py-1.5 font-mono">{{ l.sku }}</td>
              <td class="px-2 py-1.5">{{ l.productName }} / {{ l.colorName }} / {{ l.sizeName }}</td>
              <td class="px-2 py-1.5 font-mono">{{ l.provisionalNumberSnapshot || '—' }}</td>
              <td class="px-2 py-1.5 text-right font-mono">{{ l.quantity }}</td>
              <td class="px-2 py-1.5 text-right font-mono">{{ l.packQuantity != null ? l.packQuantity.toLocaleString() : '—' }}</td>
              <td class="px-2 py-1.5 text-right font-mono">{{ l.currencyCodeSnapshot }} {{ l.unitPriceSnapshot.toLocaleString() }}</td>
              <td class="px-2 py-1.5 text-right font-mono">{{ l.estimateUnitPrice != null ? l.estimateUnitPrice.toLocaleString() : '—' }}</td>
              <td class="px-2 py-1.5">{{ l.remark || '—' }}</td>
              <td class="px-2 py-1.5 text-right font-mono">{{ l.subtotal.toLocaleString() }}</td>
            </tr>
            <!-- 分納×倉庫の多次元明細 (PR5b、読取表示)。分納がある明細のみ、内訳を 1 分納 = 1 行で表示。 -->
            <tr v-if="l.deliveries.length > 0" class="border-b border-gray-100 last:border-0 bg-gray-50 text-xs text-gray-600">
              <td colspan="11" class="px-3 py-2">
                <span class="font-semibold">分納 / 倉庫別:</span>
                <span v-for="d in l.deliveries" :key="d.id" class="ml-2 inline-block rounded border border-gray-300 bg-white px-2 py-0.5">
                  {{ d.warehouseName || '倉庫未指定' }} / {{ d.deliveryDate || '(発注明細日)' }} / 数量 {{ d.quantity.toLocaleString() }}<template v-if="d.packQuantity != null"> / 入数 {{ d.packQuantity.toLocaleString() }}</template>
                </span>
              </td>
            </tr>
            </template>
          </tbody>
        </table>

        <!-- 編集モード -->
        <div v-else>
          <table class="w-full text-sm">
            <thead class="border-b border-gray-200 bg-gray-50">
              <tr>
                <th class="px-2 py-1.5 text-left">品番</th>
                <th class="px-2 py-1.5 text-left">SKU</th>
                <th class="px-2 py-1.5 text-left">仮番号</th>
                <th class="px-2 py-1.5 text-right">発注数</th>
                <th class="px-2 py-1.5 text-right">入数</th>
                <th class="px-2 py-1.5 text-right">仕入単価</th>
                <th class="px-2 py-1.5 text-right">見積単価</th>
                <th class="px-2 py-1.5 text-left">備考</th>
                <th class="px-2 py-1.5 text-right">小計</th>
              </tr>
            </thead>
            <tbody>
              <template v-for="(l, idx) in editLines" :key="idx">
              <tr class="border-b border-gray-100" :class="l.showDeliveries ? '' : 'last:border-0'">
                <td class="px-2 py-1.5 font-mono">{{ l.sku.slice(0, 7) }}</td>
                <td class="px-2 py-1.5 font-mono">{{ l.sku }}</td>
                <td class="px-2 py-1.5 font-mono text-gray-500">{{ l.provisionalNumberSnapshot || '—' }}</td>
                <td class="px-2 py-1.5 text-right">
                  <!-- 分納あり時は数量を合計の読取表示 (単一数量入力は分納なし時のみ、PR5b)。 -->
                  <input v-if="l.deliveries.length === 0" v-model.number="l.quantity" type="number" min="1" class="w-20 rounded-md border border-gray-300 px-2 py-1 text-right" />
                  <span v-else class="inline-block w-20 px-2 py-1 text-right font-mono text-gray-700" title="分納数量の合計">{{ editLineQuantity(l).toLocaleString() }}</span>
                </td>
                <td class="px-2 py-1.5 text-right">
                  <input v-model.number="l.packQuantity" type="number" min="0" placeholder="—" class="w-20 rounded-md border border-gray-300 px-2 py-1 text-right" />
                </td>
                <td class="px-2 py-1.5 text-right">
                  <input v-model.number="l.unitPriceSnapshot" type="number" min="0" step="0.01" class="w-24 rounded-md border border-gray-300 px-2 py-1 text-right" />
                </td>
                <td class="px-2 py-1.5 text-right">
                  <input v-model.number="l.estimateUnitPrice" type="number" min="0" step="0.01" placeholder="—" class="w-24 rounded-md border border-gray-300 px-2 py-1 text-right" />
                </td>
                <td class="px-2 py-1.5">
                  <input v-model="l.remark" type="text" maxlength="255" placeholder="—" class="w-32 rounded-md border border-gray-300 px-2 py-1" />
                </td>
                <td class="px-2 py-1.5 text-right font-mono">
                  <div>{{ editLineSubtotal(l).toLocaleString() }}</div>
                  <!-- 分納 / 倉庫別 サブセクションの開閉トグル (PR5b)。 -->
                  <button type="button" class="text-xs font-normal text-blue-600 hover:underline" @click="l.showDeliveries = !l.showDeliveries">
                    分納{{ l.deliveries.length > 0 ? ` (${l.deliveries.length})` : '' }}
                  </button>
                </td>
              </tr>
              <!-- 分納 / 倉庫別 サブセクション (PR5b、任意・折りたたみ可)。倉庫 + 納期 + 数量 + 入数 を行追加/削除。 -->
              <tr v-if="l.showDeliveries" class="border-b border-gray-100 last:border-0 bg-gray-50">
                <td colspan="9" class="px-3 py-2">
                  <div class="mb-2 flex items-center justify-between">
                    <span class="text-xs font-semibold text-gray-600">分納 / 倉庫別 (任意 — 倉庫×納期で多次元化。空なら単一明細)</span>
                    <button type="button" class="rounded-md border border-gray-300 bg-white px-2 py-0.5 text-xs hover:bg-gray-100" @click="addDelivery(idx)">+ 分納行を追加</button>
                  </div>
                  <div v-if="l.deliveries.length === 0" class="py-2 text-center text-xs text-gray-400">
                    分納なし (単一明細)。倉庫別・複数納期で分けたい場合は「+ 分納行を追加」してください。
                  </div>
                  <div v-else class="space-y-2">
                    <div v-for="(d, dIdx) in l.deliveries" :key="dIdx" class="grid grid-cols-1 gap-2 rounded-md border border-gray-200 bg-white p-2 sm:grid-cols-[1fr_10rem_7rem_7rem_auto] sm:items-end">
                      <label class="flex flex-col gap-0.5">
                        <span class="text-xs font-medium text-gray-600">倉庫</span>
                        <MasterSelect v-model="d.warehouseId" :items="warehouses" allow-empty empty-label="（未指定）" placeholder="（任意）" />
                      </label>
                      <label class="flex flex-col gap-0.5">
                        <span class="text-xs font-medium text-gray-600">納期</span>
                        <input v-model="d.deliveryDate" type="date" class="rounded-md border border-gray-300 px-2 py-1 text-sm" />
                      </label>
                      <label class="flex flex-col gap-0.5">
                        <span class="text-xs font-medium text-gray-600">数量 <span class="text-red-500">*</span></span>
                        <input v-model.number="d.quantity" type="number" min="1" class="rounded-md border border-gray-300 px-2 py-1 text-right text-sm" />
                      </label>
                      <label class="flex flex-col gap-0.5">
                        <span class="text-xs font-medium text-gray-600">入数</span>
                        <input v-model.number="d.packQuantity" type="number" min="0" placeholder="—" class="rounded-md border border-gray-300 px-2 py-1 text-right text-sm" />
                      </label>
                      <button type="button" class="h-fit rounded-md border border-red-300 bg-white px-2 py-1 text-xs text-red-600 hover:bg-red-50" @click="removeDelivery(idx, dIdx)">削除</button>
                    </div>
                    <div class="text-right text-xs text-gray-500">分納合計数量: <span class="font-mono font-semibold">{{ editLineQuantity(l).toLocaleString() }}</span></div>
                  </div>
                </td>
              </tr>
              </template>
            </tbody>
          </table>

          <!-- 発注区分 + 発注情報 編集 (§5 国内/海外共通)。区分トグルは帳票の言語切替のみに使い、
               入力欄の出し分けはしない (国内でも荷揚地・各出荷日・納入倉庫2/3 を編集可能)。 -->
          <div class="mt-4 rounded-md border border-gray-200 bg-gray-50 p-4">
            <div class="mb-3 flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
              <span class="font-semibold text-gray-700">発注区分</span>
              <div class="inline-flex self-start overflow-hidden rounded-md border border-gray-300 text-sm">
                <button
                  type="button"
                  class="px-4 py-1.5 font-medium transition-colors"
                  :class="!editHeader.isOverseas ? 'bg-blue-600 text-white' : 'bg-white text-gray-600 hover:bg-gray-50'"
                  @click="editHeader.isOverseas = false"
                >国内</button>
                <button
                  type="button"
                  class="border-l border-gray-300 px-4 py-1.5 font-medium transition-colors"
                  :class="editHeader.isOverseas ? 'bg-blue-600 text-white' : 'bg-white text-gray-600 hover:bg-gray-50'"
                  @click="editHeader.isOverseas = true"
                >海外</button>
              </div>
            </div>
            <div class="grid grid-cols-1 gap-x-3 gap-y-2 text-sm sm:grid-cols-2 lg:grid-cols-3">
              <label class="flex flex-col gap-1">
                <span class="font-medium">荷揚地</span>
                <input v-model="editHeader.landingPlace" type="text" maxlength="128" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="font-medium">得意先</span>
                <input v-model="editHeader.customerRef" type="text" maxlength="128" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="font-medium">工場出荷日</span>
                <input v-model="editHeader.factoryShippingDate" type="date" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="font-medium">検品場出荷日</span>
                <input v-model="editHeader.deliveryPlaceShippingDate" type="date" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="font-medium">海外出港日</span>
                <input v-model="editHeader.overseasDepartureDate" type="date" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="font-medium">納入倉庫2</span>
                <MasterSelect v-model="editHeader.warehouse2Id" :items="warehouses" allow-empty empty-label="（なし）" placeholder="（任意）" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="font-medium">納入倉庫3</span>
                <MasterSelect v-model="editHeader.warehouse3Id" :items="warehouses" allow-empty empty-label="（なし）" placeholder="（任意）" />
              </label>
            </div>
          </div>

          <!-- 連絡文書 6 行 (構造化、PR6。旧 spec 発注明細 No.27-32「連絡文書01行〜06行」) -->
          <div class="mt-4 rounded-md border border-gray-200 bg-gray-50 p-4">
            <div class="mb-3 flex flex-col gap-1 sm:flex-row sm:items-center sm:justify-between">
              <span class="font-semibold text-gray-700">連絡文書 (6 行)</span>
              <p class="text-xs text-gray-500">各行はテンプレ選択 + 自由編集できます (空行は出力されません)。</p>
            </div>
            <!-- 6 行スロット。各行: テンプレ選択 (AutoComplete) + テキスト入力。モバイルは縦積み (原則8)。 -->
            <div class="space-y-2">
              <div
                v-for="(_, i) in editCommLines"
                :key="i"
                class="flex flex-col gap-2 sm:flex-row sm:items-center"
              >
                <span class="w-12 shrink-0 text-xs font-medium text-gray-500">{{ (i + 1).toString().padStart(2, '0') }} 行</span>
                <input
                  v-model="editCommLines[i]"
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
          </div>

          <!-- F-16 EditReason 必須 -->
          <div class="mt-4 rounded-md bg-yellow-50 p-4">
            <div class="mb-2 font-semibold text-yellow-800">編集理由 (F-16 必須)</div>
            <div class="grid grid-cols-1 gap-x-3 gap-y-2 text-sm sm:grid-cols-2">
              <label class="flex flex-col gap-1">
                <span class="font-medium">理由 <span class="text-red-500">*</span></span>
                <AutoComplete :model-value="editReason" :options="editReasonOptions.map((r) => ({ value: r, label: editReasonLabel(r) }))" :allow-empty="false" placeholder="理由を選択…" @update:model-value="(v) => editReason = v as EditReason" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="font-medium">メモ (任意)</span>
                <input v-model="editNote" type="text" maxlength="255" placeholder="補足説明 (audit_logs に記録)" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
              </label>
            </div>
          </div>

          <div class="mt-4 flex justify-end gap-2">
            <button type="button" class="rounded-md border border-gray-300 bg-white px-4 py-1.5 text-sm hover:bg-gray-50" @click="onCancelEdit">キャンセル</button>
            <button type="button" class="rounded-md bg-blue-600 px-4 py-1.5 text-sm text-white hover:bg-blue-700" @click="onSaveEdit">保存</button>
          </div>
        </div>
      </section>

      <!-- 連絡文書 (構造化 6 行優先、PR6)。編集中は上の編集スロットを使うため非表示。
           新データは 6 列を 1 行ずつ表示。6 列が全て空の旧発注は communicationText にフォールバック。 -->
      <section
        v-if="!editing && (displayCommLines.length > 0 || detail.communicationText)"
        class="mb-4 rounded-lg border border-gray-200 bg-white p-3 shadow-sm"
      >
        <h2 class="mb-3 border-b border-gray-100 pb-2 font-semibold">連絡文書</h2>
        <ol v-if="displayCommLines.length > 0" class="space-y-1 text-sm">
          <li v-for="(line, i) in displayCommLines" :key="i" class="flex gap-2">
            <span class="shrink-0 font-mono text-xs text-gray-400">{{ (i + 1).toString().padStart(2, '0') }}</span>
            <span class="whitespace-pre-wrap">{{ line }}</span>
          </li>
        </ol>
        <!-- フォールバック (旧データ): 6 列が全て空の発注は従来の communicationText を表示。 -->
        <pre v-else class="whitespace-pre-wrap text-sm">{{ detail.communicationText }}</pre>
      </section>

      <p class="mt-3 text-xs text-gray-400">
        API: GET/PATCH/POST cancel/POST export /api/maker/v1/orders/{{ id }}
      </p>
    </template>
  </main>
</template>
