<script setup lang="ts">
import { masterDefinitions } from '~/utils/master-definitions'
</script>

<template>
  <main class="mx-auto max-w-7xl px-4 py-8">
    <header class="mb-6">
      <h1 class="text-2xl font-bold">マスタ一覧</h1>
      <p class="mt-1 text-sm text-gray-500">
        17 マスタの CRUD 操作 (M-01 / M-02 共通テンプレート + M-04 仕入先 / M-05 連絡文書 個別対応) + 為替マスタ (§2f)
      </p>
    </header>

    <div class="grid grid-cols-1 gap-3 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4">
      <NuxtLink
        v-for="m in masterDefinitions"
        :key="m.slug"
        :to="`/masters/${m.slug}`"
        class="rounded-lg border border-gray-200 bg-white p-4 shadow-sm transition hover:border-blue-500 hover:shadow-md"
      >
        <div class="font-semibold text-gray-900">{{ m.label }}</div>
        <div class="mt-1 text-xs text-gray-500">{{ m.slug }}</div>
        <p v-if="m.description" class="mt-2 text-xs text-gray-600">{{ m.description }}</p>
      </NuxtLink>

      <!-- 為替マスタ (§2f、bespoke master。年月×通貨の複合キーのため専用ページ) -->
      <NuxtLink
        to="/masters/exchange-rates"
        class="rounded-lg border border-gray-200 bg-white p-4 shadow-sm transition hover:border-blue-500 hover:shadow-md"
      >
        <div class="font-semibold text-gray-900">為替マスタ</div>
        <div class="mt-1 text-xs text-gray-500">exchange-rates</div>
        <p class="mt-2 text-xs text-gray-600">年月×通貨ごとの対円レート。商品⑤の円換算に使用。</p>
      </NuxtLink>
    </div>

    <p class="mt-4 text-xs text-gray-400">
      ※ ドレー代設定は「仕入先 (工場)」マスタの各仕入先に「ドレー代」として登録します (§2i、仕入先ごと)。
    </p>
  </main>
</template>
