#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Melpomene
{
    /// <summary>
    /// Melpomene設定ウィンドウ
    /// NOTE: Melpomene設定の表示と管理（スタブ実装）
    /// </summary>
    public class MelpomeneSettingsWindow : EditorWindow
    {
        public void InitializeForMusa() { }

        public void DrawContent()
        {
            EditorGUILayout.LabelField("Melpomene Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            if (GUILayout.Button("設定アセットを開く", GUILayout.Height(30)))
            {
                MelpomeneConfig.OpenSettings();
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("設定はMelpomeneConfigアセットで管理されています", MessageType.Info);
        }
    }
}
#endif
