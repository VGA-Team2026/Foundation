using System.Collections.Generic;

namespace MermaidGraphView
{
    public enum NodeShape
    {
        Rectangle,
        Rounded,
        Stadium,
        Cylinder,
        Circle,
        Diamond,
        Hexagon,
        Parallelogram,
        Subroutine,
        Trapezoid
    }

    public enum EdgeStyle
    {
        Solid,
        Dotted,
        Thick
    }

    public enum ArrowType
    {
        Normal,
        Circle,
        Cross,
        Open
    }

    public enum FlowDirection
    {
        TB,
        TD,
        BT,
        RL,
        LR
    }

    public class FlowchartDocument
    {
        public FlowDirection Direction { get; set; } = FlowDirection.TD;
        public List<FlowNode> Nodes { get; set; } = new List<FlowNode>();
        public List<FlowEdge> Edges { get; set; } = new List<FlowEdge>();
        public List<FlowSubgraph> Subgraphs { get; set; } = new List<FlowSubgraph>();
    }

    public class FlowNode
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public NodeShape Shape { get; set; } = NodeShape.Rectangle;
        public string CssClass { get; set; }

        public FlowNode() { }

        public FlowNode(string id, string label = null, NodeShape shape = NodeShape.Rectangle)
        {
            Id = id;
            Label = label ?? id;
            Shape = shape;
        }
    }

    public class FlowEdge
    {
        public string SourceId { get; set; }
        public string TargetId { get; set; }
        public string Label { get; set; }
        public EdgeStyle Style { get; set; } = EdgeStyle.Solid;
        public ArrowType Arrow { get; set; } = ArrowType.Normal;

        public FlowEdge() { }

        public FlowEdge(string sourceId, string targetId, string label = null)
        {
            SourceId = sourceId;
            TargetId = targetId;
            Label = label;
        }
    }

    public class FlowSubgraph
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public List<string> NodeIds { get; set; } = new List<string>();
        public List<FlowSubgraph> Children { get; set; } = new List<FlowSubgraph>();
    }
}
