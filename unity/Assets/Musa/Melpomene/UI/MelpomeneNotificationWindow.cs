#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Melpomene
{
    /// <summary>
    /// Melpomene通知ウィンドウ
    /// NOTE: 通知一覧の表示と管理（スタブ実装）
    /// </summary>
    public class MelpomeneNotificationWindow : EditorWindow
    {
        public void InitializeForMusa() { }
        public void CleanupForMusa() { }

        public void DrawContent()
        {
            EditorGUILayout.HelpBox("通知機能は現在準備中です", MessageType.Info);
        }
    }
}
#endif
