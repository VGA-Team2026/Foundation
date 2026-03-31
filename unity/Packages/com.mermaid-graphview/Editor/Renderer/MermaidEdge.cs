using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;

namespace MermaidGraphView
{
    public class MermaidEdge : Edge
    {
        public MermaidEdge(EdgeVisualStyle style)
        {
            AddToClassList($"mermaid-edge-{style.LineStyle}");

            if (!string.IsNullOrEmpty(style.Label))
            {
                var edgeLabel = new Label(style.Label);
                edgeLabel.AddToClassList("mermaid-edge-label");
                Add(edgeLabel);
            }

            capabilities &= ~Capabilities.Deletable;
        }
    }
}
