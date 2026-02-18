using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using ClaudeCodeBridge;
using Melpomene;

/// <summary>
/// Musa - AIオーケストレーション統合EditorWindow
/// NOTE: テルプシコラー（Command Server/Bridge/Watcher）とメルポメネー（チケット/通知/マイルストーン）を統合
/// NOTE: 上部にメインタブ、左側にサブタブを配置
/// </summary>
public class MusaWindow : EditorWindow
{
    // NOTE: メインタブ
    private int mainTab;
    private readonly string[] mainTabNames = { "Terpsichore", "Melpomene", "Global Settings" };

    // NOTE: サブタブ
    private int terpsichoreSubTab;
    private readonly string[] terpsichoreSubTabNames = { "Server", "Bridge", "Watcher", "設定", "環境チェック" };
    private int melpomeneSubTab;
    private readonly string[] melpomeneSubTabNames = { "チケット", "通知", "マイルストーン", "Eureka", "設定" };

    // NOTE: 左サイドバー幅
    private const float SidebarWidth = 100f;

    // =====================================================================
    // Terpsichore: Server タブ用
    // =====================================================================
    private Vector2 errorScrollPosition;
    private bool showErrors = true;
    private bool showWarnings;

    // =====================================================================
    // Terpsichore: Bridge タブ用
    // =====================================================================
    private ClaudeCodeBridgeClient bridgeClient;
    private bool isBridgeConnected;
    private string bridgeHost = "localhost";
    private int bridgePort = 3456;
    private List<GitHubIssue> issues = new List<GitHubIssue>();
    private Vector2 issueScrollPosition;
    private int selectedIssueIndex = -1;
    private GitHubIssue selectedIssueDetail;
    private List<TaskInfo> tasks = new List<TaskInfo>();
    private Vector2 taskScrollPosition;
    private string currentTaskId;
    private string customCommand = "";
    private int bridgeSubTab;
    private readonly string[] bridgeSubTabNames = { "Issues", "Tasks", "Execute" };
    private bool isBridgeLoading;
    private string bridgeStatusMessage = "";

    // =====================================================================
    // Terpsichore: Watcher タブ用
    // =====================================================================
    private Vector2 watcherLogScrollPosition;
    private bool watcherAutoScroll = true;
    private List<UnityCommandServer.WatcherLogEntry> cachedWatcherLogs = new List<UnityCommandServer.WatcherLogEntry>();

    // =====================================================================
    // Terpsichore: Config タブ用
    // =====================================================================
    private int configPort = 8686;
    private bool configEnabled = true;
    private string configToken = "";
    private int configRoleIndex;
    private readonly string[] roleOptions = { "worker", "watcher", "debugger" };

    // =====================================================================
    // Melpomene: 埋め込み用インスタンス
    // =====================================================================
    private MelpomeneWindow melpomeneTicketWindow;
    private MelpomeneNotificationWindow melpomeneNotificationWindow;
    private MelpomeneMilestoneWindow melpomeneMilestoneWindow;
    private MelpomeneSettingsWindow melpomeneSettingsWindow;

    // =====================================================================
    // Settings: 環境チェック用
    // =====================================================================
    private bool envGhAvailable;
    private string envGhVersion;
    private bool envGitAvailable;
    private string envGitVersion;
    private bool envNodeAvailable;
    private string envNodeVersion;
    private bool envChecked;

    // =====================================================================
    // 共通
    // =====================================================================
    private double nextRefreshTime;
    private const double RefreshInterval = 2.0;

    // NOTE: GUIStyleキャッシュ
    private GUIStyle headerStyle;
    private GUIStyle statusRunningStyle;
    private GUIStyle statusStoppedStyle;
    private GUIStyle logStyleNormal;
    private GUIStyle logStyleError;
    private GUIStyle errorItemStyle;
    private GUIStyle sidebarButtonStyle;
    private GUIStyle sidebarActiveButtonStyle;
    private bool stylesInitialized;

    [MenuItem("Musa/Musa %#m")]
    public static void ShowWindow()
    {
        var window = GetWindow<MusaWindow>("Musa");
        window.minSize = new Vector2(600, 450);
    }

    // NOTE: F1でMelpomene/チケットタブに切り替え
    [MenuItem("Musa/Melpomene チケット _F1")]
    public static void ShowMelpomeneTickets()
    {
        var window = GetWindow<MusaWindow>("Musa");
        window.mainTab = 1;
        window.melpomeneSubTab = 0;
        window.Focus();
    }

    // NOTE: F2でMelpomene/通知タブに切り替え
    [MenuItem("Musa/Melpomene 通知 _F2")]
    public static void ShowMelpomeneNotifications()
    {
        var window = GetWindow<MusaWindow>("Musa");
        window.mainTab = 1;
        window.melpomeneSubTab = 1;
        window.Focus();
    }

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
        bridgeClient = new ClaudeCodeBridgeClient(bridgeHost, bridgePort);
        LoadConfigFromFile();

        // NOTE: Melpomene埋め込みウィンドウの初期化
        InitMelpomeneWindows();
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        CleanupMelpomeneWindows();
    }

    private void InitMelpomeneWindows()
    {
        // NOTE: 非表示のEditorWindowインスタンスを作成して描画を委譲
        melpomeneTicketWindow = CreateInstance<MelpomeneWindow>();
        melpomeneTicketWindow.InitializeForMusa();

        melpomeneNotificationWindow = CreateInstance<MelpomeneNotificationWindow>();
        melpomeneNotificationWindow.InitializeForMusa();

        melpomeneMilestoneWindow = CreateInstance<MelpomeneMilestoneWindow>();
        melpomeneMilestoneWindow.InitializeForMusa();

        melpomeneSettingsWindow = CreateInstance<MelpomeneSettingsWindow>();
        melpomeneSettingsWindow.InitializeForMusa();
    }

    private void CleanupMelpomeneWindows()
    {
        if (melpomeneTicketWindow != null) DestroyImmediate(melpomeneTicketWindow);
        if (melpomeneNotificationWindow != null)
        {
            melpomeneNotificationWindow.CleanupForMusa();
            DestroyImmediate(melpomeneNotificationWindow);
        }
        if (melpomeneMilestoneWindow != null) DestroyImmediate(melpomeneMilestoneWindow);
        if (_eurekaWindowInstance != null)
        {
            _eurekaCleanupForMusa?.Invoke(_eurekaWindowInstance, null);
            DestroyImmediate(_eurekaWindowInstance);
            _eurekaWindowInstance = null;
        }
        if (melpomeneSettingsWindow != null) DestroyImmediate(melpomeneSettingsWindow);
    }

    private void OnEditorUpdate()
    {
        if (EditorApplication.timeSinceStartup > nextRefreshTime)
        {
            nextRefreshTime = EditorApplication.timeSinceStartup + RefreshInterval;
            cachedWatcherLogs = UnityCommandServer.GetWatcherLogs();
            Repaint();
        }
    }

    private void InitStyles()
    {
        if (stylesInitialized) return;

        headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 13,
            padding = new RectOffset(4, 4, 4, 4)
        };

        statusRunningStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
        statusRunningStyle.normal.textColor = new Color(0.3f, 0.9f, 0.3f);

        statusStoppedStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
        statusStoppedStyle.normal.textColor = new Color(0.9f, 0.4f, 0.4f);

        logStyleNormal = new GUIStyle(EditorStyles.label)
        {
            wordWrap = true, richText = true, fontSize = 11,
            padding = new RectOffset(4, 4, 1, 1)
        };
        logStyleNormal.normal.textColor = EditorGUIUtility.isProSkin
            ? new Color(0.78f, 0.78f, 0.78f) : new Color(0.2f, 0.2f, 0.2f);

        logStyleError = new GUIStyle(logStyleNormal);
        logStyleError.normal.textColor = new Color(1f, 0.4f, 0.4f);

        errorItemStyle = new GUIStyle(EditorStyles.label)
        {
            richText = true, wordWrap = true, fontSize = 11,
            padding = new RectOffset(8, 4, 2, 2)
        };

        sidebarButtonStyle = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(10, 4, 6, 6),
            fixedHeight = 30
        };

        sidebarActiveButtonStyle = new GUIStyle(sidebarButtonStyle)
        {
            fontStyle = FontStyle.Bold
        };
        sidebarActiveButtonStyle.normal.textColor = new Color(0.3f, 0.8f, 1f);

        stylesInitialized = true;
    }

    private void OnGUI()
    {
        InitStyles();

        // NOTE: メインタブ（上部）
        mainTab = GUILayout.Toolbar(mainTab, mainTabNames, GUILayout.Height(30));
        EditorGUILayout.Space(2);

        // NOTE: サイドバー + コンテンツ
        EditorGUILayout.BeginHorizontal();

        // NOTE: 左サイドバー
        EditorGUILayout.BeginVertical(GUILayout.Width(SidebarWidth));
        DrawSidebar();
        EditorGUILayout.EndVertical();

        // NOTE: セパレーター
        var separatorRect = EditorGUILayout.GetControlRect(false, GUILayout.Width(1));
        EditorGUI.DrawRect(separatorRect, new Color(0.3f, 0.3f, 0.3f));

        // NOTE: メインコンテンツ
        EditorGUILayout.BeginVertical();
        DrawMainContent();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    private void DrawSidebar()
    {
        if (mainTab == 0)
        {
            // Terpsichore サブタブ
            for (int i = 0; i < terpsichoreSubTabNames.Length; i++)
            {
                var style = i == terpsichoreSubTab ? sidebarActiveButtonStyle : sidebarButtonStyle;
                if (GUILayout.Button(terpsichoreSubTabNames[i], style))
                {
                    terpsichoreSubTab = i;
                }
            }
        }
        else if (mainTab == 1)
        {
            // Melpomene サブタブ
            for (int i = 0; i < melpomeneSubTabNames.Length; i++)
            {
                var style = i == melpomeneSubTab ? sidebarActiveButtonStyle : sidebarButtonStyle;
                if (GUILayout.Button(melpomeneSubTabNames[i], style))
                {
                    melpomeneSubTab = i;
                }
            }
        }
        // NOTE: 設定タブはサブタブなし

        GUILayout.FlexibleSpace();
    }

    private void DrawMainContent()
    {
        if (mainTab == 0)
        {
            switch (terpsichoreSubTab)
            {
                case 0: DrawServerTab(); break;
                case 1: DrawBridgeTab(); break;
                case 2: DrawWatcherTab(); break;
                case 3: DrawConfigTab(); break;
                case 4: DrawEnvironmentCheckTab(); break;
            }
        }
        else if (mainTab == 1)
        {
            switch (melpomeneSubTab)
            {
                case 0: DrawMelpomeneTicketTab(); break;
                case 1: DrawMelpomeneNotificationTab(); break;
                case 2: DrawMelpomeneMilestoneTab(); break;
                case 3: DrawMelpomeneEurekaTab(); break;
                case 4: DrawMelpomeneSettingsTab(); break;
            }
        }
        else if (mainTab == 2)
        {
            DrawGlobalSettingsTab();
        }
    }

    // =====================================================================
    // Terpsichore: Server Tab
    // =====================================================================
    #region Server Tab

    private void DrawServerTab()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Command Server", headerStyle);
        EditorGUILayout.Space(2);

        DrawStatusRow("Status", UnityCommandServer.IsRunning ? "Running" : "Stopped",
            UnityCommandServer.IsRunning ? statusRunningStyle : statusStoppedStyle);
        DrawStatusRow("Port", UnityCommandServer.ServerPort.ToString());
        DrawStatusRow("Role", UnityCommandServer.ServerRole);
        DrawStatusRow("Auth", UnityCommandServer.IsAuthEnabled ? "Enabled" : "Disabled");
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(4);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Runtime", headerStyle);
        EditorGUILayout.Space(2);
        DrawStatusRow("PlayMode", UnityCommandServer.IsPlaying ? "Playing" : "Stopped",
            UnityCommandServer.IsPlaying ? statusRunningStyle : null);
        DrawStatusRow("Build", UnityCommandServer.IsBuildInProgress ? "Building..." : UnityCommandServer.LastBuildResult);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(4);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Compilation", headerStyle);
        EditorGUILayout.Space(2);

        if (UnityCommandServer.IsCompiling)
        {
            EditorGUILayout.HelpBox("コンパイル中...", MessageType.Info);
        }
        else
        {
            var errors = UnityCommandServer.GetCompileErrors();
            var warnings = UnityCommandServer.GetCompileWarnings();

            if (errors.Length > 0 || warnings.Length > 0)
            {
                var messageType = errors.Length > 0 ? MessageType.Error : MessageType.Warning;
                EditorGUILayout.HelpBox($"エラー: {errors.Length}件, 警告: {warnings.Length}件", messageType);

                showErrors = EditorGUILayout.Foldout(showErrors, $"Errors ({errors.Length})");
                if (showErrors && errors.Length > 0)
                {
                    errorScrollPosition = EditorGUILayout.BeginScrollView(errorScrollPosition, GUILayout.MaxHeight(200));
                    foreach (var error in errors)
                    {
                        EditorGUILayout.BeginHorizontal();
                        string shortFile = Path.GetFileName(error.file);
                        if (GUILayout.Button($"{shortFile}:{error.line}", EditorStyles.linkLabel, GUILayout.ExpandWidth(false)))
                            OpenFileAtLine(error.file, error.line);
                        EditorGUILayout.LabelField(error.message, errorItemStyle);
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.EndScrollView();
                }

                showWarnings = EditorGUILayout.Foldout(showWarnings, $"Warnings ({warnings.Length})");
                if (showWarnings && warnings.Length > 0)
                {
                    foreach (var warning in warnings)
                    {
                        EditorGUILayout.BeginHorizontal();
                        string shortFile = Path.GetFileName(warning.file);
                        if (GUILayout.Button($"{shortFile}:{warning.line}", EditorStyles.linkLabel, GUILayout.ExpandWidth(false)))
                            OpenFileAtLine(warning.file, warning.line);
                        EditorGUILayout.LabelField(warning.message, errorItemStyle);
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("コンパイル成功", MessageType.None);
            }
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Recompile", GUILayout.Height(28)))
            UnityCommandServer.TriggerRecompile();
        if (GUILayout.Button(UnityCommandServer.IsPlaying ? "Stop" : "Play", GUILayout.Height(28)))
            EditorApplication.isPlaying = !EditorApplication.isPlaying;
        if (GUILayout.Button("Restart Server", GUILayout.Height(28)))
            UnityCommandServer.ReloadAndRestart();
        EditorGUILayout.EndHorizontal();
    }

    #endregion

    // =====================================================================
    // Terpsichore: Bridge Tab
    // =====================================================================
    #region Bridge Tab

    private void DrawBridgeTab()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        var connStyle = isBridgeConnected ? statusRunningStyle : statusStoppedStyle;
        GUILayout.Label(isBridgeConnected ? "● Connected" : "● Disconnected", connStyle, GUILayout.Width(110));
        GUILayout.Label("Host:", GUILayout.Width(35));
        bridgeHost = EditorGUILayout.TextField(bridgeHost, GUILayout.Width(100));
        GUILayout.Label("Port:", GUILayout.Width(30));
        bridgePort = EditorGUILayout.IntField(bridgePort, GUILayout.Width(50));
        if (GUILayout.Button("Connect", EditorStyles.toolbarButton, GUILayout.Width(60)))
        {
            bridgeClient = new ClaudeCodeBridgeClient(bridgeHost, bridgePort);
            CheckBridgeConnection();
        }
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
            RefreshBridgeData();
        EditorGUILayout.EndHorizontal();

        bridgeSubTab = GUILayout.Toolbar(bridgeSubTab, bridgeSubTabNames);
        EditorGUILayout.Space(4);

        switch (bridgeSubTab)
        {
            case 0: DrawBridgeIssuesPanel(); break;
            case 1: DrawBridgeTasksPanel(); break;
            case 2: DrawBridgeExecutePanel(); break;
        }

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label(isBridgeLoading ? "Loading..." : (string.IsNullOrEmpty(bridgeStatusMessage) ? "Ready" : bridgeStatusMessage));
        EditorGUILayout.EndHorizontal();
    }

    private void DrawBridgeIssuesPanel()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.BeginVertical(GUILayout.Width(200));
        EditorGUILayout.LabelField("Open Issues", EditorStyles.boldLabel);
        issueScrollPosition = EditorGUILayout.BeginScrollView(issueScrollPosition);
        for (int i = 0; i < issues.Count; i++)
        {
            var issue = issues[i];
            var style = i == selectedIssueIndex ? EditorStyles.selectionRect : EditorStyles.label;
            if (GUILayout.Button($"#{issue.number}: {TruncateString(issue.title, 25)}", style))
            {
                selectedIssueIndex = i;
                LoadIssueDetail(issue.number);
            }
        }
        EditorGUILayout.EndScrollView();
        if (GUILayout.Button("Refresh Issues")) LoadIssues();
        EditorGUILayout.EndVertical();

        EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField("Issue Details", EditorStyles.boldLabel);
        if (selectedIssueDetail != null)
        {
            EditorGUILayout.LabelField($"#{selectedIssueDetail.number}: {selectedIssueDetail.title}");
            EditorGUILayout.LabelField($"State: {selectedIssueDetail.state}");
            if (selectedIssueDetail.labels != null && selectedIssueDetail.labels.Count > 0)
                EditorGUILayout.LabelField($"Labels: {string.Join(", ", selectedIssueDetail.labels.ConvertAll(l => l.name))}");
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Body:", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(selectedIssueDetail.body ?? "(No description)", GUILayout.Height(150));
            EditorGUILayout.Space();
            GUI.enabled = !isBridgeLoading && isBridgeConnected;
            if (GUILayout.Button("Process Issue & Create PR", GUILayout.Height(30)))
                ProcessSelectedIssue();
            GUI.enabled = true;
        }
        else
        {
            EditorGUILayout.HelpBox("Select an issue to view details", MessageType.Info);
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.EndHorizontal();
    }

    private void DrawBridgeTasksPanel()
    {
        EditorGUILayout.LabelField("Task History", EditorStyles.boldLabel);
        taskScrollPosition = EditorGUILayout.BeginScrollView(taskScrollPosition);
        foreach (var task in tasks)
        {
            EditorGUILayout.BeginHorizontal("box");
            var statusColor = task.status switch
            {
                "running" => Color.yellow, "completed" => Color.green,
                "failed" => Color.red, "cancelled" => Color.gray, _ => Color.white
            };
            var style = new GUIStyle(EditorStyles.label) { normal = { textColor = statusColor } };
            EditorGUILayout.LabelField($"[{task.status.ToUpper()}]", style, GUILayout.Width(100));
            EditorGUILayout.LabelField(TruncateString(task.command, 40));
            if (task.status == "running" && GUILayout.Button("Cancel", GUILayout.Width(60)))
                CancelTask(task.id);
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
        if (GUILayout.Button("Refresh Tasks")) LoadTasks();
    }

    private void DrawBridgeExecutePanel()
    {
        EditorGUILayout.LabelField("Execute Claude Code Command", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Enter a Claude Code command to execute.\nExamples:\n  /impl PlayerMove\n  /review\n  Fix the bug in PlayerController.cs",
            MessageType.Info);
        EditorGUILayout.Space();
        customCommand = EditorGUILayout.TextArea(customCommand, GUILayout.Height(100));
        EditorGUILayout.Space();
        GUI.enabled = !isBridgeLoading && isBridgeConnected && !string.IsNullOrWhiteSpace(customCommand);
        if (GUILayout.Button("Execute", GUILayout.Height(30))) ExecuteCustomCommand();
        GUI.enabled = true;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Quick Commands", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("/review")) { customCommand = "/review"; ExecuteCustomCommand(); }
        if (GUILayout.Button("/impl")) { customCommand = "/impl "; }
        if (GUILayout.Button("/spec_update")) { customCommand = "/spec_update "; }
        EditorGUILayout.EndHorizontal();
    }

    #region Bridge API Methods

    private async void CheckBridgeConnection()
    {
        isBridgeLoading = true;
        bridgeStatusMessage = "Checking connection...";
        Repaint();
        try
        {
            try
            {
                isBridgeConnected = await bridgeClient.CheckHealthAsync();
                bridgeStatusMessage = isBridgeConnected ? "Connected to server" : "Server not available";
                if (isBridgeConnected) { LoadIssues(); LoadTasks(); }
            }
            catch (Exception e) { isBridgeConnected = false; bridgeStatusMessage = $"Connection failed: {e.Message}"; }
        }
        finally
        {
            isBridgeLoading = false;
            Repaint();
        }
    }

    private void RefreshBridgeData()
    {
        if (isBridgeConnected) { LoadIssues(); LoadTasks(); }
        else CheckBridgeConnection();
    }

    private async void LoadIssues()
    {
        if (!isBridgeConnected) return;
        isBridgeLoading = true; bridgeStatusMessage = "Loading issues..."; Repaint();
        try { issues = await bridgeClient.ListIssuesAsync(); bridgeStatusMessage = $"Loaded {issues.Count} issues"; }
        catch (Exception e) { bridgeStatusMessage = $"Failed to load issues: {e.Message}"; }
        isBridgeLoading = false; Repaint();
    }

    private async void LoadIssueDetail(int issueNumber)
    {
        if (!isBridgeConnected) return;
        isBridgeLoading = true; bridgeStatusMessage = $"Loading issue #{issueNumber}..."; Repaint();
        try { selectedIssueDetail = await bridgeClient.GetIssueAsync(issueNumber); bridgeStatusMessage = $"Loaded issue #{issueNumber}"; }
        catch (Exception e) { bridgeStatusMessage = $"Failed to load issue: {e.Message}"; }
        isBridgeLoading = false; Repaint();
    }

    private async void LoadTasks()
    {
        if (!isBridgeConnected) return;
        isBridgeLoading = true; Repaint();
        try { tasks = await bridgeClient.ListTasksAsync(); }
        catch (Exception e) { Debug.LogWarning($"Failed to load tasks: {e.Message}"); }
        isBridgeLoading = false; Repaint();
    }

    private async void ProcessSelectedIssue()
    {
        if (selectedIssueDetail == null) return;
        isBridgeLoading = true; bridgeStatusMessage = $"Processing issue #{selectedIssueDetail.number}..."; Repaint();
        try { currentTaskId = await bridgeClient.ProcessIssueAsync(selectedIssueDetail.number); bridgeStatusMessage = $"Started task: {currentTaskId}"; bridgeSubTab = 1; LoadTasks(); }
        catch (Exception e) { bridgeStatusMessage = $"Failed to process issue: {e.Message}"; }
        isBridgeLoading = false; Repaint();
    }

    private async void ExecuteCustomCommand()
    {
        if (string.IsNullOrWhiteSpace(customCommand)) return;
        isBridgeLoading = true; bridgeStatusMessage = "Executing command..."; Repaint();
        try { currentTaskId = await bridgeClient.ExecuteCommandAsync(customCommand); bridgeStatusMessage = $"Started task: {currentTaskId}"; bridgeSubTab = 1; LoadTasks(); }
        catch (Exception e) { bridgeStatusMessage = $"Failed to execute: {e.Message}"; }
        isBridgeLoading = false; Repaint();
    }

    private async void CancelTask(string taskId)
    {
        try { await bridgeClient.CancelTaskAsync(taskId); bridgeStatusMessage = "Task cancelled"; LoadTasks(); }
        catch (Exception e) { bridgeStatusMessage = $"Failed to cancel: {e.Message}"; }
        Repaint();
    }

    #endregion

    #endregion

    // =====================================================================
    // Terpsichore: Watcher Tab
    // =====================================================================
    #region Watcher Tab

    private void DrawWatcherTab()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("PR Watcher", headerStyle);
        EditorGUILayout.Space(2);

        bool isWatcherRunning = UnityCommandServer.IsWatcherRunning;
        DrawStatusRow("Status", isWatcherRunning ? "Running" : "Stopped",
            isWatcherRunning ? statusRunningStyle : statusStoppedStyle);
        DrawStatusRow("Role", UnityCommandServer.ServerRole);
        DrawStatusRow("Active PRs", UnityCommandServer.WatcherActivePRCount.ToString());
        var lastPoll = UnityCommandServer.LastPollTime;
        DrawStatusRow("Last Poll", lastPoll == DateTime.MinValue ? "---" : lastPoll.ToString("HH:mm:ss"));
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        GUI.enabled = !isWatcherRunning;
        if (GUILayout.Button("Start Watcher", GUILayout.Height(28))) UnityCommandServer.ManualStartWatcher();
        GUI.enabled = isWatcherRunning;
        if (GUILayout.Button("Stop Watcher", GUILayout.Height(28))) UnityCommandServer.ManualStopWatcher();
        GUI.enabled = true;
        if (GUILayout.Button("Clear Log", GUILayout.Height(28), GUILayout.Width(80)))
        { UnityCommandServer.ClearWatcherLogs(); cachedWatcherLogs.Clear(); }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandHeight(true));
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField($"Log ({cachedWatcherLogs.Count})", headerStyle);
        GUILayout.FlexibleSpace();
        watcherAutoScroll = GUILayout.Toggle(watcherAutoScroll, "Auto Scroll", GUILayout.Width(90));
        EditorGUILayout.EndHorizontal();

        watcherLogScrollPosition = EditorGUILayout.BeginScrollView(watcherLogScrollPosition, GUILayout.ExpandHeight(true));
        for (int i = 0; i < cachedWatcherLogs.Count; i++)
        {
            var entry = cachedWatcherLogs[i];
            string timeStr = entry.timestamp.ToString("HH:mm:ss");
            string text = $"<color=#888888>[{timeStr}]</color> {EscapeRichText(entry.message)}";
            EditorGUILayout.LabelField(text, entry.isError ? logStyleError : logStyleNormal);
        }
        if (watcherAutoScroll && cachedWatcherLogs.Count > 0) watcherLogScrollPosition.y = float.MaxValue;
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    #endregion

    // =====================================================================
    // Terpsichore: Config Tab
    // =====================================================================
    #region Config Tab

    private void DrawConfigTab()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Command Server 設定", headerStyle);
        EditorGUILayout.Space(4);

        string configPath = UnityCommandServer.GetConfigFilePath();
        bool configExists = File.Exists(configPath);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Config File:", GUILayout.Width(80));
        EditorGUILayout.LabelField(configExists ? "Found" : "Not Found",
            configExists ? statusRunningStyle : statusStoppedStyle);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);
        configPort = EditorGUILayout.IntField("Port", configPort);
        configEnabled = EditorGUILayout.Toggle("Enabled", configEnabled);
        configToken = EditorGUILayout.TextField("Token", configToken);
        configRoleIndex = EditorGUILayout.Popup("Role", configRoleIndex, roleOptions);
        EditorGUILayout.Space(8);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Save", GUILayout.Height(28)))
        {
            UnityCommandServer.SaveConfig(configPort, configEnabled, configToken, roleOptions[configRoleIndex]);
            EditorUtility.DisplayDialog("Musa", "設定を保存しました。\nRestart Serverで反映されます。", "OK");
        }
        if (GUILayout.Button("Reload", GUILayout.Height(28))) LoadConfigFromFile();
        if (GUILayout.Button("Restart Server", GUILayout.Height(28))) UnityCommandServer.ReloadAndRestart();
        EditorGUILayout.EndHorizontal();

        if (!configExists)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.HelpBox("設定ファイルが見つかりません。\nSaveボタンでデフォルト設定を作成できます。", MessageType.Warning);
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(8);

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("現在の稼働状態", headerStyle);
        EditorGUILayout.Space(2);
        DrawStatusRow("Server", UnityCommandServer.IsRunning ? "Running" : "Stopped",
            UnityCommandServer.IsRunning ? statusRunningStyle : statusStoppedStyle);
        DrawStatusRow("Port", UnityCommandServer.ServerPort.ToString());
        DrawStatusRow("Role", UnityCommandServer.ServerRole);
        EditorGUILayout.EndVertical();
    }

    private void LoadConfigFromFile()
    {
        string configPath = UnityCommandServer.GetConfigFilePath();
        if (!File.Exists(configPath)) return;
        try
        {
            string json = File.ReadAllText(configPath);
            var config = JsonUtility.FromJson<ConfigData>(json);
            configPort = config.port;
            configEnabled = config.enabled;
            configToken = config.token ?? "";
            string role = string.IsNullOrEmpty(config.role) ? "worker" : config.role.ToLower();
            configRoleIndex = Array.IndexOf(roleOptions, role);
            if (configRoleIndex < 0) configRoleIndex = 0;
        }
        catch (Exception e) { Debug.LogWarning($"[Musa] Failed to load config: {e.Message}"); }
    }

    [Serializable]
    private class ConfigData
    {
        public int port = 8686;
        public bool enabled = true;
        public string token = "";
        public string role = "worker";
    }

    #endregion

    // =====================================================================
    // Melpomene タブ描画（委譲）
    // =====================================================================
    #region Melpomene Tabs

    private void DrawMelpomeneTicketTab()
    {
        if (melpomeneTicketWindow != null)
            melpomeneTicketWindow.DrawContent();
        else
            EditorGUILayout.HelpBox("MelpomeneWindow is not initialized", MessageType.Warning);
    }

    private void DrawMelpomeneNotificationTab()
    {
        if (melpomeneNotificationWindow != null)
            melpomeneNotificationWindow.DrawContent();
        else
            EditorGUILayout.HelpBox("MelpomeneNotificationWindow is not initialized", MessageType.Warning);
    }

    private void DrawMelpomeneMilestoneTab()
    {
        if (melpomeneMilestoneWindow != null)
            melpomeneMilestoneWindow.DrawContent();
        else
            EditorGUILayout.HelpBox("MelpomeneMilestoneWindow is not initialized", MessageType.Warning);
    }

    // NOTE: EurekaWindowはasmdef外のため、リフレクションで開く
    private static System.Type _eurekaWindowType;
    private static System.Reflection.MethodInfo _eurekaDrawContent;
    private static System.Reflection.MethodInfo _eurekaInitForMusa;
    private static System.Reflection.MethodInfo _eurekaCleanupForMusa;
    private EditorWindow _eurekaWindowInstance;

    private void EnsureEurekaWindow()
    {
        if (_eurekaWindowType == null)
        {
            _eurekaWindowType = System.Type.GetType("EurekaWindow, Assembly-CSharp-Editor");
            if (_eurekaWindowType != null)
            {
                _eurekaDrawContent = _eurekaWindowType.GetMethod("DrawContent");
                _eurekaInitForMusa = _eurekaWindowType.GetMethod("InitializeForMusa");
                _eurekaCleanupForMusa = _eurekaWindowType.GetMethod("CleanupForMusa");
            }
        }

        if (_eurekaWindowType != null && _eurekaWindowInstance == null)
        {
            _eurekaWindowInstance = CreateInstance(_eurekaWindowType) as EditorWindow;
            _eurekaInitForMusa?.Invoke(_eurekaWindowInstance, null);
        }
    }

    private void DrawMelpomeneEurekaTab()
    {
        EnsureEurekaWindow();

        if (_eurekaWindowInstance != null && _eurekaDrawContent != null)
        {
            _eurekaDrawContent.Invoke(_eurekaWindowInstance, null);
        }
        else
        {
            EditorGUILayout.HelpBox("EurekaWindow が見つかりません", MessageType.Warning);
            if (GUILayout.Button("Eureka Windowを開く", GUILayout.Height(30)))
            {
                EditorApplication.ExecuteMenuItem("Tools/Eureka Window");
            }
        }
    }

    private void DrawMelpomeneSettingsTab()
    {
        if (melpomeneSettingsWindow != null)
            melpomeneSettingsWindow.DrawContent();
        else
            EditorGUILayout.HelpBox("MelpomeneSettingsWindow is not initialized", MessageType.Warning);
    }

    #endregion

    // =====================================================================
    // Settings タブ（GlobalSettings）
    // =====================================================================
    #region Settings Tab

    private void DrawGlobalSettingsTab()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        MusaGlobalSettings.DrawGUI();
        EditorGUILayout.EndVertical();
    }

    private void DrawEnvironmentCheckTab()
    {
        // NOTE: 初回表示時に自動チェック
        if (!envChecked)
        {
            RunEnvironmentCheck();
            envChecked = true;
        }

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("環境チェック", headerStyle);
        EditorGUILayout.Space(4);

        // GitHub CLI
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(10);
        if (envGhAvailable)
            EditorGUILayout.LabelField($"GitHub CLI (gh): \u2713 {envGhVersion}");
        else
            EditorGUILayout.LabelField("GitHub CLI (gh): \u2717 見つかりません",
                new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.8f, 0.5f, 0f) } });
        EditorGUILayout.EndHorizontal();

        // Git
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(10);
        if (envGitAvailable)
            EditorGUILayout.LabelField($"Git: \u2713 {envGitVersion}");
        else
            EditorGUILayout.LabelField("Git: \u2717 見つかりません",
                new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.8f, 0.5f, 0f) } });
        EditorGUILayout.EndHorizontal();

        // Node.js
        EditorGUILayout.BeginHorizontal();
        GUILayout.Space(10);
        if (envNodeAvailable)
            EditorGUILayout.LabelField($"Node.js: \u2713 {envNodeVersion}");
        else
            EditorGUILayout.LabelField("Node.js: \u2717 見つかりません",
                new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.8f, 0.5f, 0f) } });
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);

        if (GUILayout.Button("チェック実行", GUILayout.Height(28), GUILayout.Width(120)))
        {
            RunEnvironmentCheck();
        }

        EditorGUILayout.EndVertical();
    }

    private void RunEnvironmentCheck()
    {
        (envGhAvailable, envGhVersion) = CheckCommandAvailability("gh", "--version");
        (envGitAvailable, envGitVersion) = CheckCommandAvailability("git", "--version");
        (envNodeAvailable, envNodeVersion) = CheckCommandAvailability("node", "--version");
    }

    private static (bool available, string version) CheckCommandAvailability(string command, string args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(command, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var process = System.Diagnostics.Process.Start(psi))
            {
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                var firstLine = output.Split('\n')[0].Trim();
                return (process.ExitCode == 0, firstLine);
            }
        }
        catch
        {
            return (false, null);
        }
    }

    #endregion

    // =====================================================================
    // 共通ユーティリティ
    // =====================================================================
    #region Utility

    private void DrawStatusRow(string label, string value, GUIStyle valueStyle = null)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label + ":", GUILayout.Width(80));
        EditorGUILayout.LabelField(value, valueStyle ?? EditorStyles.label);
        EditorGUILayout.EndHorizontal();
    }

    private static void OpenFileAtLine(string filePath, int line)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        int assetsIndex = filePath.IndexOf("Assets", StringComparison.OrdinalIgnoreCase);
        if (assetsIndex >= 0)
        {
            string relativePath = filePath.Substring(assetsIndex);
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(relativePath);
            if (asset != null) { AssetDatabase.OpenAsset(asset, line); return; }
        }
        UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(filePath, line);
    }

    private static string TruncateString(string str, int maxLength)
    {
        if (string.IsNullOrEmpty(str)) return "";
        return str.Length <= maxLength ? str : str.Substring(0, maxLength) + "...";
    }

    private static string EscapeRichText(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Replace("<", "<\u200B").Replace(">", "\u200B>");
    }

    #endregion
}
