#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// スタンドアロンドッキング可能ドキュメントウインドウ
/// NOTE: Projectウインドウでのスクリプト選択、またはInspectorでのコンポーネントクリックに連動
/// NOTE: 「インスペクタから自動更新」ON時は選択GameObjectの全コンポーネントをタブ表示
/// </summary>
public class MusaDocumentWindow : EditorWindow
{
    // =====================================================================
    // インスペクタ自動更新
    // =====================================================================
    private bool autoUpdateFromInspector = true;

    // NOTE: インスペクタ連動時のタブ情報
    private class DocTab
    {
        public string ScriptName;
        public string DocPath;
        public string DocContent;
        public ThaleiaRenderer Renderer = new ThaleiaRenderer();
    }
    private List<DocTab> docTabs = new List<DocTab>();
    private int selectedTabIndex;
    private string[] tabNames = new string[0];

    // NOTE: 前回のGameObject識別用（不要な再スキャン防止）
    private int lastGameObjectInstanceId;

    // =====================================================================
    // 手動選択（インスペクタ連動OFF / Specリンククリック時）
    // =====================================================================
    private string currentScriptName;
    private string currentDocPath;
    private string currentDocContent;
    private ThaleiaRenderer singleRenderer = new ThaleiaRenderer();

    private Vector2 scrollPosition;

    [MenuItem("Musa/ドキュメント")]
    public static void ShowWindow()
    {
        var window = GetWindow<MusaDocumentWindow>("ドキュメント");
        window.minSize = new Vector2(300, 200);
    }

    private void OnEnable()
    {
        Selection.selectionChanged += OnSelectionChanged;
        Editor.finishedDefaultHeaderGUI += OnInspectorHeaderGUI;
        OnSelectionChanged();
    }

    private void OnDisable()
    {
        Selection.selectionChanged -= OnSelectionChanged;
        Editor.finishedDefaultHeaderGUI -= OnInspectorHeaderGUI;
    }

    // =====================================================================
    // 選択変更
    // =====================================================================

    private void OnSelectionChanged()
    {
        var activeObject = Selection.activeObject;

        // NOTE: MonoScript選択（Project ウインドウ）
        if (activeObject is MonoScript script)
        {
            SetSingleDocument(script.name);
            return;
        }

        // NOTE: GameObject選択 → インスペクタ自動更新
        if (autoUpdateFromInspector && activeObject is GameObject go)
        {
            RefreshFromGameObject(go);
            return;
        }
    }

    // =====================================================================
    // インスペクタ自動更新: GameObjectのコンポーネントをスキャン
    // =====================================================================

    private void RefreshFromGameObject(GameObject go)
    {
        if (go == null) return;
        if (go.GetInstanceID() == lastGameObjectInstanceId) return;
        lastGameObjectInstanceId = go.GetInstanceID();

        docTabs.Clear();
        var components = go.GetComponents<Component>();

        foreach (var component in components)
        {
            if (component == null) continue;

            MonoScript script = null;
            if (component is MonoBehaviour mb)
                script = MonoScript.FromMonoBehaviour(mb);

            if (script == null) continue;

            var docPath = MusaDocumentBrowser.FindDocForScript(script.name);
            if (string.IsNullOrEmpty(docPath)) continue;

            var tab = new DocTab
            {
                ScriptName = script.name,
                DocPath = docPath,
                DocContent = MusaDocumentBrowser.ReadDocContent(docPath),
            };
            tab.Renderer.Parse(tab.DocContent);
            docTabs.Add(tab);
        }

        // NOTE: タブ名配列を構築
        tabNames = new string[docTabs.Count];
        for (int i = 0; i < docTabs.Count; i++)
            tabNames[i] = docTabs[i].ScriptName;

        selectedTabIndex = 0;
        scrollPosition = Vector2.zero;

        // NOTE: タブモードに切替（手動選択をクリア）
        currentScriptName = null;
        currentDocPath = null;
        currentDocContent = null;

        Repaint();
    }

    // =====================================================================
    // 手動選択: 単一ドキュメント表示
    // =====================================================================

    private void SetSingleDocument(string scriptName)
    {
        if (string.IsNullOrEmpty(scriptName)) return;
        if (scriptName == currentScriptName) return;

        // NOTE: タブモードを解除
        docTabs.Clear();
        tabNames = new string[0];
        lastGameObjectInstanceId = 0;

        currentScriptName = scriptName;
        currentDocPath = MusaDocumentBrowser.FindDocForScript(scriptName);
        if (!string.IsNullOrEmpty(currentDocPath))
        {
            currentDocContent = MusaDocumentBrowser.ReadDocContent(currentDocPath);
            singleRenderer.Parse(currentDocContent);
        }
        else
        {
            currentDocContent = null;
            singleRenderer.Parse(null);
        }
        scrollPosition = Vector2.zero;
        Repaint();
    }

    // =====================================================================
    // Inspectorヘッダーへの Spec リンク追加
    // =====================================================================

    private void OnInspectorHeaderGUI(Editor editor)
    {
        MonoScript script = null;
        if (editor.target is MonoBehaviour mb)
            script = MonoScript.FromMonoBehaviour(mb);
        else if (editor.target is ScriptableObject so && !(so is EditorWindow))
            script = MonoScript.FromScriptableObject(so);

        if (script == null) return;

        var docPath = MusaDocumentBrowser.FindDocForScript(script.name);
        if (string.IsNullOrEmpty(docPath)) return;

        if (GUILayout.Button("Spec: " + script.name, EditorStyles.linkLabel))
        {
            SetSingleDocument(script.name);
        }
    }

    // =====================================================================
    // GUI描画
    // =====================================================================

    private void OnGUI()
    {
        // NOTE: ツールバー（自動更新チェックボックス）
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        var newAutoUpdate = GUILayout.Toggle(autoUpdateFromInspector, "インスペクタから自動更新", EditorStyles.toolbarButton, GUILayout.Width(160));
        if (newAutoUpdate != autoUpdateFromInspector)
        {
            autoUpdateFromInspector = newAutoUpdate;
            if (autoUpdateFromInspector)
            {
                // NOTE: ON にした瞬間に現在の選択を反映
                lastGameObjectInstanceId = 0;
                OnSelectionChanged();
            }
        }
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        // NOTE: タブモード（GameObjectのコンポーネント一覧）
        if (docTabs.Count > 0)
        {
            DrawTabMode();
            return;
        }

        // NOTE: 単一ドキュメントモード
        DrawSingleMode();
    }

    // =====================================================================
    // タブモード描画
    // =====================================================================

    private void DrawTabMode()
    {
        // NOTE: コンポーネント別タブ
        if (tabNames.Length > 0)
        {
            selectedTabIndex = GUILayout.Toolbar(selectedTabIndex, tabNames, GUILayout.Height(24));
            if (selectedTabIndex < 0 || selectedTabIndex >= docTabs.Count)
                selectedTabIndex = 0;
        }

        var tab = docTabs[selectedTabIndex];

        // NOTE: パス表示
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField(tab.ScriptName, EditorStyles.boldLabel);
        var specRoot = MusaDocumentBrowser.GetSpecRootPath();
        var relativePath = tab.DocPath.Replace('\\', '/');
        var specRootNorm = specRoot.Replace('\\', '/');
        if (relativePath.StartsWith(specRootNorm))
            relativePath = "spec" + relativePath.Substring(specRootNorm.Length);
        EditorGUILayout.LabelField("Path: " + relativePath, EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(2);

        // NOTE: Thaleia描画
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        tab.Renderer.Draw();
        EditorGUILayout.EndScrollView();
    }

    // =====================================================================
    // 単一ドキュメント描画
    // =====================================================================

    private void DrawSingleMode()
    {
        // NOTE: ヘッダー
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField(
            string.IsNullOrEmpty(currentScriptName) ? "---" : currentScriptName,
            EditorStyles.boldLabel);

        if (!string.IsNullOrEmpty(currentDocPath))
        {
            var specRoot = MusaDocumentBrowser.GetSpecRootPath();
            var relativePath = currentDocPath.Replace('\\', '/');
            var specRootNorm = specRoot.Replace('\\', '/');
            if (relativePath.StartsWith(specRootNorm))
                relativePath = "spec" + relativePath.Substring(specRootNorm.Length);
            EditorGUILayout.LabelField("Path: " + relativePath, EditorStyles.miniLabel);
        }
        else if (!string.IsNullOrEmpty(currentScriptName))
        {
            EditorGUILayout.LabelField("Path: ---", EditorStyles.miniLabel);
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(2);

        // NOTE: コンテンツ
        if (string.IsNullOrEmpty(currentScriptName))
        {
            EditorGUILayout.HelpBox("Projectウインドウでスクリプトを選択、\nまたはInspectorでSpecリンクをクリックしてください", MessageType.Info);
        }
        else if (string.IsNullOrEmpty(currentDocPath))
        {
            EditorGUILayout.HelpBox("ドキュメントが見つかりません", MessageType.Info);
        }
        else
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            singleRenderer.Draw();
            EditorGUILayout.EndScrollView();
        }
    }
}
#endif
