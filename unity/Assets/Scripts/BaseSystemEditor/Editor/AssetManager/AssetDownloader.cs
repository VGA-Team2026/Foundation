using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace AssetManagerEditor
{
    /// <summary>
    /// アセットダウンローダー
    /// Lambda APIから管理データを取得し、ローカルに存在しないアセットをS3からダウンロードする
    /// </summary>
    public class AssetDownloader : EditorWindow
    {
        private AssetManagerConfig _config;
        private List<AssetInfo> _remoteAssets = new List<AssetInfo>();
        private LocalAssetRegistry _localRegistry;
        private Vector2 _scrollPosition;
        private bool _isLoading = false;
        private string _statusMessage = "";
        private Dictionary<string, bool> _downloadSelection = new Dictionary<string, bool>();
        private Dictionary<string, float> _downloadProgress = new Dictionary<string, float>();

        private static readonly HttpClient _httpClient = new HttpClient();

        [MenuItem("VTNTools/Asset Manager/Download Assets")]
        public static void ShowWindow()
        {
            var window = GetWindow<AssetDownloader>("Asset Downloader");
            window.minSize = new Vector2(600, 400);
            window.Show();
        }

        private void OnEnable()
        {
            _config = AssetManagerConfig.LoadOrCreate();
            LoadLocalRegistry();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Asset Downloader", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

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

            // ボタン類
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !_isLoading;

            if (GUILayout.Button("Fetch Asset List", GUILayout.Width(150)))
            {
                FetchAssetList();
            }

            if (GUILayout.Button("Download Selected", GUILayout.Width(150)))
            {
                DownloadSelectedAssets();
            }

            if (GUILayout.Button("Download All New", GUILayout.Width(150)))
            {
                DownloadAllNewAssets();
            }

            GUI.enabled = true;

            if (GUILayout.Button("Config", GUILayout.Width(80)))
            {
                Selection.activeObject = _config;
            }

            EditorGUILayout.EndHorizontal();

            // ステータス表示
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.HelpBox(_statusMessage, _statusMessage.Contains("Error") ? MessageType.Error : MessageType.Info);
            }

            EditorGUILayout.Space(10);

            // アセット一覧
            EditorGUILayout.LabelField($"Remote Assets ({_remoteAssets.Count})", EditorStyles.boldLabel);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            foreach (var asset in _remoteAssets)
            {
                DrawAssetItem(asset);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawAssetItem(AssetInfo asset)
        {
            bool isInstalled = IsAssetInstalled(asset.asset_hash);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();

            // 選択チェックボックス
            if (!_downloadSelection.ContainsKey(asset.asset_hash))
            {
                _downloadSelection[asset.asset_hash] = false;
            }

            GUI.enabled = !isInstalled && !_isLoading;
            _downloadSelection[asset.asset_hash] = EditorGUILayout.Toggle(_downloadSelection[asset.asset_hash], GUILayout.Width(20));
            GUI.enabled = true;

            // アセット情報
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(asset.asset_name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Version: {asset.version} | Hash: {asset.asset_hash.Substring(0, 8)}...");
            if (!string.IsNullOrEmpty(asset.description))
            {
                EditorGUILayout.LabelField(asset.description, EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndVertical();

            // ステータス
            if (isInstalled)
            {
                EditorGUILayout.LabelField("Installed", EditorStyles.miniLabel, GUILayout.Width(80));
            }
            else if (_downloadProgress.ContainsKey(asset.asset_hash))
            {
                EditorGUI.ProgressBar(
                    EditorGUILayout.GetControlRect(GUILayout.Width(100)),
                    _downloadProgress[asset.asset_hash],
                    $"{_downloadProgress[asset.asset_hash] * 100:F0}%"
                );
            }
            else
            {
                EditorGUILayout.LabelField("Not Installed", EditorStyles.miniLabel, GUILayout.Width(80));
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private bool IsAssetInstalled(string assetHash)
        {
            if (_localRegistry == null) return false;
            return _localRegistry.installed_assets.Exists(a => a.asset_hash == assetHash);
        }

        private void LoadLocalRegistry()
        {
            string registryPath = Path.Combine(_config.FullImportPath, ".asset_registry.json");
            if (File.Exists(registryPath))
            {
                try
                {
                    string json = File.ReadAllText(registryPath);
                    _localRegistry = JsonUtility.FromJson<LocalAssetRegistry>(json);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to load local registry: {e.Message}");
                    _localRegistry = new LocalAssetRegistry();
                }
            }
            else
            {
                _localRegistry = new LocalAssetRegistry();
            }
        }

        private void SaveLocalRegistry()
        {
            string registryPath = Path.Combine(_config.FullImportPath, ".asset_registry.json");

            // ディレクトリ作成
            string directory = Path.GetDirectoryName(registryPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonUtility.ToJson(_localRegistry, true);
            File.WriteAllText(registryPath, json);
        }

        private async void FetchAssetList()
        {
            _isLoading = true;
            _statusMessage = "Fetching asset list...";
            Repaint();

            try
            {
                string url = $"{_config.ApiEndpoint}/assets?project_id={_config.ProjectId}";

                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("x-api-key", _config.ApiKey);

                var response = await _httpClient.SendAsync(request);
                string responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonUtility.FromJson<AssetListResponse>(responseBody);
                    _remoteAssets = result.assets ?? new List<AssetInfo>();
                    _statusMessage = $"Found {_remoteAssets.Count} assets";
                }
                else
                {
                    var error = JsonUtility.FromJson<ErrorResponse>(responseBody);
                    _statusMessage = $"Error: {error?.error ?? response.ReasonPhrase}";
                }
            }
            catch (Exception e)
            {
                _statusMessage = $"Error: {e.Message}";
                Debug.LogError($"FetchAssetList failed: {e}");
            }
            finally
            {
                _isLoading = false;
                Repaint();
            }
        }

        private async void DownloadSelectedAssets()
        {
            var selectedAssets = new List<AssetInfo>();
            foreach (var asset in _remoteAssets)
            {
                if (_downloadSelection.TryGetValue(asset.asset_hash, out bool selected) && selected)
                {
                    if (!IsAssetInstalled(asset.asset_hash))
                    {
                        selectedAssets.Add(asset);
                    }
                }
            }

            if (selectedAssets.Count == 0)
            {
                _statusMessage = "No assets selected for download";
                return;
            }

            await DownloadAssets(selectedAssets);
        }

        private async void DownloadAllNewAssets()
        {
            var newAssets = new List<AssetInfo>();
            foreach (var asset in _remoteAssets)
            {
                if (!IsAssetInstalled(asset.asset_hash))
                {
                    newAssets.Add(asset);
                }
            }

            if (newAssets.Count == 0)
            {
                _statusMessage = "All assets are already installed";
                return;
            }

            await DownloadAssets(newAssets);
        }

        private async Task DownloadAssets(List<AssetInfo> assets)
        {
            _isLoading = true;
            int successCount = 0;
            int failCount = 0;

            // 一時ディレクトリ作成
            if (!Directory.Exists(_config.FullTempPath))
            {
                Directory.CreateDirectory(_config.FullTempPath);
            }

            foreach (var asset in assets)
            {
                _statusMessage = $"Downloading {asset.asset_name}...";
                Repaint();

                try
                {
                    bool success = await DownloadAndImportAsset(asset);
                    if (success)
                    {
                        successCount++;
                        _downloadSelection[asset.asset_hash] = false;
                    }
                    else
                    {
                        failCount++;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to download {asset.asset_name}: {e.Message}");
                    failCount++;
                }
            }

            _statusMessage = $"Download complete: {successCount} succeeded, {failCount} failed";
            _isLoading = false;
            _downloadProgress.Clear();

            // レジストリ保存
            SaveLocalRegistry();

            // AssetDatabase更新
            AssetDatabase.Refresh();
            Repaint();
        }

        private async Task<bool> DownloadAndImportAsset(AssetInfo asset)
        {
            if (string.IsNullOrEmpty(asset.download_url))
            {
                Debug.LogError($"No download URL for {asset.asset_name}");
                return false;
            }

            _downloadProgress[asset.asset_hash] = 0f;

            // ダウンロード先パス
            string fileName = $"{asset.asset_hash}.unitypackage";
            string downloadPath = Path.Combine(_config.FullTempPath, fileName);

            try
            {
                // ダウンロード
                using (var response = await _httpClient.GetAsync(asset.download_url, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    var receivedBytes = 0L;

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var buffer = new byte[8192];
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            receivedBytes += bytesRead;

                            if (totalBytes > 0)
                            {
                                _downloadProgress[asset.asset_hash] = (float)receivedBytes / totalBytes;
                                Repaint();
                            }
                        }
                    }
                }

                _downloadProgress[asset.asset_hash] = 1f;
                Repaint();

                // パッケージをインポート
                AssetDatabase.ImportPackage(downloadPath, false);

                // ローカルレジストリに追加
                _localRegistry.installed_assets.Add(new InstalledAsset
                {
                    asset_hash = asset.asset_hash,
                    asset_name = asset.asset_name,
                    version = asset.version,
                    installed_at = DateTime.UtcNow.ToString("o"),
                    install_path = _config.ImportTargetPath
                });

                // 一時ファイル削除
                if (File.Exists(downloadPath))
                {
                    File.Delete(downloadPath);
                }

                Debug.Log($"Successfully imported: {asset.asset_name}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Download failed for {asset.asset_name}: {e.Message}");

                // 一時ファイル削除
                if (File.Exists(downloadPath))
                {
                    try { File.Delete(downloadPath); } catch { }
                }

                return false;
            }
        }
    }
}
