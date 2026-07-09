<script setup lang="ts">
import type { FamilyDetail, ImageSummary } from '~/composables/useProducts'
import { productStatusLabel } from '~/composables/useProducts'
import type { MasterItem } from '~/composables/useMasters'

const route = useRoute()
// 第二段階契約: family id は uuid 文字列。数値変換せず文字列のまま使う。
const familyId = computed(() => String(route.params.familyId))
const { user, canEditMaster } = useAuth()
// 操作導線（次アクション）の権限: 生産指示・発注書の作成は発注書作成権限が必要。
const canCreateOrder = computed(() => (user.value?.purchaseOrderCreatePermission ?? 0) >= 1)
const {
  getFamily, updateFamily, deleteFamily,
  addSupplierPrice, uploadImage, deleteImage,
} = useProducts()
const { list } = useMasters()
const { apiData } = useApi()

const detail = ref<FamilyDetail | null>(null)
const loading = ref(true)
const errorMessage = ref('')
const successMessage = ref('')

// 編集用フォーム
const editing = ref(false)
const editForm = ref({
  // ID 系は uuid 文字列 (第二段階契約)。未選択は '' で表す (reload で既存値がロードされる)。
  brandId: '',
  functionId: null as string | null,
  productGroupId: '',
  upperMaterialId: '',
  insoleMaterialId: '',
  outsoleMaterialId: '',
  productName1: '',
  productName2: '',
  status: 1,
  // 旧 品番台帳 項目 (Phase A、全て任意)
  productYear: null as number | null,
  managementSeasonId: null as string | null,
  plannerUserId: null as string | null,
  provisionalNumber: '',
  sampleApprovalDate: '',
  retailPrice: null as number | null,
  deliveryPrice: null as number | null,
  planningCost: null as number | null,
  brandCost: null as number | null,
  royaltyTarget: null as number | null,
  royaltyRate: null as number | null,
  // 旧 品番台帳 項目 追補 (PR1、全て任意)
  remark: '',
  colorRemark: '',
})

// アソート/セット明細 (PR3、旧 spec No.37/38)。編集時に全置換する行コレクション。
// reload() で detail.setComponents から初期化、保存時に updateFamily へ配列を送る (全置換)。
const editSetComponents = ref<{ childItemNumber: string; quantity: number }[]>([])
const addSetComponent = () => {
  editSetComponents.value.push({ childItemNumber: '', quantity: 1 })
}
const removeSetComponent = (idx: number) => {
  editSetComponents.value.splice(idx, 1)
}

// 単価追加フォーム
const showPriceForm = ref(false)
const priceForm = ref({
  supplierId: '',
  // サイズ別仕入単価 (PR2)。null = 全サイズ共通の既定単価 (未選択)。
  sizeId: null as string | null,
  unitPrice: 0,
  currencyCode: 'JPY',
  exchangeRate: null as number | null,
  // 業務日付は JST 基準 (effectiveFrom は BR-04 単価履歴のクローズ日に直結するためずれ厳禁)
  effectiveFrom: todayJst(),
  decidedAt: todayJst(),
  // 旧 仕入コスト計算明細 項目 (Phase C、全て任意)
  estimateUnitPrice: null as number | null,
  estimateReceivedDate: '',
  estimateCost: null as number | null,
  estimateMarginRate: null as number | null,
  purchaseCost: null as number | null,
  purchaseMarginRate: null as number | null,
  lossCost: null as number | null,
  drayageCost: null as number | null,
  taxRate: null as number | null,
})

// マスタ参照
const brands = ref<MasterItem[]>([])
const functions_ = ref<MasterItem[]>([])
const productGroups = ref<MasterItem[]>([])
const materials = ref<MasterItem[]>([])
const suppliers = ref<MasterItem[]>([])
// サイズ別仕入単価 (PR2): 単価追加フォームのサイズ選択肢
const sizes = ref<MasterItem[]>([])
// 旧 品番台帳 項目 (Phase A): 管理季節・企画者の選択肢
const productSeasons = ref<MasterItem[]>([])
interface UserOption { id: string; loginId: string; displayName: string }
const users = ref<UserOption[]>([])
const userOptions = computed(() => users.value.map((u) => ({ id: u.id, label: `${u.displayName} (${u.loginId})` })))
const royaltyTargetOptions = [{ value: '1', label: '小売価格' }, { value: '2', label: '納品価格' }]
const royaltyTargetLabel = (t: number | null): string =>
  t === 1 ? '小売価格' : t === 2 ? '納品価格' : '—'

// 画像アップロード (§2a: 企画0 / 本番1 の区分ごと)
const fileInput = ref<HTMLInputElement | null>(null)
const uploading = ref(false)
const uploadCategory = ref(0) // 追加時の区分 (0=企画 / 1=本番)
const IMAGE_CATEGORIES = [
  { key: 0, label: '企画画像' },
  { key: 1, label: '本番画像' },
] as const
const imagesByCategory = (cat: number) => detail.value?.images.filter((i) => i.imageCategory === cat) ?? []

const reload = async () => {
  loading.value = true
  errorMessage.value = ''
  try {
    detail.value = await getFamily(familyId.value)
    if (detail.value) {
      editForm.value = {
        brandId: detail.value.family.brandId,
        functionId: detail.value.family.functionId,
        productGroupId: detail.value.family.productGroupId,
        upperMaterialId: detail.value.family.upperMaterialId,
        insoleMaterialId: detail.value.family.insoleMaterialId,
        outsoleMaterialId: detail.value.family.outsoleMaterialId,
        productName1: detail.value.family.productName1,
        productName2: detail.value.family.productName2 ?? '',
        status: detail.value.family.status,
        // 旧 品番台帳 項目 (Phase A)
        productYear: detail.value.family.productYear,
        managementSeasonId: detail.value.family.managementSeasonId,
        plannerUserId: detail.value.family.plannerUserId,
        provisionalNumber: detail.value.family.provisionalNumber ?? '',
        sampleApprovalDate: detail.value.family.sampleApprovalDate ?? '',
        retailPrice: detail.value.family.retailPrice,
        deliveryPrice: detail.value.family.deliveryPrice,
        planningCost: detail.value.family.planningCost,
        brandCost: detail.value.family.brandCost,
        royaltyTarget: detail.value.family.royaltyTarget,
        royaltyRate: detail.value.family.royaltyRate,
        // 旧 品番台帳 項目 追補 (PR1)
        remark: detail.value.family.remark ?? '',
        colorRemark: detail.value.family.colorRemark ?? '',
      }
      // アソート/セット明細 (PR3)。詳細応答の明細で編集行を初期化 (lineNo 昇順は API 側で保証)。
      // 既存応答互換のため null/undefined を `?? []` で吸収。
      editSetComponents.value = (detail.value.setComponents ?? []).map((c) => ({
        childItemNumber: c.childItemNumber,
        quantity: c.quantity,
      }))
    }
  } catch (e) {
    const err = e as { statusCode?: number }
    errorMessage.value = err.statusCode === 404
      ? '商品企画が見つかりません'
      : '商品詳細の取得に失敗しました'
  } finally {
    loading.value = false
  }
}

onMounted(async () => {
  await Promise.all([
    reload(),
    (async () => {
      const [br, fn, pg, mt, sup, sz, ps, usrRes] = await Promise.all([
        list('brands'), list('functions'), list('product-groups'),
        list('materials'), list('suppliers'),
        list('sizes'),
        list('product-seasons'),
        apiData<UserOption[]>('/users'),
      ])
      brands.value = br
      functions_.value = fn
      productGroups.value = pg
      materials.value = mt
      suppliers.value = sup
      sizes.value = sz
      productSeasons.value = ps
      users.value = usrRes
      if (sup.length) priceForm.value.supplierId = sup[0].id
    })(),
  ])
})

const onStartEdit = () => {
  editing.value = true
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
    await updateFamily(familyId.value, {
      brandId: editForm.value.brandId,
      functionId: editForm.value.functionId,
      productGroupId: editForm.value.productGroupId,
      upperMaterialId: editForm.value.upperMaterialId,
      insoleMaterialId: editForm.value.insoleMaterialId,
      outsoleMaterialId: editForm.value.outsoleMaterialId,
      productName1: editForm.value.productName1.trim(),
      productName2: editForm.value.productName2.trim() || null,
      status: Number(editForm.value.status),
      // 旧 品番台帳 項目 (Phase A、未入力は null)
      productYear: editForm.value.productYear,
      managementSeasonId: editForm.value.managementSeasonId,
      plannerUserId: editForm.value.plannerUserId,
      provisionalNumber: editForm.value.provisionalNumber.trim() || null,
      sampleApprovalDate: editForm.value.sampleApprovalDate || null,
      retailPrice: editForm.value.retailPrice,
      deliveryPrice: editForm.value.deliveryPrice,
      planningCost: editForm.value.planningCost,
      brandCost: editForm.value.brandCost,
      royaltyTarget: editForm.value.royaltyTarget,
      royaltyRate: editForm.value.royaltyRate,
      // 旧 品番台帳 項目 追補 (PR1、未入力は null)
      remark: editForm.value.remark.trim() || null,
      colorRemark: editForm.value.colorRemark.trim() || null,
      // アソート/セット明細 (PR3)。配列を送ると全置換。子品番が空の行は除外、trim + 数量 Number 化
      // (空入力時の NaN は 0 に丸めてサーバの SETC-002 で弾く)。空配列なら全削除 (= 通常商品化)。
      // 編集画面では常に配列を送るため明細は全置換される。
      setComponents: editSetComponents.value
        .filter((c) => c.childItemNumber.trim() !== '')
        .map((c) => ({ childItemNumber: c.childItemNumber.trim(), quantity: Number(c.quantity) || 0 })),
    })
    successMessage.value = '更新しました'
    editing.value = false
    await reload()
  } catch (e) {
    errorMessage.value = '更新に失敗しました'
  }
}

const onDelete = async () => {
  if (!detail.value) return
  if (!confirm(`${detail.value.family.productName1} を論理削除しますか？\n配下の SKU も連動で削除されます (復元は SQL で別途対応)`)) return
  try {
    await deleteFamily(familyId.value)
    await navigateTo('/products')
  } catch {
    errorMessage.value = '削除に失敗しました'
  }
}

const onAddPrice = async () => {
  if (priceForm.value.unitPrice <= 0) {
    errorMessage.value = '単価は 0 より大きい値を指定してください'
    return
  }
  try {
    await addSupplierPrice(familyId.value, {
      supplierId: priceForm.value.supplierId,
      unitPrice: Number(priceForm.value.unitPrice),
      currencyCode: priceForm.value.currencyCode,
      exchangeRate: priceForm.value.currencyCode === 'JPY' ? null : priceForm.value.exchangeRate,
      effectiveFrom: priceForm.value.effectiveFrom,
      decidedAt: priceForm.value.decidedAt,
      // 旧 仕入コスト計算明細 項目 (Phase C、未入力は null)
      estimateUnitPrice: priceForm.value.estimateUnitPrice,
      estimateReceivedDate: priceForm.value.estimateReceivedDate || null,
      estimateCost: priceForm.value.estimateCost,
      estimateMarginRate: priceForm.value.estimateMarginRate,
      purchaseCost: priceForm.value.purchaseCost,
      purchaseMarginRate: priceForm.value.purchaseMarginRate,
      lossCost: priceForm.value.lossCost,
      drayageCost: priceForm.value.drayageCost,
      taxRate: priceForm.value.taxRate,
      // サイズ別仕入単価 (PR2、未選択は null = 全サイズ共通の既定単価)
      sizeId: priceForm.value.sizeId,
    })
    successMessage.value = '単価を追加しました (旧単価の有効終了日も自動更新済)'
    showPriceForm.value = false
    // サイズ別仕入単価 (PR2): サイズ選択を全サイズ共通 (null) にリセット
    priceForm.value.sizeId = null
    // 原価明細フィールドをリセット (次の追加時に前回値が残らないように)
    priceForm.value.estimateUnitPrice = null
    priceForm.value.estimateReceivedDate = ''
    priceForm.value.estimateCost = null
    priceForm.value.estimateMarginRate = null
    priceForm.value.purchaseCost = null
    priceForm.value.purchaseMarginRate = null
    priceForm.value.lossCost = null
    priceForm.value.drayageCost = null
    priceForm.value.taxRate = null
    await reload()
  } catch (e) {
    errorMessage.value = getApiErrorMessage(e, '単価追加に失敗しました')
  }
}

const onFileSelect = async (event: Event) => {
  const target = event.target as HTMLInputElement
  if (!target.files?.length) return
  uploading.value = true
  errorMessage.value = ''
  try {
    for (const file of Array.from(target.files)) {
      await uploadImage(familyId.value, file, uploadCategory.value)
    }
    successMessage.value = '画像をアップロードしました'
    await reload()
  } catch (e) {
    errorMessage.value = getApiErrorMessage(e, '画像アップロードに失敗しました')
  } finally {
    uploading.value = false
    if (target) target.value = ''
  }
}

const onDeleteImage = async (img: ImageSummary) => {
  if (!confirm(`画像 (orderNo=${img.orderNo}) を削除しますか？`)) return
  try {
    await deleteImage(familyId.value, img.id)
    await reload()
  } catch {
    errorMessage.value = '画像削除に失敗しました'
  }
}

const formatBytes = (b: number) => `${(b / 1024).toFixed(1)} KB`
</script>

<template>
  <main class="mx-auto max-w-screen-2xl px-4 py-4">
    <div v-if="loading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500">
      読み込み中…
    </div>

    <div v-else-if="!detail" class="rounded border border-red-300 bg-red-50 p-4 text-red-700">
      {{ errorMessage || '商品が見つかりません' }}
      <div class="mt-2">
        <NuxtLink to="/products" class="text-blue-600 underline">商品一覧に戻る</NuxtLink>
      </div>
    </div>

    <template v-else>
      <header class="mb-4 flex items-start justify-between gap-4">
        <div>
          <div class="text-xs text-gray-500">
            <span class="font-mono">品番 {{ detail.family.itemNumber }}</span>
            <span class="mx-1 text-gray-400">·</span>
            <span class="font-mono">他品番 {{ detail.family.itemFamilyNumber }}</span>
          </div>
          <h1 class="text-2xl font-bold">{{ detail.family.productName1 }}</h1>
          <p v-if="detail.family.productName2" class="text-sm text-gray-500">{{ detail.family.productName2 }}</p>
        </div>
        <div class="flex gap-2">
          <button
            v-if="canEditMaster && !editing"
            type="button"
            class="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-sm hover:bg-gray-50"
            @click="onStartEdit"
          >編集</button>
          <button
            v-if="canEditMaster && !editing"
            type="button"
            class="rounded-md border border-red-300 bg-white px-3 py-1.5 text-sm text-red-600 hover:bg-red-50"
            @click="onDelete"
          >削除</button>
        </div>
      </header>

      <div v-if="errorMessage" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
        {{ errorMessage }}
      </div>
      <div v-if="successMessage" class="mb-3 rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-700">
        {{ successMessage }}
      </div>

      <!-- 次のアクション（操作導線）: この商品からの業務の次工程へ直接遷移する -->
      <section
        v-if="canEditMaster || canCreateOrder"
        class="mb-4 rounded-lg border border-gray-200 bg-white p-3 shadow-sm"
      >
        <h2 class="mb-3 text-sm font-semibold text-gray-700">次のアクション</h2>
        <div class="flex flex-wrap gap-2">
          <NuxtLink
            v-if="canEditMaster"
            :to="`/production/bom/${familyId}`"
            class="inline-flex items-center gap-1.5 rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-sm font-medium text-amber-800 no-underline transition hover:bg-amber-100"
          >
            <NavIcon name="layers" class="h-4 w-4" />
            素材構成(BOM)を編集
          </NuxtLink>
          <NuxtLink
            v-if="canCreateOrder"
            :to="`/production/instructions/new?familyId=${familyId}`"
            class="inline-flex items-center gap-1.5 rounded-md border border-blue-300 bg-blue-50 px-3 py-2 text-sm font-medium text-blue-800 no-underline transition hover:bg-blue-100"
          >
            <NavIcon name="file-text" class="h-4 w-4" />
            生産指示を作成
          </NuxtLink>
          <NuxtLink
            v-if="canCreateOrder"
            to="/orders/new"
            class="inline-flex items-center gap-1.5 rounded-md border border-indigo-300 bg-indigo-50 px-3 py-2 text-sm font-medium text-indigo-800 no-underline transition hover:bg-indigo-100"
          >
            <NavIcon name="clipboard" class="h-4 w-4" />
            発注書を作成
          </NuxtLink>
        </div>
      </section>

      <!-- 企画情報 -->
      <section class="mb-4 rounded-lg border border-gray-200 bg-white p-3 shadow-sm">
        <h2 class="mb-3 border-b border-gray-100 pb-2 font-semibold">企画情報</h2>

        <div v-if="!editing" class="grid grid-cols-2 gap-x-6 gap-y-2 text-sm sm:grid-cols-3 lg:grid-cols-4">
          <div><span class="text-gray-500">年式:</span> <strong>{{ detail.family.plannedYearCode }}</strong></div>
          <div><span class="text-gray-500">連番:</span> <strong class="font-mono">{{ detail.family.sequenceNo }}</strong></div>
          <div><span class="text-gray-500">商品タイプ:</span> {{ detail.family.productTypeName }}</div>
          <div><span class="text-gray-500">商品季節:</span> {{ detail.family.productSeasonName }}</div>
          <div><span class="text-gray-500">工場:</span> {{ detail.family.factorySupplierName }}</div>
          <div><span class="text-gray-500">状態:</span> {{ productStatusLabel(detail.family.status) }}</div>
          <div><span class="text-gray-500">ブランド:</span> {{ detail.family.brandName }}</div>
          <div><span class="text-gray-500">機能:</span> {{ detail.family.functionName ?? '—' }}</div>
          <div><span class="text-gray-500">商品群:</span> {{ detail.family.productGroupName }}</div>
          <div><span class="text-gray-500">甲皮素材:</span> {{ detail.family.upperMaterialName }}</div>
          <div><span class="text-gray-500">中底素材:</span> {{ detail.family.insoleMaterialName }}</div>
          <div><span class="text-gray-500">底素材:</span> {{ detail.family.outsoleMaterialName }}</div>
          <!-- 旧 品番台帳 項目 (Phase A、未設定は —) -->
          <div><span class="text-gray-500">商品年度:</span> {{ detail.family.productYear ?? '—' }}</div>
          <div><span class="text-gray-500">管理季節:</span> {{ detail.family.managementSeasonName ?? '—' }}</div>
          <div><span class="text-gray-500">企画者:</span> {{ detail.family.plannerName ?? '—' }}</div>
          <div><span class="text-gray-500">仮番号:</span> {{ detail.family.provisionalNumber ?? '—' }}</div>
          <div><span class="text-gray-500">サンプル合格日:</span> {{ detail.family.sampleApprovalDate ?? '—' }}</div>
          <div><span class="text-gray-500">小売価格:</span> {{ detail.family.retailPrice != null ? detail.family.retailPrice.toLocaleString() : '—' }}</div>
          <div><span class="text-gray-500">納品価格:</span> {{ detail.family.deliveryPrice != null ? detail.family.deliveryPrice.toLocaleString() : '—' }}</div>
          <div><span class="text-gray-500">企画費:</span> {{ detail.family.planningCost != null ? detail.family.planningCost.toLocaleString() : '—' }}</div>
          <div><span class="text-gray-500">ブランド費:</span> {{ detail.family.brandCost != null ? detail.family.brandCost.toLocaleString() : '—' }}</div>
          <div><span class="text-gray-500">版権対象:</span> {{ royaltyTargetLabel(detail.family.royaltyTarget) }}</div>
          <div><span class="text-gray-500">版権料率:</span> {{ detail.family.royaltyRate != null ? `${detail.family.royaltyRate}%` : '—' }}</div>
          <!-- 備考 / 備考（色）(旧 品番台帳 項目 追補 PR1、未設定は —)。長文のため横幅をまたぐ。 -->
          <div class="col-span-2 sm:col-span-3 lg:col-span-4"><span class="text-gray-500">備考:</span> <span class="whitespace-pre-wrap">{{ detail.family.remark ?? '—' }}</span></div>
          <div class="col-span-2 sm:col-span-3 lg:col-span-4"><span class="text-gray-500">備考（色）:</span> <span class="whitespace-pre-wrap">{{ detail.family.colorRemark ?? '—' }}</span></div>
        </div>

        <!-- 登録/更新 情報 (旧 spec No.27/28、PR1)。編集不可の表示のみ。 -->
        <div v-if="!editing" class="mt-3 grid grid-cols-2 gap-x-6 gap-y-2 border-t border-gray-100 pt-3 text-sm sm:grid-cols-4">
          <div><span class="text-gray-500">登録日:</span> {{ formatJstDateTime(detail.family.createdAt) }}</div>
          <div><span class="text-gray-500">登録者:</span> {{ detail.family.createdByUserName ?? '—' }}</div>
          <div><span class="text-gray-500">最終更新日:</span> {{ formatJstDateTime(detail.family.updatedAt) }}</div>
          <div><span class="text-gray-500">最終更新者:</span> {{ detail.family.updatedByUserName ?? '—' }}</div>
        </div>

        <form v-else class="grid grid-cols-1 gap-x-3 gap-y-2 text-sm sm:grid-cols-2 lg:grid-cols-3" @submit.prevent="onSaveEdit">
          <label class="flex flex-col gap-1">
            <span class="font-medium">ブランド</span>
            <MasterSelect :model-value="editForm.brandId" :items="brands" placeholder="ブランドを検索…" @update:model-value="(v) => editForm.brandId = v ?? ''" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">機能</span>
            <MasterSelect :model-value="editForm.functionId" :items="functions_" allow-empty empty-label="(なし)" placeholder="機能を検索…" @update:model-value="(v) => editForm.functionId = v" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">商品群</span>
            <MasterSelect :model-value="editForm.productGroupId" :items="productGroups" placeholder="商品群を検索…" @update:model-value="(v) => editForm.productGroupId = v ?? ''" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">状態</span>
            <AutoComplete :model-value="String(editForm.status)" :options="[{ value: '0', label: 'Draft' }, { value: '1', label: 'Active' }, { value: '2', label: 'Discontinued' }]" :allow-empty="false" placeholder="状態を選択…" @update:model-value="(v) => editForm.status = Number(v)" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">甲皮素材</span>
            <MasterSelect :model-value="editForm.upperMaterialId" :items="materials" placeholder="甲皮素材を検索…" @update:model-value="(v) => editForm.upperMaterialId = v ?? ''" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">中底素材</span>
            <MasterSelect :model-value="editForm.insoleMaterialId" :items="materials" placeholder="中底素材を検索…" @update:model-value="(v) => editForm.insoleMaterialId = v ?? ''" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">底素材</span>
            <MasterSelect :model-value="editForm.outsoleMaterialId" :items="materials" placeholder="底素材を検索…" @update:model-value="(v) => editForm.outsoleMaterialId = v ?? ''" />
          </label>

          <!-- 旧 品番台帳 項目 (Phase A、全て任意) -->
          <label class="flex flex-col gap-1">
            <span class="font-medium">商品年度</span>
            <input v-model.number="editForm.productYear" type="number" min="0" max="9999" step="1" placeholder="例: 2026 (9999=通年)" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">管理季節</span>
            <MasterSelect :model-value="editForm.managementSeasonId" :items="productSeasons" allow-empty empty-label="(なし)" placeholder="管理季節を検索…" @update:model-value="(v) => editForm.managementSeasonId = v" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">企画者</span>
            <MasterSelect :model-value="editForm.plannerUserId" :items="userOptions" allow-empty empty-label="(なし)" placeholder="企画者を検索…" @update:model-value="(v) => editForm.plannerUserId = v" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">仮番号</span>
            <input v-model="editForm.provisionalNumber" type="text" maxlength="64" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">サンプル合格日</span>
            <input v-model="editForm.sampleApprovalDate" type="date" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">版権対象</span>
            <AutoComplete :model-value="editForm.royaltyTarget != null ? String(editForm.royaltyTarget) : ''" :options="royaltyTargetOptions" allow-empty empty-label="(なし)" placeholder="版権対象を選択…" @update:model-value="(v) => editForm.royaltyTarget = v === '' ? null : Number(v)" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">版権料率 (%)</span>
            <input v-model.number="editForm.royaltyRate" type="number" min="0" step="0.01" placeholder="例: 5.00" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">小売価格</span>
            <input v-model.number="editForm.retailPrice" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">納品価格</span>
            <input v-model.number="editForm.deliveryPrice" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">企画費</span>
            <input v-model.number="editForm.planningCost" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">ブランド費</span>
            <input v-model.number="editForm.brandCost" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
          </label>

          <label class="flex flex-col gap-1 sm:col-span-2 lg:col-span-3">
            <span class="font-medium">商品名 1</span>
            <input v-model="editForm.productName1" type="text" maxlength="255" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
          </label>
          <label class="flex flex-col gap-1 sm:col-span-2 lg:col-span-3">
            <span class="font-medium">商品名 2</span>
            <input v-model="editForm.productName2" type="text" maxlength="255" class="rounded-md border border-gray-300 px-2.5 py-1.5" />
          </label>
          <!-- 備考 / 備考（色）(旧 品番台帳 項目 追補 PR1、任意) -->
          <label class="flex flex-col gap-1 sm:col-span-2 lg:col-span-3">
            <span class="font-medium">備考</span>
            <textarea v-model="editForm.remark" rows="2" maxlength="2000" placeholder="商品本体に関する備考 (任意)" class="rounded-md border border-gray-300 px-2.5 py-1.5"></textarea>
          </label>
          <label class="flex flex-col gap-1 sm:col-span-2 lg:col-span-3">
            <span class="font-medium">備考（色）</span>
            <textarea v-model="editForm.colorRemark" rows="2" maxlength="2000" placeholder="色に関する備考 (商品全体で 1 つ、任意)" class="rounded-md border border-gray-300 px-2.5 py-1.5"></textarea>
          </label>
          <div class="flex justify-end gap-2 pt-2 sm:col-span-2 lg:col-span-3">
            <button type="button" class="rounded-md border border-gray-300 bg-white px-4 py-1.5 hover:bg-gray-50" @click="onCancelEdit">キャンセル</button>
            <button type="submit" class="rounded-md bg-blue-600 px-4 py-1.5 text-white hover:bg-blue-700">保存</button>
          </div>
        </form>
      </section>

      <!-- SKU 一覧 -->
      <section class="mb-4 overflow-x-auto rounded-lg border border-gray-200 bg-white p-3 shadow-sm">
        <h2 class="mb-3 border-b border-gray-100 pb-2 font-semibold">SKU 一覧 ({{ detail.products.length }} 件)</h2>
        <table class="w-full text-sm">
          <thead class="border-b border-gray-200 bg-gray-50">
            <tr>
              <th class="px-2 py-1.5 text-left">SKU (11 桁)</th>
              <th class="px-2 py-1.5 text-left">色</th>
              <th class="px-2 py-1.5 text-left">サイズ</th>
              <th class="px-2 py-1.5 text-left">状態</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="p in detail.products" :key="p.id" class="border-b border-gray-100 last:border-0"
                :class="{ 'text-gray-400 bg-gray-50': p.isDeleted }">
              <td class="px-2 py-1.5 font-mono">{{ p.sku }}</td>
              <td class="px-2 py-1.5">{{ p.colorCode }} {{ p.colorName }}</td>
              <td class="px-2 py-1.5">{{ p.sizeCode }} {{ p.sizeName }}</td>
              <td class="px-2 py-1.5">{{ p.isDeleted ? '削除済' : '有効' }}</td>
            </tr>
          </tbody>
        </table>
      </section>

      <!-- アソート/セット明細 (PR3、旧 spec No.37/38) -->
      <section class="mb-4 overflow-x-auto rounded-lg border border-gray-200 bg-white p-3 shadow-sm">
        <h2 class="mb-3 border-b border-gray-100 pb-2 font-semibold">
          アソート/セット明細
          <span class="ml-2 text-xs font-normal text-gray-500">(子品番 + 数量、アソート/セット品のみ)</span>
        </h2>

        <!-- 表示モード (編集していないとき) -->
        <template v-if="!editing">
          <div v-if="(detail.setComponents?.length ?? 0) === 0" class="py-4 text-center text-sm text-gray-500">
            アソート/セット明細はありません (通常商品)
          </div>
          <table v-else class="w-full text-sm">
            <thead class="border-b border-gray-200 bg-gray-50">
              <tr>
                <th class="px-2 py-1.5 text-left">No.</th>
                <th class="px-2 py-1.5 text-left">子品番</th>
                <th class="px-2 py-1.5 text-right">数量</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="c in detail.setComponents ?? []" :key="c.id" class="border-b border-gray-100 last:border-0">
                <td class="px-2 py-1.5 text-gray-500">{{ c.lineNo }}</td>
                <td class="px-2 py-1.5 font-mono">{{ c.childItemNumber }}</td>
                <td class="px-2 py-1.5 text-right font-mono">{{ c.quantity.toLocaleString() }}</td>
              </tr>
            </tbody>
          </table>
        </template>

        <!-- 編集モード: 行追加/削除 (色サイズ/単価明細と同じパターン)。編集は企画情報の「編集」で開始。 -->
        <template v-else>
          <div class="mb-3 flex items-center justify-between">
            <p class="text-xs text-gray-500">
              アソート/セット品の構成 (子品番は手入力)。保存すると明細は全置換されます。通常商品は空にしてください。
            </p>
            <button
              type="button"
              class="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50"
              @click="addSetComponent"
            >+ 行を追加</button>
          </div>

          <div v-if="editSetComponents.length === 0" class="py-4 text-center text-sm text-gray-400">
            明細なし (通常商品)。アソート/セット品の場合は「+ 行を追加」してください。
          </div>

          <div v-else class="space-y-2">
            <div
              v-for="(c, idx) in editSetComponents"
              :key="idx"
              class="grid grid-cols-1 gap-2 rounded-md border border-gray-200 bg-gray-50 p-3 sm:grid-cols-[1fr_8rem_auto] sm:items-end sm:gap-3"
            >
              <label class="flex flex-col gap-1">
                <span class="text-sm font-medium">子品番 <span class="text-red-500">*</span></span>
                <input
                  v-model="c.childItemNumber"
                  type="text"
                  maxlength="32"
                  placeholder="例: NA1001A (手入力)"
                  class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm"
                />
              </label>
              <label class="flex flex-col gap-1">
                <span class="text-sm font-medium">数量 <span class="text-red-500">*</span></span>
                <input
                  v-model.number="c.quantity"
                  type="number"
                  min="1"
                  step="1"
                  class="rounded-md border border-gray-300 px-2.5 py-1.5 text-sm"
                />
              </label>
              <button
                type="button"
                class="h-fit rounded-md border border-red-300 bg-white px-3 py-1.5 text-sm text-red-600 hover:bg-red-50"
                @click="removeSetComponent(idx)"
              >削除</button>
            </div>
          </div>
        </template>
      </section>

      <!-- 仕入単価 -->
      <section class="mb-4 overflow-x-auto rounded-lg border border-gray-200 bg-white p-3 shadow-sm">
        <div class="mb-3 flex items-center justify-between border-b border-gray-100 pb-2">
          <h2 class="font-semibold">現在有効な仕入単価 ({{ detail.currentSupplierPrices.length }} 件)</h2>
          <button
            v-if="canEditMaster"
            type="button"
            class="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50"
            @click="showPriceForm = !showPriceForm"
          >
            {{ showPriceForm ? '閉じる' : '+ 新単価追加' }}
          </button>
        </div>

        <form v-if="showPriceForm" class="mb-4 grid grid-cols-4 gap-3 rounded-md bg-gray-50 p-4 text-sm" @submit.prevent="onAddPrice">
          <label class="flex flex-col gap-1">
            <span class="font-medium">仕入先</span>
            <MasterSelect :model-value="priceForm.supplierId" :items="suppliers" placeholder="仕入先を検索…" @update:model-value="(v) => priceForm.supplierId = v ?? ''" />
          </label>
          <!-- サイズ別仕入単価 (PR2)。未選択 = 全サイズ共通の既定単価、個別サイズ = そのサイズ専用単価。 -->
          <label class="flex flex-col gap-1">
            <span class="font-medium">サイズ</span>
            <MasterSelect :model-value="priceForm.sizeId" :items="sizes" allow-empty empty-label="全サイズ共通" placeholder="サイズを検索…" @update:model-value="(v) => priceForm.sizeId = v" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">単価</span>
            <div class="flex gap-1">
              <input v-model.number="priceForm.unitPrice" type="number" step="0.01" min="0.01"
                     class="flex-1 rounded-md border border-gray-300 px-2 py-1.5" />
              <div class="w-20">
                <AutoComplete :model-value="priceForm.currencyCode" :options="[{ value: 'JPY', label: 'JPY' }, { value: 'USD', label: 'USD' }, { value: 'CNY', label: 'CNY' }]" :allow-empty="false" @update:model-value="(v) => priceForm.currencyCode = v" />
              </div>
            </div>
          </label>
          <label v-if="priceForm.currencyCode !== 'JPY'" class="flex flex-col gap-1">
            <span class="font-medium">為替レート <span class="text-xs font-normal text-gray-400">(円換算)</span></span>
            <input v-model.number="priceForm.exchangeRate" type="number" min="0" step="0.0001" placeholder="例: 21.5" class="rounded-md border border-gray-300 px-2 py-1.5" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">有効開始</span>
            <input v-model="priceForm.effectiveFrom" type="date" class="rounded-md border border-gray-300 px-2 py-1.5" />
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">決定日</span>
            <input v-model="priceForm.decidedAt" type="date" class="rounded-md border border-gray-300 px-2 py-1.5" />
          </label>

          <!-- 旧 仕入コスト計算明細 項目 (Phase C、全て任意) -->
          <fieldset class="col-span-4 rounded-md border border-gray-200 bg-white p-3">
            <legend class="px-1 text-xs font-semibold text-gray-600">原価明細 (任意)</legend>
            <div class="grid grid-cols-1 gap-3 sm:grid-cols-2 md:grid-cols-3">
              <label class="flex flex-col gap-1">
                <span class="font-medium">見積単価</span>
                <input v-model.number="priceForm.estimateUnitPrice" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2 py-1.5" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="font-medium">見積単価受領日</span>
                <input v-model="priceForm.estimateReceivedDate" type="date" class="rounded-md border border-gray-300 px-2 py-1.5" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="font-medium">見積原価</span>
                <input v-model.number="priceForm.estimateCost" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2 py-1.5" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="font-medium">見積利益率 (%)</span>
                <input v-model.number="priceForm.estimateMarginRate" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2 py-1.5" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="font-medium">仕入原価</span>
                <input v-model.number="priceForm.purchaseCost" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2 py-1.5" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="font-medium">仕入利益率 (%)</span>
                <input v-model.number="priceForm.purchaseMarginRate" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2 py-1.5" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="font-medium">ロス費</span>
                <input v-model.number="priceForm.lossCost" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2 py-1.5" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="font-medium">ドレー代</span>
                <input v-model.number="priceForm.drayageCost" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2 py-1.5" />
              </label>
              <label class="flex flex-col gap-1">
                <span class="font-medium">税率 (%)</span>
                <input v-model.number="priceForm.taxRate" type="number" min="0" step="0.01" class="rounded-md border border-gray-300 px-2 py-1.5" />
              </label>
            </div>
          </fieldset>

          <div class="col-span-4 flex justify-end">
            <button type="submit" class="rounded-md bg-blue-600 px-4 py-1.5 text-white hover:bg-blue-700">追加</button>
          </div>
        </form>

        <table class="w-full text-sm">
          <thead class="border-b border-gray-200 bg-gray-50">
            <tr>
              <th class="px-2 py-1.5 text-left">仕入先</th>
              <th class="px-2 py-1.5 text-left">サイズ</th>
              <th class="px-2 py-1.5 text-right">単価</th>
              <th class="px-2 py-1.5 text-right">為替レート</th>
              <th class="px-2 py-1.5 text-right">見積単価</th>
              <th class="px-2 py-1.5 text-right">仕入原価</th>
              <th class="px-2 py-1.5 text-right">税率</th>
              <th class="px-2 py-1.5 text-left">有効開始</th>
              <th class="px-2 py-1.5 text-left">決定日</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="p in detail.currentSupplierPrices" :key="p.id" class="border-b border-gray-100 last:border-0">
              <td class="px-2 py-1.5">{{ p.supplierCode }} {{ p.supplierName }}</td>
              <!-- サイズ別仕入単価 (PR2)。sizeId=null は全サイズ共通の既定単価。 -->
              <td class="px-2 py-1.5">
                <span v-if="p.sizeName">{{ p.sizeName }}</span>
                <span v-else class="text-gray-500">全サイズ共通</span>
              </td>
              <td class="px-2 py-1.5 text-right font-mono">{{ p.currencyCode }} {{ p.unitPrice.toLocaleString() }}</td>
              <td class="px-2 py-1.5 text-right font-mono text-gray-500">{{ p.exchangeRate != null ? p.exchangeRate.toLocaleString() : '—' }}</td>
              <td class="px-2 py-1.5 text-right font-mono text-gray-500">{{ p.estimateUnitPrice != null ? p.estimateUnitPrice.toLocaleString() : '—' }}</td>
              <td class="px-2 py-1.5 text-right font-mono text-gray-500">{{ p.purchaseCost != null ? p.purchaseCost.toLocaleString() : '—' }}</td>
              <td class="px-2 py-1.5 text-right font-mono text-gray-500">{{ p.taxRate != null ? `${p.taxRate}%` : '—' }}</td>
              <td class="px-2 py-1.5">{{ p.effectiveFrom }}</td>
              <td class="px-2 py-1.5">{{ p.decidedAt }}</td>
            </tr>
            <tr v-if="detail.currentSupplierPrices.length === 0">
              <td colspan="9" class="px-2 py-4 text-center text-gray-500">単価が登録されていません</td>
            </tr>
          </tbody>
        </table>
      </section>

      <!-- 画像管理 (§2a: 企画画像 / 本番画像 の区分ごとに表示・追加) -->
      <section class="rounded-lg border border-gray-200 bg-white p-3 shadow-sm">
        <div class="mb-3 flex flex-col gap-2 border-b border-gray-100 pb-2 sm:flex-row sm:items-center sm:justify-between">
          <h2 class="font-semibold">画像 <span class="text-xs font-normal text-gray-500">(区分ごと最大 5 枚)</span></h2>
          <div v-if="canEditMaster" class="flex items-center gap-2">
            <!-- 追加区分の選択 -->
            <div class="inline-flex overflow-hidden rounded-md border border-gray-300 text-xs">
              <button
                v-for="cat in IMAGE_CATEGORIES"
                :key="cat.key"
                type="button"
                class="px-3 py-1 font-medium transition-colors"
                :class="[
                  cat.key > 0 ? 'border-l border-gray-300' : '',
                  uploadCategory === cat.key ? 'bg-blue-600 text-white' : 'bg-white text-gray-600 hover:bg-gray-50',
                ]"
                @click="uploadCategory = cat.key"
              >{{ cat.label }}</button>
            </div>
            <input
              ref="fileInput"
              type="file"
              accept="image/jpeg,image/png,image/webp"
              multiple
              class="hidden"
              @change="onFileSelect"
            />
            <button
              type="button"
              :disabled="uploading || imagesByCategory(uploadCategory).length >= 5"
              class="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50 disabled:opacity-50"
              @click="fileInput?.click()"
            >
              {{ uploading ? 'アップロード中…' : `+ ${uploadCategory === 1 ? '本番' : '企画'}画像を追加` }}
            </button>
          </div>
        </div>

        <div v-if="detail.images.length === 0" class="py-8 text-center text-sm text-gray-500">
          画像が登録されていません (JPEG / PNG / WebP、最大 5MB、区分ごと 5 枚まで)
        </div>

        <!-- 区分ごとのグリッド -->
        <div v-else class="space-y-3">
          <div v-for="cat in IMAGE_CATEGORIES" :key="cat.key">
            <div class="mb-1.5 text-sm font-medium text-gray-700">
              {{ cat.label }} <span class="text-xs font-normal text-gray-400">({{ imagesByCategory(cat.key).length }} 枚)</span>
            </div>
            <div v-if="imagesByCategory(cat.key).length === 0" class="py-3 text-center text-xs text-gray-400">
              {{ cat.label }}はありません
            </div>
            <div v-else class="grid grid-cols-2 gap-4 sm:grid-cols-3 md:grid-cols-5">
              <div
                v-for="img in imagesByCategory(cat.key)"
                :key="img.id"
                class="group relative overflow-hidden rounded-md border border-gray-200"
              >
                <img v-if="img.url" :src="img.url" :alt="img.originalFilename ?? `image-${img.orderNo}`"
                     class="aspect-square w-full object-cover" />
                <div v-else
                     class="flex aspect-square w-full items-center justify-center bg-gray-100 text-xs text-gray-400">
                  画像読込失敗
                </div>
                <div class="absolute right-1 top-1 rounded-full bg-black/70 px-2 py-0.5 text-xs text-white">
                  #{{ img.orderNo }}
                </div>
                <div class="border-t border-gray-100 bg-gray-50 px-2 py-1 text-xs">
                  <div class="truncate" :title="img.originalFilename ?? ''">
                    {{ img.originalFilename ?? '—' }}
                  </div>
                  <div class="flex justify-between text-gray-500">
                    <span>{{ formatBytes(img.fileSizeBytes) }}</span>
                    <button
                      v-if="canEditMaster"
                      type="button"
                      class="text-red-600 hover:underline"
                      @click="onDeleteImage(img)"
                    >削除</button>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <p class="mt-3 text-xs text-gray-400">
        API: GET/PATCH/DELETE /api/maker/v1/products/families/{{ familyId }} + 画像 + 単価
      </p>
    </template>
  </main>
</template>
