#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.IO;

namespace Melpomene
{
    /// <summary>
    /// マイルストーン情報
    /// NOTE: GitHubマイルストーンと同期可能なローカルデータ
    /// </summary>
    [Serializable]
    public class MelpomeneMilestoneInfo
    {
        /// <summary>マイルストーンID（ユニーク識別子）</summary>
        public string id;

        /// <summary>GitHubマイルストーン番号（同期時に設定）</summary>
        public int githubNumber;

        /// <summary>マイルストーンのタイトル（目的）</summary>
        public string title;

        /// <summary>期限（yyyy-MM-dd形式）</summary>
        public string dueDate;

        /// <summary>説明</summary>
        public string description;

        /// <summary>作成日時</summary>
        public string createdAt;

        /// <summary>
        /// 期限をDateTime形式で取得
        /// </summary>
        public DateTime? DueDateAsDateTime
        {
            get
            {
                if (DateTime.TryParse(dueDate, out DateTime dt))
                {
                    return dt;
                }
                return null;
            }
        }

        /// <summary>
        /// 期限切れかどうか
        /// </summary>
        public bool IsOverdue
        {
            get
            {
                var dt = DueDateAsDateTime;
                return dt.HasValue && dt.Value.Date < DateTime.Today;
            }
        }

        /// <summary>
        /// 残り日数を取得（期限切れの場合は負の値）
        /// </summary>
        public int? RemainingDays
        {
            get
            {
                var dt = DueDateAsDateTime;
                if (!dt.HasValue) return null;
                return (int)(dt.Value.Date - DateTime.Today).TotalDays;
            }
        }

        /// <summary>
        /// 表示用文字列を取得
        /// </summary>
        public string DisplayText
        {
            get
            {
                var remaining = RemainingDays;
                if (!remaining.HasValue)
                {
                    return $"{title}";
                }
                else if (remaining.Value < 0)
                {
                    return $"{title} (期限切れ: {-remaining.Value}日超過)";
                }
                else if (remaining.Value == 0)
                {
                    return $"{title} (本日期限)";
                }
                else
                {
                    return $"{title} (残り{remaining.Value}日)";
                }
            }
        }
    }

    /// <summary>
    /// マイルストーン管理用のシリアライズ可能なコンテナ
    /// </summary>
    [Serializable]
    public class MelpomeneMilestoneContainer
    {
        /// <summary>現在アクティブなマイルストーンのID（null/空の場合は未設定）</summary>
        public string currentMilestoneId;

        /// <summary>デフォルトマイルストーンのGitHub番号（新規チケット作成時に使用）</summary>
        public int defaultMilestoneNumber;

        /// <summary>登録されているマイルストーン一覧</summary>
        public List<MelpomeneMilestoneInfo> milestones = new List<MelpomeneMilestoneInfo>();
    }

    /// <summary>
    /// Melpomeneマイルストーン管理クラス
    /// NOTE: ローカルJSONファイルでマイルストーン情報を管理
    /// </summary>
    public static class MelpomeneMilestoneManager
    {
        /// <summary>マイルストーンデータのファイルパス</summary>
        private const string MILESTONE_FILE_PATH = "Assets/Melpomene/MilestoneData.json";

        /// <summary>キャッシュされたコンテナ</summary>
        private static MelpomeneMilestoneContainer cachedContainer;

        /// <summary>
        /// マイルストーンコンテナを取得（ファイルから読み込み）
        /// </summary>
        public static MelpomeneMilestoneContainer GetContainer()
        {
            if (cachedContainer != null)
            {
                return cachedContainer;
            }

            if (File.Exists(MILESTONE_FILE_PATH))
            {
                try
                {
                    var json = File.ReadAllText(MILESTONE_FILE_PATH);
                    cachedContainer = JsonUtility.FromJson<MelpomeneMilestoneContainer>(json);
                    if (cachedContainer == null)
                    {
                        cachedContainer = new MelpomeneMilestoneContainer();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Melpomene] Failed to load milestone data: {e.Message}");
                    cachedContainer = new MelpomeneMilestoneContainer();
                }
            }
            else
            {
                cachedContainer = new MelpomeneMilestoneContainer();
            }

            return cachedContainer;
        }

        /// <summary>
        /// マイルストーンコンテナを保存
        /// </summary>
        public static void SaveContainer(MelpomeneMilestoneContainer container)
        {
            try
            {
                // ディレクトリ確認
                var directory = Path.GetDirectoryName(MILESTONE_FILE_PATH);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonUtility.ToJson(container, true);
                File.WriteAllText(MILESTONE_FILE_PATH, json);

                // キャッシュを更新
                cachedContainer = container;

                Debug.Log($"[Melpomene] Milestone data saved to: {MILESTONE_FILE_PATH}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Melpomene] Failed to save milestone data: {e.Message}");
            }
        }

        /// <summary>
        /// キャッシュをクリア
        /// </summary>
        public static void ClearCache()
        {
            cachedContainer = null;
        }

        /// <summary>
        /// 現在のマイルストーンを取得
        /// </summary>
        public static MelpomeneMilestoneInfo GetCurrentMilestone()
        {
            var container = GetContainer();
            if (string.IsNullOrEmpty(container.currentMilestoneId))
            {
                return null;
            }

            return container.milestones.Find(m => m.id == container.currentMilestoneId);
        }

        /// <summary>
        /// 現在のマイルストーンを設定
        /// </summary>
        public static void SetCurrentMilestone(string milestoneId)
        {
            var container = GetContainer();
            container.currentMilestoneId = milestoneId;
            SaveContainer(container);
        }

        /// <summary>
        /// マイルストーンを追加
        /// </summary>
        public static MelpomeneMilestoneInfo AddMilestone(string title, string dueDate, string description = "")
        {
            var container = GetContainer();

            var milestone = new MelpomeneMilestoneInfo
            {
                id = Guid.NewGuid().ToString("N"),
                title = title,
                dueDate = dueDate,
                description = description,
                createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            container.milestones.Add(milestone);
            SaveContainer(container);

            return milestone;
        }

        /// <summary>
        /// マイルストーンを更新
        /// </summary>
        public static bool UpdateMilestone(string id, string title, string dueDate, string description)
        {
            var container = GetContainer();
            var milestone = container.milestones.Find(m => m.id == id);

            if (milestone == null)
            {
                return false;
            }

            milestone.title = title;
            milestone.dueDate = dueDate;
            milestone.description = description;
            SaveContainer(container);

            return true;
        }

        /// <summary>
        /// マイルストーンを削除
        /// </summary>
        public static bool RemoveMilestone(string id)
        {
            var container = GetContainer();

            // 現在のマイルストーンだった場合はクリア
            if (container.currentMilestoneId == id)
            {
                container.currentMilestoneId = "";
            }

            var removed = container.milestones.RemoveAll(m => m.id == id) > 0;

            if (removed)
            {
                SaveContainer(container);
            }

            return removed;
        }

        /// <summary>
        /// 全マイルストーンを取得（防御的コピーを返す）
        /// </summary>
        public static List<MelpomeneMilestoneInfo> GetAllMilestones()
        {
            return new List<MelpomeneMilestoneInfo>(GetContainer().milestones);
        }

        /// <summary>
        /// IDからマイルストーンを取得
        /// </summary>
        public static MelpomeneMilestoneInfo GetMilestone(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            return GetContainer().milestones.Find(m => m.id == id);
        }

        /// <summary>
        /// GitHub番号からマイルストーンを取得
        /// </summary>
        public static MelpomeneMilestoneInfo GetMilestoneByGitHubNumber(int githubNumber)
        {
            if (githubNumber <= 0)
            {
                return null;
            }

            return GetContainer().milestones.Find(m => m.githubNumber == githubNumber);
        }

        /// <summary>
        /// デフォルトマイルストーンのGitHub番号を取得
        /// </summary>
        public static int GetDefaultMilestoneNumber()
        {
            return GetContainer().defaultMilestoneNumber;
        }

        /// <summary>
        /// デフォルトマイルストーンを設定
        /// </summary>
        public static void SetDefaultMilestoneNumber(int githubNumber)
        {
            var container = GetContainer();
            container.defaultMilestoneNumber = githubNumber;
            SaveContainer(container);
        }

        /// <summary>
        /// GitHubマイルストーン一覧からローカルJSONを同期（GitHub側を正とする）
        /// </summary>
        public static void SyncFromGitHub(List<GitHubMilestone> githubMilestones)
        {
            var container = GetContainer();

            // 既存のマイルストーンをクリアして再構築
            container.milestones.Clear();

            foreach (var ghMilestone in githubMilestones)
            {
                var localMilestone = new MelpomeneMilestoneInfo
                {
                    id = $"gh_{ghMilestone.number}",
                    githubNumber = ghMilestone.number,
                    title = ghMilestone.title,
                    description = ghMilestone.description ?? "",
                    createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                };

                // 期限をパース
                if (!string.IsNullOrEmpty(ghMilestone.dueOn) && DateTime.TryParse(ghMilestone.dueOn, out DateTime dt))
                {
                    localMilestone.dueDate = dt.ToString("yyyy-MM-dd");
                }

                container.milestones.Add(localMilestone);
            }

            // デフォルトマイルストーンが存在しなくなった場合はクリア
            if (container.defaultMilestoneNumber > 0)
            {
                var exists = container.milestones.Exists(m => m.githubNumber == container.defaultMilestoneNumber);
                if (!exists)
                {
                    container.defaultMilestoneNumber = 0;
                }
            }

            SaveContainer(container);
            Debug.Log($"[Melpomene] Synced {githubMilestones.Count} milestones from GitHub");
        }
    }
}
#endif
