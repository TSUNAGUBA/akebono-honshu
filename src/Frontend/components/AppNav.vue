<script setup lang="ts">
const { user, logout } = useAuth()

const onLogout = async () => {
  logout()
  await navigateTo('/login')
}
</script>

<template>
  <nav class="border-b border-gray-200 bg-white">
    <div class="mx-auto flex max-w-7xl items-center justify-between px-4 py-3">
      <div class="flex items-center gap-6">
        <NuxtLink to="/" class="text-lg font-bold">あけぼの本州</NuxtLink>
        <div class="flex items-center gap-4 text-sm">
          <NuxtLink
            to="/users"
            class="text-gray-600 hover:text-gray-900"
            active-class="font-semibold text-blue-600"
          >
            ユーザ
          </NuxtLink>
          <NuxtLink
            to="/masters"
            class="text-gray-600 hover:text-gray-900"
            active-class="font-semibold text-blue-600"
          >
            マスタ管理
          </NuxtLink>
        </div>
      </div>
      <div class="flex items-center gap-4">
        <span v-if="user" class="text-sm text-gray-600">
          <strong>{{ user.displayName }}</strong>
        </span>
        <button
          v-if="user"
          type="button"
          class="rounded-md border border-gray-300 bg-white px-3 py-1 text-sm hover:bg-gray-50"
          @click="onLogout"
        >
          ログアウト
        </button>
      </div>
    </div>
  </nav>
</template>
