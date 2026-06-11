#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Document Explorer — spec/ 配下の仕様書をツリー表示・Thaleia描画するスタンドアロンウインドウ
/// </summary>
public class MusaDocumentExplorer : EditorWindow
{
    private List<MusaDocumentBrowser.DocTreeNode> fileTree = new List<MusaDocumentBrowser.DocTreeNode>();
    private int selectedCategoryIndex = -1;
    private string[] categoryNames;
    private string selectedFilePath;
    private string fileContent;
    private Vector2 sidebarScrollPosition;
    private Vector2 treeScrollPosition;
    private Vector2 contentScrollPosition;
    private ThaleiaRenderer thaleiaRenderer = new ThaleiaRenderer();

    private const float SidebarWidth = 100f;
    private const float TreePaneWidth = 180f;

    // NOTE: GUIStyleキャッシュ
    private GUIStyle sidebarButtonStyle;
    private GUIStyle sidebarActiveButtonStyle;
    private bool stylesInitialized;

    [MenuItem("Musa/Document Explorer")]
    public static void ShowWindow()
    {
        var window = GetWindow<MusaDocumentExplorer>("Document Explorer");
        window.minSize = new Vector2(600, 400);
    }

    private void OnEnable()
    {
        MusaDocumentBrowser.RefreshCache();
        categoryNames = MusaDocumentBrowser.GetCategories();
    }

    private void InitStyles()
    {
        if (stylesInitialized) return;

        sidebarButtonStyle = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleLeft,
            padding = new RectOffset(10, 4, 6, 6),
            fixedHeight = 30
        };

        sidebarActiveButtonStyle = new GUIStyle(sidebarButtonStyle)
        {
            fontStyle = FontStyle.Bold
        };
        sidebarActiveButtonStyle.normal.textColor = new Color(0.3f, 0.8f, 1f);

        stylesInitialized = true;
    }

    private void OnGUI()
    {
        InitStyles();

        EditorGUILayout.BeginHorizontal();

        // NOTE: 左サイドバー — カテゴリ一覧
        EditorGUILayout.BeginVertical(GUILayout.Width(SidebarWidth));
        DrawCategorySidebar();
        EditorGUILayout.EndVertical();

        // NOTE: セパレーター
        var sep1 = EditorGUILayout.GetControlRect(false, GUILayout.Width(1));
        EditorGUI.DrawRect(sep1, new Color(0.3f, 0.3f, 0.3f));

        // NOTE: 中央ペイン — ファイルツリー
        EditorGUILayout.BeginVertical(GUILayout.Width(TreePaneWidth));
        DrawFileTreePane();
        EditorGUILayout.EndVertical();

        // NOTE: セパレーター
        var sep2 = EditorGUILayout.GetControlRect(false, GUILayout.Width(1));
        EditorGUI.DrawRect(sep2, new Color(0.3f, 0.3f, 0.3f));

        // NOTE: 右ペイン — ドキュメント内容
        EditorGUILayout.BeginVertical();
        DrawContentPane();
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndHorizontal();
    }

    // =====================================================================
    // カテゴリサイドバー
    // =====================================================================

    private void DrawCategorySidebar()
    {
        EditorGUILayout.LabelField("Categories", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        var hasCategories = categoryNames != null && categoryNames.Length > 0;
        if (!hasCategories)
        {
            EditorGUILayout.HelpBox("spec/ が見つかりません", MessageType.Warning);
        }
        else
        {
            sidebarScrollPosition = EditorGUILayout.BeginScrollView(sidebarScrollPosition);
            for (int i = 0; i < categoryNames.Length; i++)
            {
                var style = i == selectedCategoryIndex ? sidebarActiveButtonStyle : sidebarButtonStyle;
                if (GUILayout.Button(categoryNames[i], style))
                {
                    SelectCategory(i);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        GUILayout.FlexibleSpace();

        if (GUILayout.Button("Refresh", GUILayout.Height(24)))
        {
            MusaDocumentBrowser.RefreshCache();
            categoryNames = MusaDocumentBrowser.GetCategories();
            var prevIndex = selectedCategoryIndex;
            selectedCategoryIndex = -1;
            if (prevIndex >= 0 && prevIndex < categoryNames.Length)
            {
                SelectCategory(prevIndex);
            }
            else
            {
                fileTree.Clear();
                selectedFilePath = null;
                fileContent = null;
                treeScrollPosition = Vector2.zero;
                contentScrollPosition = Vector2.zero;
            }
        }
    }

    private void SelectCategory(int index)
    {
        if (index < 0 || categoryNames == null || index >= categoryNames.Length) return;
        if (selectedCategoryIndex == index) return;

        selectedCategoryIndex = index;
        var specRoot = MusaDocumentBrowser.GetSpecRootPath();
        var categoryPath = Path.Combine(specRoot, categoryNames[index]);
        fileTree = MusaDocumentBrowser.BuildFileTree(categoryPath);

        selectedFilePath = null;
        fileContent = null;
        treeScrollPosition = Vector2.zero;
        contentScrollPosition = Vector2.zero;
    }

    // =====================================================================
    // ファイルツリーペイン
    // =====================================================================

    private void DrawFileTreePane()
    {
        EditorGUILayout.LabelField("Files", EditorStyles.boldLabel);
        EditorGUILayout.Space(2);

        if (selectedCategoryIndex < 0)
        {
            EditorGUILayout.HelpBox("カテゴリを選択", MessageType.Info);
            return;
        }

        treeScrollPosition = EditorGUILayout.BeginScrollView(treeScrollPosition);
        DrawTreeNodes(fileTree);
        EditorGUILayout.EndScrollView();
    }

    private void DrawTreeNodes(List<MusaDocumentBrowser.DocTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsDirectory)
            {
                node.IsExpanded = EditorGUILayout.Foldout(node.IsExpanded, node.Name, true);
                if (node.IsExpanded)
                {
                    EditorGUI.indentLevel++;
                    DrawTreeNodes(node.Children);
                    EditorGUI.indentLevel--;
                }
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(EditorGUI.indentLevel * 15f);

                bool isSelected = selectedFilePath == node.FullPath;
                var style = isSelected ? EditorStyles.boldLabel : EditorStyles.linkLabel;
                if (GUILayout.Button(node.Name, style, GUILayout.ExpandWidth(false)))
                {
                    selectedFilePath = node.FullPath;
                    fileContent = MusaDocumentBrowser.ReadDocContent(node.FullPath);
                    thaleiaRenderer.Parse(fileContent ?? string.Empty);
                    contentScrollPosition = Vector2.zero;
                }
                EditorGUILayout.EndHorizontal();
            }
        }
    }

    // =====================================================================
    // ドキュメント内容ペイン
    // =====================================================================

    private void DrawContentPane()
    {
        if (string.IsNullOrEmpty(selectedFilePath))
        {
            EditorGUILayout.HelpBox("ファイルを選択してください", MessageType.Info);
            return;
        }

        if (fileContent == null)
        {
            EditorGUILayout.HelpBox("ドキュメントの読み込みに失敗しました", MessageType.Warning);
            return;
        }

        contentScrollPosition = EditorGUILayout.BeginScrollView(contentScrollPosition);
        thaleiaRenderer.Draw();
        EditorGUILayout.EndScrollView();
    }
}
#endif
