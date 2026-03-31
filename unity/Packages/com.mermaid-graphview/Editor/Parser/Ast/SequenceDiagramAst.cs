using System.Collections.Generic;

namespace MermaidGraphView
{
    public enum MessageStyle
    {
        Solid,
        Dotted
    }

    public enum ArrowHead
    {
        Filled,
        Open,
        Cross
    }

    public enum BlockKind
    {
        Loop,
        Alt,
        Opt,
        Par,
        Critical
    }

    public enum NotePosition
    {
        LeftOf,
        RightOf,
        Over
    }

    public class SequenceDiagramDocument
    {
        public List<Participant> Participants { get; set; } = new List<Participant>();
        public List<SequenceElement> Elements { get; set; } = new List<SequenceElement>();
    }

    public class Participant
    {
        public string Id { get; set; }
        public string Alias { get; set; }
        public bool IsActor { get; set; }

        public Participant() { }

        public Participant(string id, string alias = null, bool isActor = false)
        {
            Id = id;
            Alias = alias ?? id;
            IsActor = isActor;
        }
    }

    public abstract class SequenceElement { }

    public class SequenceMessage : SequenceElement
    {
        public string FromId { get; set; }
        public string ToId { get; set; }
        public string Text { get; set; }
        public MessageStyle Style { get; set; } = MessageStyle.Solid;
        public ArrowHead ArrowHead { get; set; } = ArrowHead.Filled;
        public bool Activate { get; set; }

        public SequenceMessage() { }

        public SequenceMessage(string fromId, string toId, string text,
            MessageStyle style = MessageStyle.Solid, ArrowHead arrowHead = ArrowHead.Filled)
        {
            FromId = fromId;
            ToId = toId;
            Text = text;
            Style = style;
            ArrowHead = arrowHead;
        }
    }

    public class SequenceBlock : SequenceElement
    {
        public BlockKind Kind { get; set; }
        public string Label { get; set; }
        public List<SequenceSection> Sections { get; set; } = new List<SequenceSection>();

        public SequenceBlock() { }

        public SequenceBlock(BlockKind kind, string label = null)
        {
            Kind = kind;
            Label = label;
        }
    }

    public class SequenceSection
    {
        public string Label { get; set; }
        public List<SequenceElement> Elements { get; set; } = new List<SequenceElement>();

        public SequenceSection() { }

        public SequenceSection(string label)
        {
            Label = label;
        }
    }

    public class SequenceNote : SequenceElement
    {
        public NotePosition Position { get; set; }
        public List<string> OverParticipants { get; set; } = new List<string>();
        public string Text { get; set; }

        public SequenceNote() { }

        public SequenceNote(NotePosition position, string text, params string[] participants)
        {
            Position = position;
            Text = text;
            if (participants != null)
                OverParticipants = new List<string>(participants);
        }
    }
}
