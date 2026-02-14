using UnityEngine;
using UnityEditor;
using UnityEditor.Compilation;
using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Debug = UnityEngine.Debug;

/// <summary>
/// Unity外部コマンドサーバ
/// NOTE: HTTPサーバを起動し、外部ツールからUnityの操作を可能にする
/// NOTE: リコンパイル、コンパイルステータス確認、ビルド等のAPIを提供
/// NOTE: プロジェクトルートの unity_command_server.json から設定を読み取る
/// NOTE: JSONが存在しない場合はサーバを起動しない（複数Unity対応）
/// </summary>
[InitializeOnLoad]
public static class UnityCommandServer
{
    private const int DefaultPort = 8686;
    private const string ConfigFileName = "unity_command_server.json";
    private const string ConfigSubDir = "musa/terpsichore";
    private const string EnabledEnvVar = "UNITY_COMMAND_SERVER_ENABLED";

    // NOTE: 認証トークン（設定されている場合のみ認証を要求）
    private static string authToken;

    private static HttpListener httpListener;
    private static Thread listenerThread;
    private static volatile bool isRunning;
    private static int serverPort;
    // NOTE: サーバの役割 (#616)
    private static string serverRole = "worker";
    // NOTE: PR Watcherプロセス (#616)
    private static Process watcherProcess;
    // NOTE: Watcherログバッファ (#616)
    private static readonly List<WatcherLogEntry> watcherLogs = new List<WatcherLogEntry>();
    private const int MaxWatcherLogs = 200;
    private static DateTime lastPollTime = DateTime.MinValue;
    private static int watcherActivePRCount;

    // NOTE: スレッドセーフティ用ロックオブジェクト
    private static readonly object stateLock = new object();

    // NOTE: コンパイルエラー情報を保持
    private static List<CompileError> compileErrors = new List<CompileError>();
    private static List<CompileError> compileWarnings = new List<CompileError>();
    private static bool lastCompileHadErrors = false;

    // NOTE: PlayMode状態をキャッシュ（メインスレッドで更新）
    private static volatile bool cachedIsPlaying = false;

    // NOTE: ビルド情報を保持
    private static bool isBuilding = false;
    private static string lastBuildResult = "None";
    private static DateTime lastBuildTime = DateTime.MinValue;
    private static List<string> buildErrors = new List<string>();

    /// <summary>
    /// サーバ設定JSONのデータクラス
    /// </summary>
    [Serializable]
    private class ServerConfig
    {
        public int port = 8686;
        public bool enabled = true;
        public string token = "";
        // NOTE: サーバの役割（worker/watcher/debugger）(#616)
        public string role = "worker";
    }

    static UnityCommandServer()
    {
        // NOTE: 環境変数でサーバを無効化可能（後方互換性）
        string enabledStr = Environment.GetEnvironmentVariable(EnabledEnvVar);
        if (enabledStr != null && enabledStr.ToLower() == "false")
        {
            Debug.Log("[UnityCommandServer] Disabled by environment variable");
            return;
        }

        // NOTE: プロジェクトルートの設定JSONを読み取る
        var config = LoadConfig();
        if (config == null)
        {
            Debug.Log("[UnityCommandServer] Config file not found. Server will not start. Create unity_command_server.json in project root to enable.");
            return;
        }

        if (!config.enabled)
        {
            Debug.Log("[UnityCommandServer] Disabled by config file");
            return;
        }

        if (config.port < 1 || config.port > 65535)
        {
            Debug.LogError($"[UnityCommandServer] Invalid port in config: {config.port}");
            return;
        }
        serverPort = config.port;

        // NOTE: 認証トークンを設定ファイルから取得
        authToken = string.IsNullOrEmpty(config.token) ? null : config.token;
        if (!string.IsNullOrEmpty(authToken))
        {
            Debug.Log("[UnityCommandServer] Authentication enabled");
        }

        // NOTE: サーバの役割を設定 (#616)
        serverRole = string.IsNullOrEmpty(config.role) ? "worker" : config.role.ToLower();
        Debug.Log($"[UnityCommandServer] Role: {serverRole}");

        // NOTE: コンパイルイベントを購読
        CompilationPipeline.compilationStarted += OnCompilationStarted;
        CompilationPipeline.compilationFinished += OnCompilationFinished;
        CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;

        // NOTE: PlayMode状態をメインスレッドで監視
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        cachedIsPlaying = EditorApplication.isPlaying;

        // NOTE: エディタ終了時にサーバを停止
        EditorApplication.quitting += StopServer;
        AssemblyReloadEvents.beforeAssemblyReload += StopServer;

        StartServer();

        // NOTE: role=watcher の場合、PR Watcherを自動起動 (#616)
        if (serverRole == "watcher")
        {
            StartWatcher();
        }
    }

    /// <summary>
    /// プロジェクトルートから設定JSONを読み取る
    /// NOTE: JSONが存在しない場合はnullを返す
    /// </summary>
    private static ServerConfig LoadConfig()
    {
        // NOTE: Unityプロジェクトの親ディレクトリ（リポジトリルート）を探す
        string unityProjectPath = Path.GetDirectoryName(Application.dataPath);
        string projectRoot = Path.GetDirectoryName(unityProjectPath);

        // NOTE: 新パス musa/terpsichore/ を優先、旧パス(ルート直下)にフォールバック
        string newConfigPath = Path.Combine(projectRoot, ConfigSubDir, ConfigFileName);
        string legacyConfigPath = Path.Combine(projectRoot, ConfigFileName);

        string configPath = null;
        if (File.Exists(newConfigPath))
        {
            configPath = newConfigPath;
        }
        else if (File.Exists(legacyConfigPath))
        {
            // NOTE: 旧パスから新パスに自動移動
            try
            {
                string newDir = Path.Combine(projectRoot, ConfigSubDir);
                if (!Directory.Exists(newDir))
                    Directory.CreateDirectory(newDir);
                File.Move(legacyConfigPath, newConfigPath);
                configPath = newConfigPath;
                Debug.Log($"[UnityCommandServer] Config migrated to {ConfigSubDir}/{ConfigFileName}");
            }
            catch (Exception)
            {
                // NOTE: 移動失敗時は旧パスをそのまま使用
                configPath = legacyConfigPath;
            }
        }

        if (configPath == null)
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(configPath);
            var config = JsonUtility.FromJson<ServerConfig>(json);
            Debug.Log($"[UnityCommandServer] Config loaded: port={config.port}, enabled={config.enabled}");
            return config;
        }
        catch (Exception e)
        {
            Debug.LogError($"[UnityCommandServer] Failed to load config: {e.Message}");
            return null;
        }
    }

    private static void StartServer()
    {
        if (isRunning) return;

        try
        {
            httpListener = new HttpListener();
            httpListener.Prefixes.Add($"http://localhost:{serverPort}/");
            httpListener.Start();

            isRunning = true;
            listenerThread = new Thread(ListenLoop);
            listenerThread.IsBackground = true;
            listenerThread.Start();

            Debug.Log($"[UnityCommandServer] Started on port {serverPort}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[UnityCommandServer] Failed to start: {e.Message}");
        }
    }

    private static void StopServer()
    {
        isRunning = false;

        // NOTE: PR Watcherを停止 (#616)
        StopWatcher();

        if (httpListener != null)
        {
            try
            {
                httpListener.Stop();
                httpListener.Close();
            }
            catch { }
            httpListener = null;
        }

        if (listenerThread != null)
        {
            // NOTE: httpListener.Stop()によりGetContext()がHttpListenerExceptionをスローし、
            // スレッドは自然に終了するため、Abort()は不要
            listenerThread.Join(1000); // スレッド終了を待機
            listenerThread = null;
        }

        UnityEngine.Debug.Log("[UnityCommandServer] Stopped");
    }

    /// <summary>
    /// PR Watcherプロセスを起動 (#616)
    /// NOTE: node scripts/pr-watcher.js をバックグラウンドで実行
    /// </summary>
    private static void StartWatcher()
    {
        try
        {
            string unityProjectPath = Path.GetDirectoryName(Application.dataPath);
            string projectRoot = Path.GetDirectoryName(unityProjectPath);
            string scriptPath = Path.Combine(projectRoot, "scripts", "pr-watcher.js");

            if (!File.Exists(scriptPath))
            {
                UnityEngine.Debug.LogWarning("[UnityCommandServer] PR Watcher script not found: " + scriptPath);
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = $"\"{scriptPath}\"",
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            watcherProcess = Process.Start(startInfo);
            if (watcherProcess != null && !watcherProcess.HasExited)
            {
                // NOTE: 出力をUnityコンソール＋ログバッファにリダイレクト
                watcherProcess.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        UnityEngine.Debug.Log(e.Data);
                        AddWatcherLog(e.Data, false);
                        ParseWatcherOutput(e.Data);
                    }
                };
                watcherProcess.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        UnityEngine.Debug.LogWarning(e.Data);
                        AddWatcherLog(e.Data, true);
                    }
                };
                watcherProcess.BeginOutputReadLine();
                watcherProcess.BeginErrorReadLine();

                UnityEngine.Debug.Log($"[UnityCommandServer] PR Watcher started (PID: {watcherProcess.Id})");
            }
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"[UnityCommandServer] Failed to start PR Watcher: {e.Message}");
        }
    }

    /// <summary>
    /// PR Watcherプロセスを停止 (#616)
    /// </summary>
    private static void StopWatcher()
    {
        if (watcherProcess != null)
        {
            try
            {
                if (!watcherProcess.HasExited)
                {
                    watcherProcess.Kill();
                    watcherProcess.WaitForExit(3000);
                    UnityEngine.Debug.Log("[UnityCommandServer] PR Watcher stopped");
                }
            }
            catch { }
            watcherProcess = null;
        }
    }

    /// <summary>
    /// Watcherログを追加 (#616)
    /// </summary>
    private static void AddWatcherLog(string message, bool isError)
    {
        lock (stateLock)
        {
            watcherLogs.Add(new WatcherLogEntry
            {
                timestamp = DateTime.Now,
                message = message,
                isError = isError
            });
            if (watcherLogs.Count > MaxWatcherLogs)
            {
                watcherLogs.RemoveRange(0, watcherLogs.Count - MaxWatcherLogs);
            }
        }
    }

    /// <summary>
    /// Watcher出力からステータス情報をパース (#616)
    /// </summary>
    private static void ParseWatcherOutput(string line)
    {
        if (line.Contains("ポーリング開始"))
        {
            lastPollTime = DateTime.Now;
        }
        else if (line.Contains("アクティブPR:"))
        {
            // NOTE: "アクティブPR: 3件" のような行からPR数を抽出
            var match = Regex.Match(line, @"アクティブPR:\s*(\d+)件");
            if (match.Success)
            {
                int.TryParse(match.Groups[1].Value, out watcherActivePRCount);
            }
        }
        else if (line.Contains("アクティブPRなし"))
        {
            watcherActivePRCount = 0;
        }
    }

    #region Watcher Public API (#616)

    /// <summary>
    /// Watcherが稼働中か
    /// </summary>
    public static bool IsWatcherRunning
    {
        get { return watcherProcess != null && !watcherProcess.HasExited; }
    }

    /// <summary>
    /// 現在のサーバーRole
    /// </summary>
    public static string ServerRole => serverRole;

    /// <summary>
    /// 最後のポーリング時刻
    /// </summary>
    public static DateTime LastPollTime => lastPollTime;

    /// <summary>
    /// アクティブPR件数
    /// </summary>
    public static int WatcherActivePRCount => watcherActivePRCount;

    /// <summary>
    /// Watcherログを取得
    /// </summary>
    public static List<WatcherLogEntry> GetWatcherLogs()
    {
        lock (stateLock)
        {
            return new List<WatcherLogEntry>(watcherLogs);
        }
    }

    /// <summary>
    /// Watcherを手動起動
    /// </summary>
    public static void ManualStartWatcher()
    {
        if (IsWatcherRunning)
        {
            UnityEngine.Debug.LogWarning("[UnityCommandServer] PR Watcher is already running");
            return;
        }
        StartWatcher();
    }

    /// <summary>
    /// Watcherを手動停止
    /// </summary>
    public static void ManualStopWatcher()
    {
        StopWatcher();
    }

    /// <summary>
    /// Watcherログをクリア
    /// </summary>
    public static void ClearWatcherLogs()
    {
        lock (stateLock)
        {
            watcherLogs.Clear();
        }
    }

    /// <summary>
    /// Watcherログエントリ
    /// </summary>
    public struct WatcherLogEntry
    {
        public DateTime timestamp;
        public string message;
        public bool isError;
    }

    #endregion

    #region Public API for EditorWindow

    /// <summary>
    /// サーバーが稼働中か
    /// </summary>
    public static bool IsRunning => isRunning;

    /// <summary>
    /// サーバーのポート番号
    /// </summary>
    public static int ServerPort => serverPort;

    /// <summary>
    /// 認証が有効か
    /// </summary>
    public static bool IsAuthEnabled => !string.IsNullOrEmpty(authToken);

    /// <summary>
    /// 最後のコンパイルにエラーがあったか
    /// </summary>
    public static bool HasCompileErrors => lastCompileHadErrors;

    /// <summary>
    /// コンパイル中か
    /// </summary>
    public static bool IsCompiling => EditorApplication.isCompiling;

    /// <summary>
    /// PlayMode中か
    /// </summary>
    public static bool IsPlaying => cachedIsPlaying;

    /// <summary>
    /// ビルド中か
    /// </summary>
    public static bool IsBuildInProgress => isBuilding;

    /// <summary>
    /// 最後のビルド結果
    /// </summary>
    public static string LastBuildResult => lastBuildResult;

    /// <summary>
    /// コンパイルエラー一覧を取得
    /// </summary>
    public static CompileError[] GetCompileErrors()
    {
        lock (stateLock)
        {
            return compileErrors.ToArray();
        }
    }

    /// <summary>
    /// コンパイル警告一覧を取得
    /// </summary>
    public static CompileError[] GetCompileWarnings()
    {
        lock (stateLock)
        {
            return compileWarnings.ToArray();
        }
    }

    /// <summary>
    /// 設定ファイルのパスを取得
    /// </summary>
    public static string GetConfigFilePath()
    {
        string unityProjectPath = Path.GetDirectoryName(Application.dataPath);
        string projectRoot = Path.GetDirectoryName(unityProjectPath);
        return Path.Combine(projectRoot, ConfigSubDir, ConfigFileName);
    }

    /// <summary>
    /// 設定をJSONに保存
    /// </summary>
    public static void SaveConfig(int port, bool enabled, string token, string role)
    {
        string configPath = GetConfigFilePath();
        string dir = Path.GetDirectoryName(configPath);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        var config = new ServerConfig
        {
            port = port,
            enabled = enabled,
            token = token,
            role = string.IsNullOrEmpty(role) ? "worker" : role
        };
        string json = JsonUtility.ToJson(config, true);
        File.WriteAllText(configPath, json);
        Debug.Log($"[UnityCommandServer] Config saved to {configPath}");
    }

    /// <summary>
    /// 設定を再読み込みしてサーバーを再起動
    /// NOTE: 次回のドメインリロード時に反映される
    /// </summary>
    public static void ReloadAndRestart()
    {
        StopServer();

        var config = LoadConfig();
        if (config == null || !config.enabled)
        {
            Debug.Log("[UnityCommandServer] Config not found or disabled. Server stopped.");
            return;
        }

        serverPort = config.port;
        authToken = string.IsNullOrEmpty(config.token) ? null : config.token;
        serverRole = string.IsNullOrEmpty(config.role) ? "worker" : config.role.ToLower();

        StartServer();

        if (serverRole == "watcher" && !IsWatcherRunning)
        {
            StartWatcher();
        }
        else if (serverRole != "watcher" && IsWatcherRunning)
        {
            StopWatcher();
        }

        Debug.Log($"[UnityCommandServer] Reloaded. Port={serverPort}, Role={serverRole}");
    }

    /// <summary>
    /// リコンパイルをトリガー（EditorWindow用）
    /// </summary>
    public static void TriggerRecompile()
    {
        lock (stateLock)
        {
            compileErrors.Clear();
            compileWarnings.Clear();
        }
        CompilationPipeline.RequestScriptCompilation();
    }

    #endregion

    private static void ListenLoop()
    {
        while (isRunning && httpListener != null)
        {
            try
            {
                var context = httpListener.GetContext();
                ProcessRequest(context);
            }
            catch (HttpListenerException)
            {
                // NOTE: サーバ停止時の例外は無視
            }
            catch (Exception e)
            {
                if (isRunning)
                {
                    Debug.LogError($"[UnityCommandServer] Error processing request: {e.Message}");
                }
            }
        }
    }

    private static void ProcessRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        // NOTE: CORSヘッダを追加
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, X-Auth-Token");

        // NOTE: OPTIONSリクエスト（プリフライト）の処理
        if (request.HttpMethod == "OPTIONS")
        {
            response.StatusCode = 200;
            response.Close();
            return;
        }

        // NOTE: 認証トークンの検証（設定されている場合のみ）
        if (!string.IsNullOrEmpty(authToken))
        {
            string providedToken = request.Headers["X-Auth-Token"];
            if (providedToken != authToken)
            {
                try
                {
                    response.StatusCode = 401;
                    response.ContentType = "application/json";
                    string errorJson = CreateErrorResponse("Unauthorized. Provide valid X-Auth-Token header.");
                    byte[] errorBuffer = Encoding.UTF8.GetBytes(errorJson);
                    response.ContentLength64 = errorBuffer.Length;
                    response.OutputStream.Write(errorBuffer, 0, errorBuffer.Length);
                    response.Close();
                }
                catch { }
                return;
            }
        }

        string path = request.Url.AbsolutePath.ToLower();
        string responseJson;

        try
        {
            switch (path)
            {
                case "/api/health":
                    if (request.HttpMethod == "GET")
                    {
                        responseJson = HandleHealth();
                    }
                    else
                    {
                        responseJson = CreateErrorResponse("Method not allowed. Use GET.");
                        response.StatusCode = 405;
                    }
                    break;

                case "/api/recompile":
                    if (request.HttpMethod == "POST")
                    {
                        responseJson = HandleRecompile();
                    }
                    else
                    {
                        responseJson = CreateErrorResponse("Method not allowed. Use POST.");
                        response.StatusCode = 405;
                    }
                    break;

                case "/api/compile-status":
                    if (request.HttpMethod == "GET")
                    {
                        responseJson = HandleCompileStatus();
                    }
                    else
                    {
                        responseJson = CreateErrorResponse("Method not allowed. Use GET.");
                        response.StatusCode = 405;
                    }
                    break;

                case "/api/build":
                    if (request.HttpMethod == "POST")
                    {
                        string body = ReadRequestBody(request);
                        int buildStatusCode;
                        responseJson = HandleBuild(body, out buildStatusCode);
                        if (buildStatusCode != 200)
                        {
                            response.StatusCode = buildStatusCode;
                        }
                    }
                    else
                    {
                        responseJson = CreateErrorResponse("Method not allowed. Use POST.");
                        response.StatusCode = 405;
                    }
                    break;

                case "/api/build-status":
                    if (request.HttpMethod == "GET")
                    {
                        responseJson = HandleBuildStatus();
                    }
                    else
                    {
                        responseJson = CreateErrorResponse("Method not allowed. Use GET.");
                        response.StatusCode = 405;
                    }
                    break;

                // NOTE: ランタイムコマンド実行 (#611)
                case "/api/execute-command":
                    if (request.HttpMethod == "POST")
                    {
                        string cmdBody = ReadRequestBody(request);
                        responseJson = HandleExecuteCommand(cmdBody);
                    }
                    else
                    {
                        responseJson = CreateErrorResponse("Method not allowed. Use POST.");
                        response.StatusCode = 405;
                    }
                    break;

                // NOTE: Injectパラメータ切り替え (#611)
                case "/api/inject":
                    if (request.HttpMethod == "POST")
                    {
                        string injectBody = ReadRequestBody(request);
                        responseJson = HandleInject(injectBody);
                    }
                    else
                    {
                        responseJson = CreateErrorResponse("Method not allowed. Use POST.");
                        response.StatusCode = 405;
                    }
                    break;

                // NOTE: ゲーム状態取得 (#611)
                case "/api/game-status":
                    if (request.HttpMethod == "GET")
                    {
                        responseJson = HandleGameStatus();
                    }
                    else
                    {
                        responseJson = CreateErrorResponse("Method not allowed. Use GET.");
                        response.StatusCode = 405;
                    }
                    break;

                case "/api/play-start":
                    if (request.HttpMethod == "POST")
                    {
                        responseJson = HandlePlayStart();
                    }
                    else
                    {
                        responseJson = CreateErrorResponse("Method not allowed. Use POST.");
                        response.StatusCode = 405;
                    }
                    break;

                case "/api/play-stop":
                    if (request.HttpMethod == "POST")
                    {
                        responseJson = HandlePlayStop();
                    }
                    else
                    {
                        responseJson = CreateErrorResponse("Method not allowed. Use POST.");
                        response.StatusCode = 405;
                    }
                    break;

                case "/api/play-status":
                    if (request.HttpMethod == "GET")
                    {
                        responseJson = HandlePlayStatus();
                    }
                    else
                    {
                        responseJson = CreateErrorResponse("Method not allowed. Use GET.");
                        response.StatusCode = 405;
                    }
                    break;

                // NOTE: Eurekaランタイムからのレポート受信
                case "/api/eureka-report":
                    if (request.HttpMethod == "POST")
                    {
                        string eurekaBody = ReadRequestBody(request);
                        responseJson = HandleEurekaReport(eurekaBody);
                    }
                    else
                    {
                        responseJson = CreateErrorResponse("Method not allowed. Use POST.");
                        response.StatusCode = 405;
                    }
                    break;

                default:
                    responseJson = CreateErrorResponse($"Unknown endpoint: {path}");
                    response.StatusCode = 404;
                    break;
            }
        }
        catch (Exception e)
        {
            responseJson = CreateErrorResponse($"Internal error: {e.Message}");
            response.StatusCode = 500;
        }

        // NOTE: レスポンスを送信
        try
        {
            response.ContentType = "application/json";
            byte[] buffer = Encoding.UTF8.GetBytes(responseJson);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.Close();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[UnityCommandServer] Failed to send response: {e.Message}");
        }
    }

    private static string ReadRequestBody(HttpListenerRequest request)
    {
        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
        {
            return reader.ReadToEnd();
        }
    }

    #region API Handlers

    private static string HandleHealth()
    {
        return JsonUtility.ToJson(new HealthResponse
        {
            status = "ok",
            unityVersion = Application.unityVersion,
            projectName = Application.productName,
            role = serverRole
        });
    }

    private static string HandleRecompile()
    {
        // NOTE: メインスレッドでリコンパイルをトリガー
        EditorApplication.delayCall += () =>
        {
            lock (stateLock)
            {
                compileErrors.Clear();
                compileWarnings.Clear();
            }
            CompilationPipeline.RequestScriptCompilation();
        };

        return JsonUtility.ToJson(new RecompileResponse
        {
            success = true,
            message = "Recompile triggered",
            isCompiling = true
        });
    }

    private static string HandleCompileStatus()
    {
        lock (stateLock)
        {
            var response = new CompileStatusResponse
            {
                isCompiling = EditorApplication.isCompiling,
                hasErrors = lastCompileHadErrors,
                errors = compileErrors.ToArray(),
                warnings = compileWarnings.ToArray()
            };

            return JsonUtility.ToJson(response);
        }
    }

    private static string HandleBuild(string requestBody, out int statusCode)
    {
        statusCode = 200;

        // NOTE: ビルド中の再リクエストを拒否
        lock (stateLock)
        {
            if (isBuilding)
            {
                statusCode = 409;
                return JsonUtility.ToJson(new BuildResponse
                {
                    success = false,
                    message = "Build already in progress",
                    buildId = ""
                });
            }
        }

        BuildRequest buildRequest = null;
        if (!string.IsNullOrEmpty(requestBody))
        {
            try
            {
                buildRequest = JsonUtility.FromJson<BuildRequest>(requestBody);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UnityCommandServer] Failed to parse build request JSON: {e.Message}. Using defaults.");
            }
        }

        // NOTE: デフォルト値
        string target = buildRequest?.target ?? "StandaloneWindows64";
        string outputPath = buildRequest?.outputPath ?? "Build/Game.exe";

        // NOTE: メインスレッドでビルドを実行
        EditorApplication.delayCall += () =>
        {
            StartBuild(target, outputPath);
        };

        return JsonUtility.ToJson(new BuildResponse
        {
            success = true,
            message = "Build started",
            buildId = $"build-{DateTime.Now.Ticks}"
        });
    }

    private static void StartBuild(string target, string outputPath)
    {
        lock (stateLock)
        {
            isBuilding = true;
            buildErrors.Clear();
        }

        BuildTarget buildTarget;
        if (!Enum.TryParse(target, out buildTarget))
        {
            Debug.LogWarning($"[UnityCommandServer] Invalid build target '{target}'. Falling back to StandaloneWindows64.");
            buildTarget = BuildTarget.StandaloneWindows64;
        }

        var options = new BuildPlayerOptions
        {
            scenes = GetEnabledScenes(),
            locationPathName = outputPath,
            target = buildTarget,
            options = BuildOptions.None
        };

        try
        {
            var report = BuildPipeline.BuildPlayer(options);

            lock (stateLock)
            {
                lastBuildTime = DateTime.Now;

                if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
                {
                    lastBuildResult = "Success";
                }
                else
                {
                    lastBuildResult = "Failed";
                    foreach (var step in report.steps)
                    {
                        foreach (var message in step.messages)
                        {
                            if (message.type == LogType.Error)
                            {
                                buildErrors.Add(message.content);
                            }
                        }
                    }
                }
            }

            Debug.Log($"[UnityCommandServer] Build completed: {lastBuildResult}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[UnityCommandServer] Build failed with exception: {e.Message}");
            lock (stateLock)
            {
                lastBuildResult = "Failed";
                lastBuildTime = DateTime.Now;
                buildErrors.Add($"Exception: {e.Message}");
            }
        }
        finally
        {
            lock (stateLock)
            {
                isBuilding = false;
            }
        }
    }

    private static string[] GetEnabledScenes()
    {
        var scenes = new List<string>();
        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled)
            {
                scenes.Add(scene.path);
            }
        }
        return scenes.ToArray();
    }

    private static string HandleBuildStatus()
    {
        lock (stateLock)
        {
            return JsonUtility.ToJson(new BuildStatusResponse
            {
                isBuilding = isBuilding,
                lastBuildResult = lastBuildResult,
                lastBuildTime = lastBuildTime.ToString("o"),
                errors = buildErrors.ToArray()
            });
        }
    }

    /// <summary>
    /// ゲームコマンド実行ハンドラ (#611)
    /// NOTE: HTTPスレッドからRuntimeCommandBridge経由でメインスレッドに転送
    /// NOTE: コマンド結果はManualResetEventSlimで同期的に待機して返す
    /// </summary>
    private static string HandleExecuteCommand(string requestBody)
    {
        if (string.IsNullOrEmpty(requestBody))
        {
            return JsonUtility.ToJson(new CommandResponse
            {
                success = false,
                message = "Request body is required. Provide {\"type\": \"<command>\"}"
            });
        }

        ExecuteCommandRequest cmdRequest;
        try
        {
            cmdRequest = JsonUtility.FromJson<ExecuteCommandRequest>(requestBody);
        }
        catch (Exception e)
        {
            return JsonUtility.ToJson(new CommandResponse
            {
                success = false,
                message = $"Invalid JSON: {e.Message}"
            });
        }

        if (string.IsNullOrEmpty(cmdRequest.type))
        {
            return JsonUtility.ToJson(new CommandResponse
            {
                success = false,
                message = "Command type is required"
            });
        }

        // NOTE: RuntimeCommandBridge経由でメインスレッドに転送
        // NOTE: ManualResetEventSlimで結果を同期待機（タイムアウト5秒）
        // NOTE: Interlockedガードでtimeout後のcallbackによるObjectDisposedExceptionを防止
        var resetEvent = new ManualResetEventSlim(false);
        RuntimeCommandBridge.CommandResult result = null;
        int alive = 1;

        RuntimeCommandBridge.EnqueueCommand(new RuntimeCommandBridge.CommandRequest
        {
            commandType = cmdRequest.type,
            parametersJson = cmdRequest.parameters,
            callback = (r) =>
            {
                result = r;
                if (Interlocked.CompareExchange(ref alive, 0, 1) == 1)
                    resetEvent.Set();
            }
        });

        bool completed = resetEvent.Wait(TimeSpan.FromSeconds(5));
        Interlocked.Exchange(ref alive, 0);
        resetEvent.Dispose();

        if (!completed)
        {
            return JsonUtility.ToJson(new CommandResponse
            {
                success = false,
                message = "Command execution timed out. Is the game running?"
            });
        }

        return JsonUtility.ToJson(new CommandResponse
        {
            success = result.success,
            message = result.message
        });
    }

    /// <summary>
    /// Injectパラメータ切り替えハンドラ (#611)
    /// NOTE: メインスレッドでInjectParamListを切り替える
    /// </summary>
    private static string HandleInject(string requestBody)
    {
        if (string.IsNullOrEmpty(requestBody))
        {
            return JsonUtility.ToJson(new CommandResponse
            {
                success = false,
                message = "Request body is required. Provide {\"paramListName\": \"<name>\"}"
            });
        }

        InjectRequest injectRequest;
        try
        {
            injectRequest = JsonUtility.FromJson<InjectRequest>(requestBody);
        }
        catch (Exception e)
        {
            return JsonUtility.ToJson(new CommandResponse
            {
                success = false,
                message = $"Invalid JSON: {e.Message}"
            });
        }

        if (string.IsNullOrEmpty(injectRequest.paramListName))
        {
            return JsonUtility.ToJson(new CommandResponse
            {
                success = false,
                message = "paramListName is required"
            });
        }

        // NOTE: メインスレッドでInjectParamList切り替えを実行し、結果を同期待機
        // NOTE: Interlockedガードでtimeout後のcallbackによるObjectDisposedExceptionを防止
        var resetEvent = new ManualResetEventSlim(false);
        RuntimeCommandBridge.CommandResult result = null;
        int alive = 1;

        EditorApplication.delayCall += () =>
        {
            result = RuntimeCommandBridge.ChangeInjectParamList(injectRequest.paramListName);
            if (Interlocked.CompareExchange(ref alive, 0, 1) == 1)
                resetEvent.Set();
        };

        bool completed = resetEvent.Wait(TimeSpan.FromSeconds(5));
        Interlocked.Exchange(ref alive, 0);
        resetEvent.Dispose();

        if (!completed)
        {
            return JsonUtility.ToJson(new CommandResponse
            {
                success = false,
                message = "Inject operation timed out"
            });
        }

        return JsonUtility.ToJson(new CommandResponse
        {
            success = result.success,
            message = result.message
        });
    }

    /// <summary>
    /// ゲーム状態取得ハンドラ (#611)
    /// NOTE: メインスレッドでゲーム状態を取得し、結果を同期待機
    /// </summary>
    private static string HandleGameStatus()
    {
        // NOTE: Interlockedガードでtimeout後のcallbackによるObjectDisposedExceptionを防止
        var resetEvent = new ManualResetEventSlim(false);
        RuntimeCommandBridge.GameStatusInfo statusInfo = null;
        int alive = 1;

        EditorApplication.delayCall += () =>
        {
            statusInfo = RuntimeCommandBridge.GetGameStatus();
            if (Interlocked.CompareExchange(ref alive, 0, 1) == 1)
                resetEvent.Set();
        };

        bool completed = resetEvent.Wait(TimeSpan.FromSeconds(5));
        Interlocked.Exchange(ref alive, 0);
        resetEvent.Dispose();

        if (!completed)
        {
            return JsonUtility.ToJson(new GameStatusResponse
            {
                gameState = "Unknown",
                isPlaying = false,
                totalDistance = 0f,
                currentInjectList = "Unknown"
            });
        }

        return JsonUtility.ToJson(new GameStatusResponse
        {
            gameState = statusInfo.gameState,
            isPlaying = statusInfo.isPlaying,
            totalDistance = statusInfo.totalDistance,
            currentInjectList = statusInfo.currentInjectList
        });
    }

    private static string HandlePlayStart()
    {
        if (cachedIsPlaying)
        {
            return JsonUtility.ToJson(new PlayModeResponse
            {
                success = false,
                message = "Already in PlayMode",
                isPlaying = true
            });
        }

        // NOTE: メインスレッドでPlayModeを開始
        EditorApplication.delayCall += () =>
        {
            EditorApplication.isPlaying = true;
        };

        return JsonUtility.ToJson(new PlayModeResponse
        {
            success = true,
            message = "PlayMode starting",
            isPlaying = false
        });
    }

    private static string HandlePlayStop()
    {
        if (!cachedIsPlaying)
        {
            return JsonUtility.ToJson(new PlayModeResponse
            {
                success = false,
                message = "Not in PlayMode",
                isPlaying = false
            });
        }

        // NOTE: メインスレッドでPlayModeを停止
        EditorApplication.delayCall += () =>
        {
            EditorApplication.isPlaying = false;
        };

        return JsonUtility.ToJson(new PlayModeResponse
        {
            success = true,
            message = "PlayMode stopping",
            isPlaying = true
        });
    }

    private static string HandlePlayStatus()
    {
        return JsonUtility.ToJson(new PlayModeResponse
        {
            success = true,
            message = cachedIsPlaying ? "Playing" : "Stopped",
            isPlaying = cachedIsPlaying
        });
    }

    /// <summary>
    /// Eurekaランタイムからのレポートを処理
    /// NOTE: レポートをlog/eureka/に保存し、MelpomeneInputWindowを開く
    /// </summary>
    private static string HandleEurekaReport(string requestBody)
    {
        if (string.IsNullOrEmpty(requestBody))
        {
            return JsonUtility.ToJson(new EurekaReportResponse
            {
                success = false,
                message = "Request body is required"
            });
        }

        EurekaReportRequest report;
        try
        {
            report = JsonUtility.FromJson<EurekaReportRequest>(requestBody);
        }
        catch (Exception e)
        {
            return JsonUtility.ToJson(new EurekaReportResponse
            {
                success = false,
                message = $"Invalid JSON: {e.Message}"
            });
        }

        // NOTE: レポートをファイルに保存
        try
        {
            SaveEurekaReport(report);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[UnityCommandServer] Failed to save Eureka report: {e.Message}");
        }

        // NOTE: メインスレッドでMelpomeneInputWindowを開く
        EditorApplication.delayCall += () =>
        {
            OpenMelpomeneInputWindowForEureka(report);
        };

        return JsonUtility.ToJson(new EurekaReportResponse
        {
            success = true,
            message = "Eureka report received"
        });
    }

    /// <summary>
    /// Eurekaレポートをファイルに保存
    /// </summary>
    private static void SaveEurekaReport(EurekaReportRequest report)
    {
        string unityProjectPath = Path.GetDirectoryName(Application.dataPath);
        string projectRoot = Path.GetDirectoryName(unityProjectPath);
        string eurekaLogDir = Path.Combine(projectRoot, "log", "eureka");

        if (!Directory.Exists(eurekaLogDir))
        {
            Directory.CreateDirectory(eurekaLogDir);
        }

        string safeLogCode = Path.GetFileName(report.logCode ?? "unknown");
        string fileName = $"eureka_{safeLogCode}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        string filePath = Path.Combine(eurekaLogDir, fileName);

        string json = JsonUtility.ToJson(report, true);
        File.WriteAllText(filePath, json);

        Debug.Log($"[UnityCommandServer] Eureka report saved: {filePath}");
    }

    /// <summary>
    /// MelpomeneInputWindowを開く（Eureka用）
    /// </summary>
    private static void OpenMelpomeneInputWindowForEureka(EurekaReportRequest report)
    {
        // NOTE: ログファイルパスを構築
        string unityProjectPath = Path.GetDirectoryName(Application.dataPath);
        string projectRoot = Path.GetDirectoryName(unityProjectPath);
        string logFilePath = Path.GetFullPath(Path.Combine(projectRoot, report.logFilePath ?? ""));
        if (!logFilePath.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogWarning("[UnityCommandServer] Invalid logFilePath (outside project root)");
            return;
        }

        string safeLogCode = Path.GetFileName(report.logCode ?? "unknown");

        // NOTE: MelpomeneInputWindowを開く
        Melpomene.MelpomeneInputWindow.ShowWindowForEureka(
            "", // videoUrl - ビルド時は動画なし
            "", // logUrl - ローカルパスのみ
            safeLogCode,
            "", // videoLocalPath - ビルド時は動画なし
            logFilePath, // logLocalPath
            true // isGitHubIssueMode
        );

        Debug.Log($"[UnityCommandServer] MelpomeneInputWindow opened for Eureka report: {report.logCode}");
    }

    #endregion

    #region PlayMode Event Handler

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        // NOTE: メインスレッドで呼ばれるためスレッドセーフにキャッシュ更新
        // ExitingPlayMode中はまだ再生中なので true を維持する
        cachedIsPlaying = (state == PlayModeStateChange.EnteredPlayMode || state == PlayModeStateChange.ExitingPlayMode);
    }

    #endregion

    #region Compilation Event Handlers

    private static void OnCompilationStarted(object obj)
    {
        // NOTE: コンパイル開始時にエラー一覧をリセット
        lock (stateLock)
        {
            compileErrors.Clear();
            compileWarnings.Clear();
            lastCompileHadErrors = false;
        }
    }

    private static void OnCompilationFinished(object obj)
    {
        lock (stateLock)
        {
            lastCompileHadErrors = compileErrors.Count > 0;
            Debug.Log($"[UnityCommandServer] Compilation finished. Errors: {compileErrors.Count}, Warnings: {compileWarnings.Count}");
        }
    }

    private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
    {
        lock (stateLock)
        {
            foreach (var message in messages)
            {
                var error = new CompileError
                {
                    file = message.file,
                    line = message.line,
                    column = message.column,
                    message = message.message
                };

                if (message.type == CompilerMessageType.Error)
                {
                    compileErrors.Add(error);
                }
                else if (message.type == CompilerMessageType.Warning)
                {
                    compileWarnings.Add(error);
                }
            }
        }
    }

    #endregion

    #region Helper Methods

    private static string CreateErrorResponse(string message)
    {
        return JsonUtility.ToJson(new ErrorResponse { error = message });
    }

    #endregion

    #region Response Classes

    [Serializable]
    private class HealthResponse
    {
        public string status;
        public string unityVersion;
        public string projectName;
        public string role;
    }

    [Serializable]
    private class RecompileResponse
    {
        public bool success;
        public string message;
        public bool isCompiling;
    }

    [Serializable]
    private class CompileStatusResponse
    {
        public bool isCompiling;
        public bool hasErrors;
        public CompileError[] errors;
        public CompileError[] warnings;
    }

    [Serializable]
    public class CompileError
    {
        public string file;
        public int line;
        public int column;
        public string message;
    }

    [Serializable]
    private class BuildRequest
    {
        public string target;
        public string outputPath;
    }

    [Serializable]
    private class BuildResponse
    {
        public bool success;
        public string message;
        public string buildId;
    }

    [Serializable]
    private class BuildStatusResponse
    {
        public bool isBuilding;
        public string lastBuildResult;
        public string lastBuildTime;
        public string[] errors;
    }

    [Serializable]
    private class PlayModeResponse
    {
        public bool success;
        public string message;
        public bool isPlaying;
    }

    [Serializable]
    private class ErrorResponse
    {
        public string error;
    }

    // NOTE: ランタイムコマンド関連 (#611)

    [Serializable]
    private class ExecuteCommandRequest
    {
        public string type;
        public string parameters;
    }

    [Serializable]
    private class InjectRequest
    {
        public string paramListName;
    }

    [Serializable]
    private class CommandResponse
    {
        public bool success;
        public string message;
    }

    [Serializable]
    private class GameStatusResponse
    {
        public string gameState;
        public bool isPlaying;
        public float totalDistance;
        public string currentInjectList;
    }

    // NOTE: Eurekaランタイム連携

    [Serializable]
    private class EurekaReportRequest
    {
        public string timestamp;
        public string buildHash;
        public string logCode;
        public string logFilePath;
        public string gameState;
        public float totalDistance;
    }

    [Serializable]
    private class EurekaReportResponse
    {
        public bool success;
        public string message;
    }

    #endregion
}
