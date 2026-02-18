# Google Driveへのアクセス

Google Drive APIにアクセスする際のスキル。

## 認証方式

Lambda Function URLの `/token` エンドポイントからアクセストークンを取得する（サービスアカウント方式）。
認証URLは `musa/melpomene/settings.json` の `googleAuthUrl` に設定されている。

## アクセストークンの取得

```bash
# Lambda経由でアクセストークンを取得（1時間有効）
curl -s "https://<lambda-function-url>/token"
```

レスポンス:
```json
{
  "access_token": "ya29.xxx",
  "token_type": "Bearer",
  "expires_in": 3600
}
```

## 手順

1. `musa/melpomene/settings.json` から `googleAuthUrl` を読み込む
2. `GET {googleAuthUrl}/token` でアクセストークンを取得する
3. 取得した `access_token` を使ってAPIにアクセスする

## フォルダID

アップロード先フォルダIDは `musa/melpomene/settings.json`（Git管理対象）で管理:

```json
{
    "googleAuthUrl": "https://<lambda-function-url>",
    "googleDriveFolderIdLog": "フォルダID",
    "googleDriveFolderIdVideo": "フォルダID"
}
```

## API例

### ファイル一覧取得
```bash
curl -s -H "Authorization: Bearer ACCESS_TOKEN" \
  "https://www.googleapis.com/drive/v3/files?q='FOLDER_ID'+in+parents&fields=files(id,name,mimeType,createdTime)"
```

### ファイルアップロード
```bash
curl -s -X POST \
  -H "Authorization: Bearer ACCESS_TOKEN" \
  -F "metadata={\"name\":\"filename\",\"parents\":[\"FOLDER_ID\"]};type=application/json" \
  -F "file=@/path/to/file" \
  "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart"
```

### ファイル共有リンク取得
```bash
# 閲覧権限を付与
curl -s -X POST \
  -H "Authorization: Bearer ACCESS_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"role":"reader","type":"anyone"}' \
  "https://www.googleapis.com/drive/v3/files/FILE_ID/permissions"

# 共有URL
# https://drive.google.com/file/d/FILE_ID/view?usp=sharing
```
