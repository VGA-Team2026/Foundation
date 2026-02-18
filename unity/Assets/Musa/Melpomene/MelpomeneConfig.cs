#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using Cysharp.Threading.Tasks;

namespace Melpomene
{
    /// <summary>
    /// Melpomene設定
    /// NOTE: 3つのJSONファイルから設定を読み込む
    ///   - Melpomene/serverconf.json     (サーバ設定、gitignore対象)
    ///   - Melpomene/settings.json       (プロジェクト共通設定、Git管理)
    ///   - Melpomene/settings.local.json (個人設定、gitignore対象)
    /// </summary>
    public class MelpomeneConfig
    {
        /// <summary>
        /// Melpomeneのバージョン
        /// NOTE: Issue互換性のために使用
        /// </summary>
        public const string Version = "1.0.0";

        #region File Paths

        // NOTE: Application.dataPath = "unity/Assets" → ".." で "unity/"（Unityプロジェクトルート）
        //       さらに ".." でリポジトリルート
        private static string UnityRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        private static string ConfigDir
        {
            get
            {
                // NOTE: リポジトリルート/musa/melpomene/
                return Path.Combine(RepoRoot, "musa", "melpomene");
            }
        }

        // NOTE: 旧パス（自動移行用）unity/Melpomene/
        private static string LegacyConfigDir => Path.Combine(UnityRoot, "Melpomene");

        private static string ServerConfPath => Path.Combine(ConfigDir, "serverconf.json");
        private static string ProjectSettingsPath => Path.Combine(ConfigDir, "settings.json");
        private static string LocalSettingsPath => Path.Combine(ConfigDir, "settings.local.json");

        #endregion

        #region Internal Data

        [Serializable]
        private class ServerConf
        {
            public string serverHost = "localhost";
            public int serverPort = 31280;
            public bool enableObs = false;
            public string obsHost = "localhost";
            public int obsPort = 4455;
            public bool obsUsePassword = false;
            public string obsPassword = "";
        }

        [Serializable]
        private class ProjectSettings
        {
            public string repositoryOwner = "";
            public string repositoryName = "";
            public string[] defaultLabels = new string[] { "melpomene", "auto-generated" };
            public string defaultPriority = "Medium";
            public string defaultCategory = "Bug";
            public int cacheDurationMinutes = 10;
            public string googleDriveFolderIdLog = "";
            public string googleDriveFolderIdVideo = "";
            public string googleAuthUrl = "";
        }

        [Serializable]
        private class LocalSettings
        {
            public string accessToken = "";
            public string defaultUserName = "";
            public bool enableAltClickShortcut = true;
            public bool enableTicketDisplay = true;
            public bool eurekaAutoRecord = false;
            public string eurekaKeyBinding = "Pause";
            public bool autoRefreshCache = true;
            public bool enableNotificationPolling = true;
            public bool skipSetupWizard = false;
        }

        private ServerConf _server = new ServerConf();
        private ProjectSettings _project = new ProjectSettings();
        private LocalSettings _local = new LocalSettings();

        #endregion

        #region Forwarding Properties — Server

        public string serverHost
        {
            get => _server.serverHost;
            set => _server.serverHost = value;
        }

        public int serverPort
        {
            get => _server.serverPort;
            set => _server.serverPort = value;
        }

        public bool enableObs
        {
            get => _server.enableObs;
            set => _server.enableObs = value;
        }

        public string obsHost
        {
            get => _server.obsHost;
            set => _server.obsHost = value;
        }

        public int obsPort
        {
            get => _server.obsPort;
            set => _server.obsPort = value;
        }

        public bool obsUsePassword
        {
            get => _server.obsUsePassword;
            set => _server.obsUsePassword = value;
        }

        public string obsPassword
        {
            get => _server.obsPassword;
            set => _server.obsPassword = value;
        }

        #endregion

        #region Forwarding Properties — Project

        public string repositoryOwner
        {
            get => _project.repositoryOwner;
            set => _project.repositoryOwner = value;
        }

        public string repositoryName
        {
            get => _project.repositoryName;
            set => _project.repositoryName = value;
        }

        public string[] defaultLabels
        {
            get => _project.defaultLabels;
            set => _project.defaultLabels = value;
        }

        public MelpomenePriority defaultPriority
        {
            get => Enum.TryParse<MelpomenePriority>(_project.defaultPriority, true, out var p) ? p : MelpomenePriority.Medium;
            set => _project.defaultPriority = value.ToString();
        }

        public MelpomeneCategory defaultCategory
        {
            get => Enum.TryParse<MelpomeneCategory>(_project.defaultCategory, true, out var c) ? c : MelpomeneCategory.Bug;
            set => _project.defaultCategory = value.ToString();
        }

        public int cacheDurationMinutes
        {
            get => _project.cacheDurationMinutes;
            set => _project.cacheDurationMinutes = value;
        }

        public string googleDriveFolderIdLog
        {
            get => _project.googleDriveFolderIdLog;
            set => _project.googleDriveFolderIdLog = value;
        }

        public string googleDriveFolderIdVideo
        {
            get => _project.googleDriveFolderIdVideo;
            set => _project.googleDriveFolderIdVideo = value;
        }

        public string googleAuthUrl
        {
            get => _project.googleAuthUrl;
            set => _project.googleAuthUrl = value;
        }

        #endregion

        #region Forwarding Properties — Local

        public string accessToken
        {
            get => _local.accessToken;
            set => _local.accessToken = value;
        }

        public string defaultUserName
        {
            get => _local.defaultUserName;
            set => _local.defaultUserName = value;
        }

        public bool enableAltClickShortcut
        {
            get => _local.enableAltClickShortcut;
            set => _local.enableAltClickShortcut = value;
        }

        public bool enableTicketDisplay
        {
            get => _local.enableTicketDisplay;
            set => _local.enableTicketDisplay = value;
        }

        public bool eurekaAutoRecord
        {
            get => _local.eurekaAutoRecord;
            set => _local.eurekaAutoRecord = value;
        }

        public string eurekaKeyBinding
        {
            get => _local.eurekaKeyBinding;
            set => _local.eurekaKeyBinding = value;
        }

        public bool autoRefreshCache
        {
            get => _local.autoRefreshCache;
            set => _local.autoRefreshCache = value;
        }

        public bool enableNotificationPolling
        {
            get => _local.enableNotificationPolling;
            set => _local.enableNotificationPolling = value;
        }

        public bool skipSetupWizard
        {
            get => _local.skipSetupWizard;
            set => _local.skipSetupWizard = value;
        }

        #endregion

        #region Computed Properties

        /// <summary>GitHub API URL</summary>
        public string ApiBaseUrl => $"https://api.github.com/repos/{repositoryOwner}/{repositoryName}";

        /// <summary>Melpomene Server URL</summary>
        public string ServerUrl => $"http://{serverHost}:{serverPort}";

        /// <summary>設定が有効かどうか</summary>
        public bool IsValid =>
            !string.IsNullOrEmpty(repositoryOwner) &&
            !string.IsNullOrEmpty(repositoryName) &&
            !string.IsNullOrEmpty(accessToken);

        #endregion

        #region Singleton

        private static MelpomeneConfig _instance;

        /// <summary>
        /// 設定を取得または作成
        /// NOTE: 旧ScriptableObject版と同じシグネチャを維持
        /// </summary>
        public static MelpomeneConfig GetOrCreateConfig()
        {
            if (_instance == null)
            {
                _instance = new MelpomeneConfig();
                _instance.Load();
            }
            return _instance;
        }

        /// <summary>
        /// キャッシュをクリアして次回アクセス時に再読み込みさせる
        /// </summary>
        public static void ReloadConfig()
        {
            _instance = null;
        }

        #endregion

        #region Load / Save

        private void Load()
        {
            EnsureConfigDir();
            MigrateJsonFilesFromLegacyDir();

            _server = LoadJson<ServerConf>(ServerConfPath) ?? new ServerConf();
            _project = LoadJson<ProjectSettings>(ProjectSettingsPath) ?? new ProjectSettings();
            _local = LoadJson<LocalSettings>(LocalSettingsPath) ?? new LocalSettings();
        }

        /// <summary>
        /// 旧ディレクトリ(Melpomene/)から新ディレクトリ(musa/melpomene/)へJSONファイルを移動
        /// NOTE: 新ディレクトリにファイルが存在しない場合のみ移動
        /// </summary>
        private static void MigrateJsonFilesFromLegacyDir()
        {
            if (!Directory.Exists(LegacyConfigDir)) return;

            string[] fileNames = { "serverconf.json", "settings.json", "settings.local.json" };
            int moved = 0;

            foreach (var fileName in fileNames)
            {
                string oldPath = Path.Combine(LegacyConfigDir, fileName);
                string newPath = Path.Combine(ConfigDir, fileName);

                if (File.Exists(oldPath) && !File.Exists(newPath))
                {
                    try
                    {
                        File.Move(oldPath, newPath);
                        moved++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[MelpomeneConfig] Failed to move {fileName}: {ex.Message}");
                    }
                }
            }

            if (moved > 0)
            {
                Debug.Log($"[MelpomeneConfig] 設定ファイル {moved} 件を musa/melpomene/ に移動しました");
            }
        }

        /// <summary>
        /// 設定を3つのJSONファイルに書き出す
        /// </summary>
        public void Save()
        {
            EnsureConfigDir();

            SaveJson(ServerConfPath, _server);
            SaveJson(ProjectSettingsPath, _project);
            SaveJson(LocalSettingsPath, _local);
        }

        /// <summary>
        /// サーバ設定のみ保存
        /// </summary>
        public void SaveServerConf()
        {
            EnsureConfigDir();
            SaveJson(ServerConfPath, _server);
        }

        /// <summary>
        /// プロジェクト設定のみ保存
        /// </summary>
        public void SaveProjectSettings()
        {
            EnsureConfigDir();
            SaveJson(ProjectSettingsPath, _project);
        }

        /// <summary>
        /// 個人設定のみ保存
        /// </summary>
        public void SaveLocalSettings()
        {
            EnsureConfigDir();
            SaveJson(LocalSettingsPath, _local);
        }

        private static void EnsureConfigDir()
        {
            if (!Directory.Exists(ConfigDir))
            {
                Directory.CreateDirectory(ConfigDir);
            }
        }

        private static T LoadJson<T>(string path) where T : class, new()
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(path);
                return JsonUtility.FromJson<T>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MelpomeneConfig] Failed to load {Path.GetFileName(path)}: {ex.Message}");
                return null;
            }
        }

        private static void SaveJson<T>(string path, T data)
        {
            try
            {
                var json = JsonUtility.ToJson(data, true);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MelpomeneConfig] Failed to save {Path.GetFileName(path)}: {ex.Message}");
            }
        }

        #endregion

        #region Migration

        // NOTE: 旧ScriptableObjectアセットからの自動移行
        private static readonly string[] LegacyAssetSearchPaths = new[]
        {
            "Assets/ThirdParty/Melpomene/Editor/MelpomeneConfig.asset",
            "Assets/Melpomene/MelpomeneConfig.asset",
            "Assets/Musa/Melpomene/MelpomeneConfig.asset"
        };

        /// <summary>
        /// 旧ScriptableObject(.asset)からJSON設定へ自動移行
        /// NOTE: JSONファイルがデフォルト値のままで旧アセットが存在する場合に実行
        /// </summary>
        [MenuItem("Musa/Melpomene/旧設定から移行")]
        public static void MigrateFromLegacyAsset()
        {
            string assetPath = FindLegacyAsset();
            if (string.IsNullOrEmpty(assetPath))
            {
                EditorUtility.DisplayDialog("移行", "旧設定アセット (.asset) が見つかりません。", "OK");
                return;
            }

            string fullPath = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), assetPath);
            if (!File.Exists(fullPath))
            {
                EditorUtility.DisplayDialog("移行", $"ファイルが存在しません:\n{fullPath}", "OK");
                return;
            }

            var yaml = File.ReadAllText(fullPath);
            var config = GetOrCreateConfig();
            int migrated = 0;

            // NOTE: YAMLから各フィールドを抽出して設定に反映
            string val;

            // --- Project Settings ---
            val = ExtractYamlValue(yaml, "repositoryOwner");
            if (!string.IsNullOrEmpty(val)) { config.repositoryOwner = val; migrated++; }

            val = ExtractYamlValue(yaml, "repositoryName");
            if (!string.IsNullOrEmpty(val)) { config.repositoryName = val; migrated++; }

            var labels = ExtractYamlArray(yaml, "defaultLabels");
            if (labels != null && labels.Length > 0) { config.defaultLabels = labels; migrated++; }

            val = ExtractYamlValue(yaml, "defaultPriority");
            if (!string.IsNullOrEmpty(val) && int.TryParse(val, out int priorityIdx))
            {
                var names = Enum.GetNames(typeof(MelpomenePriority));
                if (priorityIdx >= 0 && priorityIdx < names.Length)
                {
                    config.defaultPriority = (MelpomenePriority)priorityIdx;
                    migrated++;
                }
            }

            val = ExtractYamlValue(yaml, "defaultCategory");
            if (!string.IsNullOrEmpty(val) && int.TryParse(val, out int categoryIdx))
            {
                var names = Enum.GetNames(typeof(MelpomeneCategory));
                if (categoryIdx >= 0 && categoryIdx < names.Length)
                {
                    config.defaultCategory = (MelpomeneCategory)categoryIdx;
                    migrated++;
                }
            }

            val = ExtractYamlValue(yaml, "cacheDurationMinutes");
            if (!string.IsNullOrEmpty(val) && int.TryParse(val, out int cache))
            {
                config.cacheDurationMinutes = cache;
                migrated++;
            }

            // --- Local Settings ---
            val = ExtractYamlValue(yaml, "accessToken");
            if (!string.IsNullOrEmpty(val)) { config.accessToken = val; migrated++; }

            val = ExtractYamlValue(yaml, "defaultUserName");
            if (!string.IsNullOrEmpty(val)) { config.defaultUserName = val; migrated++; }

            val = ExtractYamlValue(yaml, "enableAltClickShortcut");
            if (!string.IsNullOrEmpty(val))
            {
                config.enableAltClickShortcut = val == "1" || val.ToLower() == "true";
                migrated++;
            }

            val = ExtractYamlValue(yaml, "enableTicketDisplay");
            if (!string.IsNullOrEmpty(val))
            {
                config.enableTicketDisplay = val == "1" || val.ToLower() == "true";
                migrated++;
            }

            config.Save();

            Debug.Log($"[Melpomene] 旧設定から {migrated} 項目を移行しました (source: {assetPath})");
            EditorUtility.DisplayDialog("移行完了",
                $"旧設定アセットから {migrated} 項目を移行しました。\n\nソース: {assetPath}\n\n旧アセットは手動で削除してください。",
                "OK");

            // NOTE: キャッシュをリフレッシュ
            ReloadConfig();
        }

        /// <summary>
        /// 旧アセットファイルを検索
        /// </summary>
        private static string FindLegacyAsset()
        {
            foreach (var path in LegacyAssetSearchPaths)
            {
                string fullPath = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), path);
                if (File.Exists(fullPath))
                    return path;
            }

            // NOTE: GUIDで検索（アセットが移動されている場合）
            var guids = AssetDatabase.FindAssets("t:ScriptableObject MelpomeneConfig");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("MelpomeneConfig.asset"))
                    return path;
            }

            return null;
        }

        /// <summary>
        /// Unity YAML形式から単一値を抽出
        /// NOTE: "key: value" 形式の行をパース
        /// </summary>
        private static string ExtractYamlValue(string yaml, string key)
        {
            // NOTE: "  key: value" のパターンに一致する行を検索
            foreach (var line in yaml.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith(key + ":"))
                {
                    var value = trimmed.Substring(key.Length + 1).Trim();
                    return value;
                }
            }
            return null;
        }

        /// <summary>
        /// Unity YAML形式から配列を抽出
        /// NOTE: "key:" の後に "- value" 行が続くパターン
        /// </summary>
        private static string[] ExtractYamlArray(string yaml, string key)
        {
            var lines = yaml.Split('\n');
            var items = new System.Collections.Generic.List<string>();
            bool inArray = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.StartsWith(key + ":"))
                {
                    // NOTE: 同じ行に値がある場合はスカラー（配列でない）
                    var afterColon = trimmed.Substring(key.Length + 1).Trim();
                    if (!string.IsNullOrEmpty(afterColon))
                        return null;

                    inArray = true;
                    continue;
                }

                if (inArray)
                {
                    if (trimmed.StartsWith("- "))
                    {
                        items.Add(trimmed.Substring(2).Trim());
                    }
                    else
                    {
                        break;
                    }
                }
            }

            return items.Count > 0 ? items.ToArray() : null;
        }

        /// <summary>
        /// 初回起動時に旧設定が存在し、JSONがデフォルトのままなら自動移行を提案
        /// </summary>
        [InitializeOnLoadMethod]
        private static void CheckMigrationOnLoad()
        {
            // NOTE: JSONファイルのaccessTokenが空（デフォルト）で旧アセットが存在する場合に提案
            string localPath = Path.Combine(ConfigDir, "settings.local.json");
            if (!File.Exists(localPath))
                return;

            try
            {
                var json = File.ReadAllText(localPath);
                // NOTE: accessTokenが空かデフォルトなら移行が必要な可能性あり
                if (json.Contains("\"accessToken\": \"\"") || json.Contains("\"accessToken\":\"\""))
                {
                    string assetPath = FindLegacyAsset();
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        string fullPath = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), assetPath);
                        if (File.Exists(fullPath))
                        {
                            var yaml = File.ReadAllText(fullPath);
                            var token = ExtractYamlValue(yaml, "accessToken");
                            if (!string.IsNullOrEmpty(token))
                            {
                                // NOTE: 旧アセットにトークンがあり、新設定にない場合のみ提案
                                EditorApplication.delayCall += () =>
                                {
                                    if (EditorUtility.DisplayDialog("Melpomene 設定移行",
                                        "旧ScriptableObject形式の設定が見つかりました。\n新しいJSON設定に移行しますか？",
                                        "移行する", "後で"))
                                    {
                                        MigrateFromLegacyAsset();
                                    }
                                };
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // NOTE: チェック中のエラーは無視（起動を妨げない）
            }
        }

        #endregion

        #region Menu Items

        /// <summary>
        /// 設定ウィンドウを開く
        /// NOTE: MusaWindowの設定タブから呼び出される
        /// </summary>
        public static void OpenSettings()
        {
            MelpomeneSettingsWindow.ShowWindow();
        }

        /// <summary>
        /// シーンのチケット表示を切り替え
        /// </summary>
        [MenuItem("Musa/Melpomene/シーン表示切替 _F4")]
        public static void ToggleTicketDisplay()
        {
            MelpomeneManager.Instance.ToggleTicketDisplay();
        }

        /// <summary>
        /// メニューのチェック状態を更新
        /// </summary>
        [MenuItem("Musa/Melpomene/シーン表示切替 _F4", true)]
        public static bool ValidateToggleTicketDisplay()
        {
            var config = GetOrCreateConfig();
            Menu.SetChecked("Musa/Melpomene/シーン表示切替 _F4", config.enableTicketDisplay);
            return true;
        }

        /// <summary>
        /// マイルストーン管理ウィンドウを開く
        /// </summary>
        [MenuItem("Tools/Melpomene/マイルストーン管理")]
        [MenuItem("Window/Melpomene/マイルストーン管理")]
        public static void OpenMilestoneManager()
        {
            MelpomeneMilestoneWindow.ShowWindow();
        }

        /// <summary>
        /// チケットキャッシュを手動更新
        /// </summary>
        [MenuItem("Musa/Melpomene/チケット更新 _F5")]
        public static void RefreshTicketCache()
        {
            var manager = MelpomeneManager.Instance;
            if (manager == null)
            {
                UnityEngine.Debug.LogWarning("[Melpomene] MelpomeneManager が初期化されていません");
                return;
            }
            manager.RefreshCacheAsync().Forget();
            UnityEngine.Debug.Log("[Melpomene] チケットキャッシュを更新しました");
        }

        #endregion
    }
}
#endif
