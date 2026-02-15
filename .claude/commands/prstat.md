# PR ステータス確認コマンド

アクティブPRの一覧と未対応レビューコメント件数を表示します。

## アクティブPRの定義

以下の条件を**すべて**満たすPR:
1. 状態がOPEN
2. 更新日が3日以内
3. `HOLD`ラベルがない

## 処理フロー

### 1. アクティブPRの取得

```bash
gh pr list --state open --json number,title,updatedAt,labels,headRefName --jq '[.[] | select(
  (now - (.updatedAt | fromdateiso8601)) < (3 * 24 * 60 * 60) and
  ([.labels[].name] | index("HOLD") | not)
)] | sort_by(.number)'
```

### 2. 各PRの未対応レビューコメント件数を取得

各PRに対して以下を実行し、未対応コメント件数を取得:

```bash
node scripts/commands.js review-comments <PR番号>
```

出力からコメント件数をカウントする。

### 3. 結果を一覧表示

以下のフォーマットで表示すること:

```
## アクティブPR ステータス

| PR | タイトル | ブランチ | 更新 | コメント |
|----|---------|---------|------|---------|
| #653 | feat: Command Server Window | feature/command-server-window | 1時間前 | 0件 |
| #650 | fix: カーブ入力修正 | fix/646-curve-input | 2時間前 | ⚠️ 3件 |

### サマリ
- アクティブPR: 2件
- 未対応コメントあり: 1件
```

- 「更新」列は相対時間で表示（例: 1時間前、2日前）
- 未対応コメントが1件以上あるPRには ⚠️ マークを付ける
- アクティブPRが0件の場合は「アクティブなPRはありません」と表示

## 注意事項

- **読み取り専用**: ファイル変更・コミット・ブランチ切り替えは一切行わない
- 情報収集と表示のみ
- エラーが発生した場合はその旨を表示して続行
