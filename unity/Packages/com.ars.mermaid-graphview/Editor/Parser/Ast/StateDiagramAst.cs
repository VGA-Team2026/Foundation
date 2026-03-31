using System.Collections.Generic;

namespace Ars.MermaidGraphView
{
    public enum StateKind
    {
        Normal,
        Start,
        End,
        Fork,
        Join,
        Choice
    }

    public class StateDiagramDocument
    {
        public List<StateNode> States { get; set; } = new List<StateNode>();
        public List<StateTransition> Transitions { get; set; } = new List<StateTransition>();
    }

    public class StateNode
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public StateKind Kind { get; set; } = StateKind.Normal;
        public List<StateNode> Children { get; set; } = new List<StateNode>();
        public List<StateTransition> InternalTransitions { get; set; } = new List<StateTransition>();

        public StateNode() { }

        public StateNode(string id, string label = null, StateKind kind = StateKind.Normal)
        {
            Id = id;
            Label = label ?? id;
            Kind = kind;
        }
    }

    public class StateTransition
    {
        public string FromId { get; set; }
        public string ToId { get; set; }
        public string Label { get; set; }

        public StateTransition() { }

        public StateTransition(string fromId, string toId, string label = null)
        {
            FromId = fromId;
            ToId = toId;
            Label = label;
        }
    }
}
