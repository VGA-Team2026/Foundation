using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Google Drive アセットインポーター
/// NOTE: GoogleDriveからunitypackageをダウンロードしてインポートする
/// NOTE: カタログファイルで管理し、更新分または未取得分のみダウンロードする
/// NOTE: GUIDベース判定でアセット存在チェックを行う (#680)
/// </summary>
public class GoogleDriveAssetImporter : EditorWindow
{
    #region Constants

    private const string WINDOW_TITLE = "Asset Importer";
    internal const string CATALOG_CACHE_FILE = "asset_catalog_cache.json";
    private const string DOWNLOAD_FOLDER = "Downloads/ExternalAssets";
    private const string SETTINGS_PATH = "musa/melpomene/settings.json";
    private const int REQUEST_TIMEOUT = 60;
    private const int DOWNLOAD_TIMEOUT = 0; // NOTE: 大容量ファイルのため無制限
    private const int MAX_AUTH_RETRY = 1; // NOTE: 401リトライの最大回数

    #endregion

    #region Fields

    /// <summary>カタログファイルのGoogleDrive FileID</summary>
    [SerializeField] private string _catalogFileId = "";

    /// <summary>ダウンロードステータス</summary>
    private string _statusMessage = "";

    /// <summary>進捗</summary>
    private float _progress = 0f;

    /// <summary>処理中フラグ</summary>
    private bool _isProcessing = false;

    /// <summary>認証URL（Lambda URL）</summary>
    private string _authUrl;

    /// <summary>アクセストークン</summary>
    private string _accessToken;

    /// <summary>カタログキャッシュ</summary>
    private AssetCatalog _catalogCache;

    /// <summary>スクロール位置</summary>
    private Vector2 _scrollPosition;

    /// <summary>認証方式表示</summary>
    private string _authMethod = "未認証";

    /// <summary>トークンデータ</summary>
    private TokenData _tokenData;

    #endregion

    #region Menu

    [MenuItem("Tools/Musa/Asset Importer")]
    public static void ShowWindow()
    {
        var window = GetWindow<GoogleDriveAssetImporter>(WINDOW_TITLE);
        window.minSize = new Vector2(450, 350);
    }

    #endregion

    #region Unity Callbacks

    private void OnEnable()
    {
        LoadSettings();
        LoadCatalogCache();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Google Drive Asset Importer", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        // NOTE: Global Settings（共通設定）
        MusaGlobalSettings.DrawGUI();

        // NOTE: 認証状態（Lambda or トークンファイル）
        var hasLambdaUrl = !string.IsNullOrEmpty(MusaGlobalSettings.GoogleAuthUrl);
        var hasRefreshToken = _tokenData != null && !string.IsNullOrEmpty(_tokenData.refresh_token);
        var isAuthenticated = hasLambdaUrl || hasRefreshToken;
        EditorGUILayout.LabelField("認証方式:", _authMethod);

        EditorGUILayout.Space(10);

        // NOTE: カタログFileID（自動検出または手動入力）
        EditorGUILayout.BeginHorizontal();
        EditorGUI.BeginChangeCheck();
        _catalogFileId = EditorGUILayout.TextField("カタログFileID", _catalogFileId);
        if (EditorGUI.EndChangeCheck())
        {
            MusaGlobalSettings.GoogleDriveCatalogFileId = _catalogFileId;
            MusaGlobalSettings.Save();
        }

        // NOTE: 自動検出ボタン
        EditorGUI.BeginDisabledGroup(_isProcessing || !isAuthenticated || string.IsNullOrEmpty(MusaGlobalSettings.GoogleDriveFolderIdAsset));
        if (GUILayout.Button("自動検出", GUILayout.Width(80)))
        {
            AutoDetectCatalogAsync().Forget();
        }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        // NOTE: アクションボタン
        EditorGUI.BeginDisabledGroup(_isProcessing || !isAuthenticated || string.IsNullOrEmpty(_catalogFileId));

        if (GUILayout.Button("カタログを取得して更新確認", GUILayout.Height(30)))
        {
            CheckForUpdatesAsync().Forget();
        }

        if (GUILayout.Button("全てダウンロード＆インポート", GUILayout.Height(30)))
        {
            DownloadAndImportAllAsync().Forget();
        }

        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(10);

        // NOTE: 進捗表示
        if (_isProcessing)
        {
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 20), _progress, _statusMessage);
        }
        else if (!string.IsNullOrEmpty(_statusMessage))
        {
            EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);
        }

        EditorGUILayout.Space(10);

        // NOTE: カタログ内容表示
        if (_catalogCache != null && _catalogCache.assets != null && _catalogCache.assets.Length > 0)
        {
            EditorGUILayout.LabelField("カタログ内容:", EditorStyles.boldLabel);
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            foreach (var asset in _catalogCache.assets)
            {
                var status = GetAssetStatus(asset);
                DrawAssetRow(asset, status);
            }

            EditorGUILayout.EndScrollView();
        }
    }

    /// <summary>
    /// アセット行の描画
    /// </summary>
    private void DrawAssetRow(AssetEntry asset, AssetStatus status)
    {
        EditorGUILayout.BeginHorizontal();

        // NOTE: ステータスアイコン
        string icon;
        switch (status)
        {
            case AssetStatus.UpToDate:
                icon = "✓";
                break;
            case AssetStatus.NeedsUpdate:
                icon = "↻";
                break;
            case AssetStatus.PartiallyImported:
                icon = "△";
                break;
            default:
                icon = "↓";
                break;
        }

        // NOTE: GUID判定詳細
        string guidInfo = "";
        if (asset.guids != null && asset.guids.Length > 0)
        {
            int found = 0;
            int missing = 0;
            foreach (var guid in asset.guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path) && File.Exists(Path.Combine(Application.dataPath, "..", path)))
                {
                    found++;
                }
                else
                {
                    missing++;
                }
            }
            guidInfo = $" [GUID: {found}/{asset.guids.Length}]";
        }

        EditorGUILayout.LabelField(
            $"{icon} {asset.name}{guidInfo}",
            $"v{asset.version} ({FormatFileSize(asset.size)})",
            GUILayout.MinWidth(200)
        );

        // NOTE: 個別DLボタン
        EditorGUI.BeginDisabledGroup(
            _isProcessing ||
            (string.IsNullOrEmpty(MusaGlobalSettings.GoogleAuthUrl) && (_tokenData == null || string.IsNullOrEmpty(_tokenData.refresh_token))) ||
            status == AssetStatus.UpToDate
        );
        if (GUILayout.Button("DL", GUILayout.Width(40)))
        {
            DownloadAndImportSingleAsync(asset).Forget();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndHorizontal();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 更新確認
    /// </summary>
    public async UniTaskVoid CheckForUpdatesAsync()
    {
        _isProcessing = true;
        _progress = 0f;
        _statusMessage = "カタログをダウンロード中...";
        Repaint();

        try
        {
            // NOTE: アクセストークンを取得
            if (!await EnsureAccessTokenAsync())
            {
                _statusMessage = "認証に失敗しました";
                return;
            }

            // NOTE: カタログをダウンロード
            var catalogJson = await DownloadTextFileAsync(_catalogFileId);
            if (string.IsNullOrEmpty(catalogJson))
            {
                _statusMessage = "カタログのダウンロードに失敗しました";
                return;
            }

            // NOTE: カタログを解析（ローカル情報を保持）
            var localAssets = _catalogCache?.localAssets;
            _catalogCache = JsonUtility.FromJson<AssetCatalog>(catalogJson);
            if (_catalogCache.assets == null)
            {
                _catalogCache.assets = new AssetEntry[0];
            }
            if (_catalogCache.localAssets == null)
            {
                _catalogCache.localAssets = localAssets;
            }
            SaveCatalogCache();

            // NOTE: 更新が必要なアセットをカウント
            int needsUpdate = 0;
            int upToDate = 0;
            int partial = 0;
            foreach (var asset in _catalogCache.assets)
            {
                var status = GetAssetStatus(asset);
                if (status == AssetStatus.UpToDate) upToDate++;
                else if (status == AssetStatus.PartiallyImported) partial++;
                else needsUpdate++;
            }

            _statusMessage = $"カタログ取得完了: {_catalogCache.assets.Length}件 (最新: {upToDate}, 部分: {partial}, 要更新: {needsUpdate})";
        }
        catch (Exception ex)
        {
            _statusMessage = $"エラー: {ex.Message}";
            Debug.LogException(ex);
        }
        finally
        {
            _isProcessing = false;
            _progress = 1f;
            Repaint();
        }
    }

    /// <summary>
    /// 全てダウンロードしてインポート
    /// </summary>
    public async UniTaskVoid DownloadAndImportAllAsync()
    {
        _isProcessing = true;
        _progress = 0f;
        Repaint();

        try
        {
            // NOTE: アクセストークンを取得
            if (!await EnsureAccessTokenAsync())
            {
                _statusMessage = "認証に失敗しました";
                return;
            }

            // NOTE: カタログがない場合は取得
            if (_catalogCache == null || _catalogCache.assets == null)
            {
                _statusMessage = "カタログをダウンロード中...";
                Repaint();

                var catalogJson = await DownloadTextFileAsync(_catalogFileId);
                if (string.IsNullOrEmpty(catalogJson))
                {
                    _statusMessage = "カタログのダウンロードに失敗しました";
                    return;
                }

                _catalogCache = JsonUtility.FromJson<AssetCatalog>(catalogJson);
                SaveCatalogCache();
            }

            // NOTE: 更新が必要なアセットを収集
            var assetsToDownload = new List<AssetEntry>();
            foreach (var asset in _catalogCache.assets)
            {
                var status = GetAssetStatus(asset);
                if (status != AssetStatus.UpToDate)
                {
                    assetsToDownload.Add(asset);
                }
            }

            if (assetsToDownload.Count == 0)
            {
                _statusMessage = "全てのアセットが最新です";
                return;
            }

            // NOTE: ダウンロードフォルダを作成
            var downloadPath = Path.Combine(Application.dataPath, "..", DOWNLOAD_FOLDER);
            if (!Directory.Exists(downloadPath))
            {
                Directory.CreateDirectory(downloadPath);
            }

            // NOTE: 各アセットをダウンロードしてインポート
            for (int i = 0; i < assetsToDownload.Count; i++)
            {
                var asset = assetsToDownload[i];
                _progress = (float)i / assetsToDownload.Count;
                _statusMessage = $"ダウンロード中: {asset.name} ({i + 1}/{assetsToDownload.Count})";
                Repaint();

                var localPath = Path.Combine(downloadPath, $"{asset.name}.unitypackage");

                // NOTE: ダウンロード
                if (!await DownloadFileAsync(asset.fileId, localPath))
                {
                    Debug.LogError($"[AssetImporter] Failed to download: {asset.name}");
                    continue;
                }

                // NOTE: MD5チェック
                if (!string.IsNullOrEmpty(asset.md5))
                {
                    var localMd5 = CalculateMD5(localPath);
                    if (!string.Equals(localMd5, asset.md5, StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.LogError($"[AssetImporter] MD5 mismatch: {asset.name} (expected: {asset.md5}, got: {localMd5})");
                        continue;
                    }
                }

                // NOTE: インポート
                _statusMessage = $"インポート中: {asset.name}";
                Repaint();

                AssetDatabase.ImportPackage(localPath, false);

                // NOTE: ローカルキャッシュを更新
                UpdateLocalAssetInfo(asset);

                Debug.Log($"[AssetImporter] Imported: {asset.name}");
            }

            _statusMessage = $"完了: {assetsToDownload.Count}件のアセットをインポートしました";
            SaveCatalogCache();
        }
        catch (Exception ex)
        {
            _statusMessage = $"エラー: {ex.Message}";
            Debug.LogException(ex);
        }
        finally
        {
            _isProcessing = false;
            _progress = 1f;
            Repaint();
            AssetDatabase.Refresh();
        }
    }

    /// <summary>
    /// 個別アセットをダウンロードしてインポート
    /// </summary>
    internal async UniTaskVoid DownloadAndImportSingleAsync(AssetEntry asset)
    {
        _isProcessing = true;
        _progress = 0f;
        _statusMessage = $"ダウンロード中: {asset.name}";
        Repaint();

        try
        {
            if (!await EnsureAccessTokenAsync())
            {
                _statusMessage = "認証に失敗しました";
                return;
            }

            var downloadPath = Path.Combine(Application.dataPath, "..", DOWNLOAD_FOLDER);
            if (!Directory.Exists(downloadPath))
            {
                Directory.CreateDirectory(downloadPath);
            }

            var localPath = Path.Combine(downloadPath, $"{asset.name}.unitypackage");

            if (!await DownloadFileAsync(asset.fileId, localPath))
            {
                _statusMessage = $"ダウンロード失敗: {asset.name}";
                return;
            }

            if (!string.IsNullOrEmpty(asset.md5))
            {
                var localMd5 = CalculateMD5(localPath);
                if (!string.Equals(localMd5, asset.md5, StringComparison.OrdinalIgnoreCase))
                {
                    _statusMessage = $"MD5不一致: {asset.name}";
                    Debug.LogError($"[AssetImporter] MD5 mismatch: {asset.name}");
                    return;
                }
            }

            _statusMessage = $"インポート中: {asset.name}";
            Repaint();

            AssetDatabase.ImportPackage(localPath, false);
            UpdateLocalAssetInfo(asset);
            SaveCatalogCache();

            _statusMessage = $"完了: {asset.name} をインポートしました";
            Debug.Log($"[AssetImporter] Imported: {asset.name}");
        }
        catch (Exception ex)
        {
            _statusMessage = $"エラー: {ex.Message}";
            Debug.LogException(ex);
        }
        finally
        {
            _isProcessing = false;
            _progress = 1f;
            Repaint();
            AssetDatabase.Refresh();
        }
    }

    #endregion

    #region Private Methods - Authentication

    /// <summary>
    /// アクセストークンを確保
    /// NOTE: Lambda経由を優先、フォールバックでrefresh_token経由
    /// </summary>
    private async UniTask<bool> EnsureAccessTokenAsync()
    {
        // NOTE: Lambda経由でトークン取得を試みる
        if (!string.IsNullOrEmpty(MusaGlobalSettings.GoogleAuthUrl))
        {
            if (await GetAccessTokenViaLambdaAsync())
            {
                _authMethod = "認証済み (Lambda)";
                return true;
            }
        }

        // NOTE: フォールバック: refresh_token経由
        if (_tokenData != null && !string.IsNullOrEmpty(_tokenData.refresh_token))
        {
            if (string.IsNullOrEmpty(_tokenData.access_token))
            {
                if (await RefreshAccessTokenAsync())
                {
                    _authMethod = "認証済み (token)";
                    return true;
                }
            }
            else
            {
                _accessToken = _tokenData.access_token;
                _authMethod = "認証済み (token)";
                return true;
            }
        }

        _authMethod = "未認証";
        Debug.LogError("[AssetImporter] 認証情報がありません。Lambda URLまたはトークンファイルを設定してください。");
        return false;
    }

    /// <summary>
    /// Lambda経由でアクセストークンを取得
    /// </summary>
    private async UniTask<bool> GetAccessTokenViaLambdaAsync()
    {
        var tokenUrl = MusaGlobalSettings.GoogleAuthUrl.TrimEnd('/') + "/token";

        using (var request = UnityWebRequest.Get(tokenUrl))
        {
            request.timeout = REQUEST_TIMEOUT;
            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[AssetImporter] Lambda token取得失敗: {request.error}");
                return false;
            }

            try
            {
                var response = JsonUtility.FromJson<LambdaTokenResponse>(request.downloadHandler.text);
                if (string.IsNullOrEmpty(response.access_token))
                {
                    Debug.LogWarning("[AssetImporter] Lambda responseにaccess_tokenがありません");
                    return false;
                }

                if (_tokenData == null) _tokenData = new TokenData();
                _tokenData.access_token = response.access_token;
                _accessToken = response.access_token;
                Debug.Log("[AssetImporter] Lambda経由でアクセストークン取得成功");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AssetImporter] Lambda response解析失敗: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// refresh_token経由でアクセストークンを更新
    /// </summary>
    private async UniTask<bool> RefreshAccessTokenAsync()
    {
        if (_tokenData == null || string.IsNullOrEmpty(_tokenData.refresh_token))
        {
            Debug.LogError("[AssetImporter] refresh_tokenが未設定です");
            return false;
        }

        // NOTE: settings.jsonからclient_id/client_secretを取得
        var settingsPath = Path.Combine(Application.dataPath, "..", SETTINGS_PATH);
        if (!File.Exists(settingsPath))
        {
            Debug.LogError("[AssetImporter] settings.jsonが見つかりません");
            return false;
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            var settings = JsonUtility.FromJson<SettingsJson>(json);

            var form = new WWWForm();
            form.AddField("client_id", settings.googleClientId ?? "");
            form.AddField("client_secret", settings.googleClientSecret ?? "");
            form.AddField("refresh_token", _tokenData.refresh_token);
            form.AddField("grant_type", "refresh_token");

            using (var request = UnityWebRequest.Post("https://oauth2.googleapis.com/token", form))
            {
                request.timeout = REQUEST_TIMEOUT;
                await request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError($"[AssetImporter] Token refresh failed: {request.error}");
                    return false;
                }

                var response = JsonUtility.FromJson<TokenResponse>(request.downloadHandler.text);
                if (string.IsNullOrEmpty(response?.access_token))
                {
                    Debug.LogError("[AssetImporter] Token response missing access_token");
                    return false;
                }

                _tokenData.access_token = response.access_token;
                _accessToken = response.access_token;
                Debug.Log("[AssetImporter] Access token refreshed");
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AssetImporter] Failed to refresh token: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Private Methods - Download

    /// <summary>
    /// テキストファイルをダウンロード
    /// </summary>
    private async UniTask<string> DownloadTextFileAsync(string fileId, int retryCount = 0)
    {
        var url = $"https://www.googleapis.com/drive/v3/files/{fileId}?alt=media";

        using (var request = UnityWebRequest.Get(url))
        {
            request.SetRequestHeader("Authorization", $"Bearer {_accessToken}");
            request.timeout = REQUEST_TIMEOUT;
            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                // NOTE: 401の場合はトークンを更新してリトライ（最大1回まで）
                if (request.responseCode == 401 && retryCount < MAX_AUTH_RETRY)
                {
                    if (await EnsureAccessTokenAsync())
                    {
                        return await DownloadTextFileAsync(fileId, retryCount + 1);
                    }
                }
                Debug.LogError($"[AssetImporter] Download failed: {request.error}");
                return null;
            }

            return request.downloadHandler.text;
        }
    }

    /// <summary>
    /// フォルダ内のファイルを名前で検索
    /// NOTE: Google Drive Files List APIを使用
    /// </summary>
    private async UniTask<string> SearchFileByNameAsync(string folderId, string fileName, int retryCount = 0)
    {
        // NOTE: クエリ: name='fileName' and 'folderId' in parents and trashed=false
        var query = UnityWebRequest.EscapeURL($"name='{fileName}' and '{folderId}' in parents and trashed=false");
        var url = $"https://www.googleapis.com/drive/v3/files?q={query}&fields=files(id,name)";

        using (var request = UnityWebRequest.Get(url))
        {
            request.SetRequestHeader("Authorization", $"Bearer {_accessToken}");
            request.timeout = REQUEST_TIMEOUT;
            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                // NOTE: 401の場合はトークンを更新してリトライ
                if (request.responseCode == 401 && retryCount < MAX_AUTH_RETRY)
                {
                    if (await EnsureAccessTokenAsync())
                    {
                        return await SearchFileByNameAsync(folderId, fileName, retryCount + 1);
                    }
                }
                Debug.LogError($"[AssetImporter] File search failed: {request.error}");
                return null;
            }

            var response = JsonUtility.FromJson<FileListResponse>(request.downloadHandler.text);
            if (response?.files != null && response.files.Length > 0)
            {
                return response.files[0].id;
            }

            return null;
        }
    }

    /// <summary>
    /// カタログファイルを自動検出
    /// NOTE: アセットフォルダ内の "asset_catalog.json" を検索
    /// </summary>
    private async UniTask<bool> AutoDetectCatalogAsync()
    {
        var folderId = MusaGlobalSettings.GoogleDriveFolderIdAsset;
        if (string.IsNullOrEmpty(folderId))
        {
            _statusMessage = "アセットフォルダIDが未設定です";
            return false;
        }

        _isProcessing = true;
        _statusMessage = "カタログファイルを検索中...";
        Repaint();

        try
        {
            if (!await EnsureAccessTokenAsync())
            {
                _statusMessage = "認証に失敗しました";
                return false;
            }

            var catalogId = await SearchFileByNameAsync(folderId, "asset_catalog.json");
            if (string.IsNullOrEmpty(catalogId))
            {
                _statusMessage = "カタログファイルが見つかりませんでした";
                return false;
            }

            // NOTE: 検出したIDをGlobalSettingsに保存
            MusaGlobalSettings.GoogleDriveCatalogFileId = catalogId;
            MusaGlobalSettings.Save();
            _catalogFileId = catalogId;
            _statusMessage = $"カタログを検出しました: {catalogId}";
            Debug.Log($"[AssetImporter] カタログファイル自動検出: {catalogId}");
            return true;
        }
        catch (Exception ex)
        {
            _statusMessage = $"検索エラー: {ex.Message}";
            Debug.LogError($"[AssetImporter] Auto-detect failed: {ex.Message}");
            return false;
        }
        finally
        {
            _isProcessing = false;
            Repaint();
        }
    }

    /// <summary>
    /// バイナリファイルをダウンロード
    /// </summary>
    private async UniTask<bool> DownloadFileAsync(string fileId, string savePath, int retryCount = 0)
    {
        var url = $"https://www.googleapis.com/drive/v3/files/{fileId}?alt=media";

        using (var request = UnityWebRequest.Get(url))
        {
            request.SetRequestHeader("Authorization", $"Bearer {_accessToken}");
            request.downloadHandler = new DownloadHandlerFile(savePath);
            request.timeout = DOWNLOAD_TIMEOUT;

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                _progress = operation.progress;
                Repaint();
                await UniTask.Delay(100);
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                // NOTE: 401の場合はトークンを更新してリトライ（最大1回まで）
                if (request.responseCode == 401 && retryCount < MAX_AUTH_RETRY)
                {
                    if (await EnsureAccessTokenAsync())
                    {
                        return await DownloadFileAsync(fileId, savePath, retryCount + 1);
                    }
                }
                Debug.LogError($"[AssetImporter] Download failed: {request.error}");
                return false;
            }

            return true;
        }
    }

    #endregion

    #region Private Methods - Catalog

    /// <summary>
    /// アセットのステータスを取得
    /// NOTE: guidsがある場合はGUIDベースで判定、ない場合はMD5/versionフォールバック
    /// </summary>
    internal AssetStatus GetAssetStatus(AssetEntry asset)
    {
        // NOTE: GUIDベース判定（guidsフィールドがある場合に優先）
        if (asset.guids != null && asset.guids.Length > 0)
        {
            return GetAssetStatusByGuids(asset);
        }

        // NOTE: MD5/versionフォールバック
        return GetAssetStatusByLocalCache(asset);
    }

    /// <summary>
    /// GUIDベースでアセットステータスを判定
    /// </summary>
    private AssetStatus GetAssetStatusByGuids(AssetEntry asset)
    {
        int found = 0;
        foreach (var guid in asset.guids)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.IsNullOrEmpty(assetPath) && File.Exists(Path.Combine(Application.dataPath, "..", assetPath)))
            {
                found++;
            }
        }

        if (found == asset.guids.Length)
        {
            // NOTE: バージョン更新チェック（localAssetsと比較）
            if (_catalogCache?.localAssets != null)
            {
                foreach (var local in _catalogCache.localAssets)
                {
                    if (local.name == asset.name)
                    {
                        if (!string.IsNullOrEmpty(asset.md5) && !string.Equals(local.md5, asset.md5, StringComparison.OrdinalIgnoreCase))
                        {
                            return AssetStatus.NeedsUpdate;
                        }
                        if (!string.IsNullOrEmpty(asset.version) && local.version != asset.version)
                        {
                            return AssetStatus.NeedsUpdate;
                        }
                        break;
                    }
                }
            }
            return AssetStatus.UpToDate;
        }

        if (found > 0)
        {
            return AssetStatus.PartiallyImported;
        }

        return AssetStatus.NotDownloaded;
    }

    /// <summary>
    /// ローカルキャッシュベースでアセットステータスを判定（フォールバック）
    /// </summary>
    private AssetStatus GetAssetStatusByLocalCache(AssetEntry asset)
    {
        if (_catalogCache == null || _catalogCache.localAssets == null)
        {
            return AssetStatus.NotDownloaded;
        }

        foreach (var local in _catalogCache.localAssets)
        {
            if (local.name == asset.name)
            {
                // NOTE: MD5が存在する場合はMD5で判定を確定させる
                if (!string.IsNullOrEmpty(asset.md5))
                {
                    return string.Equals(local.md5, asset.md5, StringComparison.OrdinalIgnoreCase)
                        ? AssetStatus.UpToDate
                        : AssetStatus.NeedsUpdate;
                }
                // NOTE: MD5が存在しない場合はversionで比較
                if (local.version == asset.version)
                {
                    return AssetStatus.UpToDate;
                }
                return AssetStatus.NeedsUpdate;
            }
        }

        return AssetStatus.NotDownloaded;
    }

    /// <summary>
    /// ローカルアセット情報を更新
    /// </summary>
    private void UpdateLocalAssetInfo(AssetEntry asset)
    {
        if (_catalogCache.localAssets == null)
        {
            _catalogCache.localAssets = new LocalAssetEntry[0];
        }

        var list = new List<LocalAssetEntry>(_catalogCache.localAssets);

        // NOTE: 既存エントリを探す
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].name == asset.name)
            {
                list[i] = new LocalAssetEntry
                {
                    name = asset.name,
                    version = asset.version,
                    md5 = asset.md5,
                    importedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };
                _catalogCache.localAssets = list.ToArray();
                return;
            }
        }

        // NOTE: 新規追加
        list.Add(new LocalAssetEntry
        {
            name = asset.name,
            version = asset.version,
            md5 = asset.md5,
            importedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        });
        _catalogCache.localAssets = list.ToArray();
    }

    #endregion

    #region Private Methods - Settings

    /// <summary>
    /// 設定を読み込み
    /// NOTE: MusaGlobalSettingsを優先、settings.jsonにフォールバック
    /// </summary>
    private void LoadSettings()
    {
        _authUrl = null;
        _tokenData = null;

        // NOTE: カタログFileID（MusaGlobalSettings優先）
        _catalogFileId = MusaGlobalSettings.GoogleDriveCatalogFileId;

        try
        {
            var settingsPath = Path.Combine(Application.dataPath, "..", SETTINGS_PATH);
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                var settings = JsonUtility.FromJson<SettingsJson>(json);

                // NOTE: 認証URL（Legacy - MusaGlobalSettingsにない場合のフォールバック→移行）
                _authUrl = settings?.googleAuthUrl?.Trim();
                if (string.IsNullOrEmpty(_authUrl)) _authUrl = null;
                if (string.IsNullOrEmpty(MusaGlobalSettings.GoogleAuthUrl) && !string.IsNullOrEmpty(_authUrl))
                {
                    MusaGlobalSettings.GoogleAuthUrl = _authUrl;
                    MusaGlobalSettings.Save();
                    Debug.Log("[AssetImporter] 認証URLをMusaGlobalSettingsに移行しました");
                }

                // NOTE: カタログFileID（MusaGlobalSettingsが空の場合のみsettings.jsonから移行）
                if (string.IsNullOrEmpty(_catalogFileId) && !string.IsNullOrEmpty(settings?.assetCatalogFileId))
                {
                    _catalogFileId = settings.assetCatalogFileId;
                    // NOTE: MusaGlobalSettingsに移行
                    MusaGlobalSettings.GoogleDriveCatalogFileId = _catalogFileId;
                    MusaGlobalSettings.Save();
                    Debug.Log("[AssetImporter] カタログFileIDをMusaGlobalSettingsに移行しました");
                }
            }

            // NOTE: トークンファイル読み込み
            var tokenPath = Path.Combine(Application.dataPath, "..", "musa/melpomene/google_token.json");
            if (File.Exists(tokenPath))
            {
                var tokenJson = File.ReadAllText(tokenPath);
                _tokenData = JsonUtility.FromJson<TokenData>(tokenJson);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AssetImporter] Failed to load settings: {ex.Message}");
        }
    }

    [Serializable]
    private class SettingsJson
    {
        public string googleAuthUrl;
        public string googleClientId;
        public string googleClientSecret;
        public string assetCatalogFileId;
        public string googleDriveFolderIdAsset;
    }

    [Serializable]
    private class TokenData
    {
        public string access_token;
        public string refresh_token;
    }

    /// <summary>
    /// カタログキャッシュを読み込み
    /// </summary>
    private void LoadCatalogCache()
    {
        var cachePath = Path.Combine(Application.dataPath, "..", CATALOG_CACHE_FILE);
        if (File.Exists(cachePath))
        {
            try
            {
                var json = File.ReadAllText(cachePath);
                _catalogCache = JsonUtility.FromJson<AssetCatalog>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AssetImporter] Failed to load catalog cache: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// カタログキャッシュを保存
    /// </summary>
    private void SaveCatalogCache()
    {
        if (_catalogCache == null) return;

        var cachePath = Path.Combine(Application.dataPath, "..", CATALOG_CACHE_FILE);
        var json = JsonUtility.ToJson(_catalogCache, true);
        File.WriteAllText(cachePath, json);
    }

    #endregion

    #region Private Methods - Utility

    /// <summary>
    /// MD5ハッシュを計算
    /// </summary>
    private string CalculateMD5(string filePath)
    {
        using (var md5 = MD5.Create())
        using (var stream = File.OpenRead(filePath))
        {
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    /// <summary>
    /// ファイルサイズをフォーマット
    /// </summary>
    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }

    #endregion

    #region Data Classes

    internal enum AssetStatus
    {
        NotDownloaded,
        NeedsUpdate,
        PartiallyImported,
        UpToDate
    }

    [Serializable]
    private class TokenResponse
    {
        public string access_token;
        public int expires_in;
        public string token_type;
    }

    /// <summary>
    /// Lambda Token Response
    /// </summary>
    [Serializable]
    private class LambdaTokenResponse
    {
        public string access_token;
        public int expires_in;
    }

    /// <summary>
    /// Google Drive Files List API Response
    /// </summary>
    [Serializable]
    private class FileListResponse
    {
        public FileInfo[] files;
    }

    /// <summary>
    /// Google Drive File Info
    /// </summary>
    [Serializable]
    private class FileInfo
    {
        public string id;
        public string name;
    }

    /// <summary>
    /// アセットカタログ
    /// NOTE: GoogleDrive上のJSONファイルとして管理
    /// NOTE: GoogleDriveAssetUploaderからも参照されるためpublic
    /// </summary>
    [Serializable]
    public class AssetCatalog
    {
        public string version;
        public string updatedAt;
        public AssetEntry[] assets;

        // NOTE: ローカルのみで管理（ダウンロード履歴）
        public LocalAssetEntry[] localAssets;
    }

    /// <summary>
    /// アセットエントリ（カタログ内の各アセット情報）
    /// NOTE: GoogleDriveAssetUploaderからも参照されるためpublic
    /// </summary>
    [Serializable]
    public class AssetEntry
    {
        public string name;
        public string fileId;
        public string version;
        public string md5;
        public long size;
        public string description;
        public bool required;
        public string[] guids;
    }

    /// <summary>
    /// ローカルアセットエントリ（ダウンロード済みアセット情報）
    /// NOTE: GoogleDriveAssetUploaderからも参照されるためpublic
    /// </summary>
    [Serializable]
    public class LocalAssetEntry
    {
        public string name;
        public string version;
        public string md5;
        public string importedAt;
    }

    #endregion
}
