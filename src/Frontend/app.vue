<script setup lang="ts">
const route = useRoute()
const showNav = computed(() => route.path !== '/login')
</script>

<template>
  <div class="min-h-screen bg-gray-50 text-gray-900">
    <AppNav v-if="showNav" />
    <NuxtPage />
  </div>
</template>
