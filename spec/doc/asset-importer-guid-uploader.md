# Asset Importer - GUID判定・外部アップローダー対応

## 概要
100MB超の非更新系ファイル（3Dモデル、テクスチャ等）をGoogle Drive経由で別管理する仕組み。
既存の`GoogleDriveAssetImporter`をベースに、GUIDベース判定・起動時チェック・外部アップローダーを追加。

## アーキテクチャ

### コンポーネント構成
```
scripts/asset-uploader.js       ← Node.jsアップローダー（カタログ自動管理）
  ↓ upload
Google Drive
  ├── asset_catalog.json        ← カタログ（アセット一覧 + GUID情報）
  └── *.unitypackage            ← アセットファイル本体
  ↓ download
GoogleDriveAssetImporter.cs     ← Unity EditorWindow（DL & インポート）
AssetImporterStartupChecker.cs  ← 起動時不足チェック
```

### カタログJSON構造
```json
{
  "version": "1.0.0",
  "updatedAt": "2026-02-05T00:00:00.000Z",
  "assets": [
    {
      "name": "ExampleModel",
      "fileId": "google-drive-file-id",
      "version": "1.0.0",
      "md5": "abc123...",
      "size": 104857600,
      "description": "サンプルモデル",
      "guids": ["guid1", "guid2", "..."]
    }
  ]
}
```

## 判定ロジック

### GUIDベース判定（優先）
`AssetEntry.guids`が存在する場合:
1. 各GUIDに対して`AssetDatabase.GUIDToAssetPath()`を実行
2. 全GUIDが存在 → `UpToDate`（バージョン/MD5更新チェック付き）
3. 一部存在 → `PartiallyImported`
4. 全て未存在 → `NotDownloaded`

### MD5/バージョンフォールバック
`guids`が空の場合、従来のローカルキャッシュベース判定を使用。

## ファイル一覧

| ファイル | 種別 | 説明 |
|---|---|---|
| `unity/Assets/Musa/GoogleDriveAssetImporter.cs` | 改修 | GUID判定・settings.json読み書き・個別DL・GUI改善 |
| `unity/Assets/Musa/AssetImporterStartupChecker.cs` | 新規 | 起動時GUID不足チェック |
| `scripts/asset-uploader.js` | 新規 | ラッパースクリプト |
| `scripts/asset-uploader/index.js` | 新規 | アップローダー本体 |
| `scripts/asset-uploader/package.json` | 新規 | 依存定義（tar） |
| `musa/melpomene/settings.json` | 改修 | `googleDriveFolderIdAsset`, `assetCatalogFileId` 追加 |
| `.gitignore` | 改修 | キャッシュ・DL除外追加 |

## 使用方法

### アセットアップロード
```bash
cd scripts/asset-uploader && npm install
node scripts/asset-uploader.js upload path/to/Model.unitypackage --name MyModel --version 1.0.0
```

### カタログ確認
```bash
node scripts/asset-uploader.js catalog
```

### Unity側
1. `Tools > Musa > Asset Importer` でウィンドウを開く
2. カタログFileIDが自動設定済み（settings.json経由）
3. 「カタログを取得して更新確認」で差分チェック
4. 個別DLボタンまたは「全てダウンロード＆インポート」で取得

### 起動時チェック
- Unity起動時に`AssetImporterStartupChecker`が自動実行
- ローカルキャッシュのGUID情報を参照（ネットワーク不要）
- 不足アセットがあればダイアログで通知
