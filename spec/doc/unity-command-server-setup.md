# Unity Command Server セットアップ・トラブルシューティング

## 概要
UnityCommandServerの複数起動対応と、設定ファイルベースの起動管理について。

## 設計

### サーバー起動情報JSONファイル

`musa/terpsichore/unity_command_server.json` を配置：

```json
{
  "port": 8686,
  "enabled": true,
  "token": ""
}
```

- `.gitignore`で除外対象にする
- JSONが存在しない場合、Unityサーバーは起動しない
- `scripts/commands.js`もJSONからポート情報を読む

### 複数Unity起動時の対応

2台目のUnityプロジェクトでは異なるポート番号を使用：
```json
{
  "port": 8687,
  "enabled": true,
  "token": ""
}
```

### 変更対象ファイル

1. `unity/Assets/Musa/UnityCommandServer.cs`
   - JSONファイルを読み取りポートを決定
   - JSONがない場合はサーバー起動しない

2. `scripts/commands.js`
   - JSONファイルからポート情報を読み取り
   - 環境変数よりJSONを優先

3. `.gitignore`
   - `musa/terpsichore/unity_command_server.json` を追加

## 処理フロー

### 正常時の処理

1. **Unityエディタ起動時**
   - `UnityCommandServer.cs`の静的コンストラクタ実行（[InitializeOnLoad]属性）
   - `musa/terpsichore/unity_command_server.json`を読み取り
   - HTTPサーバー起動（デフォルト: localhost:8686）

2. **外部コマンド実行時**
   - `node scripts/commands.js unity-health` 実行
   - `unity_command_server.json`からポート番号とトークンを読み取り
   - HTTPリクエストをUnityサーバーに送信

3. **コンパイルチェック実行時**
   - `node scripts/commands.js unity-recompile` → POST /api/recompile
   - UnityがCompilationPipeline.RequestScriptCompilation()実行
   - `node scripts/commands.js unity-compile-status` → GET /api/compile-status

## トラブルシューティング

### 設定ファイルが存在しない（サーバー未起動の主因）

**現象**: `unity_command_server.json`が存在しない

**影響**: UnityCommandServerが起動しない

**コード証拠** (UnityCommandServer.cs):
```csharp
var config = LoadConfig();
if (config == null)
{
    Debug.Log("[UnityCommandServer] Config file not found. Server will not start.");
    return;
}
```

### 解決策

設定ファイルの作成:
```json
{
  "port": 8686,
  "enabled": true,
  "token": ""
}
```

### 動作確認手順

1. Unityエディタを起動
2. コンソールで起動ログを確認:
   ```
   [UnityCommandServer] Config loaded: port=8686, enabled=true
   [UnityCommandServer] Started on port 8686
   ```
3. ヘルスチェック:
   ```bash
   node scripts/commands.js unity-health
   ```
