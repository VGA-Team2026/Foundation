using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
#if UNITY_6000_0_OR_NEWER
using UnityEditor.Build.Profile;
#endif
using UnityEngine;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using UnityEditor.Compilation;


/// <summary>
/// CI用ビルドスクリプト
/// GitHub Actionsでのビルド実行に使用
/// </summary>
public static class BuildScript
{
    private static List<LogEntry> _logEntries = new List<LogEntry>();
    private static bool _isCapturingLogs = false;

    [Serializable]
    private class LogEntry
    {
        public string message;
        public LogType logType;
        public string stackTrace;
        public string timestamp;

        public LogEntry(string message, LogType logType, string stackTrace)
        {
            this.message = message;
            this.logType = logType;
            this.stackTrace = stackTrace;
            this.timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        }
    }

    /// <summary>
    /// ビルドプロファイル種別
    /// </summary>
    public enum BuildProfileType
    {
        Development,
        Release
    }

    /// <summary>
    /// Build Profileアセットのパス
    /// </summary>
    private const string BUILD_PROFILE_PATH_FORMAT = "Assets/Settings/Build Profiles/{0}.asset";

    /// <summary>
    /// プレイヤースクリプトのコンパイルテストを実行
    /// コマンドライン引数: -executeMethod BuildScript.CompilePlayerScripts
    /// </summary>
    [MenuItem("Tools/CompileCheck")]
    public static void CompilePlayerScripts()
    {
        // ログキャプチャを開始
        StartLogCapture();

        Debug.Log("=== CompileCheck: Starting player scripts compilation test ===");

        try
        {
            // アセンブリの再コンパイルを強制実行
            Debug.Log("=== CompileCheck: Refreshing AssetDatabase ===");
            AssetDatabase.Refresh();

            // コンパイル完了時の処理を登録
            CompilationPipeline.compilationFinished += CheckCompilationResult;

            Debug.Log("=== CompileCheck: Requesting script compilation ===");
            CompilationPipeline.RequestScriptCompilation();
        }
        catch (Exception ex)
        {
            // イベントハンドラを解除
            CompilationPipeline.compilationFinished -= CheckCompilationResult;
            Debug.LogError($"=== CompileCheck: Exception occurred during compilation ===");
            Debug.LogError($"Exception: {ex.Message}");
            Debug.LogError($"StackTrace: {ex.StackTrace}");
            StopLogCapture();
            OutputLogReport();
            EditorApplication.Exit(1);
        }
    }

    /// <summary>
    /// コンパイル結果をチェック
    /// </summary>
    private static void CheckCompilationResult(object obj)
    {
        Debug.Log("=== CompileCheck: Checking compilation result ===");

        // イベントハンドラを解除（重要）
        CompilationPipeline.compilationFinished -= CheckCompilationResult;

        // アセンブリ情報を取得してログ出力
        LogAssemblyInfo();

        // ログキャプチャを停止
        StopLogCapture();

        // ログレポートを出力
        OutputLogReport();

        // エディタからのテストならここで終わる
        if (!Application.isBatchMode) return;

        // コンパイルエラーをチェック
        if (HasCompilationErrors())
        {
            Debug.LogError("=== CompileCheck: Compilation completed with errors ===");
            EditorApplication.Exit(1);
        }
        else
        {
            Debug.Log("=== CompileCheck: Compilation completed successfully ===");
            EditorApplication.Exit(0);
        }
    }

    /// <summary>
    /// コンパイルエラーがあるかチェック
    /// </summary>
    private static bool HasCompilationErrors()
    {
        try
        {
            // まず、EditorUtility.scriptCompilationFailedをチェック
            if (EditorUtility.scriptCompilationFailed)
            {
                Debug.LogError("=== CompileCheck: scriptCompilationFailed is true ===");
                return true;
            }

            // アセンブリが正しく取得できるかチェック
            var assemblies = CompilationPipeline.GetAssemblies();
            if (assemblies == null || assemblies.Length == 0)
            {
                Debug.LogError("=== CompileCheck: No assemblies found ===");
                return true;
            }

            Debug.Log($"=== CompileCheck: Found {assemblies.Length} assemblies ===");

            // 各アセンブリの基本情報をチェック
            foreach (var assembly in assemblies)
            {
                if (assembly.sourceFiles == null || assembly.sourceFiles.Length == 0)
                {
                    Debug.Log($"=== CompileCheck: Assembly '{assembly.name}' has no source files ===");
                    continue;
                }

                Debug.Log($"=== CompileCheck: Assembly '{assembly.name}' has {assembly.sourceFiles.Length} source files ===");
            }

            var errorCount = _logEntries.Count(e => e.logType == LogType.Error || e.logType == LogType.Exception);
            if (errorCount > 0)
            {
                Debug.LogError("=== CompileCheck: has error log ===");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Debug.LogError($"=== CompileCheck: Exception in HasCompilationErrors: {ex.Message} ===");
            return true;
        }
    }

    /// <summary>
    /// アセンブリ情報をログ出力（デバッグ用）
    /// </summary>
    private static void LogAssemblyInfo()
    {
        try
        {
            Debug.Log("=== CompileCheck: Assembly Information ===");

            var assemblies = CompilationPipeline.GetAssemblies();
            Debug.Log($"Total assemblies: {assemblies?.Length ?? 0}");

            if (assemblies != null)
            {
                foreach (var assembly in assemblies)
                {
                    Debug.Log($"Assembly: {assembly.name}");
                    Debug.Log($"  Output: {assembly.outputPath}");
                    Debug.Log($"  Source files: {assembly.sourceFiles?.Length ?? 0}");

                    // 参照されているアセンブリも表示
                    if (assembly.assemblyReferences != null && assembly.assemblyReferences.Length > 0)
                    {
                        Debug.Log($"  References: {string.Join(", ", assembly.assemblyReferences.Select(r => r.name))}");
                    }
                }
            }

            Debug.Log("=== CompileCheck: End Assembly Information ===");
        }
        catch (Exception ex)
        {
            Debug.LogError($"=== CompileCheck: Exception in LogAssemblyInfo: {ex.Message} ===");
        }
    }

    /// <summary>
    /// ログレポートをMarkdown形式でファイルに出力
    /// </summary>
    private static void OutputLogReport()
    {
        try
        {
            var reportPath = Path.Combine(Application.dataPath, "..", "..", "compile-report.md");
            var report = GenerateMarkdownReport();

            File.WriteAllText(reportPath, report, Encoding.UTF8);

            Debug.Log($"=== CompileCheck: Log report saved to {reportPath} ===");

            // コンソールにも全文を出力
            Debug.Log("=== COMPILE REPORT START ===");
            Debug.Log(report);
            Debug.Log("=== COMPILE REPORT END ===");
        }
        catch (Exception ex)
        {
            Debug.LogError($"=== CompileCheck: Failed to output log report: {ex.Message} ===");
        }
    }

#if UNITY_6000_0_OR_NEWER
    /// <summary>
    /// Build Profileアセットをロード
    /// </summary>
    /// <param name="profileType">プロファイル種別</param>
    /// <returns>Build Profileアセット（見つからない場合はnull）</returns>
    private static BuildProfile LoadBuildProfile(BuildProfileType profileType)
    {
        var profilePath = string.Format(BUILD_PROFILE_PATH_FORMAT, profileType.ToString());
        var buildProfile = AssetDatabase.LoadAssetAtPath<BuildProfile>(profilePath);

        if (buildProfile == null)
        {
            Debug.LogWarning($"=== BuildScript: Build Profile not found at {profilePath} ===");
        }
        else
        {
            Debug.Log($"=== BuildScript: Loaded Build Profile from {profilePath} ===");
        }

        return buildProfile;
    }
#endif

    /// <summary>
    /// CIからビルドを実行
    /// コマンドライン引数: -executeMethod BuildScript.PerformBuild -buildProfile Development -buildPath "C:\Build\Development"
    /// </summary>
    public static void PerformBuild()
    {
        StartLogCapture();
        Debug.Log("=== BuildScript: Starting Unity Build ===");

        try
        {
            // コマンドライン引数を解析
            var args = ParseCommandLineArgs();
            var profileType = args.profileType;
            var buildPath = args.buildPath;

            Debug.Log($"=== BuildScript: ProfileType = {profileType}, BuildPath = {buildPath} ===");

#if UNITY_6000_0_OR_NEWER
            // Build Profileアセットをロード
            var buildProfile = LoadBuildProfile(profileType);

            // ビルド対象シーンリスト（Build Profileから取得、なければEditorBuildSettingsから）
            string[] scenes;
            if (buildProfile != null && buildProfile.scenes != null && buildProfile.scenes.Length > 0)
            {
                scenes = buildProfile.scenes
                    .Where(scene => scene.enabled)
                    .Select(scene => scene.path)
                    .ToArray();
                Debug.Log($"=== BuildScript: Using scenes from Build Profile ===");
            }
            else
            {
                scenes = EditorBuildSettings.scenes
                    .Where(scene => scene.enabled)
                    .Select(scene => scene.path)
                    .ToArray();
                Debug.Log($"=== BuildScript: Using scenes from EditorBuildSettings ===");
            }
#else
            // Unity 6未満ではEditorBuildSettingsから取得
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            Debug.Log($"=== BuildScript: Using scenes from EditorBuildSettings ===");
#endif

            if (scenes.Length == 0)
            {
                Debug.LogError("=== BuildScript: No scenes found in Build Settings ===");
                OutputBuildReport(null, profileType);
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"=== BuildScript: Building {scenes.Length} scenes ===");
            foreach (var scene in scenes)
            {
                Debug.Log($"  - {scene}");
            }

            // 出力ディレクトリを作成
            if (!string.IsNullOrEmpty(buildPath))
            {
                Directory.CreateDirectory(buildPath);
            }

#if UNITY_6000_0_OR_NEWER
            // Build Profileをアクティブに設定（スクリプトシンボル等を適用）
            if (buildProfile != null)
            {
                BuildProfile.SetActiveBuildProfile(buildProfile);
                Debug.Log($"=== BuildScript: Set active Build Profile to {profileType} ===");
            }
#endif

            // ビルドオプションを設定
            // NOTE: ビルドターゲットはWindows64固定（Build Profileはスクリプトシンボル適用のみに使用）
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(buildPath, $"{PlayerSettings.productName}.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = GetBuildOptions(profileType)
            };

            Debug.Log($"=== BuildScript: Output Path = {options.locationPathName} ===");
            Debug.Log($"=== BuildScript: Build Target = StandaloneWindows64 ===");

            // Addressablesをビルド
            Debug.Log("=== BuildScript: Building Addressables ===");
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult addressablesResult);
            if (addressablesResult != null && !string.IsNullOrEmpty(addressablesResult.Error))
            {
                Debug.LogError($"=== BuildScript: Addressables build failed: {addressablesResult.Error} ===");
                OutputBuildReport(null, profileType);
                EditorApplication.Exit(1);
                return;
            }
            Debug.Log("=== BuildScript: Addressables build completed ===");

            // BuildStateを生成
            BuildScript.BuildStateBuild(BuildState.TeamID);

            // ビルドを実行
            Debug.Log("=== BuildScript: Building Player ===");
            var report = BuildPipeline.BuildPlayer(options);

            // レポートを出力
            OutputBuildReport(report, profileType);

            // 結果に応じて終了
            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log("=== BuildScript: BUILD SUCCESS ===");
                Debug.Log($"=== BuildScript: Total Time = {report.summary.totalTime} ===");
                Debug.Log($"=== BuildScript: Total Size = {report.summary.totalSize / (1024 * 1024)} MB ===");
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError("=== BuildScript: BUILD FAILED ===");
                Debug.LogError($"=== BuildScript: Total Errors = {report.summary.totalErrors} ===");
                Debug.LogError($"=== BuildScript: Total Warnings = {report.summary.totalWarnings} ===");

                // ビルドステップを出力
                foreach (var step in report.steps)
                {
                    Debug.Log($"Step: {step.name} - {step.duration}");
                    foreach (var message in step.messages)
                    {
                        if (message.type == LogType.Error || message.type == LogType.Exception)
                        {
                            Debug.LogError($"  {message.content}");
                        }
                    }
                }

                EditorApplication.Exit(1);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"=== BuildScript: Exception occurred ===");
            Debug.LogError($"Message: {ex.Message}");
            Debug.LogError($"StackTrace: {ex.StackTrace}");
            StopLogCapture();
            OutputBuildReport(null, BuildProfileType.Development);
            EditorApplication.Exit(1);
        }
    }

    /// <summary>
    /// コマンドライン引数を解析
    /// </summary>
    private static (BuildProfileType profileType, string buildPath) ParseCommandLineArgs()
    {
        var profileType = BuildProfileType.Development;
        var buildPath = Path.Combine(Application.dataPath, "../../Build/Development");

        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-buildProfile":
                    if (i + 1 < args.Length)
                    {
                        if (Enum.TryParse<BuildProfileType>(args[i + 1], true, out var parsedProfile))
                        {
                            profileType = parsedProfile;
                        }
                    }
                    break;
                case "-buildPath":
                    if (i + 1 < args.Length)
                    {
                        buildPath = args[i + 1].Trim('"');
                    }
                    break;
            }
        }

        return (profileType, buildPath);
    }

    /// <summary>
    /// ビルドオプションを取得
    /// </summary>
    private static BuildOptions GetBuildOptions(BuildProfileType profileType)
    {
        switch (profileType)
        {
            case BuildProfileType.Development:
                return BuildOptions.Development | BuildOptions.AllowDebugging;
            case BuildProfileType.Release:
                return BuildOptions.None;
            default:
                return BuildOptions.Development;
        }
    }

    /// <summary>
    /// BuildStateファイルを生成
    /// </summary>
    public static void BuildStateBuild(string teamID)
    {
        var targetPath = Path.Combine(Application.dataPath, "Scripts", "BaseSystem", "Dynamic");
        const string source = @"
public class BuildState
{
    const string _hash = ""<Hash>"";
    public const string TeamID = ""<TeamID>"";

    public static string BuildHash
    {
        get
        {
#if UNITY_EDITOR
            return ""UNITY_EDITOR"";
#else
            return _hash;
#endif
        }
    }
};";

        try
        {
            Directory.CreateDirectory(targetPath);

            var sourceCode = source
                .Replace("<Hash>", Guid.NewGuid().ToString())
                .Replace("<TeamID>", teamID);
            File.WriteAllText(Path.Combine(targetPath, "BuildState.cs"), sourceCode, Encoding.UTF8);

            Debug.Log($"=== BuildScript: BuildState.cs generated ===");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"=== BuildScript: Failed to generate BuildState.cs: {ex.Message} ===");
        }
    }

    /// <summary>
    /// ログキャプチャを開始
    /// </summary>
    private static void StartLogCapture()
    {
        if (!_isCapturingLogs)
        {
            _logEntries.Clear();
            Application.logMessageReceived += OnLogMessageReceived;
            _isCapturingLogs = true;
        }
    }

    /// <summary>
    /// ログキャプチャを停止
    /// </summary>
    private static void StopLogCapture()
    {
        if (_isCapturingLogs)
        {
            Application.logMessageReceived -= OnLogMessageReceived;
            _isCapturingLogs = false;
        }
    }

    /// <summary>
    /// ログメッセージ受信時の処理
    /// </summary>
    private static void OnLogMessageReceived(string logString, string stackTrace, LogType type)
    {
        if (_isCapturingLogs)
        {
            _logEntries.Add(new LogEntry(logString, type, stackTrace));
        }
    }

    /// <summary>
    /// ビルドレポートを出力
    /// </summary>
    private static void OutputBuildReport(BuildReport report, BuildProfileType profileType)
    {
        StopLogCapture();

        try
        {
            var reportPath = Path.Combine(Application.dataPath, "..", "..", "build-report.md");
            var markdown = GenerateBuildReport(report, profileType);
            File.WriteAllText(reportPath, markdown, Encoding.UTF8);
            Debug.Log($"=== BuildScript: Report saved to {reportPath} ===");
        }
        catch (Exception ex)
        {
            Debug.LogError($"=== BuildScript: Failed to output report: {ex.Message} ===");
        }
    }

    /// <summary>
    /// Markdownレポートを生成
    /// </summary>
    private static string GenerateMarkdownReport()
    {
        var sb = new StringBuilder();

        // ヘッダー
        sb.AppendLine("# Unity Compilation Report");
        sb.AppendLine();
        sb.AppendLine($"**Generated:** {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"**Total Logs:** {_logEntries.Count}");
        sb.AppendLine();

        // 統計情報
        var errorCount = _logEntries.Count(e => e.logType == LogType.Error || e.logType == LogType.Exception);
        var warningCount = _logEntries.Count(e => e.logType == LogType.Warning);
        var infoCount = _logEntries.Count(e => e.logType == LogType.Log);

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- **Errors:** {errorCount}");
        sb.AppendLine($"- **Warnings:** {warningCount}");
        sb.AppendLine($"- **Info:** {infoCount}");
        sb.AppendLine();

        // 成否判定
        bool hasCompilationErrors = HasCompilationErrors() || errorCount > 0;
        sb.AppendLine($"**Result:** {(hasCompilationErrors ? "FAILED" : "SUCCESS")}");
        sb.AppendLine();

        // エラーセクション
        if (errorCount > 0)
        {
            sb.AppendLine("## Errors");
            sb.AppendLine();
            var errors = _logEntries.Where(e => e.logType == LogType.Error || e.logType == LogType.Exception);
            foreach (var error in errors)
            {
                sb.AppendLine($"### [{error.timestamp}] {error.logType}");
                sb.AppendLine("```");
                sb.AppendLine(error.message);
                if (!string.IsNullOrEmpty(error.stackTrace))
                {
                    sb.AppendLine();
                    sb.AppendLine("Stack Trace:");
                    sb.AppendLine(error.stackTrace);
                }
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        // 警告セクション
        if (warningCount > 0)
        {
            sb.AppendLine("## Warnings");
            sb.AppendLine();
            var warnings = _logEntries.Where(e => e.logType == LogType.Warning);
            foreach (var warning in warnings)
            {
                sb.AppendLine($"### [{warning.timestamp}] Warning");
                sb.AppendLine("```");
                sb.AppendLine(warning.message);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        // 情報ログセクション
        if (infoCount > 0)
        {
            sb.AppendLine("## Information Logs");
            sb.AppendLine();
            var infos = _logEntries.Where(e => e.logType == LogType.Log);
            foreach (var info in infos)
            {
                sb.AppendLine($"- [{info.timestamp}] {info.message}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Markdownレポートを生成
    /// </summary>
    private static string GenerateBuildReport(BuildReport report, BuildProfileType profileType)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Unity Build Report");
        sb.AppendLine();
        sb.AppendLine($"**Generated:** {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"**Profile:** {profileType}");
        sb.AppendLine();

        if (report != null)
        {
            sb.AppendLine("## Build Summary");
            sb.AppendLine();
            sb.AppendLine($"- **Result:** {(report.summary.result == BuildResult.Succeeded ? "SUCCESS" : "FAILED")}");
            sb.AppendLine($"- **Platform:** {report.summary.platform}");
            sb.AppendLine($"- **Total Time:** {report.summary.totalTime}");
            sb.AppendLine($"- **Total Size:** {report.summary.totalSize / (1024 * 1024)} MB");
            sb.AppendLine($"- **Errors:** {report.summary.totalErrors}");
            sb.AppendLine($"- **Warnings:** {report.summary.totalWarnings}");
            sb.AppendLine();

            // ビルドステップ
            if (report.steps.Length > 0)
            {
                sb.AppendLine("## Build Steps");
                sb.AppendLine();
                foreach (var step in report.steps)
                {
                    sb.AppendLine($"### {step.name}");
                    sb.AppendLine($"Duration: {step.duration}");

                    var errors = step.messages.Where(m => m.type == LogType.Error || m.type == LogType.Exception).ToList();
                    if (errors.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine("**Errors:**");
                        sb.AppendLine("```");
                        foreach (var msg in errors)
                        {
                            sb.AppendLine(msg.content);
                        }
                        sb.AppendLine("```");
                    }
                    sb.AppendLine();
                }
            }
        }
        else
        {
            sb.AppendLine("## Build Summary");
            sb.AppendLine();
            sb.AppendLine("**Result:** FAILED (No build report generated)");
            sb.AppendLine();
        }

        // エラーログセクション
        var errorLogs = _logEntries.Where(e => e.logType == LogType.Error || e.logType == LogType.Exception).ToList();
        if (errorLogs.Count > 0)
        {
            sb.AppendLine("## Error Logs");
            sb.AppendLine();
            foreach (var error in errorLogs)
            {
                sb.AppendLine($"### [{error.timestamp}] {error.logType}");
                sb.AppendLine("```");
                sb.AppendLine(error.message);
                if (!string.IsNullOrEmpty(error.stackTrace))
                {
                    sb.AppendLine();
                    sb.AppendLine("Stack Trace:");
                    sb.AppendLine(error.stackTrace);
                }
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// エディターメニューからビルドを実行（テスト用）
    /// </summary>
    [MenuItem("Tools/Build/Development Build")]
    public static void DevelopmentBuild()
    {
        var buildPath = Path.Combine(Application.dataPath, "../../Build/Development");
        Directory.CreateDirectory(buildPath);

        var scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(buildPath, $"{PlayerSettings.productName}.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development | BuildOptions.AllowDebugging
        };

        BuildScript.BuildStateBuild(BuildState.TeamID);
        AddressableAssetSettings.BuildPlayerContent(out var devAddressablesResult);
        if (!string.IsNullOrEmpty(devAddressablesResult.Error))
        {
            Debug.LogError($"Build failed: Addressables build failed: {devAddressablesResult.Error}");
            return;
        }
        var report = BuildPipeline.BuildPlayer(options);

        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"Build succeeded: {options.locationPathName}");
            EditorUtility.RevealInFinder(options.locationPathName);
        }
        else
        {
            Debug.LogError("Build failed!");
        }
    }

    /// <summary>
    /// エディターメニューからリリースビルドを実行（テスト用）
    /// </summary>
    [MenuItem("Tools/Build/Release Build")]
    public static void ReleaseBuild()
    {
        var buildPath = Path.Combine(Application.dataPath, "../../Build/Release");
        Directory.CreateDirectory(buildPath);

        var scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = Path.Combine(buildPath, $"{PlayerSettings.productName}.exe"),
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        BuildScript.BuildStateBuild(BuildState.TeamID);
        AddressableAssetSettings.BuildPlayerContent(out var releaseAddressablesResult);
        if (!string.IsNullOrEmpty(releaseAddressablesResult.Error))
        {
            Debug.LogError($"Build failed: Addressables build failed: {releaseAddressablesResult.Error}");
            return;
        }
        var report = BuildPipeline.BuildPlayer(options);

        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"Build succeeded: {options.locationPathName}");
            EditorUtility.RevealInFinder(options.locationPathName);
        }
        else
        {
            Debug.LogError("Build failed!");
        }
    }
}
