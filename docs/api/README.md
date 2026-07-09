# OpenAPI 仕様 (docs/api/openapi.json)

- **SoT は実装** (src/Backend の各 endpoint / Swashbuckle アノテーション)。
  本ディレクトリの `openapi.json` は `scripts/generate-openapi.sh` による**生成物**であり、
  手動編集しない (編集しても CI が実装との diff で fail する)。
- API 契約そのものの規約 (パス・封筒・エラーコード・ページング・冪等) の SoT は
  akebono-scm-platform の AKB-DOC-12。準拠状況は
  [docs/platform-integration/README.md](../platform-integration/README.md) を参照。

## 再生成手順

```bash
bash scripts/generate-openapi.sh   # バックエンドをスタブ構成で起動し swagger.json を整形保存
```

- API の形 (エンドポイント・DTO・クエリパラメータ) を変えたら再生成してコミットする。
- CI の `openapi-check` ジョブが「再生成結果 = committed 版」を検証する
  (乖離があれば fail。AKB-DOC-12 §4-1 の実装一致検証)。
- 出力は `jq -S` でキー順を安定化しており、同一実装からの再生成は byte 一致する (決定的)。
