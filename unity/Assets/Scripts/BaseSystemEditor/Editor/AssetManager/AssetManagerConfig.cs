using UnityEngine;

namespace AssetManagerEditor
{
    /// <summary>
    /// アセットマネージャーの設定を保持するScriptableObject
    /// </summary>
    [CreateAssetMenu(fileName = "AssetManagerConfig", menuName = "VTNTools/Asset Manager Config")]
    public class AssetManagerConfig : ScriptableObject
    {
        [Header("API設定")]
        [Tooltip("Lambda APIのエンドポイントURL")]
        public string ApiEndpoint = "https://your-api.execute-api.region.amazonaws.com/dev";

        [Tooltip("API Key")]
        public string ApiKey = "";

        [Header("プロジェクト設定")]
        [Tooltip("このプロジェクトのID")]
        public string ProjectId = "Foundation";

        [Header("ローカル設定")]
        [Tooltip("アセットの展開先（Assets/からの相対パス）")]
        public string ImportTargetPath = "ThirdParty";

        [Tooltip("ダウンロードしたパッケージの一時保存先")]
        public string TempDownloadPath = "Temp/AssetDownloads";

        [Header("アップロード権限")]
        [Tooltip("アップロード権限を持つユーザー名のリスト")]
        public string[] AuthorizedUploadUsers = new string[] { };

        [Tooltip("アップロード権限の秘密キー（環境変数から取得推奨）")]
        public string UploadSecretKey = "";

        /// <summary>
        /// アセットの完全な展開先パスを取得
        /// </summary>
        public string FullImportPath => System.IO.Path.Combine(Application.dataPath, ImportTargetPath);

        /// <summary>
        /// ダウンロードの一時保存先の完全パスを取得
        /// </summary>
        public string FullTempPath => System.IO.Path.Combine(Application.dataPath, "..", TempDownloadPath);

        /// <summary>
        /// 現在のユーザーがアップロード権限を持っているか確認
        /// </summary>
        public bool HasUploadPermission()
        {
            // 環境変数から秘密キーを確認
            string envKey = System.Environment.GetEnvironmentVariable("ASSET_MANAGER_UPLOAD_KEY");
            if (!string.IsNullOrEmpty(envKey) && envKey == UploadSecretKey)
            {
                return true;
            }

            // ユーザー名で確認
            string currentUser = System.Environment.UserName;
            foreach (var user in AuthorizedUploadUsers)
            {
                if (user == currentUser)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 設定のインスタンスをロードまたは作成
        /// </summary>
        public static AssetManagerConfig LoadOrCreate()
        {
            const string configPath = "Assets/Scripts/BaseSystemEditor/Editor/AssetManager/AssetManagerConfig.asset";

            var config = UnityEditor.AssetDatabase.LoadAssetAtPath<AssetManagerConfig>(configPath);
            if (config == null)
            {
                config = CreateInstance<AssetManagerConfig>();

                // ディレクトリが存在しない場合は作成
                string directory = System.IO.Path.GetDirectoryName(configPath);
                if (!System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                UnityEditor.AssetDatabase.CreateAsset(config, configPath);
                UnityEditor.AssetDatabase.SaveAssets();
                Debug.Log($"AssetManagerConfig created at: {configPath}");
            }

            return config;
        }
    }
}
