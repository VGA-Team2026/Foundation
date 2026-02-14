using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace ClaudeCodeBridge
{
    /// <summary>
    /// Claude Code Bridge エディタウィンドウ
    /// GitHub Issueの確認と処理、Claude Codeコマンドの実行が可能
    /// </summary>
    public class ClaudeCodeBridgeWindow : EditorWindow
    {
        private ClaudeCodeBridgeClient client;
        private bool isConnected = false;
        private string serverHost = "localhost";
        private int serverPort = 3456;

        // Issue関連
        private List<GitHubIssue> issues = new List<GitHubIssue>();
        private Vector2 issueScrollPosition;
        private int selectedIssueIndex = -1;
        private GitHubIssue selectedIssueDetail;

        // タスク関連
        private List<TaskInfo> tasks = new List<TaskInfo>();
        private Vector2 taskScrollPosition;
        private string currentTaskId;

        // コマンド実行
        private string customCommand = "";

        // UI状態
        private int selectedTab = 0;
        private readonly string[] tabNames = { "Issues", "Tasks", "Execute" };
        private bool isLoading = false;
        private string statusMessage = "";

        public static void ShowWindow()
        {
            var window = GetWindow<ClaudeCodeBridgeWindow>("Claude Code Bridge");
            window.minSize = new Vector2(400, 300);
        }

        private void OnEnable()
        {
            client = new ClaudeCodeBridgeClient(serverHost, serverPort);
            CheckConnection();
        }

        private void OnGUI()
        {
            DrawHeader();
            DrawTabs();

            switch (selectedTab)
            {
                case 0:
                    DrawIssuesTab();
                    break;
                case 1:
                    DrawTasksTab();
                    break;
                case 2:
                    DrawExecuteTab();
                    break;
            }

            DrawStatusBar();
        }

        #region Header

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // 接続状態
            var statusStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = isConnected ? Color.green : Color.red }
            };
            GUILayout.Label(isConnected ? "● Connected" : "● Disconnected", statusStyle, GUILayout.Width(100));

            // サーバー設定
            GUILayout.Label("Host:", GUILayout.Width(35));
            serverHost = EditorGUILayout.TextField(serverHost, GUILayout.Width(100));
            GUILayout.Label("Port:", GUILayout.Width(30));
            serverPort = EditorGUILayout.IntField(serverPort, GUILayout.Width(50));

            if (GUILayout.Button("Connect", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                client = new ClaudeCodeBridgeClient(serverHost, serverPort);
                CheckConnection();
            }

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60)))
            {
                RefreshData();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawTabs()
        {
            selectedTab = GUILayout.Toolbar(selectedTab, tabNames);
            EditorGUILayout.Space(5);
        }

        #endregion

        #region Issues Tab

        private void DrawIssuesTab()
        {
            EditorGUILayout.BeginHorizontal();

            // Issue一覧
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

            if (GUILayout.Button("Refresh Issues"))
            {
                LoadIssues();
            }

            EditorGUILayout.EndVertical();

            // Issue詳細
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField("Issue Details", EditorStyles.boldLabel);

            if (selectedIssueDetail != null)
            {
                EditorGUILayout.LabelField($"#{selectedIssueDetail.number}: {selectedIssueDetail.title}");
                EditorGUILayout.LabelField($"State: {selectedIssueDetail.state}");

                if (selectedIssueDetail.labels != null && selectedIssueDetail.labels.Count > 0)
                {
                    var labelNames = string.Join(", ", selectedIssueDetail.labels.ConvertAll(l => l.name));
                    EditorGUILayout.LabelField($"Labels: {labelNames}");
                }

                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Body:", EditorStyles.boldLabel);
                EditorGUILayout.TextArea(selectedIssueDetail.body ?? "(No description)", GUILayout.Height(150));

                EditorGUILayout.Space();

                GUI.enabled = !isLoading && isConnected;
                if (GUILayout.Button("Process Issue & Create PR", GUILayout.Height(30)))
                {
                    ProcessSelectedIssue();
                }
                GUI.enabled = true;
            }
            else
            {
                EditorGUILayout.HelpBox("Select an issue to view details", MessageType.Info);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Tasks Tab

        private void DrawTasksTab()
        {
            EditorGUILayout.LabelField("Task History", EditorStyles.boldLabel);

            taskScrollPosition = EditorGUILayout.BeginScrollView(taskScrollPosition);

            foreach (var task in tasks)
            {
                EditorGUILayout.BeginHorizontal("box");

                var statusColor = task.status switch
                {
                    "running" => Color.yellow,
                    "completed" => Color.green,
                    "failed" => Color.red,
                    "cancelled" => Color.gray,
                    _ => Color.white
                };

                var style = new GUIStyle(EditorStyles.label) { normal = { textColor = statusColor } };
                EditorGUILayout.LabelField($"[{task.status.ToUpper()}]", style, GUILayout.Width(100));
                EditorGUILayout.LabelField(TruncateString(task.command, 40));

                if (task.status == "running")
                {
                    if (GUILayout.Button("Cancel", GUILayout.Width(60)))
                    {
                        CancelTask(task.id);
                    }
                }
                else
                {
                    if (GUILayout.Button("View", GUILayout.Width(60)))
                    {
                        ShowTaskOutput(task);
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Refresh Tasks"))
            {
                LoadTasks();
            }
        }

        #endregion

        #region Execute Tab

        private void DrawExecuteTab()
        {
            EditorGUILayout.LabelField("Execute Claude Code Command", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Enter a Claude Code command to execute.\nExamples:\n" +
                "  /impl PlayerMove\n" +
                "  /review\n" +
                "  Fix the bug in PlayerController.cs",
                MessageType.Info);

            EditorGUILayout.Space();

            customCommand = EditorGUILayout.TextArea(customCommand, GUILayout.Height(100));

            EditorGUILayout.Space();

            GUI.enabled = !isLoading && isConnected && !string.IsNullOrWhiteSpace(customCommand);
            if (GUILayout.Button("Execute", GUILayout.Height(30)))
            {
                ExecuteCustomCommand();
            }
            GUI.enabled = true;

            // クイックコマンド
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Quick Commands", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("/review"))
            {
                customCommand = "/review";
                ExecuteCustomCommand();
            }
            if (GUILayout.Button("/impl"))
            {
                customCommand = "/impl ";
            }
            if (GUILayout.Button("/spec_update"))
            {
                customCommand = "/spec_update ";
            }
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Status Bar

        private void DrawStatusBar()
        {
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (isLoading)
            {
                GUILayout.Label("Loading...");
            }
            else if (!string.IsNullOrEmpty(statusMessage))
            {
                GUILayout.Label(statusMessage);
            }
            else
            {
                GUILayout.Label("Ready");
            }

            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region API Methods

        private async void CheckConnection()
        {
            isLoading = true;
            statusMessage = "Checking connection...";
            Repaint();

            try
            {
                isConnected = await client.CheckHealthAsync();
                statusMessage = isConnected ? "Connected to server" : "Server not available";

                if (isConnected)
                {
                    LoadIssues();
                    LoadTasks();
                }
            }
            catch (Exception e)
            {
                isConnected = false;
                statusMessage = $"Connection failed: {e.Message}";
            }

            isLoading = false;
            Repaint();
        }

        private void RefreshData()
        {
            if (isConnected)
            {
                LoadIssues();
                LoadTasks();
            }
            else
            {
                CheckConnection();
            }
        }

        private async void LoadIssues()
        {
            if (!isConnected) return;

            isLoading = true;
            statusMessage = "Loading issues...";
            Repaint();

            try
            {
                issues = await client.ListIssuesAsync();
                statusMessage = $"Loaded {issues.Count} issues";
            }
            catch (Exception e)
            {
                statusMessage = $"Failed to load issues: {e.Message}";
            }

            isLoading = false;
            Repaint();
        }

        private async void LoadIssueDetail(int issueNumber)
        {
            if (!isConnected) return;

            isLoading = true;
            statusMessage = $"Loading issue #{issueNumber}...";
            Repaint();

            try
            {
                selectedIssueDetail = await client.GetIssueAsync(issueNumber);
                statusMessage = $"Loaded issue #{issueNumber}";
            }
            catch (Exception e)
            {
                statusMessage = $"Failed to load issue: {e.Message}";
            }

            isLoading = false;
            Repaint();
        }

        private async void LoadTasks()
        {
            if (!isConnected) return;

            isLoading = true;
            Repaint();

            try
            {
                tasks = await client.ListTasksAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to load tasks: {e.Message}");
            }

            isLoading = false;
            Repaint();
        }

        private async void ProcessSelectedIssue()
        {
            if (selectedIssueDetail == null) return;

            isLoading = true;
            statusMessage = $"Processing issue #{selectedIssueDetail.number}...";
            Repaint();

            try
            {
                currentTaskId = await client.ProcessIssueAsync(selectedIssueDetail.number);
                statusMessage = $"Started task: {currentTaskId}";
                selectedTab = 1; // Switch to Tasks tab
                LoadTasks();
            }
            catch (Exception e)
            {
                statusMessage = $"Failed to process issue: {e.Message}";
            }

            isLoading = false;
            Repaint();
        }

        private async void ExecuteCustomCommand()
        {
            if (string.IsNullOrWhiteSpace(customCommand)) return;

            isLoading = true;
            statusMessage = "Executing command...";
            Repaint();

            try
            {
                currentTaskId = await client.ExecuteCommandAsync(customCommand);
                statusMessage = $"Started task: {currentTaskId}";
                selectedTab = 1; // Switch to Tasks tab
                LoadTasks();
            }
            catch (Exception e)
            {
                statusMessage = $"Failed to execute: {e.Message}";
            }

            isLoading = false;
            Repaint();
        }

        private async void CancelTask(string taskId)
        {
            try
            {
                await client.CancelTaskAsync(taskId);
                statusMessage = "Task cancelled";
                LoadTasks();
            }
            catch (Exception e)
            {
                statusMessage = $"Failed to cancel: {e.Message}";
            }

            Repaint();
        }

        private void ShowTaskOutput(TaskInfo task)
        {
            var window = GetWindow<TaskOutputWindow>("Task Output");
            window.SetTask(task);
        }

        #endregion

        #region Utility

        private string TruncateString(string str, int maxLength)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Length <= maxLength ? str : str.Substring(0, maxLength) + "...";
        }

        #endregion
    }

    /// <summary>
    /// タスク出力表示ウィンドウ
    /// </summary>
    public class TaskOutputWindow : EditorWindow
    {
        private TaskInfo task;
        private Vector2 scrollPosition;

        public void SetTask(TaskInfo taskInfo)
        {
            task = taskInfo;
            Repaint();
        }

        private void OnGUI()
        {
            if (task == null)
            {
                EditorGUILayout.HelpBox("No task selected", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField($"Task: {task.id}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Status: {task.status}");
            EditorGUILayout.LabelField($"Command: {task.command}");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output:", EditorStyles.boldLabel);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUILayout.TextArea(task.output ?? "(No output)", GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(task.error))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Error:", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(task.error, MessageType.Error);
            }
        }
    }
}
