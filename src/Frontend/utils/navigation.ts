/**
 * ポータル型ナビゲーションのメニュー・ページ構成 SoT（単一定義）。
 *
 * akebono-warehouse のポータル型ナビゲーション（目的ごとにアイコンカードで
 * ドリルダウンし、配下ページをタブで遷移する構成）に倣い、本州（フットウェア/
 * ホームウェア OEM 生産管理）の画面を「ホーム（目的別カード）→（必要なら）
 * 中分類カード → ページ群タブ」の構成へ刷新する。
 *
 * 旧ヘッダ（フラットな横並び 8 リンク）は、業務フローと無関係な並び（マスタ・
 * ユーザ管理が先頭）で直感性に欠けていた。本 SoT ではカテゴリの並び順を生産管理部の
 * 思考導線（商品企画 → 発注 → 生産手配 → マスタ整備 → システム管理）に合わせ、
 * 設定系（マスタ・管理）を末尾に降格する。
 *
 * 階層の深さは内容に追従する:
 * - セクションが 1 つだけのカテゴリ ... カード → タブ（2 階層）。
 *   カードはセクション先頭ページへ遷移し、タブバーでページを切り替える。
 *   （リンクが 1 つだけのセクションではタブバーを出さず、カードから直接ページへ。）
 * - セクションが 2 つ以上のカテゴリ ... カード → カード → タブ（3 階層）。
 *
 * ホームのカード・中分類カード・タブバー・パンくず・戻るボタンは全て本ファイルの
 * 定義から導出する。既存ページの URL は変更しない（ブックマーク・外部リンク・
 * ミドルウェアのパス判定の下位互換を維持する）。カテゴリ/セクションはナビゲーション
 * 表示上のグルーピングであり、ルーティングには影響しない。
 *
 * アイコンは warehouse の lucide-vue-next（Component 直格納）と異なり、本州の既存
 * スタイル（インライン SVG・Tailwind prefix 無し）に合わせ「文字列キー」で持つ。
 * 実体は components/NavIcon.vue が解決する（新規依存を増やさないため）。
 */

/**
 * ナビ表示上のアクセス制御キー。省略時は認証済みの全ユーザが対象。
 * - 'owner': 工程実績管理権限（processRecordPermission === 1）を持つ管理者のみ。
 *   現状はデータ移行（旧システム取込）のみが該当する。
 * - 'attendance': 勤怠権限あり（attendancePermission が 1=更新可能 または 2=参照のみ）。
 *   0=なし のユーザには勤怠カテゴリごと出さない。
 *
 * 実際の判定は composables/useNav.ts の canAccess が一元的に行う（本ファイルは純関数のまま保つ）。
 */
export type NavGuard = 'owner' | 'attendance'

/** ナビ上のページ。セクション内のタブ 1 つに対応する。 */
export interface NavLink {
  /** ルートパス（既存 URL をそのまま使う）。 */
  path: string
  label: string
  /** NavIcon.vue が解決するアイコンキー。 */
  icon: string
  /** アクセス制御キー。省略時は認証済みの全ユーザ可。 */
  requires?: NavGuard
}

/** 中分類セクション。タブバー 1 本・中分類カード 1 枚の単位。 */
export interface NavSection {
  id: string
  label: string
  icon: string
  /** 中分類カードに表示する説明。 */
  description: string
  /** 配下ページ。先頭がセクションの既定ページ（カード・パンくず押下時の遷移先）。 */
  links: NavLink[]
}

/** 目的別カテゴリ。ホームのカード 1 枚の単位。 */
export interface NavCategory {
  id: string
  label: string
  icon: string
  /** ホームカードに表示する目的の説明。 */
  description: string
  /**
   * 複数セクションを持つカテゴリの中分類カード一覧ページのパス。
   * 設定したカテゴリは「カード → カード → タブ」（3 階層）として振る舞う。
   * 単一セクションのカテゴリでは省略し、「カード → タブ」（2 階層）として振る舞う。
   */
  landingPath?: string
  sections: NavSection[]
}

/**
 * 詳細サブルート（メニューに出さない一覧→詳細の子ページ）を、どのリンクの配下として
 * 扱うか。タブのアクティブ判定・パンくず・戻る先の導出に使う。prefix で前方一致する。
 *
 * 大半の子ルート（/products/new・/orders/{id}・/production/instructions/new 等）は
 * 「リンクパス + '/'」の最長前方一致で自動的に親リンク配下になるため、ここに書くのは
 * 親リンクと前方一致しない例外のみ。
 * - 素材構成(BOM) /production/bom/{familyId} は商品（/products）の一部として辿るため、
 *   商品一覧リンクの配下として扱う（パスは /production 配下だが業務上は商品に属する）。
 */
interface DetailRouteRule {
  prefix: string
  linkPath: string
  exclude?: string[]
}

const DETAIL_ROUTE_RULES: DetailRouteRule[] = [
  { prefix: '/production/bom', linkPath: '/products' },
]

/**
 * 詳細サブルート（メニューに出さない子ページ）のパンくず末尾ラベル。
 * 親リンクと完全一致するパス（タブ本体）では使われない（navBreadcrumbs が分岐する）。
 * 先頭から順に test し、最初に一致したラベルを使う（該当なしは「詳細」にフォールバック）。
 */
const DETAIL_LABELS: { test: (path: string) => boolean; label: string }[] = [
  { test: (p) => p === '/products/new', label: '商品新規登録' },
  { test: (p) => p.startsWith('/production/bom/'), label: '素材構成(BOM)' },
  { test: (p) => p.startsWith('/products/'), label: '商品詳細' },
  { test: (p) => p === '/orders/new', label: '発注書作成' },
  { test: (p) => p.startsWith('/orders/'), label: '発注書詳細' },
  { test: (p) => p === '/production/instructions/new', label: '生産指示書作成' },
  { test: (p) => p.startsWith('/production/instructions/'), label: '生産指示書詳細' },
  { test: (p) => p === '/production/material-orders/new', label: '素材発注書作成' },
  { test: (p) => p.startsWith('/production/material-orders/'), label: '素材発注書詳細' },
]

function detailLabel(currentPath: string): string {
  for (const d of DETAIL_LABELS) {
    if (d.test(currentPath)) return d.label
  }
  return '詳細'
}

/**
 * 目的別カテゴリの定義。並び順は生産管理部の業務 E2E（思考導線）に合わせる:
 * 商品企画（何を作るか）→ 発注（完成品を発注）→ 生産（指示・素材手配）→
 * マスタ（前提データ整備）→ システム管理（ユーザ・移行）。
 * 設定系（マスタ・システム管理）は末尾に置き、業務系を優先表示する。
 */
export const NAV_CATEGORIES: NavCategory[] = [
  {
    id: 'products',
    label: '商品',
    icon: 'box',
    description: '商品マスタ（品番）の企画・登録、素材構成(BOM)・仕入単価・画像の管理',
    sections: [
      {
        id: 'products-main',
        label: '商品',
        icon: 'box',
        description: '商品マスタの一覧・登録・詳細',
        links: [
          { path: '/products', label: '商品一覧', icon: 'box' },
        ],
      },
    ],
  },
  {
    id: 'orders',
    label: '発注',
    icon: 'clipboard',
    description: '完成品の発注書を作成し、工場・仕入先向けに出力する',
    sections: [
      {
        id: 'orders-main',
        label: '発注',
        icon: 'clipboard',
        description: '発注書の一覧・作成・出力',
        links: [
          { path: '/orders', label: '発注書一覧', icon: 'clipboard' },
        ],
      },
    ],
  },
  {
    id: 'production',
    label: '生産',
    icon: 'factory',
    description: '生産手配状況の確認、生産指示書・素材発注書の作成と出力',
    sections: [
      {
        id: 'production-main',
        label: '生産',
        icon: 'factory',
        description: '手配状況・生産指示書・素材発注書',
        links: [
          { path: '/production/status', label: '生産手配状況', icon: 'clipboard-check' },
          { path: '/production/instructions', label: '生産指示書', icon: 'file-text' },
          { path: '/production/material-orders', label: '素材発注書', icon: 'layers' },
        ],
      },
    ],
  },
  {
    id: 'sales',
    label: '販売管理',
    icon: 'cart',
    description: '売上・請求・入金・債権（売掛）を管理する',
    sections: [
      {
        id: 'sales-main',
        label: '販売管理',
        icon: 'cart',
        description: '売上・請求・入金・債権',
        links: [
          { path: '/sales', label: '売上', icon: 'cart' },
          { path: '/sales/billing', label: '請求', icon: 'file-text' },
          { path: '/sales/payments', label: '入金', icon: 'coin' },
          { path: '/sales/receivables', label: '債権', icon: 'ledger' },
        ],
      },
    ],
  },
  {
    id: 'shipping',
    label: '出荷',
    icon: 'truck',
    description: 'データ受信・ピッキング・帳票出力・ASN送信',
    sections: [
      {
        id: 'shipping-main',
        label: '出荷',
        icon: 'truck',
        description: '出荷業務一式',
        links: [
          { path: '/shipping/receive', label: 'データ受信', icon: 'inbox' },
          { path: '/shipping/picking', label: 'ピッキングリスト', icon: 'list' },
          { path: '/shipping/reports', label: '帳票出力', icon: 'file-text' },
          { path: '/shipping/asn', label: 'ASN送信', icon: 'send' },
        ],
      },
    ],
  },
  {
    id: 'inventory',
    label: '在庫管理',
    icon: 'boxes',
    description: '入荷・出荷情報、在庫調整・棚卸調整',
    sections: [
      {
        id: 'inventory-main',
        label: '在庫管理',
        icon: 'boxes',
        description: '在庫業務一式',
        links: [
          { path: '/inventory/inbound', label: '入荷情報', icon: 'inbox' },
          { path: '/inventory/outbound', label: '出荷情報', icon: 'truck' },
          { path: '/inventory/adjustment', label: '在庫調整', icon: 'sliders' },
          { path: '/inventory/stocktaking', label: '棚卸調整', icon: 'clipboard-check' },
        ],
      },
    ],
  },
  {
    id: 'analytics',
    label: '分析',
    icon: 'chart',
    description: '商品・発注・生産の状況を KPI で把握する',
    sections: [
      {
        id: 'analytics-main',
        label: '分析',
        icon: 'chart',
        description: 'KPI ダッシュボード',
        links: [
          { path: '/analytics', label: '分析ダッシュボード', icon: 'chart' },
        ],
      },
    ],
  },
  {
    id: 'attendance',
    label: '勤怠',
    icon: 'clock',
    description: '打刻・勤怠集計・休暇',
    sections: [
      {
        id: 'attendance',
        label: '勤怠',
        icon: 'clock',
        description: '打刻とタイムカード、日次/週次/月次の集計・休暇・各種申請',
        // links[0] がセクションの既定ページ（カード押下時の遷移先）。
        // 日常的に開くのは打刻画面のためタイムカードを先頭に置く。
        // /attendance と /attendance/timecard は resolveActiveLinkPath の
        // 「完全一致 → 詳細ルート → 最長前方一致」の順で解決されるため、
        // 並び順に関わらず /attendance が /attendance/timecard を誤って奪うことはない。
        links: [
          { path: '/attendance/timecard', label: 'タイムカード', icon: 'clock', requires: 'attendance' },
          { path: '/attendance', label: '勤怠管理', icon: 'clipboard-check', requires: 'attendance' },
        ],
      },
    ],
  },
  {
    id: 'masters',
    label: 'マスタ',
    icon: 'sliders',
    description: '各種マスタ（サイズ・色・素材・取引先・文書テンプレ 等）の整備',
    sections: [
      {
        id: 'masters-main',
        label: 'マスタ',
        icon: 'sliders',
        description: '各種マスタの一覧・編集',
        links: [
          { path: '/masters', label: 'マスタ一覧', icon: 'sliders' },
        ],
      },
    ],
  },
  {
    id: 'system',
    label: 'システム管理',
    icon: 'shield',
    description: 'ユーザー管理と、旧システムからのデータ移行',
    sections: [
      {
        id: 'system-main',
        label: 'システム管理',
        icon: 'shield',
        description: 'ユーザー管理・データ移行',
        links: [
          { path: '/users', label: 'ユーザー管理', icon: 'users' },
          // データ移行は旧システム取込のため、工程実績管理権限（owner）のみに開放する。
          { path: '/admin/legacy-import', label: 'データ移行', icon: 'database', requires: 'owner' },
        ],
      },
    ],
  },
]

// ============================================
// 導出ヘルパー（全コンポーネントが共有する）
// ============================================

/** カテゴリが複数セクション（カード→カード→タブ）か。 */
export function isMultiSection(category: NavCategory): boolean {
  return category.sections.length > 1
}

/** セクションの既定ページ（中分類カード押下時の遷移先）。 */
export function sectionDefaultPath(section: NavSection): string {
  return section.links[0]?.path ?? '/'
}

/**
 * カテゴリの既定ページ（ホームカード押下時の遷移先）。
 * 複数セクションは landingPath（中分類カード一覧）、単一セクションは先頭ページ。
 */
export function categoryDefaultPath(category: NavCategory): string {
  if (isMultiSection(category) && category.landingPath) return category.landingPath
  return sectionDefaultPath(category.sections[0])
}

/** 全リンクをフラットに走査する内部ヘルパー。 */
function eachLink(fn: (link: NavLink, section: NavSection, category: NavCategory) => void): void {
  for (const category of NAV_CATEGORIES) {
    for (const section of category.sections) {
      for (const link of section.links) fn(link, section, category)
    }
  }
}

/**
 * 現在パスに対応するリンクのパスを解決する。
 * 1) リンクと完全一致 → そのリンク
 * 2) 詳細ルール（DETAIL_ROUTE_RULES）に前方一致 → 対応リンク
 * 3) リンクパスが現在パスの前方一致（`link.path + '/'` 配下）で最長のもの → そのリンク
 *    （`/products` と `/products/new` のような親子衝突は最長一致で解決する）
 * 該当なしは null。
 */
export function resolveActiveLinkPath(currentPath: string): string | null {
  // 1) 完全一致
  let exact: string | null = null
  eachLink((link) => {
    if (link.path === currentPath) exact = link.path
  })
  if (exact) return exact

  // 2) 詳細ルート
  for (const rule of DETAIL_ROUTE_RULES) {
    if (currentPath.startsWith(rule.prefix) && !rule.exclude?.includes(currentPath)) {
      return rule.linkPath
    }
  }

  // 3) 最長前方一致（子ルートを親リンク配下として扱う）
  let best: string | null = null
  eachLink((link) => {
    if (currentPath.startsWith(`${link.path}/`)) {
      if (!best || link.path.length > best.length) best = link.path
    }
  })
  return best
}

/** タブのアクティブ判定（現在パスが解決するリンクと一致するか）。 */
export function isLinkActive(link: NavLink, currentPath: string): boolean {
  return resolveActiveLinkPath(currentPath) === link.path
}

/** パスがいずれかのカテゴリの landing（中分類カード一覧）か。 */
export function findLandingCategory(currentPath: string): NavCategory | undefined {
  return NAV_CATEGORIES.find((c) => c.landingPath === currentPath)
}

export interface NavContext {
  category: NavCategory
  section?: NavSection
  link?: NavLink
  /** 現在パスが landing（中分類カード一覧）か。 */
  isLanding: boolean
}

/** 現在パスのナビ文脈（カテゴリ・セクション・リンク・landing 判定）を返す。 */
export function findNavContext(currentPath: string): NavContext | undefined {
  const landing = findLandingCategory(currentPath)
  if (landing) return { category: landing, isLanding: true }

  const activePath = resolveActiveLinkPath(currentPath)
  if (!activePath) return undefined

  let result: NavContext | undefined
  eachLink((link, section, category) => {
    if (link.path === activePath && !result) {
      result = { category, section, link, isLanding: false }
    }
  })
  return result
}

/** パンくず 1 要素。 */
export interface NavCrumb {
  label: string
  to?: string
  icon?: string
}

/**
 * パンくずを導出する。
 * - ホーム ... [ホーム]
 * - landing ... [ホーム, カテゴリ]
 * - 単一セクションのページ ... [ホーム, カテゴリ(→既定ページ), ページ]
 * - 複数セクションのページ ... [ホーム, カテゴリ(→landing), セクション(→先頭), ページ]
 * - 詳細サブルート ... 末尾に具体ラベルを追加（親リンクまでは上記に従う）
 */
export function navBreadcrumbs(currentPath: string): NavCrumb[] {
  const home: NavCrumb = { label: 'ホーム', to: '/', icon: 'home' }
  if (currentPath === '/') return [home]

  const ctx = findNavContext(currentPath)
  if (!ctx) return [home]

  const crumbs: NavCrumb[] = [home]
  const { category, section, link, isLanding } = ctx

  if (isLanding) {
    crumbs.push({ label: category.label, icon: category.icon })
    return crumbs
  }

  crumbs.push({ label: category.label, to: categoryDefaultPath(category), icon: category.icon })
  if (isMultiSection(category) && section) {
    crumbs.push({ label: section.label, to: sectionDefaultPath(section), icon: section.icon })
  }

  if (link) {
    // 詳細サブルート（リンクパスと不一致）は一覧リンク + 具体ラベルを積む
    if (currentPath !== link.path) {
      crumbs.push({ label: link.label, to: link.path, icon: link.icon })
      crumbs.push({ label: detailLabel(currentPath) })
    } else {
      crumbs.push({ label: link.label, icon: link.icon })
    }
  }

  return crumbs
}

/**
 * 戻るボタンの遷移先（1 つ上の階層）。
 * - ホーム / 非ナビページ ... null（戻る非表示）
 * - landing ... ホーム（/）
 * - 詳細サブルート ... その一覧リンク
 * - 複数セクションのページ ... カテゴリ landing（中分類カード一覧）
 * - 単一セクションのページ ... ホーム（/）
 */
export function findParentPath(currentPath: string): string | null {
  if (currentPath === '/') return null

  const ctx = findNavContext(currentPath)
  if (!ctx) return null

  const { category, link, isLanding } = ctx
  if (isLanding) return '/'

  if (link && currentPath !== link.path) return link.path

  if (isMultiSection(category) && category.landingPath) return category.landingPath
  return '/'
}
