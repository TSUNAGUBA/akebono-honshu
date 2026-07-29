<script setup lang="ts">
import type { FamilyDetail } from '~/composables/useProducts'
import type { MasterItem } from '~/composables/useMasters'

const route = useRoute()
// 第二段階契約: family id は uuid 文字列。未指定は '' (数値変換しない)。
const familyId = computed(() => String(route.query.familyId ?? ''))
const { getFamily } = useProducts()
const { list } = useMasters()
const { piCreate } = useProduction()
// スナックバー通知（必須未入力の押下時アラート）。共通の通知基盤 (composables/useSnackbar)。
const { showError } = useSnackbar()

const family = ref<FamilyDetail | null>(null)
// 加工先 = 工場 (Part2)。工場マスタを参照する (旧: 仕入先兼用)。
const factories = ref<MasterItem[]>([])
const loading = ref(true)
const errorMessage = ref('')
const submitting = ref(false)

// 必須項目バリデーション状態（要件: 登録ボタン押下時に未入力項目を赤枠＋メッセージで明示）。
// キーはフォーム項目名。true = 未入力エラー。submit の validate() で毎回再構築する。
const fieldErrors = ref<Record<string, boolean>>({})

const form = ref({
  factorySupplierId: '',  // uuid 文字列 ('' = 未選択、要件: 必須項目はデフォルト未選択)
  dueDate: todayJstPlusDays(30), // 業務日付は JST 基準
  communicationText: '',
})
// productId (uuid 文字列) -> quantity
const qty = ref<Record<string, number>>({})

const reload = async () => {
  loading.value = true
  errorMessage.value = ''
  try {
    if (!familyId.value) { errorMessage.value = '品番が指定されていません。生産手配状況または商品一覧から選択してください。'; return }
    family.value = await getFamily(familyId.value)
    factories.value = await list('factories')
    for (const p of family.value?.products ?? []) qty.value[p.id] = 0
  } catch {
    errorMessage.value = '商品情報の取得に失敗しました'
  } finally { loading.value = false }
}
onMounted(reload)

const totalQty = computed(() => Object.values(qty.value).reduce((a, b) => a + (Number(b) || 0), 0))

// 必須項目バリデーション。未入力項目に赤枠フラグを立て、未入力ラベルの一覧を返す。
// 既存の個別ガード（加工先未選択・生産数量 0）を validate() に集約し、赤枠＋スナックバーの対象にする。
const validate = (): string[] => {
  const errs: Record<string, boolean> = {}
  const missing: string[] = []
  if (!form.value.factorySupplierId) { errs.factorySupplierId = true; missing.push('加工先') }
  if (!(totalQty.value > 0)) { errs.quantity = true; missing.push('生産数量') }
  fieldErrors.value = errs
  return missing
}

const submit = async () => {
  errorMessage.value = ''
  const missing = validate()
  if (missing.length > 0) {
    const msg = `必須項目が未入力です（${missing.join(' / ')}）`
    errorMessage.value = msg
    showError(msg) // スナックバーでも警告（要件: 押下時アラート）
    return
  }
  const lines = Object.entries(qty.value)
    .map(([pid, q]) => ({ productId: pid, quantity: Number(q) || 0 }))
    .filter(l => l.quantity > 0)
  submitting.value = true
  try {
    const res = await piCreate({
      productFamilyId: familyId.value,
      factorySupplierId: form.value.factorySupplierId,
      dueDate: form.value.dueDate,
      communicationText: form.value.communicationText || null,
      lines,
    })
    await navigateTo(`/production/instructions/${res.id}`)
  } catch (e) {
    errorMessage.value = getApiErrorMessage(e, '生産指示書の作成に失敗しました')
  } finally { submitting.value = false }
}
</script>

<template>
  <main class="mx-auto max-w-screen-2xl px-3 py-3">
    <header class="mb-3">
      <h1 class="text-2xl font-bold">生産指示書 新規作成</h1>
      <p class="mt-1 text-sm text-gray-500">PI-01 商品マスタ＋生産数量を起点に、加工先へ生産を指示します。</p>
    </header>

    <div v-if="errorMessage" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">{{ errorMessage }}</div>
    <section v-if="loading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500 shadow-sm">読み込み中…</section>

    <template v-else-if="family">
      <section class="mb-4 rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm">
        <div class="mb-3 text-sm"><span class="text-gray-500">品番:</span> <strong>{{ family.family.productName1 }}</strong></div>
        <div class="grid grid-cols-1 gap-x-3 gap-y-2 sm:grid-cols-3">
          <label class="block text-sm">
            <span class="text-gray-600">加工先 (工場) <span class="text-red-500">*</span></span>
            <div class="mt-1">
              <MasterSelect :model-value="form.factorySupplierId" :items="factories" :error="fieldErrors.factorySupplierId" placeholder="加工先を検索…" @update:model-value="(v) => form.factorySupplierId = v ?? ''" />
            </div>
            <span v-if="fieldErrors.factorySupplierId" class="mt-1 block text-xs text-red-600">加工先を選択してください</span>
          </label>
          <label class="block text-sm">
            <span class="text-gray-600">希望納期</span>
            <input v-model="form.dueDate" type="date" class="mt-1 w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm" />
          </label>
          <div class="flex items-end text-sm text-gray-600">生産総数: <strong class="ml-1">{{ totalQty.toLocaleString() }}</strong></div>
        </div>
        <label class="mt-3 block text-sm">
          <span class="text-gray-600">連絡事項</span>
          <textarea v-model="form.communicationText" rows="2" class="mt-1 w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm" />
        </label>
      </section>

      <section class="mb-4 overflow-x-auto rounded-lg border bg-white shadow-sm" :class="fieldErrors.quantity ? 'border-red-400 ring-1 ring-red-400' : 'border-gray-200'">
        <p v-if="fieldErrors.quantity" class="px-3 pt-2 text-xs text-red-600">生産数量を 1 件以上入力してください</p>
        <table class="w-full">
          <thead class="border-b border-gray-200 bg-gray-50">
            <tr>
              <th class="px-3 py-1.5 text-left text-xs font-semibold uppercase text-gray-600">品番(SKU)</th>
              <th class="px-3 py-1.5 text-left text-xs font-semibold uppercase text-gray-600">色</th>
              <th class="px-3 py-1.5 text-left text-xs font-semibold uppercase text-gray-600">サイズ</th>
              <th class="px-3 py-1.5 text-right text-xs font-semibold uppercase text-gray-600">生産数量 <span class="text-red-500">*</span></th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="p in family.products" :key="p.id" class="border-b border-gray-100 last:border-0">
              <td class="px-3 py-1.5 font-mono text-sm">{{ p.sku }}</td>
              <td class="px-3 py-1.5 text-sm">{{ p.colorName }}</td>
              <td class="px-3 py-1.5 text-sm">{{ p.sizeName }}</td>
              <td class="px-3 py-1.5 text-right">
                <input v-model.number="qty[p.id]" type="number" min="0" class="w-24 rounded-md border border-gray-300 px-2 py-1 text-right text-sm" />
              </td>
            </tr>
          </tbody>
        </table>
      </section>

      <div class="flex gap-3">
        <button type="button" :disabled="submitting" class="rounded-md bg-blue-600 px-4 py-2 text-sm text-white shadow-sm hover:bg-blue-700 disabled:opacity-50" @click="submit">
          {{ submitting ? '作成中…' : '生産指示書を作成 (下書き)' }}
        </button>
        <NuxtLink to="/production/instructions" class="rounded-md border border-gray-300 px-4 py-2 text-sm hover:bg-gray-50">キャンセル</NuxtLink>
      </div>
    </template>
    <p class="mt-3 text-xs text-gray-400">API: POST /api/maker/v1/production-instructions</p>
  </main>
</template>
