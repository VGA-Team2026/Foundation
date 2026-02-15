using System;
using Cysharp.Threading.Tasks;
using Melpomene;
using UnityEditor;
using UnityEngine;

/// <summary>
/// GitHub PR/Actions 通知ウィンドウ
/// NOTE: PRとActionsの状態を表示する常駐EditorWindow
/// </summary>
public class MelpomeneNotificationWindow : EditorWindow
{
    /// <summary>スクロール位置</summary>
    private Vector2 scrollPosition;

    /// <summary>PRセクション展開状態</summary>
    private bool showPRs = true;

    /// <summary>Actionsセクション展開状態</summary>
    private bool showActions = true;

    /// <summary>最終更新時刻</summary>
    private DateTime lastUpdateTime;

    /// <summary>ハイライト用背景テクスチャ（キャッシュ）</summary>
    private static Texture2D highlightTexture;

    /// <summary>ハイライト用スタイル（キャッシュ）</summary>
    private static GUIStyle highlightStyle;

    /// <summary>
    /// ウィンドウ表示
    /// </summary>
    public static void ShowWindow()
    {
        var window = GetWindow<MelpomeneNotificationWindow>("GitHub通知");
        window.minSize = new Vector2(350, 400);
    }

    /// <summary>
    /// MusaWindow埋め込み用の初期化
    /// NOTE: OnEnable相当のイベント購読を外部から呼び出す
    /// </summary>
    public void InitializeForMusa()
    {
        var service = MelpomeneNotificationService.Instance;
        if (service != null)
        {
            service.OnDataUpdated += OnDataUpdated;
            service.OnNewReviewDetected += OnNewReview;
            service.OnWorkflowStatusChanged += OnWorkflowChanged;
        }
    }

    /// <summary>
    /// MusaWindow埋め込み用のクリーンアップ
    /// NOTE: OnDisable相当のイベント解除を外部から呼び出す
    /// </summary>
    public void CleanupForMusa()
    {
        var service = MelpomeneNotificationService.Instance;
        if (service != null)
        {
            service.OnDataUpdated -= OnDataUpdated;
            service.OnNewReviewDetected -= OnNewReview;
            service.OnWorkflowStatusChanged -= OnWorkflowChanged;
        }
    }

    /// <summary>
    /// MusaWindow埋め込み用の描画
    /// NOTE: OnGUI相当の処理を外部から呼び出す
    /// </summary>
    public void DrawContent()
    {
        var service = MelpomeneNotificationService.Instance;
        if (service == null)
        {
            EditorGUILayout.HelpBox("MelpomeneNotificationService is not initialized", MessageType.Warning);
            return;
        }

        DrawToolbar(service);

        if (!string.IsNullOrEmpty(service.LastError))
        {
            EditorGUILayout.HelpBox($"Error: {service.LastError}", MessageType.Error);
        }

        if (!service.IsInitialized)
        {
            EditorGUILayout.HelpBox("Initializing... Waiting for GitHub authentication", MessageType.Info);
            return;
        }

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        DrawPRSection(service);
        EditorGUILayout.Space(10);
        DrawActionsSection(service);
        EditorGUILayout.EndScrollView();
    }

    private void OnEnable()
    {
        var service = MelpomeneNotificationService.Instance;
        if (service != null)
        {
            service.OnDataUpdated += OnDataUpdated;
            service.OnNewReviewDetected += OnNewReview;
            service.OnWorkflowStatusChanged += OnWorkflowChanged;
        }
    }

    private void OnDisable()
    {
        var service = MelpomeneNotificationService.Instance;
        if (service != null)
        {
            service.OnDataUpdated -= OnDataUpdated;
            service.OnNewReviewDetected -= OnNewReview;
            service.OnWorkflowStatusChanged -= OnWorkflowChanged;
        }
    }

    private void OnDataUpdated()
    {
        lastUpdateTime = DateTime.Now;
        Repaint();
    }

    private void OnNewReview(GitHubPullRequest pr)
    {
        // 新規レビュー検出時の通知（オプション: EditorUtility.DisplayDialog等）
        Debug.Log($"[Melpomene] 新しいレビューがあります: PR #{pr.number} {pr.title}");
    }

    private void OnWorkflowChanged(GitHubWorkflowRun run)
    {
        // ワークフローステータス変更時の通知
        Debug.Log($"[Melpomene] ワークフロー状態変更: {run.name} - {run.StatusText}");
    }

    private void OnGUI()
    {
        var service = MelpomeneNotificationService.Instance;
        if (service == null)
        {
            EditorGUILayout.HelpBox("MelpomeneNotificationService is not initialized", MessageType.Warning);
            return;
        }

        // ヘッダーツールバー
        DrawToolbar(service);

        // エラー表示
        if (!string.IsNullOrEmpty(service.LastError))
        {
            EditorGUILayout.HelpBox($"Error: {service.LastError}", MessageType.Error);
        }

        // 初期化待ち表示
        if (!service.IsInitialized)
        {
            EditorGUILayout.HelpBox("Initializing... Waiting for GitHub authentication", MessageType.Info);
            return;
        }

        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        // PRセクション
        DrawPRSection(service);

        EditorGUILayout.Space(10);

        // Actionsセクション
        DrawActionsSection(service);

        EditorGUILayout.EndScrollView();
    }

    /// <summary>
    /// ツールバー描画
    /// </summary>
    private void DrawToolbar(MelpomeneNotificationService service)
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        // Refreshボタン
        EditorGUI.BeginDisabledGroup(service.IsPolling);
        if (GUILayout.Button(service.IsPolling ? "Refreshing..." : "Refresh", EditorStyles.toolbarButton, GUILayout.Width(80)))
        {
            service.ForceRefresh();
        }
        EditorGUI.EndDisabledGroup();

        GUILayout.FlexibleSpace();

        // ユーザー名表示
        if (!string.IsNullOrEmpty(service.AuthenticatedUser))
        {
            EditorGUILayout.LabelField($"@{service.AuthenticatedUser}", EditorStyles.miniLabel, GUILayout.Width(100));
        }

        // 最終更新時刻
        if (lastUpdateTime != default)
        {
            EditorGUILayout.LabelField($"Last: {lastUpdateTime:HH:mm:ss}", EditorStyles.miniLabel, GUILayout.Width(80));
        }

        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// PRセクション描画
    /// </summary>
    private void DrawPRSection(MelpomeneNotificationService service)
    {
        var prs = service.PullRequests;
        showPRs = EditorGUILayout.Foldout(showPRs, $"Pull Requests ({prs.Count})", true, EditorStyles.foldoutHeader);

        if (!showPRs) return;

        EditorGUI.indentLevel++;

        if (prs.Count == 0)
        {
            EditorGUILayout.LabelField("No open pull requests", EditorStyles.miniLabel);
        }
        else
        {
            foreach (var pr in prs)
            {
                DrawPullRequest(pr);
            }
        }

        EditorGUI.indentLevel--;
    }

    /// <summary>
    /// 個別PR描画
    /// </summary>
    private void DrawPullRequest(GitHubPullRequest pr)
    {
        // 新規レビューがある場合はハイライト背景
        GUIStyle bgStyle;
        if (pr.hasNewReview)
        {
            if (highlightStyle == null)
            {
                highlightStyle = new GUIStyle(EditorStyles.helpBox);
                highlightTexture = MakeColorTexture(new Color(0.2f, 0.5f, 0.2f, 0.3f));
                highlightStyle.normal.background = highlightTexture;
            }
            bgStyle = highlightStyle;
        }
        else
        {
            bgStyle = EditorStyles.helpBox;
        }

        EditorGUILayout.BeginVertical(bgStyle);

        // タイトル行
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"#{pr.number}", EditorStyles.boldLabel, GUILayout.Width(50));

        // ドラフトバッジ
        if (pr.draft)
        {
            GUILayout.Label("DRAFT", EditorStyles.miniLabel, GUILayout.Width(45));
        }

        EditorGUILayout.LabelField(pr.title, EditorStyles.label);
        EditorGUILayout.EndHorizontal();

        // ブランチ情報
        EditorGUILayout.LabelField($"{pr.head_ref} -> {pr.base_ref}", EditorStyles.miniLabel);

        // レビュー状態
        EditorGUILayout.BeginHorizontal();
        if (pr.hasNewReview)
        {
            var starStyle = new GUIStyle(EditorStyles.boldLabel);
            starStyle.normal.textColor = Color.yellow;
            EditorGUILayout.LabelField("* New Review!", starStyle, GUILayout.Width(100));
        }
        EditorGUILayout.LabelField($"Reviews: {pr.reviewCount}", EditorStyles.miniLabel, GUILayout.Width(80));

        // マージ可能状態
        string mergeableText = pr.mergeable_state switch
        {
            "clean" => "Ready to merge",
            "dirty" => "Conflicts",
            "blocked" => "Blocked",
            "unknown" => "Unknown",
            _ => pr.mergeable_state ?? "Unknown"
        };
        Color mergeableColor = pr.mergeable_state switch
        {
            "clean" => Color.green,
            "dirty" => Color.red,
            "blocked" => Color.yellow,
            _ => Color.gray
        };
        var mergeableStyle = new GUIStyle(EditorStyles.miniLabel);
        mergeableStyle.normal.textColor = mergeableColor;
        EditorGUILayout.LabelField(mergeableText, mergeableStyle, GUILayout.Width(100));

        EditorGUILayout.EndHorizontal();

        // ボタン行
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Open in Browser", GUILayout.Width(120)))
        {
            Application.OpenURL(pr.html_url);
        }

        // マージボタン（マージ可能な場合のみ有効）
        EditorGUI.BeginDisabledGroup(!pr.mergeable || pr.mergeable_state != "clean");
        if (GUILayout.Button("Merge", GUILayout.Width(60)))
        {
            MergePRAsync(pr).Forget();
        }
        EditorGUI.EndDisabledGroup();

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(5);
    }

    /// <summary>
    /// Actionsセクション描画
    /// </summary>
    private void DrawActionsSection(MelpomeneNotificationService service)
    {
        var runs = service.WorkflowRuns;
        showActions = EditorGUILayout.Foldout(showActions, $"Actions - UnityBuild ({runs.Count})", true, EditorStyles.foldoutHeader);

        if (!showActions) return;

        EditorGUI.indentLevel++;

        if (runs.Count == 0)
        {
            EditorGUILayout.LabelField("No recent workflow runs", EditorStyles.miniLabel);
        }
        else
        {
            foreach (var run in runs)
            {
                DrawWorkflowRun(run);
            }
        }

        EditorGUI.indentLevel--;
    }

    /// <summary>
    /// 個別ワークフロー実行描画
    /// </summary>
    private void DrawWorkflowRun(GitHubWorkflowRun run)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.BeginHorizontal();

        // ステータスアイコン
        Color statusColor = run.IsSuccess ? Color.green :
                           run.IsFailed ? Color.red :
                           run.IsRunning ? Color.yellow :
                           run.IsQueued ? Color.cyan : Color.gray;

        var iconStyle = new GUIStyle(EditorStyles.label);
        iconStyle.normal.textColor = statusColor;
        EditorGUILayout.LabelField("*", iconStyle, GUILayout.Width(15));

        // ブランチ名
        EditorGUILayout.LabelField(run.head_branch, EditorStyles.boldLabel, GUILayout.Width(150));

        // ステータス
        var statusStyle = new GUIStyle(EditorStyles.label);
        statusStyle.normal.textColor = statusColor;
        EditorGUILayout.LabelField(run.StatusText, statusStyle, GUILayout.Width(80));

        // 実行番号
        EditorGUILayout.LabelField($"#{run.run_number}", EditorStyles.miniLabel, GUILayout.Width(50));

        EditorGUILayout.EndHorizontal();

        // 更新日時
        EditorGUILayout.LabelField($"Updated: {run.UpdatedAtDateTime:MM/dd HH:mm}", EditorStyles.miniLabel);

        // 詳細ボタン
        if (GUILayout.Button("View Details", GUILayout.Width(100)))
        {
            Application.OpenURL(run.html_url);
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(3);
    }

    /// <summary>
    /// PRマージ実行
    /// </summary>
    private async UniTaskVoid MergePRAsync(GitHubPullRequest pr)
    {
        if (!EditorUtility.DisplayDialog("PRをマージ",
            $"PR #{pr.number} をマージしますか?\n{pr.title}", "マージ", "キャンセル"))
        {
            return;
        }

        var client = MelpomeneManager.Instance?.GitHubClient;
        if (client == null)
        {
            EditorUtility.DisplayDialog("エラー", "GitHubClient is not available", "OK");
            return;
        }

        bool success = await client.MergePullRequestAsync(pr.number);

        if (success)
        {
            EditorUtility.DisplayDialog("成功", "PRがマージされました", "OK");
            MelpomeneNotificationService.Instance?.ForceRefresh();
        }
        else
        {
            EditorUtility.DisplayDialog("エラー", "マージに失敗しました", "OK");
        }
    }

    /// <summary>
    /// 単色テクスチャ生成（ハイライト背景用）
    /// </summary>
    private static Texture2D MakeColorTexture(Color color)
    {
        var texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }
}
