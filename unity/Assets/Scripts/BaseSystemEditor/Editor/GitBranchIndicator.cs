using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

/// <summary>
/// メインツールバー（Play/Pause付近）に現在のGitブランチ名を表示する
/// Unity 6 MainToolbar API使用
/// </summary>
public static class GitBranchIndicator
{
    private const string ElementPath = "Git/Branch";
    private const float UpdateIntervalSeconds = 10f;

    private static string _currentBranch = "";
    private static double _lastUpdateTime;

    [MainToolbarElement(ElementPath, defaultDockPosition = MainToolbarDockPosition.Right)]
    public static MainToolbarElement CreateBranchLabel()
    {
        _currentBranch = GetCurrentBranch();

        var content = new MainToolbarContent($"\u2387 {_currentBranch}");
        var label = new MainToolbarLabel(content);

        EditorApplication.update += () =>
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastUpdateTime < UpdateIntervalSeconds) return;
            _lastUpdateTime = now;

            string branch = GetCurrentBranch();
            if (!string.IsNullOrEmpty(branch) && branch != _currentBranch)
            {
                _currentBranch = branch;
                label.content = new MainToolbarContent($"\u2387 {_currentBranch}");
                MainToolbar.Refresh(ElementPath);
            }
        };

        return label;
    }

    // --- Git操作 ---

    private static string GetCurrentBranch()
    {
        try
        {
            string gitPath = GetGitPath();
            if (string.IsNullOrEmpty(gitPath)) return "";
            return GetStandardOutputFromProcess(gitPath, "rev-parse --abbrev-ref HEAD").Trim();
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static string GetGitPath()
    {
        if (Application.platform == RuntimePlatform.OSXEditor)
        {
            string[] exePaths = { "/usr/local/bin/git", "/usr/bin/git" };
            return exePaths.FirstOrDefault(exePath => File.Exists(exePath));
        }
        return "git";
    }

    private static string GetStandardOutputFromProcess(string exePath, string arguments)
    {
        var startInfo = new ProcessStartInfo()
        {
            FileName = exePath,
            Arguments = arguments,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
        };

        using (var process = Process.Start(startInfo))
        {
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output;
        }
    }
}
