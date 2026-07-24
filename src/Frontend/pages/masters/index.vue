<script setup lang="ts">
// マスタ一覧 (Part5)。数が多いためカテゴリチップで切り替え、選択カテゴリのマスタのみ表示する。
import { findMasterBySlug } from '~/utils/master-definitions'

interface MasterCard { key: string; label: string; description?: string; to: string }
interface Category { key: string; label: string; cards: MasterCard[] }

// 汎用マスタ (master-definitions) を slug からカードに変換する。未定義 slug は null。
const cardOf = (slug: string): MasterCard | null => {
  const m = findMasterBySlug(slug)
  return m ? { key: m.slug, label: m.label, description: m.description, to: `/masters/${m.slug}` } : null
}
const cards = (slugs: string[]): MasterCard[] =>
  slugs.map(cardOf).filter((c): c is MasterCard => c !== null)

// カテゴリ定義。汎用マスタ + 専用ページ (為替/ドレー代/利用者) を混在して束ねる。
const categories: Category[] = [
  {
    key: 'product',
    label: '商品',
    cards: cards(['brands', 'product-types', 'product-seasons', 'product-groups', 'functions', 'colors', 'sizes', 'materials', 'material-classifications']),
  },
  {
    key: 'partner',
    label: '取引先・工場',
    cards: [
      ...cards(['suppliers', 'factories', 'countries', 'delivery-destinations']),
      { key: 'drayage', label: 'ドレー代', description: '仕入先ごとのドレー代の管理。商品⑤仕入単価に自動反映。', to: '/masters/drayage' },
    ],
  },
  {
    key: 'price',
    label: '価格・税',
    cards: [
      ...cards(['currencies', 'tax-rates']),
      { key: 'exchange-rates', label: '為替マスタ', description: '年月×通貨ごとの対円レート。商品⑤の円換算に使用。', to: '/masters/exchange-rates' },
    ],
  },
  {
    key: 'order',
    label: '発注・帳票',
    cards: cards(['departments', 'warehouses', 'document-template-purchases', 'document-template-confirmations', 'document-text-purchases']),
  },
  {
    key: 'system',
    label: 'システム',
    cards: [
      { key: 'users', label: '利用者マスタ', description: '利用者と、各ページ・機能の閲覧/操作権限の管理。', to: '/masters/users' },
    ],
  },
]

const selected = ref(categories[0].key)
const currentCards = computed(() => categories.find((c) => c.key === selected.value)?.cards ?? [])
</script>

<template>
  <main class="mx-auto max-w-screen-2xl px-3 py-3">
    <header class="mb-3">
      <h1 class="text-2xl font-bold">マスタ一覧</h1>
      <p class="mt-1 text-sm text-gray-500">
        カテゴリを選択して、そのカテゴリのマスタメンテナンスを開きます。
      </p>
    </header>

    <!-- カテゴリチップ (アプリ全体のトグル/バッジと統一したチップ表現、原則8 レスポンシブで折返し) -->
    <div class="mb-4 flex flex-wrap gap-2">
      <button
        v-for="c in categories"
        :key="c.key"
        type="button"
        class="rounded-full border px-4 py-1.5 text-sm font-medium transition-colors"
        :class="selected === c.key
          ? 'border-blue-600 bg-blue-600 text-white'
          : 'border-gray-300 bg-white text-gray-700 hover:border-blue-400 hover:bg-blue-50'"
        @click="selected = c.key"
      >
        {{ c.label }}
        <span class="ml-1 text-xs opacity-70">{{ c.cards.length }}</span>
      </button>
    </div>

    <div class="grid grid-cols-1 gap-3 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4">
      <NuxtLink
        v-for="m in currentCards"
        :key="m.key"
        :to="m.to"
        class="rounded-lg border border-gray-200 bg-white p-2.5 shadow-sm transition hover:border-blue-500 hover:shadow-md"
      >
        <div class="font-semibold text-gray-900">{{ m.label }}</div>
        <div class="mt-1 text-xs text-gray-500">{{ m.to.replace('/masters/', '') }}</div>
        <p v-if="m.description" class="mt-2 text-xs text-gray-600">{{ m.description }}</p>
      </NuxtLink>
    </div>
  </main>
</template>
