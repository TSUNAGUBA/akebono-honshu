<script setup lang="ts">
/**
 * ポータル型レイアウト（旧 AppNav の刷新）。
 * 固定ヘッダ（ブランド・パンくず・戻る・ユーザ）＋ セクションタブバー ＋ メイン の構成。
 * メニュー構成は utils/navigation.ts を SoT とし、現在位置は route.path から導出する。
 * 各ページは自前の <main> でコンテンツ幅・余白を持つため、本レイアウトは chrome のみ提供する。
 */
const { currentSection } = useNav()
</script>

<template>
  <div>
    <div class="sticky top-0 z-30">
      <PortalHeader />
      <PortalTabBar
        v-if="currentSection && currentSection.links.length > 1"
        :section="currentSection"
      />
    </div>
    <slot />
  </div>
</template>
