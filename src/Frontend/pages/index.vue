<script setup lang="ts">
// ホーム = ポータル（目的別カテゴリカード）。業務フロー順（商品 → 発注 → 生産 →
// マスタ → システム管理）にカードを並べる。権限による絞り込みは useNav が行う。
const { categories } = useNav()
const { user } = useAuth()
</script>

<template>
  <main class="mx-auto w-full max-w-screen-2xl px-3 py-3">
    <div class="mb-3">
      <h1 class="text-xl font-bold text-gray-800">ホーム</h1>
      <p class="mt-1 text-sm text-gray-500">
        <template v-if="user?.displayName">
          {{ user.displayName }} さん、業務メニューを選択してください。
        </template>
        <template v-else>
          業務メニューを選択してください。
        </template>
      </p>
    </div>

    <div class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
      <PortalCategoryCard
        v-for="category in categories"
        :key="category.id"
        :category="category"
      />
    </div>
  </main>
</template>
