using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Ars.MermaidGraphView
{
    public class ClassDiagramParser : IMermaidParser
    {
        private ClassDiagramDocument _doc;
        private Dictionary<string, ClassNode> _classMap;
        private ClassNode _currentClass;

        // Relation patterns ordered by length (longest first to avoid partial matches)
        private static readonly (string pattern, RelationType type, bool reversed)[] RelationPatterns =
        {
            ("<|..", RelationType.Realization, true),
            ("..|>", RelationType.Realization, false),
            ("<|--", RelationType.Inheritance, true),
            ("--|>", RelationType.Inheritance, false),
            ("*--",  RelationType.Composition, true),
            ("--*",  RelationType.Composition, false),
            ("o--",  RelationType.Aggregation, true),
            ("--o",  RelationType.Aggregation, false),
            ("<--",  RelationType.Association, true),
            ("-->",  RelationType.Association, false),
            ("..>",  RelationType.Dependency, false),
            ("<..",  RelationType.Dependency, true),
            ("--",   RelationType.Link, false),
            ("..",   RelationType.Dependency, false),
        };

        public MermaidDocument Parse(string source)
        {
            _doc = new ClassDiagramDocument();
            _classMap = new Dictionary<string, ClassNode>();
            _currentClass = null;

            var lines = NormalizeSource(source);

            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];

                // Skip header
                if (i == 0 && line.StartsWith("classDiagram"))
                    continue;

                ParseLine(line);
            }

            _doc.Classes = new List<ClassNode>(_classMap.Values);
            return new MermaidDocument
            {
                DiagramType = DiagramType.ClassDiagram,
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
            // Handle closing brace for class body
            if (line == "}")
            {
                _currentClass = null;
                return;
            }

            // If inside a class body, parse member
            if (_currentClass != null)
            {
                ParseMember(line, _currentClass);
                return;
            }

            // Handle annotation <<interface>> etc.
            if (TryParseAnnotation(line)) return;

            // Handle "class ClassName {" or "class ClassName"
            if (TryParseClassDeclaration(line)) return;

            // Handle relationships
            if (TryParseRelation(line)) return;

            // Handle namespace or other directives (skip)
            if (line.StartsWith("namespace ") || line.StartsWith("note ") ||
                line.StartsWith("click ") || line.StartsWith("link ") ||
                line.StartsWith("style ") || line.StartsWith("cssClass ") ||
                line.StartsWith("callback ") || line.StartsWith("direction "))
                return;

            // Handle member addition via ClassName : member syntax
            if (TryParseMemberShorthand(line)) return;
        }

        private bool TryParseAnnotation(string line)
        {
            // <<interface>> ClassName
            // <<abstract>> ClassName
            var m = Regex.Match(line, @"^<<(\w+)>>\s+(\w+)$");
            if (!m.Success) return false;

            var stereotype = m.Groups[1].Value;
            var className = m.Groups[2].Value;
            var cls = EnsureClass(className);
            cls.Stereotype = stereotype;
            return true;
        }

        private bool TryParseClassDeclaration(string line)
        {
            // class ClassName {
            var m = Regex.Match(line, @"^class\s+(\w+)\s*\{$");
            if (m.Success)
            {
                var cls = EnsureClass(m.Groups[1].Value);
                _currentClass = cls;
                return true;
            }

            // class ClassName
            m = Regex.Match(line, @"^class\s+(\w+)\s*$");
            if (m.Success)
            {
                EnsureClass(m.Groups[1].Value);
                return true;
            }

            // class ClassName~GenericType~
            m = Regex.Match(line, @"^class\s+(\w+)\s*~(\w+)~\s*(\{?)$");
            if (m.Success)
            {
                var cls = EnsureClass(m.Groups[1].Value);
                cls.Stereotype = m.Groups[2].Value;
                if (m.Groups[3].Value == "{")
                    _currentClass = cls;
                return true;
            }

            // class ClassName:::cssClass
            m = Regex.Match(line, @"^class\s+(\w+)\s*:::\s*\w+\s*(\{?)$");
            if (m.Success)
            {
                var cls = EnsureClass(m.Groups[1].Value);
                if (m.Groups[2].Value == "{")
                    _currentClass = cls;
                return true;
            }

            return false;
        }

        private bool TryParseRelation(string line)
        {
            // Find relation pattern in the line
            foreach (var (pattern, relType, reversed) in RelationPatterns)
            {
                int idx = FindRelationIndex(line, pattern);
                if (idx < 0) continue;

                var leftPart = line.Substring(0, idx).Trim();
                var rightPart = line.Substring(idx + pattern.Length).Trim();

                // Parse cardinalities: "1" ClassName or ClassName "1"
                string leftClass, rightClass, leftCard, rightCard, label;
                ParseRelationSide(leftPart, out leftClass, out leftCard, isLeft: true);
                ParseRelationRightSide(rightPart, out rightClass, out rightCard, out label);

                if (string.IsNullOrEmpty(leftClass) || string.IsNullOrEmpty(rightClass))
                    continue;

                EnsureClass(leftClass);
                EnsureClass(rightClass);

                var relation = new ClassRelation
                {
                    FromClass = reversed ? rightClass : leftClass,
                    ToClass = reversed ? leftClass : rightClass,
                    Type = relType,
                    Label = label,
                    FromCardinality = reversed ? rightCard : leftCard,
                    ToCardinality = reversed ? leftCard : rightCard
                };

                _doc.Relations.Add(relation);
                return true;
            }

            return false;
        }

        private int FindRelationIndex(string line, string pattern)
        {
            // Find the pattern, but not inside quoted strings
            int idx = line.IndexOf(pattern, StringComparison.Ordinal);
            if (idx <= 0) return -1;

            // Make sure we're not matching inside a class name
            // The character before should be whitespace or a quote/cardinality
            // and the character after should be whitespace or a quote/cardinality or end-of-string
            return idx;
        }

        private void ParseRelationSide(string part, out string className, out string cardinality, bool isLeft)
        {
            className = null;
            cardinality = null;

            if (string.IsNullOrEmpty(part)) return;

            // Check for cardinality: ClassName "1..n" or "1..n" ClassName
            var m = Regex.Match(part, @"^(\w+)\s+""([^""]+)""$");
            if (m.Success)
            {
                className = m.Groups[1].Value;
                cardinality = m.Groups[2].Value;
                return;
            }

            m = Regex.Match(part, @"^""([^""]+)""\s+(\w+)$");
            if (m.Success)
            {
                cardinality = m.Groups[1].Value;
                className = m.Groups[2].Value;
                return;
            }

            // Just a class name
            m = Regex.Match(part, @"^(\w+)$");
            if (m.Success)
            {
                className = m.Groups[1].Value;
                return;
            }

            // Fallback
            className = part.Trim();
        }

        private void ParseRelationRightSide(string part, out string className, out string cardinality, out string label)
        {
            className = null;
            cardinality = null;
            label = null;

            if (string.IsNullOrEmpty(part)) return;

            // Check for label after colon: ClassName : label
            var colonIdx = part.IndexOf(':');
            string beforeColon = colonIdx >= 0 ? part.Substring(0, colonIdx).Trim() : part.Trim();
            if (colonIdx >= 0)
            {
                label = part.Substring(colonIdx + 1).Trim();
            }

            // Parse cardinality from the remaining part
            ParseRelationSide(beforeColon, out className, out cardinality, isLeft: false);
        }

        private bool TryParseMemberShorthand(string line)
        {
            // ClassName : +memberName type
            // ClassName : +methodName() returnType
            var m = Regex.Match(line, @"^(\w+)\s*:\s*(.+)$");
            if (!m.Success) return false;

            var className = m.Groups[1].Value;
            var memberText = m.Groups[2].Value.Trim();

            var cls = EnsureClass(className);
            ParseMember(memberText, cls);
            return true;
        }

        private void ParseMember(string line, ClassNode cls)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            var text = line.Trim();
            var visibility = MemberVisibility.Public;

            // Check visibility prefix
            if (text.Length > 0)
            {
                switch (text[0])
                {
                    case '+':
                        visibility = MemberVisibility.Public;
                        text = text.Substring(1).Trim();
                        break;
                    case '-':
                        visibility = MemberVisibility.Private;
                        text = text.Substring(1).Trim();
                        break;
                    case '#':
                        visibility = MemberVisibility.Protected;
                        text = text.Substring(1).Trim();
                        break;
                    case '~':
                        visibility = MemberVisibility.Internal;
                        text = text.Substring(1).Trim();
                        break;
                }
            }

            // Determine if it's a method (contains parentheses)
            bool isMethod = text.Contains("(");

            string name;
            string type = null;

            if (isMethod)
            {
                // Parse method: name(params) returnType
                var m = Regex.Match(text, @"^(.+\(.*\))\s*(.*)$");
                if (m.Success)
                {
                    name = m.Groups[1].Value.Trim();
                    type = m.Groups[2].Value.Trim();
                    if (string.IsNullOrEmpty(type)) type = null;
                }
                else
                {
                    name = text;
                }
            }
            else
            {
                // Parse field: type name  or  name type  or  name : type
                var colonIdx = text.IndexOf(':');
                if (colonIdx >= 0)
                {
                    name = text.Substring(0, colonIdx).Trim();
                    type = text.Substring(colonIdx + 1).Trim();
                }
                else
                {
                    // Try "Type name" pattern
                    var parts = text.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        // Mermaid convention: name Type (name first)
                        name = parts[0];
                        type = parts[1];
                    }
                    else
                    {
                        name = text;
                    }
                }
            }

            var member = new ClassMember
            {
                Name = name,
                Type = type,
                IsMethod = isMethod,
                Visibility = visibility
            };

            if (isMethod)
                cls.Methods.Add(member);
            else
                cls.Fields.Add(member);
        }

        private ClassNode EnsureClass(string name)
        {
            if (_classMap.TryGetValue(name, out var existing))
                return existing;

            var cls = new ClassNode(name);
            _classMap[name] = cls;
            return cls;
        }
    }
}
