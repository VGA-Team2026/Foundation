# mainマージテストチェックリスト作成コマンド

mainマージ用PRに対して、マージ対象の機能一覧とテストチェックリストを作成します。

## 引数

- `$ARGUMENTS`: mainマージPRの番号（省略時は現在のブランチのPRを使用）

## 実行手順

### 1. 対象PR特定

```bash
# 引数ありの場合
gh pr view <PR番号> --json number,title,body,baseRefName,headRefName

# 引数なしの場合
gh pr view --json number,title,body,baseRefName,headRefName
```

### 2. developブランチのマージ済みPR一覧を取得

mainマージPRに含まれるコミットからマージ済みPRを特定する。

```bash
# mainとdevelop（またはheadブランチ）の差分コミットを取得
git log main..develop --oneline --merges
```

各マージコミットメッセージからPR番号（`#xxx`）を抽出する。

### 3. 各PRの詳細を取得

```bash
# 各PRの情報を取得
gh pr view <PR番号> --json number,title,labels,body
```

### 4. マージ機能テーブルを作成

以下の形式でテーブルを作成する:

```markdown
## マージ機能一覧

| # | PR | タイトル | ラベル | 状態 |
|---|---|---|---|---|
| 1 | #xxx | feat: 機能名 | enhancement | ✅ merged |
| 2 | #yyy | fix: バグ修正 | bug | ✅ merged |
```

### 5. テストチェックリストを作成

各PRの内容を分析し、テスト項目を網羅的に生成する。

テスト項目の分類:
- **基本動作確認**: 各機能が正常に動作すること
- **回帰テスト**: 既存機能への影響がないこと
- **統合テスト**: 複数機能の組み合わせ
- **エッジケース**: 境界条件やエラーケース

```markdown
## テストチェックリスト

### 基本動作確認
- [ ] [#xxx] 機能Aのテスト項目1
- [ ] [#xxx] 機能Aのテスト項目2
- [ ] [#yyy] 機能Bのテスト項目1

### 回帰テスト
- [ ] ゲーム開始〜クリアまでの通しプレイ
- [ ] エンドレスモードの基本動作
- [ ] リトライ・コンティニューの動作

### 統合テスト
- [ ] 機能A + 機能Bの組み合わせ確認
```

### 6. developブランチの最終コミットハッシュを記録

```bash
git rev-parse develop
```

### 7. PRトップコメント（本文）を更新

```bash
gh pr edit <PR番号> --body "<更新された本文>"
```

PR本文のテンプレート:

```markdown
## Summary
mainマージ: develop → main

## マージ機能一覧

| # | PR | タイトル | ラベル | 状態 |
|---|---|---|---|---|
| 1 | #xxx | feat: 機能名 | enhancement | ✅ merged |
...

## テストチェックリスト

### 基本動作確認
- [ ] [#xxx] テスト項目...

### 回帰テスト
- [ ] 通しプレイ確認...

### 統合テスト
- [ ] 組み合わせ確認...

## developブランチ情報
- 最終確認コミット: `<ハッシュ>`
- 確認日時: yyyy-mm-dd HH:MM

🤖 Generated with [Claude Code](https://claude.ai/code)
```

## 注意事項

- PRのbody全体を上書きするので、既存の内容がある場合はマージして更新する
- テスト項目はPRの変更内容を読み取って具体的に記述する（汎用的すぎる項目は避ける）
- ラベルが設定されていないPRは「-」と表示
- 同じPR番号が複数回出現する場合は重複排除する
