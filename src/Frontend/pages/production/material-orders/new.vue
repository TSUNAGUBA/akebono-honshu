<script setup lang="ts">
import type { MaterialRequirements } from '~/composables/useProduction'
import { materialRoleLabel } from '~/composables/useProduction'
import type { MasterItem } from '~/composables/useMasters'

const route = useRoute()
const piId = computed(() => route.query.piId ? Number(route.query.piId) : null)
const familyId = computed(() => route.query.familyId ? Number(route.query.familyId) : null)
const quantity = computed(() => route.query.quantity ? Number(route.query.quantity) : null)

const { prepareMaterialOrder, moCreate } = useProduction()
const { list } = useMasters()

interface EditLine { materialId: number; materialName: string; requiredQuantity: number; unit: string; unitPrice: number | null }
interface EditGroup { supplierId: number; supplierName: string | null; dueDate: string; currency: string; lines: EditLine[] }

const req = ref<MaterialRequirements | null>(null)
const groups = ref<EditGroup[]>([])
const suppliers = ref<MasterItem[]>([])
const loading = ref(true)
const errorMessage = ref('')
const bomMissing = ref(false)
const submittingIdx = ref<number | null>(null)

const defaultDue = new Date(Date.now() + 30 * 86400000).toISOString().split('T')[0]

const reload = async () => {
  loading.value = true; errorMessage.value = ''; bomMissing.value = false
  try {
    suppliers.value = await list('suppliers')
    req.value = await prepareMaterialOrder({
      productionInstructionId: piId.value,
      productFamilyId: familyId.value,
      quantity: quantity.value,
    })
    groups.value = req.value.groups.map(g => ({
      supplierId: g.recommendedSupplierId ?? 0,
      supplierName: g.recommendedSupplierName,
      dueDate: defaultDue,
      currency: 'JPY',
      lines: g.lines.map(l => ({ materialId: l.materialId, materialName: l.materialName, requiredQuantity: l.requiredQuantity, unit: l.unit, unitPrice: null as number | null })),
    }))
  } catch (e) {
    const err = e as { statusCode?: number; data?: { detail?: string } }
    if (err.data?.detail?.includes('MORD-001')) { bomMissing.value = true }
    else errorMessage.value = err.data?.detail ?? '所要量展開に失敗しました'
  } finally { loading.value = false }
}
onMounted(reload)

const createGroup = async (gi: number) => {
  errorMessage.value = ''
  const g = groups.value[gi]
  if (!g.supplierId) { errorMessage.value = '素材仕入先を選択してください'; return }
  submittingIdx.value = gi
  try {
    const res = await moCreate({
      materialSupplierId: g.supplierId,
      productionInstructionId: piId.value,
      dueDate: g.dueDate,
      communicationText: null,
      lines: g.lines.map(l => ({
        materialId: l.materialId,
        productFamilyId: req.value!.familyId,
        sourcePiLineId: null,
        requiredQuantity: l.requiredQuantity,
        unit: l.unit,
        unitPrice: l.unitPrice,
        currencyCode: g.currency,
      })),
    })
    await navigateTo(`/production/material-orders/${res.id}`)
  } catch (e) {
    const err = e as { data?: { detail?: string } }
    errorMessage.value = err.data?.detail ?? '素材発注書の作成に失敗しました'
  } finally { submittingIdx.value = null }
}
</script>

<template>
  <main class="mx-auto max-w-5xl px-4 py-8">
    <header class="mb-6">
      <h1 class="text-2xl font-bold">素材発注書 新規作成</h1>
      <p class="mt-1 text-sm text-gray-500">MO-01 BOM×生産数量で所要量を展開し、素材仕入先ごとに発注書を作成します。</p>
    </header>

    <div v-if="errorMessage" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">{{ errorMessage }}</div>

    <section v-if="bomMissing" class="rounded-lg border border-amber-300 bg-amber-50 p-6 text-sm text-amber-800">
      素材構成（BOM）が未登録のため所要量を展開できません。
      <NuxtLink v-if="familyId || req" :to="`/production/bom/${familyId ?? req?.familyId}`" class="font-semibold text-amber-900 underline">BOM を登録する</NuxtLink>
    </section>

    <section v-else-if="loading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500 shadow-sm">読み込み中…</section>

    <template v-else-if="req">
      <p class="mb-4 text-sm text-gray-600">生産数量: <strong>{{ req.totalQuantity.toLocaleString() }}</strong> / 推奨仕入先ごとに {{ groups.length }} 件の発注書候補</p>
      <section v-for="(g, gi) in groups" :key="gi" class="mb-5 rounded-lg border border-gray-200 bg-white p-4 shadow-sm">
        <div class="mb-3 grid grid-cols-1 gap-3 sm:grid-cols-3">
          <label class="block text-sm">
            <span class="text-gray-600">素材仕入先</span>
            <div class="mt-1">
              <MasterSelect :model-value="g.supplierId" :items="suppliers" placeholder="仕入先を検索…" @update:model-value="(v) => g.supplierId = v ?? 0" />
            </div>
          </label>
          <label class="block text-sm"><span class="text-gray-600">納入希望日</span><input v-model="g.dueDate" type="date" class="mt-1 w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm" /></label>
          <label class="block text-sm"><span class="text-gray-600">通貨</span>
            <div class="mt-1">
              <AutoComplete :model-value="g.currency" :options="[{ value: 'JPY', label: 'JPY' }, { value: 'CNY', label: 'CNY' }, { value: 'USD', label: 'USD' }]" :allow-empty="false" @update:model-value="(v) => g.currency = v" />
            </div>
          </label>
        </div>
        <table class="w-full">
          <thead class="border-b border-gray-200 bg-gray-50">
            <tr>
              <th class="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-600">素材</th>
              <th class="px-3 py-2 text-right text-xs font-semibold uppercase text-gray-600">所要量</th>
              <th class="px-3 py-2 text-left text-xs font-semibold uppercase text-gray-600">単位</th>
              <th class="px-3 py-2 text-right text-xs font-semibold uppercase text-gray-600">単価(任意)</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(l, li) in g.lines" :key="li" class="border-b border-gray-100 last:border-0">
              <td class="px-3 py-2 text-sm">{{ l.materialName }}</td>
              <td class="px-3 py-2 text-right font-mono text-sm">{{ l.requiredQuantity.toLocaleString() }}</td>
              <td class="px-3 py-2 text-sm">{{ l.unit }}</td>
              <td class="px-3 py-2 text-right"><input v-model.number="l.unitPrice" type="number" min="0" step="0.01" placeholder="未設定" class="w-28 rounded-md border border-gray-300 px-2 py-1 text-right text-sm" /></td>
            </tr>
          </tbody>
        </table>
        <div class="mt-3">
          <button type="button" :disabled="submittingIdx === gi" class="rounded-md bg-amber-500 px-4 py-2 text-sm text-white hover:bg-amber-600 disabled:opacity-50" @click="createGroup(gi)">
            {{ submittingIdx === gi ? '作成中…' : 'この仕入先で素材発注書を作成' }}
          </button>
        </div>
      </section>
      <NuxtLink to="/production/material-orders" class="text-sm text-blue-600 hover:underline">← 素材発注書一覧へ</NuxtLink>
    </template>
    <p class="mt-3 text-xs text-gray-400">API: POST /api/v1/material-orders/prepare → POST /api/v1/material-orders</p>
  </main>
</template>
