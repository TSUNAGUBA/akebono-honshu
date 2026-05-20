<script setup lang="ts">
interface UserItem {
  id: number
  employeeNo: string
  loginId: string
  displayName: string
  isActive: boolean
}

const { logout } = useAuth()
const { apiFetch } = useApi()

const users = ref<UserItem[]>([])
const loading = ref(true)
const errorMessage = ref('')

onMounted(async () => {
  try {
    const res = await apiFetch<{ data: UserItem[] }>('/users')
    users.value = res.data
  } catch (e: unknown) {
    const err = e as { statusCode?: number }
    errorMessage.value = err.statusCode === 401
      ? 'セッションが切れました。再ログインしてください。'
      : 'ユーザ一覧の取得に失敗しました'
    if (err.statusCode === 401) {
      await logout()
      await navigateTo('/login')
    }
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <main class="mx-auto max-w-5xl px-4 py-8">
    <header class="mb-6">
      <h1 class="text-2xl font-bold">ユーザ一覧</h1>
      <p class="text-sm text-gray-500">M-03 ユーザマスタ (Iteration 1 で権限管理拡張予定)</p>
    </header>

    <section class="rounded-lg border border-gray-200 bg-white shadow-sm">
      <div v-if="loading" class="p-8 text-center text-gray-500">読み込み中…</div>

      <div v-else-if="errorMessage" class="p-8 text-center text-red-600">
        {{ errorMessage }}
      </div>

      <table v-else class="w-full">
        <thead class="border-b border-gray-200 bg-gray-50">
          <tr>
            <th class="px-4 py-3 text-left text-sm font-semibold">社員番号</th>
            <th class="px-4 py-3 text-left text-sm font-semibold">ログイン ID</th>
            <th class="px-4 py-3 text-left text-sm font-semibold">表示名</th>
            <th class="px-4 py-3 text-left text-sm font-semibold">状態</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="u in users"
            :key="u.id"
            class="border-b border-gray-100 last:border-0"
          >
            <td class="px-4 py-3 text-sm font-mono">{{ u.employeeNo }}</td>
            <td class="px-4 py-3 text-sm">{{ u.loginId }}</td>
            <td class="px-4 py-3 text-sm">{{ u.displayName }}</td>
            <td class="px-4 py-3 text-sm">
              <span
                :class="u.isActive ? 'bg-green-100 text-green-700' : 'bg-gray-100 text-gray-500'"
                class="inline-block rounded-full px-2 py-0.5 text-xs"
              >
                {{ u.isActive ? '有効' : '無効' }}
              </span>
            </td>
          </tr>
        </tbody>
      </table>
    </section>

    <p class="mt-4 text-xs text-gray-400">
      API: GET /api/v1/users (audit_logs に User.List 記録)
    </p>
  </main>
</template>
