<script setup lang="ts">
const route = useRoute()
const showNav = computed(() => route.path !== '/login')
</script>

<template>
  <div class="min-h-screen bg-gray-50 text-gray-900">
    <ClientOnly>
      <AppNav v-if="showNav" />
      <NuxtPage />
      <template #fallback>
        <div class="flex min-h-screen items-center justify-center">
          <p class="text-gray-500">読み込み中…</p>
        </div>
      </template>
    </ClientOnly>
  </div>
</template>
