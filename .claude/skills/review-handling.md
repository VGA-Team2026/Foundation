# レビューコメント対応スキル

PRのレビューコメントに対応し、修正・コミット・返信を行うスキル。

## 前提条件

- `gh` CLIがインストール済み
- PRが作成済み
- `scripts/commands.js` が利用可能

## 対応フロー

### 1. レビューコメント取得

```bash
node scripts/commands.js review-comments
```

出力例：
```json
{
  "prNumber": 495,
  "comments": [
    {
      "id": 123456,
      "user": "coderabbitai[bot]",
      "isBot": true,
      "body": "指摘内容...",
      "path": "scripts/commands.js",
      "line": 42
    }
  ]
}
```

### 2. コメント分類

| 投稿者 | 優先度 | 対応方針 |
|--------|--------|----------|
| 人間のレビュアー | 高 | 必ず対応 |
| coderabbitai[bot] | 中 | 妥当性を判断 |
| その他Bot | 低 | 必要に応じて |

### 3. 修正実施

1. 対象ファイルを `Read` ツールで確認
2. `Edit` ツールで修正
3. 修正内容をメモ

### 4. コミット＆プッシュ

```bash
# コミット
node scripts/commands.js git-commit "fix: レビューコメント対応

- [修正内容1]
- [修正内容2]"

# プッシュ
node scripts/commands.js git-push
```

### 5. コメント返信

```bash
# 各コメントに返信
node scripts/commands.js review-reply <PR番号> <コメントID> "修正しました。[説明]"
```

返信例：
- 修正した場合: `"修正しました。[具体的な修正内容]"`
- 対応不要の場合: `"ご指摘ありがとうございます。[理由]のため現状維持とします。"`

### 6. 繰り返し確認

```bash
# 未対応コメントが0件になるまで繰り返し
node scripts/commands.js review-comments
```

## サブエージェント使用

複雑なレビュー対応は `review-handler` サブエージェントを使用：

```
Task tool:
  subagent_type: review-handler
  model: sonnet
  prompt: |
    PR #495 のレビューコメントに対応してください。

    コメント:
    - id: 123456
      path: scripts/commands.js
      line: 42
      body: "指摘内容..."
```

## 判断基準

### 必須対応
- バグ指摘
- セキュリティ問題
- 明らかな設計ミス
- テスト不足

### 検討対応
- パフォーマンス改善
- コード品質向上
- リファクタリング提案

### 対応不要
- スタイルの好み（プロジェクト規約に従っている場合）
- 過度な最適化
- 既に対応済み（✅ Addressed）

## コミットメッセージ形式

```
fix: レビューコメント対応

- [修正内容1]
- [修正内容2]

Generated with [Claude Code](https://claude.ai/code)
via [Happy](https://happy.engineering)

Co-Authored-By: Claude <noreply@anthropic.com>
Co-Authored-By: Happy <yesreply@happy.engineering>
```

## 注意事項

- 人間のレビュアーのコメントを最優先
- Botの指摘は妥当性を確認してから対応
- 対応しない場合も必ず理由を返信
- 機密情報を含むコードの修正は慎重に
- 大きな変更は複数コミットに分割

## トラブルシューティング

### コメント返信が404エラー

```bash
# PRコメントとして投稿（フォールバック）
gh pr comment <PR番号> --body "返信内容"
```

### lockエラー発生

コマンドスクリプトが自動リトライするため、通常は自動解決。
手動解決が必要な場合：

```bash
node scripts/commands.js git-remove-lock
```
