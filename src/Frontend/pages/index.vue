<script setup lang="ts">
// / アクセス時のリダイレクトは middleware/auth.global.ts で処理されるため、
// 本ページは middleware 通過中の loading 表示のみ。
// onMounted で navigateTo を呼ぶと、URL 直接アクセス時の Vue Router 初期化
// タイミングで誤動作する可能性があるため使わない。
</script>

<template>
  <div class="flex min-h-screen items-center justify-center">
    <p class="text-gray-500">読み込み中…</p>
  </div>
</template>
