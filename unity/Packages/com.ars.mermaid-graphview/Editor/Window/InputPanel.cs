using System;
using UnityEngine.UIElements;

namespace Ars.MermaidGraphView
{
    public class InputPanel : VisualElement
    {
        public event Action<string> OnTextChanged;

        private readonly Label _diagramTypeLabel;
        private readonly TextField _textField;
        private readonly Label _statusLabel;

        private const string DefaultText = "flowchart TD\n    A[Start] --> B[End]";

        public string SourceText
        {
            get => _textField.value;
            set => _textField.value = value;
        }

        public InputPanel()
        {
            style.flexGrow = 1;
            style.minWidth = 200;

            _diagramTypeLabel = new Label("Diagram Type: --");
            _diagramTypeLabel.AddToClassList("mermaid-diagram-type");
            Add(_diagramTypeLabel);

            _textField = new TextField
            {
                multiline = true,
                value = DefaultText
            };
            _textField.style.flexGrow = 1;
            _textField.style.whiteSpace = WhiteSpace.Normal;

            // The inner text input element needs to fill the container
            var textInput = _textField.Q<VisualElement>(className: "unity-text-field__input");
            if (textInput != null)
            {
                textInput.style.flexGrow = 1;
                textInput.style.unityTextAlign = UnityEngine.TextAnchor.UpperLeft;
                textInput.style.whiteSpace = WhiteSpace.Normal;
                textInput.style.fontSize = 12;
                textInput.style.backgroundColor = new UnityEngine.Color(0.15f, 0.15f, 0.15f);
                textInput.style.color = new UnityEngine.Color(0.85f, 0.85f, 0.85f);
            }

            _textField.RegisterValueChangedCallback(evt =>
            {
                OnTextChanged?.Invoke(evt.newValue);
            });

            Add(_textField);

            _statusLabel = new Label("Ready");
            _statusLabel.AddToClassList("mermaid-status");
            Add(_statusLabel);
        }

        public void SetDiagramType(string diagramType)
        {
            _diagramTypeLabel.text = $"Diagram Type: {diagramType}";
        }

        public void SetStatus(string message, bool isError = false)
        {
            _statusLabel.text = message;
            _statusLabel.RemoveFromClassList("mermaid-status-error");
            _statusLabel.RemoveFromClassList("mermaid-status");

            if (isError)
            {
                _statusLabel.AddToClassList("mermaid-status-error");
            }
            else
            {
                _statusLabel.AddToClassList("mermaid-status");
            }
        }
    }
}
