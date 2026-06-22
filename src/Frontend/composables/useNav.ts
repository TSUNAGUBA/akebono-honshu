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
   * 'owner' は工程実績管理権限（processRecordPermission === 1）を要求する
   * （旧 AppNav の「データ移行」表示条件を踏襲）。
   */
  const canAccess = (requires?: NavGuard): boolean => {
    if (!requires) return true
    if (requires === 'owner') return (user.value?.processRecordPermission ?? 0) === 1
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
