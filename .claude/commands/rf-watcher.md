# PR Watcher用レビューコメント対応コマンド

PR Watcherから自動実行されるレビューコメント対応コマンド。
通常の `/rf` と異なり、worktree環境での制約とWatcher向け最適化を含む。

## コンテクスト（重要）

- **実行環境**: PR Watcherによる自動実行（worktree環境）
- **Unityエディタ**: 使用不可（worktreeにはUnityが開いていない）
- **コンパイルチェック**: 不要・不可能（スキップすること）
- **モデル**: サブエージェントは `sonnet` を使用
- **並列処理**: 各PRはworktreeで独立して処理される

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
| `draft` | 待機せず終了（Watcher側が再ポーリングする） |
| `pending` | 待機せず終了 |
| `review_requested` | 待機せず終了 |
| `in_review` | コメント取得へ進む |
| `changes_requested` | コメント取得へ進む |
| `approved` | 完了報告（対応不要） |
| `merged` / `closed` | 完了報告（PRクローズ済み） |

### 2. レビューコメント取得
```bash
node scripts/commands.js review-comments
```

コメントには `diffHunk` が含まれる。差分情報を活用して修正箇所を正確に特定すること。

### 3. コメント対応

コメントがある場合、`review-handler` サブエージェントを起動:

```
Task tool:
  subagent_type: review-handler
  model: sonnet
  prompt: |
    PR #<PR番号> のレビューコメントに対応してください。

    ## 制約事項
    - コンパイルチェックは不要（Unityが開いていないため実行不可能）
    - diffHunk を参照して修正箇所を正確に特定してください

    ## コメント:
    <取得したコメント情報（diffHunk含む）>
```

コメントがない場合、完了を報告。

### 4. 完了

修正・コミット・Push・コメント返信が完了したら終了。

## 制約事項（必ず守ること）

1. **コンパイルチェックを絶対に実行しない** — Unityエディタが開いていないため不可能
2. **Unityへのフォーカス切替を実行しない** — worktree環境にはUnityプロセスがない
3. **サブエージェントは `model: sonnet` を指定** — コスト最適化
4. **diffHunk を活用** — コメントの差分情報から修正箇所を正確に特定する

## 注意事項

- 人間のレビューコメントを優先して対応する
- coderabbitaiの指摘は妥当性を判断してから対応する
- 対応不要と判断した場合は、その理由をコメントで返信する
- 修正がない場合でもコメント返信は行う
