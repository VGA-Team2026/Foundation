# Unity Asset Manager - AWS Lambda

Unity プロジェクト用のアセット管理システム（サーバーサイド）

## 概要

- **DynamoDB**: プロジェクトごとのアセット情報を管理
- **S3**: アセットファイル（.unitypackage）を保存
- **Lambda + API Gateway**: RESTful API を提供

## デプロイ

### 前提条件

- AWS CLI がインストール・設定済み
- AWS SAM CLI がインストール済み
- 適切な IAM 権限

### デプロイ手順

```bash
# 1. ディレクトリに移動
cd aws/lambda/asset-manager

# 2. ビルド
sam build

# 3. デプロイ（初回）
sam deploy --guided

# 4. デプロイ（2回目以降）
sam deploy
```

### デプロイ時のパラメータ

| パラメータ | 説明 | デフォルト |
|-----------|------|-----------|
| Environment | dev/staging/prod | dev |
| S3BucketName | S3バケット名のプレフィックス | unity-assets-bucket |
| PresignedUrlExpiration | 署名付きURLの有効期限（秒） | 3600 |

## API エンドポイント

### GET /assets

プロジェクトのアセット一覧を取得

```bash
curl -X GET \
  "https://{api-id}.execute-api.{region}.amazonaws.com/{stage}/assets?project_id=Foundation" \
  -H "x-api-key: YOUR_API_KEY"
```

**レスポンス:**
```json
{
  "project_id": "Foundation",
  "assets": [
    {
      "asset_hash": "abc123...",
      "asset_name": "SomeAsset",
      "version": "1.0.0",
      "description": "Asset description",
      "download_url": "https://s3-presigned-url...",
      "created_at": "2024-01-01T00:00:00Z"
    }
  ],
  "count": 1
}
```

### POST /assets

新規アセットを登録（アップロードURL取得）

```bash
curl -X POST \
  "https://{api-id}.execute-api.{region}.amazonaws.com/{stage}/assets" \
  -H "x-api-key: YOUR_API_KEY" \
  -H "Content-Type: application/json" \
  -d '{
    "project_id": "Foundation",
    "asset_name": "NewAsset",
    "version": "1.0.0",
    "description": "New asset description"
  }'
```

**レスポンス:**
```json
{
  "asset_hash": "abc123...",
  "upload_url": "https://s3-presigned-url-for-upload...",
  "s3_bucket": "unity-assets-bucket-dev",
  "s3_key": "assets/abc123..."
}
```

### DELETE /assets

アセットを削除

```bash
curl -X DELETE \
  "https://{api-id}.execute-api.{region}.amazonaws.com/{stage}/assets?project_id=Foundation&asset_hash=abc123" \
  -H "x-api-key: YOUR_API_KEY"
```

## DynamoDB テーブル構造

| 属性名 | 型 | 説明 |
|--------|-----|------|
| project_id (PK) | String | プロジェクト識別子 |
| asset_hash (SK) | String | アセットのハッシュ値（ファイル名にも使用） |
| asset_name | String | アセット名 |
| s3_bucket | String | S3バケット名 |
| s3_key | String | S3キー |
| version | String | バージョン |
| description | String | 説明 |
| created_at | String | 作成日時 (ISO 8601) |
| updated_at | String | 更新日時 (ISO 8601) |

## API Key の取得

デプロイ後、API Key を取得:

```bash
# API Key ID を確認
aws cloudformation describe-stacks \
  --stack-name your-stack-name \
  --query "Stacks[0].Outputs[?OutputKey=='ApiKeyId'].OutputValue" \
  --output text

# API Key の値を取得
aws apigateway get-api-key \
  --api-key {api-key-id} \
  --include-value \
  --query "value" \
  --output text
```

## Unity 側の設定

1. Unity Editor で `VTNTools > Asset Manager > Download Assets` を開く
2. Config ボタンで `AssetManagerConfig` を選択
3. 以下を設定:
   - **API Endpoint**: デプロイ後に出力される URL
   - **API Key**: 上記で取得した値
   - **Project ID**: プロジェクト識別子

## 権限設定（アップロード用）

アップロード権限は以下のいずれかで付与:

1. 環境変数 `ASSET_MANAGER_UPLOAD_KEY` に秘密キーを設定
2. `AssetManagerConfig` の `AuthorizedUploadUsers` にユーザー名を追加

## トラブルシューティング

### CORS エラー

API Gateway の CORS 設定を確認してください。

### 403 Forbidden

API Key が正しく設定されているか確認してください。

### Presigned URL の期限切れ

`PresignedUrlExpiration` パラメータを調整してください。
