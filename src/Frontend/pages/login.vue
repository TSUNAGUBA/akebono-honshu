<script setup lang="ts">
const { login } = useAuth()

const email = ref('')
const password = ref('')
const errorMessage = ref('')
const submitting = ref(false)

const onSubmit = async () => {
  errorMessage.value = ''
  submitting.value = true
  try {
    await login(email.value, password.value)
    await navigateTo('/users')
  }
  catch (e: unknown) {
    // Firebase Auth エラーは FirebaseError.code を持つ。
    // /auth/sync が 403 (業務ユーザ未紐付け) を返すケースも別メッセージで案内。
    const err = e as { code?: string; data?: { detail?: string }; statusCode?: number }
    if (err.statusCode === 403 && err.data?.detail) {
      errorMessage.value = err.data.detail
    }
    else if (err.code === 'auth/invalid-credential'
          || err.code === 'auth/wrong-password'
          || err.code === 'auth/user-not-found') {
      errorMessage.value = 'メールアドレスまたはパスワードが正しくありません'
    }
    else if (err.code === 'auth/too-many-requests') {
      errorMessage.value = '試行回数が多すぎます。しばらく時間を置いてからお試しください'
    }
    else if (err.code === 'auth/network-request-failed') {
      errorMessage.value = 'ネットワークエラー。接続を確認してください'
    }
    else {
      errorMessage.value = 'ログインに失敗しました'
    }
  }
  finally {
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
      <h1 class="mb-1 text-2xl font-bold">
        akebono 生産管理
      </h1>
      <p class="mb-6 text-sm text-gray-500">
        Firebase Authentication でログイン
      </p>

      <label class="mb-4 block">
        <span class="mb-1 block text-sm font-medium">メールアドレス</span>
        <input
          v-model="email"
          type="email"
          required
          autocomplete="username"
          class="w-full rounded-md border border-gray-300 px-3 py-2 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
        >
      </label>

      <label class="mb-6 block">
        <span class="mb-1 block text-sm font-medium">パスワード</span>
        <input
          v-model="password"
          type="password"
          required
          autocomplete="current-password"
          class="w-full rounded-md border border-gray-300 px-3 py-2 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
        >
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
    </form>
  </main>
</template>
