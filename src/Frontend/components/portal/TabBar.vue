<script setup lang="ts">
/**
 * セクション内のページ切替タブ。layouts/default.vue が「リンクが 2 つ以上」の
 * セクションでのみ表示する。アクティブ判定は navigation.ts の純関数に委譲するため、
 * 詳細サブルート（一覧→詳細）でも親タブがアクティブ表示される。
 */
import { isLinkActive, type NavSection } from '~/utils/navigation'

defineProps<{ section: NavSection }>()

const route = useRoute()
</script>

<template>
  <div class="border-b border-gray-200 bg-white">
    <div class="mx-auto flex max-w-screen-2xl items-center gap-1 overflow-x-auto px-4">
      <NuxtLink
        v-for="link in section.links"
        :key="link.path"
        :to="link.path"
        class="flex items-center gap-1.5 whitespace-nowrap border-b-2 px-3 py-2.5 text-sm font-medium no-underline transition"
        :class="isLinkActive(link, route.path)
          ? 'border-blue-600 text-blue-700'
          : 'border-transparent text-gray-500 hover:border-gray-300 hover:text-gray-800'"
        :aria-current="isLinkActive(link, route.path) ? 'page' : undefined"
      >
        <NavIcon :name="link.icon" class="h-4 w-4" />
        {{ link.label }}
      </NuxtLink>
    </div>
  </div>
</template>
