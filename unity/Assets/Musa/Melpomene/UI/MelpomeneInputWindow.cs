#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using Cysharp.Threading.Tasks;

namespace Melpomene
{
    /// <summary>
    /// Melpomeneチケット入力ウィンドウ
    /// NOTE: Alt+クリックで表示されるチケット入力UI
    /// NOTE: ウィンドウ表示中はシーンクリックでオブジェクト自動選択
    /// </summary>
    public class MelpomeneInputWindow : EditorWindow
    {
        private MelpomeneTicket ticket;
        private Vector2 scrollPosition;
        private bool isSending;
        private GameObject targetObject;
        private bool isSceneClickEnabled = true;

        // GitHubマイルストーン関連
        private List<GitHubMilestone> githubMilestones = new List<GitHubMilestone>();
        private bool isFetchingMilestones;

        // Eureka連携: ローカルファイルパス（GitHubIssueモード時にアップロード対象）
        private string eurekaVideoLocalPath;
        private string eurekaLogLocalPath;
        private bool isEurekaGitHubIssueMode;

        private static MelpomeneInputWindow currentWindow;

        /// <summary>
        /// ウィンドウを表示
        /// </summary>
        public static void ShowWindow(Vector2 screenPosition, Vector3 worldPosition, GameObject targetObject)
        {
            // 既存のウィンドウがあれば閉じる
            if (currentWindow != null)
            {
                currentWindow.Close();
            }

            var window = CreateInstance<MelpomeneInputWindow>();
            window.titleContent = new GUIContent("Melpomene - New Ticket");
            window.minSize = new Vector2(400, 500);
            window.maxSize = new Vector2(600, 800);

            // チケットデータを準備
            window.ticket = MelpomeneManager.Instance.PrepareNewTicket(screenPosition, worldPosition, targetObject);
            window.targetObject = targetObject;

            // ウィンドウをマウス位置の近くに表示
            var mousePos = GUIUtility.GUIToScreenPoint(Event.current?.mousePosition ?? Vector2.zero);
            window.position = new Rect(mousePos.x + 20, mousePos.y - 100, 450, 550);

            window.ShowUtility();
            currentWindow = window;

            // GitHubマイルストーンを取得
            window.FetchGitHubMilestonesAsync().Forget();
        }

        /// <summary>
        /// Eureka実行後にチケット作成ウィンドウを表示
        /// NOTE: 動画URL・ログURLをdescriptionにプリフィルする
        /// </summary>
        public static void ShowWindowForEureka(string videoUrl, string logUrl, string logCode)
        {
            ShowWindowForEureka(videoUrl, logUrl, logCode, null, null, false);
        }

        /// <summary>
        /// Eureka実行後にチケット作成ウィンドウを表示（GitHub Issueモード対応）
        /// NOTE: isGitHubIssueModeがtrueの場合、Issue作成後にローカルファイルをアップロードしてコメント投稿
        /// </summary>
        /// <param name="videoUrl">動画URL（Google Drive時）またはローカルファイルパス</param>
        /// <param name="logUrl">ログURL（Google Drive時）またはローカルファイルパス</param>
        /// <param name="logCode">ログコード</param>
        /// <param name="videoLocalPath">動画ローカルパス（GitHub Issueモード時に使用）</param>
        /// <param name="logLocalPath">ログローカルパス（GitHub Issueモード時に使用）</param>
        /// <param name="isGitHubIssueMode">GitHub Issueモードかどうか</param>
        /// <param name="worldPosition">ワールド位置（Stageのtransformの逆）</param>
        public static void ShowWindowForEureka(string videoUrl, string logUrl, string logCode,
            string videoLocalPath, string logLocalPath, bool isGitHubIssueMode, Vector3 worldPosition = default)
        {
            // 既存のウィンドウがあれば閉じる
            if (currentWindow != null)
            {
                currentWindow.Close();
            }

            var window = CreateInstance<MelpomeneInputWindow>();
            window.titleContent = new GUIContent("Melpomene - Eureka Ticket");
            window.minSize = new Vector2(400, 500);
            window.maxSize = new Vector2(600, 800);

            // NOTE: Eureka連携パラメータを保持
            window.eurekaVideoLocalPath = videoLocalPath;
            window.eurekaLogLocalPath = logLocalPath;
            window.isEurekaGitHubIssueMode = isGitHubIssueMode;

            // チケットデータを準備
            window.ticket = MelpomeneManager.Instance.PrepareNewTicket(Vector2.zero, worldPosition, null);
            window.ticket.category = MelpomeneCategory.Bug;

            // NOTE: Eureka情報をdescriptionにプリフィルする
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("## Eureka Report");
            sb.AppendLine();

            if (isGitHubIssueMode)
            {
                // NOTE: GitHub Issueモードではプレースホルダを表示（Issue作成後にアップロード＆コメント）
                sb.AppendLine("※ Issue作成後に動画・ログがアップロードされコメントに添付されます");
            }
            else
            {
                if (!string.IsNullOrEmpty(videoUrl))
                {
                    sb.AppendLine($"**動画:** {videoUrl}");
                }
                if (!string.IsNullOrEmpty(logUrl))
                {
                    sb.AppendLine($"**ログ:** {logUrl}");
                }
            }
            if (!string.IsNullOrEmpty(logCode))
            {
                sb.AppendLine($"**ログコード:** {logCode}");
            }
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
            window.ticket.description = sb.ToString();

            // NOTE: 画面中央付近に表示
            float x = (Screen.currentResolution.width - 450) * 0.5f;
            float y = (Screen.currentResolution.height - 550) * 0.5f;
            window.position = new Rect(x, y, 450, 550);

            window.ShowUtility();
            currentWindow = window;

            // GitHubマイルストーンを取得
            window.FetchGitHubMilestonesAsync().Forget();
        }

        /// <summary>
        /// GitHubマイルストーンを非同期で取得
        /// </summary>
        private async UniTaskVoid FetchGitHubMilestonesAsync()
        {
            // 二重取得を防止
            if (isFetchingMilestones) return;

            isFetchingMilestones = true;
            Repaint();

            try
            {
                githubMilestones = await MelpomeneManager.Instance.GetGitHubMilestonesAsync();

                // デフォルトマイルストーンを適用（未設定の場合のみ）
                if (ticket != null && ticket.githubMilestoneNumber == 0)
                {
                    var defaultNumber = MelpomeneMilestoneManager.GetDefaultMilestoneNumber();
                    if (defaultNumber > 0)
                    {
                        var defaultMilestone = githubMilestones.Find(m => m.number == defaultNumber);
                        if (defaultMilestone != null)
                        {
                            ticket.githubMilestoneNumber = defaultMilestone.number;
                            ticket.milestoneName = defaultMilestone.title;
                        }
                    }
                }
            }
            finally
            {
                isFetchingMilestones = false;
                Repaint();
            }
        }

        private void OnEnable()
        {
            // SceneViewのイベントをフック
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            // SceneViewのイベントをアンフック
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        /// <summary>
        /// SceneView上のクリックでオブジェクトを自動選択
        /// NOTE: ウィンドウ表示中のみ有効
        /// </summary>
        private void OnSceneGUI(SceneView sceneView)
        {
            if (!isSceneClickEnabled) return;

            var e = Event.current;

            // 左クリック（Alt/Ctrlなしの通常クリック）
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && !e.control)
            {
                Vector2 screenPosition = e.mousePosition;
                Ray ray = HandleUtility.GUIPointToWorldRay(screenPosition);

                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    // ターゲットオブジェクトを更新
                    targetObject = hit.collider.gameObject;
                    UpdateTargetObject();

                    // ウィンドウを再描画
                    Repaint();

                    // イベントを消費してオブジェクト選択を防ぐ
                    e.Use();
                }
            }
        }

        private void OnGUI()
        {
            if (ticket == null)
            {
                EditorGUILayout.HelpBox("Ticket data is not initialized.", MessageType.Error);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            EditorGUI.BeginDisabledGroup(isSending);

            // ヘッダー
            EditorGUILayout.LabelField("New Ticket", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // 必須項目
            EditorGUILayout.LabelField("Required Fields", EditorStyles.boldLabel);

            ticket.userName = EditorGUILayout.TextField("User Name", ticket.userName);
            ticket.title = EditorGUILayout.TextField("Title", ticket.title);

            EditorGUILayout.LabelField("Description");
            ticket.description = EditorGUILayout.TextArea(ticket.description, GUILayout.Height(100));

            EditorGUILayout.Space();

            // 自動取得項目
            EditorGUILayout.LabelField("Target Info", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("Scene", ticket.sceneName);
            EditorGUI.EndDisabledGroup();

            // Target Object - 編集可能
            EditorGUI.BeginChangeCheck();
            targetObject = (GameObject)EditorGUILayout.ObjectField(
                "Target Object",
                targetObject,
                typeof(GameObject),
                true
            );
            if (EditorGUI.EndChangeCheck())
            {
                UpdateTargetObject();
            }

            // Target Object Path（読み取り専用で表示）
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("Object Path", string.IsNullOrEmpty(ticket.targetObjectPath) ? "(none)" : ticket.targetObjectPath);
            EditorGUILayout.Vector2Field("Screen Position", ticket.screenPosition);
            EditorGUILayout.Vector3Field("World Position", ticket.worldPosition);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space();

            // オプション項目
            EditorGUILayout.LabelField("Optional Settings", EditorStyles.boldLabel);

            ticket.priority = (MelpomenePriority)EditorGUILayout.EnumPopup("Priority", ticket.priority);
            ticket.category = (MelpomeneCategory)EditorGUILayout.EnumPopup("Category", ticket.category);

            // マイルストーン選択
            DrawMilestoneSelection();

            ticket.labels = EditorGUILayout.TextField("Additional Labels", ticket.labels);

            EditorGUILayout.Space(20);

            EditorGUI.EndDisabledGroup();

            // バリデーション
            bool isValid = ValidateTicket();

            if (!isValid)
            {
                EditorGUILayout.HelpBox("Please fill in all required fields (User Name, Title, Description)", MessageType.Warning);
            }

            if (!MelpomeneManager.Instance.IsConfigValid)
            {
                EditorGUILayout.HelpBox("GitHub configuration is not set. Please configure in Tools/Melpomene/Settings", MessageType.Error);

                if (GUILayout.Button("Open Settings"))
                {
                    MelpomeneConfig.OpenSettings();
                }
            }

            EditorGUILayout.Space();

            // ボタン
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Cancel", GUILayout.Height(30)))
            {
                Close();
            }

            EditorGUI.BeginDisabledGroup(!isValid || !MelpomeneManager.Instance.IsConfigValid || isSending);

            if (GUILayout.Button(isSending ? "Sending..." : "Create Issue", GUILayout.Height(30)))
            {
                CreateIssueAsync().Forget();
            }

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();
        }

        private bool ValidateTicket()
        {
            return !string.IsNullOrWhiteSpace(ticket.userName) &&
                   !string.IsNullOrWhiteSpace(ticket.title) &&
                   !string.IsNullOrWhiteSpace(ticket.description);
        }

        private async UniTaskVoid CreateIssueAsync()
        {
            isSending = true;
            Repaint();

            try
            {
                var createdTicket = await MelpomeneManager.Instance.CreateTicketAsync(ticket);

                if (createdTicket != null)
                {
                    // NOTE: GitHub Issueモードの場合、ファイルをアップロードしてコメントを投稿
                    if (isEurekaGitHubIssueMode)
                    {
                        await UploadAndCommentAsync(createdTicket.issueNumber);
                    }

                    EditorUtility.DisplayDialog(
                        "Melpomene",
                        $"Issue #{createdTicket.issueNumber} created successfully!\n\n{createdTicket.issueUrl}",
                        "OK"
                    );

                    // URLをクリップボードにコピー
                    GUIUtility.systemCopyBuffer = createdTicket.issueUrl;

                    Close();
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "Melpomene",
                        "Failed to create issue. Please check the console for errors.",
                        "OK"
                    );
                }
            }
            finally
            {
                isSending = false;
                Repaint();
            }
        }

        /// <summary>
        /// Eurekaファイルアップロード用デリゲート
        /// NOTE: MelpomeneはEurekaManagerを直接参照できないため、外部からデリゲートを設定する
        /// </summary>
        public static System.Func<int, string, string, UniTask> UploadToIssueDelegate;

        /// <summary>
        /// Eurekaファイルをアップロードし、IssueコメントにURLを投稿する
        /// NOTE: GitHub Issueモード時にのみ呼び出される
        /// NOTE: 実際のアップロード処理はEurekaManagerに委譲される
        /// </summary>
        private async UniTask UploadAndCommentAsync(int issueNumber)
        {
            if (UploadToIssueDelegate != null)
            {
                await UploadToIssueDelegate(issueNumber, eurekaVideoLocalPath, eurekaLogLocalPath);
            }
            else
            {
                Debug.LogWarning("[MelpomeneInputWindow] UploadToIssueDelegate not set, skipping file upload");
            }
        }

        private void OnDestroy()
        {
            if (currentWindow == this)
            {
                currentWindow = null;
            }
        }

        /// <summary>
        /// Target Objectが変更されたときにticketの情報を更新
        /// </summary>
        private void UpdateTargetObject()
        {
            if (targetObject != null)
            {
                ticket.targetObjectPath = GetHierarchyPath(targetObject);
                ticket.worldPosition = targetObject.transform.position;
            }
            else
            {
                ticket.targetObjectPath = "";
            }
        }

        /// <summary>
        /// GameObjectのHierarchyパスを取得
        /// </summary>
        private string GetHierarchyPath(GameObject obj)
        {
            string path = obj.name;
            Transform parent = obj.transform.parent;

            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        /// <summary>
        /// マイルストーン選択UIを描画
        /// NOTE: GitHubマイルストーンをドロップダウンで選択可能、「なし」も選択可能
        /// </summary>
        private void DrawMilestoneSelection()
        {
            EditorGUILayout.BeginHorizontal();

            if (isFetchingMilestones)
            {
                // 取得中
                EditorGUILayout.LabelField("Milestone", "読込中...");
            }
            else if (githubMilestones.Count == 0)
            {
                // マイルストーンがない場合 - 選択状態もクリアして整合性を保つ
                if (ticket != null && ticket.githubMilestoneNumber != 0)
                {
                    ticket.githubMilestoneNumber = 0;
                    ticket.milestoneName = "";
                }
                EditorGUILayout.LabelField("Milestone", "(なし)");
                if (GUILayout.Button("更新", GUILayout.Width(50)))
                {
                    FetchGitHubMilestonesAsync().Forget();
                }
            }
            else
            {
                // ドロップダウン用の選択肢を作成
                var options = new string[githubMilestones.Count + 1];
                options[0] = "(なし)";
                for (int i = 0; i < githubMilestones.Count; i++)
                {
                    options[i + 1] = githubMilestones[i].DisplayText;
                }

                // 現在選択中のインデックスを取得
                int currentIndex = 0;
                bool foundSelectedMilestone = false;
                if (ticket.githubMilestoneNumber > 0)
                {
                    for (int i = 0; i < githubMilestones.Count; i++)
                    {
                        if (githubMilestones[i].number == ticket.githubMilestoneNumber)
                        {
                            currentIndex = i + 1;
                            foundSelectedMilestone = true;
                            break;
                        }
                    }
                    // 選択中のマイルストーンが存在しなくなった場合はクリア
                    if (!foundSelectedMilestone)
                    {
                        ticket.githubMilestoneNumber = 0;
                        ticket.milestoneName = "";
                    }
                }

                // ドロップダウン表示
                int newIndex = EditorGUILayout.Popup("Milestone", currentIndex, options);

                if (newIndex != currentIndex)
                {
                    if (newIndex == 0)
                    {
                        // 「なし」を選択
                        ticket.githubMilestoneNumber = 0;
                        ticket.milestoneName = "";
                    }
                    else
                    {
                        // マイルストーンを選択
                        var selected = githubMilestones[newIndex - 1];
                        ticket.githubMilestoneNumber = selected.number;
                        ticket.milestoneName = selected.title;
                    }
                }

                // 更新ボタン
                if (GUILayout.Button("更新", GUILayout.Width(50)))
                {
                    FetchGitHubMilestonesAsync().Forget();
                }
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif
