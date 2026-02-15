#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace Melpomene
{
    /// <summary>
    /// Melpomeneチケット詳細ウィンドウ
    /// NOTE: チケットの詳細情報とコメントを表示
    /// NOTE: コメントの投稿も可能
    /// NOTE: タイトル、説明、位置の編集も可能
    /// </summary>
    public class MelpomeneTicketDetailWindow : EditorWindow
    {
        private MelpomeneTicket ticket;
        private Vector2 scrollPosition;
        private Vector2 commentsScrollPosition;
        private GameObject newTargetObject;
        private bool isEditingTarget;

        // 編集用フィールド
        private string editTitle;
        private string editDescription;
        private Vector3 editWorldPosition;

        // 元の値（比較用）
        private string originalTitle;
        private string originalDescription;
        private Vector3 originalWorldPosition;
        private string originalTargetObjectPath;

        // DirtyFlag
        private bool isDirty = false;
        private bool isUpdating = false;

        // Foldout状態
        private bool metadataFoldout = true;

        // コメント関連
        private List<MelpomeneComment> comments = new List<MelpomeneComment>();
        private string newCommentText = "";
        private bool isLoadingComments = false;
        private bool isPostingComment = false;
        private MelpomeneGitHubClient gitHubClient;
        private bool commentsFoldout = true;

        // クローズ処理
        private bool isClosingIssue = false;

        /// <summary>
        /// ウィンドウを表示
        /// </summary>
        public static void ShowWindow(MelpomeneTicket ticket)
        {
            var window = GetWindow<MelpomeneTicketDetailWindow>();
            window.titleContent = new GUIContent($"Ticket #{ticket.issueNumber}");
            window.ticket = ticket;
            window.minSize = new Vector2(400, 600);
            window.InitializeEditFields();
            window.InitializeAndFetchComments();
            window.Show();
        }

        /// <summary>
        /// 編集フィールドを初期化
        /// </summary>
        private void InitializeEditFields()
        {
            if (ticket == null) return;

            editTitle = ticket.title;
            editDescription = ticket.description ?? "";
            editWorldPosition = ticket.worldPosition;

            originalTitle = ticket.title;
            originalDescription = ticket.description ?? "";
            originalWorldPosition = ticket.worldPosition;
            originalTargetObjectPath = ticket.targetObjectPath;

            isDirty = false;
        }

        /// <summary>
        /// DirtyFlagを更新
        /// </summary>
        private void UpdateDirtyFlag()
        {
            isDirty = editTitle != originalTitle ||
                      editDescription != originalDescription ||
                      editWorldPosition != originalWorldPosition ||
                      ticket.targetObjectPath != originalTargetObjectPath;
        }

        /// <summary>
        /// 初期化とコメント取得
        /// </summary>
        private void InitializeAndFetchComments()
        {
            var config = MelpomeneConfig.GetOrCreateConfig();
            gitHubClient = new MelpomeneGitHubClient(config);
            FetchComments();
        }

        /// <summary>
        /// コメントを取得
        /// </summary>
        private async void FetchComments()
        {
            if (ticket == null || ticket.issueNumber <= 0 || gitHubClient == null)
                return;

            isLoadingComments = true;
            Repaint();

            comments = await gitHubClient.GetCommentsAsync(ticket.issueNumber);

            isLoadingComments = false;
            Repaint();
        }

        /// <summary>
        /// コメントを投稿
        /// </summary>
        private async void PostComment()
        {
            if (string.IsNullOrWhiteSpace(newCommentText) || ticket == null || gitHubClient == null)
                return;

            isPostingComment = true;
            Repaint();

            bool success = await gitHubClient.PostCommentAsync(ticket.issueNumber, newCommentText);

            if (success)
            {
                newCommentText = "";
                // コメントリストを更新
                FetchComments();
            }

            isPostingComment = false;
            Repaint();
        }

        /// <summary>
        /// Issueを更新
        /// </summary>
        private async void UpdateIssue()
        {
            if (ticket == null || gitHubClient == null)
                return;

            // 編集内容をチケットに反映
            ticket.title = editTitle;
            ticket.description = editDescription;
            ticket.worldPosition = editWorldPosition;

            isUpdating = true;
            Repaint();

            bool success = await gitHubClient.UpdateIssueAsync(ticket);

            if (success)
            {
                // 元の値を更新
                originalTitle = editTitle;
                originalDescription = editDescription;
                originalWorldPosition = editWorldPosition;
                originalTargetObjectPath = ticket.targetObjectPath;
                isDirty = false;

                // キャッシュを更新
                await MelpomeneManager.Instance.RefreshCacheAsync();

                EditorUtility.DisplayDialog("Melpomene", "Issue updated successfully!", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Melpomene", "Failed to update issue. Check console for details.", "OK");
            }

            isUpdating = false;
            Repaint();
        }

        private void OnGUI()
        {
            if (ticket == null)
            {
                EditorGUILayout.HelpBox("No ticket selected.", MessageType.Info);
                return;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            // ヘッダー
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"#{ticket.issueNumber}", EditorStyles.boldLabel, GUILayout.Width(60));

            // 状態バッジ
            Color oldColor = GUI.backgroundColor;
            GUI.backgroundColor = ticket.state == "open" ? Color.green : Color.red;
            GUILayout.Label(ticket.state.ToUpper(), "box", GUILayout.Width(60));
            GUI.backgroundColor = oldColor;

            GUILayout.FlexibleSpace();

            // Dirtyインジケーター
            if (isDirty)
            {
                GUI.backgroundColor = Color.yellow;
                GUILayout.Label("*", "box", GUILayout.Width(20));
                GUI.backgroundColor = oldColor;
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // タイトル（編集可能）
            EditorGUILayout.LabelField("Title", EditorStyles.boldLabel);
            string newTitle = EditorGUILayout.TextField(editTitle);
            if (newTitle != editTitle)
            {
                editTitle = newTitle;
                UpdateDirtyFlag();
            }

            EditorGUILayout.Space();

            // 説明（編集可能）
            EditorGUILayout.LabelField("Description", EditorStyles.boldLabel);
            string newDescription = EditorGUILayout.TextArea(editDescription, GUILayout.MinHeight(60));
            if (newDescription != editDescription)
            {
                editDescription = newDescription;
                UpdateDirtyFlag();
            }

            EditorGUILayout.Space();

            // メタデータ（折りたたみ可能）
            metadataFoldout = EditorGUILayout.Foldout(metadataFoldout, "Metadata", true, EditorStyles.foldoutHeader);

            if (metadataFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("User", ticket.userName);
                EditorGUILayout.LabelField("Scene", ticket.sceneName);

                // Target Object - 編集可能
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Object", string.IsNullOrEmpty(ticket.targetObjectPath) ? "(none)" : ticket.targetObjectPath);
                if (GUILayout.Button(isEditingTarget ? "Cancel" : "Edit", GUILayout.Width(60)))
                {
                    isEditingTarget = !isEditingTarget;
                    if (isEditingTarget)
                    {
                        // 現在のパスからオブジェクトを探す
                        newTargetObject = !string.IsNullOrEmpty(ticket.targetObjectPath)
                            ? GameObject.Find(ticket.targetObjectPath)
                            : null;
                    }
                }
                EditorGUILayout.EndHorizontal();

                // Target Object編集UI
                if (isEditingTarget)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.BeginVertical("box");

                    newTargetObject = (GameObject)EditorGUILayout.ObjectField(
                        "New Target",
                        newTargetObject,
                        typeof(GameObject),
                        true
                    );

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Apply"))
                    {
                        ApplyNewTargetObject();
                        isEditingTarget = false;
                        UpdateDirtyFlag();
                    }
                    if (GUILayout.Button("Clear Target"))
                    {
                        ClearTargetObject();
                        isEditingTarget = false;
                        UpdateDirtyFlag();
                    }
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.EndVertical();
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.LabelField("Priority", ticket.priority.ToString());
                EditorGUILayout.LabelField("Category", ticket.category.ToString());
                EditorGUILayout.LabelField("Created", ticket.timestamp);

                // World Position（編集可能）
                Vector3 newWorldPosition = EditorGUILayout.Vector3Field("World Position", editWorldPosition);
                if (newWorldPosition != editWorldPosition)
                {
                    editWorldPosition = newWorldPosition;
                    UpdateDirtyFlag();
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(10);

            // Issueを更新ボタン
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUI.BeginDisabledGroup(!isDirty || isUpdating);
            if (GUILayout.Button(isUpdating ? "Updating..." : "Update Issue", GUILayout.Width(150), GUILayout.Height(30)))
            {
                UpdateIssue();
            }
            EditorGUI.EndDisabledGroup();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // アクションボタン
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Open in Browser", GUILayout.Height(25)))
            {
                if (!string.IsNullOrEmpty(ticket.issueUrl))
                {
                    Application.OpenURL(ticket.issueUrl);
                }
            }

            if (GUILayout.Button("Copy URL", GUILayout.Height(25)))
            {
                GUIUtility.systemCopyBuffer = ticket.issueUrl;
                Debug.Log($"[Melpomene] URL copied: {ticket.issueUrl}");
            }

            if (GUILayout.Button("Go to Position", GUILayout.Height(25)))
            {
                // シーンビューをチケットの位置にフォーカス
                SceneView.lastActiveSceneView?.LookAt(editWorldPosition);
            }

            EditorGUILayout.EndHorizontal();

            // 対象オブジェクトへのフォーカス（オブジェクトがない場合は非活性化）
            bool hasTargetObject = !string.IsNullOrEmpty(ticket.targetObjectPath);
            EditorGUI.BeginDisabledGroup(!hasTargetObject);
            if (GUILayout.Button("Select Target Object", GUILayout.Height(25)))
            {
                var obj = GameObject.Find(ticket.targetObjectPath);
                if (obj != null)
                {
                    Selection.activeGameObject = obj;
                    SceneView.lastActiveSceneView?.FrameSelected();
                }
                else
                {
                    EditorUtility.DisplayDialog("Melpomene", $"Object not found: {ticket.targetObjectPath}", "OK");
                }
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(10);

            // クローズボタン（openかつ自分が作成者の場合のみ表示）
            if (ticket.state == "open" && IsOwnTicket(ticket))
            {
                EditorGUI.BeginDisabledGroup(isClosingIssue);
                Color oldBtnColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
                if (GUILayout.Button(isClosingIssue ? "Closing..." : "Close Issue", GUILayout.Height(30)))
                {
                    CloseIssue();
                }
                GUI.backgroundColor = oldBtnColor;
                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.Space(20);

            // コメントセクション
            DrawCommentsSection();

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// コメントセクションを描画
        /// </summary>
        private void DrawCommentsSection()
        {
            EditorGUILayout.BeginHorizontal();
            commentsFoldout = EditorGUILayout.Foldout(commentsFoldout, $"Comments ({comments.Count})", true, EditorStyles.foldoutHeader);

            // 更新ボタン
            if (GUILayout.Button("↻", GUILayout.Width(25), GUILayout.Height(18)))
            {
                FetchComments();
            }
            EditorGUILayout.EndHorizontal();

            if (!commentsFoldout) return;

            EditorGUI.indentLevel++;

            // ローディング表示
            if (isLoadingComments)
            {
                EditorGUILayout.HelpBox("Loading comments...", MessageType.Info);
            }
            else if (comments.Count == 0)
            {
                EditorGUILayout.HelpBox("No comments yet.", MessageType.Info);
            }
            else
            {
                // コメント一覧
                commentsScrollPosition = EditorGUILayout.BeginScrollView(
                    commentsScrollPosition,
                    GUILayout.MaxHeight(200)
                );

                foreach (var comment in comments)
                {
                    DrawComment(comment);
                }

                EditorGUILayout.EndScrollView();
            }

            EditorGUILayout.Space(10);

            // 新規コメント入力
            EditorGUILayout.LabelField("New Comment", EditorStyles.boldLabel);
            newCommentText = EditorGUILayout.TextArea(newCommentText, GUILayout.MinHeight(60));

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            EditorGUI.BeginDisabledGroup(isPostingComment || string.IsNullOrWhiteSpace(newCommentText));
            if (GUILayout.Button(isPostingComment ? "Posting..." : "Post Comment", GUILayout.Width(120), GUILayout.Height(25)))
            {
                PostComment();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
        }

        /// <summary>
        /// 個別のコメントを描画
        /// </summary>
        private void DrawComment(MelpomeneComment comment)
        {
            EditorGUILayout.BeginVertical("box");

            // ヘッダー（ユーザー名と日時）
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"@{comment.userName}", EditorStyles.boldLabel, GUILayout.Width(150));
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(comment.FormattedCreatedAt, EditorStyles.miniLabel, GUILayout.Width(120));
            EditorGUILayout.EndHorizontal();

            // 本文
            EditorGUILayout.LabelField(comment.body, EditorStyles.wordWrappedLabel);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        /// <summary>
        /// 新しいTarget Objectを適用
        /// </summary>
        private void ApplyNewTargetObject()
        {
            if (newTargetObject != null)
            {
                ticket.targetObjectPath = GetHierarchyPath(newTargetObject);
                editWorldPosition = newTargetObject.transform.position;
                Debug.Log($"[Melpomene] Target object updated: {ticket.targetObjectPath}");
            }
            Repaint();
        }

        /// <summary>
        /// Target Objectをクリア
        /// </summary>
        private void ClearTargetObject()
        {
            ticket.targetObjectPath = "";
            newTargetObject = null;
            Debug.Log("[Melpomene] Target object cleared");
            Repaint();
        }

        /// <summary>
        /// 自分が作成したチケットかどうかを判定
        /// NOTE: ticket.userNameとconfig.defaultUserNameを比較
        /// </summary>
        private bool IsOwnTicket(MelpomeneTicket ticket)
        {
            if (ticket == null) return false;
            var config = MelpomeneConfig.GetOrCreateConfig();
            return !string.IsNullOrEmpty(config.defaultUserName) &&
                   ticket.userName == config.defaultUserName;
        }

        /// <summary>
        /// Issueをクローズ
        /// NOTE: 確認ダイアログを表示してからクローズ
        /// </summary>
        private async void CloseIssue()
        {
            if (ticket == null) return;

            // 確認ダイアログ
            bool confirmed = EditorUtility.DisplayDialog(
                "チケットをクローズ",
                $"チケット #{ticket.issueNumber} をクローズしますか？\n\n「{ticket.title}」",
                "クローズする",
                "キャンセル"
            );

            if (!confirmed) return;

            isClosingIssue = true;
            Repaint();

            bool success = await MelpomeneManager.Instance.CloseTicketAsync(ticket.issueNumber);

            if (success)
            {
                ticket.state = "closed";
                EditorUtility.DisplayDialog("Melpomene", $"チケット #{ticket.issueNumber} をクローズしました。", "OK");

                // キャッシュを更新
                await MelpomeneManager.Instance.RefreshCacheAsync();
            }
            else
            {
                EditorUtility.DisplayDialog("Melpomene", "チケットのクローズに失敗しました。コンソールを確認してください。", "OK");
            }

            isClosingIssue = false;
            Repaint();
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
    }
}
#endif
