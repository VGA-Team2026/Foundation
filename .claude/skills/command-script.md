# コマンドスクリプト使用スキル

Node.jsコマンドスクリプト（`scripts/commands.js`）を使用して安全にシステム操作を行うスキル。

## 前提条件

- Node.jsが利用可能
- `scripts/commands.js` が存在
- `.claude/settings.json` に `Bash(node scripts/commands.js:*)` が許可済み

## コマンド一覧

### Unity操作

```bash
# ヘルスチェック
node scripts/commands.js unity-health

# リコンパイル
node scripts/commands.js unity-recompile

# コンパイルステータス確認
node scripts/commands.js unity-compile-status

# ビルド
node scripts/commands.js unity-build [target] [output]
```

### Git操作（lockエラー自動リトライ付き）

```bash
# lockファイル削除
node scripts/commands.js git-remove-lock

# コミット（特定ファイル）
node scripts/commands.js git-commit "メッセージ" file1.cs file2.cs

# コミット（全変更）
node scripts/commands.js git-commit "メッセージ"

# プッシュ
node scripts/commands.js git-push

# force push
node scripts/commands.js git-push origin branch --force
```

### GitHub Review操作

```bash
# レビューコメント取得
node scripts/commands.js review-comments [PR番号]

# コメント待機
node scripts/commands.js review-wait [PR番号] [タイムアウト秒] [間隔秒]

# コメント返信
node scripts/commands.js review-reply <PR番号> <コメントID> "<返信内容>"
```

## 使用パターン

### パターン1: コミット＆プッシュ

```bash
# 1. 特定ファイルをコミット
node scripts/commands.js git-commit "fix: バグ修正" path/to/file.cs

# 2. プッシュ
node scripts/commands.js git-push
```

### パターン2: レビュー対応

```bash
# 1. レビューコメント取得
node scripts/commands.js review-comments

# 2. 修正実施後、コミット＆プッシュ
node scripts/commands.js git-commit "fix: レビューコメント対応"
node scripts/commands.js git-push

# 3. コメント返信
node scripts/commands.js review-reply 123 456789 "修正しました"
```

### パターン3: コンパイルチェック

```bash
# 1. Unityサーバ確認
node scripts/commands.js unity-health

# 2. リコンパイル
node scripts/commands.js unity-recompile

# 3. ステータス確認
node scripts/commands.js unity-compile-status
```

## lockエラー時の動作

`git-commit` と `git-push` はlockエラー発生時に自動リトライ：
1. エラー検出
2. 2秒待機
3. lockファイル自動削除
4. リトライ（最大3回）

## 注意事項

- 直接 `git` コマンドを使うとlockエラーが発生しやすい
- コマンドスクリプト経由で安定した操作が可能
- PR番号省略時は現在のブランチから自動取得
- worktree/submodule環境にも対応

## 出力形式

### git-commit 成功時
```
ステージング: file1.cs, file2.cs
コミット完了: fix: バグ修正
  変更ファイル数: 2
```

### git-push 成功時
```
プッシュ中: origin/feature-branch
プッシュ完了
```

### review-comments 出力
```json
{
  "prNumber": 123,
  "comments": [
    {
      "id": 456789,
      "user": "reviewer",
      "body": "コメント内容",
      "path": "path/to/file.cs",
      "line": 42
    }
  ]
}
```
