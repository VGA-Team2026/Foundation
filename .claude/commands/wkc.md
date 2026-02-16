# ワークフロー一括コマンド (Issue → ブランチ → コミット → PR)

現在の作業内容をもとに、Issue発行からPR作成までを一括で実行します。

## 引数

`$ARGUMENTS` にIssueタイトルを指定する。
- 例: `/wkc Slack通知をWebhookに変更`
- 引数がない場合は、現在の変更内容（`git diff`）を分析してタイトルを自動生成する

## 前提条件
- 対象の変更が既にワーキングツリーに存在すること（未コミットの変更がある状態）
- 変更がない場合はエラーメッセージを表示して終了する

## 実行手順

### 1. 変更内容の確認

```bash
git status
git diff --stat
```

- 変更がない場合: 「コミット対象の変更がありません」と表示して終了
- 変更がある場合: 差分を分析して以降のステップで使用する

### 2. GitHub Issue の作成

差分の内容をもとにIssue本文を生成する。

```bash
gh issue create --title "<タイトル>" --body "<本文>"
```

- タイトル: `$ARGUMENTS` が指定されていればそれを使用、なければ差分から自動生成
- 本文テンプレート:
  ```markdown
  ## 概要
  <変更内容の要約（1-2行）>

  ## 変更対象
  - <変更ファイル1> - 説明
  - <変更ファイル2> - 説明
  ```

- 作成されたIssue番号を控える

### 3. ブランチの作成

```bash
# 現在developにいない場合はstash → develop → pull
git stash  # 変更がある場合
git checkout develop
git pull
git checkout -b fix/<Issue番号>
git stash pop  # stashした場合
```

- ブランチ名: `fix/<Issue番号>`
- developブランチの最新から作成する

### 4. コミット

変更内容をコミットする。commit スキルの手順に従う。

```bash
git add <変更ファイル>
git commit -m "<type>: <summary> (#<Issue番号>)

Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>"
```

- .csファイルがある場合は.metaファイルの存在を確認する
- コミットメッセージにIssue番号を含める

### 5. Push & PR作成

```bash
git push -u origin fix/<Issue番号>
gh pr create --title "<PRタイトル>" --body "<PR本文>"
```

- PRタイトル: コミットメッセージと同様の形式
- PR本文テンプレート:
  ```markdown
  Closes #<Issue番号>

  ## Summary
  - <変更内容の箇条書き>

  ## Test plan
  - [ ] <テスト項目>

  🤖 Generated with [Claude Code](https://claude.ai/code)
  ```

### 6. 完了通知

以下を表示する:
- Issue URL
- PR URL
- 変更ファイル一覧

## 注意事項
- mainブランチに直接コミットしないこと
- 機密情報（.env, credentials等）をコミットしないこと
- 新規.csファイルには対応する.metaファイルを含めること
