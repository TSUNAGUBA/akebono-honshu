<script setup lang="ts">
/**
 * MIG-3 既存生産管理システム CSV 取込画面 (Phase 7 Iteration 4 Hardening)。
 *
 * ファイル添付 → 取込実行 → 結果表示。Backend は POST /api/maker/v1/admin/legacy-import。
 * 認可: process_record_permission = 1 (Owner) のみ。
 */
interface PhaseResult { applied: boolean; detail: string }
interface StagingResult { rowsLoaded: number; distinctFamilyCount: number; rowsWithCostPrice: number }
interface ImportResult { productFamiliesInserted: number; productsInserted: number; supplierPricesInserted: number }
interface FallbackResult { colorFallback: number; sizeFallback: number; supplierFallback: number }
interface ApiResult {
  prePatch: PhaseResult
  masterFill: PhaseResult
  staging: StagingResult
  import: ImportResult
  fallbacks: FallbackResult
  warnings: string[]
  elapsed: string
}

const { user, logout } = useAuth()
const { apiData } = useApi()

const fileInput = ref<HTMLInputElement | null>(null)
const selectedFile = ref<File | null>(null)
const loading = ref(false)
const errorMessage = ref('')
const result = ref<ApiResult | null>(null)
const confirmOpen = ref(false)

const isOwner = computed(() => user.value?.processRecordPermission === 1)

const onFileChange = (e: Event) => {
  const target = e.target as HTMLInputElement
  selectedFile.value = target.files?.[0] ?? null
  errorMessage.value = ''
  result.value = null
}

const formatBytes = (bytes: number) => {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / 1024 / 1024).toFixed(2)} MB`
}

const onSubmit = () => {
  if (!selectedFile.value) {
    errorMessage.value = 'CSV ファイルを選択してください'
    return
  }
  confirmOpen.value = true
}

const execute = async () => {
  if (!selectedFile.value) return
  confirmOpen.value = false
  loading.value = true
  errorMessage.value = ''
  result.value = null

  const formData = new FormData()
  formData.append('file', selectedFile.value)

  try {
    const res = await apiData<ApiResult>('/admin/legacy-import', {
      method: 'POST',
      body: formData,
    })
    result.value = res
  } catch (e: unknown) {
    const err = e as { statusCode?: number }
    if (err.statusCode === 401) {
      await logout()
      await navigateTo('/login')
      return
    }
    errorMessage.value = getApiErrorMessage(e, '取込に失敗しました (詳細不明)')
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <main class="mx-auto max-w-4xl px-3 py-3">
    <header class="mb-3">
      <h1 class="text-2xl font-bold">MIG-3 既存データ移行</h1>
      <p class="mt-1 text-sm text-gray-500">
        旧生産管理システムの商品マスタ CSV (1,288 行 × 138 列) を新システム DB に取込みます。
        Phase 7 Iteration 4 Hardening。
      </p>
    </header>

    <!-- 権限不足 -->
    <ClientOnly>
      <div v-if="!isOwner" class="rounded-md border border-red-300 bg-red-50 p-4">
        <p class="font-semibold text-red-800">アクセス権限がありません</p>
        <p class="mt-1 text-sm text-red-700">
          この画面はオーナー権限 (process_record_permission = 1) を持つユーザのみアクセスできます。
        </p>
        <NuxtLink to="/" class="mt-2 inline-block text-sm text-blue-600 underline">トップに戻る</NuxtLink>
      </div>

      <!-- 取込フォーム -->
      <div v-else>
        <div class="rounded-md border border-amber-300 bg-amber-50 p-4">
          <p class="font-semibold text-amber-900">非可逆操作の注意</p>
          <ul class="mt-2 list-inside list-disc text-sm text-amber-900">
            <li>本取込は <code>products.sku</code> 列を VARCHAR(11) → VARCHAR(16) に拡張します</li>
            <li>旧 31 色 / 旧 10 サイズ / 旧 11 仕入先 を <strong>colors / sizes / suppliers</strong> に追加します</li>
            <li>686 件の <strong>product_families</strong> を <code>planned_year_code = 'Z'</code> で取込みます</li>
            <li>1,288 件の <strong>products</strong> を旧 SKU そのままで取込みます</li>
            <li>原価単価ありの <strong>product_supplier_prices</strong> を追加します</li>
            <li>取込前に <code>pg_dump</code> でバックアップを取得してください</li>
          </ul>
        </div>

        <div class="mt-6 rounded-md border border-gray-200 bg-white p-5 shadow-sm">
          <label class="block text-sm font-semibold text-gray-700">
            CSV ファイル
            <span class="ml-1 text-xs font-normal text-gray-500">
              (Shift_JIS / UTF-8 自動判定、最大 50 MB)
            </span>
          </label>
          <input
            ref="fileInput"
            type="file"
            accept=".csv,text/csv"
            class="mt-2 block w-full text-sm text-gray-700 file:mr-4 file:rounded-md file:border-0 file:bg-blue-50 file:px-4 file:py-2 file:text-sm file:font-semibold file:text-blue-700 hover:file:bg-blue-100"
            :disabled="loading"
            @change="onFileChange"
          />
          <p v-if="selectedFile" class="mt-2 text-sm text-gray-600">
            選択: <strong>{{ selectedFile.name }}</strong> ({{ formatBytes(selectedFile.size) }})
          </p>

          <button
            type="button"
            class="mt-4 inline-flex items-center rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:cursor-not-allowed disabled:bg-gray-400"
            :disabled="!selectedFile || loading"
            @click="onSubmit"
          >
            <span v-if="loading">取込中… (数分かかる場合があります)</span>
            <span v-else>取込実行</span>
          </button>
        </div>

        <!-- エラー -->
        <div v-if="errorMessage" class="mt-4 rounded-md border border-red-300 bg-red-50 p-4">
          <p class="font-semibold text-red-800">取込エラー</p>
          <p class="mt-1 whitespace-pre-wrap text-sm text-red-700">{{ errorMessage }}</p>
        </div>

        <!-- 結果 -->
        <div v-if="result" class="mt-6 space-y-3">
          <div class="rounded-md border border-green-300 bg-green-50 p-4">
            <p class="font-semibold text-green-900">取込完了 (経過時間: {{ result.elapsed }})</p>
          </div>

          <div class="grid grid-cols-1 gap-4 md:grid-cols-2">
            <div class="rounded-md border border-gray-200 bg-white p-4">
              <h3 class="text-sm font-semibold text-gray-700">DB 拡張 / マスタ補完</h3>
              <dl class="mt-2 space-y-1 text-sm">
                <div class="flex justify-between">
                  <dt class="text-gray-600">Pre-patch:</dt>
                  <dd class="font-mono text-gray-900">{{ result.prePatch.detail }}</dd>
                </div>
                <div class="flex justify-between">
                  <dt class="text-gray-600">Master-fill:</dt>
                  <dd class="font-mono text-gray-900">{{ result.masterFill.detail }}</dd>
                </div>
              </dl>
            </div>

            <div class="rounded-md border border-gray-200 bg-white p-4">
              <h3 class="text-sm font-semibold text-gray-700">Staging 取込</h3>
              <dl class="mt-2 space-y-1 text-sm">
                <div class="flex justify-between">
                  <dt class="text-gray-600">行数:</dt>
                  <dd class="font-mono text-gray-900">{{ result.staging.rowsLoaded }}</dd>
                </div>
                <div class="flex justify-between">
                  <dt class="text-gray-600">ユニーク 他品番:</dt>
                  <dd class="font-mono text-gray-900">{{ result.staging.distinctFamilyCount }}</dd>
                </div>
                <div class="flex justify-between">
                  <dt class="text-gray-600">単価あり:</dt>
                  <dd class="font-mono text-gray-900">{{ result.staging.rowsWithCostPrice }}</dd>
                </div>
              </dl>
            </div>

            <div class="rounded-md border border-gray-200 bg-white p-4">
              <h3 class="text-sm font-semibold text-gray-700">本テーブル取込</h3>
              <dl class="mt-2 space-y-1 text-sm">
                <div class="flex justify-between">
                  <dt class="text-gray-600">product_families:</dt>
                  <dd class="font-mono text-gray-900">{{ result.import.productFamiliesInserted }}</dd>
                </div>
                <div class="flex justify-between">
                  <dt class="text-gray-600">products:</dt>
                  <dd class="font-mono text-gray-900">{{ result.import.productsInserted }}</dd>
                </div>
                <div class="flex justify-between">
                  <dt class="text-gray-600">supplier_prices:</dt>
                  <dd class="font-mono text-gray-900">{{ result.import.supplierPricesInserted }}</dd>
                </div>
              </dl>
            </div>

            <div class="rounded-md border border-gray-200 bg-white p-4">
              <h3 class="text-sm font-semibold text-gray-700">フォールバック適用件数</h3>
              <p class="mt-1 text-xs text-gray-500">
                マスタに該当コードが無く、デフォルト値 (黒 / M / 工場 A) で取込んだレコード数
              </p>
              <dl class="mt-2 space-y-1 text-sm">
                <div class="flex justify-between">
                  <dt class="text-gray-600">color:</dt>
                  <dd class="font-mono" :class="result.fallbacks.colorFallback > 0 ? 'text-amber-700' : 'text-gray-900'">
                    {{ result.fallbacks.colorFallback }}
                  </dd>
                </div>
                <div class="flex justify-between">
                  <dt class="text-gray-600">size:</dt>
                  <dd class="font-mono" :class="result.fallbacks.sizeFallback > 0 ? 'text-amber-700' : 'text-gray-900'">
                    {{ result.fallbacks.sizeFallback }}
                  </dd>
                </div>
                <div class="flex justify-between">
                  <dt class="text-gray-600">supplier:</dt>
                  <dd class="font-mono" :class="result.fallbacks.supplierFallback > 0 ? 'text-amber-700' : 'text-gray-900'">
                    {{ result.fallbacks.supplierFallback }}
                  </dd>
                </div>
              </dl>
            </div>
          </div>

          <div v-if="result.warnings.length > 0" class="rounded-md border border-amber-200 bg-amber-50 p-4">
            <h3 class="text-sm font-semibold text-amber-900">警告 ({{ result.warnings.length }})</h3>
            <ul class="mt-2 list-inside list-disc text-sm text-amber-900">
              <li v-for="(w, i) in result.warnings" :key="i">{{ w }}</li>
            </ul>
          </div>

          <div class="rounded-md border border-blue-200 bg-blue-50 p-4">
            <h3 class="text-sm font-semibold text-blue-900">次のステップ (業務担当者作業)</h3>
            <ol class="mt-2 list-inside list-decimal text-sm text-blue-900">
              <li>商品一覧 (<NuxtLink to="/products" class="underline">/products</NuxtLink>) で「年式: Z」「ステータス: Draft」のフィルタを使い、取込まれた 686 件を確認</li>
              <li>業務担当者が商品タイプ / 季節 / ブランド / 素材を UI から正しい値に更新</li>
              <li>商品分類 (旧 1〜20) は <code>staging_legacy_products.c036〜c055</code> を参照しながら手動マッピング</li>
            </ol>
          </div>
        </div>

        <!-- ナビゲーション -->
        <div class="mt-6">
          <NuxtLink to="/" class="text-sm text-blue-600 underline">トップに戻る</NuxtLink>
        </div>
      </div>
    </ClientOnly>

    <!-- 確認ダイアログ -->
    <div
      v-if="confirmOpen"
      class="fixed inset-0 z-50 flex items-center justify-center bg-black/50"
      @click.self="confirmOpen = false"
    >
      <div class="w-full max-w-md rounded-lg bg-white p-6 shadow-xl">
        <h2 class="text-lg font-bold text-gray-900">取込実行の確認</h2>
        <p class="mt-2 text-sm text-gray-700">
          <strong>{{ selectedFile?.name }}</strong> を取込みます。
          DB スキーマ拡張 + マスタ補完 + 1,288 件の商品登録を行います。
        </p>
        <p class="mt-2 text-sm text-amber-700">
          バックアップ取得済みであることを確認してください。
        </p>
        <div class="mt-4 flex justify-end gap-2">
          <button
            type="button"
            class="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm hover:bg-gray-50"
            @click="confirmOpen = false"
          >
            キャンセル
          </button>
          <button
            type="button"
            class="rounded-md bg-blue-600 px-4 py-2 text-sm font-semibold text-white hover:bg-blue-700"
            @click="execute"
          >
            取込実行
          </button>
        </div>
      </div>
    </div>
  </main>
</template>
