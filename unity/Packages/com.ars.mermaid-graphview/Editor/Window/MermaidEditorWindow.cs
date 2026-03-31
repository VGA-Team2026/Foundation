using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ars.MermaidGraphView
{
    public class MermaidEditorWindow : EditorWindow
    {
        private InputPanel _inputPanel;
        private MermaidGraphViewPanel _graphViewPanel;
        private IVisualElementScheduledItem _debounceTask;
        private string _pendingText;
        private bool _hasPendingUpdate;

        [MenuItem("Window/Mermaid Viewer")]
        public static void ShowWindow()
        {
            var window = GetWindow<MermaidEditorWindow>();
            window.titleContent = new GUIContent("Mermaid Viewer");
            window.minSize = new Vector2(800, 500);
        }

        private void CreateGUI()
        {
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                FindStyleSheetPath("MermaidGraphView"));
            if (styleSheet != null)
            {
                rootVisualElement.styleSheets.Add(styleSheet);
            }

            var nodeStyleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                FindStyleSheetPath("MermaidNodeStyles"));
            if (nodeStyleSheet != null)
            {
                rootVisualElement.styleSheets.Add(nodeStyleSheet);
            }

            var splitView = new TwoPaneSplitView(0, 300, TwoPaneSplitViewOrientation.Horizontal);
            rootVisualElement.Add(splitView);
            splitView.style.flexGrow = 1;

            _inputPanel = new InputPanel();
            splitView.Add(_inputPanel);

            _graphViewPanel = new MermaidGraphViewPanel();
            splitView.Add(_graphViewPanel);

            _inputPanel.OnTextChanged += OnInputTextChanged;

            // Register drag & drop
            rootVisualElement.RegisterCallback<DragUpdatedEvent>(OnDragUpdated);
            rootVisualElement.RegisterCallback<DragPerformEvent>(OnDragPerform);

            // Initial render with default text
            ScheduleUpdate(_inputPanel.SourceText);
        }

        private void OnInputTextChanged(string newText)
        {
            ScheduleUpdate(newText);
        }

        private void ScheduleUpdate(string text)
        {
            _pendingText = text;
            _hasPendingUpdate = true;

            if (_debounceTask != null)
            {
                return;
            }

            _debounceTask = rootVisualElement.schedule.Execute(() =>
            {
                _debounceTask = null;

                if (_hasPendingUpdate)
                {
                    _hasPendingUpdate = false;
                    ProcessInput(_pendingText);
                }
            }).StartingIn(300);
        }

        private void ProcessInput(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                _inputPanel.SetStatus("Empty input", true);
                return;
            }

            try
            {
                var parser = ParserFactory.Create(source);
                var document = parser.Parse(source);

                _inputPanel.SetDiagramType(document.DiagramType.ToString());

                var layoutGraph = LayoutGraphConverter.Convert(document);
                var engine = LayoutEngineFactory.Create(document.DiagramType);
                var layoutResult = engine.Calculate(layoutGraph, new LayoutConfig());

                _graphViewPanel.Render(document, layoutResult);
                _inputPanel.SetStatus("OK");
            }
            catch (MermaidParseException ex)
            {
                _inputPanel.SetStatus($"Parse error: {ex.Message}", true);
            }
            catch (System.Exception ex)
            {
                _inputPanel.SetStatus($"Error: {ex.Message}", true);
            }
        }

        private void OnDragUpdated(DragUpdatedEvent evt)
        {
            if (DragAndDrop.paths != null && DragAndDrop.paths.Length > 0)
            {
                foreach (var path in DragAndDrop.paths)
                {
                    if (path.EndsWith(".mmd") || path.EndsWith(".md"))
                    {
                        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                        evt.StopPropagation();
                        return;
                    }
                }
            }
        }

        private void OnDragPerform(DragPerformEvent evt)
        {
            if (DragAndDrop.paths == null || DragAndDrop.paths.Length == 0)
                return;

            DragAndDrop.AcceptDrag();

            foreach (var path in DragAndDrop.paths)
            {
                if (path.EndsWith(".mmd"))
                {
                    string content = System.IO.File.ReadAllText(path);
                    _inputPanel.SourceText = content;
                    evt.StopPropagation();
                    return;
                }

                if (path.EndsWith(".md"))
                {
                    string content = System.IO.File.ReadAllText(path);
                    string mermaidCode = ExtractMermaidFromMarkdown(content);
                    if (!string.IsNullOrEmpty(mermaidCode))
                    {
                        _inputPanel.SourceText = mermaidCode;
                    }
                    else
                    {
                        _inputPanel.SetStatus("No mermaid code block found in .md file", true);
                    }
                    evt.StopPropagation();
                    return;
                }
            }
        }

        private static string ExtractMermaidFromMarkdown(string markdown)
        {
            const string startTag = "```mermaid";
            const string endTag = "```";

            int startIndex = markdown.IndexOf(startTag, System.StringComparison.OrdinalIgnoreCase);
            if (startIndex < 0) return null;

            startIndex += startTag.Length;

            // Skip any characters on the same line as the opening fence
            int newlineAfterStart = markdown.IndexOf('\n', startIndex);
            if (newlineAfterStart >= 0)
            {
                startIndex = newlineAfterStart + 1;
            }

            int endIndex = markdown.IndexOf(endTag, startIndex, System.StringComparison.Ordinal);
            if (endIndex < 0) return null;

            return markdown.Substring(startIndex, endIndex - startIndex).Trim();
        }

        private static string FindStyleSheetPath(string name)
        {
            string[] guids = AssetDatabase.FindAssets($"{name} t:StyleSheet");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("com.ars.mermaid-graphview") && path.EndsWith($"{name}.uss"))
                {
                    return path;
                }
            }

            // Fallback to known path
            return $"Packages/com.ars.mermaid-graphview/Editor/Styles/{name}.uss";
        }
    }
}
