<script setup lang="ts">
/**
 * 業務拡張モジュール（販売管理/出荷/在庫管理）の一覧表示。
 * constants/ops-data.ts の dataset 定義を表形式で表示する。
 * モバイルは横スクロール（原則8）。状態列はバッジ表示。
 * 表示元は DB の対応テーブル（07-ops-data.sql、正規化再設計後）と同内容のサンプル（API 連携は次段階）。
 * ステータス等のコード列 (英 snake_case) は、列定義の codeLabels 対応表を
 * ラベル関数 opsCodeLabel() 経由で日本語ラベルに変換して表示する。
 */
import { OPS_DATASETS, opsCodeLabel } from '~/constants/ops-data'
import type { OpsColumn } from '~/constants/ops-data'

const props = defineProps<{ dataset: string }>()

const ds = computed(() => OPS_DATASETS[props.dataset])

// セルの表示値。codeLabels を持つ列はコード → 日本語ラベルに変換 (それ以外はそのまま)。
const cellText = (row: Record<string, string | number>, c: OpsColumn): string =>
  c.codeLabels ? opsCodeLabel(c.codeLabels, String(row[c.key] ?? '')) : String(row[c.key] ?? '')

// バッジ配色は変換後の日本語ラベルで判定する (従来の見た目を維持)。
const badgeClass = (s: string): string => {
  if (['出荷済', '取込済', '送信済', '完了', '入金済', '消込済', '正常'].includes(s)) return 'bg-green-100 text-green-700'
  if (['エラー', '取消', '督促中'].includes(s)) return 'bg-red-100 text-red-700'
  if (['受信済', '受注', '送信待ち', '未着手', '引当済', 'ピッキング中', '請求済', '一部入金', '発行待ち', '一部消込', '未消込', '滞留'].includes(s)) return 'bg-amber-100 text-amber-700'
  return 'bg-gray-100 text-gray-600'
}
</script>

<template>
  <main class="mx-auto w-full max-w-screen-2xl px-4 py-4">
    <div v-if="ds">
      <div class="mb-4">
        <h1 class="text-xl font-bold text-gray-800">{{ ds.title }}</h1>
        <p class="mt-1 text-sm text-gray-500">{{ ds.note }}</p>
      </div>

      <div class="overflow-x-auto rounded-lg border border-gray-200 bg-white shadow-sm">
        <table class="w-full text-sm">
          <thead class="border-b border-gray-200 bg-gray-50">
            <tr>
              <th
                v-for="c in ds.columns"
                :key="c.key"
                class="whitespace-nowrap px-3 py-2 font-medium text-gray-600"
                :class="c.align === 'right' ? 'text-right' : 'text-left'"
              >
                {{ c.label }}
              </th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="(row, i) in ds.rows"
              :key="i"
              class="border-b border-gray-100 last:border-0 hover:bg-gray-50"
            >
              <td
                v-for="c in ds.columns"
                :key="c.key"
                class="whitespace-nowrap px-3 py-2"
                :class="c.align === 'right' ? 'text-right font-mono text-gray-700' : 'text-gray-700'"
              >
                <span
                  v-if="c.key === 'status'"
                  class="rounded-full px-2 py-0.5 text-xs font-medium"
                  :class="badgeClass(cellText(row, c))"
                >
                  {{ cellText(row, c) }}
                </span>
                <template v-else>{{ cellText(row, c) }}</template>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <p class="mt-3 text-xs text-gray-400">
        表示は DB テーブル「<span class="font-mono">{{ ds.table }}</span>」のサンプルデータです。
      </p>
    </div>
    <div v-else class="py-20 text-center text-sm text-gray-400">
      データ定義が見つかりません（dataset: {{ dataset }}）。
    </div>
  </main>
</template>
