using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MermaidGraphView
{
    public class StateDiagramParser : IMermaidParser
    {
        private StateDiagramDocument _doc;
        private Dictionary<string, StateNode> _stateMap;
        private Stack<CompositeContext> _compositeStack;

        private struct CompositeContext
        {
            public StateNode State;
            public List<StateTransition> Transitions;
        }

        public MermaidDocument Parse(string source)
        {
            _doc = new StateDiagramDocument();
            _stateMap = new Dictionary<string, StateNode>();
            _compositeStack = new Stack<CompositeContext>();

            var lines = NormalizeSource(source);

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                // Skip header line
                if (i == 0 && (line.StartsWith("stateDiagram") || line.StartsWith("stateDiagram-v2")))
                    continue;

                ParseLine(line);
            }

            _doc.States = new List<StateNode>(_stateMap.Values);
            return new MermaidDocument
            {
                DiagramType = DiagramType.StateDiagram,
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
                if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("%%"))
                    lines.Add(trimmed);
            }
            return lines;
        }

        private void ParseLine(string line)
        {
            // Handle "end" for composite states
            if (line == "end")
            {
                CloseComposite();
                return;
            }

            // Handle "note right of/left of State : text"
            if (TryParseNote(line)) return;

            // Handle "state StateName <<stereotype>>"
            if (TryParseStateDeclaration(line)) return;

            // Handle "state StateName {" for composite states
            if (TryParseCompositeState(line)) return;

            // Handle transitions: State1 --> State2 : label
            if (TryParseTransition(line)) return;

            // Handle state alias: state "Long name" as s1
            if (TryParseStateAlias(line)) return;
        }

        private bool TryParseNote(string line)
        {
            // note right of State : text  (skip notes for state diagram AST - not in spec)
            if (line.StartsWith("note ") || line.StartsWith("Note "))
                return true;
            return false;
        }

        private bool TryParseStateDeclaration(string line)
        {
            // state StateName <<fork>>
            // state StateName <<join>>
            // state StateName <<choice>>
            var m = Regex.Match(line, @"^state\s+(\w[\w\-]*)\s+<<(\w+)>>$");
            if (!m.Success) return false;

            var name = m.Groups[1].Value;
            var stereotype = m.Groups[2].Value.ToLowerInvariant();

            var kind = StateKind.Normal;
            switch (stereotype)
            {
                case "fork": kind = StateKind.Fork; break;
                case "join": kind = StateKind.Join; break;
                case "choice": kind = StateKind.Choice; break;
            }

            var node = EnsureState(name);
            node.Kind = kind;
            return true;
        }

        private bool TryParseCompositeState(string line)
        {
            // state StateName {
            var m = Regex.Match(line, @"^state\s+(\w[\w\-]*)\s*\{$");
            if (!m.Success)
            {
                // state "Label" as Id {
                m = Regex.Match(line, @"^state\s+""([^""]+)""\s+as\s+(\w[\w\-]*)\s*\{$");
                if (!m.Success) return false;

                var label = m.Groups[1].Value;
                var id = m.Groups[2].Value;
                var node = EnsureState(id);
                node.Label = label;

                _compositeStack.Push(new CompositeContext
                {
                    State = node,
                    Transitions = node.InternalTransitions
                });
                return true;
            }

            var name = m.Groups[1].Value;
            var stateNode = EnsureState(name);

            _compositeStack.Push(new CompositeContext
            {
                State = stateNode,
                Transitions = stateNode.InternalTransitions
            });
            return true;
        }

        private void CloseComposite()
        {
            if (_compositeStack.Count > 0)
            {
                _compositeStack.Pop();
            }
        }

        private bool TryParseTransition(string line)
        {
            // [*] --> State1 : label
            // State1 --> State2 : label
            // State1 --> State2
            // State1 --> [*]
            var m = Regex.Match(line, @"^(\[?\*?\]?[\w\-]+|\[\*\])\s*-->\s*(\[?\*?\]?[\w\-]+|\[\*\])(\s*:\s*(.+))?$");
            if (!m.Success) return false;

            var fromRaw = m.Groups[1].Value.Trim();
            var toRaw = m.Groups[2].Value.Trim();
            var label = m.Groups[4].Success ? m.Groups[4].Value.Trim() : null;

            var fromId = ResolveStateId(fromRaw);
            var toId = ResolveStateId(toRaw);

            // Ensure states exist
            EnsureState(fromId, fromRaw);
            EnsureState(toId, toRaw);

            var transition = new StateTransition(fromId, toId, label);

            if (_compositeStack.Count > 0)
            {
                var ctx = _compositeStack.Peek();
                ctx.Transitions.Add(transition);

                // Also track child states
                var fromState = _stateMap[fromId];
                var toState = _stateMap[toId];
                if (!ctx.State.Children.Contains(fromState) && fromState != ctx.State)
                    ctx.State.Children.Add(fromState);
                if (!ctx.State.Children.Contains(toState) && toState != ctx.State)
                    ctx.State.Children.Add(toState);
            }
            else
            {
                _doc.Transitions.Add(transition);
            }

            return true;
        }

        private bool TryParseStateAlias(string line)
        {
            // state "Long name" as s1
            var m = Regex.Match(line, @"^state\s+""([^""]+)""\s+as\s+(\w[\w\-]*)$");
            if (!m.Success) return false;

            var label = m.Groups[1].Value;
            var id = m.Groups[2].Value;
            var node = EnsureState(id);
            node.Label = label;
            return true;
        }

        private string ResolveStateId(string raw)
        {
            if (raw == "[*]") return "[*]";
            return raw;
        }

        private StateNode EnsureState(string id, string raw = null)
        {
            if (_stateMap.TryGetValue(id, out var existing))
                return existing;

            var kind = StateKind.Normal;
            if (raw == "[*]" || id == "[*]")
            {
                // [*] can be start or end; we mark it as Start by default
                // The actual role depends on context (source = Start, target = End)
                // We'll leave it as Normal and let consumers decide
                kind = StateKind.Normal;
            }

            var node = new StateNode(id, id, kind);
            _stateMap[id] = node;
            return node;
        }
    }
}
