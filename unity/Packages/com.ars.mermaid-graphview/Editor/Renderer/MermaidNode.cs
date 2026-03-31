using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;

namespace Ars.MermaidGraphView
{
    public class MermaidNode : Node
    {
        public string MermaidId { get; private set; }
        public Port InputPort { get; private set; }
        public Port OutputPort { get; private set; }

        public MermaidNode(string id, string label, NodeVisualStyle style)
        {
            MermaidId = id;
            title = label;
            AddToClassList($"mermaid-node-{style.ShapeClass}");

            InputPort = Port.Create<Edge>(Orientation.Vertical, Direction.Input, Port.Capacity.Multi, typeof(bool));
            InputPort.portName = "";
            inputContainer.Add(InputPort);

            OutputPort = Port.Create<Edge>(Orientation.Vertical, Direction.Output, Port.Capacity.Multi, typeof(bool));
            OutputPort.portName = "";
            outputContainer.Add(OutputPort);

            capabilities &= ~Capabilities.Movable;
            capabilities &= ~Capabilities.Deletable;
            RefreshExpandedState();
            RefreshPorts();
        }
    }
}
