#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Cysharp.Threading.Tasks;

namespace Melpomene
{
    /// <summary>
    /// Melpomeneチケットのコンテキストメニュー表示ユーティリティ
    /// NOTE: チケット右クリックメニューの表示を担当
    /// </summary>
    public static class MelpomeneGizmoDrawer
    {

        /// <summary>
        /// チケットのコンテキストメニューを表示
        /// </summary>
        public static void ShowTicketPopup(MelpomeneTicket ticket, Vector2 position)
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent($"#{ticket.issueNumber}: {ticket.title}"), false, null);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Open in Browser"), false, () =>
            {
                if (!string.IsNullOrEmpty(ticket.issueUrl))
                {
                    Application.OpenURL(ticket.issueUrl);
                }
            });
            menu.AddItem(new GUIContent("Copy URL"), false, () =>
            {
                GUIUtility.systemCopyBuffer = ticket.issueUrl;
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("View Details"), false, () =>
            {
                MelpomeneTicketDetailWindow.ShowWindow(ticket);
            });

            // 自分が作成したチケットのみクローズ可能
            if (ticket.state == "open" && IsOwnTicket(ticket))
            {
                menu.AddItem(new GUIContent("Close Issue"), false, () =>
                {
                    if (EditorUtility.DisplayDialog(
                        "チケットをクローズ",
                        $"チケット #{ticket.issueNumber} をクローズしますか？\n\n「{ticket.title}」",
                        "クローズする",
                        "キャンセル"))
                    {
                        MelpomeneManager.Instance.CloseTicketAsync(ticket.issueNumber).Forget();
                    }
                });
            }

            menu.ShowAsContext();
        }

        /// <summary>
        /// 自分が作成したチケットかどうかを判定
        /// NOTE: ticket.userNameとconfig.defaultUserNameを比較
        /// </summary>
        private static bool IsOwnTicket(MelpomeneTicket ticket)
        {
            if (ticket == null) return false;
            var config = MelpomeneManager.Instance.Config;
            return config != null &&
                   !string.IsNullOrEmpty(config.defaultUserName) &&
                   ticket.userName == config.defaultUserName;
        }
    }
}
#endif
