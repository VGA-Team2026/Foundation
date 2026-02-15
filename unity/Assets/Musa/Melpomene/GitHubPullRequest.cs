using System;

/// <summary>
/// GitHub Pull Request データクラス
/// NOTE: GitHub API から取得したPR情報を保持
/// </summary>
[Serializable]
public class GitHubPullRequest
{
    /// <summary>PR番号</summary>
    public int number;

    /// <summary>PRタイトル</summary>
    public string title;

    /// <summary>PR状態 ("open", "closed", "merged")</summary>
    public string state;

    /// <summary>PRのURL</summary>
    public string html_url;

    /// <summary>ソースブランチ名</summary>
    public string head_ref;

    /// <summary>ターゲットブランチ名</summary>
    public string base_ref;

    /// <summary>PR作成者のログイン名</summary>
    public string user_login;

    /// <summary>マージ可能かどうか</summary>
    public bool mergeable;

    /// <summary>マージ状態 ("clean", "dirty", "blocked", "unknown")</summary>
    public string mergeable_state;

    /// <summary>更新日時</summary>
    public string updated_at;

    /// <summary>レビュー数（API取得後に設定）</summary>
    public int reviewCount;

    /// <summary>前回チェック以降の新規レビューがあるか</summary>
    public bool hasNewReview;

    /// <summary>ドラフトPRかどうか</summary>
    public bool draft;

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
}
