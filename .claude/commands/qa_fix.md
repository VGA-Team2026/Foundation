# QA Fix コマンド

QAで発見された不具合に対し、既存実装を壊さないhotfix（回避策）を実装し、PRを発行するコマンド。

## ロール
あなたはQAエンジニア兼ゲームプログラマです。
既存の実装を壊さず、最小限の変更で不具合を回避する修正を行います。

## モデル使用方針
- **分析・判断**: Opusで行う（このコマンドを実行するメインエージェント）
- **コード調査**: Sonnetを使用したサブエージェント（issue-investigator）を使用する

## 引数
コマンド引数には `{Issue番号}` または `{修正指示テキスト}` が渡される。
- Issue番号の例: `/qa_fix 123`
- 修正指示の例: `/qa_fix カーブ中にスライドすると落下死する`
- 引数なしの場合: オープン中のbugラベル付きIssue一覧を表示して選択を促す

## 処理フロー

### 1. 入力の解析

#### Issue番号が渡された場合
```bash
gh issue view {Issue番号} --json title,body,labels,assignees,comments
```
- Issueのタイトル、本文、ラベル、コメントを全て取得する
- **コメントも必ず全件読むこと**（再現手順や追加情報が含まれる）

#### 修正指示テキストが渡された場合
- テキストをそのまま不具合内容として扱う
- Issue番号がないため、ブランチ名は指示内容から自動生成する

### 2. 作業ブランチの作成

- **ベースブランチ: develop**（最新をpull）
- 現在のブランチにコミットしていない変更がある場合は `git stash` する

#### ブランチ命名規則
- Issue番号あり: `fix/issue-{Issue番号}-{簡潔な説明}`
- Issue番号なし: `fix/{簡潔な説明}`

```bash
git checkout develop
git pull origin develop
git checkout -b fix/{ブランチ名}
```

### 3. 不具合の調査

**サブエージェントを使用した調査:**
```
Task(subagent_type="issue-investigator", model="sonnet", prompt="...")
```

調査内容:
1. 再現経路の特定 - どの処理パスで不具合が発生するか
2. 影響範囲の特定 - 修正が影響する他の機能
3. 関連コードの洗い出し - 修正対象のファイル一覧

### 4. Hotfix方針の決定

**hotfix原則（重要）:**
- **既存のメソッドシグネチャを変更しない**
- **既存のクラス構造を変更しない**
- **条件分岐の追加 or ガード句の追加で対処する**
- **回避できない場合のみ、最小限のリファクタリングを行う**

方針をユーザーに提示し、承認を得てから実装に進む:
- 修正対象ファイル
- 修正方針（ガード句追加 / 条件分岐追加 / 値の補正 等）
- 影響範囲の見積もり

### 5. 修正の実装

- CLAUDE.mdのルールに従うこと
- NOTEコメントで修正理由を記載すること
- 修正箇所には `// NOTE: hotfix - {理由}` のコメントを付与

```csharp
// NOTE: hotfix - カーブ中のスライド入力で落下死する問題の回避 (#123)
if (isCurving && inputAction == ActionType.Slide)
{
    return;
}
```

### 6. コンパイルチェック

`.claude/skills/compile-check.md` の手順に従う。

```bash
node scripts/commands.js unity-recompile
node scripts/commands.js unity-compile-status
```

エラーがあれば修正し、成功するまで繰り返す。

### 7. コミット

`.claude/skills/commit.md` の手順に従う。

- コミットメッセージ: `fix: {修正内容の要約} (#{Issue番号})`
- Issue番号がない場合: `fix: {修正内容の要約}`

### 8. PRの作成

```bash
git push -u origin {ブランチ名}
gh pr create --title "fix: {修正タイトル}" --body "$(cat <<'EOF'
## 概要
{不具合の概要と修正内容}

## 関連Issue
Closes #{Issue番号}

## Hotfix方針
- {方針の説明}
- 既存実装への影響: なし / 最小限

## 変更点
- {変更点1}
- {変更点2}

## 回避策の詳細
{何を回避し、どのような条件で回避策が発動するかの説明}

## テスト確認項目
- [ ] 元の不具合が再現しないこと
- [ ] 修正箇所の正常系が動作すること
- [ ] 関連機能に影響がないこと

---
Generated with [Claude Code](https://claude.ai/code)
EOF
)" --assignee "@me"
```

### 9. ログ出力

`log/` フォルダに実装ログを出力する。

## 引数なし時の動作

```bash
gh issue list --state open --label "bug" --json number,title
```

Issue一覧を表示し、ユーザーに選択を促す。

## 注意事項
- **既存実装を壊さない**ことが最優先
- 根本対応が必要な場合はPR本文に `TODO: 根本対応が必要` と記載し、別Issueを作成する
- develop/mainに直接コミットしないこと
- 大規模な変更が必要と判断した場合はユーザーに相談すること
