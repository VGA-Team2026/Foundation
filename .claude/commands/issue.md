# GitHub Issue 処理コマンド

GitHub CLIを使用して特定のIssueを取得し、実装または問題の修正を行い、コミットpushしてPRを発行するコマンド。

## ロール
あなたはゲームプログラマです。
GitHubのIssueを確認し、その内容に基づいて実装を行い、PRを発行します。

## モデル使用方針
- **分析・判断**: Opusで行う（このコマンドを実行するメインエージェント）
- **細かい調査**: Sonnetを使用したサブエージェント（issue-investigator）を使用する
- サブエージェントには具体的な調査タスクを指示し、結果を受け取って分析する

## 引数
コマンド引数には `{Issue番号}` が渡される。
- 例: `/issue 123`
- Issue番号がない場合は、オープン中のIssue一覧を表示して選択を促す。

## 処理フロー

### 1. Issue情報の取得
```bash
gh issue view {Issue番号} --json title,body,labels,assignees,milestone,comments
```
- Issueのタイトル、本文、ラベル、担当者、マイルストーン、**コメント**を取得する
- **重要: Issueのコメントは全て取得して読むこと**（追加情報や議論が含まれている可能性がある）

### 2. 作業ブランチの作成
- ブランチのルートはデフォルトブランチ(develop)とする
- Issue内容を確認し、修正か機能実装化によりブランチの接頭語をわける
  - Bugのラベルがある場合は修正である

#### 実装の場合
- ブランチ名: `feature/issue-{Issue番号}-{簡潔な説明}`
- 例: `feature/issue-123-add-player-jump`

#### 修正の場合
- ブランチ名: `fix/issue-{Issue番号}-{簡潔な説明}`
- 例: `fix/issue-123-add-player-jump`

```bash
git checkout -b {ブランチ名}
```

### 3. Issue内容の分析
- Issueの本文を読み、実装すべき内容を理解する
- 関連する仕様書があれば `spec/` フォルダ内を確認する
- 不明点があればユーザーに確認する

### 4. 実装または修正

#### 実装の場合
- Issueの要件に従って実装を行う
- CLAUDE.mdのルールに従うこと
- NOTEコメントを適切に追加すること
- 未実装部分はTODOを記載すること
- 実装後は対応する仕様書を作成すること。または、仕様が確認できた場合はそれをプロジェクト内に保存すること。

#### 修正の場合
- CLAUDE.mdのルールに従うこと
- Issueの要件をもとにプロジェクトを分析する

**サブエージェントを使用した調査:**
細かいコード調査やgit履歴調査は、Taskツールでissue-investigatorサブエージェント（Sonnet）を起動して行う:
```
Task(subagent_type="issue-investigator", model="sonnet", prompt="...")
```

**調査内容:**
  1. どのようなプロセスで処理をしているか？を調査し手順を番号リストで出力
  2. 問題点を列挙
  3. 対応する仕様書のリストを出力
  上記3点をまとめた資料をspec/doc/issue-{Issue番号}-{簡潔な説明}.mdに保存する
  /spec/docは必ず存在するので、存在確認は行わなくてよい

- 上記内容を参照し、コードレビューをまず行う。
- コードレビュー後、問題点を特定できた場合は修正を行いコミットpushする
  - 問題点を特定できない場合は、Issueに調査内容を記載して終了する。このケースの場合はIssueをCloseしてはならない。
- 修正をコミット後、PRに分析結果のmdもコミットする。
- PRにコードレビューの結果と対応後のサマリを記載する。

### 5. 仕様書への反映
- 実装内容に関連する仕様書があれば `spec/` フォルダ内を更新する
- /specは必ず存在するので、存在確認は行わなくてよい
- 新しい機能や動作の変更は必ず仕様書に反映すること
- 仕様書が存在しない場合は新規作成する

### 6. コミット
- 変更内容をコミットする
- コミットメッセージにIssue番号を含める
- 例: `fix: プレイヤージャンプ機能を追加 (#123)`

### 7. PRの作成
```bash
gh pr create --title "{PRタイトル}" --body "{PR本文}" --assignee "@me"
```

PRの本文には以下を含める:
```markdown
## 概要
{Issueの要約と実装内容}

## 関連Issue
Closes #{Issue番号}

## 変更点
- {変更点1}
- {変更点2}
- ...

## テスト確認項目
- [ ] {確認項目1}
- [ ] {確認項目2}

---
Generated with [Claude Code](https://claude.ai/code)
```

### 8. 調査資料のPRコメント投稿

PR作成後、最終的な調査資料をPRにコメントとして投稿する:

```bash
# 調査資料をPRにコメント投稿
gh pr comment {PR番号} --body "$(cat spec/doc/issue-{Issue番号}-{簡潔な説明}.md)"
```

**重要:**
- 調査資料を更新した場合は、**新規コメントを投稿せず、既存コメントを編集して更新**する
- コメントIDを取得して編集する:
```bash
# PRのコメント一覧を取得
gh api repos/{owner}/{repo}/issues/{PR番号}/comments --jq '.[] | select(.body | contains("調査資料")) | .id'

# コメントを編集
gh api repos/{owner}/{repo}/issues/comments/{コメントID} -X PATCH -f body="$(cat spec/doc/issue-{Issue番号}-{簡潔な説明}.md)"
```

### 9. ログ出力
`log/` フォルダに実装ログを出力する。

## 便利なGitHub CLIコマンド

### Issue一覧表示
```bash
gh issue list --state open
```

### Issue詳細表示
```bash
gh issue view {番号}
```

### PR作成
```bash
gh pr create --title "タイトル" --body "本文"
```

### PRにレビュアー追加
```bash
gh pr edit --add-reviewer {ユーザー名}
```

## 注意事項
- リリースブランチ(main)やデフォルトブランチ(develop)に直接コミットしないこと
- PRを作成する前に、変更内容を確認すること
- Issueをクローズするには `Closes #番号` をPR本文に含めること
