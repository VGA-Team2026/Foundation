using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Melpomene;
using UnityEditor;

/// <summary>
/// GitHub PR/Actions 通知サービス
/// NOTE: 10秒おきにGitHub APIをポーリングしてPRとActionsの状態を監視
/// </summary>
[InitializeOnLoad]
public class MelpomeneNotificationService
{
    /// <summary>シングルトンインスタンス</summary>
    public static MelpomeneNotificationService Instance { get; private set; }

    /// <summary>キャッシュされたPR一覧</summary>
    private List<GitHubPullRequest> cachedPullRequests = new();

    /// <summary>キャッシュされたワークフロー実行一覧</summary>
    private List<GitHubWorkflowRun> cachedWorkflowRuns = new();

    /// <summary>PR番号ごとの前回レビュー数</summary>
    private Dictionary<int, int> lastReviewCounts = new();

    /// <summary>認証ユーザー名</summary>
    private string authenticatedUser;

    /// <summary>ポーリング間隔（秒）</summary>
    private const float POLL_INTERVAL = 10f;

    /// <summary>前回ポーリング時刻</summary>
    private double lastPollTime;

    /// <summary>ポーリング中フラグ</summary>
    private bool isPolling = false;

    /// <summary>初期化済みフラグ</summary>
    private bool isInitialized = false;

    /// <summary>最後のエラーメッセージ</summary>
    private string lastError;

    /// <summary>データ更新イベント</summary>
    public event Action OnDataUpdated;

    /// <summary>新規レビュー検出イベント</summary>
    public event Action<GitHubPullRequest> OnNewReviewDetected;

    /// <summary>ワークフローステータス変更イベント</summary>
    public event Action<GitHubWorkflowRun> OnWorkflowStatusChanged;

    /// <summary>公開プロパティ: PR一覧</summary>
    public IReadOnlyList<GitHubPullRequest> PullRequests => cachedPullRequests;

    /// <summary>公開プロパティ: ワークフロー実行一覧</summary>
    public IReadOnlyList<GitHubWorkflowRun> WorkflowRuns => cachedWorkflowRuns;

    /// <summary>公開プロパティ: 認証ユーザー名</summary>
    public string AuthenticatedUser => authenticatedUser;

    /// <summary>公開プロパティ: 最後のエラー</summary>
    public string LastError => lastError;

    /// <summary>公開プロパティ: ポーリング中かどうか</summary>
    public bool IsPolling => isPolling;

    /// <summary>
    /// 静的コンストラクタ - エディタ起動時に初期化
    /// </summary>
    static MelpomeneNotificationService()
    {
        Instance = new MelpomeneNotificationService();
        EditorApplication.update += Instance.Update;
    }

    /// <summary>
    /// エディタ更新ループ
    /// </summary>
    private void Update()
    {
        // MelpomeneManager が初期化されていない場合はスキップ
        if (MelpomeneManager.Instance == null || MelpomeneManager.Instance.GitHubClient == null)
        {
            return;
        }

        // 通知ポーリングが無効の場合はスキップ
        var config = MelpomeneManager.Instance.Config;
        if (config == null || !config.enableNotificationPolling)
        {
            return;
        }

        // ポーリング間隔チェック
        if (EditorApplication.timeSinceStartup - lastPollTime >= POLL_INTERVAL)
        {
            lastPollTime = EditorApplication.timeSinceStartup;
            PollAsync().Forget();
        }
    }

    /// <summary>
    /// 非同期ポーリング処理
    /// </summary>
    private async UniTaskVoid PollAsync()
    {
        if (isPolling) return;
        isPolling = true;
        lastError = null;

        try
        {
            var client = MelpomeneManager.Instance?.GitHubClient;
            if (client == null)
            {
                lastError = "GitHubClient is not available";
                return;
            }

            // 認証ユーザー取得（初回のみ）
            if (string.IsNullOrEmpty(authenticatedUser))
            {
                authenticatedUser = await client.GetAuthenticatedUserAsync();
                if (string.IsNullOrEmpty(authenticatedUser))
                {
                    lastError = "Failed to get authenticated user";
                    return;
                }
                isInitialized = true;
            }

            // PR取得 & フィルタ（自分のPRのみ）
            var allPRs = await client.GetPullRequestsAsync();
            var myPRs = allPRs.Where(pr => pr.user_login == authenticatedUser).ToList();

            // 新規レビュー検出
            foreach (var pr in myPRs)
            {
                var reviews = await client.GetPullRequestReviewsAsync(pr.number);
                pr.reviewCount = reviews.Count;

                if (lastReviewCounts.TryGetValue(pr.number, out int lastCount))
                {
                    pr.hasNewReview = reviews.Count > lastCount;
                    if (pr.hasNewReview)
                    {
                        OnNewReviewDetected?.Invoke(pr);
                    }
                }
                lastReviewCounts[pr.number] = reviews.Count;
            }

            // クローズされたPRのレビューカウントをクリーンアップ
            var currentPRNumbers = new HashSet<int>(myPRs.Select(pr => pr.number));
            var keysToRemove = lastReviewCounts.Keys.Where(k => !currentPRNumbers.Contains(k)).ToList();
            foreach (var key in keysToRemove)
            {
                lastReviewCounts.Remove(key);
            }

            cachedPullRequests = myPRs;

            // Actions取得（UnityBuildワークフローのみ）
            var runs = await client.GetWorkflowRunsAsync("unity-build.yml");

            // ステータス変更検出
            foreach (var run in runs)
            {
                var cached = cachedWorkflowRuns.FirstOrDefault(r => r.id == run.id);
                if (cached != null && cached.status != run.status)
                {
                    OnWorkflowStatusChanged?.Invoke(run);
                }
            }
            cachedWorkflowRuns = runs;

            OnDataUpdated?.Invoke();
        }
        catch (Exception ex)
        {
            lastError = ex.Message;
            UnityEngine.Debug.LogWarning($"[MelpomeneNotificationService] Polling error: {ex.Message}");
        }
        finally
        {
            isPolling = false;
        }
    }

    /// <summary>
    /// 手動リフレッシュ
    /// </summary>
    public void ForceRefresh()
    {
        lastPollTime = 0; // 次のUpdateで即座にポーリング実行
    }

    /// <summary>
    /// 認証ユーザーをクリア（再取得用）
    /// </summary>
    public void ClearAuthenticatedUser()
    {
        authenticatedUser = null;
        isInitialized = false;
    }

    /// <summary>
    /// 初期化済みかどうか
    /// </summary>
    public bool IsInitialized => isInitialized;
}
