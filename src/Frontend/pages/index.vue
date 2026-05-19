<script setup lang="ts">
// ルートアクセス時は middleware/auth.global.ts により /login or /users へ
onMounted(() => navigateTo('/users'))
</script>

<template>
  <div class="flex min-h-screen items-center justify-center">
    <p class="text-gray-500">読み込み中…</p>
  </div>
</template>
