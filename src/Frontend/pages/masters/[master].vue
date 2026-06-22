<script setup lang="ts">
import { findMasterBySlug } from '~/utils/master-definitions'
import type { MasterItem } from '~/composables/useMasters'

const route = useRoute()
const slug = computed(() => route.params.master as string)
const def = computed(() => findMasterBySlug(slug.value))

const { canEditMaster } = useAuth()
const { list, create, update, softDelete, restore } = useMasters()

const items = ref<MasterItem[]>([])
const loading = ref(true)
const errorMessage = ref('')
const includeDeleted = ref(false)
const search = ref('')

const referenceData = ref<Record<string, MasterItem[]>>({})

const dialogOpen = ref(false)
const dialogTarget = ref<MasterItem | null>(null)

const reload = async () => {
  if (!def.value) return
  loading.value = true
  errorMessage.value = ''
  try {
    items.value = await list(slug.value, includeDeleted.value)
  } catch (e) {
    const err = e as { statusCode?: number }
    errorMessage.value = err.statusCode === 401
      ? 'セッションが切れました。再ログインしてください。'
      : `${def.value.label} の取得に失敗しました`
  } finally {
    loading.value = false
  }
}

// FK select-master 用に参照先マスタを取得
const loadReferences = async () => {
  if (!def.value) return
  const fkSlugs = def.value.extensionFields
    .filter((f) => f.type === 'select-master' && f.master)
    .map((f) => f.master!)
  const unique = [...new Set(fkSlugs)]
  for (const fkSlug of unique) {
    if (!referenceData.value[fkSlug]) {
      try {
        referenceData.value[fkSlug] = await list(fkSlug, false)
      } catch {
        referenceData.value[fkSlug] = []
      }
    }
  }
}

watch(slug, async () => {
  items.value = []
  referenceData.value = {}
  await loadReferences()
  await reload()
}, { immediate: true })

watch(includeDeleted, reload)

const filteredItems = computed(() => {
  const q = search.value.trim().toLowerCase()
  if (!q) return items.value
  return items.value.filter(
    (i) =>
      i.code.toLowerCase().includes(q) ||
      i.name.toLowerCase().includes(q),
  )
})

const onNew = () => {
  dialogTarget.value = null
  dialogOpen.value = true
}

const onEdit = (item: MasterItem) => {
  dialogTarget.value = item
  dialogOpen.value = true
}

const onSaved = async (payload: Record<string, unknown>) => {
  try {
    if (dialogTarget.value) {
      await update(slug.value, dialogTarget.value.id, payload)
    } else {
      await create(slug.value, payload)
    }
    dialogOpen.value = false
    await reload()
  } catch (e) {
    const err = e as { data?: { detail?: string }; statusMessage?: string }
    errorMessage.value = err.data?.detail ?? err.statusMessage ?? '保存に失敗しました'
  }
}

const onDelete = async (item: MasterItem) => {
  if (!confirm(`${item.code} - ${item.name} を論理削除しますか？\n(復元可能、関連データへの影響は反映されません)`)) return
  try {
    await softDelete(slug.value, item.id)
    await reload()
  } catch {
    errorMessage.value = '削除に失敗しました'
  }
}

const onRestore = async (item: MasterItem) => {
  try {
    await restore(slug.value, item.id)
    await reload()
  } catch {
    errorMessage.value = '復元に失敗しました'
  }
}

const formatCell = (value: unknown): string => {
  if (value === null || value === undefined) return '—'
  if (typeof value === 'boolean') return value ? '✓' : ''
  return String(value)
}
</script>

<template>
  <main class="mx-auto max-w-7xl px-4 py-8">
    <div v-if="!def" class="rounded border border-red-300 bg-red-50 p-4 text-red-700">
      マスタが見つかりません: <code>{{ slug }}</code>
      <div class="mt-2">
        <NuxtLink to="/masters" class="text-blue-600 underline">マスタ一覧に戻る</NuxtLink>
      </div>
    </div>

    <template v-else>
      <header class="mb-6 flex items-start justify-between gap-4">
        <div>
          <div class="text-xs text-gray-500">
            <NuxtLink to="/masters" class="hover:underline">マスタ一覧</NuxtLink>
            <span class="mx-1">/</span>
            <span>{{ slug }}</span>
          </div>
          <h1 class="text-2xl font-bold">{{ def.label }}</h1>
          <p v-if="def.description" class="mt-1 text-sm text-gray-600">{{ def.description }}</p>
        </div>
        <button
          v-if="canEditMaster"
          type="button"
          class="rounded-md bg-blue-600 px-4 py-2 text-sm text-white shadow-sm hover:bg-blue-700"
          @click="onNew"
        >
          + 新規追加
        </button>
        <span v-else class="text-xs text-gray-400">参照のみ (品番台帳管理権限なし)</span>
      </header>

      <div class="mb-3 flex items-center gap-4">
        <input
          v-model="search"
          type="search"
          placeholder="コード / 名称で検索"
          class="w-64 rounded-md border border-gray-300 px-3 py-1.5 text-sm focus:border-blue-500 focus:outline-none"
        />
        <label class="inline-flex items-center gap-2 text-sm text-gray-600">
          <input v-model="includeDeleted" type="checkbox" class="h-4 w-4 rounded border-gray-300" />
          論理削除済みを含む
        </label>
        <span class="ml-auto text-xs text-gray-500">{{ filteredItems.length }} 件</span>
      </div>

      <div v-if="errorMessage" class="mb-3 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
        {{ errorMessage }}
      </div>

      <section class="overflow-hidden rounded-lg border border-gray-200 bg-white shadow-sm">
        <div v-if="loading" class="p-8 text-center text-gray-500">読み込み中…</div>

        <table v-else class="w-full">
          <thead class="border-b border-gray-200 bg-gray-50">
            <tr>
              <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">コード</th>
              <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">名称</th>
              <th
                v-for="col in def.extensionColumns ?? []"
                :key="col.key"
                class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600"
              >
                {{ col.label }}
              </th>
              <th class="px-4 py-3 text-left text-xs font-semibold uppercase text-gray-600">状態</th>
              <th class="px-4 py-3 text-right text-xs font-semibold uppercase text-gray-600">操作</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="filteredItems.length === 0">
              <td colspan="99" class="px-4 py-8 text-center text-sm text-gray-500">
                {{ search ? '検索条件に一致するデータがありません' : 'データがありません' }}
              </td>
            </tr>
            <tr
              v-for="i in filteredItems"
              :key="i.id"
              :class="{ 'bg-gray-50 text-gray-400': i.deleteFlag }"
              class="border-b border-gray-100 last:border-0"
            >
              <td class="px-4 py-3 font-mono text-sm">{{ i.code }}</td>
              <td class="px-4 py-3 text-sm">{{ i.name }}</td>
              <td
                v-for="col in def.extensionColumns ?? []"
                :key="col.key"
                class="px-4 py-3 text-sm"
              >
                {{ formatCell(i[col.key]) }}
              </td>
              <td class="px-4 py-3 text-sm">
                <span
                  v-if="i.deleteFlag"
                  class="inline-block rounded-full bg-gray-200 px-2 py-0.5 text-xs text-gray-600"
                >
                  削除済
                </span>
                <span
                  v-else
                  class="inline-block rounded-full bg-green-100 px-2 py-0.5 text-xs text-green-700"
                >
                  有効
                </span>
              </td>
              <td class="px-4 py-3 text-right">
                <template v-if="canEditMaster">
                  <button
                    v-if="!i.deleteFlag"
                    type="button"
                    class="mr-2 text-sm text-blue-600 hover:underline"
                    @click="onEdit(i)"
                  >
                    編集
                  </button>
                  <button
                    v-if="!i.deleteFlag"
                    type="button"
                    class="text-sm text-red-600 hover:underline"
                    @click="onDelete(i)"
                  >
                    削除
                  </button>
                  <button
                    v-else
                    type="button"
                    class="text-sm text-green-700 hover:underline"
                    @click="onRestore(i)"
                  >
                    復元
                  </button>
                </template>
                <span v-else class="text-xs text-gray-400">—</span>
              </td>
            </tr>
          </tbody>
        </table>
      </section>

      <p class="mt-3 text-xs text-gray-400">
        API: GET/POST/PATCH/DELETE /api/v1/masters/{{ slug }} (audit_logs 記録)
      </p>

      <MasterEditDialog
        v-if="def"
        :open="dialogOpen"
        :def="def"
        :initial="dialogTarget"
        :reference-data="referenceData"
        @close="dialogOpen = false"
        @saved="onSaved"
      />
    </template>
  </main>
</template>
