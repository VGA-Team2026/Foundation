# Push & PR コマンド

現在のブランチをpushし、PRを発行します。

## 実行手順

### 1. PR存在確認
```bash
gh pr view --json number 2>/dev/null
```

### 2. PRが存在しない場合

1. **リモートにpush**
   ```bash
   git push -u origin <current_branch>
   ```

2. **PR作成**
   ```bash
   gh pr create --title "<PR title>" --body "<PR body>"
   ```

   PR本文のテンプレート:
   ```markdown
   ## Summary
   - <変更内容の箇条書き>

   ## Test plan
   - [ ] <テスト項目>

   🤖 Generated with [Claude Code](https://claude.ai/code)
   ```

3. **セッションメモリ更新**
   - `push_enabled`: true
   - `pr_created`: true
   - `pr_url`: 作成したPRのURL

### 3. PRが既に存在する場合

1. **pushのみ実行**
   ```bash
   git push
   ```

2. **PR URLを表示**
   ```bash
   gh pr view --json url -q .url
   ```

## 完了時の出力

- PR URL
- push成功メッセージ
- 以降のコミット時に自動pushされる旨を通知

## セッションメモリへの記録

このコマンド実行後:
- `push_enabled: true` に設定
- 以降のコミットスキル実行時に自動でpushが行われる
