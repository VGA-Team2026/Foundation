using System;

/// <summary>
/// GitHub Pull Request Review データクラス
/// NOTE: PRのレビュー情報を保持
/// </summary>
[Serializable]
public class GitHubReview
{
    /// <summary>レビューID</summary>
    public long id;

    /// <summary>レビュー状態 ("APPROVED", "CHANGES_REQUESTED", "COMMENTED", "PENDING", "DISMISSED")</summary>
    public string state;

    /// <summary>レビューコメント本文</summary>
    public string body;

    /// <summary>レビュアーのログイン名</summary>
    public string user_login;

    /// <summary>送信日時</summary>
    public string submitted_at;

    /// <summary>承認済みかどうか</summary>
    public bool IsApproved => state == "APPROVED";

    /// <summary>変更リクエストかどうか</summary>
    public bool IsChangesRequested => state == "CHANGES_REQUESTED";

    /// <summary>コメントのみかどうか</summary>
    public bool IsCommented => state == "COMMENTED";

    /// <summary>
    /// 送信日時をDateTimeとして取得
    /// </summary>
    public DateTime SubmittedAtDateTime
    {
        get
        {
            if (DateTime.TryParse(submitted_at, out DateTime result))
            {
                return result;
            }
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// レビュー状態の表示用テキスト
    /// </summary>
    public string StateText
    {
        get
        {
            return state switch
            {
                "APPROVED" => "Approved",
                "CHANGES_REQUESTED" => "Changes Requested",
                "COMMENTED" => "Commented",
                "PENDING" => "Pending",
                "DISMISSED" => "Dismissed",
                _ => state ?? "Unknown"
            };
        }
    }
}
