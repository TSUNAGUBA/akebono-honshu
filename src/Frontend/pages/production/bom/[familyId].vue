<script setup lang="ts">
import type { BomLineInput } from '~/composables/useProduction'
import { materialRoleLabel } from '~/composables/useProduction'
import type { MasterItem } from '~/composables/useMasters'
import type { FamilyDetail } from '~/composables/useProducts'

const route = useRoute()
// 第二段階契約: family id は uuid 文字列。数値変換せず文字列のまま使う。
const familyId = computed(() => String(route.params.familyId))
const { getBom, replaceBom } = useProduction()
const { getFamily } = useProducts()
const { list } = useMasters()
const { user } = useAuth()
const canEdit = computed(() => (user.value?.productLedgerPermission ?? 0) >= 1)

const ROLES = [0, 1, 2, 3, 4]
const UNITS = ['組', '足', '枚', '個', 'm', '㎡', 'cm', '本']

interface Row extends BomLineInput { _key: number }
let keySeq = 1
const rows = ref<Row[]>([])
const family = ref<FamilyDetail | null>(null)
const materials = ref<MasterItem[]>([])
const suppliers = ref<MasterItem[]>([])
const loading = ref(true)
const saving = ref(false)
const errorMessage = ref('')
const successMessage = ref('')

const reload = async () => {
  loading.value = true; errorMessage.value = ''
  try {
    family.value = await getFamily(familyId.value)
    materials.value = await list('materials')
    suppliers.value = await list('suppliers')
    const existing = await getBom(familyId.value)
    if (existing.length > 0) {
      rows.value = existing.map(e => ({
        _key: keySeq++, materialRole: e.materialRole, materialId: e.materialId,
        requiredQtyPerUnit: e.requiredQtyPerUnit, unit: e.unit,
        recommendedSupplierId: e.recommendedSupplierId, lossRate: e.lossRate, remark: e.remark,
      }))
    } else if (family.value) {
      // BOM 未登録: 既存3FK代表素材を初期シード (保存するまで product_materials には未反映)
      const f = family.value.family
      rows.value = [
        { _key: keySeq++, materialRole: 0, materialId: f.upperMaterialId, requiredQtyPerUnit: 1, unit: '組', recommendedSupplierId: null, lossRate: 0, remark: null },
        { _key: keySeq++, materialRole: 1, materialId: f.insoleMaterialId, requiredQtyPerUnit: 1, unit: '組', recommendedSupplierId: null, lossRate: 0, remark: null },
        { _key: keySeq++, materialRole: 2, materialId: f.outsoleMaterialId, requiredQtyPerUnit: 1, unit: '組', recommendedSupplierId: null, lossRate: 0, remark: null },
      ]
    }
  } catch { errorMessage.value = 'BOM 情報の取得に失敗しました' }
  finally { loading.value = false }
}
onMounted(reload)

const addRow = () => rows.value.push({ _key: keySeq++, materialRole: 3, materialId: materials.value[0]?.id ?? '', requiredQtyPerUnit: 1, unit: '個', recommendedSupplierId: null, lossRate: 0, remark: null })
const removeRow = (k: number) => { rows.value = rows.value.filter(r => r._key !== k) }

const save = async () => {
  errorMessage.value = ''; successMessage.value = ''
  if (rows.value.some(r => !r.materialId || r.requiredQtyPerUnit <= 0)) { errorMessage.value = '素材と所要量(>0)を入力してください'; return }
  saving.value = true
  try {
    await replaceBom(familyId.value, rows.value.map(r => ({
      materialRole: r.materialRole, materialId: r.materialId, requiredQtyPerUnit: r.requiredQtyPerUnit,
      unit: r.unit, recommendedSupplierId: r.recommendedSupplierId, lossRate: r.lossRate, remark: r.remark,
    })))
    successMessage.value = 'BOM を保存しました'
    await reload()
  } catch (e) {
    errorMessage.value = getApiErrorMessage(e, 'BOM の保存に失敗しました')
  } finally { saving.value = false }
}
</script>

<template>
  <main class="mx-auto max-w-screen-2xl px-4 py-4">
    <header class="mb-4 flex items-start justify-between gap-4">
      <div>
        <h1 class="text-2xl font-bold">素材構成 (BOM)</h1>
        <p class="mt-1 text-sm text-gray-500">B-01 1足あたりの素材所要量を登録します。{{ family ? `品番: ${family.family.productName1}` : '' }}</p>
      </div>
      <NuxtLink :to="`/products/${familyId}`" class="text-sm text-blue-600 hover:underline">← 商品詳細へ</NuxtLink>
    </header>

    <div v-if="errorMessage" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">{{ errorMessage }}</div>
    <div v-if="successMessage" class="mb-3 rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-700">{{ successMessage }}</div>
    <section v-if="loading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500 shadow-sm">読み込み中…</section>

    <template v-else>
      <section class="mb-4 overflow-x-auto rounded-lg border border-gray-200 bg-white shadow-sm">
        <table class="w-full min-w-[820px]">
          <thead class="border-b border-gray-200 bg-gray-50">
            <tr>
              <th class="px-2 py-1.5 text-left text-xs font-semibold uppercase text-gray-600">部位</th>
              <th class="px-2 py-1.5 text-left text-xs font-semibold uppercase text-gray-600">素材</th>
              <th class="px-2 py-1.5 text-right text-xs font-semibold uppercase text-gray-600">所要量/足</th>
              <th class="px-2 py-1.5 text-left text-xs font-semibold uppercase text-gray-600">単位</th>
              <th class="px-2 py-1.5 text-left text-xs font-semibold uppercase text-gray-600">推奨仕入先</th>
              <th class="px-2 py-1.5 text-right text-xs font-semibold uppercase text-gray-600">ロス率</th>
              <th class="px-2 py-1.5 text-left text-xs font-semibold uppercase text-gray-600">備考</th>
              <th class="px-2 py-1.5"></th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="rows.length === 0"><td colspan="8" class="px-2 py-6 text-center text-sm text-gray-500">行を追加してください</td></tr>
            <tr v-for="r in rows" :key="r._key" class="border-b border-gray-100 last:border-0">
              <td class="px-2 py-1.5"><div class="w-28"><AutoComplete :model-value="String(r.materialRole)" :options="ROLES.map((ro) => ({ value: String(ro), label: materialRoleLabel(ro) }))" :allow-empty="false" :disabled="!canEdit" @update:model-value="(v) => r.materialRole = Number(v)" /></div></td>
              <td class="px-2 py-1.5"><div class="w-44 lg:w-56"><MasterSelect :model-value="r.materialId" :items="materials" :disabled="!canEdit" placeholder="素材を検索…" @update:model-value="(v) => r.materialId = v ?? ''" /></div></td>
              <td class="px-2 py-1.5 text-right"><input v-model.number="r.requiredQtyPerUnit" :disabled="!canEdit" type="number" min="0" step="0.0001" class="w-24 rounded-md border border-gray-300 px-2 py-1 text-right text-sm" /></td>
              <td class="px-2 py-1.5"><div class="w-20"><AutoComplete :model-value="r.unit" :options="UNITS.map((u) => ({ value: u, label: u }))" :allow-empty="false" :disabled="!canEdit" @update:model-value="(v) => r.unit = v" /></div></td>
              <td class="px-2 py-1.5"><div class="w-40 lg:w-52"><MasterSelect :model-value="r.recommendedSupplierId" :items="suppliers" :disabled="!canEdit" allow-empty empty-label="(未指定)" placeholder="仕入先を検索…" @update:model-value="(v) => r.recommendedSupplierId = v" /></div></td>
              <td class="px-2 py-1.5 text-right"><input v-model.number="r.lossRate" :disabled="!canEdit" type="number" min="0" max="0.99" step="0.01" class="w-20 rounded-md border border-gray-300 px-2 py-1 text-right text-sm" /></td>
              <td class="px-2 py-1.5"><input v-model="r.remark" :disabled="!canEdit" type="text" class="w-32 rounded-md border border-gray-300 px-2 py-1 text-sm lg:w-48" /></td>
              <td class="px-2 py-1.5 text-right"><button v-if="canEdit" type="button" class="text-xs text-red-600 hover:underline" @click="removeRow(r._key)">削除</button></td>
            </tr>
          </tbody>
        </table>
      </section>

      <div v-if="canEdit" class="flex gap-3">
        <button type="button" class="rounded-md border border-gray-300 px-4 py-2 text-sm hover:bg-gray-50" @click="addRow">+ 行を追加</button>
        <button type="button" :disabled="saving" class="rounded-md bg-blue-600 px-4 py-2 text-sm text-white hover:bg-blue-700 disabled:opacity-50" @click="save">{{ saving ? '保存中…' : 'BOM を保存' }}</button>
      </div>
      <p v-else class="text-sm text-gray-400">参照のみ (品番台帳管理権限なし)</p>
      <p class="mt-2 text-xs text-gray-400">ロス率は 0〜0.99（例 0.05 = 5%）。素材発注数 = 所要量 × 生産数量 × (1 + ロス率)。代表素材(甲皮/中底/底)は表示用で、BOMが所要量の正です。</p>
    </template>
  </main>
</template>
