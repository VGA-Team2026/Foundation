# GoogleDriveAssetImporter

## 概要

Google Driveに保存された大容量アセット（unitypackage）をダウンロードしてUnityプロジェクトにインポートするエディタツール。カタログベースの管理により、更新分または未取得分のみをダウンロードする。

## 背景

- フォントファイル等の大容量アセット（~100GB）をGitリポジトリに含めるとclone/pull時間が長くなる
- Google Driveで外部管理し、必要時にのみダウンロードする仕組みが必要

## 機能

### カタログ管理
- Google Drive上にJSONカタログファイルを配置
- カタログにはアセット一覧（名前、FileID、バージョン、MD5、サイズ）を記載
- ローカルキャッシュでダウンロード履歴を管理

### 更新確認
- カタログをダウンロードして解析
- MD5/バージョンでローカルとの差分を検出
- 更新必要・最新・未取得のステータスを表示

### ダウンロード＆インポート
- 更新が必要なアセットのみをダウンロード
- MD5チェックでファイル整合性を確認
- UnityPackageとして自動インポート

## クラス設計

```
GoogleDriveAssetImporter : EditorWindow
├── Fields
│   ├── _catalogFileId : string       // カタログのGoogleDrive FileID
│   ├── _statusMessage : string       // 進捗メッセージ
│   ├── _progress : float             // 進捗率
│   ├── _isProcessing : bool          // 処理中フラグ
│   ├── _tokenData : TokenData        // OAuth認証情報
│   └── _catalogCache : AssetCatalog  // カタログキャッシュ
│
├── Public Methods
│   ├── CheckForUpdatesAsync()        // 更新確認
│   └── DownloadAndImportAllAsync()   // 全てダウンロード＆インポート
│
├── Private Methods - Authentication
│   ├── EnsureAccessTokenAsync()      // アクセストークン確保
│   └── RefreshAccessTokenAsync()     // トークン更新
│
├── Private Methods - Download
│   ├── DownloadTextFileAsync()       // カタログ等テキストファイル取得
│   └── DownloadFileAsync()           // unitypackageバイナリ取得
│
├── Private Methods - Catalog
│   ├── GetAssetStatus()              // アセットの状態判定
│   └── UpdateLocalAssetInfo()        // ローカル情報更新
│
└── Data Classes
    ├── TokenData                     // OAuth認証情報
    ├── TokenResponse                 // トークン更新レスポンス
    ├── AssetCatalog                  // カタログ全体
    ├── AssetEntry                    // アセットエントリ（リモート）
    └── LocalAssetEntry               // アセットエントリ（ローカル）
```

## 認証

### 認証情報の取得元（優先順）
1. 環境変数
   - `GOOGLE_CLIENT_ID`
   - `GOOGLE_CLIENT_SECRET`
   - `GOOGLE_REFRESH_TOKEN`
2. プロジェクトルートの `google_drive_token.json`

### トークンリフレッシュ
- アクセストークンは一時的なため、refresh_tokenから都度取得
- 401エラー時は自動でトークン更新してリトライ

## カタログファイル形式

Google Drive上に配置するJSONファイル:

```json
{
  "version": "1.0.0",
  "updatedAt": "2026-01-27",
  "assets": [
    {
      "name": "FontAssets",
      "fileId": "1ABC...xyz",
      "version": "1.0.0",
      "md5": "a1b2c3d4e5f6...",
      "size": 1073741824,
      "description": "フォントファイル一式"
    }
  ]
}
```

## ローカルキャッシュファイル

プロジェクトルートの `asset_catalog_cache.json`:

```json
{
  "version": "1.0.0",
  "updatedAt": "2026-01-27",
  "assets": [...],
  "localAssets": [
    {
      "name": "FontAssets",
      "version": "1.0.0",
      "md5": "a1b2c3d4e5f6...",
      "importedAt": "2026-01-27 12:00:00"
    }
  ]
}
```

## 使用方法

1. **認証設定**
   - Google Cloud Consoleでプロジェクト作成
   - OAuth 2.0クライアントIDを作成
   - refresh_tokenを取得
   - 環境変数または`google_drive_token.json`に設定

2. **カタログ作成**
   - Google Driveにカタログ用JSONファイルを作成
   - unitypackageをアップロードしFileIDを取得
   - カタログにアセット情報を追記

3. **インポート**
   - Unity: `Tools > Musa > Asset Importer`
   - カタログFileIDを入力
   - 「カタログを取得して更新確認」で差分確認
   - 「全てダウンロード＆インポート」で取得

## 定数

| 定数名 | 値 | 説明 |
|--------|-----|------|
| REQUEST_TIMEOUT | 60秒 | 通常リクエストタイムアウト |
| DOWNLOAD_TIMEOUT | 300秒 | ダウンロードタイムアウト |
| DOWNLOAD_FOLDER | Downloads/ExternalAssets | ダウンロード保存先 |

## ファイル

- `unity/Assets/Musa/GoogleDriveAssetImporter.cs`

## 依存

- UniTask（非同期処理）
- UnityWebRequest（HTTP通信）
