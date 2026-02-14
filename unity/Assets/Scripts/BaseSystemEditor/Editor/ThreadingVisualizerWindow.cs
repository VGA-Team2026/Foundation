using System;
using System.Collections.Generic;
using System.Reflection;
using Foundation.Threading;
using UnityEditor;
using UnityEngine;

/// <summary>
/// シーン内の全MonoBehaviourのスレッディングモデルを一覧表示するエディタウィンドウ
/// メニュー: Window > Foundation > Threading Visualizer
/// </summary>
public class ThreadingVisualizerWindow : EditorWindow
{
    Vector2 _scrollPos;
    List<ComponentEntry> _entries = new List<ComponentEntry>();
    ThreadingType? _filterType = null;
    string _searchText = "";

    // 統計
    int _countMainThread;
    int _countAsync;
    int _countMultiThread;
    int _countNoAttribute;

    struct ComponentEntry
    {
        public MonoBehaviour Component;
        public string GameObjectName;
        public string ComponentName;
        public ThreadingModelAttribute Attribute;
        public bool HasAttribute;
    }

    [MenuItem("Window/Foundation/Threading Visualizer")]
    static void Open()
    {
        GetWindow<ThreadingVisualizerWindow>("Threading Visualizer");
    }

    void OnEnable()
    {
        ScanScene();
    }

    void OnFocus()
    {
        ScanScene();
    }

    void ScanScene()
    {
        _entries.Clear();
        _countMainThread = 0;
        _countAsync = 0;
        _countMultiThread = 0;
        _countNoAttribute = 0;

        var behaviours = FindObjectsOfType<MonoBehaviour>();
        foreach (var behaviour in behaviours)
        {
            if (behaviour == null) continue;

            var type = behaviour.GetType();
            var attr = type.GetCustomAttribute<ThreadingModelAttribute>(true);

            var entry = new ComponentEntry
            {
                Component = behaviour,
                GameObjectName = behaviour.gameObject.name,
                ComponentName = type.Name,
                Attribute = attr,
                HasAttribute = attr != null
            };

            _entries.Add(entry);

            if (attr != null)
            {
                switch (attr.Type)
                {
                    case ThreadingType.MainThreadOnly:
                        _countMainThread++;
                        break;
                    case ThreadingType.AsyncCapable:
                        _countAsync++;
                        break;
                    case ThreadingType.MultiThreaded:
                        _countMultiThread++;
                        break;
                }
            }
            else
            {
                _countNoAttribute++;
            }
        }

        _entries.Sort((a, b) =>
        {
            // アトリビュートありを先に、その中でThreadingType順
            if (a.HasAttribute != b.HasAttribute)
                return a.HasAttribute ? -1 : 1;
            if (a.HasAttribute && b.HasAttribute)
            {
                int cmp = a.Attribute.Type.CompareTo(b.Attribute.Type);
                if (cmp != 0) return -cmp; // MultiThreaded を先に
            }
            return string.Compare(a.ComponentName, b.ComponentName, StringComparison.Ordinal);
        });
    }

    void OnGUI()
    {
        DrawToolbar();
        DrawSummary();
        DrawComponentList();
    }

    void DrawToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("Scan Scene", EditorStyles.toolbarButton, GUILayout.Width(80)))
        {
            ScanScene();
        }

        GUILayout.Space(8);

        // 検索フィールド
        _searchText = EditorGUILayout.TextField(_searchText, EditorStyles.toolbarSearchField);

        GUILayout.Space(8);

        // フィルタボタン
        DrawFilterButton("All", null);
        DrawFilterButton("MT", ThreadingType.MultiThreaded);
        DrawFilterButton("Async", ThreadingType.AsyncCapable);
        DrawFilterButton("Main", ThreadingType.MainThreadOnly);

        EditorGUILayout.EndHorizontal();
    }

    void DrawFilterButton(string label, ThreadingType? type)
    {
        bool isActive = _filterType == type;
        var style = isActive ? GetActiveToolbarButtonStyle() : EditorStyles.toolbarButton;

        if (GUILayout.Button(label, style, GUILayout.Width(50)))
        {
            _filterType = isActive ? null : type;
        }
    }

    static GUIStyle GetActiveToolbarButtonStyle()
    {
        var style = new GUIStyle(EditorStyles.toolbarButton)
        {
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.2f, 0.6f, 1f) }
        };
        return style;
    }

    void DrawSummary()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

        DrawStat("Multi-Threaded", _countMultiThread, new Color(0.2f, 0.7f, 0.3f));
        DrawStat("Async", _countAsync, new Color(0.9f, 0.7f, 0.1f));
        DrawStat("Main Thread", _countMainThread, new Color(0.5f, 0.5f, 0.5f));
        DrawStat("Unmarked", _countNoAttribute, new Color(0.3f, 0.3f, 0.3f));

        EditorGUILayout.EndHorizontal();
    }

    static void DrawStat(string label, int count, Color color)
    {
        var style = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = color },
            fontStyle = FontStyle.Bold
        };

        EditorGUILayout.BeginVertical();
        EditorGUILayout.LabelField(count.ToString(), style);
        EditorGUILayout.LabelField(label, new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 9
        });
        EditorGUILayout.EndVertical();
    }

    void DrawComponentList()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        foreach (var entry in _entries)
        {
            if (!PassesFilter(entry)) continue;

            DrawComponentEntry(entry);
        }

        EditorGUILayout.EndScrollView();
    }

    bool PassesFilter(ComponentEntry entry)
    {
        // フィルタチェック
        if (_filterType.HasValue)
        {
            if (!entry.HasAttribute) return false;
            if (entry.Attribute.Type != _filterType.Value) return false;
        }

        // 検索チェック
        if (!string.IsNullOrEmpty(_searchText))
        {
            if (entry.ComponentName.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) < 0 &&
                entry.GameObjectName.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }
        }

        return true;
    }

    void DrawComponentEntry(ComponentEntry entry)
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

        // スレッディングモデルバッジ
        Color badgeColor;
        string badgeText;

        if (entry.HasAttribute)
        {
            switch (entry.Attribute.Type)
            {
                case ThreadingType.MultiThreaded:
                    badgeColor = new Color(0.2f, 0.7f, 0.3f);
                    badgeText = "MT";
                    break;
                case ThreadingType.AsyncCapable:
                    badgeColor = new Color(0.9f, 0.7f, 0.1f);
                    badgeText = "Async";
                    break;
                default:
                    badgeColor = new Color(0.5f, 0.5f, 0.5f);
                    badgeText = "Main";
                    break;
            }
        }
        else
        {
            badgeColor = new Color(0.3f, 0.3f, 0.3f);
            badgeText = "---";
        }

        // バッジ描画
        var badgeRect = EditorGUILayout.GetControlRect(false, 18, GUILayout.Width(50));
        var prevColor = GUI.backgroundColor;
        GUI.backgroundColor = badgeColor;
        GUI.Box(badgeRect, GUIContent.none, EditorStyles.helpBox);
        GUI.backgroundColor = prevColor;

        var badgeStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.white },
            fontStyle = FontStyle.Bold,
            fontSize = 10
        };
        EditorGUI.LabelField(badgeRect, badgeText, badgeStyle);

        // コンポーネント名
        EditorGUILayout.LabelField(entry.ComponentName, EditorStyles.boldLabel, GUILayout.Width(200));

        // GameObject名
        EditorGUILayout.LabelField(entry.GameObjectName, EditorStyles.miniLabel);

        // 説明文
        if (entry.HasAttribute && !string.IsNullOrEmpty(entry.Attribute.Description))
        {
            EditorGUILayout.LabelField(entry.Attribute.Description, EditorStyles.miniLabel, GUILayout.Width(200));
        }

        // 選択ボタン
        if (entry.Component != null && GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(50)))
        {
            Selection.activeGameObject = entry.Component.gameObject;
            EditorGUIUtility.PingObject(entry.Component.gameObject);
        }

        EditorGUILayout.EndHorizontal();
    }
}
