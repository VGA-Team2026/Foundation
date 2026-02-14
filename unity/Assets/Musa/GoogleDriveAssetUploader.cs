using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Google Drive アセットアップローダー
/// NOTE: unitypackageをGoogle Driveにアップロードし、カタログJSONを自動管理する
/// NOTE: scripts/asset-uploader/index.js のNode.js版ロジックをUnity EditorWindowに移植
/// </summary>
public class GoogleDriveAssetUploader : EditorWindow
{
    #region Constants

    private const string WINDOW_TITLE = "Asset Uploader";
    private const string CATALOG_CACHE_FILE = "asset_catalog_cache.json";
    private const string SETTINGS_RELATIVE_PATH = "musa/melpomene/settings.json";
    private const string TOKEN_FILE_NAME = "google_drive_token.json";
    private const int REQUEST_TIMEOUT = 60;
    private const int UPLOAD_TIMEOUT = 0; // NOTE: 大容量ファイルのため無制限
    private const int MAX_AUTH_RETRY = 1;

    #endregion

    #region Fields

    /// <summary>パッケージファイルパス</summary>
    private string _packagePath = "";

    /// <summary>アセット名</summary>
    private string _assetName = "";

    /// <summary>バージョン</summary>
    private string _version = "1.0.0";

    /// <summary>説明</summary>
    private string _description = "";

    /// <summary>必須アセットフラグ</summary>
    private bool _required = false;

    /// <summary>ステータスメッセージ</summary>
    private string _statusMessage = "";

    /// <summary>進捗</summary>
    private float _progress = 0f;

    /// <summary>処理中フラグ</summary>
    private bool _isProcessing = false;

    /// <summary>カタログキャッシュ</summary>
    private GoogleDriveAssetImporter.AssetCatalog _catalogCache;

    /// <summary>スクロール位置</summary>
    private Vector2 _scrollPosition;

    /// <summary>設定</summary>
    private UploaderSettings _settings;

    /// <summary>アクセストークン</summary>
    private string _accessToken;

    /// <summary>認証方式表示</summary>
    private string _authMethod = "未認証";

    #endregion

    #region Menu

    [MenuItem("Tools/Musa/Asset Uploader")]
    public static void ShowWindow()
    {
        var window = GetWindow<GoogleDriveAssetUploader>(WINDOW_TITLE);
        window.minSize = new Vector2(450, 500);
    }

    #endregion

    #region Unity Callbacks

    private void OnEnable()
    {
        _settings = LoadSettings();
        LoadCatalogCache();
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Google Drive Asset Uploader", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        // NOTE: 認証状態
        EditorGUILayout.LabelField("認証状態:", _authMethod);

        EditorGUILayout.Space(10);

        // --- アップロードセクション ---
        EditorGUILayout.LabelField("--- アップロード ---", EditorStyles.boldLabel);

        // NOTE: パッケージファイル選択
        EditorGUILayout.BeginHorizontal();
        _packagePath = EditorGUILayout.TextField("パッケージ", _packagePath);
        if (GUILayout.Button("選択...", GUILayout.Width(60)))
        {
            var selected = EditorUtility.OpenFilePanel("unitypackageを選択", "", "unitypackage");
            if (!string.IsNullOrEmpty(selected))
            {
                _packagePath = selected;
                // NOTE: ファイル名からデフォルトのアセット名を設定
                if (string.IsNullOrEmpty(_assetName))
                {
                    _assetName = Path.GetFileNameWithoutExtension(selected);
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        _assetName = EditorGUILayout.TextField("アセット名", _assetName);
        _version = EditorGUILayout.TextField("バージョン", _version);
        _description = EditorGUILayout.TextField("説明", _description);
        _required = EditorGUILayout.Toggle("必須アセット", _required);

        EditorGUILayout.Space(5);

        // NOTE: アップロードボタン
        EditorGUI.BeginDisabledGroup(_isProcessing || string.IsNullOrEmpty(_packagePath) || string.IsNullOrEmpty(_assetName));
        if (GUILayout.Button("アップロード", GUILayout.Height(30)))
        {
            UploadPackageAsync().Forget();
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(10);

        // --- 進捗セクション ---
        if (_isProcessing)
        {
            EditorGUILayout.LabelField("--- 進捗 ---", EditorStyles.boldLabel);
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 20), _progress, _statusMessage);
        }
        else if (!string.IsNullOrEmpty(_statusMessage))
        {
            EditorGUILayout.HelpBox(_statusMessage, MessageType.Info);
        }

        EditorGUILayout.Space(10);

        // --- カタログセクション ---
        if (_catalogCache != null && _catalogCache.assets != null && _catalogCache.assets.Length > 0)
        {
            EditorGUILayout.LabelField("--- カタログ ---", EditorStyles.boldLabel);

            // NOTE: 必須アセットサマリー
            int requiredTotal = 0;
            int requiredRegistered = 0;
            foreach (var asset in _catalogCache.assets)
            {
                if (!asset.required) continue;
                requiredTotal++;
                requiredRegistered++;
            }
            if (requiredTotal > 0)
            {
                EditorGUILayout.LabelField($"必須アセット: {requiredRegistered}/{_catalogCache.assets.Length} 登録済み");
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.Height(200));

            foreach (var asset in _catalogCache.assets)
            {
                var requiredMark = asset.required ? "\u2605 " : "  ";
                var sizeMB = FormatFileSize(asset.size);
                EditorGUILayout.LabelField($"{requiredMark}\u2713 {asset.name}", $"v{asset.version} ({sizeMB})");
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(5);

            // NOTE: カタログ管理ボタン
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(_isProcessing);

            if (GUILayout.Button("カタログ更新"))
            {
                RefreshCatalogAsync().Forget();
            }

            if (GUILayout.Button("エントリ削除"))
            {
                ShowRemoveEntryMenu();
            }

            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }
        else
        {
            // NOTE: カタログが空の場合
            EditorGUI.BeginDisabledGroup(_isProcessing);
            if (GUILayout.Button("カタログ取得", GUILayout.Height(25)))
            {
                RefreshCatalogAsync().Forget();
            }
            EditorGUI.EndDisabledGroup();
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// メインアップロードフロー（8ステップ）
    /// NOTE: Node.js版 commandUpload() の移植
    /// </summary>
    public async UniTaskVoid UploadPackageAsync()
    {
        _isProcessing = true;
        _progress = 0f;
        Repaint();

        try
        {
            // Step 1: アクセストークン取得
            _statusMessage = "アクセストークンを取得中...";
            _progress = 0.05f;
            Repaint();

            if (!await EnsureAccessTokenAsync())
            {
                _statusMessage = "認証に失敗しました";
                return;
            }

            // Step 2: GUID抽出
            _statusMessage = "GUID抽出中...";
            _progress = 0.15f;
            Repaint();

            var guids = ExtractGuidsFromPackage(_packagePath);
            Debug.Log($"[AssetUploader] {guids.Length}個のGUIDを検出");

            // Step 3: MD5計算
            _statusMessage = "MD5計算中...";
            _progress = 0.25f;
            Repaint();

            var md5 = CalculateMD5(_packagePath);
            Debug.Log($"[AssetUploader] MD5: {md5}");

            // Step 4: Google Driveにマルチパートアップロード
            _statusMessage = "Google Driveにアップロード中...";
            _progress = 0.35f;
            Repaint();

            var fileSize = new FileInfo(_packagePath).Length;
            var fileName = $"{_assetName}.unitypackage";
            var uploadedFileId = await UploadFileMultipartAsync(_packagePath, fileName, _settings.googleDriveFolderIdAsset);

            if (string.IsNullOrEmpty(uploadedFileId))
            {
                _statusMessage = "アップロードに失敗しました";
                return;
            }

            Debug.Log($"[AssetUploader] アップロード完了: fileId={uploadedFileId}");

            // NOTE: 公開設定
            _statusMessage = "公開設定中...";
            _progress = 0.50f;
            Repaint();
            await SetFilePublicAsync(uploadedFileId);

            // Step 5: カタログ取得/新規作成
            _statusMessage = "カタログ取得中...";
            _progress = 0.60f;
            Repaint();

            GoogleDriveAssetImporter.AssetCatalog catalog = null;
            if (!string.IsNullOrEmpty(_settings.assetCatalogFileId))
            {
                try
                {
                    var catalogJson = await DownloadTextFileAsync(_settings.assetCatalogFileId);
                    if (!string.IsNullOrEmpty(catalogJson))
                    {
                        catalog = JsonUtility.FromJson<GoogleDriveAssetImporter.AssetCatalog>(catalogJson);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AssetUploader] カタログ取得失敗: {ex.Message}");
                }
            }

            if (catalog == null)
            {
                Debug.Log("[AssetUploader] 新規カタログを作成");
                catalog = new GoogleDriveAssetImporter.AssetCatalog
                {
                    version = "1.0.0",
                    updatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    assets = new GoogleDriveAssetImporter.AssetEntry[0]
                };
            }

            // Step 6: エントリ追記/更新
            _statusMessage = "カタログエントリ更新中...";
            _progress = 0.70f;
            Repaint();

            var newEntry = new GoogleDriveAssetImporter.AssetEntry
            {
                name = _assetName,
                fileId = uploadedFileId,
                version = _version,
                md5 = md5,
                size = fileSize,
                description = _description,
                required = _required,
                guids = guids
            };

            var assetList = new List<GoogleDriveAssetImporter.AssetEntry>(catalog.assets);
            int existingIndex = -1;
            for (int i = 0; i < assetList.Count; i++)
            {
                if (assetList[i].name == _assetName)
                {
                    existingIndex = i;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                Debug.Log($"[AssetUploader] 既存エントリを更新: {_assetName}");
                assetList[existingIndex] = newEntry;
            }
            else
            {
                Debug.Log($"[AssetUploader] 新規エントリを追加: {_assetName}");
                assetList.Add(newEntry);
            }

            catalog.assets = assetList.ToArray();
            catalog.updatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            // Step 7: カタログをGoogle Driveにアップロード/更新
            _statusMessage = "カタログアップロード中...";
            _progress = 0.85f;
            Repaint();

            // NOTE: JsonUtilityはstring[]をシリアライズできないため手動JSON生成
            var catalogContent = BuildCatalogJson(catalog);

            if (!string.IsNullOrEmpty(_settings.assetCatalogFileId))
            {
                // NOTE: 既存カタログをPATCH更新
                await UpdateFileAsync(_settings.assetCatalogFileId, catalogContent);
                Debug.Log("[AssetUploader] カタログを更新しました");
            }
            else
            {
                // NOTE: 新規カタログを作成してアップロード
                var catalogFileId = await UploadTextFileAsync(
                    "asset_catalog.json",
                    catalogContent,
                    _settings.googleDriveFolderIdAsset
                );

                if (!string.IsNullOrEmpty(catalogFileId))
                {
                    _settings.assetCatalogFileId = catalogFileId;
                    SaveSettings(_settings);
                    Debug.Log($"[AssetUploader] カタログ作成完了: fileId={catalogFileId}");

                    // NOTE: 公開設定
                    await SetFilePublicAsync(catalogFileId);
                }
            }

            // Step 8: ローカルキャッシュ更新
            _statusMessage = "ローカルキャッシュ更新中...";
            _progress = 0.95f;
            Repaint();

            SaveCatalogCacheFromJson(catalogContent);
            _catalogCache = catalog;

            _statusMessage = $"完了: {_assetName} v{_version} をアップロードしました (GUID: {guids.Length}個)";
            Debug.Log($"[AssetUploader] アップロード完了: {_assetName} v{_version}, FileID={uploadedFileId}, GUID数={guids.Length}");
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
    /// カタログ再取得
    /// </summary>
    public async UniTaskVoid RefreshCatalogAsync()
    {
        _isProcessing = true;
        _progress = 0f;
        _statusMessage = "カタログを取得中...";
        Repaint();

        try
        {
            if (!await EnsureAccessTokenAsync())
            {
                _statusMessage = "認証に失敗しました";
                return;
            }

            if (string.IsNullOrEmpty(_settings.assetCatalogFileId))
            {
                _statusMessage = "カタログFileIDが設定されていません";
                return;
            }

            var catalogJson = await DownloadTextFileAsync(_settings.assetCatalogFileId);
            if (string.IsNullOrEmpty(catalogJson))
            {
                _statusMessage = "カタログのダウンロードに失敗しました";
                return;
            }

            _catalogCache = JsonUtility.FromJson<GoogleDriveAssetImporter.AssetCatalog>(catalogJson);
            SaveCatalogCacheFromJson(catalogJson);

            _statusMessage = $"カタログ取得完了: {_catalogCache.assets?.Length ?? 0}件のアセット";
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
    /// カタログからエントリ削除
    /// </summary>
    public async UniTaskVoid RemoveEntryAsync(string assetName)
    {
        _isProcessing = true;
        _progress = 0f;
        _statusMessage = $"エントリ削除中: {assetName}";
        Repaint();

        try
        {
            if (!await EnsureAccessTokenAsync())
            {
                _statusMessage = "認証に失敗しました";
                return;
            }

            if (string.IsNullOrEmpty(_settings.assetCatalogFileId))
            {
                _statusMessage = "カタログFileIDが設定されていません";
                return;
            }

            // NOTE: カタログをリモートから取得
            var catalogJson = await DownloadTextFileAsync(_settings.assetCatalogFileId);
            if (string.IsNullOrEmpty(catalogJson))
            {
                _statusMessage = "カタログの取得に失敗しました";
                return;
            }

            var catalog = JsonUtility.FromJson<GoogleDriveAssetImporter.AssetCatalog>(catalogJson);
            var assetList = new List<GoogleDriveAssetImporter.AssetEntry>(catalog.assets);
            int originalCount = assetList.Count;

            assetList.RemoveAll(a => a.name == assetName);

            if (assetList.Count == originalCount)
            {
                _statusMessage = $"エントリが見つかりません: {assetName}";
                return;
            }

            catalog.assets = assetList.ToArray();
            catalog.updatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            var updatedContent = BuildCatalogJson(catalog);

            // NOTE: カタログを更新
            _progress = 0.5f;
            Repaint();
            await UpdateFileAsync(_settings.assetCatalogFileId, updatedContent);

            // NOTE: ローカルキャッシュ更新
            SaveCatalogCacheFromJson(updatedContent);
            _catalogCache = catalog;

            _statusMessage = $"エントリ削除完了: {assetName}";
            Debug.Log($"[AssetUploader] エントリ削除: {assetName}");
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

    #endregion

    #region Private Methods - Auth

    /// <summary>
    /// アクセストークンを確保
    /// NOTE: Lambda経由を優先、フォールバックでrefresh_token経由
    /// </summary>
    private async UniTask<bool> EnsureAccessTokenAsync()
    {
        if (!string.IsNullOrEmpty(_accessToken))
        {
            return true;
        }

        // NOTE: Lambda経由でトークン取得を試みる
        if (!string.IsNullOrEmpty(_settings?.googleAuthUrl))
        {
            if (await GetAccessTokenViaLambdaAsync())
            {
                _authMethod = "認証済み (Lambda)";
                return true;
            }
        }

        // NOTE: フォールバック: refresh_token経由
        if (await GetAccessTokenViaRefreshAsync())
        {
            _authMethod = "認証済み (token)";
            return true;
        }

        _authMethod = "未認証";
        return false;
    }

    /// <summary>
    /// Lambda経由でアクセストークンを取得
    /// </summary>
    private async UniTask<bool> GetAccessTokenViaLambdaAsync()
    {
        var tokenUrl = _settings.googleAuthUrl.TrimEnd('/') + "/token";

        using (var request = UnityWebRequest.Get(tokenUrl))
        {
            request.timeout = REQUEST_TIMEOUT;
            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[AssetUploader] Lambda token取得失敗: {request.error}");
                return false;
            }

            try
            {
                var response = JsonUtility.FromJson<LambdaTokenResponse>(request.downloadHandler.text);
                if (string.IsNullOrEmpty(response.access_token))
                {
                    Debug.LogWarning("[AssetUploader] Lambda responseにaccess_tokenがありません");
                    return false;
                }

                _accessToken = response.access_token;
                Debug.Log("[AssetUploader] Lambda経由でアクセストークン取得成功");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AssetUploader] Lambda response解析失敗: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// refresh_token経由でアクセストークン取得（フォールバック）
    /// NOTE: GoogleDriveAssetImporter互換
    /// </summary>
    private async UniTask<bool> GetAccessTokenViaRefreshAsync()
    {
        var tokenPath = Path.Combine(Application.dataPath, "..", TOKEN_FILE_NAME);

        string clientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
        string clientSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET");
        string refreshToken = Environment.GetEnvironmentVariable("GOOGLE_REFRESH_TOKEN");

        // NOTE: トークンファイルからも取得
        if (File.Exists(tokenPath))
        {
            try
            {
                var json = File.ReadAllText(tokenPath);
                var tokenData = JsonUtility.FromJson<RefreshTokenData>(json);
                if (string.IsNullOrEmpty(clientId)) clientId = tokenData.client_id;
                if (string.IsNullOrEmpty(clientSecret)) clientSecret = tokenData.client_secret;
                if (string.IsNullOrEmpty(refreshToken)) refreshToken = tokenData.refresh_token;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AssetUploader] トークンファイル読み込み失敗: {ex.Message}");
            }
        }

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(refreshToken))
        {
            Debug.LogWarning("[AssetUploader] refresh_token認証に必要な情報が不足しています");
            return false;
        }

        var form = new WWWForm();
        form.AddField("client_id", clientId);
        form.AddField("client_secret", clientSecret);
        form.AddField("refresh_token", refreshToken);
        form.AddField("grant_type", "refresh_token");

        using (var request = UnityWebRequest.Post("https://oauth2.googleapis.com/token", form))
        {
            request.timeout = REQUEST_TIMEOUT;
            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[AssetUploader] Token refresh失敗: {request.error}");
                return false;
            }

            try
            {
                var response = JsonUtility.FromJson<LambdaTokenResponse>(request.downloadHandler.text);
                _accessToken = response.access_token;
                Debug.Log("[AssetUploader] refresh_token経由でアクセストークン取得成功");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AssetUploader] Token response解析失敗: {ex.Message}");
                return false;
            }
        }
    }

    #endregion

    #region Private Methods - Google Drive

    /// <summary>
    /// マルチパートアップロード（新規ファイル）
    /// NOTE: POST https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart
    /// </summary>
    private async UniTask<string> UploadFileMultipartAsync(string filePath, string fileName, string folderId, int retryCount = 0)
    {
        var fileContent = File.ReadAllBytes(filePath);
        var boundary = "===asset_uploader_boundary_" + Guid.NewGuid().ToString("N") + "===";

        // NOTE: メタデータJSON
        string metadataJson;
        if (!string.IsNullOrEmpty(folderId))
        {
            metadataJson = $"{{\"name\":\"{EscapeJsonString(fileName)}\",\"parents\":[\"{EscapeJsonString(folderId)}\"]}}";
        }
        else
        {
            metadataJson = $"{{\"name\":\"{EscapeJsonString(fileName)}\"}}";
        }

        // NOTE: マルチパートボディを構築
        var bodyBuilder = new StringBuilder();
        bodyBuilder.Append($"--{boundary}\r\n");
        bodyBuilder.Append("Content-Type: application/json; charset=UTF-8\r\n");
        bodyBuilder.Append("\r\n");
        bodyBuilder.Append(metadataJson);
        bodyBuilder.Append("\r\n");
        bodyBuilder.Append($"--{boundary}\r\n");
        bodyBuilder.Append("Content-Type: application/octet-stream\r\n");
        bodyBuilder.Append("\r\n");

        var headerBytes = Encoding.UTF8.GetBytes(bodyBuilder.ToString());
        var footerBytes = Encoding.UTF8.GetBytes($"\r\n--{boundary}--");

        var requestBody = new byte[headerBytes.Length + fileContent.Length + footerBytes.Length];
        Buffer.BlockCopy(headerBytes, 0, requestBody, 0, headerBytes.Length);
        Buffer.BlockCopy(fileContent, 0, requestBody, headerBytes.Length, fileContent.Length);
        Buffer.BlockCopy(footerBytes, 0, requestBody, headerBytes.Length + fileContent.Length, footerBytes.Length);

        var url = "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart";

        using (var request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(requestBody);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization", $"Bearer {_accessToken}");
            request.SetRequestHeader("Content-Type", $"multipart/related; boundary={boundary}");
            request.timeout = UPLOAD_TIMEOUT;

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await UniTask.Delay(200);
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                if (request.responseCode == 401 && retryCount < MAX_AUTH_RETRY)
                {
                    _accessToken = null;
                    if (await EnsureAccessTokenAsync())
                    {
                        return await UploadFileMultipartAsync(filePath, fileName, folderId, retryCount + 1);
                    }
                }
                Debug.LogError($"[AssetUploader] アップロード失敗: {request.responseCode} {request.error} - {request.downloadHandler.text}");
                return null;
            }

            var response = JsonUtility.FromJson<UploadResponse>(request.downloadHandler.text);
            return response?.id;
        }
    }

    /// <summary>
    /// テキストファイルをGoogle Driveにアップロード（カタログ新規作成用）
    /// </summary>
    private async UniTask<string> UploadTextFileAsync(string fileName, string content, string folderId)
    {
        var boundary = "===catalog_boundary_" + Guid.NewGuid().ToString("N") + "===";

        string metadataJson;
        if (!string.IsNullOrEmpty(folderId))
        {
            metadataJson = $"{{\"name\":\"{EscapeJsonString(fileName)}\",\"mimeType\":\"application/json\",\"parents\":[\"{EscapeJsonString(folderId)}\"]}}";
        }
        else
        {
            metadataJson = $"{{\"name\":\"{EscapeJsonString(fileName)}\",\"mimeType\":\"application/json\"}}";
        }

        var bodyBuilder = new StringBuilder();
        bodyBuilder.Append($"--{boundary}\r\n");
        bodyBuilder.Append("Content-Type: application/json; charset=UTF-8\r\n");
        bodyBuilder.Append("\r\n");
        bodyBuilder.Append(metadataJson);
        bodyBuilder.Append("\r\n");
        bodyBuilder.Append($"--{boundary}\r\n");
        bodyBuilder.Append("Content-Type: application/json\r\n");
        bodyBuilder.Append("\r\n");
        bodyBuilder.Append(content);
        bodyBuilder.Append($"\r\n--{boundary}--");

        var requestBody = Encoding.UTF8.GetBytes(bodyBuilder.ToString());
        var url = "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart";

        using (var request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(requestBody);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization", $"Bearer {_accessToken}");
            request.SetRequestHeader("Content-Type", $"multipart/related; boundary={boundary}");
            request.timeout = REQUEST_TIMEOUT;

            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[AssetUploader] カタログアップロード失敗: {request.responseCode} {request.error}");
                return null;
            }

            var response = JsonUtility.FromJson<UploadResponse>(request.downloadHandler.text);
            return response?.id;
        }
    }

    /// <summary>
    /// PATCHでファイル更新
    /// NOTE: PATCH https://www.googleapis.com/upload/drive/v3/files/{fileId}?uploadType=media
    /// </summary>
    private async UniTask<bool> UpdateFileAsync(string fileId, string content, int retryCount = 0)
    {
        var url = $"https://www.googleapis.com/upload/drive/v3/files/{fileId}?uploadType=media";
        var bodyBytes = Encoding.UTF8.GetBytes(content);

        using (var request = new UnityWebRequest(url, "PATCH"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization", $"Bearer {_accessToken}");
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = REQUEST_TIMEOUT;

            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                if (request.responseCode == 401 && retryCount < MAX_AUTH_RETRY)
                {
                    _accessToken = null;
                    if (await EnsureAccessTokenAsync())
                    {
                        return await UpdateFileAsync(fileId, content, retryCount + 1);
                    }
                }
                Debug.LogError($"[AssetUploader] ファイル更新失敗: {request.responseCode} {request.error}");
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// テキストファイルをダウンロード
    /// NOTE: GET https://www.googleapis.com/drive/v3/files/{fileId}?alt=media
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
                if (request.responseCode == 401 && retryCount < MAX_AUTH_RETRY)
                {
                    _accessToken = null;
                    if (await EnsureAccessTokenAsync())
                    {
                        return await DownloadTextFileAsync(fileId, retryCount + 1);
                    }
                }
                Debug.LogError($"[AssetUploader] ダウンロード失敗: {request.responseCode} {request.error}");
                return null;
            }

            return request.downloadHandler.text;
        }
    }

    /// <summary>
    /// ファイルを公開設定
    /// NOTE: POST https://www.googleapis.com/drive/v3/files/{fileId}/permissions
    /// </summary>
    private async UniTask<bool> SetFilePublicAsync(string fileId)
    {
        var url = $"https://www.googleapis.com/drive/v3/files/{fileId}/permissions";
        var permissionJson = "{\"role\":\"reader\",\"type\":\"anyone\"}";
        var bodyBytes = Encoding.UTF8.GetBytes(permissionJson);

        using (var request = new UnityWebRequest(url, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Authorization", $"Bearer {_accessToken}");
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = REQUEST_TIMEOUT;

            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[AssetUploader] 公開設定失敗: {request.error}");
                return false;
            }

            return true;
        }
    }

    #endregion

    #region Private Methods - Package

    /// <summary>
    /// unitypackageからGUID一覧を抽出
    /// NOTE: unitypackageはtar.gz形式。ディレクトリ名が32文字hexならGUID
    /// NOTE: Node.js版 extractGuidsFromPackage() の移植
    /// </summary>
    private static string[] ExtractGuidsFromPackage(string packagePath)
    {
        var guids = new HashSet<string>();

        try
        {
            using (var fileStream = File.OpenRead(packagePath))
            using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
            using (var memoryStream = new MemoryStream())
            {
                gzipStream.CopyTo(memoryStream);
                var tarData = memoryStream.ToArray();

                int offset = 0;
                while (offset + 512 <= tarData.Length)
                {
                    // NOTE: tarヘッダーは512バイト
                    // NOTE: ゼロブロック（終端）チェック
                    bool isZero = true;
                    for (int i = 0; i < 512; i++)
                    {
                        if (tarData[offset + i] != 0)
                        {
                            isZero = false;
                            break;
                        }
                    }
                    if (isZero) break;

                    // NOTE: ファイル名を取得（0-99バイト）
                    int nameEnd = 0;
                    for (int i = 0; i < 100; i++)
                    {
                        if (tarData[offset + i] == 0)
                        {
                            nameEnd = i;
                            break;
                        }
                        if (i == 99) nameEnd = 100;
                    }
                    var name = Encoding.UTF8.GetString(tarData, offset, nameEnd).Replace("\0", "");

                    // NOTE: ファイルサイズを取得（124-135バイト、8進数）
                    var sizeStr = Encoding.UTF8.GetString(tarData, offset + 124, 12).Replace("\0", "").Trim();
                    long fileSize = 0;
                    if (!string.IsNullOrEmpty(sizeStr))
                    {
                        try
                        {
                            fileSize = Convert.ToInt64(sizeStr, 8);
                        }
                        catch
                        {
                            fileSize = 0;
                        }
                    }

                    if (!string.IsNullOrEmpty(name))
                    {
                        // NOTE: パス形式: <guid>/asset, <guid>/pathname 等
                        var cleanName = name.TrimStart('.', '/');
                        var slashIndex = cleanName.IndexOf('/');
                        var dirName = slashIndex >= 0 ? cleanName.Substring(0, slashIndex) : cleanName;

                        if (dirName.Length == 32 && IsHexString(dirName))
                        {
                            guids.Add(dirName);
                        }
                    }

                    // NOTE: 次のエントリへ（ヘッダー512 + データ（512バイト単位にパディング））
                    long dataBlocks = (fileSize + 511) / 512;
                    offset += 512 + (int)(dataBlocks * 512);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AssetUploader] GUID抽出中にエラー: {ex.Message}");
        }

        var result = new string[guids.Count];
        guids.CopyTo(result);
        return result;
    }

    /// <summary>
    /// MD5ハッシュ計算
    /// </summary>
    private static string CalculateMD5(string filePath)
    {
        using (var md5 = MD5.Create())
        using (var stream = File.OpenRead(filePath))
        {
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    #endregion

    #region Private Methods - Settings

    /// <summary>
    /// settings.json読み込み
    /// NOTE: musa/melpomene/settings.json から googleAuthUrl, googleDriveFolderIdAsset, assetCatalogFileId を読み込む
    /// </summary>
    private static UploaderSettings LoadSettings()
    {
        var settingsPath = Path.Combine(Application.dataPath, "..", SETTINGS_RELATIVE_PATH);

        if (!File.Exists(settingsPath))
        {
            Debug.LogWarning($"[AssetUploader] settings.jsonが見つかりません: {settingsPath}");
            return new UploaderSettings();
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            return JsonUtility.FromJson<UploaderSettings>(json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AssetUploader] settings.json読み込み失敗: {ex.Message}");
            return new UploaderSettings();
        }
    }

    /// <summary>
    /// settings.json書き込み（catalogFileId保存）
    /// NOTE: 既存のsettings.jsonを読み込んで該当フィールドだけ更新する
    /// </summary>
    private static void SaveSettings(UploaderSettings settings)
    {
        var settingsPath = Path.Combine(Application.dataPath, "..", SETTINGS_RELATIVE_PATH);

        try
        {
            // NOTE: 既存ファイルを読み込んで、assetCatalogFileIdだけ更新
            string existingJson = "{}";
            if (File.Exists(settingsPath))
            {
                existingJson = File.ReadAllText(settingsPath);
            }

            // NOTE: 簡易的なJSON更新（既存のキーを維持しつつassetCatalogFileIdを追加/更新）
            if (existingJson.Contains("\"assetCatalogFileId\""))
            {
                // NOTE: 既存フィールドを更新
                var startIndex = existingJson.IndexOf("\"assetCatalogFileId\"");
                var colonIndex = existingJson.IndexOf(':', startIndex);
                var valueStart = existingJson.IndexOf('"', colonIndex);
                var valueEnd = existingJson.IndexOf('"', valueStart + 1);
                existingJson = existingJson.Substring(0, valueStart + 1) +
                               settings.assetCatalogFileId +
                               existingJson.Substring(valueEnd);
            }
            else
            {
                // NOTE: フィールドを追加（最後の } の前に挿入）
                var lastBrace = existingJson.LastIndexOf('}');
                if (lastBrace >= 0)
                {
                    var beforeBrace = existingJson.Substring(0, lastBrace).TrimEnd();
                    var needsComma = !beforeBrace.EndsWith("{") && !beforeBrace.EndsWith(",");
                    existingJson = beforeBrace +
                                   (needsComma ? ",\n" : "\n") +
                                   $"    \"assetCatalogFileId\": \"{settings.assetCatalogFileId}\"\n" +
                                   "}";
                }
            }

            // NOTE: ディレクトリが存在しない場合は作成
            var dir = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(settingsPath, existingJson);
            Debug.Log($"[AssetUploader] settings.jsonにassetCatalogFileIdを保存しました");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AssetUploader] settings.json書き込み失敗: {ex.Message}");
        }
    }

    #endregion

    #region Private Methods - Catalog Cache

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
                _catalogCache = JsonUtility.FromJson<GoogleDriveAssetImporter.AssetCatalog>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AssetUploader] カタログキャッシュ読み込み失敗: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// カタログキャッシュをJSON文字列から保存
    /// </summary>
    private static void SaveCatalogCacheFromJson(string catalogJson)
    {
        var cachePath = Path.Combine(Application.dataPath, "..", CATALOG_CACHE_FILE);
        File.WriteAllText(cachePath, catalogJson);
    }

    #endregion

    #region Private Methods - Utility

    /// <summary>
    /// エントリ削除メニューを表示
    /// </summary>
    private void ShowRemoveEntryMenu()
    {
        if (_catalogCache == null || _catalogCache.assets == null || _catalogCache.assets.Length == 0)
        {
            return;
        }

        var menu = new GenericMenu();
        foreach (var asset in _catalogCache.assets)
        {
            var assetName = asset.name;
            menu.AddItem(new GUIContent(assetName), false, () => { RemoveEntryAsync(assetName).Forget(); });
        }
        menu.ShowAsContext();
    }

    /// <summary>
    /// ファイルサイズをフォーマット
    /// </summary>
    private static string FormatFileSize(long bytes)
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

    /// <summary>
    /// 文字列が16進数かどうかを判定
    /// </summary>
    private static bool IsHexString(string str)
    {
        for (int i = 0; i < str.Length; i++)
        {
            var c = str[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// JSON文字列のエスケープ
    /// </summary>
    private static string EscapeJsonString(string str)
    {
        if (string.IsNullOrEmpty(str)) return "";
        return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
    }

    /// <summary>
    /// カタログJSONを手動構築
    /// NOTE: JsonUtilityはstring[]を正しくシリアライズできないため、guidsフィールド含むカタログを手動で構築
    /// </summary>
    private static string BuildCatalogJson(GoogleDriveAssetImporter.AssetCatalog catalog)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"version\": \"{EscapeJsonString(catalog.version)}\",");
        sb.AppendLine($"  \"updatedAt\": \"{EscapeJsonString(catalog.updatedAt)}\",");
        sb.AppendLine("  \"assets\": [");

        if (catalog.assets != null)
        {
            for (int i = 0; i < catalog.assets.Length; i++)
            {
                var asset = catalog.assets[i];
                sb.AppendLine("    {");
                sb.AppendLine($"      \"name\": \"{EscapeJsonString(asset.name)}\",");
                sb.AppendLine($"      \"fileId\": \"{EscapeJsonString(asset.fileId)}\",");
                sb.AppendLine($"      \"version\": \"{EscapeJsonString(asset.version)}\",");
                sb.AppendLine($"      \"md5\": \"{EscapeJsonString(asset.md5)}\",");
                sb.AppendLine($"      \"size\": {asset.size},");
                sb.AppendLine($"      \"description\": \"{EscapeJsonString(asset.description)}\",");
                sb.AppendLine($"      \"required\": {(asset.required ? "true" : "false")},");

                // NOTE: guidsは手動でJSON配列として出力
                sb.Append("      \"guids\": [");
                if (asset.guids != null && asset.guids.Length > 0)
                {
                    sb.AppendLine();
                    for (int g = 0; g < asset.guids.Length; g++)
                    {
                        sb.Append($"        \"{EscapeJsonString(asset.guids[g])}\"");
                        if (g < asset.guids.Length - 1) sb.Append(",");
                        sb.AppendLine();
                    }
                    sb.Append("      ");
                }
                sb.AppendLine("]");

                sb.Append("    }");
                if (i < catalog.assets.Length - 1) sb.Append(",");
                sb.AppendLine();
            }
        }

        sb.AppendLine("  ]");
        sb.Append("}");

        return sb.ToString();
    }

    #endregion

    #region Data Classes

    /// <summary>
    /// settings.json対応の設定クラス
    /// </summary>
    [Serializable]
    private class UploaderSettings
    {
        public string googleAuthUrl;
        public string googleDriveFolderIdAsset;
        public string assetCatalogFileId;
    }

    /// <summary>
    /// Lambda/OAuth トークン応答
    /// </summary>
    [Serializable]
    private class LambdaTokenResponse
    {
        public string access_token;
        public int expires_in;
        public string token_type;
    }

    /// <summary>
    /// トークンファイル読み込み用（フォールバック認証）
    /// </summary>
    [Serializable]
    private class RefreshTokenData
    {
        public string client_id;
        public string client_secret;
        public string refresh_token;
        public string access_token;
    }

    /// <summary>
    /// Google Drive API アップロード応答
    /// </summary>
    [Serializable]
    private class UploadResponse
    {
        public string id;
        public string name;
        public string mimeType;
    }

    #endregion
}
