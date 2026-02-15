# developブランチ移動コマンド

developブランチに切り替えて最新に更新します。

## 実行手順

1. **現在のブランチを確認**
   - `git branch --show-current` を実行
   - 既にdevelopにいる場合はpullのみ実行

2. **未コミット変更の確認**
   - `git status --porcelain` で未コミット変更を確認
   - 変更がある場合は `git stash` を実行し、stashした旨を通知

3. **developブランチに切り替え**
   - `git checkout develop` を実行

4. **最新に更新**
   - `git pull` を実行

5. **完了通知**
   - 更新結果を簡潔に表示
