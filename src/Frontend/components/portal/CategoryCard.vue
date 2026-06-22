<script setup lang="ts">
/**
 * ホームの目的別カテゴリカード。クリックでカテゴリ既定ページへ遷移する。
 * 配下の中分類名（複数セクション時）または配下ページ名（単一セクション時）を
 * チップで列挙し、何ができるカテゴリかを一目で示す。
 */
import { categoryDefaultPath, isMultiSection, type NavCategory } from '~/utils/navigation'

const props = defineProps<{ category: NavCategory }>()

const to = computed(() => categoryDefaultPath(props.category))

// チップ: 複数セクションは中分類名、単一セクションは配下ページ名を表示。
const chips = computed<string[]>(() => {
  if (isMultiSection(props.category)) return props.category.sections.map((s) => s.label)
  return props.category.sections[0]?.links.map((l) => l.label) ?? []
})
</script>

<template>
  <NuxtLink
    :to="to"
    class="group flex flex-col rounded-xl border border-gray-200 bg-white p-5 no-underline shadow-sm transition hover:-translate-y-0.5 hover:border-blue-400 hover:shadow-md"
  >
    <div class="flex items-start gap-3">
      <span
        class="flex h-11 w-11 shrink-0 items-center justify-center rounded-lg bg-blue-50 text-blue-600 transition group-hover:bg-blue-100"
      >
        <NavIcon :name="category.icon" class="h-6 w-6" />
      </span>
      <div class="min-w-0">
        <h2 class="text-base font-bold text-gray-800">{{ category.label }}</h2>
        <p class="mt-0.5 text-sm leading-snug text-gray-500">{{ category.description }}</p>
      </div>
    </div>
    <div v-if="chips.length" class="mt-4 flex flex-wrap gap-1.5">
      <span
        v-for="chip in chips"
        :key="chip"
        class="rounded-full bg-gray-100 px-2.5 py-1 text-xs font-medium text-gray-600"
      >
        {{ chip }}
      </span>
    </div>
  </NuxtLink>
</template>
