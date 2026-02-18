# コンパイルエラー自動修正コマンド

## 概要
CIと同じコンパイルチェックを実行し、エラーがあれば自動修正を繰り返すコマンド。
コンパイルエラーがなくなるまでループ処理を行う。

## 設定
- **Unity Path**: Environment.yamlから読み込む
- **Project Path**: `unity/`
- **Log File**: `compile.log`
- **Error Summary**: `compile-errors.md`
- **Error Flag**: `.has_compile_errors`
- **最大リトライ回数**: 10回

## 処理フロー

### 1. 初期化
1. 現在の作業ディレクトリを記録
2. リトライカウンタを0に初期化
3. Unityプロジェクトのパスを確認

### 2. コンパイルチェック実行ループ
以下の処理を最大10回まで繰り返す：

#### 2.1 コンパイルチェック実行
```bash
"<Unity Path>" -batchmode -quit -projectPath ./unity -executeMethod BuildScript.CompilePlayerScripts -logFile compile.log
```

#### 2.2 エラー解析（Node.jsスクリプト使用）
```bash
node .github/scripts/parse-compile-log.js compile.log compile-errors.md
```

このスクリプトは：
- `compile.log`からコンパイルエラーのみを抽出
- ライセンスエラー等のノイズを除外
- ファイル別にエラーをグループ化
- `compile-errors.md`にサマリを出力
- エラーがある場合は`.has_compile_errors`フラグファイルを作成

#### 2.3 成功判定
- `.has_compile_errors`ファイルが存在しない場合：
  1. 「✅ コンパイル成功」を出力
  2. ループを終了して完了報告

#### 2.4 エラー修正
- `.has_compile_errors`ファイルが存在する場合：
  1. `compile-errors.md`を読み込む
  2. エラー情報を解析：
     - ファイルパス（例：`Assets/Scripts/xxx.cs`）
     - 行番号・列番号
     - エラーコード（CS0103, CS1061など）
     - エラーメッセージ
  3. エラーの種類に応じて修正：
     - **CS0103**: 名前が存在しない → 参照追加、using追加
     - **CS1061**: メソッドが存在しない → メソッド追加
     - **CS0246**: 型が見つからない → using追加
     - **CS0029**: 型変換エラー → キャスト追加
     - **CS0019**: 演算子エラー → 型変換追加
     - **その他**: エラーメッセージを解析して修正
  4. 修正内容をファイルに保存
  5. リトライカウンタをインクリメント

#### 2.5 ループ継続判定
- リトライ回数 < 10 → ステップ2.1に戻る
- リトライ回数 >= 10 → 最大リトライ到達、終了

### 3. 完了報告
1. 修正したファイル一覧を出力
2. 総リトライ回数を出力
3. 最終結果（成功/失敗）を出力

## エラー解析の出力形式

### コンソール出力例
```
═══════════════════════════════════════════════════════════
  UNITY COMPILE ERROR REPORT
═══════════════════════════════════════════════════════════

❌ 3 件のコンパイルエラーが検出されました

📁 GameManager.cs (2 errors)
──────────────────────────────────────────────────
  Line 45: [CS0103] The name 'xxx' does not exist...
  Line 78: [CS1002] ; expected

📁 Player.cs (1 errors)
──────────────────────────────────────────────────
  Line 123: [CS0246] The type or namespace...

═══════════════════════════════════════════════════════════
```

### compile-errors.md形式
```markdown
# ❌ コンパイルエラー検出

**エラー数: 3件**

## ファイル別エラー
| ファイル | エラー数 |
|----------|----------|
| GameManager.cs | 2 |
| Player.cs | 1 |

## エラー詳細
### GameManager.cs
​```
Assets/Scripts/InGame/GameManager.cs(45,17): error CS0103: The name 'xxx' does not exist
​```
```

## 注意事項
- Unityエディタが起動中の場合はbatchmodeが失敗する可能性がある
- 大規模な構造変更が必要なエラーは手動対応が必要
- 循環参照やアセンブリ定義の問題は自動修正が困難
