using System.Collections.Generic;
using UnityEngine;

namespace Ars.MermaidGraphView
{
    public class LayoutGraph
    {
        public List<LayoutNode> Nodes { get; } = new List<LayoutNode>();
        public List<LayoutEdge> Edges { get; } = new List<LayoutEdge>();
        public List<LayoutGroup> Groups { get; } = new List<LayoutGroup>();

        public LayoutNode GetNode(string id) => Nodes.Find(n => n.Id == id);
        public List<LayoutEdge> GetOutEdges(string nodeId) => Edges.FindAll(e => e.SourceId == nodeId);
        public List<LayoutEdge> GetInEdges(string nodeId) => Edges.FindAll(e => e.TargetId == nodeId);
    }

    public class LayoutNode
    {
        public string Id { get; set; }
        public Vector2 Size { get; set; } = new Vector2(150, 40);
        public string GroupId { get; set; }
        public bool IsDummy { get; set; }
    }

    public class LayoutEdge
    {
        public string SourceId { get; set; }
        public string TargetId { get; set; }
        public string Id { get; set; }
        public bool IsReversed { get; set; }

        public void Reverse()
        {
            (SourceId, TargetId) = (TargetId, SourceId);
            IsReversed = !IsReversed;
        }
    }

    public class LayoutGroup
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public List<string> NodeIds { get; } = new List<string>();
        public string ParentGroupId { get; set; }
    }

    public class LayoutResult
    {
        public Dictionary<string, Vector2> NodePositions { get; set; } = new Dictionary<string, Vector2>();
        public Dictionary<string, Rect> GroupRects { get; set; } = new Dictionary<string, Rect>();
        public Dictionary<string, List<Vector2>> EdgeWaypoints { get; set; } = new Dictionary<string, List<Vector2>>();
        public float TotalHeight { get; set; }
        public List<SequenceMessageLayout> Messages { get; set; } = new List<SequenceMessageLayout>();
        public List<SequenceBlockLayout> Blocks { get; set; } = new List<SequenceBlockLayout>();
        public List<SequenceNoteLayout> Notes { get; set; } = new List<SequenceNoteLayout>();
    }

    // Sequence diagram specific
    public class SequenceLayoutGraph : LayoutGraph
    {
        public List<SequenceParticipantInfo> Participants { get; } = new List<SequenceParticipantInfo>();
        public List<SequenceElementLayout> Elements { get; } = new List<SequenceElementLayout>();
    }

    public class SequenceParticipantInfo
    {
        public string Id { get; set; }
        public string Label { get; set; }
    }

    public abstract class SequenceElementLayout { }

    public class SequenceMessageLayout : SequenceElementLayout
    {
        public string FromId { get; set; }
        public string ToId { get; set; }
        public string Text { get; set; }
        public bool IsDotted { get; set; }
        public bool IsOpen { get; set; }
        public float Y { get; set; }
    }

    public class SequenceBlockLayout : SequenceElementLayout
    {
        public string Kind { get; set; }
        public string Label { get; set; }
        public float StartY { get; set; }
        public float EndY { get; set; }
        public float MinX { get; set; }
        public float MaxX { get; set; }
        public List<SequenceElementLayout> InnerElements { get; } = new List<SequenceElementLayout>();
        public List<SequenceSectionLayout> Sections { get; } = new List<SequenceSectionLayout>();
    }

    public class SequenceSectionLayout
    {
        public string Label { get; set; }
        public float Y { get; set; }
    }

    public class SequenceNoteLayout : SequenceElementLayout
    {
        public string Text { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
    }
}
