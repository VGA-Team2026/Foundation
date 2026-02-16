using System;

/// <summary>
/// GitHub Workflow Run データクラス
/// NOTE: GitHub Actions の実行情報を保持
/// </summary>
[Serializable]
public class GitHubWorkflowRun
{
    /// <summary>実行ID</summary>
    public long id;

    /// <summary>ワークフロー名</summary>
    public string name;

    /// <summary>実行状態 ("queued", "in_progress", "completed")</summary>
    public string status;

    /// <summary>結論 ("success", "failure", "cancelled", "skipped", null)</summary>
    public string conclusion;

    /// <summary>実行詳細URL</summary>
    public string html_url;

    /// <summary>対象ブランチ名</summary>
    public string head_branch;

    /// <summary>作成日時</summary>
    public string created_at;

    /// <summary>更新日時</summary>
    public string updated_at;

    /// <summary>実行番号</summary>
    public int run_number;

    /// <summary>完了済みかどうか</summary>
    public bool IsCompleted => status == "completed";

    /// <summary>成功したかどうか</summary>
    public bool IsSuccess => IsCompleted && conclusion == "success";

    /// <summary>失敗したかどうか</summary>
    public bool IsFailed => IsCompleted && conclusion == "failure";

    /// <summary>実行中かどうか</summary>
    public bool IsRunning => status == "in_progress";

    /// <summary>キュー中かどうか</summary>
    public bool IsQueued => status == "queued";

    /// <summary>
    /// 更新日時をDateTimeとして取得
    /// </summary>
    public DateTime UpdatedAtDateTime
    {
        get
        {
            if (DateTime.TryParse(updated_at, out DateTime result))
            {
                return result;
            }
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// ステータス表示用テキスト
    /// </summary>
    public string StatusText
    {
        get
        {
            if (IsCompleted)
            {
                return conclusion == "success" ? "Success" :
                       conclusion == "failure" ? "Failed" :
                       conclusion ?? "Unknown";
            }
            if (IsRunning) return "Running...";
            if (IsQueued) return "Queued";
            return status ?? "Unknown";
        }
    }
}
