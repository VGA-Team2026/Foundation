using System.Reflection;
using Foundation.Threading;
using UnityEditor;
using UnityEngine;

/// <summary>
/// MonoBehaviourのInspectorヘッダーにスレッディングモデルのバッジを自動表示する
/// [ThreadingModel]アトリビュートが付与されたコンポーネントにカラーバッジを描画
///
/// 色分け:
///   緑 = MultiThreaded（Job System並列処理）
///   黄 = AsyncCapable（非同期処理対応）
///   灰 = MainThreadOnly（メインスレッドのみ）
///
/// InitializeOnLoadで全Inspectorに自動適用されるため、
/// DefaultEditorやカスタムエディタと競合しない
/// </summary>
[InitializeOnLoad]
public static class ThreadingModelHeaderGUI
{
    static ThreadingModelHeaderGUI()
    {
        Editor.finishedDefaultHeaderGUI += OnHeaderGUI;
    }

    static void OnHeaderGUI(Editor editor)
    {
        if (editor.target == null) return;
        if (!(editor.target is MonoBehaviour)) return;

        var attr = editor.target.GetType().GetCustomAttribute<ThreadingModelAttribute>(true);
        if (attr == null) return;

        DrawThreadingBadge(attr);
    }

    static void DrawThreadingBadge(ThreadingModelAttribute attr)
    {
        Color badgeColor;
        string label;
        string icon;

        switch (attr.Type)
        {
            case ThreadingType.MultiThreaded:
                badgeColor = new Color(0.2f, 0.7f, 0.3f, 1f);
                label = "Multi-Threaded";
                icon = "\u2726"; // diamond star
                break;
            case ThreadingType.AsyncCapable:
                badgeColor = new Color(0.9f, 0.7f, 0.1f, 1f);
                label = "Async Capable";
                icon = "\u21BB"; // clockwise arrow
                break;
            default:
                badgeColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                label = "Main Thread Only";
                icon = "\u2501"; // horizontal line
                break;
        }

        var rect = EditorGUILayout.GetControlRect(false, 22);

        // バッジ背景
        var prevColor = GUI.backgroundColor;
        GUI.backgroundColor = badgeColor;
        GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
        GUI.backgroundColor = prevColor;

        // バッジテキスト
        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white },
            fontSize = 11
        };

        string text = $"{icon} {label}";
        if (!string.IsNullOrEmpty(attr.Description))
        {
            text += $"  -  {attr.Description}";
        }

        EditorGUI.LabelField(rect, text, style);
    }
}
