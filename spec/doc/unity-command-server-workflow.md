# Unity Command Server ワークフロー

## 概要

Unity Command Serverは、外部からUnityエディタを制御するためのHTTPサーバーです。
Claude Codeやスクリプトからコンパイルチェック、テスト実行などを行えます。

## セットアップ

### 1. 設定ファイルの作成

`musa/terpsichore/unity_command_server.json` を作成:

```json
{
  "port": 8686,
  "enabled": true,
  "token": "",
  "role": "worker"
}
```

**設定項目:**
- `port`: サーバーのポート番号（デフォルト: 8686）
- `enabled`: サーバーの有効/無効
- `token`: 認証トークン（空文字で認証なし）
- `role`: サーバーの役割（`worker` / `watcher` / `debugger`）

**role の種類:**
| role | 説明 |
|------|------|
| `worker` | デフォルト。通常の開発用 |
| `watcher` | PR監視モード。Unity起動時に `pr-watcher.js` を自動起動し、アクティブPRのレビューコメントを自動修正 |
| `debugger` | デバッグ用（将来拡張） |

### 2. Unityエディタの起動

Unityエディタを起動（または再起動）すると、自動的にサーバーが起動します。

コンソールで以下のログを確認:
```
[UnityCommandServer] Config loaded: port=8686, enabled=true
[UnityCommandServer] Started on port 8686
```

### 3. 動作確認

```bash
node scripts/commands.js unity-health
```

成功時の出力:
```
Unity Command Server: 稼働中
  Unity Version: 6000.0.49f1
  Project: <プロジェクト名>
```

## 利用可能なコマンド

### ヘルスチェック

```bash
node scripts/commands.js unity-health
```

サーバーの稼働状況とUnityバージョン、プロジェクト名を確認します。

### リコンパイル

```bash
node scripts/commands.js unity-recompile
```

Unityスクリプトのリコンパイルをリクエストします。

### コンパイルステータス確認

```bash
node scripts/commands.js unity-compile-status
```

最新のコンパイル結果（エラー・警告）を取得します。

## ワークフロー例

### コード変更後のコンパイルチェック

```bash
# 1. コードを編集
# 2. リコンパイルをリクエスト
node scripts/commands.js unity-recompile

# 3. 結果を確認
node scripts/commands.js unity-compile-status
```

### コミット前の確認

```bash
# コンパイルエラーがないことを確認
node scripts/commands.js unity-compile-status

# エラーがなければコミット
node scripts/commands.js git-commit "fix: バグ修正" path/to/file.cs
```

## トラブルシューティング

### 接続エラー

**症状:**
```
Unity Command Server: 接続失敗
```

**原因:**
- `unity_command_server.json`が存在しない
- Unityエディタが起動していない
- ポート番号が間違っている

**対処:**
1. 設定ファイルの存在を確認
2. Unityエディタを起動/再起動
3. ポート番号を確認

### ポート競合

**症状:**
複数のUnityエディタを起動したときにサーバーが起動しない

**対処:**
2台目のUnityプロジェクトでは異なるポート番号を使用:
```json
{
  "port": 8687,
  "enabled": true,
  "token": ""
}
```

## PR監視モード

`role: "watcher"` を設定すると、Unity起動時にPR監視デーモンが自動起動します。

### 動作フロー

1. Unity起動 → UnityCommandServer が `role: "watcher"` を検出
2. `node scripts/pr-watcher.js` をバックグラウンドで自動起動
3. 120秒間隔でアクティブPRをポーリング
4. 未対応レビューコメントを検出 → Claude Code Bridgeの `/rf` を実行
5. Unity終了時にwatcherプロセスも自動停止

### 手動起動

```bash
# PR Watcherを手動で起動
node scripts/commands.js pr-watcher

# オプション指定
node scripts/pr-watcher.js --interval 60 --bridge-url http://localhost:3456
```

### 前提条件

- `gh` CLI がインストール・認証済み
- Claude Code Bridge (`tools/claude-code-bridge`) が稼働中
- アクティブPR: 3日以内に更新、HOLDラベルなし、OPEN状態

## API エンドポイント

### ビルド・コンパイル系

| エンドポイント | メソッド | 説明 |
|---------------|---------|------|
| `/api/health` | GET | サーバー稼働確認（roleフィールド含む） |
| `/api/recompile` | POST | リコンパイル実行 |
| `/api/compile-status` | GET | コンパイル結果取得 |
| `/api/build` | POST | ビルド実行 |
| `/api/build-status` | GET | ビルド結果取得 |

### ランタイムコマンド系

| エンドポイント | メソッド | 説明 |
|---------------|---------|------|
| `/api/execute-command` | POST | ゲームコマンド実行（プロジェクト側で登録） |
| `/api/inject` | POST | InjectParamList切り替え（プロジェクト側で登録） |
| `/api/game-status` | GET | ゲーム状態取得（プロジェクト側で登録） |

※ ランタイムコマンド系は `RuntimeCommandBridge` のデリゲート登録が必要です。プロジェクト側で `CommandHandler`, `GameStatusHandler`, `InjectHandler` を登録してください。

## CLIコマンド（ランタイム）

```bash
# ゲームコマンド実行
node scripts/commands.js unity-execute-command <command-type> [parameters-json]

# Inject切り替え
node scripts/commands.js unity-inject <paramListName>

# ゲーム状態取得
node scripts/commands.js unity-game-status
```

## 関連ファイル

- `scripts/commands.js` - コマンドラッパー
- `scripts/pr-watcher.js` - PR監視デーモン
- `unity/Assets/Musa/UnityCommandServer.cs` - サーバー実装
- `unity/Assets/Musa/RuntimeCommandBridge.cs` - ランタイムコマンドブリッジ
