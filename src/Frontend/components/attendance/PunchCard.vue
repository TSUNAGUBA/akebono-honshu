<script setup lang="ts">
/**
 * 打刻カード（出勤 / 退勤 / 休憩開始 / 休憩終了）。
 * タイムカード画面（`/attendance/timecard`）とヘッダのタイムカードモーダルで共用する
 * （akebono-office の WidgetsPunchClock に相当。**状態機械・打刻 API は useAttendance が SoT**・原則3）。
 *
 * 打刻が成功したら `punched` を emit する。親（タイムカード画面）はこれを受けて
 * 自分の出退勤一覧を再取得する。ヘッダのモーダルは特に何もしない（当日状態は本カードが持つ）。
 *
 * 原則4（取得できていないものを断定表示しない）:
 * - 当日状態の取得に失敗したら `stateError` に残し、状態バッジは「状態不明」、本日の打刻は
 *   「取得できませんでした」と出す。既定値 'before' を根拠に打刻ボタンを活性化して、実際は
 *   勤務中の利用者にサーバ 409 を踏ませない。
 */
import type { PunchDto } from '~/composables/useAttendance'

const emit = defineEmits<{ punched: [] }>()

const { user, canPunch, canUseAttendance } = useAuth()
const { punchState, todayPunches, loadState, punch } = useAttendance()

// 業務時刻は JST ウォールクロック（閲覧環境の TZ に依存させない）。onBeforeUnmount で必ず止める。
const now = ref(nowJstHms())
let clockTimer: ReturnType<typeof setInterval> | null = null

const stateLoading = ref(true)
const punching = ref(false)
const punchError = ref('')
const punchSuccess = ref('')
/** 当日状態そのものの取得エラー（打刻操作の失敗 `punchError` とは別物・原則4）。 */
const stateError = ref('')

/**
 * 状態機械（§4.8）に沿ったボタンの活性判定。サーバも同じ判定で 409 を返す。
 * 状態を取得できていないとき（`stateError`）はどのボタンも押させない。
 */
const canPunchIn = computed(() => canPunch.value && !stateError.value && punchState.value === 'before')
const canPunchOut = computed(() => canPunch.value && !stateError.value && punchState.value === 'working')
const canBreakStart = computed(() => canPunch.value && !stateError.value && punchState.value === 'working')
const canBreakEnd = computed(() => canPunch.value && !stateError.value && punchState.value === 'breaking')

/** 状態バッジ。取得できていないときは「未出勤」と断定しない（原則4）。 */
const stateBadgeLabel = computed(() => {
  if (stateLoading.value) return '読み込み中…'
  if (stateError.value) return '状態不明'
  return PUNCH_STATE_LABELS[punchState.value]
})
const stateBadgeClass = computed(() =>
  stateError.value ? 'bg-red-100 text-red-700' : PUNCH_STATE_CLASSES[punchState.value])

/**
 * 打刻状態の取得（初回・失敗後の再試行で共通）。
 * 失敗しても投げず `stateError` に残す（カードの他の情報表示は続ける・原則4）。
 */
const reloadState = async () => {
  stateLoading.value = true
  try {
    await loadState(true)
    stateError.value = ''
  } catch (e) {
    stateError.value = getApiErrorMessage(e, '打刻状態の取得に失敗しました')
  } finally {
    stateLoading.value = false
  }
}

const doPunch = async (kind: PunchDto['kind']) => {
  punchError.value = ''
  punchSuccess.value = ''
  punching.value = true
  try {
    await punch(kind)
    punchSuccess.value = `${PUNCH_KIND_LABELS[kind]}を記録しました（${nowJstHms()}）`
    // 親（タイムカード画面）の一覧などを再取得させる。emit の購読側が失敗しても
    // 打刻自体は成立しているため、ここでは購読側の失敗を打刻失敗として扱わない（原則4）。
    emit('punched')
  } catch (e) {
    punchError.value = getApiErrorMessage(e, '打刻に失敗しました')
  } finally {
    punching.value = false
  }
}

onMounted(async () => {
  // 1 秒ごとの時計。
  clockTimer = setInterval(() => { now.value = nowJstHms() }, 1000)
  // 権限なしでも（親がガードするため通常は到達しないが）状態取得はしない。
  // フラグを立てたまま抜けると「読み込み中…」で固まるため必ず閉じる。
  if (!canUseAttendance.value) {
    stateLoading.value = false
    return
  }
  await reloadState()
})

onBeforeUnmount(() => {
  if (clockTimer !== null) {
    clearInterval(clockTimer)
    clockTimer = null
  }
})
</script>

<template>
  <div>
    <div class="flex flex-wrap items-center gap-x-4 gap-y-2">
      <div>
        <div class="text-xs text-gray-500">{{ todayJst() }}（JST）</div>
        <div class="font-mono text-3xl font-bold tabular-nums text-gray-800">{{ now }}</div>
      </div>
      <span
        class="inline-block rounded-full px-3 py-1 text-sm font-medium"
        :class="stateBadgeClass"
      >
        {{ stateBadgeLabel }}
      </span>
    </div>

    <!-- 状態を取得できていないことの告知と復帰導線。打刻ボタンは無効化しているため、
         ここから再取得できないと画面が詰む（原則1: 手動手順に頼らせない） -->
    <div v-if="stateError" class="mt-2 rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
      <p class="whitespace-pre-line">{{ stateError }}</p>
      <p class="mt-1 text-xs">
        現在の打刻状態が分からないため、打刻ボタンを無効にしています（誤った二重打刻を防ぐため）。
      </p>
      <!-- 指で押す導線のため 44px 以上のタップ領域を確保する（原則8） -->
      <button
        type="button"
        :disabled="stateLoading"
        class="inline-flex min-h-[44px] items-center text-xs font-semibold underline disabled:opacity-50"
        @click="reloadState"
      >打刻状態を再取得する</button>
    </div>
    <div v-if="punchError" class="mt-2 whitespace-pre-line rounded border border-red-300 bg-red-50 px-3 py-2 text-sm text-red-700">
      {{ punchError }}
    </div>
    <div v-if="punchSuccess" class="mt-2 rounded border border-green-300 bg-green-50 px-3 py-2 text-sm text-green-700">
      {{ punchSuccess }}
    </div>

    <!-- 打刻できない理由の明示（打刻対象外 / 参照のみ） -->
    <div
      v-if="user && !user.punchRequired"
      class="mt-2 rounded border border-gray-300 bg-gray-50 px-3 py-2 text-sm text-gray-600"
    >
      打刻対象外です（役員・外注等）。集計の参照のみ利用できます。
    </div>
    <div
      v-else-if="!canPunch"
      class="mt-2 rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-800"
    >
      勤怠権限が「参照のみ」のため打刻できません。
    </div>

    <!-- ボタン 4 つ。モバイルは 2 列、PC は横並び。 -->
    <div class="mt-3 grid grid-cols-2 gap-2 sm:flex sm:flex-wrap">
      <button
        type="button"
        :disabled="!canPunchIn || punching"
        class="rounded-md bg-blue-600 px-5 py-2.5 text-sm font-semibold text-white shadow-sm hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-40"
        @click="doPunch('in')"
      >出勤</button>
      <button
        type="button"
        :disabled="!canPunchOut || punching"
        class="rounded-md bg-gray-700 px-5 py-2.5 text-sm font-semibold text-white shadow-sm hover:bg-gray-800 disabled:cursor-not-allowed disabled:opacity-40"
        @click="doPunch('out')"
      >退勤</button>
      <button
        type="button"
        :disabled="!canBreakStart || punching"
        class="rounded-md border border-amber-400 bg-amber-50 px-5 py-2.5 text-sm font-semibold text-amber-800 shadow-sm hover:bg-amber-100 disabled:cursor-not-allowed disabled:opacity-40"
        @click="doPunch('breakStart')"
      >休憩開始</button>
      <button
        type="button"
        :disabled="!canBreakEnd || punching"
        class="rounded-md border border-amber-400 bg-amber-50 px-5 py-2.5 text-sm font-semibold text-amber-800 shadow-sm hover:bg-amber-100 disabled:cursor-not-allowed disabled:opacity-40"
        @click="doPunch('breakEnd')"
      >休憩終了</button>
    </div>

    <!-- 本日の打刻 -->
    <div class="mt-3 border-t border-gray-100 pt-2">
      <h2 class="text-xs font-semibold text-gray-600">本日の打刻</h2>
      <ul v-if="todayPunches.length > 0" class="mt-1 flex flex-wrap gap-x-4 gap-y-1 text-sm text-gray-700">
        <li v-for="p in todayPunches" :key="p.id" class="tabular-nums">
          {{ PUNCH_KIND_LABELS[p.kind] }} {{ fmtJstHm(p.at) }}
          <span v-if="p.source === 'fix'" class="ml-1 rounded bg-blue-100 px-1.5 py-0.5 text-xs text-blue-700">修正反映</span>
        </li>
      </ul>
      <!-- 初回取得が終わるまでは「打刻がありません」と断定しない（読み込み中 → 取得失敗 → 確定値 の順・原則4） -->
      <p v-else-if="stateLoading" class="mt-1 text-sm text-gray-500">読み込み中…</p>
      <p v-else-if="stateError" class="mt-1 text-sm text-amber-700">本日の打刻を取得できませんでした。</p>
      <p v-else class="mt-1 text-sm text-gray-500">まだ打刻がありません。</p>
      <!-- 誤操作からの復帰導線を明示する（打刻は記録系のため取消ではなく修正申請で戻す）。 -->
      <p class="mt-1.5 text-xs text-gray-500">
        打刻を間違えた場合は
        <NuxtLink to="/attendance?tab=daily" class="text-blue-600 hover:underline">勤怠管理の日次タブ</NuxtLink>
        から「打刻修正を申請」できます（承認されると打刻に反映されます）。
      </p>
    </div>
  </div>
</template>
