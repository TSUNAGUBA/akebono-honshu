<script setup lang="ts">
// ルートは <NuxtLayout>（layouts/default.vue = ポータル）で chrome を提供する。
// SSR では Firebase Web SDK が動かないため、実コンテンツは <ClientOnly> 内で CSR 描画する。
// /login は definePageMeta({ layout: false }) でヘッダ無しの全画面表示。
</script>

<template>
  <div class="min-h-screen bg-gray-50 text-gray-900">
    <ClientOnly>
      <NuxtLayout>
        <NuxtPage />
      </NuxtLayout>
      <!-- グローバルスナックバー（新規登録フォーム等のバリデーション警告表示）。全ページ共通。 -->
      <AppSnackbar />
      <template #fallback>
        <div class="flex min-h-screen items-center justify-center">
          <p class="text-gray-500">読み込み中…</p>
        </div>
      </template>
    </ClientOnly>
  </div>
</template>
