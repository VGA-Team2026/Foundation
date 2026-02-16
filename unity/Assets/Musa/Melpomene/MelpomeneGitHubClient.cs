#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;

namespace Melpomene
{
    /// <summary>
    /// GitHubマイルストーン情報
    /// </summary>
    [Serializable]
    public class GitHubMilestone
    {
        /// <summary>マイルストーン番号（GitHub API用）</summary>
        public int number;

        /// <summary>マイルストーン名</summary>
        public string title;

        /// <summary>説明</summary>
        public string description;

        /// <summary>期限（ISO 8601形式、nullの場合あり）</summary>
        public string dueOn;

        /// <summary>状態（open/closed）</summary>
        public string state;

        /// <summary>
        /// 表示用テキストを取得
        /// </summary>
        public string DisplayText
        {
            get
            {
                if (string.IsNullOrEmpty(dueOn))
                {
                    return title;
                }

                // ISO 8601形式をパース（UTCをローカル時間に変換して比較）
                if (DateTimeOffset.TryParse(dueOn, out DateTimeOffset dto))
                {
                    var localDate = dto.LocalDateTime.Date;
                    var remaining = (int)(localDate - DateTime.Today).TotalDays;
                    if (remaining < 0)
                    {
                        return $"{title} (期限切れ: {-remaining}日超過)";
                    }
                    else if (remaining == 0)
                    {
                        return $"{title} (本日期限)";
                    }
                    else
                    {
                        return $"{title} (残り{remaining}日)";
                    }
                }

                return title;
            }
        }
    }

    /// <summary>
    /// GitHub API クライアント
    /// NOTE: GitHub Issues APIとの通信を担当
    /// </summary>
    public class MelpomeneGitHubClient
    {
        private readonly MelpomeneConfig config;

        public MelpomeneGitHubClient(MelpomeneConfig config)
        {
            this.config = config;
        }

        /// <summary>
        /// Issueを作成する
        /// </summary>
        public async UniTask<MelpomeneTicket> CreateIssueAsync(MelpomeneTicket ticket)
        {
            if (!config.IsValid)
            {
                Debug.LogWarning("[Melpomene] Config is not valid. Please set repository and access token.");
                return null;
            }

            var url = $"{config.ApiBaseUrl}/issues";

            // ラベルを構築
            var labels = new List<string>(config.defaultLabels);
            if (!string.IsNullOrEmpty(ticket.labels))
            {
                labels.AddRange(ticket.labels.Split(','));
            }
            labels.Add(ticket.priority.ToString().ToLower());
            labels.Add(ticket.category.ToString().ToLower());

            // リクエストボディを構築
            var requestBody = new GitHubIssueRequest
            {
                title = ticket.GenerateIssueTitle(),
                body = ticket.GenerateIssueBody(),
                labels = labels.ToArray()
            };

            var json = JsonUtility.ToJson(requestBody);
            var bodyBytes = Encoding.UTF8.GetBytes(json);

            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {config.accessToken}");
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.SetRequestHeader("User-Agent", "Melpomene-Unity");

                try
                {
                    await request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var response = JsonUtility.FromJson<GitHubIssueResponse>(request.downloadHandler.text);
                        ticket.issueNumber = response.number;
                        ticket.issueUrl = response.html_url;
                        ticket.state = response.state;
                        Debug.Log($"[Melpomene] Issue created: #{response.number} - {response.html_url}");
                        return ticket;
                    }
                    else
                    {
                        Debug.LogError($"[Melpomene] Failed to create issue: {request.error}\n{request.downloadHandler.text}");
                        return null;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Melpomene] Exception creating issue: {e.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// Melpomeneタグ付きのIssue一覧を取得する
        /// </summary>
        public async UniTask<List<MelpomeneTicket>> GetIssuesAsync()
        {
            if (!config.IsValid)
            {
                Debug.LogWarning("[Melpomene] Config is not valid. Please set repository and access token.");
                return new List<MelpomeneTicket>();
            }

            var tickets = new List<MelpomeneTicket>();
            // NOTE: ラベルフィルタを外し、全てのopen issueを取得
            var url = $"{config.ApiBaseUrl}/issues?state=open&per_page=100";

            Debug.Log($"[Melpomene] Fetching issues from: {url}");

            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Authorization", $"Bearer {config.accessToken}");
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.SetRequestHeader("User-Agent", "Melpomene-Unity");

                try
                {
                    await request.SendWebRequest();

                    Debug.Log($"[Melpomene] Response code: {request.responseCode}");

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        // JSONの配列をパース
                        var jsonArray = request.downloadHandler.text;
                        Debug.Log($"[Melpomene] Response length: {jsonArray.Length} chars");

                        var issues = ParseIssueArray(jsonArray);
                        Debug.Log($"[Melpomene] Parsed {issues.Count} issues from response");

                        foreach (var issueData in issues)
                        {
                            // [Melpomene]タグを含むIssueのみ処理
                            if (issueData.issue.title != null && issueData.issue.title.Contains("[Melpomene]"))
                            {
                                var ticket = MelpomeneTicket.ParseFromIssue(
                                    issueData.issue.number,
                                    issueData.issue.title,
                                    issueData.issue.body ?? "",
                                    issueData.issue.html_url,
                                    issueData.issue.state,
                                    issueData.issue.created_at,
                                    issueData.labels
                                );
                                tickets.Add(ticket);
                                Debug.Log($"[Melpomene] Added ticket: #{issueData.issue.number} - {issueData.issue.title} (labels: {string.Join(", ", issueData.labels)})");
                            }
                        }

                        Debug.Log($"[Melpomene] Fetched {tickets.Count} Melpomene tickets from GitHub (total issues: {issues.Count})");
                    }
                    else
                    {
                        Debug.LogError($"[Melpomene] Failed to fetch issues: {request.error}\nResponse: {request.downloadHandler.text}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Melpomene] Exception fetching issues: {e.Message}\n{e.StackTrace}");
                }
            }

            return tickets;
        }

        /// <summary>
        /// Issueのコメント一覧を取得する
        /// </summary>
        public async UniTask<List<MelpomeneComment>> GetCommentsAsync(int issueNumber)
        {
            if (!config.IsValid)
            {
                Debug.LogWarning("[Melpomene] Config is not valid. Please set repository and access token.");
                return new List<MelpomeneComment>();
            }

            var comments = new List<MelpomeneComment>();
            var url = $"{config.ApiBaseUrl}/issues/{issueNumber}/comments?per_page=100";

            Debug.Log($"[Melpomene] Fetching comments from: {url}");

            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Authorization", $"Bearer {config.accessToken}");
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.SetRequestHeader("User-Agent", "Melpomene-Unity");

                try
                {
                    await request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var jsonArray = request.downloadHandler.text;
                        comments = ParseCommentArray(jsonArray);
                        Debug.Log($"[Melpomene] Fetched {comments.Count} comments for issue #{issueNumber}");
                    }
                    else
                    {
                        Debug.LogError($"[Melpomene] Failed to fetch comments: {request.error}\nResponse: {request.downloadHandler.text}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Melpomene] Exception fetching comments: {e.Message}");
                }
            }

            return comments;
        }

        /// <summary>
        /// Issueにコメントを投稿する
        /// </summary>
        public async UniTask<bool> PostCommentAsync(int issueNumber, string body)
        {
            if (!config.IsValid)
            {
                Debug.LogWarning("[Melpomene] Config is not valid. Please set repository and access token.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                Debug.LogWarning("[Melpomene] Comment body is empty.");
                return false;
            }

            var url = $"{config.ApiBaseUrl}/issues/{issueNumber}/comments";
            var requestBody = new GitHubCommentRequest { body = body };
            var json = JsonUtility.ToJson(requestBody);
            var bodyBytes = Encoding.UTF8.GetBytes(json);

            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {config.accessToken}");
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.SetRequestHeader("User-Agent", "Melpomene-Unity");

                try
                {
                    await request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        Debug.Log($"[Melpomene] Comment posted to issue #{issueNumber}");
                        return true;
                    }
                    else
                    {
                        Debug.LogError($"[Melpomene] Failed to post comment: {request.error}\n{request.downloadHandler.text}");
                        return false;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Melpomene] Exception posting comment: {e.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Issueを更新する（タイトルと本文）
        /// </summary>
        public async UniTask<bool> UpdateIssueAsync(MelpomeneTicket ticket)
        {
            if (!config.IsValid)
            {
                Debug.LogWarning("[Melpomene] Config is not valid.");
                return false;
            }

            if (ticket.issueNumber <= 0)
            {
                Debug.LogError("[Melpomene] Invalid issue number.");
                return false;
            }

            var url = $"{config.ApiBaseUrl}/issues/{ticket.issueNumber}";

            // リクエストボディを構築
            var requestBody = new GitHubIssueUpdateRequest
            {
                title = ticket.GenerateIssueTitle(),
                body = ticket.GenerateIssueBody()
            };

            var json = JsonUtility.ToJson(requestBody);
            var bodyBytes = Encoding.UTF8.GetBytes(json);

            using (var request = new UnityWebRequest(url, "PATCH"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {config.accessToken}");
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.SetRequestHeader("User-Agent", "Melpomene-Unity");

                try
                {
                    await request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        Debug.Log($"[Melpomene] Issue #{ticket.issueNumber} updated");
                        return true;
                    }
                    else
                    {
                        Debug.LogError($"[Melpomene] Failed to update issue: {request.error}\n{request.downloadHandler.text}");
                        return false;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Melpomene] Exception updating issue: {e.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Issueをクローズする
        /// </summary>
        public async UniTask<bool> CloseIssueAsync(int issueNumber)
        {
            if (!config.IsValid)
            {
                Debug.LogWarning("[Melpomene] Config is not valid.");
                return false;
            }

            var url = $"{config.ApiBaseUrl}/issues/{issueNumber}";
            var json = "{\"state\":\"closed\"}";
            var bodyBytes = Encoding.UTF8.GetBytes(json);

            using (var request = new UnityWebRequest(url, "PATCH"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {config.accessToken}");
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.SetRequestHeader("User-Agent", "Melpomene-Unity");

                try
                {
                    await request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        Debug.Log($"[Melpomene] Issue #{issueNumber} closed");
                        return true;
                    }
                    else
                    {
                        Debug.LogError($"[Melpomene] Failed to close issue: {request.error}");
                        return false;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Melpomene] Exception closing issue: {e.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// パース結果を保持する構造体
        /// </summary>
        private struct ParsedIssueData
        {
            public GitHubIssueResponse issue;
            public string[] labels;
        }

        /// <summary>
        /// JSON配列をパースする（JsonUtilityは配列に対応していないため）
        /// </summary>
        private List<ParsedIssueData> ParseIssueArray(string jsonArray)
        {
            var issues = new List<ParsedIssueData>();

            if (string.IsNullOrEmpty(jsonArray) || jsonArray.Trim() == "[]")
            {
                Debug.Log("[Melpomene] Empty issue array received");
                return issues;
            }

            // 手動でパースを試みる
            // 各Issueオブジェクトを個別に抽出
            int depth = 0;
            int start = -1;
            bool inString = false;
            bool escape = false;

            for (int i = 0; i < jsonArray.Length; i++)
            {
                char c = jsonArray[i];

                // エスケープシーケンスの処理
                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escape = true;
                    continue;
                }

                // 文字列内外の判定
                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                // 文字列内では括弧をカウントしない
                if (inString)
                {
                    continue;
                }

                if (c == '{')
                {
                    if (depth == 0)
                    {
                        start = i;
                    }
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        var issueJson = jsonArray.Substring(start, i - start + 1);
                        try
                        {
                            var issue = JsonUtility.FromJson<GitHubIssueResponse>(issueJson);
                            if (issue != null && issue.number > 0)
                            {
                                // ラベルを手動で抽出
                                var labels = ExtractLabelsFromJson(issueJson);
                                issues.Add(new ParsedIssueData { issue = issue, labels = labels });
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[Melpomene] Failed to parse issue JSON: {e.Message}");
                        }
                        start = -1;
                    }
                }
            }

            return issues;
        }

        /// <summary>
        /// コメントJSON配列をパースする
        /// </summary>
        private List<MelpomeneComment> ParseCommentArray(string jsonArray)
        {
            var comments = new List<MelpomeneComment>();

            if (string.IsNullOrEmpty(jsonArray) || jsonArray.Trim() == "[]")
            {
                return comments;
            }

            // 手動でパース（JsonUtilityは配列に対応していない）
            int depth = 0;
            int start = -1;
            bool inString = false;
            bool escape = false;

            for (int i = 0; i < jsonArray.Length; i++)
            {
                char c = jsonArray[i];

                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString) continue;

                if (c == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        var commentJson = jsonArray.Substring(start, i - start + 1);
                        try
                        {
                            var response = JsonUtility.FromJson<GitHubCommentResponse>(commentJson);
                            if (response != null && response.id > 0)
                            {
                                // userオブジェクトからloginを抽出
                                string userName = ExtractUserLoginFromJson(commentJson);

                                var comment = new MelpomeneComment
                                {
                                    id = response.id,
                                    body = response.body,
                                    userName = userName,
                                    createdAt = response.created_at,
                                    updatedAt = response.updated_at
                                };
                                comments.Add(comment);
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[Melpomene] Failed to parse comment JSON: {e.Message}");
                        }
                        start = -1;
                    }
                }
            }

            return comments;
        }

        /// <summary>
        /// JSONからuser.loginを抽出
        /// </summary>
        private string ExtractUserLoginFromJson(string json)
        {
            // "user":{...,"login":"xxx",...} を探す
            int userStart = json.IndexOf("\"user\":");
            if (userStart < 0) return "unknown";

            int loginPos = json.IndexOf("\"login\":", userStart);
            if (loginPos < 0) return "unknown";

            int valueStart = json.IndexOf('"', loginPos + 8);
            if (valueStart < 0) return "unknown";

            int valueEnd = json.IndexOf('"', valueStart + 1);
            if (valueEnd < 0) return "unknown";

            return json.Substring(valueStart + 1, valueEnd - valueStart - 1);
        }

        /// <summary>
        /// GitHubにマイルストーンを作成する
        /// </summary>
        public async UniTask<GitHubMilestone> CreateMilestoneAsync(string title, string dueDate, string description = "")
        {
            if (!config.IsValid)
            {
                Debug.LogWarning("[Melpomene] Config is not valid. Please set repository and access token.");
                return null;
            }

            var url = $"{config.ApiBaseUrl}/milestones";

            // リクエストボディを構築
            var requestBody = new GitHubMilestoneCreateRequest
            {
                title = title,
                description = description ?? ""
            };

            // 期限が設定されている場合はISO 8601形式（UTC）に変換
            if (!string.IsNullOrEmpty(dueDate) && DateTimeOffset.TryParse(dueDate, out DateTimeOffset dto))
            {
                // ローカル時間をUTCに変換してから出力
                requestBody.due_on = dto.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            }

            var json = JsonUtility.ToJson(requestBody);
            var bodyBytes = Encoding.UTF8.GetBytes(json);

            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {config.accessToken}");
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.SetRequestHeader("User-Agent", "Melpomene-Unity");

                try
                {
                    await request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var response = JsonUtility.FromJson<GitHubMilestoneResponse>(request.downloadHandler.text);
                        var milestone = new GitHubMilestone
                        {
                            number = response.number,
                            title = response.title,
                            description = response.description,
                            dueOn = response.due_on,
                            state = response.state
                        };
                        Debug.Log($"[Melpomene] Milestone created: #{response.number} - {response.title}");
                        return milestone;
                    }
                    else
                    {
                        Debug.LogError($"[Melpomene] Failed to create milestone: {request.error}\n{request.downloadHandler.text}");
                        return null;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Melpomene] Exception creating milestone: {e.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// GitHubからマイルストーン一覧を取得する
        /// </summary>
        public async UniTask<List<GitHubMilestone>> GetMilestonesAsync()
        {
            if (!config.IsValid)
            {
                Debug.LogWarning("[Melpomene] Config is not valid. Please set repository and access token.");
                return new List<GitHubMilestone>();
            }

            var milestones = new List<GitHubMilestone>();
            var url = $"{config.ApiBaseUrl}/milestones?state=open&per_page=100";

            Debug.Log($"[Melpomene] Fetching milestones from: {url}");

            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Authorization", $"Bearer {config.accessToken}");
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.SetRequestHeader("User-Agent", "Melpomene-Unity");

                try
                {
                    await request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var jsonArray = request.downloadHandler.text;
                        milestones = ParseMilestoneArray(jsonArray);
                        Debug.Log($"[Melpomene] Fetched {milestones.Count} milestones from GitHub");
                    }
                    else
                    {
                        Debug.LogError($"[Melpomene] Failed to fetch milestones: {request.error}\nResponse: {request.downloadHandler.text}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Melpomene] Exception fetching milestones: {e.Message}");
                }
            }

            return milestones;
        }

        /// <summary>
        /// マイルストーン付きでIssueを作成する
        /// </summary>
        public async UniTask<MelpomeneTicket> CreateIssueWithMilestoneAsync(MelpomeneTicket ticket, int? milestoneNumber)
        {
            if (!config.IsValid)
            {
                Debug.LogWarning("[Melpomene] Config is not valid. Please set repository and access token.");
                return null;
            }

            var url = $"{config.ApiBaseUrl}/issues";

            // ラベルを構築
            var labels = new List<string>(config.defaultLabels);
            if (!string.IsNullOrEmpty(ticket.labels))
            {
                labels.AddRange(ticket.labels.Split(','));
            }
            labels.Add(ticket.priority.ToString().ToLower());
            labels.Add(ticket.category.ToString().ToLower());

            // リクエストボディを構築（マイルストーン付き）
            string json;
            if (milestoneNumber.HasValue && milestoneNumber.Value > 0)
            {
                var requestBody = new GitHubIssueRequestWithMilestone
                {
                    title = ticket.GenerateIssueTitle(),
                    body = ticket.GenerateIssueBody(),
                    labels = labels.ToArray(),
                    milestone = milestoneNumber.Value
                };
                json = JsonUtility.ToJson(requestBody);
            }
            else
            {
                var requestBody = new GitHubIssueRequest
                {
                    title = ticket.GenerateIssueTitle(),
                    body = ticket.GenerateIssueBody(),
                    labels = labels.ToArray()
                };
                json = JsonUtility.ToJson(requestBody);
            }

            var bodyBytes = Encoding.UTF8.GetBytes(json);

            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {config.accessToken}");
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.SetRequestHeader("User-Agent", "Melpomene-Unity");

                try
                {
                    await request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var response = JsonUtility.FromJson<GitHubIssueResponse>(request.downloadHandler.text);
                        ticket.issueNumber = response.number;
                        ticket.issueUrl = response.html_url;
                        ticket.state = response.state;
                        Debug.Log($"[Melpomene] Issue created: #{response.number} - {response.html_url}");
                        return ticket;
                    }
                    else
                    {
                        Debug.LogError($"[Melpomene] Failed to create issue: {request.error}\n{request.downloadHandler.text}");
                        return null;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Melpomene] Exception creating issue: {e.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// マイルストーンJSON配列をパースする
        /// </summary>
        private List<GitHubMilestone> ParseMilestoneArray(string jsonArray)
        {
            var milestones = new List<GitHubMilestone>();

            if (string.IsNullOrEmpty(jsonArray) || jsonArray.Trim() == "[]")
            {
                return milestones;
            }

            // 手動でパース（JsonUtilityは配列に対応していない）
            int depth = 0;
            int start = -1;
            bool inString = false;
            bool escape = false;

            for (int i = 0; i < jsonArray.Length; i++)
            {
                char c = jsonArray[i];

                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString) continue;

                if (c == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        var milestoneJson = jsonArray.Substring(start, i - start + 1);
                        try
                        {
                            var response = JsonUtility.FromJson<GitHubMilestoneResponse>(milestoneJson);
                            if (response != null && response.number > 0)
                            {
                                var milestone = new GitHubMilestone
                                {
                                    number = response.number,
                                    title = response.title,
                                    description = response.description,
                                    dueOn = response.due_on,
                                    state = response.state
                                };
                                milestones.Add(milestone);
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[Melpomene] Failed to parse milestone JSON: {e.Message}");
                        }
                        start = -1;
                    }
                }
            }

            return milestones;
        }

        #region Pull Request API

        /// <summary>
        /// 認証ユーザーのログイン名を取得する
        /// NOTE: PRのフィルタリングに使用
        /// </summary>
        public async UniTask<string> GetAuthenticatedUserAsync()
        {
            if (!config.IsValid)
            {
                Debug.LogWarning("[Melpomene] Config is not valid.");
                return null;
            }

            // NOTE: /user エンドポイントはリポジトリに依存しないため、ベースURLから構築
            var baseUrl = config.ApiBaseUrl;
            var reposIndex = baseUrl.IndexOf("/repos/");
            if (reposIndex < 0)
            {
                Debug.LogError("[Melpomene] Invalid ApiBaseUrl format: missing '/repos/' segment");
                return null;
            }
            var apiRoot = baseUrl.Substring(0, reposIndex);
            var url = $"{apiRoot}/user";

            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Authorization", $"Bearer {config.accessToken}");
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.SetRequestHeader("User-Agent", "Melpomene-Unity");

                try
                {
                    await request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        var login = ExtractStringField(request.downloadHandler.text, "login");
                        Debug.Log($"[Melpomene] Authenticated user: {login}");
                        return login;
                    }
                    else
                    {
                        Debug.LogError($"[Melpomene] Failed to get authenticated user: {request.error}");
                        return null;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Melpomene] Exception getting authenticated user: {e.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// オープンPR一覧を取得する
        /// </summary>
        public async UniTask<List<GitHubPullRequest>> GetPullRequestsAsync()
        {
            if (!config.IsValid)
            {
                Debug.LogWarning("[Melpomene] Config is not valid.");
                return new List<GitHubPullRequest>();
            }

            var pullRequests = new List<GitHubPullRequest>();
            var url = $"{config.ApiBaseUrl}/pulls?state=open&per_page=100";

            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Authorization", $"Bearer {config.accessToken}");
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.SetRequestHeader("User-Agent", "Melpomene-Unity");

                try
                {
                    await request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        pullRequests = ParsePullRequestArray(request.downloadHandler.text);
                        Debug.Log($"[Melpomene] Fetched {pullRequests.Count} pull requests");
                    }
                    else
                    {
                        Debug.LogError($"[Melpomene] Failed to fetch PRs: {request.error}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Melpomene] Exception fetching PRs: {e.Message}");
                }
            }

            return pullRequests;
        }

        /// <summary>
        /// PRのレビュー一覧を取得する
        /// </summary>
        public async UniTask<List<GitHubReview>> GetPullRequestReviewsAsync(int pullNumber)
        {
            if (!config.IsValid)
            {
                Debug.LogWarning("[Melpomene] Config is not valid.");
                return new List<GitHubReview>();
            }

            var reviews = new List<GitHubReview>();
            var url = $"{config.ApiBaseUrl}/pulls/{pullNumber}/reviews?per_page=100";

            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Authorization", $"Bearer {config.accessToken}");
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.SetRequestHeader("User-Agent", "Melpomene-Unity");

                try
                {
                    await request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        reviews = ParseReviewArray(request.downloadHandler.text);
                        Debug.Log($"[Melpomene] Fetched {reviews.Count} reviews for PR #{pullNumber}");
                    }
                    else
                    {
                        Debug.LogError($"[Melpomene] Failed to fetch reviews: {request.error}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Melpomene] Exception fetching reviews: {e.Message}");
                }
            }

            return reviews;
        }

        /// <summary>
        /// PRをマージする
        /// </summary>
        public async UniTask<bool> MergePullRequestAsync(int pullNumber, string mergeMethod = "merge")
        {
            if (!config.IsValid)
            {
                Debug.LogWarning("[Melpomene] Config is not valid.");
                return false;
            }

            var url = $"{config.ApiBaseUrl}/pulls/{pullNumber}/merge";
            var json = $"{{\"merge_method\":\"{mergeMethod}\"}}";
            var bodyBytes = Encoding.UTF8.GetBytes(json);

            using (var request = new UnityWebRequest(url, "PUT"))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyBytes);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {config.accessToken}");
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.SetRequestHeader("User-Agent", "Melpomene-Unity");

                try
                {
                    await request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        Debug.Log($"[Melpomene] PR #{pullNumber} merged successfully");
                        return true;
                    }
                    else
                    {
                        Debug.LogError($"[Melpomene] Failed to merge PR: {request.error}\n{request.downloadHandler.text}");
                        return false;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Melpomene] Exception merging PR: {e.Message}");
                    return false;
                }
            }
        }

        /// <summary>
        /// PR配列をパースする
        /// </summary>
        private List<GitHubPullRequest> ParsePullRequestArray(string jsonArray)
        {
            var pullRequests = new List<GitHubPullRequest>();

            if (string.IsNullOrEmpty(jsonArray) || jsonArray.Trim() == "[]")
            {
                return pullRequests;
            }

            int depth = 0;
            int start = -1;
            bool inString = false;
            bool escape = false;

            for (int i = 0; i < jsonArray.Length; i++)
            {
                char c = jsonArray[i];

                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString) continue;

                if (c == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        var prJson = jsonArray.Substring(start, i - start + 1);
                        try
                        {
                            var pr = new GitHubPullRequest
                            {
                                number = ExtractIntField(prJson, "number"),
                                title = ExtractStringField(prJson, "title"),
                                state = ExtractStringField(prJson, "state"),
                                html_url = ExtractStringField(prJson, "html_url"),
                                updated_at = ExtractStringField(prJson, "updated_at"),
                                draft = ExtractBoolField(prJson, "draft"),
                                mergeable_state = ExtractStringField(prJson, "mergeable_state")
                            };

                            // ネストしたオブジェクトからフィールドを抽出
                            pr.user_login = ExtractNestedStringField(prJson, "user", "login");
                            pr.head_ref = ExtractNestedStringField(prJson, "head", "ref");
                            pr.base_ref = ExtractNestedStringField(prJson, "base", "ref");

                            if (pr.number > 0)
                            {
                                pullRequests.Add(pr);
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[Melpomene] Failed to parse PR JSON: {e.Message}");
                        }
                        start = -1;
                    }
                }
            }

            return pullRequests;
        }

        /// <summary>
        /// レビュー配列をパースする
        /// </summary>
        private List<GitHubReview> ParseReviewArray(string jsonArray)
        {
            var reviews = new List<GitHubReview>();

            if (string.IsNullOrEmpty(jsonArray) || jsonArray.Trim() == "[]")
            {
                return reviews;
            }

            int depth = 0;
            int start = -1;
            bool inString = false;
            bool escape = false;

            for (int i = 0; i < jsonArray.Length; i++)
            {
                char c = jsonArray[i];

                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString) continue;

                if (c == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        var reviewJson = jsonArray.Substring(start, i - start + 1);
                        try
                        {
                            var review = new GitHubReview
                            {
                                id = ExtractLongField(reviewJson, "id"),
                                state = ExtractStringField(reviewJson, "state"),
                                body = ExtractStringField(reviewJson, "body"),
                                submitted_at = ExtractStringField(reviewJson, "submitted_at"),
                                user_login = ExtractNestedStringField(reviewJson, "user", "login")
                            };

                            if (review.id > 0)
                            {
                                reviews.Add(review);
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[Melpomene] Failed to parse review JSON: {e.Message}");
                        }
                        start = -1;
                    }
                }
            }

            return reviews;
        }

        #endregion

        #region Workflow/Actions API

        /// <summary>
        /// ワークフロー実行一覧を取得する
        /// </summary>
        public async UniTask<List<GitHubWorkflowRun>> GetWorkflowRunsAsync(string workflowFileName = null, int perPage = 10)
        {
            if (!config.IsValid)
            {
                Debug.LogWarning("[Melpomene] Config is not valid.");
                return new List<GitHubWorkflowRun>();
            }

            var runs = new List<GitHubWorkflowRun>();
            string url;

            if (!string.IsNullOrEmpty(workflowFileName))
            {
                url = $"{config.ApiBaseUrl}/actions/workflows/{workflowFileName}/runs?per_page={perPage}";
            }
            else
            {
                url = $"{config.ApiBaseUrl}/actions/runs?per_page={perPage}";
            }

            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("Authorization", $"Bearer {config.accessToken}");
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.SetRequestHeader("User-Agent", "Melpomene-Unity");

                try
                {
                    await request.SendWebRequest();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        runs = ParseWorkflowRunsFromResponse(request.downloadHandler.text);
                        Debug.Log($"[Melpomene] Fetched {runs.Count} workflow runs");
                    }
                    else
                    {
                        Debug.LogError($"[Melpomene] Failed to fetch workflow runs: {request.error}");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Melpomene] Exception fetching workflow runs: {e.Message}");
                }
            }

            return runs;
        }

        /// <summary>
        /// ワークフロー実行レスポンスをパースする
        /// NOTE: レスポンスは {"total_count": N, "workflow_runs": [...]} 形式
        /// </summary>
        private List<GitHubWorkflowRun> ParseWorkflowRunsFromResponse(string json)
        {
            var runs = new List<GitHubWorkflowRun>();

            // workflow_runs配列を抽出
            int runsStart = json.IndexOf("\"workflow_runs\":");
            if (runsStart < 0) return runs;

            int arrayStart = json.IndexOf('[', runsStart);
            if (arrayStart < 0) return runs;

            // 配列の終わりを見つける
            int depth = 0;
            int arrayEnd = -1;
            bool inString = false;
            bool escape = false;

            for (int i = arrayStart; i < json.Length; i++)
            {
                char c = json[i];

                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString) continue;

                if (c == '[') depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        arrayEnd = i;
                        break;
                    }
                }
            }

            if (arrayEnd < 0) return runs;

            string runsArrayJson = json.Substring(arrayStart, arrayEnd - arrayStart + 1);
            return ParseWorkflowRunArray(runsArrayJson);
        }

        /// <summary>
        /// ワークフロー実行配列をパースする
        /// </summary>
        private List<GitHubWorkflowRun> ParseWorkflowRunArray(string jsonArray)
        {
            var runs = new List<GitHubWorkflowRun>();

            if (string.IsNullOrEmpty(jsonArray) || jsonArray.Trim() == "[]")
            {
                return runs;
            }

            int depth = 0;
            int start = -1;
            bool inString = false;
            bool escape = false;

            for (int i = 0; i < jsonArray.Length; i++)
            {
                char c = jsonArray[i];

                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString) continue;

                if (c == '{')
                {
                    if (depth == 0) start = i;
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0 && start >= 0)
                    {
                        var runJson = jsonArray.Substring(start, i - start + 1);
                        try
                        {
                            var run = new GitHubWorkflowRun
                            {
                                id = ExtractLongField(runJson, "id"),
                                name = ExtractStringField(runJson, "name"),
                                status = ExtractStringField(runJson, "status"),
                                conclusion = ExtractStringField(runJson, "conclusion"),
                                html_url = ExtractStringField(runJson, "html_url"),
                                head_branch = ExtractStringField(runJson, "head_branch"),
                                created_at = ExtractStringField(runJson, "created_at"),
                                updated_at = ExtractStringField(runJson, "updated_at"),
                                run_number = ExtractIntField(runJson, "run_number")
                            };

                            if (run.id > 0)
                            {
                                runs.Add(run);
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[Melpomene] Failed to parse workflow run JSON: {e.Message}");
                        }
                        start = -1;
                    }
                }
            }

            return runs;
        }

        #endregion

        #region JSON Helper Methods

        /// <summary>
        /// JSONから文字列フィールドを抽出する
        /// </summary>
        private string ExtractStringField(string json, string fieldName)
        {
            string pattern = $"\"{fieldName}\":";
            int fieldStart = json.IndexOf(pattern);
            if (fieldStart < 0) return null;

            int valueStart = fieldStart + pattern.Length;
            // 空白をスキップ
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
            {
                valueStart++;
            }

            if (valueStart >= json.Length) return null;

            // null チェック
            if (json.Substring(valueStart, Math.Min(4, json.Length - valueStart)) == "null")
            {
                return null;
            }

            if (json[valueStart] != '"') return null;

            int valueEnd = valueStart + 1;
            bool escape = false;
            while (valueEnd < json.Length)
            {
                if (escape)
                {
                    escape = false;
                    valueEnd++;
                    continue;
                }
                if (json[valueEnd] == '\\')
                {
                    escape = true;
                    valueEnd++;
                    continue;
                }
                if (json[valueEnd] == '"')
                {
                    break;
                }
                valueEnd++;
            }

            return json.Substring(valueStart + 1, valueEnd - valueStart - 1);
        }

        /// <summary>
        /// JSONから整数フィールドを抽出する
        /// </summary>
        private int ExtractIntField(string json, string fieldName)
        {
            string pattern = $"\"{fieldName}\":";
            int fieldStart = json.IndexOf(pattern);
            if (fieldStart < 0) return 0;

            int valueStart = fieldStart + pattern.Length;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
            {
                valueStart++;
            }

            int valueEnd = valueStart;
            while (valueEnd < json.Length && (char.IsDigit(json[valueEnd]) || json[valueEnd] == '-'))
            {
                valueEnd++;
            }

            if (valueEnd > valueStart && int.TryParse(json.Substring(valueStart, valueEnd - valueStart), out int result))
            {
                return result;
            }
            return 0;
        }

        /// <summary>
        /// JSONからlong型フィールドを抽出する
        /// </summary>
        private long ExtractLongField(string json, string fieldName)
        {
            string pattern = $"\"{fieldName}\":";
            int fieldStart = json.IndexOf(pattern);
            if (fieldStart < 0) return 0;

            int valueStart = fieldStart + pattern.Length;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
            {
                valueStart++;
            }

            int valueEnd = valueStart;
            while (valueEnd < json.Length && (char.IsDigit(json[valueEnd]) || json[valueEnd] == '-'))
            {
                valueEnd++;
            }

            if (valueEnd > valueStart && long.TryParse(json.Substring(valueStart, valueEnd - valueStart), out long result))
            {
                return result;
            }
            return 0;
        }

        /// <summary>
        /// JSONからboolフィールドを抽出する
        /// </summary>
        private bool ExtractBoolField(string json, string fieldName)
        {
            string pattern = $"\"{fieldName}\":";
            int fieldStart = json.IndexOf(pattern);
            if (fieldStart < 0) return false;

            int valueStart = fieldStart + pattern.Length;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart]))
            {
                valueStart++;
            }

            if (json.Substring(valueStart, Math.Min(4, json.Length - valueStart)) == "true")
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// ネストしたオブジェクトから文字列フィールドを抽出する
        /// </summary>
        private string ExtractNestedStringField(string json, string objectName, string fieldName)
        {
            string pattern = $"\"{objectName}\":";
            int objStart = json.IndexOf(pattern);
            if (objStart < 0) return null;

            int braceStart = json.IndexOf('{', objStart);
            if (braceStart < 0) return null;

            // オブジェクトの終わりを見つける
            int depth = 0;
            int braceEnd = -1;
            bool inString = false;
            bool escape = false;

            for (int i = braceStart; i < json.Length; i++)
            {
                char c = json[i];

                if (escape)
                {
                    escape = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escape = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString) continue;

                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        braceEnd = i;
                        break;
                    }
                }
            }

            if (braceEnd < 0) return null;

            string nestedJson = json.Substring(braceStart, braceEnd - braceStart + 1);
            return ExtractStringField(nestedJson, fieldName);
        }

        #endregion

        #region JSON Data Classes

        [Serializable]
        private class GitHubIssueRequest
        {
            public string title;
            public string body;
            public string[] labels;
        }

        [Serializable]
        private class GitHubIssueRequestWithMilestone
        {
            public string title;
            public string body;
            public string[] labels;
            public int milestone;
        }

        [Serializable]
        private class GitHubMilestoneCreateRequest
        {
            public string title;
            public string description;
            public string due_on;
        }

        [Serializable]
        private class GitHubMilestoneResponse
        {
            public int number;
            public string title;
            public string description;
            public string due_on;
            public string state;
        }

        [Serializable]
        private class GitHubIssueUpdateRequest
        {
            public string title;
            public string body;
        }

        [Serializable]
        private class GitHubIssueResponse
        {
            public int number;
            public string title;
            public string body;
            public string html_url;
            public string state;
            public string created_at;
            // NOTE: labelsはJsonUtilityでネストした配列をパースできないため、手動でパースする
        }

        [Serializable]
        private class GitHubCommentRequest
        {
            public string body;
        }

        [Serializable]
        private class GitHubCommentResponse
        {
            // NOTE: GitHub fullDatabaseIdはint範囲を超える可能性があるためlong型
            public long id;
            public string body;
            public string created_at;
            public string updated_at;
            // NOTE: userはネストしたオブジェクトなので手動でパースする
        }

        #endregion

        /// <summary>
        /// JSONからラベル名の配列を手動で抽出する
        /// NOTE: JsonUtilityはネストした配列をパースできないため
        /// </summary>
        private string[] ExtractLabelsFromJson(string issueJson)
        {
            var labels = new List<string>();

            // "labels":[...] を探す
            int labelsStart = issueJson.IndexOf("\"labels\":");
            if (labelsStart < 0)
                return labels.ToArray();

            int arrayStart = issueJson.IndexOf('[', labelsStart);
            if (arrayStart < 0)
                return labels.ToArray();

            int arrayEnd = issueJson.IndexOf(']', arrayStart);
            if (arrayEnd < 0)
                return labels.ToArray();

            string labelsSection = issueJson.Substring(arrayStart, arrayEnd - arrayStart + 1);

            // "name":"xxx" を探す
            int searchPos = 0;
            while (true)
            {
                int namePos = labelsSection.IndexOf("\"name\":", searchPos);
                if (namePos < 0)
                    break;

                int valueStart = labelsSection.IndexOf('"', namePos + 7);
                if (valueStart < 0)
                    break;

                int valueEnd = labelsSection.IndexOf('"', valueStart + 1);
                if (valueEnd < 0)
                    break;

                string labelName = labelsSection.Substring(valueStart + 1, valueEnd - valueStart - 1);
                labels.Add(labelName);

                searchPos = valueEnd + 1;
            }

            return labels.ToArray();
        }
    }
}
#endif
