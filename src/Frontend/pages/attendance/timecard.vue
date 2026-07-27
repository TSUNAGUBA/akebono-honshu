<script setup lang="ts">
/**
 * タイムカード（本人のみ・勤怠移植仕様 §6.4）。
 * 打刻カード（出勤/退勤/休憩開始/休憩終了）と、期間指定の出退勤一覧を 1 画面に置く。
 *
 * データ経路:
 * - 当日の状態・打刻列 ... `/attendance/state`（useAttendance のキャッシュが SoT。打刻後は破棄→再取得）
 * - 出退勤一覧 ......... 月サマリからの射影（`rangeSummaries`）。日別 API を叩かないことで
 *                        60h 超残業の月内累計を保つ（§6.3 キャッシュ規約）。
 *
 * 全員のタイムカード（オーナー向け）は /attendance の「全員のタイムカード」タブ側にあり、本画面は本人専用。
 */
import type { DaySummary } from '~/composables/useAttendance'

const { user, canUseAttendance } = useAuth()
const { rangeSummaries } = useAttendance()

// 打刻カード（時計・状態・4 ボタン・本日の打刻）は共有コンポーネント
// <AttendancePunchCard> が担う（ヘッダのモーダルと共用・原則3）。本画面は打刻成功の
// `punched` を受けて出退勤一覧を取り直すだけ。

// ------------------------------------------
// 出退勤の一覧（期間フィルタ）
// ------------------------------------------

// 初期値は直近 7 日。UTC 由来の前日ずれを防ぐため必ず todayJst() 系を使う。
const from = ref(todayJstPlusDays(-6))
const to = ref(todayJst())

const days = ref<DaySummary[]>([])
// 初期値は true。false で始めると、初回の取得が終わるまでの間だけ
// 「この期間の打刻がありません」という偽の空状態が一瞬出る（reloadRange の finally で必ず false に戻る）。
const listLoading = ref(true)
const listError = ref('')
/** 期間上限（62 日）を超えて丸めたか。true の間は警告を出す。 */
const rangeClamped = ref(false)

/**
 * 実際に問い合わせる期間。
 * - `from > to` は入れ替えて解釈する
 * - 上限 `TIMECARD_RANGE_MAX_DAYS`（62 日・両端含む）を超えたら「直近 62 日」に丸める
 *   （サーバも同値でガードし 422 を返す。SoT は AttendanceService.TimecardRangeMaxDays）
 */
const effectiveRange = computed(() => {
  const start = from.value <= to.value ? from.value : to.value
  const end = from.value <= to.value ? to.value : from.value
  if (diffBizDays(start, end) + 1 > TIMECARD_RANGE_MAX_DAYS) {
    return { start: addBizDays(end, -(TIMECARD_RANGE_MAX_DAYS - 1)), end, clamped: true }
  }
  return { start, end, clamped: false }
})

const reloadRange = async () => {
  const { start, end, clamped } = effectiveRange.value
  rangeClamped.value = clamped
  listLoading.value = true
  listError.value = ''
  try {
    days.value = await rangeSummaries(start, end)
  } catch (e) {
    listError.value = getApiErrorMessage(e, '出退勤の取得に失敗しました')
    days.value = []
  } finally {
    listLoading.value = false
  }
}

interface TimecardDisplayRow {
  date: string
  inAt: string | null
  outAt: string | null
  workMinutes: number
  breakMinutes: number
  breakShortage: number
}

/** 打刻のある日のみを新しい順に並べる（§6.4: 打刻のある日のみ表示）。 */
const rows = computed<TimecardDisplayRow[]>(() =>
  days.value
    .filter((d) => d.punches.length > 0)
    .map((d) => ({
      date: d.date.slice(0, 10),
      // 出勤時間 = 有効打刻の最初の In / 退勤時間 = 最後の Out（§5.1 TimecardRow と同じ定義）。
      inAt: d.punches.find((p) => p.kind === 'in')?.at ?? null,
      outAt: [...d.punches].reverse().find((p) => p.kind === 'out')?.at ?? null,
      workMinutes: d.workMinutes,
      breakMinutes: d.breakMinutes,
      breakShortage: d.breakShortage,
    }))
    .sort((a, b) => (a.date < b.date ? 1 : a.date > b.date ? -1 : 0)),
)

/** 出勤日数は「実労働が 1 分でもある日」で数える（サーバの MonthSummary.workDays と同じ定義）。 */
const workDays = computed(() => days.value.filter((d) => d.workMinutes > 0).length)
const totalWorkMinutes = computed(() => days.value.reduce((sum, d) => sum + d.workMinutes, 0))
const totalBreakMinutes = computed(() => days.value.reduce((sum, d) => sum + d.breakMinutes, 0))

const resetRange = () => {
  from.value = todayJstPlusDays(-6)
  to.value = todayJst()
}

// 期間の変更で自動再取得する（月サマリはキャッシュ済みのため、同月内の変更では通信が発生しない）。
// date 入力を消した直後（空文字）は再取得せず、直前の表示を保つ。
watch([from, to], () => {
  if (!canUseAttendance.value || !from.value || !to.value) return
  void reloadRange()
})

// ------------------------------------------
// ライフサイクル
// ------------------------------------------

onMounted(async () => {
  if (!canUseAttendance.value) {
    // 一覧を取りにいかないので、初期値 true のローディングを閉じる
    // （権限なしの分岐で一覧自体は描画しないが、フラグを立てたまま抜けると
    //   将来テンプレートを触ったときに読み込み中のまま固まる）。
    listLoading.value = false
    return
  }
  await reloadRange()
})
</script>

<template>
  <main class="mx-auto w-full max-w-screen-2xl px-3 py-3">
    <header class="mb-3">
      <h1 class="text-xl font-bold text-gray-800">タイムカード</h1>
      <p class="mt-1 text-sm text-gray-500">
        {{ user?.displayName ?? '' }} さんの打刻と出退勤・労働時間（本人のみ）
      </p>
    </header>

    <!-- 権限なし（ナビには出ないが直接 URL で来られるためガードする） -->
    <div
      v-if="!canUseAttendance"
      class="rounded-lg border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-800"
    >
      勤怠機能の利用権限がありません。必要な場合は管理者に「勤怠権限」の付与を依頼してください。
    </div>

    <template v-else>
      <!-- 打刻カード（ヘッダのタイムカードモーダルと同じ共有ウィジェット・原則3）。
           打刻が成立したら当日集計に効くため、出退勤一覧を取り直す。 -->
      <section class="mb-4 rounded-lg border border-gray-200 bg-white p-3 shadow-sm">
        <AttendancePunchCard @punched="reloadRange" />
      </section>

      <!-- 期間フィルタ -->
      <FilterPanel title="期間" storage-key="filters:attendance-timecard">
        <template #actions>
          <button
            type="button"
            class="rounded-md border border-gray-300 bg-white px-3 py-1 text-xs text-gray-600 hover:bg-gray-50"
            @click="resetRange"
          >直近 7 日に戻す</button>
        </template>
        <div class="grid grid-cols-1 gap-x-3 gap-y-2 text-sm sm:grid-cols-2 lg:grid-cols-4">
          <label class="flex flex-col gap-1">
            <span class="font-medium">日付（から）</span>
            <input
              v-model="from"
              type="date"
              class="rounded-md border border-gray-300 px-2.5 py-1.5 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
            />
          </label>
          <label class="flex flex-col gap-1">
            <span class="font-medium">日付（まで）</span>
            <input
              v-model="to"
              type="date"
              class="rounded-md border border-gray-300 px-2.5 py-1.5 focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"
            />
          </label>
        </div>
      </FilterPanel>

      <div v-if="rangeClamped" class="mb-3 rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-800">
        期間が長いため直近 {{ TIMECARD_RANGE_MAX_DAYS }} 日分のみ表示しています。期間を狭めてください。
      </div>
      <div v-if="listError" class="mb-3 whitespace-pre-line rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
        {{ listError }}
      </div>

      <!-- サマリ。取得に失敗しているときは描画しない (勤怠は法定記録であり、
           0 値を本物の集計と誤読されるコストが高いため。原則4) -->
      <div v-if="!listError" class="mb-3 grid grid-cols-2 gap-3 sm:grid-cols-3">
        <div class="rounded-lg border border-gray-200 bg-white p-3 shadow-sm">
          <div class="text-xs text-gray-500">出勤日数</div>
          <div class="mt-0.5 text-2xl font-bold text-gray-800">{{ workDays }}<span class="ml-0.5 text-sm font-normal text-gray-500">日</span></div>
        </div>
        <div class="rounded-lg border border-gray-200 bg-white p-3 shadow-sm">
          <div class="text-xs text-gray-500">実労働合計</div>
          <div class="mt-0.5 text-2xl font-bold tabular-nums text-gray-800">{{ fmtMinutes(totalWorkMinutes) }}</div>
          <div class="text-xs text-gray-400">{{ fmtHours(totalWorkMinutes) }}</div>
        </div>
        <div class="rounded-lg border border-gray-200 bg-white p-3 shadow-sm">
          <div class="text-xs text-gray-500">休憩合計</div>
          <div class="mt-0.5 text-2xl font-bold tabular-nums text-gray-800">{{ fmtMinutes(totalBreakMinutes) }}</div>
        </div>
      </div>

      <div v-if="listLoading" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500">
        読み込み中…
      </div>
      <!-- 空状態もサマリと同じ扱いにする: 取得に失敗しているときは「打刻がありません」と断定しない
           （エラー帯と矛盾する断定を出さない。勤怠は法定記録であり誤読のコストが高い・原則4） -->
      <div v-else-if="!listError && rows.length === 0" class="rounded-lg border border-gray-200 bg-white p-8 text-center text-gray-500">
        この期間の打刻がありません
      </div>

      <template v-else-if="!listError">
        <!-- PC: テーブル -->
        <div class="hidden overflow-x-auto rounded-lg border border-gray-200 bg-white shadow-sm md:block">
          <table class="w-full text-sm">
            <thead class="border-b border-gray-200 bg-gray-50 text-xs uppercase text-gray-600">
              <tr>
                <th class="px-3 py-2 text-left">日付</th>
                <th class="px-3 py-2 text-left">出勤時間</th>
                <th class="px-3 py-2 text-left">退勤時間</th>
                <th class="px-3 py-2 text-right">休憩</th>
                <th class="px-3 py-2 text-right">労働時間</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="r in rows" :key="r.date" class="border-b border-gray-100 last:border-0">
                <td class="whitespace-nowrap px-3 py-2">
                  <span class="text-gray-800">{{ r.date }}</span>
                  <span class="ml-1 text-xs text-gray-400">{{ fmtBizDate(r.date) }}</span>
                </td>
                <td class="whitespace-nowrap px-3 py-2 tabular-nums">{{ fmtJstHm(r.inAt) }}</td>
                <td class="whitespace-nowrap px-3 py-2 tabular-nums">{{ fmtJstHm(r.outAt) }}</td>
                <td class="whitespace-nowrap px-3 py-2 text-right tabular-nums">
                  {{ fmtMinutes(r.breakMinutes) }}
                  <span v-if="r.breakShortage > 0" class="ml-1 rounded bg-red-100 px-1.5 py-0.5 text-xs text-red-700">
                    不足 {{ fmtMinutes(r.breakShortage) }}
                  </span>
                </td>
                <td class="whitespace-nowrap px-3 py-2 text-right font-medium tabular-nums text-gray-800">{{ fmtMinutes(r.workMinutes) }}</td>
              </tr>
            </tbody>
          </table>
        </div>

        <!-- モバイル: カード型（原則8） -->
        <ul class="space-y-2 md:hidden">
          <li
            v-for="r in rows"
            :key="r.date"
            class="rounded-lg border border-gray-200 bg-white p-3 shadow-sm"
          >
            <div class="flex items-baseline justify-between">
              <div>
                <span class="font-medium text-gray-800">{{ r.date }}</span>
                <span class="ml-1 text-xs text-gray-400">{{ fmtBizDate(r.date) }}</span>
              </div>
              <span class="font-mono text-lg font-bold tabular-nums text-gray-800">{{ fmtMinutes(r.workMinutes) }}</span>
            </div>
            <dl class="mt-2 grid grid-cols-3 gap-2 text-sm">
              <div>
                <dt class="text-xs text-gray-500">出勤</dt>
                <dd class="tabular-nums text-gray-800">{{ fmtJstHm(r.inAt) }}</dd>
              </div>
              <div>
                <dt class="text-xs text-gray-500">退勤</dt>
                <dd class="tabular-nums text-gray-800">{{ fmtJstHm(r.outAt) }}</dd>
              </div>
              <div>
                <dt class="text-xs text-gray-500">休憩</dt>
                <dd class="tabular-nums text-gray-800">{{ fmtMinutes(r.breakMinutes) }}</dd>
              </div>
            </dl>
            <p v-if="r.breakShortage > 0" class="mt-1.5 rounded bg-red-50 px-2 py-1 text-xs text-red-700">
              休憩が {{ fmtMinutes(r.breakShortage) }} 不足しています（労基法 34 条）
            </p>
          </li>
        </ul>
      </template>
    </template>
  </main>
</template>
