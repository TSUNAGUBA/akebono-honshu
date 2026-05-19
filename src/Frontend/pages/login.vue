<script setup lang="ts">
const { login } = useAuth()

const loginId = ref('owner')
const password = ref('localdev')
const errorMessage = ref('')
const submitting = ref(false)

const onSubmit = async () => {
  errorMessage.value = ''
  submitting.value = true
  try {
    await login(loginId.value, password.value)
    await navigateTo('/users')
  } catch (e: unknown) {
    const err = e as { data?: { detail?: string }; statusCode?: number }
    errorMessage.value = err.data?.detail
      || (err.statusCode === 401 ? 'ログイン ID またはパスワードが正しくありません' : 'ログインに失敗しました')
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <main class="flex min-h-screen items-center justify-center px-4">
    <form
      class="w-full max-w-md rounded-lg border border-gray-200 bg-white p-8 shadow-sm"
      @submit.prevent="onSubmit"
    >
      <h1 class="mb-1 text-2xl font-bold">akebono 生産管理</h1>
      <p class="mb-6 text-sm text-gray-500">Iteration 0 (ローカル開発環境) ダミー認証</p>

      <label class="mb-4 block">
        <span class="mb-1 block text-sm font-medium">ログイン ID</span>
        <input
          v-model="loginId"
          type="text"
          required
          autocomplete="username"
          class="w-full rounded-md border border-gray-300 px-3 py-2 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
        />
      </label>

      <label class="mb-6 block">
        <span class="mb-1 block text-sm font-medium">パスワード</span>
        <input
          v-model="password"
          type="password"
          required
          autocomplete="current-password"
          class="w-full rounded-md border border-gray-300 px-3 py-2 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
        />
      </label>

      <button
        type="submit"
        :disabled="submitting"
        class="w-full rounded-md bg-blue-600 px-4 py-2 font-medium text-white transition hover:bg-blue-700 disabled:opacity-50"
      >
        {{ submitting ? 'ログイン中…' : 'ログイン' }}
      </button>

      <p v-if="errorMessage" class="mt-4 rounded-md bg-red-50 px-3 py-2 text-sm text-red-700">
        {{ errorMessage }}
      </p>

      <p class="mt-6 text-xs text-gray-400">
        Seed: owner / planner / sales のいずれか + パスワード "localdev"
      </p>
    </form>
  </main>
</template>
