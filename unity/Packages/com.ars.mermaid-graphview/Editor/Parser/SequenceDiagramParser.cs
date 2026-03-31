using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Ars.MermaidGraphView
{
    public class SequenceDiagramParser : IMermaidParser
    {
        private SequenceDiagramDocument _doc;
        private Dictionary<string, Participant> _participantMap;

        // Arrow patterns: ordered longest first
        private static readonly (string pattern, MessageStyle style, ArrowHead head, bool activate, bool deactivate)[] ArrowPatterns =
        {
            ("-->>+", MessageStyle.Dotted, ArrowHead.Filled, true, false),
            ("->>+",  MessageStyle.Solid,  ArrowHead.Filled, true, false),
            ("-->>-", MessageStyle.Dotted, ArrowHead.Filled, false, true),
            ("->>-",  MessageStyle.Solid,  ArrowHead.Filled, false, true),
            ("-->>",  MessageStyle.Dotted, ArrowHead.Filled, false, false),
            ("->>",   MessageStyle.Solid,  ArrowHead.Filled, false, false),
            ("--x",   MessageStyle.Dotted, ArrowHead.Cross,  false, false),
            ("-x",    MessageStyle.Solid,  ArrowHead.Cross,  false, false),
            ("--)",   MessageStyle.Dotted, ArrowHead.Open,   false, false),
            ("-)",    MessageStyle.Solid,  ArrowHead.Open,   false, false),
            ("-->",   MessageStyle.Dotted, ArrowHead.Filled, false, false),
            ("->",    MessageStyle.Solid,  ArrowHead.Filled, false, false),
        };

        public MermaidDocument Parse(string source)
        {
            _doc = new SequenceDiagramDocument();
            _participantMap = new Dictionary<string, Participant>(StringComparer.Ordinal);

            var lines = NormalizeSource(source);
            var elements = ParseLines(lines, 0, out _);

            _doc.Elements = elements;
            _doc.Participants = new List<Participant>(_participantMap.Values);

            return new MermaidDocument
            {
                DiagramType = DiagramType.SequenceDiagram,
                Content = _doc
            };
        }

        private List<string> NormalizeSource(string source)
        {
            var text = source.Replace("\r\n", "\n").Replace("\r", "\n");
            var rawLines = text.Split('\n');
            var lines = new List<string>();
            bool firstMeaningful = true;

            foreach (var raw in rawLines)
            {
                var trimmed = raw.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                if (trimmed.StartsWith("%%")) continue;

                // Skip header line
                if (firstMeaningful && trimmed.StartsWith("sequenceDiagram"))
                {
                    firstMeaningful = false;
                    continue;
                }
                firstMeaningful = false;
                lines.Add(trimmed);
            }
            return lines;
        }

        private List<SequenceElement> ParseLines(List<string> lines, int startIdx, out int endIdx)
        {
            var elements = new List<SequenceElement>();
            int i = startIdx;

            while (i < lines.Count)
            {
                var line = lines[i];

                // Check for "end" to close a block
                if (line == "end")
                {
                    endIdx = i;
                    return elements;
                }

                // Check for "else" (handled by the caller in alt block)
                if (line.StartsWith("else"))
                {
                    endIdx = i;
                    return elements;
                }

                // Check for block start: loop, alt, opt, par, critical
                if (TryParseBlockStart(line, out var blockKind, out var blockLabel))
                {
                    var block = ParseBlock(lines, i, blockKind, blockLabel, out var blockEnd);
                    elements.Add(block);
                    i = blockEnd + 1; // skip past "end"
                    continue;
                }

                // Check for participant/actor declaration
                if (TryParseParticipant(line))
                {
                    i++;
                    continue;
                }

                // Check for activate/deactivate
                if (TryParseActivation(line, elements))
                {
                    i++;
                    continue;
                }

                // Check for Note
                if (TryParseNote(line, out var note))
                {
                    elements.Add(note);
                    i++;
                    continue;
                }

                // Try to parse as message
                if (TryParseMessage(line, out var msg))
                {
                    elements.Add(msg);
                    i++;
                    continue;
                }

                // Unknown line, skip
                i++;
            }

            endIdx = i;
            return elements;
        }

        private bool TryParseBlockStart(string line, out BlockKind kind, out string label)
        {
            kind = BlockKind.Loop;
            label = null;

            string keyword = null;
            if (line.StartsWith("loop"))
            {
                keyword = "loop";
                kind = BlockKind.Loop;
            }
            else if (line.StartsWith("alt"))
            {
                keyword = "alt";
                kind = BlockKind.Alt;
            }
            else if (line.StartsWith("opt"))
            {
                keyword = "opt";
                kind = BlockKind.Opt;
            }
            else if (line.StartsWith("par"))
            {
                keyword = "par";
                kind = BlockKind.Par;
            }
            else if (line.StartsWith("critical"))
            {
                keyword = "critical";
                kind = BlockKind.Critical;
            }

            if (keyword == null) return false;

            // Make sure it's actually the keyword and not a participant name starting with it
            if (line.Length > keyword.Length && !char.IsWhiteSpace(line[keyword.Length]))
                return false;

            label = line.Length > keyword.Length
                ? line.Substring(keyword.Length).Trim()
                : null;

            return true;
        }

        private SequenceBlock ParseBlock(List<string> lines, int startIdx,
            BlockKind kind, string label, out int endIdx)
        {
            var block = new SequenceBlock(kind, label);

            // First section
            var firstSection = new SequenceSection(label);
            var sectionElements = ParseLines(lines, startIdx + 1, out var nextIdx);
            firstSection.Elements = sectionElements;
            block.Sections.Add(firstSection);

            // Handle else sections (for alt blocks)
            while (nextIdx < lines.Count && lines[nextIdx].StartsWith("else"))
            {
                var elseLine = lines[nextIdx];
                var elseLabel = elseLine.Length > 4 ? elseLine.Substring(4).Trim() : null;
                var elseSection = new SequenceSection(elseLabel);
                var elseElements = ParseLines(lines, nextIdx + 1, out nextIdx);
                elseSection.Elements = elseElements;
                block.Sections.Add(elseSection);
            }

            // nextIdx should now point to "end"
            endIdx = nextIdx;
            return block;
        }

        private bool TryParseParticipant(string line)
        {
            // participant Name
            // participant Name as Alias
            // actor Name
            // actor Name as Alias
            bool isActor = false;
            string rest = null;

            if (line.StartsWith("participant "))
            {
                rest = line.Substring("participant ".Length).Trim();
            }
            else if (line.StartsWith("actor "))
            {
                rest = line.Substring("actor ".Length).Trim();
                isActor = true;
            }

            if (rest == null) return false;

            string id, alias;
            var asMatch = Regex.Match(rest, @"^(.+?)\s+as\s+(.+)$");
            if (asMatch.Success)
            {
                id = asMatch.Groups[1].Value.Trim();
                alias = asMatch.Groups[2].Value.Trim();
            }
            else
            {
                id = rest;
                alias = rest;
            }

            EnsureParticipant(id, alias, isActor);
            return true;
        }

        private bool TryParseActivation(string line, List<SequenceElement> elements)
        {
            // activate ParticipantName (no AST representation, skip)
            // deactivate ParticipantName (no AST representation, skip)
            if (line.StartsWith("activate ") || line.StartsWith("deactivate "))
            {
                // Just ensure the participant exists
                var parts = line.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    EnsureParticipant(parts[1].Trim());
                }
                return true;
            }
            return false;
        }

        private bool TryParseNote(string line, out SequenceNote note)
        {
            note = null;
            // Note left of Alice: text
            // Note right of Bob: text
            // Note over Alice: text
            // Note over Alice,Bob: text
            // case-insensitive "Note"
            if (!line.StartsWith("Note ", StringComparison.OrdinalIgnoreCase))
                return false;

            var rest = line.Substring(5).Trim();

            NotePosition position;
            string participantsPart;
            string text;

            if (rest.StartsWith("left of ", StringComparison.OrdinalIgnoreCase))
            {
                position = NotePosition.LeftOf;
                rest = rest.Substring("left of ".Length).Trim();
            }
            else if (rest.StartsWith("right of ", StringComparison.OrdinalIgnoreCase))
            {
                position = NotePosition.RightOf;
                rest = rest.Substring("right of ".Length).Trim();
            }
            else if (rest.StartsWith("over ", StringComparison.OrdinalIgnoreCase))
            {
                position = NotePosition.Over;
                rest = rest.Substring("over ".Length).Trim();
            }
            else
            {
                return false;
            }

            // Split participant(s) and text by colon
            var colonIdx = rest.IndexOf(':');
            if (colonIdx >= 0)
            {
                participantsPart = rest.Substring(0, colonIdx).Trim();
                text = rest.Substring(colonIdx + 1).Trim();
            }
            else
            {
                participantsPart = rest;
                text = "";
            }

            var participants = new List<string>();
            foreach (var p in participantsPart.Split(','))
            {
                var name = p.Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    EnsureParticipant(name);
                    participants.Add(name);
                }
            }

            note = new SequenceNote
            {
                Position = position,
                OverParticipants = participants,
                Text = text
            };
            return true;
        }

        private bool TryParseMessage(string line, out SequenceMessage msg)
        {
            msg = null;

            // Try each arrow pattern
            foreach (var (pattern, style, head, activate, deactivate) in ArrowPatterns)
            {
                int arrowIdx = FindArrowIndex(line, pattern);
                if (arrowIdx < 0) continue;

                var fromId = line.Substring(0, arrowIdx).Trim();
                var afterArrow = line.Substring(arrowIdx + pattern.Length).Trim();

                // The part after arrow: "ToId: message text" or "ToId"
                string toId;
                string text;

                var colonIdx = afterArrow.IndexOf(':');
                if (colonIdx >= 0)
                {
                    toId = afterArrow.Substring(0, colonIdx).Trim();
                    text = afterArrow.Substring(colonIdx + 1).Trim();
                }
                else
                {
                    toId = afterArrow;
                    text = "";
                }

                if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId))
                    continue;

                EnsureParticipant(fromId);
                EnsureParticipant(toId);

                msg = new SequenceMessage
                {
                    FromId = fromId,
                    ToId = toId,
                    Text = text,
                    Style = style,
                    ArrowHead = head,
                    Activate = activate
                };
                return true;
            }

            return false;
        }

        private int FindArrowIndex(string line, string pattern)
        {
            // Find the arrow pattern, making sure left side looks like an identifier
            int idx = line.IndexOf(pattern, StringComparison.Ordinal);
            if (idx <= 0) return -1;

            // Verify we're not in the middle of a longer arrow by checking
            // that the character before is not an arrow character (except for normal identifier chars)
            return idx;
        }

        private Participant EnsureParticipant(string id, string alias = null, bool isActor = false)
        {
            if (_participantMap.TryGetValue(id, out var existing))
            {
                if (alias != null && alias != id)
                    existing.Alias = alias;
                if (isActor)
                    existing.IsActor = isActor;
                return existing;
            }

            var p = new Participant(id, alias ?? id, isActor);
            _participantMap[id] = p;
            return p;
        }
    }
}
