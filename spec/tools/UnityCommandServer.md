# Unity外部コマンドサーバ仕様書

## 概要

Unityエディタ内でHTTPサーバを起動し、外部ツール（Claude Code等）からUnityの操作を行えるようにする。
これにより、Unityエディタが開いている状態でもリコンパイルやビルドコマンドを実行可能になる。

## 背景・目的

- 現状、Unityエディタが開いている状態ではバッチモードのコンパイルチェックが失敗する
- HTTPサーバ経由でUnityエディタに直接コマンドを送ることで、この問題を解決する

## API仕様

### 基本情報

- **プロトコル**: HTTP
- **ポート**: 8686（デフォルト）
- **レスポンス形式**: JSON

### エンドポイント一覧

#### 1. リコンパイル

スクリプトの再コンパイルをトリガーする。

```
POST /api/recompile
```

**リクエスト**: なし

**レスポンス**:
```json
{
  "success": true,
  "message": "Recompile triggered",
  "isCompiling": true
}
```

#### 2. コンパイルステータス確認

現在のコンパイル状態とエラーを取得する。

```
GET /api/compile-status
```

**レスポンス**:
```json
{
  "isCompiling": false,
  "hasErrors": false,
  "errors": [],
  "warnings": []
}
```

エラーがある場合:
```json
{
  "isCompiling": false,
  "hasErrors": true,
  "errors": [
    {
      "file": "Assets/Scripts/Example.cs",
      "line": 10,
      "column": 5,
      "message": "CS0103: The name 'foo' does not exist in the current context"
    }
  ],
  "warnings": []
}
```

#### 3. ビルド

プレイヤービルドを実行する。

```
POST /api/build
```

**リクエストボディ**:
```json
{
  "target": "StandaloneWindows64",
  "outputPath": "Build/Game.exe"
}
```

**レスポンス**:
```json
{
  "success": true,
  "message": "Build started",
  "buildId": "build-12345"
}
```

#### 4. ビルドステータス確認

ビルドの進行状況を取得する。

```
GET /api/build-status
```

**レスポンス**:
```json
{
  "isBuilding": false,
  "lastBuildResult": "Success",
  "lastBuildTime": "2026-01-22T10:30:00Z",
  "errors": []
}
```

#### 5. ヘルスチェック

サーバの稼働状況を確認する。

```
GET /api/health
```

**レスポンス**:
```json
{
  "status": "ok",
  "unityVersion": "6000.0.49f1",
  "projectName": "<プロジェクト名>"
}
```

### TODO（将来実装予定）

#### テスト実行

```
POST /api/run-tests
```

**リクエストボディ**:
```json
{
  "testMode": "EditMode",
  "testFilter": ""
}
```

## 実装ファイル

- `unity/Assets/Musa/UnityCommandServer.cs` - HTTPサーバ本体

## 使用方法

### サーバの起動

1. Unityエディタを開く
2. サーバは自動的に起動する（InitializeOnLoad属性）
3. コンソールに `[UnityCommandServer] Started on port 8686` と表示される

### 外部からの呼び出し例

```bash
# リコンパイル
curl -X POST http://localhost:8686/api/recompile

# コンパイルステータス確認
curl http://localhost:8686/api/compile-status

# ヘルスチェック
curl http://localhost:8686/api/health
```

## 設定

環境変数で設定可能:

- `UNITY_COMMAND_SERVER_PORT`: ポート番号（デフォルト: 8686）
- `UNITY_COMMAND_SERVER_ENABLED`: サーバの有効/無効（デフォルト: true）
- `UNITY_COMMAND_SERVER_TOKEN`: 認証トークン（設定時のみ認証を要求）

## セキュリティ考慮事項

- ローカルホスト（127.0.0.1）からの接続のみ許可
- 認証トークンを設定することで、不正アクセスを防止可能
- トークン認証を有効にする場合、`X-Auth-Token`ヘッダーにトークンを指定

### 認証付きリクエスト例

```bash
# 認証トークンを設定
export UNITY_COMMAND_SERVER_TOKEN="your-secret-token"

# 認証付きリコンパイル
curl -X POST -H "X-Auth-Token: your-secret-token" http://localhost:8686/api/recompile
```
