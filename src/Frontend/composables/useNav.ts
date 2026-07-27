// ============================================
// ポータル型ナビゲーション Composable
// ============================================
//
// utils/navigation.ts（静的な SoT）に対し、権限（NavGuard）による出し分けと、
// route.path から現在位置（カテゴリ・セクション・パンくず・戻り先）を導出する。
// navigation.ts はランタイム状態を持たず、表示時の絞り込みは本 composable が一元的に行う。
//
// warehouse 版の通知バッジ・倉庫セレクタ・ホーム要約帯は本州には該当データソースが
// 無いため持たない（原則3: 不要な新規コードを書かない）。将来ダッシュボード API を
// 用意した際にバッジ機構を追加できるよう、navigation.ts 側の純関数は汎用のまま残す。

import {
  NAV_CATEGORIES,
  findNavContext,
  navBreadcrumbs,
  findParentPath,
  type NavCategory,
  type NavGuard,
  type NavLink,
  type NavSection,
} from '~/utils/navigation'

export const useNav = () => {
  const route = useRoute()
  const { user } = useAuth()

  /**
   * リンクのアクセス可否。requires 省略時は認証済みの全ユーザ可。
   * 'owner' は工程実績管理権限（processRecordPermission）を要求する
   * （旧 AppNav の「データ移行」表示条件を踏襲）。オーナー権限は 0=なし / 1=あり の 2 値で
   * 非単調スケールを持たないため、判定式は useAuth の isAttendanceAdmin・pages/masters/users.vue と
   * 揃えて `>= 1` に統一する（同一概念の判定式が画面ごとに違うと、権限値が拡張されたときに
   * ナビだけリンクが消える）。
   * 'attendance' は勤怠権限（1=更新可能 / 2=参照のみ）を要求する。0=なし には勤怠カテゴリを出さない
   * （勤怠移植仕様 §6.2）。**勤怠の権限スケールは非単調のため、こちらは >= では判定しない。**
   */
  const canAccess = (requires?: NavGuard): boolean => {
    if (!requires) return true
    if (requires === 'owner') return (user.value?.processRecordPermission ?? 0) >= 1
    if (requires === 'attendance') {
      const p = user.value?.attendancePermission ?? 0
      return p === 1 || p === 2
    }
    return true
  }

  /** 権限でアクセス不可のリンク・空セクション・空カテゴリを除外したナビ構成。 */
  const categories = computed<NavCategory[]>(() => {
    const result: NavCategory[] = []
    for (const category of NAV_CATEGORIES) {
      const sections: NavSection[] = []
      for (const section of category.sections) {
        const links: NavLink[] = section.links.filter((l) => canAccess(l.requires))
        if (links.length > 0) sections.push({ ...section, links })
      }
      if (sections.length > 0) result.push({ ...category, sections })
    }
    return result
  })

  // ------------------------------------------
  // 現在位置（route.path から導出）
  // ------------------------------------------

  const context = computed(() => findNavContext(route.path))
  const currentCategory = computed(() => context.value?.category)
  const currentSection = computed(() => context.value?.section)
  const isLanding = computed(() => context.value?.isLanding ?? false)
  const breadcrumbs = computed(() => navBreadcrumbs(route.path))
  const parentPath = computed(() => findParentPath(route.path))

  return {
    categories,
    currentCategory,
    currentSection,
    isLanding,
    breadcrumbs,
    parentPath,
  }
}
