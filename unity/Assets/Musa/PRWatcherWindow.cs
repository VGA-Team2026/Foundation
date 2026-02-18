using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// PR Watcher監視ウィンドウ (#616)
/// NOTE: PR監視デーモンの状態表示、ログ閲覧、手動操作を提供するEditorWindow
/// </summary>
public class PRWatcherWindow : EditorWindow
{
    private Vector2 logScrollPosition;
    private bool autoScroll = true;
    private List<UnityCommandServer.WatcherLogEntry> cachedLogs = new List<UnityCommandServer.WatcherLogEntry>();
    private double nextRefreshTime;
    private const double RefreshInterval = 2.0;

    // NOTE: GUIStyleキャッシュ
    private GUIStyle logStyleNormal;
    private GUIStyle logStyleError;
    private GUIStyle statusLabelStyle;
    private GUIStyle headerStyle;
    private bool stylesInitialized;

    public static void ShowWindow()
    {
        var window = GetWindow<PRWatcherWindow>("PR Watcher");
        window.minSize = new Vector2(400, 300);
    }

    private void OnEnable()
    {
        // NOTE: EditorApplication.updateで定期的にRepaint
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    private void OnEditorUpdate()
    {
        if (EditorApplication.timeSinceStartup > nextRefreshTime)
        {
            nextRefreshTime = EditorApplication.timeSinceStartup + RefreshInterval;
            cachedLogs = UnityCommandServer.GetWatcherLogs();
            Repaint();
        }
    }

    private void InitStyles()
    {
        if (stylesInitialized) return;

        logStyleNormal = new GUIStyle(EditorStyles.label)
        {
            wordWrap = true,
            richText = true,
            fontSize = 11,
            padding = new RectOffset(4, 4, 1, 1)
        };
        logStyleNormal.normal.textColor = EditorGUIUtility.isProSkin
            ? new Color(0.78f, 0.78f, 0.78f)
            : new Color(0.2f, 0.2f, 0.2f);

        logStyleError = new GUIStyle(logStyleNormal);
        logStyleError.normal.textColor = new Color(1f, 0.4f, 0.4f);

        statusLabelStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 13
        };

        headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 12,
            padding = new RectOffset(4, 4, 4, 4)
        };

        stylesInitialized = true;
    }

    private void OnGUI()
    {
        InitStyles();

        DrawStatusPanel();
        EditorGUILayout.Space(4);
        DrawControlPanel();
        EditorGUILayout.Space(4);
        DrawLogPanel();
    }

    /// <summary>
    /// ステータスパネル
    /// </summary>
    private void DrawStatusPanel()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.LabelField("PR Watcher Status", headerStyle);
        EditorGUILayout.Space(2);

        bool isRunning = UnityCommandServer.IsWatcherRunning;
        string role = UnityCommandServer.ServerRole;

        // NOTE: 稼働状態
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Status:", GUILayout.Width(100));
        var prevColor = GUI.color;
        GUI.color = isRunning ? new Color(0.3f, 0.9f, 0.3f) : new Color(0.9f, 0.4f, 0.4f);
        EditorGUILayout.LabelField(isRunning ? "Running" : "Stopped", statusLabelStyle);
        GUI.color = prevColor;
        EditorGUILayout.EndHorizontal();

        // NOTE: Role
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Role:", GUILayout.Width(100));
        EditorGUILayout.LabelField(role);
        EditorGUILayout.EndHorizontal();

        // NOTE: アクティブPR数
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Active PRs:", GUILayout.Width(100));
        EditorGUILayout.LabelField(UnityCommandServer.WatcherActivePRCount.ToString());
        EditorGUILayout.EndHorizontal();

        // NOTE: 最終ポーリング時刻
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Last Poll:", GUILayout.Width(100));
        var lastPoll = UnityCommandServer.LastPollTime;
        string lastPollStr = lastPoll == System.DateTime.MinValue
            ? "---"
            : lastPoll.ToString("HH:mm:ss");
        EditorGUILayout.LabelField(lastPollStr);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// 操作パネル
    /// </summary>
    private void DrawControlPanel()
    {
        EditorGUILayout.BeginHorizontal();

        bool isRunning = UnityCommandServer.IsWatcherRunning;

        GUI.enabled = !isRunning;
        if (GUILayout.Button("Start Watcher", GUILayout.Height(28)))
        {
            UnityCommandServer.ManualStartWatcher();
        }

        GUI.enabled = isRunning;
        if (GUILayout.Button("Stop Watcher", GUILayout.Height(28)))
        {
            UnityCommandServer.ManualStopWatcher();
        }

        GUI.enabled = true;
        if (GUILayout.Button("Clear Log", GUILayout.Height(28), GUILayout.Width(80)))
        {
            UnityCommandServer.ClearWatcherLogs();
            cachedLogs.Clear();
        }

        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// ログパネル
    /// </summary>
    private void DrawLogPanel()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));

        // NOTE: ヘッダ行
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Log ({cachedLogs.Count})", headerStyle);
        GUILayout.FlexibleSpace();
        autoScroll = GUILayout.Toggle(autoScroll, "Auto Scroll", GUILayout.Width(90));
        EditorGUILayout.EndHorizontal();

        // NOTE: ログスクロール領域
        logScrollPosition = EditorGUILayout.BeginScrollView(logScrollPosition, GUILayout.ExpandHeight(true));

        for (int i = 0; i < cachedLogs.Count; i++)
        {
            var entry = cachedLogs[i];
            string timeStr = entry.timestamp.ToString("HH:mm:ss");
            string text = $"<color=#888888>[{timeStr}]</color> {EscapeRichText(entry.message)}";
            var style = entry.isError ? logStyleError : logStyleNormal;
            EditorGUILayout.LabelField(text, style);
        }

        // NOTE: 自動スクロール
        if (autoScroll && cachedLogs.Count > 0)
        {
            // NOTE: スクロール位置を最下部に設定
            logScrollPosition.y = float.MaxValue;
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// RichTextのタグをエスケープ
    /// </summary>
    private static string EscapeRichText(string text)
    {
        // NOTE: ユーザ入力にRichTextタグが含まれる場合の安全対策
        // ただしタイムスタンプのcolorタグは別途付与するため、元テキストのみエスケープ
        return text.Replace("<", "<\u200B").Replace(">", "\u200B>");
    }
}
