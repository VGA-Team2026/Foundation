# コンパイルチェックスキル

Unityプロジェクトのコンパイルチェックを実行し、エラーを解析する共通スキル。

## 前提条件

- Unity Editorが起動している
- Node.jsが利用可能
- `unity/`ディレクトリにUnityプロジェクトが存在

## コンパイルチェック実行

### scripts/commands.js を使用（推奨）

Unity Command Server経由でコンパイルを実行：

```bash
# ヘルスチェック
node scripts/commands.js unity-health

# リコンパイル実行
node scripts/commands.js unity-recompile

# コンパイルステータス確認（エラー詳細表示）
node scripts/commands.js unity-compile-status
```

### バッチモード（Unity Editor未起動時のフォールバック）

```bash
"<Unity Path>" -batchmode -quit -projectPath ./unity -executeMethod BuildScript.CompilePlayerScripts -logFile compile.log
node .github/scripts/parse-compile-log.js compile.log compile-errors.md
```

## エラー判定

### Command Server使用時
- `unity-compile-status` の出力を確認
- エラーがある場合はエラー詳細が表示される
- エラーがない場合は「コンパイル成功」と表示

### バッチモード使用時
- `.has_compile_errors`ファイルが存在する場合はエラー
- `compile-errors.md`にエラー詳細が記載

## エラーパターン

### 正規表現
```regex
^(.+\.cs)\((\d+),(\d+)\):\s*error\s+(CS\d+):\s*(.+)$
```

### 抽出情報
- グループ1: ファイルパス
- グループ2: 行番号
- グループ3: 列番号
- グループ4: エラーコード
- グループ5: エラーメッセージ

## 一般的なエラーコードと対処

| コード | 説明 | 対処 |
|--------|------|------|
| CS0103 | 名前が存在しない | using追加、変数宣言確認 |
| CS0246 | 型が見つからない | using追加、アセンブリ参照確認 |
| CS1061 | メソッドが存在しない | メソッド名確認、拡張メソッド確認 |
| CS0029 | 暗黙的型変換不可 | 明示的キャスト追加 |
| CS0019 | 演算子適用不可 | 型変換追加 |
| CS1002 | ; が必要 | セミコロン追加 |
| CS0117 | メンバーが存在しない | メンバー名確認 |
| CS0234 | 名前空間に存在しない | using確認、アセンブリ確認 |

## 使用例

### コンパイルチェックのみ実行
```bash
node scripts/commands.js unity-recompile
node scripts/commands.js unity-compile-status
```

### エラー修正ループ（compile-fixコマンド）
```
1. unity-recompile 実行
2. unity-compile-status でエラー確認
3. エラーがある場合:
   - エラー詳細を解析
   - エラーを修正
   - 1に戻る（最大10回）
4. エラーがない場合: 完了
```
