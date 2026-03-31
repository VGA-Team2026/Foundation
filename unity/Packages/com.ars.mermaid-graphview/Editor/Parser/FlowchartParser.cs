using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Ars.MermaidGraphView
{
    public class FlowchartParser : IMermaidParser
    {
        private FlowchartDocument _doc;
        private Dictionary<string, FlowNode> _nodeMap;
        private Stack<FlowSubgraph> _subgraphStack;

        public MermaidDocument Parse(string source)
        {
            _doc = new FlowchartDocument();
            _nodeMap = new Dictionary<string, FlowNode>();
            _subgraphStack = new Stack<FlowSubgraph>();

            var lines = NormalizeSource(source);

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("%%")) continue;

                if (i == 0)
                {
                    ParseHeaderLine(line);
                    continue;
                }

                ParseContentLine(line);
            }

            // Finalize: ensure all nodes are in the document list
            _doc.Nodes = new List<FlowNode>(_nodeMap.Values);

            return new MermaidDocument
            {
                DiagramType = DiagramType.Flowchart,
                Content = _doc
            };
        }

        private List<string> NormalizeSource(string source)
        {
            var text = source.Replace("\r\n", "\n").Replace("\r", "\n");
            var rawLines = text.Split('\n');
            var lines = new List<string>();
            foreach (var raw in rawLines)
            {
                var trimmed = raw.Trim();
                if (trimmed.Length > 0)
                    lines.Add(trimmed);
            }
            return lines;
        }

        private void ParseHeaderLine(string line)
        {
            // flowchart TD / graph LR / etc.
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                _doc.Direction = ParseDirection(parts[1]);
            }
            else
            {
                _doc.Direction = FlowDirection.TD;
            }
        }

        private FlowDirection ParseDirection(string dir)
        {
            switch (dir.ToUpperInvariant())
            {
                case "TB": return FlowDirection.TB;
                case "TD": return FlowDirection.TD;
                case "BT": return FlowDirection.BT;
                case "RL": return FlowDirection.RL;
                case "LR": return FlowDirection.LR;
                default: return FlowDirection.TD;
            }
        }

        private void ParseContentLine(string line)
        {
            // Handle "end" for subgraph
            if (line == "end")
            {
                CloseSubgraph();
                return;
            }

            // Handle subgraph
            if (line.StartsWith("subgraph"))
            {
                ParseSubgraph(line);
                return;
            }

            // Handle style/class directives (skip for now but don't error)
            if (line.StartsWith("style ") || line.StartsWith("class ") ||
                line.StartsWith("classDef ") || line.StartsWith("click ") ||
                line.StartsWith("linkStyle "))
            {
                return;
            }

            // Try to parse as edge(s) or node definition
            // Split by semicolons to allow multiple statements per line
            var statements = line.Split(';');
            foreach (var stmt in statements)
            {
                var trimmed = stmt.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                ParseStatement(trimmed);
            }
        }

        private void ParseSubgraph(string line)
        {
            // subgraph id [Title]
            // subgraph Title
            var rest = line.Substring("subgraph".Length).Trim();
            var sg = new FlowSubgraph();

            // Check for bracket title: subgraph id [Title]
            var bracketMatch = Regex.Match(rest, @"^(\S+)\s*\[(.+)\]$");
            if (bracketMatch.Success)
            {
                sg.Id = bracketMatch.Groups[1].Value;
                sg.Label = bracketMatch.Groups[2].Value;
            }
            else if (!string.IsNullOrEmpty(rest))
            {
                // Use the text as both id and label
                // If it contains spaces, generate an id
                sg.Id = rest.Replace(" ", "_");
                sg.Label = rest;
            }
            else
            {
                sg.Id = "subgraph_" + _doc.Subgraphs.Count;
                sg.Label = sg.Id;
            }

            if (_subgraphStack.Count > 0)
            {
                _subgraphStack.Peek().Children.Add(sg);
            }
            else
            {
                _doc.Subgraphs.Add(sg);
            }

            _subgraphStack.Push(sg);
        }

        private void CloseSubgraph()
        {
            if (_subgraphStack.Count > 0)
            {
                _subgraphStack.Pop();
            }
        }

        private void ParseStatement(string stmt)
        {
            // Try to find an edge pattern
            // Arrows: -->, --->, -.->, -.-, ==>, ===>, --, ---, ~~>
            // Also with labels: -->|label|, -- label -->
            var edgeParsed = TryParseEdgeChain(stmt);
            if (!edgeParsed)
            {
                // It's a standalone node definition
                ParseNodeDefinition(stmt);
            }
        }

        private bool TryParseEdgeChain(string stmt)
        {
            // Find arrow patterns in the statement
            // Strategy: scan for arrow patterns, split into segments
            var arrowPattern = new Regex(
                @"(={3}>|={2}>|<={2}>|" +     // thick arrows
                @"-\.{1,2}->|" +               // dotted arrows
                @"-{3}>|-{2}>|" +              // solid arrows
                @"-{3}|-{2}(?!>)|" +           // lines without arrow
                @"~~>|" +                       // wave arrow
                @"<-{2}>|<-{2,3})" +           // bidirectional
                @"");

            // More practical approach: find arrows with possible labels
            // Pattern: NodeRef ArrowWithOptionalLabel NodeRef (ArrowWithOptionalLabel NodeRef)*
            var segments = SplitByArrows(stmt);
            if (segments == null || segments.Count < 2)
                return false;

            // segments is a list of alternating node-refs and arrow-info
            // [nodeRef, arrowInfo, nodeRef, arrowInfo, nodeRef, ...]
            for (int i = 0; i < segments.Count - 1; i += 2)
            {
                var sourceRef = segments[i].NodeText;
                var arrowInfo = segments[i + 1];
                if (i + 2 >= segments.Count) break;
                var targetRef = segments[i + 2].NodeText;

                var sourceNode = EnsureNode(sourceRef);
                var targetNode = EnsureNode(targetRef);

                var edge = new FlowEdge
                {
                    SourceId = sourceNode.Id,
                    TargetId = targetNode.Id,
                    Label = arrowInfo.Label,
                    Style = arrowInfo.EdgeStyle,
                    Arrow = arrowInfo.ArrowType
                };

                _doc.Edges.Add(edge);

                // Track subgraph membership
                if (_subgraphStack.Count > 0)
                {
                    var sg = _subgraphStack.Peek();
                    if (!sg.NodeIds.Contains(sourceNode.Id))
                        sg.NodeIds.Add(sourceNode.Id);
                    if (!sg.NodeIds.Contains(targetNode.Id))
                        sg.NodeIds.Add(targetNode.Id);
                }
            }

            return true;
        }

        private struct ParsedSegment
        {
            public string NodeText;
            public string Label;
            public EdgeStyle EdgeStyle;
            public ArrowType ArrowType;
        }

        private List<ParsedSegment> SplitByArrows(string stmt)
        {
            // Regex to match arrows (with optional pipe-labels or inline labels)
            // Arrow patterns ordered by length (longest first)
            var arrowRegex = new Regex(
                @"(?:" +
                @"(={3}>)" +           // ===>  thick
                @"|(={2}>)" +          // ==>   thick
                @"|(-\.+->\s*)" +      // -.-> or -..-> dotted arrow
                @"|(-{3}>)" +          // --->  solid
                @"|(-{2}>)" +          // -->   solid
                @"|(-{2,3}(?!>))" +    // -- or --- solid line (no arrow)
                @"|(~~>)" +            // ~~> wave
                @")" +
                @"(\|[^|]*\|)?"        // optional |label|
            );

            // Also handle: A -- text --> B  (label between dashes and arrow)
            var inlineLabelRegex = new Regex(
                @"(-{2})\s+([^-=~\.][^-=~\.]*?)\s+(-{2}>|={2}>|-\.->)"
            );

            // First try inline label pattern
            var inlineMatch = inlineLabelRegex.Match(stmt);
            if (inlineMatch.Success)
            {
                return SplitByInlineLabel(stmt, inlineMatch);
            }

            var matches = arrowRegex.Matches(stmt);
            if (matches.Count == 0) return null;

            var result = new List<ParsedSegment>();
            int lastEnd = 0;

            foreach (Match m in matches)
            {
                // Node text before this arrow
                var nodeText = stmt.Substring(lastEnd, m.Index - lastEnd).Trim();
                if (nodeText.Length > 0)
                {
                    result.Add(new ParsedSegment { NodeText = nodeText });
                }

                // Arrow info
                string arrowStr = m.Value;
                string label = null;
                EdgeStyle style = EdgeStyle.Solid;
                ArrowType arrowType = ArrowType.Normal;

                // Extract pipe label if present
                var pipeGroup = m.Groups[8];
                if (pipeGroup.Success && pipeGroup.Value.Length > 2)
                {
                    label = pipeGroup.Value.Substring(1, pipeGroup.Value.Length - 2);
                    arrowStr = arrowStr.Substring(0, arrowStr.Length - pipeGroup.Value.Length);
                }

                arrowStr = arrowStr.Trim();
                ClassifyArrow(arrowStr, out style, out arrowType);

                result.Add(new ParsedSegment
                {
                    Label = label,
                    EdgeStyle = style,
                    ArrowType = arrowType
                });

                lastEnd = m.Index + m.Length;
            }

            // Remaining text is the last node
            var remaining = stmt.Substring(lastEnd).Trim();
            if (remaining.Length > 0)
            {
                result.Add(new ParsedSegment { NodeText = remaining });
            }

            return result.Count >= 3 ? result : null;
        }

        private List<ParsedSegment> SplitByInlineLabel(string stmt, Match inlineMatch)
        {
            var result = new List<ParsedSegment>();

            var beforeNode = stmt.Substring(0, inlineMatch.Index).Trim();
            result.Add(new ParsedSegment { NodeText = beforeNode });

            string label = inlineMatch.Groups[2].Value.Trim();
            string arrowEnd = inlineMatch.Groups[3].Value;
            ClassifyArrow(arrowEnd, out var style, out var arrowType);

            result.Add(new ParsedSegment
            {
                Label = label,
                EdgeStyle = style,
                ArrowType = arrowType
            });

            var afterNode = stmt.Substring(inlineMatch.Index + inlineMatch.Length).Trim();
            result.Add(new ParsedSegment { NodeText = afterNode });

            return result;
        }

        private void ClassifyArrow(string arrow, out EdgeStyle style, out ArrowType arrowType)
        {
            style = EdgeStyle.Solid;
            arrowType = ArrowType.Normal;

            if (string.IsNullOrEmpty(arrow)) return;

            if (arrow.Contains("="))
            {
                style = EdgeStyle.Thick;
            }
            else if (arrow.Contains("."))
            {
                style = EdgeStyle.Dotted;
            }

            if (arrow.EndsWith(">"))
            {
                arrowType = ArrowType.Normal;
            }
            else if (arrow.EndsWith("x"))
            {
                arrowType = ArrowType.Cross;
            }
            else if (arrow.EndsWith("o"))
            {
                arrowType = ArrowType.Circle;
            }
            else
            {
                arrowType = ArrowType.Open;
            }
        }

        private FlowNode EnsureNode(string nodeText)
        {
            // Parse node text: could be "A", "A[Text]", "A(Text)", etc.
            string id;
            string label;
            NodeShape shape;
            ParseNodeText(nodeText, out id, out label, out shape);

            if (_nodeMap.TryGetValue(id, out var existing))
            {
                // Update if we have new shape/label info
                if (label != id)
                {
                    existing.Label = label;
                    existing.Shape = shape;
                }
                return existing;
            }

            var node = new FlowNode(id, label, shape);
            _nodeMap[id] = node;

            if (_subgraphStack.Count > 0)
            {
                var sg = _subgraphStack.Peek();
                if (!sg.NodeIds.Contains(id))
                    sg.NodeIds.Add(id);
            }

            return node;
        }

        private void ParseNodeDefinition(string text)
        {
            ParseNodeText(text, out var id, out var label, out var shape);
            if (string.IsNullOrEmpty(id)) return;

            if (_nodeMap.TryGetValue(id, out var existing))
            {
                if (label != id)
                {
                    existing.Label = label;
                    existing.Shape = shape;
                }
            }
            else
            {
                var node = new FlowNode(id, label, shape);
                _nodeMap[id] = node;
            }

            if (_subgraphStack.Count > 0)
            {
                var sg = _subgraphStack.Peek();
                if (!sg.NodeIds.Contains(id))
                    sg.NodeIds.Add(id);
            }
        }

        private void ParseNodeText(string text, out string id, out string label, out NodeShape shape)
        {
            text = text.Trim();
            id = text;
            label = text;
            shape = NodeShape.Rectangle;

            if (string.IsNullOrEmpty(text)) return;

            // Try matching various node shapes
            // Order matters: check multi-char delimiters first

            // {{text}} - Hexagon
            var m = Regex.Match(text, @"^(\w[\w\-]*)(\{\{(.+)\}\})$");
            if (m.Success) { id = m.Groups[1].Value; label = m.Groups[3].Value; shape = NodeShape.Hexagon; return; }

            // [[text]] - Subroutine
            m = Regex.Match(text, @"^(\w[\w\-]*)\[\[(.+)\]\]$");
            if (m.Success) { id = m.Groups[1].Value; label = m.Groups[2].Value; shape = NodeShape.Subroutine; return; }

            // ((text)) - Circle (double circle)
            m = Regex.Match(text, @"^(\w[\w\-]*)\(\((.+)\)\)$");
            if (m.Success) { id = m.Groups[1].Value; label = m.Groups[2].Value; shape = NodeShape.Circle; return; }

            // ([text]) - Stadium
            m = Regex.Match(text, @"^(\w[\w\-]*)\(\[(.+)\]\)$");
            if (m.Success) { id = m.Groups[1].Value; label = m.Groups[2].Value; shape = NodeShape.Stadium; return; }

            // [(text)] - Cylinder
            m = Regex.Match(text, @"^(\w[\w\-]*)\[\((.+)\)\]$");
            if (m.Success) { id = m.Groups[1].Value; label = m.Groups[2].Value; shape = NodeShape.Cylinder; return; }

            // [/text/] - Parallelogram
            m = Regex.Match(text, @"^(\w[\w\-]*)\[/(.+)/\]$");
            if (m.Success) { id = m.Groups[1].Value; label = m.Groups[2].Value; shape = NodeShape.Parallelogram; return; }

            // [/text\] - Trapezoid
            m = Regex.Match(text, @"^(\w[\w\-]*)\[/(.+)\\\]$");
            if (m.Success) { id = m.Groups[1].Value; label = m.Groups[2].Value; shape = NodeShape.Trapezoid; return; }

            // {text} - Diamond
            m = Regex.Match(text, @"^(\w[\w\-]*)\{([^{].*[^}])\}$");
            if (m.Success) { id = m.Groups[1].Value; label = m.Groups[2].Value; shape = NodeShape.Diamond; return; }

            // (text) - Rounded
            m = Regex.Match(text, @"^(\w[\w\-]*)\(([^(\[].+)\)$");
            if (m.Success) { id = m.Groups[1].Value; label = m.Groups[2].Value; shape = NodeShape.Rounded; return; }

            // [text] - Rectangle
            m = Regex.Match(text, @"^(\w[\w\-]*)\[([^\[/].+)\]$");
            if (m.Success) { id = m.Groups[1].Value; label = m.Groups[2].Value; shape = NodeShape.Rectangle; return; }

            // Bare identifier
            m = Regex.Match(text, @"^(\w[\w\-]*)$");
            if (m.Success) { id = m.Groups[1].Value; label = id; shape = NodeShape.Rectangle; return; }

            // Fallback: use whole text as id (cleaned)
            id = Regex.Replace(text, @"[^\w\-]", "_");
            label = text;
        }
    }
}
