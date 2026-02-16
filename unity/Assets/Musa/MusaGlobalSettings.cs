using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Musa共通設定
/// NOTE: musa/musa_settings.json の読み書きを行う
/// NOTE: GoogleDriveAssetImporter/Uploaderで共通利用 (#692)
/// </summary>
public static class MusaGlobalSettings
{
    #region Constants

    private const string SETTINGS_RELATIVE_PATH = "musa/musa_settings.json";

    #endregion

    #region Fields

    private static MusaSettingsData _cachedSettings;
    private static bool _isLoaded = false;

    #endregion

    #region Properties

    /// <summary>
    /// Google認証URL（Lambda URL）
    /// NOTE: 初回アクセス時に自動ロード
    /// </summary>
    public static string GoogleAuthUrl
    {
        get
        {
            EnsureLoaded();
            return _cachedSettings?.googleAuthUrl ?? "";
        }
        set
        {
            EnsureLoaded();
            if (_cachedSettings == null)
            {
                _cachedSettings = new MusaSettingsData();
            }
            _cachedSettings.googleAuthUrl = value;
        }
    }

    /// <summary>
    /// 認証URLが設定されているか
    /// </summary>
    public static bool HasAuthUrl => !string.IsNullOrEmpty(GoogleAuthUrl);

    /// <summary>
    /// GoogleDriveアセットフォルダID
    /// </summary>
    public static string GoogleDriveFolderIdAsset
    {
        get
        {
            EnsureLoaded();
            return _cachedSettings?.googleDriveFolderIdAsset ?? "";
        }
        set
        {
            EnsureLoaded();
            if (_cachedSettings == null)
            {
                _cachedSettings = new MusaSettingsData();
            }
            _cachedSettings.googleDriveFolderIdAsset = value;
        }
    }

    /// <summary>
    /// GoogleDriveカタログファイルID（自動検出でキャッシュ）
    /// </summary>
    public static string GoogleDriveCatalogFileId
    {
        get
        {
            EnsureLoaded();
            return _cachedSettings?.googleDriveCatalogFileId ?? "";
        }
        set
        {
            EnsureLoaded();
            if (_cachedSettings == null)
            {
                _cachedSettings = new MusaSettingsData();
            }
            _cachedSettings.googleDriveCatalogFileId = value;
        }
    }

    /// <summary>
    /// カタログファイルIDが設定されているか
    /// </summary>
    public static bool HasCatalogFileId => !string.IsNullOrEmpty(GoogleDriveCatalogFileId);

    #endregion

    #region Public Methods

    /// <summary>
    /// 設定を読み込み
    /// </summary>
    public static void Load()
    {
        var settingsPath = GetSettingsPath();
        if (File.Exists(settingsPath))
        {
            try
            {
                var json = File.ReadAllText(settingsPath);
                _cachedSettings = JsonUtility.FromJson<MusaSettingsData>(json);
                Debug.Log($"[MusaGlobalSettings] Loaded settings from {settingsPath}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MusaGlobalSettings] Failed to load settings: {ex.Message}");
                _cachedSettings = new MusaSettingsData();
            }
        }
        else
        {
            Debug.Log($"[MusaGlobalSettings] Settings file not found, creating default: {settingsPath}");
            _cachedSettings = new MusaSettingsData();
            Save(); // NOTE: デフォルトファイルを作成
        }
        _isLoaded = true;
    }

    /// <summary>
    /// 設定を保存
    /// </summary>
    public static void Save()
    {
        if (_cachedSettings == null)
        {
            _cachedSettings = new MusaSettingsData();
        }

        var settingsPath = GetSettingsPath();
        try
        {
            var directory = Path.GetDirectoryName(settingsPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonUtility.ToJson(_cachedSettings, true);
            File.WriteAllText(settingsPath, json);
            Debug.Log($"[MusaGlobalSettings] Saved settings to {settingsPath}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[MusaGlobalSettings] Failed to save settings: {ex.Message}");
        }
    }

    /// <summary>
    /// 設定GUIを描画（EditorWindow埋め込み用）
    /// </summary>
    /// <returns>変更があればtrue</returns>
    public static bool DrawGUI()
    {
        EnsureLoaded();

        bool changed = false;

        EditorGUILayout.LabelField("Global Settings", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        var newAuthUrl = EditorGUILayout.TextField("Google Auth URL", GoogleAuthUrl);
        var newFolderId = EditorGUILayout.TextField("Asset Folder ID", GoogleDriveFolderIdAsset);
        if (EditorGUI.EndChangeCheck())
        {
            GoogleAuthUrl = newAuthUrl;
            GoogleDriveFolderIdAsset = newFolderId;
            Save();
            changed = true;
        }

        // NOTE: 認証状態表示
        var statusText = HasAuthUrl ? "設定済み" : "未設定";
        var statusColor = HasAuthUrl ? Color.green : Color.red;
        var originalColor = GUI.contentColor;
        GUI.contentColor = statusColor;
        EditorGUILayout.LabelField("認証URL状態:", statusText);
        GUI.contentColor = originalColor;

        EditorGUILayout.Space(5);

        return changed;
    }

    /// <summary>
    /// 設定を強制リロード
    /// </summary>
    public static void Reload()
    {
        _isLoaded = false;
        Load();
    }

    #endregion

    #region Private Methods

    private static void EnsureLoaded()
    {
        if (!_isLoaded)
        {
            Load();
        }
    }

    private static string GetSettingsPath()
    {
        // NOTE: Application.dataPath = unity/Assets なので、../.. でプロジェクトルートへ
        return Path.Combine(Application.dataPath, "..", "..", SETTINGS_RELATIVE_PATH);
    }

    #endregion

    #region Data Classes

    [Serializable]
    private class MusaSettingsData
    {
        public string googleAuthUrl = "";
        public string googleDriveFolderIdAsset = "";
        public string googleDriveCatalogFileId = "";
    }

    #endregion
}
