using System.Diagnostics;

/// <summary>
/// Musa共通: 外部プロセス実行ユーティリティ
/// NOTE: MusaWindow, MelpomeneSetupWizardで共有
/// </summary>
public static class MusaProcessUtility
{
    /// <summary>
    /// コマンドの利用可否とバージョンを取得
    /// </summary>
    public static (bool available, string version) CheckCommand(string command, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(command, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var process = Process.Start(psi))
            {
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.StandardError.ReadToEnd();
                process.WaitForExit(5000);
                // NOTE: 最初の行だけ取得（gh --versionは複数行出力する場合がある）
                var firstLine = output.Split('\n')[0].Trim();
                return (process.ExitCode == 0, firstLine);
            }
        }
        catch
        {
            return (false, null);
        }
    }
}
