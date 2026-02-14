# Musa - Unity Editor ツール群

## 概要

MusaはUnity Editor上で動作する開発支援ツール群。Google Driveアセット管理、外部コマンド連携、パッケージ更新管理などを統合的に提供する。

## セットアップガイド

### 前提条件

- Unity 6000.0 以降
- 依存アセンブリ: Melpomene, Terpsichore（asmdef参照）

### 1. 基本セットアップ（必須）

Musa本体はUnity Editorで `unity/Assets/Musa/` フォルダが認識されれば自動的に利用可能になる。

### 2. UnityCommandServer セットアップ

外部ツール（Claude Code等）からUnityを操作するためのHTTPサーバ。

#### 設定ファイルの作成

`musa/terpsichore/unity_command_server.json` を作成:

```json
{
  "port": 8686,
  "enabled": true,
  "token": "",
  "role": "worker"
}
```

| フィールド | 説明 | デフォルト |
|---|---|---|
| `port` | HTTPサーバのポート番号 | `8686` |
| `enabled` | サーバの有効/無効 | `true` |
| `token` | 認証トークン（空の場合は認証なし） | `""` |
| `role` | 動作モード: `worker` / `watcher` / `debugger` | `worker` |

> **NOTE**: 設定ファイルが存在しない場合、サーバは起動しない。

#### role の違い

- **worker**: 標準モード。コマンド受信・実行のみ
- **watcher**: PR Watcherプロセスを自動起動し、PR監視を行う
- **debugger**: デバッグ向け設定

### 3. Google Drive アセット管理セットアップ

Google Driveを使ったアセットの共有・配布を行う。

#### 3a. Lambda認証方式（推奨）

`musa/musa_settings.json` を作成:

```json
{
  "googleAuthUrl": "https://your-lambda-url.execute-api.region.amazonaws.com/auth",
  "googleDriveFolderIdAsset": "Google DriveのフォルダID",
  "googleDriveCatalogFileId": ""
}
```

| フィールド | 説明 |
|---|---|
| `googleAuthUrl` | Lambda認証エンドポイントURL |
| `googleDriveFolderIdAsset` | アセットを保存するGoogle DriveフォルダのID |
| `googleDriveCatalogFileId` | カタログJSONのファイルID（空なら自動検出） |

Lambda認証の流れ:
1. Importer/Uploaderが `{googleAuthUrl}/token` にリクエスト
2. Lambdaが OAuth access_token を返却
3. そのトークンでGoogle Drive APIを呼び出す

#### 3b. refresh_token方式（フォールバック）

Lambda認証が使えない場合、以下のファイルを手動作成する:

`musa/melpomene/settings.json`:
```json
{
  "googleClientId": "your-client-id.apps.googleusercontent.com",
  "googleClientSecret": "your-client-secret"
}
```

`musa/melpomene/google_token.json`:
```json
{
  "access_token": "",
  "refresh_token": "your-refresh-token"
}
```

### 4. AssetManager（S3ベース）セットアップ

AWS S3を使ったアセット管理を行う場合の設定。

1. Unity Editorメニュー `VTNTools/Asset Manager Config` でScriptableObjectを作成
2. 以下を設定:

| フィールド | 説明 |
|---|---|
| `ApiEndpoint` | Lambda API URL |
| `ApiKey` | API認証キー |
| `ProjectId` | プロジェクトID（デフォルト: `Foundation`） |
| `ImportTargetPath` | インポート先（デフォルト: `Assets/ThirdParty`） |
| `AuthorizedUploadUsers` | アップロード許可ユーザー名リスト |
| `UploadSecretKey` | アップロード権限キー |

アップロード権限は以下のいずれかで付与:
- 環境変数 `ASSET_MANAGER_UPLOAD_KEY` に秘密キーを設定
- `AuthorizedUploadUsers` にWindowsユーザー名を追加

### 5. Claude Code Bridge セットアップ

Claude Code Bridgeサーバー（localhost:3456）が起動していれば自動接続する。

---

## 使用方法

### メインウィンドウ（MusaWindow）

**メニュー**: `Musa > Musa` （ショートカット: `Ctrl+Shift+M`）

3つのメインタブで構成:

#### Terpsichore タブ

外部コマンド連携の管理。

| サブタブ | 機能 |
|---|---|
| **Server** | UnityCommandServerの状態確認（ポート、PlayMode、コンパイル状態） |
| **Bridge** | Claude Code Bridge接続状態・タスク管理 |
| **Watcher** | PR Watcherプロセスの状態・ログ（role=watcher時のみ有効） |
| **Config** | unity_command_server.json の設定表示 |

#### Melpomene タブ

チケット・通知・マイルストーン管理。

| サブタブ | 機能 |
|---|---|
| **チケット** | チケット一覧の表示・管理 |
| **通知** | プロジェクト通知の確認 |
| **マイルストーン** | マイルストーン進捗確認 |
| **Eureka** | Eurekaウィンドウの埋め込み表示 |
| **設定** | Melpomene設定パネル |

#### Settings タブ

MusaGlobalSettings の編集UI。Google Auth URLやフォルダIDを設定可能。

---

### Google Drive Asset Importer

**メニュー**: `Tools > Musa > Asset Importer`

Google Driveからアセット（.unitypackage）をダウンロードしてプロジェクトにインポートする。

#### 操作フロー

1. Settings タブで認証URL・フォルダIDが設定済みであることを確認
2. **Fetch Catalog** でGoogle Driveからカタログ（`asset_catalog.json`）を取得
3. アセット一覧が表示される。各アセットのステータス:
   - **UpToDate**: 最新版がインポート済み
   - **NeedsUpdate**: 新バージョンあり
   - **PartiallyImported**: 一部ファイルのみ存在
   - **NotDownloaded**: 未ダウンロード
4. ダウンロードしたいアセットを選択して **Download**
5. MD5検証後、自動的に `AssetDatabase.ImportPackage()` でインポート

#### 起動時チェック

Unity起動時に `AssetImporterStartupChecker` が自動実行される:
- ローカルキャッシュ（`asset_catalog_cache.json`）を参照
- 不足アセットがあればダイアログで通知
- 「Asset Importerを開く」でImporterウィンドウを起動

---

### Google Drive Asset Uploader

**メニュー**: `Tools > Musa > Asset Uploader`

ローカルの .unitypackage をGoogle Driveにアップロードし、カタログを更新する。

#### 操作フロー

1. **Browse** でアップロードする .unitypackage を選択
2. アセット名・バージョン・説明を入力
3. **Upload** を実行
4. 自動処理:
   - unitypackageからGUID抽出
   - MD5ハッシュ計算
   - Google Driveへアップロード
   - 公開設定（anyone/reader）
   - カタログJSON更新・アップロード

---

### Package Updater

**メニュー**: `Musa > Package Updater` （ショートカット: `Ctrl+Shift+P`）

Unityパッケージの更新を一括管理する。

#### タブ

| タブ | 内容 |
|---|---|
| **All** | 全パッケージ |
| **Unity** | Unity公式パッケージ |
| **OpenUPM** | OpenUPMパッケージ |
| **Git** | Git参照パッケージ |

#### 機能

- **フィルタ**: 更新可能のみ / 未使用のみ / 名前検索
- **Analyze**: `packages-lock.json` から依存関係を解析し、使用状況を判定
  - `DirectlyUsed`: プロジェクトが直接参照
  - `Transitive`: 依存先経由で間接参照
  - `Unused`: 未使用（削除候補）
- **バージョン制約**: `manifest.json` の `packageUpdaterConfig` セクションで制御

```json
{
  "packageUpdaterConfig": {
    "versionConstraints": {
      "com.some.package": {
        "pin": true,
        "maxVersion": "2.0.0"
      }
    }
  }
}
```

---

### PR Watcher

**メニュー**: MusaWindow > Terpsichore > Watcher タブ

PRの監視・自動処理を行う。

#### 有効化

`unity_command_server.json` の `role` を `"watcher"` に設定すると自動起動。

#### UI

- **ステータス**: Running / Stopped、Active PR数、最終ポーリング時刻
- **操作**: Start / Stop / Clear Log
- **ログ**: タイムスタンプ付きログ表示（自動スクロール）

---

### AssetManager（S3ベース）

#### ダウンロード

**メニュー**: `VTNTools > Asset Manager > Download Assets`

1. **Fetch Asset List** でLambda APIからアセット一覧を取得
2. 未インストールアセットにチェック
3. **Download Selected** または **Download All New**
4. Presigned URL経由でS3からダウンロード → 自動インポート
5. インストール記録は `Assets/ThirdParty/.asset_registry.json` に保存

#### アップロード

**メニュー**: `VTNTools > Asset Manager > Upload Assets`

1. .unitypackage ファイルを選択
2. アセット名・バージョン・説明を入力
3. **Upload to S3** で Lambda経由のPresigned URLでアップロード

> **NOTE**: アップロードにはAssetManagerConfigで設定した権限が必要。

---

## 設定ファイル一覧

| ファイル | パス | 用途 |
|---|---|---|
| `musa_settings.json` | `musa/` | Google Drive認証・アセット設定 |
| `unity_command_server.json` | `musa/terpsichore/` | UnityCommandServer設定 |
| `settings.json` | `musa/melpomene/` | Melpomene設定（OAuth情報等） |
| `google_token.json` | `musa/melpomene/` | Google OAuthトークン |
| `asset_catalog_cache.json` | プロジェクトルート | ローカルカタログキャッシュ |
| `.asset_registry.json` | `Assets/ThirdParty/` | S3アセットインストール記録 |
| `AssetManagerConfig.asset` | Editor内 | AssetManager設定（ScriptableObject） |

## 実装ファイル

| ファイル | 機能 |
|---|---|
| `unity/Assets/Musa/MusaWindow.cs` | メインウィンドウUI |
| `unity/Assets/Musa/MusaGlobalSettings.cs` | グローバル設定管理 |
| `unity/Assets/Musa/UnityCommandServer.cs` | HTTPコマンドサーバ |
| `unity/Assets/Musa/RuntimeCommandBridge.cs` | ランタイムコマンド連携 |
| `unity/Assets/Musa/GoogleDriveAssetImporter.cs` | Google Driveアセットインポート |
| `unity/Assets/Musa/GoogleDriveAssetUploader.cs` | Google Driveアセットアップロード |
| `unity/Assets/Musa/AssetImporterStartupChecker.cs` | 起動時アセットチェック |
| `unity/Assets/Musa/PackageUpdaterWindow.cs` | パッケージ更新管理 |
| `unity/Assets/Musa/PRWatcherWindow.cs` | PR Watcher UI |
| `unity/Assets/Musa/ClaudeCodeBridge/ClaudeCodeBridgeWindow.cs` | Claude Code Bridge UI |
| `unity/Assets/Musa/ClaudeCodeBridge/ClaudeCodeBridgeClient.cs` | Bridge APIクライアント |
| `unity/Assets/Scripts/BaseSystemEditor/Editor/AssetManager/AssetDownloader.cs` | S3アセットダウンロード |
| `unity/Assets/Scripts/BaseSystemEditor/Editor/AssetManager/AssetUploader.cs` | S3アセットアップロード |
| `unity/Assets/Scripts/BaseSystemEditor/Editor/AssetManager/AssetData.cs` | データ構造定義 |
| `unity/Assets/Scripts/BaseSystemEditor/Editor/AssetManager/AssetManagerConfig.cs` | AssetManager設定 |
