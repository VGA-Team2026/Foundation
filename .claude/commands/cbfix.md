# ブランチ作成コマンド (fix)

fix/$ARGUMENTS ブランチを作成します。

## 実行手順

1. **現在のブランチを確認**
   - mainブランチにいない場合:
     - 未コミットの変更がある場合は `git stash` を実行
     - `git checkout main` を実行
     - `git pull` を実行（エラー時は `git stash` 後に再試行）

2. **ブランチ名の処理**
   - 引数: `$ARGUMENTS`
   - `wt_` プリフィクスがある場合:
     - プリフィクスを除去したブランチ名を使用
     - `git worktree add ../worktrees/fix/<branch_name> -b fix/<branch_name>` を実行
     - 作成したworktreeディレクトリに移動する旨を通知
   - 通常の場合:
     - `git checkout -b fix/$ARGUMENTS` を実行

3. **完了通知**
   - 作成したブランチ名を表示
   - worktreeの場合はパスも表示

## 注意事項
- ブランチ名に日本語は使用しない
- ブランチ名はスネークケースを推奨（例: `fix_null_reference`）
