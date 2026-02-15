# レビューコメント対応コマンド

現在のブランチに対応するPRのレビューコメントを取得し、対応・コミット・Push・コメント返信を行う。
コンフリクト解決やコンパイルエラーの修正も含む。

## 処理フロー

### 0. コンフリクト確認・解決

レビューコメント対応の前に、PRにコンフリクトがないか確認する。

```bash
gh pr view --json mergeStateStatus -q .mergeStateStatus
```

結果が `DIRTY` の場合、コンフリクトが発生している:
1. ベースブランチを取得: `gh pr view --json baseRefName -q .baseRefName`
2. ベースブランチをfetch: `git fetch origin <base>`
3. マージ実行: `git merge origin/<base>`
4. コンフリクトファイルを確認・解決
5. マージコミット・Push

### 1. レビューステータス確認
```bash
node scripts/commands.js review-status
```

ステータスに応じて処理を分岐:

| status | 処理 |
|--------|------|
| `draft` | ドラフト解除まで待機（30秒間隔でポーリング、最大10分） |
| `pending` | レビュー開始まで待機（30秒間隔でポーリング、最大10分） |
| `review_requested` | レビュー開始まで待機（30秒間隔でポーリング、最大10分） |
| `in_review` | コメント取得へ進む |
| `changes_requested` | コメント取得へ進む |
| `approved` | 完了報告（対応不要） |
| `merged` / `closed` | 完了報告（PRクローズ済み） |

### 2. レビューコメント取得
```bash
node scripts/commands.js review-comments
```

### 3. コメント対応
- コメントがある場合、`review-handler`サブエージェントを起動して対応
- コメントがない場合、完了を報告

### 4. コンパイルチェック（修正後）

レビューコメントの修正後、コンパイルエラーがないか確認する。

```bash
node scripts/commands.js unity-recompile
node scripts/commands.js unity-compile-status
```

- コンパイルエラーがある場合: エラーを修正し、追加コミット・Push
- Unityエディタが起動していない場合はスキップ

## 待機処理

`draft` / `pending` / `review_requested` の場合:
1. 30秒待機
2. `review-status` を再取得
3. `in_review` / `changes_requested` / `approved` / `merged` / `closed` になるまで繰り返し
4. 最大10分（20回）でタイムアウト → 現状のまま処理続行

## サブエージェント呼び出し

コメントがある場合は、Taskツールで以下を実行:
- `subagent_type`: `review-handler`
- `model`: `sonnet`
- `prompt`: 取得したコメント情報を含む指示

## 注意事項
- 人間のレビューコメントを優先して対応する
- coderabbitaiの指摘は妥当性を判断してから対応する
- 対応不要と判断した場合は、その理由をコメントで返信する
- 修正がない場合でもコメント返信は行う
