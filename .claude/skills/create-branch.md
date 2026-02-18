# ブランチ作成スキル

新しいブランチを作成する際の共通処理を定義します。

## 前提条件の確認

### 1. 現在のブランチ確認
```bash
git branch --show-current
```

### 2. mainブランチへの移動（必要な場合）
現在のブランチがmainでない場合:

1. 未コミットの変更を確認:
   ```bash
   git status --porcelain
   ```

2. 変更がある場合はstash:
   ```bash
   git stash push -m "auto-stash before branch creation"
   ```

3. mainに移動:
   ```bash
   git checkout main
   ```

4. 最新を取得:
   ```bash
   git pull
   ```
   - pullでエラーが発生した場合:
     ```bash
     git stash
     git pull
     ```

## ブランチ作成

### 通常のブランチ作成
```bash
git checkout -b <prefix>/<branch_name>
```

### worktreeを使用する場合（wt_プリフィクス）
ブランチ名が `wt_` で始まる場合:

1. プリフィクスを除去: `wt_xxx` → `xxx`
2. worktreeディレクトリを作成:
   ```bash
   git worktree add ../worktrees/<prefix>/<branch_name> -b <prefix>/<branch_name>
   ```
3. 作成されたworktreeのパスをユーザーに通知

## 完了時の出力

- 作成したブランチ名
- worktreeの場合は作業ディレクトリのパス
- stashした場合はその旨を通知（`git stash pop`で復元可能）

## セッションメモリへの記録

ブランチ作成後、以下の情報をセッションに記録:
- `current_branch`: 作成したブランチ名
- `branch_type`: "feature" または "fix"
- `is_worktree`: worktreeかどうか
- `push_enabled`: false（初期値）
- `pr_created`: false（初期値）
