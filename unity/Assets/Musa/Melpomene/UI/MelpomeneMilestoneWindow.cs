#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using Cysharp.Threading.Tasks;

namespace Melpomene
{
    /// <summary>
    /// マイルストーン管理ウィンドウ
    /// NOTE: GitHubマイルストーンの同期・作成・デフォルト設定を行うUI
    /// </summary>
    public class MelpomeneMilestoneWindow : EditorWindow
    {
        private Vector2 scrollPosition;
        private bool isEditing;
        private string editTitle;
        private string editDueDate;
        private string editDescription;

        // GitHubマイルストーン関連
        private List<GitHubMilestone> githubMilestones = new List<GitHubMilestone>();
        private bool isFetching;
        private bool isCreating;

        // GUIStyleキャッシュ
        private GUIStyle cachedBoldLabelStyle;

        /// <summary>
        /// ウィンドウを表示
        /// </summary>
        public static void ShowWindow()
        {
            var window = GetWindow<MelpomeneMilestoneWindow>();
            window.titleContent = new GUIContent("Melpomene - Milestones");
            window.minSize = new Vector2(450, 400);
            window.Show();
        }

        /// <summary>
        /// MusaWindow埋め込み用の初期化
        /// NOTE: OnEnable相当の処理を外部から呼び出す
        /// </summary>
        public void InitializeForMusa()
        {
            FetchMilestonesAsync().Forget();
        }

        /// <summary>
        /// MusaWindow埋め込み用の描画
        /// NOTE: OnGUI相当の処理を外部から呼び出す
        /// </summary>
        public void DrawContent()
        {
            EditorGUILayout.Space();
            DrawHeader();
            EditorGUILayout.Space();
            DrawDefaultMilestoneSection();
            EditorGUILayout.Space();
            if (isEditing)
            {
                DrawEditForm();
                EditorGUILayout.Space();
            }
            DrawMilestoneList();
        }

        private void OnEnable()
        {
            // ウィンドウが開かれたときにGitHubから取得
            FetchMilestonesAsync().Forget();
        }

        private void OnFocus()
        {
            // ウィンドウがフォーカスされたときに再取得（他のウィンドウから戻ってきたとき用）
            if (githubMilestones.Count == 0 && !isFetching)
            {
                FetchMilestonesAsync().Forget();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();

            // ヘッダー
            DrawHeader();

            EditorGUILayout.Space();

            // デフォルトマイルストーン設定
            DrawDefaultMilestoneSection();

            EditorGUILayout.Space();

            // 編集/新規作成フォーム
            if (isEditing)
            {
                DrawEditForm();
                EditorGUILayout.Space();
            }

            // マイルストーン一覧
            DrawMilestoneList();
        }

        /// <summary>
        /// ヘッダー描画
        /// </summary>
        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("GitHubマイルストーン管理", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            EditorGUI.BeginDisabledGroup(isFetching || isCreating);
            if (GUILayout.Button("同期", GUILayout.Width(60)))
            {
                SyncFromGitHubAsync().Forget();
            }
            if (GUILayout.Button("新規作成", GUILayout.Width(80)))
            {
                StartNewMilestone();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            if (isFetching)
            {
                EditorGUILayout.HelpBox("GitHubからマイルストーンを取得中...", MessageType.Info);
            }
        }

        /// <summary>
        /// デフォルトマイルストーン設定セクション
        /// </summary>
        private void DrawDefaultMilestoneSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("デフォルトマイルストーン", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("※ 新規チケット作成時に自動選択されます", EditorStyles.miniLabel);

            var defaultNumber = MelpomeneMilestoneManager.GetDefaultMilestoneNumber();
            var defaultMilestone = githubMilestones.Find(m => m.number == defaultNumber);

            EditorGUILayout.BeginHorizontal();

            if (defaultMilestone != null)
            {
                EditorGUILayout.LabelField($"現在: {defaultMilestone.DisplayText}");
                if (GUILayout.Button("解除", GUILayout.Width(60)))
                {
                    MelpomeneMilestoneManager.SetDefaultMilestoneNumber(0);
                }
            }
            else if (defaultNumber > 0)
            {
                EditorGUILayout.LabelField($"現在: (マイルストーン #{defaultNumber} は存在しません)");
                if (GUILayout.Button("解除", GUILayout.Width(60)))
                {
                    MelpomeneMilestoneManager.SetDefaultMilestoneNumber(0);
                }
            }
            else
            {
                EditorGUILayout.LabelField("現在: (未設定)");
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 編集フォーム（新規作成のみ、GitHub側への反映）
        /// </summary>
        private void DrawEditForm()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField("GitHubに新規マイルストーンを作成", EditorStyles.boldLabel);

            editTitle = EditorGUILayout.TextField("タイトル", editTitle);

            EditorGUILayout.BeginHorizontal();
            editDueDate = EditorGUILayout.TextField("期限 (yyyy-MM-dd)", editDueDate);
            if (GUILayout.Button("今日", GUILayout.Width(50)))
            {
                editDueDate = DateTime.Today.ToString("yyyy-MM-dd");
            }
            if (GUILayout.Button("+7日", GUILayout.Width(50)))
            {
                editDueDate = DateTime.Today.AddDays(7).ToString("yyyy-MM-dd");
            }
            if (GUILayout.Button("+30日", GUILayout.Width(50)))
            {
                editDueDate = DateTime.Today.AddDays(30).ToString("yyyy-MM-dd");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("説明");
            editDescription = EditorGUILayout.TextArea(editDescription, GUILayout.Height(50));

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("キャンセル", GUILayout.Width(100)))
            {
                CancelEdit();
            }

            var isValid = !string.IsNullOrWhiteSpace(editTitle);
            EditorGUI.BeginDisabledGroup(!isValid || isCreating);
            if (GUILayout.Button(isCreating ? "作成中..." : "GitHubに作成", GUILayout.Width(120)))
            {
                CreateMilestoneAsync().Forget();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// マイルストーン一覧
        /// </summary>
        private void DrawMilestoneList()
        {
            EditorGUILayout.LabelField("マイルストーン一覧（GitHub）", EditorStyles.boldLabel);

            if (githubMilestones.Count == 0 && !isFetching)
            {
                EditorGUILayout.HelpBox("GitHubにマイルストーンがありません。\n「新規作成」ボタンで追加するか、「同期」ボタンで取得してください。", MessageType.Info);
                return;
            }

            var defaultNumber = MelpomeneMilestoneManager.GetDefaultMilestoneNumber();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            foreach (var milestone in githubMilestones)
            {
                var isDefault = milestone.number == defaultNumber;

                EditorGUILayout.BeginVertical(isDefault ? EditorStyles.helpBox : GUI.skin.box);

                EditorGUILayout.BeginHorizontal();

                // マイルストーン情報（GUIStyleをキャッシュしてパフォーマンス向上）
                GUIStyle labelStyle;
                if (isDefault)
                {
                    if (cachedBoldLabelStyle == null)
                    {
                        cachedBoldLabelStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold };
                    }
                    labelStyle = cachedBoldLabelStyle;
                }
                else
                {
                    labelStyle = EditorStyles.label;
                }

                EditorGUILayout.LabelField($"#{milestone.number}: {milestone.DisplayText}", labelStyle);

                GUILayout.FlexibleSpace();

                // デフォルト設定ボタン
                if (isDefault)
                {
                    EditorGUILayout.LabelField("★デフォルト", GUILayout.Width(80));
                }
                else
                {
                    if (GUILayout.Button("デフォルトに設定", GUILayout.Width(100)))
                    {
                        MelpomeneMilestoneManager.SetDefaultMilestoneNumber(milestone.number);
                    }
                }

                EditorGUILayout.EndHorizontal();

                // 説明（あれば表示）
                if (!string.IsNullOrEmpty(milestone.description))
                {
                    EditorGUILayout.LabelField(milestone.description, EditorStyles.wordWrappedMiniLabel);
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// GitHubからマイルストーンを取得
        /// </summary>
        private async UniTaskVoid FetchMilestonesAsync()
        {
            isFetching = true;
            Repaint();

            try
            {
                githubMilestones = await MelpomeneManager.Instance.GetGitHubMilestonesAsync();
            }
            finally
            {
                isFetching = false;
                Repaint();
            }
        }

        /// <summary>
        /// GitHubから同期（ローカルJSONも更新）
        /// </summary>
        private async UniTaskVoid SyncFromGitHubAsync()
        {
            isFetching = true;
            Repaint();

            try
            {
                await MelpomeneManager.Instance.SyncMilestonesFromGitHubAsync();
                githubMilestones = await MelpomeneManager.Instance.GetGitHubMilestonesAsync();
                EditorUtility.DisplayDialog("同期完了", $"{githubMilestones.Count}件のマイルストーンを同期しました。", "OK");
            }
            finally
            {
                isFetching = false;
                Repaint();
            }
        }

        /// <summary>
        /// GitHubにマイルストーンを作成
        /// </summary>
        private async UniTaskVoid CreateMilestoneAsync()
        {
            isCreating = true;
            Repaint();

            try
            {
                var created = await MelpomeneManager.Instance.CreateGitHubMilestoneAsync(editTitle, editDueDate, editDescription);

                if (created != null)
                {
                    EditorUtility.DisplayDialog("作成完了", $"マイルストーン「{created.title}」を作成しました。", "OK");
                    CancelEdit();

                    // 作成後に同期
                    await MelpomeneManager.Instance.SyncMilestonesFromGitHubAsync();
                    githubMilestones = await MelpomeneManager.Instance.GetGitHubMilestonesAsync();

                    // 最初のマイルストーンならデフォルトに設定
                    if (githubMilestones.Count == 1)
                    {
                        MelpomeneMilestoneManager.SetDefaultMilestoneNumber(created.number);
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog("エラー", "マイルストーンの作成に失敗しました。\nConsoleログを確認してください。", "OK");
                }
            }
            finally
            {
                isCreating = false;
                Repaint();
            }
        }

        /// <summary>
        /// 新規マイルストーン作成開始
        /// </summary>
        private void StartNewMilestone()
        {
            isEditing = true;
            editTitle = "";
            editDueDate = DateTime.Today.AddDays(14).ToString("yyyy-MM-dd");
            editDescription = "";
        }

        /// <summary>
        /// 編集キャンセル
        /// </summary>
        private void CancelEdit()
        {
            isEditing = false;
            editTitle = "";
            editDueDate = "";
            editDescription = "";
        }
    }
}
#endif
