# Phase 7 Iteration 1 着手前タスク

> **作成日:** 2026-05-19
> **対象:** Phase 7 (MVP 構築) Iteration 1 着手前に解消必須の事前タスク
> **由来:** Phase 6 独立監査指摘 (F-21 Reka UI 衝突検証 / H-3 Excel サンプル調達)
> **完了基準:** 両タスクが「OK」判定となるまで Phase 7 本実装に着手しない

---

## T-1: Reka UI ショートカット衝突マトリクス (F-21 対応)

### 目的

既存システムの F-key 運用 (F9 サイズ展開 / F10 登録 / F12 閉じる) を Web UI で再現する際、Reka UI 標準キーバインドおよび **ブラウザ / OS レベルの予約キー** との衝突を事前に洗い出し、ショートカット設計の方針を確定する。

### 衝突マトリクス (暫定、Phase 7 Iteration 1 冒頭で裏取り更新必須)

| F-key | 既存システム業務用途 | ブラウザ予約 (Chrome/Edge/Firefox) | OS 予約 (Windows/macOS) | Reka UI / WAI-ARIA 標準 (一般則) | 結論 (暫定) |
|---|---|---|---|---|---|
| **F1** | 未使用 | ヘルプ (Chrome=Chrome help, Firefox=Firefox help) | – | 未使用 | ⚠️ ブラウザ予約あり、Web アプリで `preventDefault` 可能だがユーザ混乱の可能性 |
| **F2** | 未使用 | – | – | **編集モード突入** (DataGrid/Listbox 慣例、APG 一部パターンで定義) | ✅ 業務用途で空き、UI ライブラリ慣例と整合 |
| **F5** | 未使用 | リロード | – | 未使用 | ❌ ブラウザ予約強、`preventDefault` 不可 (Chrome 仕様) |
| **F9** | **サイズ展開 (P-03)** | 未使用 | – | 未使用 | ✅ 衝突なし、Web で再現可能 |
| **F10** | **登録 (P-01〜P-03 / O-01〜O-02)** | メニューバーフォーカス (Firefox 強、Chrome 弱) | – | 未使用 | ⚠️ Firefox でメニューバー起動、`preventDefault` で抑制可能だが Firefox 一部バージョンで残存 |
| **F11** | 未使用 | フルスクリーン | – | 未使用 | ❌ ブラウザ予約強、`preventDefault` 不可 (全主要ブラウザ) |
| **F12** | **閉じる (汎用)** | **DevTools 起動** | – | 未使用 | ❌ **重大衝突**: ブラウザ予約強、`preventDefault` 不可 (全主要ブラウザ)、ユーザが誤って DevTools 起動する誤操作リスク |

### 重大衝突: F12「閉じる」

**問題:**
- 既存システムでは F12 = 「閉じる」(モーダル/画面終了) のメンタルモデルが確立
- Web ブラウザでは F12 = DevTools 起動が **OS/ブラウザレベルで予約済**、JavaScript `preventDefault` で抑制不可
- ユーザが Web 版で F12 を押すと DevTools が開き、業務操作と無関係な画面が表示される → 重大な業務混乱

**対応方針 (推奨):**

| 案 | 内容 | 評価 |
|---|---|---|
| **A. F12 → Esc にリマップ** (推奨) | 「閉じる」操作を Esc キーに変更、業務メンタルモデルを Web 標準に寄せる | ✅ Reka UI Dialog/Combobox 標準で Esc = 閉じる、業務マニュアルで再教育コストは発生するが Web 移行時の標準化として妥当 |
| B. F12 → Ctrl+W or Ctrl+F12 にリマップ | Modifier 付加で DevTools 予約回避 | ⚠️ Ctrl+W はブラウザタブ閉じる予約、Ctrl+F12 は記憶コスト高 |
| C. 「閉じる」ボタンを画面に常設し F-key を廃止 | UI 上の明示的ボタンに統一 | ✅ アクセシビリティ良、F-key 慣れユーザは不便 |
| D. F12 そのまま提供 (preventDefault 試行) | 既存運用を維持 | ❌ ブラウザ仕様上抑制不可、ユーザ誤操作必至、非推奨 |

**推奨: A + C の併用** — F12 はリマップで廃止し Esc に統一、「閉じる」ボタンも常設 (アクセシビリティ向上)。業務マニュアルで「F12 → Esc」の変更を周知。

### F9 / F10 の対応方針

| F-key | 方針 |
|---|---|
| **F9 サイズ展開** | ✅ そのまま実装可。フロント `useKeyboard` composable で `keydown.f9` → サイズ展開ハンドラ呼出 |
| **F10 登録** | ⚠️ そのまま実装するが、Firefox 一部バージョンでメニューバー起動の副作用が残る可能性あり。**フォールバックとして Ctrl+S (汎用「保存」) も併設**、ユーザは F10 と Ctrl+S のいずれでも登録可能とする |

### Phase 7 Iteration 1 冒頭で実施すべき裏取り

本マトリクスは知識ベースで作成した暫定版。Phase 7 着手時に以下を **必ず実施**:

1. **Reka UI 公式ドキュメント (https://reka-ui.com/docs/components/) 確認**: Dialog / Combobox / Listbox / Select / Menubar の各 Keyboard Interactions セクションを精読、F-key 使用有無を確認
2. **Radix UI 公式ドキュメント (https://www.radix-ui.com/primitives/docs/components/) 確認**: Reka UI は Radix UI Vue port、同等のキーバインド設計と推定されるため照合
3. **WAI-ARIA Authoring Practices Guide (https://www.w3.org/WAI/ARIA/apg/patterns/) 確認**: combobox / listbox / dialog の標準キーバインドパターン精読
4. **ブラウザ実機検証**: Chrome / Edge / Firefox / Safari で F1-F12 の `preventDefault` 動作を実装プロトタイプで検証 (特に F10 の Firefox 挙動、F11/F12 の抑制可否)

> **WebFetch 状態 (2026-05-19 時点):** Reka UI / Radix UI / WAI-ARIA 公式ドキュメントは現環境から HTTP 403 で取得不可。Phase 7 着手時に環境改善後に WebFetch リトライ、または開発者ローカル環境で直接アクセスして裏取りすること。

### 完了基準

- [ ] Reka UI / Radix UI / WAI-ARIA 公式ドキュメントの裏取り完了、本マトリクスを更新
- [ ] F12 の対応方針 (A 案推奨) をオペレーターと合意
- [ ] F10 の Firefox 副作用検証完了、フォールバック (Ctrl+S 併設) を実装計画に組込
- [ ] `useKeyboard` composable の設計 (Reka UI 既存ハンドラとの干渉回避) を Phase 7 Iteration 1 タスクリストに登録

---

## T-2: 国内用 Excel テンプレートサンプル調達 (H-3 / F-12 対応)

### 目的

Phase 6 で確定した発注書 Excel 出力機能 (O-06、MVP は ① 国内用テンプレートのみ) の実装に必要な、既存システムから出力した **① 国内用 発注書 Excel サンプル** を調達する。

### 期限

**Phase 7 Iteration 1 開始日の 1 週間前まで** (独立監査指摘 2026-05-19)

理由: Excel 出力機能はテンプレート体裁 (フォント / 列幅 / 印刷範囲 / 改ページ) の再現が業務的に重要、テンプレートサンプルなしで実装着手すると体裁検証が後追いとなり、Phase 7 Iteration の手戻りリスクが大きい。

### 担当

**オペレーター** (調達責任者)

### 調達手順 (オペレーター向けチェックリスト)

#### Step 1: サンプル発注書の選定
- [ ] 過去の発注書から、**国内仕入先向け** かつ **明細行数が多めの実例** を 1 件選定 (テンプレートの全要素を網羅確認するため、明細 20 行以上を推奨)
- [ ] 選定した発注書に機密情報 (実在の取引先名、単価、担当者の個人情報等) が含まれる場合は **マスキング** または **架空データへの置換**

#### Step 2: 既存システムから Excel 出力
- [ ] 既存 akebono アパレル管理システム (本番 or 検証環境) にログイン
- [ ] 該当発注書を開き「Excel 出力」ボタンで .xlsx ファイル取得
- [ ] ファイル名規約: `template-domestic-sample-v1.xlsx`

#### Step 3: テンプレート構造の文書化
- [ ] サンプルファイルを開き、以下を確認・記録:
  - [ ] フォント (種類、サイズ、色) — ヘッダ部 / 明細部 / フッタ部それぞれ
  - [ ] 列幅 (各列のピクセル/ポイント値)
  - [ ] 印刷範囲 (どの範囲が印刷対象か、改ページ位置)
  - [ ] 数式・参照式 (もしあれば、ClosedXML での再現方式を検討)
  - [ ] セル結合 (どのセルが結合されているか)
  - [ ] 罫線スタイル (実線/破線/二重線、太さ)
  - [ ] 画像・ロゴ (会社ロゴ等の埋込画像の有無、位置、サイズ)

#### Step 4: 引き渡し
- [ ] `template-domestic-sample-v1.xlsx` を Phase 7 実装担当者へ引き渡し (受け渡し方法: リポジトリ内 `src/Backend/Templates/` ディレクトリへコミット、または社内ファイル共有経由)
- [ ] Step 3 で文書化した構造情報を `pre-iteration-tasks.md` または別途 `templates-spec.md` として記録
- [ ] **個人情報・機密情報のマスキング完了** を最終確認

### 完了基準

- [ ] サンプル .xlsx ファイルが Phase 7 実装担当者の手元に存在
- [ ] テンプレート構造 (フォント / 列幅 / 印刷範囲 等) が文書化済
- [ ] 機密情報マスキング完了
- [ ] Phase 7 Iteration 1 開始日の 1 週間前までに上記 3 件完了

---

## Phase 7 Iteration 1 着手判定

両タスク (T-1 / T-2) が完了基準を満たした時点で Phase 7 Iteration 1 本実装に着手可。
それまでに着手すると以下のリスクが顕在化:

| リスク | 影響 |
|---|---|
| F-key 衝突発覚時の UI 再設計 | Iteration 1 の UI コンポーネント設計を全件見直し、工数 +30% |
| Excel テンプレート体裁の後追い検証 | 出力機能完成後に「既存帳票と体裁が異なる」指摘で手戻り、業務担当者の検収不可 |

---

## 関連ドキュメント

- Phase 6 feedback-log: `.ai-native/outputs/phase6/feedback-log.md` §5.2 (F-21) / §8 (H-3)
- 方法論 Phase 7 定義: `.ai-native/methodology/common/phase-definitions.md` §Phase 7
- API 設計 Excel 出力: `.ai-native/outputs/phase5/api-design.md` §2.5 O-06
- 画面設計 ショートカット: `.ai-native/outputs/phase5/screen-design.md` §5 (共通方針、Phase 7 で `useKeyboard` composable 追加)
