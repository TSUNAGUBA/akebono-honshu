<script setup lang="ts">
/**
 * ポータルのヘッダ（旧 AppNav の刷新）。
 * ブランド（Akebono Honshu / ホームリンク）＋ 戻る ＋ パンくず ＋ ユーザ/ログアウト。
 * 現在位置（パンくず・戻り先）は useNav が route.path から導出する。
 */
const { breadcrumbs, parentPath } = useNav()
const { user, logout } = useAuth()

const onLogout = async () => {
  await logout()
  await navigateTo('/login')
}

// 先頭「ホーム」はブランドリンクが担うため、パンくず表示は 2 件目以降。
const trail = computed(() => breadcrumbs.value.slice(1))
</script>

<template>
  <header class="border-b border-gray-200 bg-white">
    <div class="mx-auto flex h-14 max-w-7xl items-center gap-2 px-4 sm:gap-3">
      <!-- 戻る（1 つ上の階層へ） -->
      <NuxtLink
        v-if="parentPath"
        :to="parentPath"
        class="flex h-8 w-8 shrink-0 items-center justify-center rounded-md text-gray-500 hover:bg-gray-100 hover:text-gray-800"
        aria-label="一つ前の画面に戻る"
      >
        <NavIcon name="arrow-left" class="h-5 w-5" />
      </NuxtLink>

      <!-- ブランド（ホーム） -->
      <NuxtLink
        to="/"
        class="flex shrink-0 items-center gap-2 no-underline"
        aria-label="ホーム"
      >
        <span class="flex h-8 w-8 items-center justify-center rounded-lg bg-blue-600 text-white">
          <NavIcon name="box" class="h-5 w-5" />
        </span>
        <span class="hidden text-base font-bold tracking-tight text-gray-900 sm:inline">
          Akebono Honshu
        </span>
      </NuxtLink>

      <!-- パンくず -->
      <nav
        v-if="trail.length"
        class="flex min-w-0 items-center text-sm text-gray-500"
        aria-label="パンくずリスト"
      >
        <template v-for="(crumb, i) in trail" :key="i">
          <span
            class="flex items-center"
            :class="i < trail.length - 1 ? 'hidden lg:flex' : 'flex min-w-0'"
          >
            <NavIcon name="chevron-right" class="mx-0.5 h-4 w-4 shrink-0 text-gray-300" />
            <NuxtLink
              v-if="crumb.to && i < trail.length - 1"
              :to="crumb.to"
              class="whitespace-nowrap rounded px-1 py-0.5 text-gray-500 no-underline hover:bg-gray-100 hover:text-gray-800"
            >
              {{ crumb.label }}
            </NuxtLink>
            <span v-else class="truncate px-1 font-semibold text-gray-800">
              {{ crumb.label }}
            </span>
          </span>
        </template>
      </nav>

      <!-- 右: ユーザ / ログアウト -->
      <div class="ml-auto flex shrink-0 items-center gap-2 sm:gap-3">
        <ClientOnly>
          <span v-if="user" class="hidden text-sm text-gray-600 md:inline">
            <strong class="font-semibold text-gray-900">{{ user.displayName }}</strong>
          </span>
          <button
            v-if="user"
            type="button"
            class="flex items-center gap-1.5 rounded-md border border-gray-300 bg-white px-2.5 py-1.5 text-sm text-gray-700 hover:bg-gray-50"
            @click="onLogout"
          >
            <NavIcon name="log-out" class="h-4 w-4" />
            <span class="hidden sm:inline">ログアウト</span>
          </button>
        </ClientOnly>
      </div>
    </div>
  </header>
</template>
