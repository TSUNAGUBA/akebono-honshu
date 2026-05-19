<script setup lang="ts">
const { user, logout } = useAuth()

const onLogout = async () => {
  logout()
  await navigateTo('/login')
}
</script>

<template>
  <nav class="border-b border-gray-200 bg-white shadow-sm">
    <div class="mx-auto flex max-w-7xl items-center justify-between px-4 py-3">
      <div class="flex items-center gap-8">
        <NuxtLink to="/" class="text-lg font-bold text-gray-900 hover:text-blue-600">
          あけぼの本州
        </NuxtLink>
        <div class="flex items-center gap-2 text-sm">
          <NuxtLink
            to="/users"
            class="rounded-md px-3 py-1.5 text-gray-700 hover:bg-blue-50 hover:text-blue-700"
            active-class="bg-blue-100 font-semibold text-blue-700"
          >
            ユーザ管理
          </NuxtLink>
          <NuxtLink
            to="/masters"
            class="rounded-md px-3 py-1.5 text-gray-700 hover:bg-blue-50 hover:text-blue-700"
            active-class="bg-blue-100 font-semibold text-blue-700"
          >
            マスタ管理
          </NuxtLink>
        </div>
      </div>
      <div class="flex items-center gap-3">
        <span v-if="user" class="text-sm text-gray-600">
          ログイン中: <strong class="text-gray-900">{{ user.displayName }}</strong>
        </span>
        <button
          v-if="user"
          type="button"
          class="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-sm hover:bg-gray-50"
          @click="onLogout"
        >
          ログアウト
        </button>
      </div>
    </div>
  </nav>
</template>
