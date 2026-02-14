#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Melpomene
{
    /// <summary>
    /// Melpomeneマイルストーンウィンドウ
    /// NOTE: マイルストーン管理（スタブ実装）
    /// </summary>
    public class MelpomeneMilestoneWindow : EditorWindow
    {
        public void InitializeForMusa() { }

        public void DrawContent()
        {
            EditorGUILayout.HelpBox("マイルストーン機能は現在準備中です", MessageType.Info);
        }
    }
}
#endif
