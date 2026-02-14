using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace ClaudeCodeBridge
{
    /// <summary>
    /// GitHub Issue情報
    /// </summary>
    [Serializable]
    public class GitHubIssue
    {
        public int number;
        public string title;
        public string body;
        public string state;
        public List<Label> labels;
        public List<Assignee> assignees;

        [Serializable]
        public class Label
        {
            public string name;
        }

        [Serializable]
        public class Assignee
        {
            public string login;
        }
    }

    /// <summary>
    /// タスク情報
    /// </summary>
    [Serializable]
    public class TaskInfo
    {
        public string id;
        public string status;
        public string command;
        public string startTime;
        public string endTime;
        public string output;
        public string error;
    }

    /// <summary>
    /// APIレスポンス
    /// </summary>
    [Serializable]
    public class ApiResponse<T>
    {
        public bool success;
        public string error;
        public T data;
    }

    /// <summary>
    /// Claude Code Bridge サーバーと通信するクライアント
    /// </summary>
    public class ClaudeCodeBridgeClient
    {
        private readonly string baseUrl;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="host">サーバーホスト（デフォルト: localhost）</param>
        /// <param name="port">サーバーポート（デフォルト: 3456）</param>
        public ClaudeCodeBridgeClient(string host = "localhost", int port = 3456)
        {
            baseUrl = $"http://{host}:{port}";
        }

        /// <summary>
        /// サーバーのヘルスチェック
        /// </summary>
        public async Task<bool> CheckHealthAsync()
        {
            try
            {
                var response = await GetAsync("/health");
                return response.Contains("\"status\":\"ok\"");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// オープン中のIssue一覧を取得
        /// </summary>
        public async Task<List<GitHubIssue>> ListIssuesAsync()
        {
            var response = await GetAsync("/issues");
            var wrapper = JsonUtility.FromJson<IssueListResponse>(response);

            if (!wrapper.success)
            {
                throw new Exception(wrapper.error ?? "Failed to list issues");
            }

            return wrapper.issues;
        }

        /// <summary>
        /// 特定のIssue情報を取得
        /// </summary>
        public async Task<GitHubIssue> GetIssueAsync(int issueNumber)
        {
            var response = await GetAsync($"/issues/{issueNumber}");
            var wrapper = JsonUtility.FromJson<IssueResponse>(response);

            if (!wrapper.success)
            {
                throw new Exception(wrapper.error ?? "Failed to get issue");
            }

            return wrapper.issue;
        }

        /// <summary>
        /// Issueを処理してPRを作成（非同期で開始）
        /// </summary>
        /// <returns>タスクID</returns>
        public async Task<string> ProcessIssueAsync(int issueNumber)
        {
            var response = await PostAsync($"/issues/{issueNumber}/process", "{}");
            var wrapper = JsonUtility.FromJson<ProcessResponse>(response);

            if (!wrapper.success)
            {
                throw new Exception(wrapper.error ?? "Failed to process issue");
            }

            return wrapper.taskId;
        }

        /// <summary>
        /// Claude Codeコマンドを実行（非同期で開始）
        /// </summary>
        /// <param name="command">実行するコマンド</param>
        /// <returns>タスクID</returns>
        public async Task<string> ExecuteCommandAsync(string command)
        {
            var body = JsonUtility.ToJson(new ExecuteRequest { command = command });
            var response = await PostAsync("/execute", body);
            var wrapper = JsonUtility.FromJson<ProcessResponse>(response);

            if (!wrapper.success)
            {
                throw new Exception(wrapper.error ?? "Failed to execute command");
            }

            return wrapper.taskId;
        }

        /// <summary>
        /// タスクのステータスを取得
        /// </summary>
        public async Task<TaskInfo> GetTaskStatusAsync(string taskId)
        {
            var response = await GetAsync($"/tasks/{taskId}");
            var wrapper = JsonUtility.FromJson<TaskResponse>(response);

            if (!wrapper.success)
            {
                throw new Exception(wrapper.error ?? "Failed to get task status");
            }

            return wrapper.task;
        }

        /// <summary>
        /// タスク一覧を取得
        /// </summary>
        public async Task<List<TaskInfo>> ListTasksAsync()
        {
            var response = await GetAsync("/tasks");
            var wrapper = JsonUtility.FromJson<TaskListResponse>(response);

            if (!wrapper.success)
            {
                throw new Exception(wrapper.error ?? "Failed to list tasks");
            }

            return wrapper.tasks;
        }

        /// <summary>
        /// タスクをキャンセル
        /// </summary>
        public async Task<bool> CancelTaskAsync(string taskId)
        {
            var response = await PostAsync($"/tasks/{taskId}/cancel", "{}");
            var wrapper = JsonUtility.FromJson<BaseResponse>(response);
            return wrapper.success;
        }

        /// <summary>
        /// タスクの完了を待機
        /// </summary>
        /// <param name="taskId">タスクID</param>
        /// <param name="pollingInterval">ポーリング間隔（ミリ秒）</param>
        /// <param name="timeout">タイムアウト（ミリ秒）</param>
        public async Task<TaskInfo> WaitForTaskCompletionAsync(
            string taskId,
            int pollingInterval = 2000,
            int timeout = 300000)
        {
            var startTime = DateTime.Now;

            while ((DateTime.Now - startTime).TotalMilliseconds < timeout)
            {
                var task = await GetTaskStatusAsync(taskId);

                if (task.status != "running")
                {
                    return task;
                }

                await Task.Delay(pollingInterval);
            }

            throw new TimeoutException($"Task {taskId} timed out after {timeout}ms");
        }

        #region Private Methods

        private async Task<string> GetAsync(string endpoint)
        {
            using var request = UnityWebRequest.Get(baseUrl + endpoint);
            var operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new Exception($"HTTP Error: {request.error}");
            }

            return request.downloadHandler.text;
        }

        private async Task<string> PostAsync(string endpoint, string jsonBody)
        {
            using var request = new UnityWebRequest(baseUrl + endpoint, "POST");
            var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");

            var operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new Exception($"HTTP Error: {request.error}");
            }

            return request.downloadHandler.text;
        }

        #endregion

        #region Response Types

        [Serializable]
        private class BaseResponse
        {
            public bool success;
            public string error;
        }

        [Serializable]
        private class IssueListResponse : BaseResponse
        {
            public List<GitHubIssue> issues;
        }

        [Serializable]
        private class IssueResponse : BaseResponse
        {
            public GitHubIssue issue;
        }

        [Serializable]
        private class ProcessResponse : BaseResponse
        {
            public string taskId;
            public string message;
        }

        [Serializable]
        private class TaskResponse : BaseResponse
        {
            public TaskInfo task;
        }

        [Serializable]
        private class TaskListResponse : BaseResponse
        {
            public List<TaskInfo> tasks;
        }

        [Serializable]
        private class ExecuteRequest
        {
            public string command;
        }

        #endregion
    }
}
