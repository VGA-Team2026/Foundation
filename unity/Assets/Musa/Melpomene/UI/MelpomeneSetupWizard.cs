#if UNITY_EDITOR
using System;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Melpomene
{
    /// <summary>
    /// Melpomene初回セットアップウィザード
    /// NOTE: PAT未設定かつスキップ設定がない場合にエディタ起動時に自動表示
    /// </summary>
    public class MelpomeneSetupWizard : EditorWindow
    {
        private const string PAT_GUIDE_URL =
            "https://candle-stoplight-544.notion.site/Personal-Access-Token-30b39cbfbab980f2a13deef36c291dc4";

        private int currentPage;
        private bool skipWizard;
        private string patInput = "";

        // PAT検証
        private bool isValidating;
        private string validationError;

        // ページ3: 確認
        private string fetchedUserName;
        private string userNameInput;
        private bool ghAvailable;
        private string ghVersion;
        private bool gitAvailable;
        private string gitVersion;
        private bool nodeAvailable;
        private string nodeVersion;

        [Serializable]
        private class GitHubUser
        {
            public string login;
        }

        #region Auto Launch

        [InitializeOnLoadMethod]
        private static void CheckSetupOnLoad()
        {
            EditorApplication.delayCall += () =>
            {
                var config = MelpomeneConfig.GetOrCreateConfig();
                if (config.skipSetupWizard) return;

                // NOTE: PAT・リポジトリ情報が未設定なら即ウィザード表示
                if (!config.IsValid)
                {
                    ShowWizard();
                    return;
                }

                // NOTE: 設定値が揃っていてもIssue取得できるか検証してからスキップ判定
                ValidateIssueAccess(config);
            };
        }

        /// <summary>
        /// GitHub Issues APIへ軽量リクエストを送り、取得可能か検証
        /// NOTE: 失敗時のみウィザードを表示する
        /// </summary>
        private static void ValidateIssueAccess(MelpomeneConfig config)
        {
            var url = $"{config.ApiBaseUrl}/issues?state=open&per_page=1";
            var request = UnityWebRequest.Get(url);
            request.SetRequestHeader("Authorization", $"Bearer {config.accessToken}");
            request.SetRequestHeader("Accept", "application/vnd.github+json");
            request.SetRequestHeader("User-Agent", "Melpomene-Unity");

            var operation = request.SendWebRequest();
            operation.completed += _ =>
            {
                bool success = request.result == UnityWebRequest.Result.Success
                    && request.responseCode == 200;
                request.Dispose();

                if (!success)
                {
                    ShowWizard();
                }
            };
        }

        private static void ShowWizard()
        {
            var window = GetWindow<MelpomeneSetupWizard>(true, "Melpomene セットアップ");
            window.minSize = new Vector2(450, 350);
            window.maxSize = new Vector2(450, 350);
            window.ShowUtility();
        }

        #endregion

        private void OnGUI()
        {
            switch (currentPage)
            {
                case 0:
                    DrawIntroPage();
                    break;
                case 1:
                    DrawPatPage();
                    break;
                case 2:
                    DrawConfirmPage();
                    break;
            }

            // ページインジケーター
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label($"{currentPage + 1} / 3", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }

        /// <summary>
        /// ページ 1/3: 導入説明
        /// </summary>
        private void DrawIntroPage()
        {
            EditorGUILayout.Space(16);

            // タイトル
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };
            GUILayout.Label("Melpomene セットアップ", titleStyle);

            EditorGUILayout.Space(16);

            // 説明文
            var descStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
            GUILayout.Label("デバッグツールのGitHub連携設定をします。\n設定をしない場合はチェックを入れてください。", descStyle);

            EditorGUILayout.Space(24);

            // スキップチェックボックス
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            skipWizard = GUILayout.Toggle(skipWizard, "設定をスキップする（今後このウインドウを表示しない）");
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(16);

            // ボタン
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (skipWizard)
            {
                if (GUILayout.Button("閉じる", GUILayout.Width(100)))
                {
                    var config = MelpomeneConfig.GetOrCreateConfig();
                    config.skipSetupWizard = true;
                    config.SaveLocalSettings();
                    Close();
                }
            }
            else
            {
                if (GUILayout.Button("Next >", GUILayout.Width(100)))
                {
                    currentPage = 1;
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// ページ 2/3: PAT設定
        /// </summary>
        private void DrawPatPage()
        {
            EditorGUILayout.Space(16);

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };
            GUILayout.Label("Personal Access Token 設定", titleStyle);

            EditorGUILayout.Space(16);

            // PAT生成手順リンク
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("PAT生成手順を開く", GUILayout.Width(200)))
            {
                Application.OpenURL(PAT_GUIDE_URL);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(16);

            // PAT入力
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(40);
            EditorGUILayout.LabelField("PAT", GUILayout.Width(30));
            patInput = EditorGUILayout.PasswordField(patInput);
            GUILayout.Space(40);
            EditorGUILayout.EndHorizontal();

            // エラーメッセージ
            if (!string.IsNullOrEmpty(validationError))
            {
                EditorGUILayout.Space(8);
                var errorStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
                {
                    normal = { textColor = Color.red },
                    alignment = TextAnchor.MiddleCenter
                };
                GUILayout.Label(validationError, errorStyle);
            }

            // 検証中表示
            if (isValidating)
            {
                EditorGUILayout.Space(8);
                var validatingStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
                {
                    alignment = TextAnchor.MiddleCenter
                };
                GUILayout.Label("検証中...", validatingStyle);
            }

            EditorGUILayout.Space(24);

            // ナビゲーションボタン
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledGroupScope(isValidating))
            {
                if (GUILayout.Button("< Back", GUILayout.Width(100)))
                {
                    currentPage = 0;
                    validationError = null;
                }
            }

            GUILayout.Space(8);

            using (new EditorGUI.DisabledGroupScope(string.IsNullOrEmpty(patInput) || isValidating))
            {
                if (GUILayout.Button("Next >", GUILayout.Width(100)))
                {
                    ValidatePatAsync();
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// GitHub APIでPATを検証し、ユーザー名を取得
        /// </summary>
        private void ValidatePatAsync()
        {
            isValidating = true;
            validationError = null;

            var request = UnityWebRequest.Get("https://api.github.com/user");
            request.SetRequestHeader("Authorization", $"Bearer {patInput}");
            request.SetRequestHeader("User-Agent", "Melpomene-Unity");

            var operation = request.SendWebRequest();
            operation.completed += _ =>
            {
                try
                {
                    if (request.responseCode == 200)
                    {
                        var user = JsonUtility.FromJson<GitHubUser>(request.downloadHandler.text);
                        fetchedUserName = user.login;
                        userNameInput = user.login;
                        validationError = null;
                        CheckCliAvailability();
                        currentPage = 2;
                    }
                    else if (request.responseCode == 401 || request.responseCode == 403)
                    {
                        validationError = "PATが無効です。トークンを確認してください。";
                    }
                    else if (request.result == UnityWebRequest.Result.ConnectionError)
                    {
                        validationError = $"接続に失敗しました: {request.error}";
                    }
                    else
                    {
                        validationError = $"接続に失敗しました: {request.error} (HTTP {request.responseCode})";
                    }
                }
                finally
                {
                    isValidating = false;
                    request.Dispose();
                    Repaint();
                }
            };
        }

        /// <summary>
        /// ページ 3/3: 設定確認
        /// </summary>
        private void DrawConfirmPage()
        {
            EditorGUILayout.Space(16);

            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };
            GUILayout.Label("設定確認", titleStyle);

            EditorGUILayout.Space(16);

            // ユーザー名セクション
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(40);
            EditorGUILayout.LabelField("ユーザー名", GUILayout.Width(70));
            userNameInput = EditorGUILayout.TextField(userNameInput);
            GUILayout.Space(40);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(16);

            // 環境チェックセクション
            var sectionStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12
            };
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(40);
            GUILayout.Label("環境チェック", sectionStyle);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // GitHub CLI
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(50);
            if (ghAvailable)
            {
                EditorGUILayout.LabelField($"GitHub CLI: \u2713 ({ghVersion})");
            }
            else
            {
                var warnStyle = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = new Color(0.8f, 0.5f, 0f) }
                };
                EditorGUILayout.LabelField("GitHub CLI: \u2717 (見つかりません)", warnStyle);
            }
            GUILayout.Space(40);
            EditorGUILayout.EndHorizontal();

            // Git
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(50);
            if (gitAvailable)
            {
                EditorGUILayout.LabelField($"Git: \u2713 ({gitVersion})");
            }
            else
            {
                var warnStyle = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = new Color(0.8f, 0.5f, 0f) }
                };
                EditorGUILayout.LabelField("Git: \u2717 (見つかりません)", warnStyle);
            }
            GUILayout.Space(40);
            EditorGUILayout.EndHorizontal();

            // Node.js
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(50);
            if (nodeAvailable)
            {
                EditorGUILayout.LabelField($"Node.js: \u2713 ({nodeVersion})");
            }
            else
            {
                var warnStyle = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = new Color(0.8f, 0.5f, 0f) }
                };
                EditorGUILayout.LabelField("Node.js: \u2717 (見つかりません)", warnStyle);
            }
            GUILayout.Space(40);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(24);

            // ナビゲーションボタン
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("< Back", GUILayout.Width(100)))
            {
                currentPage = 1;
            }

            GUILayout.Space(8);

            using (new EditorGUI.DisabledGroupScope(string.IsNullOrEmpty(userNameInput)))
            {
                if (GUILayout.Button("完了", GUILayout.Width(100)))
                {
                    var config = MelpomeneConfig.GetOrCreateConfig();
                    config.accessToken = patInput;
                    config.defaultUserName = userNameInput;
                    config.SaveLocalSettings();
                    Close();
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// gh CLI / git の利用可否をチェック
        /// </summary>
        private void CheckCliAvailability()
        {
            (ghAvailable, ghVersion) = CheckCommand("gh", "--version");
            (gitAvailable, gitVersion) = CheckCommand("git", "--version");
            (nodeAvailable, nodeVersion) = CheckCommand("node", "--version");
        }

        private static (bool available, string version) CheckCommand(string command, string args)
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
                    process.WaitForExit();
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
}
#endif
