using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace AssetManagerEditor
{
    /// <summary>
    /// アセットアップローダー
    /// ローカルのパッケージをS3にアップロードし、管理データDBに追加する
    /// 特殊な権限を持っていなければ実行できない
    /// </summary>
    public class AssetUploader : EditorWindow
    {
        private AssetManagerConfig _config;
        private string _packagePath = "";
        private string _assetName = "";
        private string _version = "1.0.0";
        private string _description = "";
        private bool _isUploading = false;
        private string _statusMessage = "";
        private float _uploadProgress = 0f;
        private bool _hasPermission = false;

        private static readonly HttpClient _httpClient = new HttpClient();

        [MenuItem("VTNTools/Asset Manager/Upload Assets")]
        public static void ShowWindow()
        {
            var window = GetWindow<AssetUploader>("Asset Uploader");
            window.minSize = new Vector2(500, 350);
            window.Show();
        }

        private void OnEnable()
        {
            _config = AssetManagerConfig.LoadOrCreate();
            CheckPermission();
        }

        private void CheckPermission()
        {
            _hasPermission = _config.HasUploadPermission();

            if (!_hasPermission)
            {
                _statusMessage = "You do not have upload permission. Contact administrator.";
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Asset Uploader", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            // 権限チェック
            if (!_hasPermission)
            {
                EditorGUILayout.HelpBox(
                    "Upload Permission Required\n\n" +
                    "You need special permission to upload assets.\n" +
                    "Either:\n" +
                    "1. Set ASSET_MANAGER_UPLOAD_KEY environment variable\n" +
                    "2. Add your username to AuthorizedUploadUsers in config",
                    MessageType.Error
                );

                EditorGUILayout.Space(10);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Refresh Permission", GUILayout.Width(150)))
                {
                    CheckPermission();
                }
                if (GUILayout.Button("Open Config", GUILayout.Width(150)))
                {
                    Selection.activeObject = _config;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(10);
                EditorGUILayout.LabelField($"Current User: {Environment.UserName}");

                return;
            }

            // 設定表示
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("API Endpoint:", GUILayout.Width(100));
            EditorGUILayout.LabelField(_config.ApiEndpoint);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Project ID:", GUILayout.Width(100));
            EditorGUILayout.LabelField(_config.ProjectId);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // パッケージ選択
            EditorGUILayout.LabelField("Upload Settings", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            _packagePath = EditorGUILayout.TextField("Package Path:", _packagePath);
            if (GUILayout.Button("Browse", GUILayout.Width(80)))
            {
                string path = EditorUtility.OpenFilePanel("Select Unity Package", "", "unitypackage");
                if (!string.IsNullOrEmpty(path))
                {
                    _packagePath = path;

                    // ファイル名からアセット名を推測
                    if (string.IsNullOrEmpty(_assetName))
                    {
                        _assetName = Path.GetFileNameWithoutExtension(path);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            _assetName = EditorGUILayout.TextField("Asset Name:", _assetName);
            _version = EditorGUILayout.TextField("Version:", _version);

            EditorGUILayout.LabelField("Description:");
            _description = EditorGUILayout.TextArea(_description, GUILayout.Height(60));

            EditorGUILayout.Space(10);

            // アップロードボタン
            GUI.enabled = !_isUploading && !string.IsNullOrEmpty(_packagePath) && !string.IsNullOrEmpty(_assetName);

            if (GUILayout.Button("Upload to S3", GUILayout.Height(30)))
            {
                if (ValidateInput())
                {
                    UploadPackage();
                }
            }

            GUI.enabled = true;

            // 進捗表示
            if (_isUploading)
            {
                EditorGUI.ProgressBar(
                    EditorGUILayout.GetControlRect(GUILayout.Height(20)),
                    _uploadProgress,
                    $"Uploading... {_uploadProgress * 100:F1}%"
                );
            }

            // ステータス表示
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                MessageType messageType = MessageType.Info;
                if (_statusMessage.Contains("Error") || _statusMessage.Contains("Failed"))
                {
                    messageType = MessageType.Error;
                }
                else if (_statusMessage.Contains("Success"))
                {
                    messageType = MessageType.Info;
                }

                EditorGUILayout.HelpBox(_statusMessage, messageType);
            }

            EditorGUILayout.Space(10);

            // 設定ボタン
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Config", GUILayout.Width(80)))
            {
                Selection.activeObject = _config;
            }
            if (GUILayout.Button("Refresh Permission", GUILayout.Width(130)))
            {
                CheckPermission();
            }
            EditorGUILayout.EndHorizontal();
        }

        private bool ValidateInput()
        {
            if (!File.Exists(_packagePath))
            {
                _statusMessage = "Error: Package file does not exist";
                return false;
            }

            if (!_packagePath.EndsWith(".unitypackage"))
            {
                _statusMessage = "Error: File must be a .unitypackage";
                return false;
            }

            if (string.IsNullOrEmpty(_assetName))
            {
                _statusMessage = "Error: Asset name is required";
                return false;
            }

            return true;
        }

        private async void UploadPackage()
        {
            _isUploading = true;
            _uploadProgress = 0f;
            _statusMessage = "Registering asset...";
            Repaint();

            try
            {
                // 1. Lambda APIでアセットを登録し、アップロードURLを取得
                var registerResult = await RegisterAsset();
                if (registerResult == null)
                {
                    return;
                }

                _statusMessage = "Uploading to S3...";
                _uploadProgress = 0.1f;
                Repaint();

                // 2. Presigned URLを使ってS3にアップロード
                bool uploadSuccess = await UploadToS3(registerResult.upload_url);

                if (uploadSuccess)
                {
                    _statusMessage = $"Success! Asset uploaded.\nHash: {registerResult.asset_hash}";
                    _uploadProgress = 1f;

                    // フォームをクリア
                    _packagePath = "";
                    _assetName = "";
                    _version = "1.0.0";
                    _description = "";
                }
                else
                {
                    _statusMessage = "Error: Upload to S3 failed";
                }
            }
            catch (Exception e)
            {
                _statusMessage = $"Error: {e.Message}";
                Debug.LogError($"Upload failed: {e}");
            }
            finally
            {
                _isUploading = false;
                Repaint();
            }
        }

        private async Task<AssetRegisterResponse> RegisterAsset()
        {
            try
            {
                string url = $"{_config.ApiEndpoint}/assets";

                var requestBody = new AssetRegisterRequest
                {
                    project_id = _config.ProjectId,
                    asset_name = _assetName,
                    version = _version,
                    description = _description
                };

                string jsonBody = JsonUtility.ToJson(requestBody);

                var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Headers.Add("x-api-key", _config.ApiKey);
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                var response = await _httpClient.SendAsync(request);
                string responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return JsonUtility.FromJson<AssetRegisterResponse>(responseBody);
                }
                else
                {
                    var error = JsonUtility.FromJson<ErrorResponse>(responseBody);
                    _statusMessage = $"Error: {error?.error ?? response.ReasonPhrase}";
                    return null;
                }
            }
            catch (Exception e)
            {
                _statusMessage = $"Error: Failed to register asset - {e.Message}";
                Debug.LogError($"RegisterAsset failed: {e}");
                return null;
            }
        }

        private async Task<bool> UploadToS3(string presignedUrl)
        {
            try
            {
                byte[] fileData = File.ReadAllBytes(_packagePath);
                long totalBytes = fileData.Length;

                using (var content = new ByteArrayContent(fileData))
                {
                    content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

                    // アップロード進捗を更新（簡易的な実装）
                    _uploadProgress = 0.5f;
                    Repaint();

                    var response = await _httpClient.PutAsync(presignedUrl, content);

                    _uploadProgress = 0.9f;
                    Repaint();

                    if (response.IsSuccessStatusCode)
                    {
                        Debug.Log($"Successfully uploaded {_assetName} to S3");
                        return true;
                    }
                    else
                    {
                        string errorBody = await response.Content.ReadAsStringAsync();
                        Debug.LogError($"S3 upload failed: {response.StatusCode} - {errorBody}");
                        return false;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"UploadToS3 failed: {e}");
                return false;
            }
        }
    }
}
