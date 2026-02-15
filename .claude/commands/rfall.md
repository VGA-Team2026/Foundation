# 全アクティブPRレビューコメント対応コマンド

アクティブな全PRのレビューコメントを順次対応します。

## アクティブPRの定義

以下の条件を**すべて**満たすPR:
1. 更新日が3日以内
2. `HOLD`ラベルがない
3. 状態がOPEN

## 処理フロー

### 1. アクティブPRの取得

```bash
gh pr list --state open --json number,title,updatedAt,labels --jq '[.[] | select(
  (now - (.updatedAt | fromdateiso8601)) < (3 * 24 * 60 * 60) and
  ([.labels[].name] | index("HOLD") | not)
)] | sort_by(.number)'
```

### 2. 各PRに対して順次処理

各PRに対して:
1. `gh pr checkout <PR番号>` でブランチ切り替え
2. `node scripts/commands.js review-comments` でコメント取得
3. コメントがあれば対応・コミット・Push・返信
4. 次のPRへ

### 3. 完了報告

処理したPR数と結果サマリを報告。

## 出力形式

```
=== アクティブPR一覧 ===
PR #123: タイトル1 (更新: 2時間前)
PR #456: タイトル2 (更新: 1日前)

=== PR #123 処理中 ===
レビューコメント: 2件
- 修正対応...
- コミット・Push完了
- コメント返信完了

=== PR #456 処理中 ===
レビューコメント: 0件
- 対応不要

=== 完了 ===
処理PR数: 2
対応コメント数: 2
```

## 注意事項

- 各PRは順次処理（並列実行しない）
- 処理完了後、元のブランチに戻る
- エラーが発生しても次のPRの処理を継続
- HOLDラベルのPRはスキップ
