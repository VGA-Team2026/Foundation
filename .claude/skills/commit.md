# コミットスキル

作業完了時にコミットを行うスキルです。

## 重要: .csファイルと.metaファイルの同時コミット

**Unityプロジェクトでは、.csファイルと対応する.metaファイルを必ず同時にコミットする必要があります。**

### 新規.csファイル追加時
1. Unityにフォーカスを当ててmetaファイルを生成させる
   ```bash
   powershell -Command "Add-Type -AssemblyName Microsoft.VisualBasic; [Microsoft.VisualBasic.Interaction]::AppActivate('Unity')"
   ```
2. metaファイルの生成を確認
   ```bash
   ls unity/Assets/Scripts/path/to/NewFile.cs.meta
   ```
3. 両方をステージング
   ```bash
   git add unity/Assets/Scripts/path/to/NewFile.cs unity/Assets/Scripts/path/to/NewFile.cs.meta
   ```

### metaファイルが見つからない場合
- Unityが起動していない、またはフォーカスを取得できていない可能性
- 数秒待ってから再度確認する
- それでも生成されない場合は、Unityを手動でフォーカスするようユーザーに依頼

## コミット実行（scripts/commands.js使用）

### 推奨: git-commitコマンド

```bash
node scripts/commands.js git-commit "<コミットメッセージ>" <ファイル1> <ファイル2> ...
```

このコマンドは以下の利点があります：
- `index.lock`エラー時に自動リトライ
- ファイル省略時は全変更をステージング

### ロックファイル削除

```bash
node scripts/commands.js git-remove-lock
```

## コミット手順

### 1. 変更の確認
```bash
git status
git diff --stat
```

### 2. metaファイルの確認（.cs追加時）
新規.csファイルがある場合、対応する.metaファイルも存在することを確認：
```bash
# 例: Rail.csを追加した場合
ls unity/Assets/Scripts/InGame/Stage/Rail.cs.meta
```

### 3. コミットメッセージの作成

作業内容のサマリをコミットログに記載：

- **feat**: 新機能追加
- **fix**: バグ修正
- **refactor**: リファクタリング
- **docs**: ドキュメント更新
- **style**: コードスタイル変更
- **test**: テスト追加・修正
- **chore**: その他の変更

### 4. コミット実行

```bash
# scripts/commands.js使用（推奨）
node scripts/commands.js git-commit "<type>: <summary>" <files...>

# または直接git使用
git add <変更ファイル> <対応するmetaファイル>
git commit -m "<type>: <summary>

<詳細説明（必要な場合）>

Generated with [Claude Code](https://claude.ai/code)
via [Happy](https://happy.engineering)

Co-Authored-By: Claude <noreply@anthropic.com>
Co-Authored-By: Happy <yesreply@happy.engineering>"
```

### 5. push実行

```bash
# scripts/commands.js使用（推奨）
node scripts/commands.js git-push

# または直接git使用
git push -u origin <ブランチ名>
```

## 注意事項

- コミット前に必ず変更内容を確認
- 機密情報（.env, credentials等）をコミットしない
- **新規.csファイルには必ず対応する.metaファイルを含める**
- 大きな変更は適切な単位に分割してコミット
