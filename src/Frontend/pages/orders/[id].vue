<script setup lang="ts">
import type { OrderDetail, EditReason } from '~/composables/useOrders'
import { editReasonLabel, orderStatusLabel } from '~/composables/useOrders'

const route = useRoute()
const id = computed(() => Number(route.params.id))
const { user } = useAuth()
const canEditOrder = computed(() => (user.value?.purchaseOrderCreatePermission ?? 0) >= 1)

const { get, update, cancel, downloadExcel } = useOrders()

const detail = ref<OrderDetail | null>(null)
const loading = ref(true)
const errorMessage = ref('')
const successMessage = ref('')
const downloading = ref(false)

// 編集モード
const editing = ref(false)
const editLines = ref<{ id: number | null; productId: number; sku: string; productName: string; quantity: number; unitPriceSnapshot: number; currencyCodeSnapshot: string; packQuantity: number | null; estimateUnitPrice: number | null; provisionalNumberSnapshot: string | null }[]>([])
const editReason = ref<EditReason>('quantity')
const editNote = ref('')

// 旧 発注書 国内/海外 項目 (Phase B) の編集状態。reload 時に detail から初期化する。
const editHeader = ref({
  isOverseas: false,
  landingPlace: '' as string,
  customerRef: '' as string,
  factoryShippingDate: '' as string,
  inspectionShippingDate: '' as string,
  overseasDepartureDate: '' as string,
  warehouse2Id: null as number | null,
  warehouse3Id: null as number | null,
})
// 納入倉庫2/3 編集用にマスタを読み込む
const { list: listMasters } = useMasters()
const warehouses = ref<{ id: number; code?: string | null; name?: string | null }[]>([])

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
      }))
      // 旧 発注書 国内/海外 項目 (Phase B) の編集状態を detail から初期化 (null → 空文字に正規化)
      editHeader.value = {
        isOverseas: detail.value.isOverseas,
        landingPlace: detail.value.landingPlace ?? '',
        customerRef: detail.value.customerRef ?? '',
        factoryShippingDate: detail.value.factoryShippingDate ?? '',
        inspectionShippingDate: detail.value.inspectionShippingDate ?? '',
        overseasDepartureDate: detail.value.overseasDepartureDate ?? '',
        warehouse2Id: detail.value.warehouse2Id,
        warehouse3Id: detail.value.warehouse3Id,
      }
    }
  } catch (e) {
    const err = e as { statusCode?: number }
    errorMessage.value = err.statusCode === 404 ? '発注書が見つかりません' : '取得に失敗しました'
  } finally {
    loading.value = false
  }
}

onMounted(async () => {
  // 納入倉庫2/3 編集用マスタを読み込む (失敗しても本体表示は継続、原則 4 非ブロッキング)
  try {
    warehouses.value = await listMasters('warehouses')
  } catch {
    warehouses.value = []
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

const onSaveEdit = async () => {
  if (!detail.value) return
  errorMessage.value = ''
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
      communicationText: detail.value.communicationText,
      lines: editLines.value.map((l) => ({
        productId: l.productId,
        quantity: Number(l.quantity),
        unitPriceSnapshot: Number(l.unitPriceSnapshot),
        currencyCodeSnapshot: l.currencyCodeSnapshot,
        packQuantity: l.packQuantity != null ? Number(l.packQuantity) : null,
        estimateUnitPrice: l.estimateUnitPrice != null ? Number(l.estimateUnitPrice) : null,
      })),
      // 旧 発注書 国内/海外 項目 (Phase B)。海外区分が false のときは海外専用項目は送らない (null/空)。
      isOverseas: editHeader.value.isOverseas,
      landingPlace: editHeader.value.isOverseas ? (editHeader.value.landingPlace.trim() || null) : null,
      customerRef: editHeader.value.isOverseas ? (editHeader.value.customerRef.trim() || null) : null,
      factoryShippingDate: editHeader.value.isOverseas ? (editHeader.value.factoryShippingDate || null) : null,
      inspectionShippingDate: editHeader.value.isOverseas ? (editHeader.value.inspectionShippingDate || null) : null,
      overseasDepartureDate: editHeader.value.isOverseas ? (editHeader.value.overseasDepartureDate || null) : null,
      warehouse2Id: editHeader.value.isOverseas ? editHeader.value.warehouse2Id : null,
      warehouse3Id: editHeader.value.isOverseas ? editHeader.value.warehouse3Id : null,
    })
    successMessage.value = '更新しました'
    editing.value = false
    await reload()
  } catch (e) {
    const err = e as { data?: { detail?: string }; statusCode?: number }
    errorMessage.value = err.statusCode === 409
      ? '中止済みの発注書は編集できません'
      : err.data?.detail ?? '更新に失敗しました'
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

const onDownload = async () => {
  downloading.value = true
  errorMessage.value = ''
  try {
    await downloadExcel(id.value)
    successMessage.value = 'Excel をダウンロードしました'
    await reload()
  } catch (e) {
    const err = e as { data?: { detail?: string } }
    errorMessage.value = err.data?.detail ?? 'Excel 出力に失敗しました'
  } finally {
    downloading.value = false
  }
}

const exportBadge = computed(() => {
  if (!detail.value) return null
  if (!detail.value.firstExportedAt) return { label: '未出力', cls: 'bg-gray-100 text-gray-600' }
  const dt = new Date(detail.value.firstExportedAt).toLocaleString('ja-JP')
  return { label: `初回出力済 (${dt})`, cls: 'bg-blue-100 text-blue-700' }
})

const editReasonOptions: EditReason[] = ['quantity', 'deadline', 'supplier', 'typo', 'other']
</script>

<template>
  <main class="mx-auto max-w-6xl px-4 py-8">
    <div v-if="loading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500">読み込み中…</div>

    <div v-else-if="!detail" class="rounded border border-red-300 bg-red-50 p-4 text-red-700">
      {{ errorMessage || '発注書が見つかりません' }}
      <div class="mt-2"><NuxtLink to="/orders" class="text-blue-600 underline">発注書一覧に戻る</NuxtLink></div>
    </div>

    <template v-else>
      <header class="mb-6 flex items-start justify-between gap-4">
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
            <span :class="detail.status === 1 ? 'bg-orange-100 text-orange-700' : 'bg-green-100 text-green-700'"
                  class="inline-block rounded-full px-2 py-0.5 text-xs">
              {{ orderStatusLabel(detail.status) }}
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
            :disabled="downloading"
            class="rounded-md bg-green-600 px-3 py-1.5 text-sm text-white hover:bg-green-700 disabled:opacity-50"
            @click="onDownload"
          >
            {{ downloading ? '出力中…' : '📥 Excel ダウンロード' }}
          </button>
          <button
            v-if="canEditOrder && detail.status === 0 && !editing"
            type="button"
            class="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-sm hover:bg-gray-50"
            @click="onStartEdit"
          >編集</button>
          <button
            v-if="canEditOrder && detail.status === 0 && !editing"
            type="button"
            class="rounded-md border border-red-300 bg-white px-3 py-1.5 text-sm text-red-600 hover:bg-red-50"
            @click="showCancelForm = true"
          >中止</button>
        </div>
      </header>

      <div v-if="errorMessage" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">{{ errorMessage }}</div>
      <div v-if="successMessage" class="mb-3 rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-700">{{ successMessage }}</div>

      <!-- 中止フォーム -->
      <div v-if="showCancelForm" class="mb-4 rounded-lg border border-orange-300 bg-orange-50 p-4">
        <div class="mb-2 font-semibold text-orange-800">発注書を中止</div>
        <input v-model="cancelReason" type="text" placeholder="中止理由を入力 (必須)" class="mb-2 w-full rounded-md border border-gray-300 px-3 py-2 text-sm" />
        <div class="flex justify-end gap-2">
          <button type="button" class="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50" @click="showCancelForm = false">キャンセル</button>
          <button type="button" class="rounded-md bg-orange-600 px-3 py-1 text-sm text-white hover:bg-orange-700" @click="onCancelOrder">中止する</button>
        </div>
      </div>

      <!-- ヘッダ情報 -->
      <section class="mb-6 rounded-lg border border-gray-200 bg-white p-5 shadow-sm">
        <div class="mb-3 flex items-center justify-between border-b border-gray-100 pb-2">
          <h2 class="font-semibold">発注書情報</h2>
          <!-- 発注区分 国内/海外 バッジ (Phase B、is_overseas) -->
          <span
            :class="detail.isOverseas ? 'bg-indigo-100 text-indigo-700' : 'bg-gray-100 text-gray-600'"
            class="inline-block rounded-full px-2.5 py-0.5 text-xs font-medium"
          >{{ detail.isOverseas ? '海外' : '国内' }}</span>
        </div>
        <div class="grid grid-cols-1 gap-x-6 gap-y-2 text-sm sm:grid-cols-2">
          <div><span class="text-gray-500">仕入先:</span> {{ detail.supplierCode }} {{ detail.supplierName }}</div>
          <div><span class="text-gray-500">納品先:</span> {{ detail.deliveryDestinationName }}</div>
          <div><span class="text-gray-500">事業部:</span> {{ detail.departmentName }}</div>
          <div><span class="text-gray-500">納入倉庫:</span> {{ detail.warehouseName }}</div>
          <div><span class="text-gray-500">納入日:</span> {{ detail.dueDate }}</div>
          <div><span class="text-gray-500">発注担当:</span> {{ detail.ordererName }}</div>
          <div><span class="text-gray-500">発注管理者:</span> {{ detail.managerName }}</div>
          <div><span class="text-gray-500">作成日:</span> {{ new Date(detail.createdAt).toLocaleString('ja-JP') }}</div>
        </div>

        <!-- 海外発注情報 (is_overseas=true のときのみ、Phase B) -->
        <div v-if="detail.isOverseas" class="mt-3 rounded-md border border-indigo-200 bg-indigo-50 px-3 py-2">
          <div class="mb-1 text-xs font-semibold text-indigo-800">海外発注情報</div>
          <div class="grid grid-cols-1 gap-x-6 gap-y-1 text-sm sm:grid-cols-2">
            <div><span class="text-gray-500">荷揚地:</span> {{ detail.landingPlace || '—' }}</div>
            <div><span class="text-gray-500">得意先:</span> {{ detail.customerRef || '—' }}</div>
            <div><span class="text-gray-500">工場出荷日:</span> {{ detail.factoryShippingDate || '—' }}</div>
            <div><span class="text-gray-500">検品所出荷日:</span> {{ detail.inspectionShippingDate || '—' }}</div>
            <div><span class="text-gray-500">海外出港日:</span> {{ detail.overseasDepartureDate || '—' }}</div>
            <div><span class="text-gray-500">納入倉庫2:</span> {{ detail.warehouse2Name || '—' }}</div>
            <div><span class="text-gray-500">納入倉庫3:</span> {{ detail.warehouse3Name || '—' }}</div>
          </div>
        </div>
        <div v-if="detail.firstExportedAt" class="mt-3 rounded-md border border-blue-200 bg-blue-50 px-3 py-2 text-xs">
          <div><strong>初回 Excel 出力:</strong> {{ new Date(detail.firstExportedAt).toLocaleString('ja-JP') }}</div>
          <div><strong>最終出力:</strong> {{ detail.lastExportedAt ? new Date(detail.lastExportedAt).toLocaleString('ja-JP') : '—' }}</div>
          <div class="mt-1 text-gray-600">
            <strong>帳票宛名 (F-22 snapshot 凍結):</strong>
            「{{ detail.supplierOfficialNameSnapshot ?? '?' }} 御中 {{ detail.supplierCodeSnapshot ?? '?' }}」
          </div>
          <div v-if="detail.customerNameSnapshot" class="text-gray-600">
            <strong>取引先 snapshot (内部識別):</strong> {{ detail.customerNameSnapshot }}
          </div>
        </div>
        <div v-if="detail.status === 1" class="mt-3 rounded-md border border-orange-200 bg-orange-50 px-3 py-2 text-xs">
          <div><strong>中止日時:</strong> {{ detail.cancelledAt ? new Date(detail.cancelledAt).toLocaleString('ja-JP') : '—' }}</div>
          <div><strong>中止理由:</strong> {{ detail.cancelReason ?? '—' }}</div>
        </div>
      </section>

      <!-- 明細 -->
      <section class="mb-6 rounded-lg border border-gray-200 bg-white p-5 shadow-sm">
        <h2 class="mb-3 border-b border-gray-100 pb-2 font-semibold">
          明細 ({{ detail.lines.length }} 件、合計 {{ detail.lines[0]?.currencyCodeSnapshot ?? 'JPY' }} {{ totalAmount.toLocaleString() }})
        </h2>

        <table v-if="!editing" class="w-full text-sm">
          <thead class="border-b border-gray-200 bg-gray-50">
            <tr>
              <th class="px-3 py-2 text-left">No</th>
              <th class="px-3 py-2 text-left">SKU</th>
              <th class="px-3 py-2 text-left">商品名 / 色 / サイズ</th>
              <th class="px-3 py-2 text-left">仮番号</th>
              <th class="px-3 py-2 text-right">数量</th>
              <th class="px-3 py-2 text-right">入数</th>
              <th class="px-3 py-2 text-right">単価</th>
              <th class="px-3 py-2 text-right">見積単価</th>
              <th class="px-3 py-2 text-right">小計</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="l in detail.lines" :key="l.id" class="border-b border-gray-100 last:border-0">
              <td class="px-3 py-2 font-mono">{{ l.lineNo }}</td>
              <td class="px-3 py-2 font-mono">{{ l.sku }}</td>
              <td class="px-3 py-2">{{ l.productName }} / {{ l.colorName }} / {{ l.sizeName }}</td>
              <td class="px-3 py-2 font-mono">{{ l.provisionalNumberSnapshot || '—' }}</td>
              <td class="px-3 py-2 text-right font-mono">{{ l.quantity }}</td>
              <td class="px-3 py-2 text-right font-mono">{{ l.packQuantity != null ? l.packQuantity.toLocaleString() : '—' }}</td>
              <td class="px-3 py-2 text-right font-mono">{{ l.currencyCodeSnapshot }} {{ l.unitPriceSnapshot.toLocaleString() }}</td>
              <td class="px-3 py-2 text-right font-mono">{{ l.estimateUnitPrice != null ? l.estimateUnitPrice.toLocaleString() : '—' }}</td>
              <td class="px-3 py-2 text-right font-mono">{{ l.subtotal.toLocaleString() }}</td>
            </tr>
          </tbody>
        </table>

        <!-- 編集モード -->
        <div v-else>
          <table class="w-full text-sm">
            <thead class="border-b border-gray-200 bg-gray-50">
              <tr>
                <th class="px-2 py-2 text-left">SKU</th>
                <th class="px-2 py-2 text-left">仮番号</th>
                <th class="px-2 py-2 text-right">数量</th>
                <th class="px-2 py-2 text-right">入数</th>
                <th class="px-2 py-2 text-right">単価</th>
                <th class="px-2 py-2 text-right">見積単価</th>
                <th class="px-2 py-2 text-right">小計</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(l, idx) in editLines" :key="idx" class="border-b border-gray-100 last:border-0">
                <td class="px-2 py-2 font-mono">{{ l.sku }}</td>
                <td class="px-2 py-2 font-mono text-gray-500">{{ l.provisionalNumberSnapshot || '—' }}</td>
                <td class="px-2 py-2 text-right">
                  <input v-model.number="l.quantity" type="number" min="1" class="w-20 rounded-md border border-gray-300 px-2 py-1 text-right" />
                </td>
                <td class="px-2 py-2 text-right">
                  <input v-model.number="l.packQuantity" type="number" min="0" placeholder="—" class="w-20 rounded-md border border-gray-300 px-2 py-1 text-right" />
                </td>
                <td class="px-2 py-2 text-right">
                  <input v-model.number="l.unitPriceSnapshot" type="number" min="0" step="0.01" class="w-24 rounded-md border border-gray-300 px-2 py-1 text-right" />
                </td>
                <td class="px-2 py-2 text-right">
                  <input v-model.number="l.estimateUnitPrice" type="number" min="0" step="0.01" placeholder="—" class="w-24 rounded-md border border-gray-300 px-2 py-1 text-right" />
                </td>
                <td class="px-2 py-2 text-right font-mono">{{ (l.quantity * l.unitPriceSnapshot).toLocaleString() }}</td>
              </tr>
            </tbody>
          </table>

          <!-- 発注区分 + 海外発注情報 編集 (Phase B) -->
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
            <div v-if="editHeader.isOverseas" class="grid grid-cols-1 gap-3 text-sm sm:grid-cols-2">
              <label class="flex flex-col gap-1">
                <span class="font-medium">荷揚地</span>
                <input v-model="editHeader.landingPlace" type="text" maxlength="128" class="rounded-md border border-gray-300 px-3 py-2" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="font-medium">得意先</span>
                <input v-model="editHeader.customerRef" type="text" maxlength="128" class="rounded-md border border-gray-300 px-3 py-2" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="font-medium">工場出荷日</span>
                <input v-model="editHeader.factoryShippingDate" type="date" class="rounded-md border border-gray-300 px-3 py-2" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="font-medium">検品所出荷日</span>
                <input v-model="editHeader.inspectionShippingDate" type="date" class="rounded-md border border-gray-300 px-3 py-2" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="font-medium">海外出港日</span>
                <input v-model="editHeader.overseasDepartureDate" type="date" class="rounded-md border border-gray-300 px-3 py-2" />
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

          <!-- F-16 EditReason 必須 -->
          <div class="mt-4 rounded-md bg-yellow-50 p-4">
            <div class="mb-2 font-semibold text-yellow-800">編集理由 (F-16 必須)</div>
            <div class="grid grid-cols-2 gap-3 text-sm">
              <label class="flex flex-col gap-1">
                <span class="font-medium">理由 <span class="text-red-500">*</span></span>
                <AutoComplete :model-value="editReason" :options="editReasonOptions.map((r) => ({ value: r, label: editReasonLabel(r) }))" :allow-empty="false" placeholder="理由を選択…" @update:model-value="(v) => editReason = v as EditReason" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="font-medium">メモ (任意)</span>
                <input v-model="editNote" type="text" maxlength="255" placeholder="補足説明 (audit_logs に記録)" class="rounded-md border border-gray-300 px-3 py-2" />
              </label>
            </div>
          </div>

          <div class="mt-4 flex justify-end gap-2">
            <button type="button" class="rounded-md border border-gray-300 bg-white px-4 py-1.5 text-sm hover:bg-gray-50" @click="onCancelEdit">キャンセル</button>
            <button type="button" class="rounded-md bg-blue-600 px-4 py-1.5 text-sm text-white hover:bg-blue-700" @click="onSaveEdit">保存</button>
          </div>
        </div>
      </section>

      <!-- 連絡文章 -->
      <section v-if="detail.communicationText" class="mb-6 rounded-lg border border-gray-200 bg-white p-5 shadow-sm">
        <h2 class="mb-3 border-b border-gray-100 pb-2 font-semibold">連絡文章</h2>
        <pre class="whitespace-pre-wrap text-sm">{{ detail.communicationText }}</pre>
      </section>

      <p class="mt-3 text-xs text-gray-400">
        API: GET/PATCH/POST cancel/GET export.xlsx /api/v1/orders/{{ id }}
      </p>
    </template>
  </main>
</template>
