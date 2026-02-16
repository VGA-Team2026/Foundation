"""
Asset Manager Lambda Function
プロジェクトに登録されているアセット情報をDynamoDBから取得し返却する

DynamoDB テーブル構造:
- テーブル名: AssetRegistry
- パーティションキー: project_id (String)
- ソートキー: asset_hash (String)

アイテム構造:
{
    "project_id": "ProjectName",
    "asset_hash": "abc123...",  # ファイル名としても使用
    "asset_name": "SomeAsset.unitypackage",
    "s3_bucket": "your-bucket-name",
    "s3_key": "assets/abc123...",
    "version": "1.0.0",
    "description": "Asset description",
    "created_at": "2024-01-01T00:00:00Z",
    "updated_at": "2024-01-01T00:00:00Z"
}
"""

import json
import boto3
import os
import hashlib
import time
from datetime import datetime
from botocore.exceptions import ClientError

# 環境変数
TABLE_NAME = os.environ.get('ASSET_TABLE_NAME', 'AssetRegistry')
S3_BUCKET = os.environ.get('S3_BUCKET', 'unity-assets-bucket')
PRESIGNED_URL_EXPIRATION = int(os.environ.get('PRESIGNED_URL_EXPIRATION', 3600))  # 1時間

# AWSクライアント
dynamodb = boto3.resource('dynamodb')
s3_client = boto3.client('s3')
table = dynamodb.Table(TABLE_NAME)


def generate_response(status_code, body):
    """APIレスポンスを生成"""
    return {
        'statusCode': status_code,
        'headers': {
            'Content-Type': 'application/json',
            'Access-Control-Allow-Origin': '*',
            'Access-Control-Allow-Headers': 'Content-Type,X-Api-Key,Authorization',
            'Access-Control-Allow-Methods': 'GET,POST,OPTIONS'
        },
        'body': json.dumps(body, ensure_ascii=False, default=str)
    }


def get_project_assets(project_id):
    """
    指定プロジェクトのアセット一覧を取得
    S3からダウンロードするためのPresigned URLも生成して返す
    """
    try:
        response = table.query(
            KeyConditionExpression=boto3.dynamodb.conditions.Key('project_id').eq(project_id)
        )

        assets = []
        for item in response.get('Items', []):
            asset_info = {
                'asset_hash': item['asset_hash'],
                'asset_name': item.get('asset_name', ''),
                'version': item.get('version', '1.0.0'),
                'description': item.get('description', ''),
                'created_at': item.get('created_at', ''),
                'updated_at': item.get('updated_at', ''),
                's3_bucket': item.get('s3_bucket', S3_BUCKET),
                's3_key': item.get('s3_key', ''),
            }

            # Presigned URL を生成
            try:
                presigned_url = s3_client.generate_presigned_url(
                    'get_object',
                    Params={
                        'Bucket': asset_info['s3_bucket'],
                        'Key': asset_info['s3_key']
                    },
                    ExpiresIn=PRESIGNED_URL_EXPIRATION
                )
                asset_info['download_url'] = presigned_url
            except ClientError as e:
                asset_info['download_url'] = None
                asset_info['error'] = str(e)

            assets.append(asset_info)

        return assets

    except ClientError as e:
        raise Exception(f"DynamoDB query failed: {str(e)}")


def register_asset(project_id, asset_data):
    """
    新しいアセットを登録
    asset_data には asset_name, s3_key, version, description が必要
    """
    now = datetime.utcnow().isoformat() + 'Z'

    # ハッシュ値を生成（ファイル名として使用）
    hash_source = f"{project_id}_{asset_data['asset_name']}_{asset_data.get('version', '1.0.0')}_{time.time()}"
    asset_hash = hashlib.sha256(hash_source.encode()).hexdigest()[:32]

    item = {
        'project_id': project_id,
        'asset_hash': asset_hash,
        'asset_name': asset_data['asset_name'],
        's3_bucket': asset_data.get('s3_bucket', S3_BUCKET),
        's3_key': asset_data.get('s3_key', f"assets/{asset_hash}"),
        'version': asset_data.get('version', '1.0.0'),
        'description': asset_data.get('description', ''),
        'created_at': now,
        'updated_at': now
    }

    table.put_item(Item=item)

    # アップロード用のPresigned URLを生成
    presigned_url = s3_client.generate_presigned_url(
        'put_object',
        Params={
            'Bucket': item['s3_bucket'],
            'Key': item['s3_key'],
            'ContentType': 'application/octet-stream'
        },
        ExpiresIn=PRESIGNED_URL_EXPIRATION
    )

    return {
        'asset_hash': asset_hash,
        'upload_url': presigned_url,
        's3_bucket': item['s3_bucket'],
        's3_key': item['s3_key']
    }


def delete_asset(project_id, asset_hash):
    """アセットを削除"""
    try:
        # まずアセット情報を取得
        response = table.get_item(
            Key={
                'project_id': project_id,
                'asset_hash': asset_hash
            }
        )

        if 'Item' not in response:
            return False, "Asset not found"

        item = response['Item']

        # S3からファイルを削除
        try:
            s3_client.delete_object(
                Bucket=item.get('s3_bucket', S3_BUCKET),
                Key=item['s3_key']
            )
        except ClientError:
            pass  # S3削除失敗は無視

        # DynamoDBからレコードを削除
        table.delete_item(
            Key={
                'project_id': project_id,
                'asset_hash': asset_hash
            }
        )

        return True, "Asset deleted successfully"

    except ClientError as e:
        return False, str(e)


def lambda_handler(event, context):
    """
    Lambda エントリーポイント

    GET /assets?project_id=XXX - プロジェクトのアセット一覧を取得
    POST /assets - 新しいアセットを登録（アップロードURL取得）
    DELETE /assets?project_id=XXX&asset_hash=YYY - アセットを削除
    """
    try:
        http_method = event.get('httpMethod', event.get('requestContext', {}).get('http', {}).get('method', 'GET'))

        # クエリパラメータ取得
        query_params = event.get('queryStringParameters') or {}

        # リクエストボディ取得
        body = {}
        if event.get('body'):
            body = json.loads(event['body']) if isinstance(event['body'], str) else event['body']

        if http_method == 'OPTIONS':
            return generate_response(200, {'message': 'OK'})

        if http_method == 'GET':
            # アセット一覧取得
            project_id = query_params.get('project_id')
            if not project_id:
                return generate_response(400, {'error': 'project_id is required'})

            assets = get_project_assets(project_id)
            return generate_response(200, {
                'project_id': project_id,
                'assets': assets,
                'count': len(assets)
            })

        elif http_method == 'POST':
            # アセット登録（アップロードURL取得）
            project_id = body.get('project_id')
            if not project_id:
                return generate_response(400, {'error': 'project_id is required'})

            if not body.get('asset_name'):
                return generate_response(400, {'error': 'asset_name is required'})

            result = register_asset(project_id, body)
            return generate_response(201, result)

        elif http_method == 'DELETE':
            # アセット削除
            project_id = query_params.get('project_id')
            asset_hash = query_params.get('asset_hash')

            if not project_id or not asset_hash:
                return generate_response(400, {'error': 'project_id and asset_hash are required'})

            success, message = delete_asset(project_id, asset_hash)
            if success:
                return generate_response(200, {'message': message})
            else:
                return generate_response(404, {'error': message})

        else:
            return generate_response(405, {'error': 'Method not allowed'})

    except Exception as e:
        return generate_response(500, {'error': str(e)})
