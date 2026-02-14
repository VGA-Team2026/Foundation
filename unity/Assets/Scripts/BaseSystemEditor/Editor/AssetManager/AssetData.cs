using System;
using System.Collections.Generic;

namespace AssetManagerEditor
{
    /// <summary>
    /// 個別アセット情報
    /// </summary>
    [Serializable]
    public class AssetInfo
    {
        public string asset_hash;
        public string asset_name;
        public string version;
        public string description;
        public string created_at;
        public string updated_at;
        public string s3_bucket;
        public string s3_key;
        public string download_url;
        public string error;
    }

    /// <summary>
    /// アセット一覧取得レスポンス
    /// </summary>
    [Serializable]
    public class AssetListResponse
    {
        public string project_id;
        public List<AssetInfo> assets;
        public int count;
    }

    /// <summary>
    /// アセット登録リクエスト
    /// </summary>
    [Serializable]
    public class AssetRegisterRequest
    {
        public string project_id;
        public string asset_name;
        public string version;
        public string description;
    }

    /// <summary>
    /// アセット登録レスポンス
    /// </summary>
    [Serializable]
    public class AssetRegisterResponse
    {
        public string asset_hash;
        public string upload_url;
        public string s3_bucket;
        public string s3_key;
    }

    /// <summary>
    /// エラーレスポンス
    /// </summary>
    [Serializable]
    public class ErrorResponse
    {
        public string error;
    }

    /// <summary>
    /// ローカルにインストール済みのアセット情報を保存
    /// </summary>
    [Serializable]
    public class LocalAssetRegistry
    {
        public List<InstalledAsset> installed_assets = new List<InstalledAsset>();
    }

    /// <summary>
    /// インストール済みアセット情報
    /// </summary>
    [Serializable]
    public class InstalledAsset
    {
        public string asset_hash;
        public string asset_name;
        public string version;
        public string installed_at;
        public string install_path;
    }
}
