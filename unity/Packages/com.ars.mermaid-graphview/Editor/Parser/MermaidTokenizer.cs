using System;
using System.Collections.Generic;
using System.Text;

namespace Ars.MermaidGraphView
{
    public class MermaidTokenizer
    {
        private string _source;
        private int _pos;
        private int _line;
        private int _column;
        private List<MermaidToken> _tokens;

        private static readonly HashSet<string> DiagramKeywords = new HashSet<string>
        {
            "flowchart", "graph", "stateDiagram", "stateDiagram-v2",
            "classDiagram", "sequenceDiagram"
        };

        private static readonly HashSet<string> DirectionKeywords = new HashSet<string>
        {
            "TB", "TD", "BT", "RL", "LR"
        };

        private static readonly HashSet<string> SequenceKeywords = new HashSet<string>
        {
            "participant", "actor"
        };

        private static readonly HashSet<string> ClassDiagramKeywords = new HashSet<string>
        {
            "class"
        };

        private static readonly string[] ArrowPatterns = new string[]
        {
            // Sequence arrows (must check before flowchart arrows)
            "-->>", "->>", "--x", "-x", "--)",  "-)","-->>+", "->>+", "-->>-", "->>-",
            // Flowchart arrows (longer patterns first)
            "===>", "===", "==>", "-..->", "-.->", "--->", "-->", "---", "-.->",
            "--", ".->", "~~>",
        };

        private static readonly string[] ClassRelationPatterns = new string[]
        {
            "<|..", "..|>", "<|--", "--|>", "*--", "--*", "o--", "--o",
            "<--", "-->", "..|>", "<|..", "..>", "<..", "--", ".."
        };

        public List<MermaidToken> Tokenize(string source)
        {
            _source = source.Replace("\r\n", "\n").Replace("\r", "\n");
            _pos = 0;
            _line = 1;
            _column = 1;
            _tokens = new List<MermaidToken>();

            while (_pos < _source.Length)
            {
                TokenizeLine();
            }

            _tokens.Add(new MermaidToken(TokenKind.EOF, "", _line, _column));
            return _tokens;
        }

        private void TokenizeLine()
        {
            SkipWhitespaceInLine();

            if (_pos >= _source.Length) return;

            // Newline
            if (_source[_pos] == '\n')
            {
                _tokens.Add(new MermaidToken(TokenKind.Newline, "\n", _line, _column));
                _pos++;
                _line++;
                _column = 1;
                return;
            }

            // Comment line
            if (Peek("%%"))
            {
                int start = _pos;
                while (_pos < _source.Length && _source[_pos] != '\n')
                    Advance();
                _tokens.Add(new MermaidToken(TokenKind.Comment, _source.Substring(start, _pos - start), _line, _column));
                return;
            }

            // Tokenize rest of the line
            while (_pos < _source.Length && _source[_pos] != '\n')
            {
                SkipWhitespaceInLine();
                if (_pos >= _source.Length || _source[_pos] == '\n') break;

                if (TryTokenizeStringLiteral()) continue;
                if (TryTokenizePipe()) continue;
                if (TryTokenizeAnnotation()) continue;
                if (TryTokenizeSingleChar()) continue;
                if (TryTokenizeArrowOrRelation()) continue;
                if (TryTokenizeWord()) continue;

                // Fallback: skip unknown character
                Advance();
            }
        }

        private bool TryTokenizeStringLiteral()
        {
            if (_pos >= _source.Length) return false;
            char ch = _source[_pos];
            if (ch != '"' && ch != '\'') return false;

            char quote = ch;
            int startLine = _line;
            int startCol = _column;
            Advance(); // skip opening quote
            var sb = new StringBuilder();
            while (_pos < _source.Length && _source[_pos] != quote)
            {
                if (_source[_pos] == '\\' && _pos + 1 < _source.Length)
                {
                    Advance();
                    sb.Append(_source[_pos]);
                }
                else
                {
                    sb.Append(_source[_pos]);
                }
                Advance();
            }
            if (_pos < _source.Length) Advance(); // skip closing quote
            _tokens.Add(new MermaidToken(TokenKind.StringLiteral, sb.ToString(), startLine, startCol));
            return true;
        }

        private bool TryTokenizePipe()
        {
            if (_pos >= _source.Length || _source[_pos] != '|') return false;

            int startLine = _line;
            int startCol = _column;
            Advance(); // skip opening |
            var sb = new StringBuilder();
            while (_pos < _source.Length && _source[_pos] != '|' && _source[_pos] != '\n')
            {
                sb.Append(_source[_pos]);
                Advance();
            }
            if (_pos < _source.Length && _source[_pos] == '|') Advance(); // skip closing |
            _tokens.Add(new MermaidToken(TokenKind.Pipe, sb.ToString(), startLine, startCol));
            return true;
        }

        private bool TryTokenizeAnnotation()
        {
            if (!Peek("<<")) return false;
            int startLine = _line;
            int startCol = _column;
            _tokens.Add(new MermaidToken(TokenKind.AnnotationOpen, "<<", startLine, startCol));
            Advance();
            Advance();

            // Read until >>
            var sb = new StringBuilder();
            while (_pos < _source.Length && !Peek(">>"))
            {
                if (_source[_pos] == '\n') break;
                sb.Append(_source[_pos]);
                Advance();
            }

            if (sb.Length > 0)
            {
                _tokens.Add(new MermaidToken(TokenKind.Identifier, sb.ToString().Trim(), startLine, startCol + 2));
            }

            if (Peek(">>"))
            {
                _tokens.Add(new MermaidToken(TokenKind.AnnotationClose, ">>", _line, _column));
                Advance();
                Advance();
            }
            return true;
        }

        private bool TryTokenizeSingleChar()
        {
            if (_pos >= _source.Length) return false;
            char ch = _source[_pos];
            TokenKind? kind = null;
            switch (ch)
            {
                case ':': kind = TokenKind.Colon; break;
                case '{':
                    // Check for double braces {{
                    if (_pos + 1 < _source.Length && _source[_pos + 1] == '{')
                    {
                        _tokens.Add(new MermaidToken(TokenKind.OpenBrace, "{{", _line, _column));
                        Advance(); Advance();
                        return true;
                    }
                    kind = TokenKind.OpenBrace;
                    break;
                case '}':
                    if (_pos + 1 < _source.Length && _source[_pos + 1] == '}')
                    {
                        _tokens.Add(new MermaidToken(TokenKind.CloseBrace, "}}", _line, _column));
                        Advance(); Advance();
                        return true;
                    }
                    kind = TokenKind.CloseBrace;
                    break;
                case '[':
                    // Check for [[ ]] subroutine
                    if (_pos + 1 < _source.Length && _source[_pos + 1] == '[')
                    {
                        _tokens.Add(new MermaidToken(TokenKind.OpenBracket, "[[", _line, _column));
                        Advance(); Advance();
                        return true;
                    }
                    kind = TokenKind.OpenBracket;
                    break;
                case ']':
                    if (_pos + 1 < _source.Length && _source[_pos + 1] == ']')
                    {
                        _tokens.Add(new MermaidToken(TokenKind.CloseBracket, "]]", _line, _column));
                        Advance(); Advance();
                        return true;
                    }
                    kind = TokenKind.CloseBracket;
                    break;
                case '(':
                    if (_pos + 1 < _source.Length && _source[_pos + 1] == '(')
                    {
                        _tokens.Add(new MermaidToken(TokenKind.OpenBracket, "((", _line, _column));
                        Advance(); Advance();
                        return true;
                    }
                    if (_pos + 1 < _source.Length && _source[_pos + 1] == '[')
                    {
                        _tokens.Add(new MermaidToken(TokenKind.OpenBracket, "([", _line, _column));
                        Advance(); Advance();
                        return true;
                    }
                    kind = TokenKind.OpenBracket;
                    break;
                case ')':
                    if (_pos + 1 < _source.Length && _source[_pos + 1] == ')')
                    {
                        _tokens.Add(new MermaidToken(TokenKind.CloseBracket, "))", _line, _column));
                        Advance(); Advance();
                        return true;
                    }
                    kind = TokenKind.CloseBracket;
                    break;
                case ';': kind = TokenKind.Semicolon; break;
                case ',': kind = TokenKind.Comma; break;
            }

            if (kind.HasValue)
            {
                _tokens.Add(new MermaidToken(kind.Value, ch.ToString(), _line, _column));
                Advance();
                return true;
            }
            return false;
        }

        private bool TryTokenizeArrowOrRelation()
        {
            if (_pos >= _source.Length) return false;
            char ch = _source[_pos];

            // Only try if starts with -, =, ., <, *, o, ~
            if (ch != '-' && ch != '=' && ch != '.' && ch != '<' && ch != '*' && ch != 'o' && ch != '~')
                return false;

            // Try class diagram relationships first
            foreach (var pattern in ClassRelationPatterns)
            {
                if (PeekString(pattern))
                {
                    // Make sure it's not just the start of a longer word
                    int afterEnd = _pos + pattern.Length;
                    if (afterEnd < _source.Length && IsIdentChar(_source[afterEnd]) && !IsArrowChar(_source[afterEnd]))
                        continue;

                    _tokens.Add(new MermaidToken(TokenKind.Relationship, pattern, _line, _column));
                    for (int i = 0; i < pattern.Length; i++) Advance();
                    return true;
                }
            }

            // Try flowchart/sequence arrows
            foreach (var pattern in ArrowPatterns)
            {
                if (PeekString(pattern))
                {
                    _tokens.Add(new MermaidToken(TokenKind.Arrow, pattern, _line, _column));
                    for (int i = 0; i < pattern.Length; i++) Advance();
                    return true;
                }
            }

            return false;
        }

        private bool TryTokenizeWord()
        {
            if (_pos >= _source.Length) return false;
            char ch = _source[_pos];
            if (!IsIdentStartChar(ch) && ch != '[' && ch != '*') return false;

            // Handle [*] for state diagrams
            if (ch == '[' && _pos + 2 < _source.Length && _source[_pos + 1] == '*' && _source[_pos + 2] == ']')
            {
                _tokens.Add(new MermaidToken(TokenKind.Identifier, "[*]", _line, _column));
                Advance(); Advance(); Advance();
                return true;
            }

            // Visibility markers for class diagrams
            if ((ch == '+' || ch == '-' || ch == '#' || ch == '~') && _pos + 1 < _source.Length && IsIdentStartChar(_source[_pos + 1]))
            {
                _tokens.Add(new MermaidToken(TokenKind.Visibility, ch.ToString(), _line, _column));
                Advance();
                return true;
            }

            int startLine = _line;
            int startCol = _column;
            var sb = new StringBuilder();

            while (_pos < _source.Length && IsIdentChar(_source[_pos]))
            {
                sb.Append(_source[_pos]);
                Advance();
            }

            if (sb.Length == 0) return false;

            string word = sb.ToString();

            // Classify the word
            if (DiagramKeywords.Contains(word))
            {
                _tokens.Add(new MermaidToken(TokenKind.Keyword, word, startLine, startCol));
            }
            else if (DirectionKeywords.Contains(word))
            {
                _tokens.Add(new MermaidToken(TokenKind.Direction, word, startLine, startCol));
            }
            else if (SequenceKeywords.Contains(word))
            {
                _tokens.Add(new MermaidToken(TokenKind.Keyword, word, startLine, startCol));
            }
            else if (ClassDiagramKeywords.Contains(word))
            {
                _tokens.Add(new MermaidToken(TokenKind.Keyword, word, startLine, startCol));
            }
            else if (word == "subgraph" || word == "state")
            {
                _tokens.Add(new MermaidToken(TokenKind.Keyword, word, startLine, startCol));
            }
            else if (word == "end")
            {
                _tokens.Add(new MermaidToken(TokenKind.End, word, startLine, startCol));
            }
            else if (word == "activate")
            {
                _tokens.Add(new MermaidToken(TokenKind.Activate, word, startLine, startCol));
            }
            else if (word == "deactivate")
            {
                _tokens.Add(new MermaidToken(TokenKind.Deactivate, word, startLine, startCol));
            }
            else if (word == "loop")
            {
                _tokens.Add(new MermaidToken(TokenKind.Loop, word, startLine, startCol));
            }
            else if (word == "alt")
            {
                _tokens.Add(new MermaidToken(TokenKind.Alt, word, startLine, startCol));
            }
            else if (word == "else")
            {
                _tokens.Add(new MermaidToken(TokenKind.Else, word, startLine, startCol));
            }
            else if (word == "opt")
            {
                _tokens.Add(new MermaidToken(TokenKind.Opt, word, startLine, startCol));
            }
            else if (word == "par")
            {
                _tokens.Add(new MermaidToken(TokenKind.Par, word, startLine, startCol));
            }
            else if (word == "Note" || word == "note")
            {
                _tokens.Add(new MermaidToken(TokenKind.Note, word, startLine, startCol));
            }
            else
            {
                _tokens.Add(new MermaidToken(TokenKind.Identifier, word, startLine, startCol));
            }

            return true;
        }

        private void SkipWhitespaceInLine()
        {
            while (_pos < _source.Length && (_source[_pos] == ' ' || _source[_pos] == '\t'))
                Advance();
        }

        private void Advance()
        {
            if (_pos < _source.Length)
            {
                _pos++;
                _column++;
            }
        }

        private bool Peek(string s)
        {
            return PeekString(s);
        }

        private bool PeekString(string s)
        {
            if (_pos + s.Length > _source.Length) return false;
            for (int i = 0; i < s.Length; i++)
            {
                if (_source[_pos + i] != s[i]) return false;
            }
            return true;
        }

        private static bool IsIdentStartChar(char c)
        {
            return char.IsLetter(c) || c == '_';
        }

        private static bool IsIdentChar(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.';
        }

        private static bool IsArrowChar(char c)
        {
            return c == '-' || c == '>' || c == '<' || c == '=' || c == '.' || c == '|' || c == '*' || c == 'o';
        }
    }
}
