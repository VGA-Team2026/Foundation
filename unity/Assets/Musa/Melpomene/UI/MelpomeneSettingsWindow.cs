#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Melpomene
{
    /// <summary>
    /// Melpomene設定EditorWindow
    /// NOTE: 3つのJSON設定ファイルをタブで切り替えて編集する（#650）
    /// </summary>
    public class MelpomeneSettingsWindow : EditorWindow
    {
        #region Constants

        private const float LABEL_WIDTH = 180f;

        private static readonly string[] TAB_NAMES = new string[]
        {
            "サーバ設定",
            "プロジェクト設定",
            "個人設定"
        };

        #endregion

        #region Private Fields

        private int selectedTab;
        private MelpomeneConfig config;
        private Vector2 scrollPosition;

        // NOTE: ラベル配列のローカルコピー（EditorGUI用）
        private string labelsText;

        // NOTE: PlayerInputActionsから取得したアクション名一覧（Eurekaキーバインド用）
        private string[] playerInputActionNames;
        private int selectedActionIndex;

        // NOTE: 接続ステータス
        private enum ConnectionStatus { Unknown, Checking, Connected, Disconnected }
        private ConnectionStatus obsStatus = ConnectionStatus.Unknown;

        // NOTE: Google Drive接続テスト
        private enum GoogleDriveTestStatus { None, Testing, Success, Failed }
        private GoogleDriveTestStatus googleDriveTestStatus = GoogleDriveTestStatus.None;
        private string googleDriveTestMessage = "";

        #endregion

        #region Show

        public static void ShowWindow()
        {
            var window = GetWindow<MelpomeneSettingsWindow>("Melpomene設定");
            window.minSize = new Vector2(400, 350);
            window.Show();
        }

        /// <summary>
        /// Musa埋め込み用の初期化
        /// </summary>
        public void InitializeForMusa()
        {
            config = MelpomeneConfig.GetOrCreateConfig();
            SyncFromConfig();
        }

        /// <summary>
        /// Musa埋め込み用の描画
        /// NOTE: OnGUI()と同等の内容をMusaWindow内で描画する
        /// </summary>
        public void DrawContent()
        {
            if (config == null)
            {
                config = MelpomeneConfig.GetOrCreateConfig();
                SyncFromConfig();
            }

            if (config == null)
            {
                EditorGUILayout.HelpBox("設定の読み込みに失敗しました。", MessageType.Error);
                return;
            }

            // タブバー
            selectedTab = GUILayout.Toolbar(selectedTab, TAB_NAMES);

            EditorGUILayout.Space(8);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            var prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = LABEL_WIDTH;

            switch (selectedTab)
            {
                case 0:
                    DrawServerTab();
                    break;
                case 1:
                    DrawProjectTab();
                    break;
                case 2:
                    DrawLocalTab();
                    break;
            }

            EditorGUIUtility.labelWidth = prevLabelWidth;

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);
            DrawFooter();
        }

        #endregion

        #region Lifecycle

        private void OnEnable()
        {
            config = MelpomeneConfig.GetOrCreateConfig();
            SyncFromConfig();
        }

        private void OnFocus()
        {
            // NOTE: ウィンドウにフォーカスが戻った時にJSONから再読み込み
            MelpomeneConfig.ReloadConfig();
            config = MelpomeneConfig.GetOrCreateConfig();
            SyncFromConfig();
        }

        #endregion

        #region GUI

        private void OnGUI()
        {
            if (config == null)
            {
                EditorGUILayout.HelpBox("設定の読み込みに失敗しました。", MessageType.Error);
                return;
            }

            // タブバー
            selectedTab = GUILayout.Toolbar(selectedTab, TAB_NAMES);

            EditorGUILayout.Space(8);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            var prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = LABEL_WIDTH;

            switch (selectedTab)
            {
                case 0:
                    DrawServerTab();
                    break;
                case 1:
                    DrawProjectTab();
                    break;
                case 2:
                    DrawLocalTab();
                    break;
            }

            EditorGUIUtility.labelWidth = prevLabelWidth;

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(4);
            DrawFooter();
        }

        /// <summary>サーバ設定タブ（serverconf.json）</summary>
        private void DrawServerTab()
        {
            DrawFileLabel("serverconf.json", "gitignore対象");

            // === OBS WebSocket ===
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("OBS WebSocket", EditorStyles.boldLabel);
            DrawConnectionStatusBadge(obsStatus);
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel++;
            config.enableObs = EditorGUILayout.Toggle("有効", config.enableObs);

            using (new EditorGUI.DisabledScope(!config.enableObs))
            {
                config.obsHost = EditorGUILayout.TextField("ホスト", config.obsHost);
                config.obsPort = EditorGUILayout.IntField("ポート", config.obsPort);
                config.obsUsePassword = EditorGUILayout.Toggle("パスワードを使用", config.obsUsePassword);
                if (config.obsUsePassword)
                {
                    config.obsPassword = EditorGUILayout.PasswordField("パスワード", config.obsPassword);
                }

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(EditorGUI.indentLevel * 15);
                if (GUILayout.Button("接続テスト", GUILayout.Width(100)))
                {
                    CheckOBSConnectionAsync().Forget();
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;
        }

        /// <summary>接続ステータスバッジを描画</summary>
        private void DrawConnectionStatusBadge(ConnectionStatus status)
        {
            string label;
            Color color;
            switch (status)
            {
                case ConnectionStatus.Connected:
                    label = "● 接続中";
                    color = new Color(0.2f, 0.8f, 0.2f);
                    break;
                case ConnectionStatus.Disconnected:
                    label = "● 未接続";
                    color = new Color(0.8f, 0.2f, 0.2f);
                    break;
                case ConnectionStatus.Checking:
                    label = "● 確認中...";
                    color = new Color(0.8f, 0.8f, 0.2f);
                    break;
                default:
                    label = "● 未確認";
                    color = Color.gray;
                    break;
            }

            var prevColor = GUI.color;
            GUI.color = color;
            GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(80));
            GUI.color = prevColor;
        }

        /// <summary>OBS WebSocket 5.x 接続テスト（完全なハンドシェイク+認証）</summary>
        /// NOTE: OBSWebSocketClientはasmdef外のため、ClientWebSocketで直接OBSプロトコルを実装
        private async UniTaskVoid CheckOBSConnectionAsync()
        {
            obsStatus = ConnectionStatus.Checking;
            Repaint();

            ClientWebSocket ws = null;
            CancellationTokenSource cts = null;

            try
            {
                ws = new ClientWebSocket();
                cts = new CancellationTokenSource(5000); // 5秒タイムアウト
                var uri = new Uri($"ws://{config.obsHost}:{config.obsPort}");

                // Step 1: WebSocket接続
                await ws.ConnectAsync(uri, cts.Token);

                // Step 2: Hello (op=0) を受信
                var helloJson = await ReceiveObsMessage(ws, cts.Token);
                var hello = JsonUtility.FromJson<ObsMessage>(helloJson);
                if (hello.op != 0)
                {
                    obsStatus = ConnectionStatus.Disconnected;
                    return;
                }

                // Step 3: Identify (op=1) を送信
                string identifyJson;
                if (hello.d?.authentication != null && !string.IsNullOrEmpty(hello.d.authentication.challenge) && config.obsUsePassword && !string.IsNullOrEmpty(config.obsPassword))
                {
                    // NOTE: 認証あり
                    string authResponse = GenerateObsAuthResponse(config.obsPassword, hello.d.authentication.challenge, hello.d.authentication.salt);
                    identifyJson = $"{{\"op\":1,\"d\":{{\"rpcVersion\":1,\"authentication\":\"{authResponse}\"}}}}";
                }
                else
                {
                    // NOTE: 認証なし
                    identifyJson = "{\"op\":1,\"d\":{\"rpcVersion\":1}}";
                }

                var identifyBytes = Encoding.UTF8.GetBytes(identifyJson);
                await ws.SendAsync(new ArraySegment<byte>(identifyBytes), WebSocketMessageType.Text, true, cts.Token);

                // Step 4: Identified (op=2) を受信
                var identifiedJson = await ReceiveObsMessage(ws, cts.Token);
                var identified = JsonUtility.FromJson<ObsMessage>(identifiedJson);
                obsStatus = (identified.op == 2) ? ConnectionStatus.Connected : ConnectionStatus.Disconnected;

                // 正常切断
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Health check done", CancellationToken.None);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MelpomeneSettings] OBS接続テスト失敗: {e.Message}");
                obsStatus = ConnectionStatus.Disconnected;
            }
            finally
            {
                ws?.Dispose();
                cts?.Dispose();
                Repaint();
            }
        }

        /// <summary>OBS WebSocketからメッセージを1件受信</summary>
        private static async UniTask<string> ReceiveObsMessage(ClientWebSocket ws, CancellationToken token)
        {
            var buffer = new byte[4096];
            var sb = new StringBuilder();
            WebSocketReceiveResult result;
            do
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            } while (!result.EndOfMessage);
            return sb.ToString();
        }

        /// <summary>OBS WebSocket 5.x 認証レスポンス生成</summary>
        /// NOTE: base64(sha256(base64(sha256(password + salt)) + challenge))
        private static string GenerateObsAuthResponse(string password, string challenge, string salt)
        {
            using (var sha256 = SHA256.Create())
            {
                var secretHash = sha256.ComputeHash(Encoding.UTF8.GetBytes(password + salt));
                var secretBase64 = Convert.ToBase64String(secretHash);
                var authHash = sha256.ComputeHash(Encoding.UTF8.GetBytes(secretBase64 + challenge));
                return Convert.ToBase64String(authHash);
            }
        }

        // NOTE: OBS WebSocket メッセージの最小限JSON構造
        // Hello: { "op": 0, "d": { "authentication": { "challenge": "...", "salt": "..." } } }
        [Serializable] private class ObsMessage { public int op; public ObsHelloData d; }
        [Serializable] private class ObsHelloData { public ObsAuthData authentication; }
        [Serializable] private class ObsAuthData { public string challenge; public string salt; }

        /// <summary>プロジェクト設定タブ（settings.json）</summary>
        private void DrawProjectTab()
        {
            DrawFileLabel("settings.json", "Git管理対象");

            EditorGUILayout.LabelField("GitHub リポジトリ", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            config.repositoryOwner = EditorGUILayout.TextField("オーナー", config.repositoryOwner);
            config.repositoryName = EditorGUILayout.TextField("リポジトリ名", config.repositoryName);
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("デフォルト設定", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            // NOTE: ラベルはカンマ区切りテキストで編集
            EditorGUI.BeginChangeCheck();
            labelsText = EditorGUILayout.TextField("デフォルトラベル", labelsText);
            if (EditorGUI.EndChangeCheck())
            {
                config.defaultLabels = labelsText.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < config.defaultLabels.Length; i++)
                {
                    config.defaultLabels[i] = config.defaultLabels[i].Trim();
                }
            }

            config.defaultPriority = (MelpomenePriority)EditorGUILayout.EnumPopup("デフォルト優先度", config.defaultPriority);
            config.defaultCategory = (MelpomeneCategory)EditorGUILayout.EnumPopup("デフォルトカテゴリ", config.defaultCategory);
            config.cacheDurationMinutes = EditorGUILayout.IntField("キャッシュ有効期限（分）", config.cacheDurationMinutes);
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("Google Drive", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            config.googleDriveFolderIdLog = EditorGUILayout.TextField("ログ用フォルダID", config.googleDriveFolderIdLog);
            config.googleDriveFolderIdVideo = EditorGUILayout.TextField("動画用フォルダID", config.googleDriveFolderIdVideo);

            // NOTE: 認証URL入力（Lambda URL）
            config.googleAuthUrl = EditorGUILayout.TextField("認証URL", config.googleAuthUrl);

            bool hasAuthUrl = !string.IsNullOrEmpty(config.googleAuthUrl);
            bool hasFolderId = !string.IsNullOrEmpty(config.googleDriveFolderIdLog) || !string.IsNullOrEmpty(config.googleDriveFolderIdVideo);

            if (!hasAuthUrl)
            {
                EditorGUILayout.HelpBox("認証URLが未設定です。Lambda Function URLを設定してください。", MessageType.Warning);
            }

            // NOTE: 接続テストボタン
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15);
            using (new EditorGUI.DisabledScope(!hasAuthUrl || !hasFolderId || googleDriveTestStatus == GoogleDriveTestStatus.Testing))
            {
                if (GUILayout.Button("接続テスト", GUILayout.Width(100)))
                {
                    TestGoogleDriveConnectionAsync().Forget();
                }
            }

            switch (googleDriveTestStatus)
            {
                case GoogleDriveTestStatus.Testing:
                    GUILayout.Label("確認中...", EditorStyles.miniLabel);
                    break;
                case GoogleDriveTestStatus.Success:
                    var pc = GUI.color;
                    GUI.color = new Color(0.2f, 0.8f, 0.2f);
                    GUILayout.Label($"● {googleDriveTestMessage}", EditorStyles.miniLabel);
                    GUI.color = pc;
                    break;
                case GoogleDriveTestStatus.Failed:
                    pc = GUI.color;
                    GUI.color = new Color(0.8f, 0.2f, 0.2f);
                    GUILayout.Label($"● {googleDriveTestMessage}", EditorStyles.miniLabel);
                    GUI.color = pc;
                    break;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
        }

        /// <summary>個人設定タブ（settings.local.json）</summary>
        private void DrawLocalTab()
        {
            DrawFileLabel("settings.local.json", "gitignore対象");

            EditorGUILayout.LabelField("GitHub 認証", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            config.accessToken = EditorGUILayout.PasswordField("Personal Access Token", config.accessToken);

            // NOTE: トークン設定状態の表示
            if (string.IsNullOrEmpty(config.accessToken))
            {
                EditorGUILayout.HelpBox("PATが未設定です。GitHub Issue連携が無効になります。", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField("ステータス", "設定済み", EditorStyles.miniLabel);
            }

            EditorGUI.indentLevel--;

            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("ユーザー設定", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            config.defaultUserName = EditorGUILayout.TextField("ユーザー名", config.defaultUserName);
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("機能設定", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            config.enableAltClickShortcut = EditorGUILayout.Toggle("Alt+クリックでチケット作成", config.enableAltClickShortcut);
            config.enableTicketDisplay = EditorGUILayout.Toggle("シーン上にチケット表示", config.enableTicketDisplay);
            config.autoRefreshCache = EditorGUILayout.Toggle("起動時に自動更新", config.autoRefreshCache);
            config.enableNotificationPolling = EditorGUILayout.Toggle("PR/Actions通知ポーリング", config.enableNotificationPolling);

            // NOTE: 手動更新ボタン
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUI.indentLevel * 15);
            if (GUILayout.Button("チケットを今すぐ更新", GUILayout.Width(150)))
            {
                var manager = MelpomeneManager.Instance;
                if (manager == null)
                {
                    Debug.LogWarning("[Melpomene] MelpomeneManager が初期化されていません");
                }
                else
                {
                    manager.RefreshCacheAsync().Forget();
                    Debug.Log("[Melpomene] チケットキャッシュを更新しました");
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;

            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("Eureka", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            config.eurekaAutoRecord = EditorGUILayout.Toggle("自動録画", config.eurekaAutoRecord);

            // NOTE: PlayerInputActionsからアクション名を取得してドロップダウンで選択
            if (playerInputActionNames == null || playerInputActionNames.Length == 0)
            {
                LoadPlayerInputActionNames();
            }
            if (playerInputActionNames != null && playerInputActionNames.Length > 0)
            {
                selectedActionIndex = Array.IndexOf(playerInputActionNames, config.eurekaKeyBinding);
                if (selectedActionIndex < 0) selectedActionIndex = 0;
                selectedActionIndex = EditorGUILayout.Popup("キーバインド", selectedActionIndex, playerInputActionNames);
                config.eurekaKeyBinding = playerInputActionNames[selectedActionIndex];
            }
            else
            {
                config.eurekaKeyBinding = EditorGUILayout.TextField("キーバインド", config.eurekaKeyBinding);
                EditorGUILayout.HelpBox("PlayerInputActions.inputactionsが見つかりません。", MessageType.Warning);
            }
            EditorGUI.indentLevel--;
        }

        /// <summary>ファイル名ラベルを描画</summary>
        private void DrawFileLabel(string fileName, string note)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            var style = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight
            };
            EditorGUILayout.LabelField($"{fileName}  ({note})", style);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }

        /// <summary>フッター（保存ボタン）</summary>
        private void DrawFooter()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("保存", GUILayout.Height(28)))
            {
                SaveCurrentTab();
            }

            if (GUILayout.Button("全て保存", GUILayout.Height(28)))
            {
                config.Save();
                Debug.Log("[Melpomene] 全設定を保存しました");
                ShowNotification(new GUIContent("全設定を保存しました"));
            }

            if (GUILayout.Button("再読み込み", GUILayout.Height(28)))
            {
                MelpomeneConfig.ReloadConfig();
                config = MelpomeneConfig.GetOrCreateConfig();
                SyncFromConfig();
                Debug.Log("[Melpomene] 設定を再読み込みしました");
                ShowNotification(new GUIContent("再読み込みしました"));
            }

            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Utility

        /// <summary>現在のタブに対応するファイルだけ保存</summary>
        private void SaveCurrentTab()
        {
            switch (selectedTab)
            {
                case 0:
                    config.SaveServerConf();
                    ShowNotification(new GUIContent("サーバ設定を保存しました"));
                    break;
                case 1:
                    config.SaveProjectSettings();
                    ShowNotification(new GUIContent("プロジェクト設定を保存しました"));
                    break;
                case 2:
                    config.SaveLocalSettings();
                    ShowNotification(new GUIContent("個人設定を保存しました"));
                    break;
            }
        }

        /// <summary>configから表示用変数を同期</summary>
        private void SyncFromConfig()
        {
            labelsText = config.defaultLabels != null ? string.Join(", ", config.defaultLabels) : "";
            LoadPlayerInputActionNames();
        }

        /// <summary>PlayerInputActions.inputactionsからアクション名一覧を読み込む</summary>
        /// NOTE: InputSystem asmdef参照を避けるため、.inputactionsファイルをJSONとして直接パースする
        private void LoadPlayerInputActionNames()
        {
            // NOTE: .inputactionsファイルを検索（TextAssetとして認識される）
            var guids = AssetDatabase.FindAssets("PlayerInputActions");
            string filePath = null;
            foreach (var guid in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.EndsWith(".inputactions"))
                {
                    filePath = p;
                    break;
                }
            }

            if (filePath == null)
            {
                playerInputActionNames = null;
                return;
            }

            try
            {
                // NOTE: .inputactionsはJSON形式 → maps[].actions[].name を抽出
                var jsonText = File.ReadAllText(filePath);
                var json = JsonUtility.FromJson<InputActionsJson>(jsonText);
                if (json?.maps == null)
                {
                    playerInputActionNames = null;
                    return;
                }

                var names = new List<string>();
                foreach (var map in json.maps)
                {
                    if (map.actions == null) continue;
                    foreach (var action in map.actions)
                    {
                        if (!string.IsNullOrEmpty(action.name))
                        {
                            names.Add(action.name);
                        }
                    }
                }
                playerInputActionNames = names.ToArray();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MelpomeneSettings] PlayerInputActions解析エラー: {e.Message}");
                playerInputActionNames = null;
            }
        }

        /// <summary>Google Driveフォルダへの接続テスト</summary>
        /// NOTE: Lambda経由でサービスアカウントのアクセストークンを取得し、フォルダアクセスを確認する
        private async UniTaskVoid TestGoogleDriveConnectionAsync()
        {
            googleDriveTestStatus = GoogleDriveTestStatus.Testing;
            googleDriveTestMessage = "";
            Repaint();

            try
            {
                if (string.IsNullOrEmpty(config.googleAuthUrl))
                {
                    googleDriveTestStatus = GoogleDriveTestStatus.Failed;
                    googleDriveTestMessage = "認証URL未設定";
                    Repaint();
                    return;
                }

                // NOTE: Lambda経由でアクセストークンを取得
                var tokenUrl = config.googleAuthUrl.TrimEnd('/') + "/token";
                string accessToken;
                using (var request = UnityEngine.Networking.UnityWebRequest.Get(tokenUrl))
                {
                    request.timeout = 15;
                    var op = request.SendWebRequest();
                    while (!op.isDone) await UniTask.Delay(100);

                    if (request.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                    {
                        googleDriveTestStatus = GoogleDriveTestStatus.Failed;
                        googleDriveTestMessage = $"トークン取得失敗: {request.responseCode}";
                        Repaint();
                        return;
                    }

                    var tokenResponse = JsonUtility.FromJson<GoogleTokenRefreshResponse>(request.downloadHandler.text);
                    accessToken = tokenResponse?.access_token;
                    if (string.IsNullOrEmpty(accessToken))
                    {
                        googleDriveTestStatus = GoogleDriveTestStatus.Failed;
                        googleDriveTestMessage = "access_tokenが取得できません";
                        Repaint();
                        return;
                    }
                }

                // NOTE: フォルダの中身を取得してアクセス確認
                var folderId = !string.IsNullOrEmpty(config.googleDriveFolderIdLog)
                    ? config.googleDriveFolderIdLog
                    : config.googleDriveFolderIdVideo;

                var listUrl = $"https://www.googleapis.com/drive/v3/files?q='{folderId}'+in+parents&fields=files(id,name)&pageSize=5";
                using (var request = UnityEngine.Networking.UnityWebRequest.Get(listUrl))
                {
                    request.SetRequestHeader("Authorization", $"Bearer {accessToken}");
                    request.timeout = 15;
                    var op = request.SendWebRequest();
                    while (!op.isDone) await UniTask.Delay(100);

                    if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                    {
                        var response = JsonUtility.FromJson<GoogleDriveFileListResponse>(request.downloadHandler.text);
                        int count = response?.files?.Length ?? 0;
                        googleDriveTestStatus = GoogleDriveTestStatus.Success;
                        googleDriveTestMessage = $"接続成功（{count}件取得）";
                    }
                    else
                    {
                        googleDriveTestStatus = GoogleDriveTestStatus.Failed;
                        googleDriveTestMessage = $"フォルダアクセス失敗: {request.responseCode}";
                    }
                }
            }
            catch (Exception e)
            {
                googleDriveTestStatus = GoogleDriveTestStatus.Failed;
                googleDriveTestMessage = e.Message;
            }

            Repaint();
        }

        [Serializable] private class GoogleTokenRefreshResponse { public string access_token; }
        [Serializable] private class GoogleDriveFileListResponse { public GoogleDriveFileEntry[] files; }
        [Serializable] private class GoogleDriveFileEntry { public string id; public string name; }

        // NOTE: .inputactionsファイルのJSON構造（必要最小限）
        [Serializable] private class InputActionsJson { public InputActionMapJson[] maps; }
        [Serializable] private class InputActionMapJson { public InputActionJson[] actions; }
        [Serializable] private class InputActionJson { public string name; }

        #endregion
    }
}
#endif
