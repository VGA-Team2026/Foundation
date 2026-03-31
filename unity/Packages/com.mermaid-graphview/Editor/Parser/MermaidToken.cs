using System.Collections.Generic;

namespace MermaidGraphView
{
    public enum TokenKind
    {
        Keyword, Identifier, StringLiteral, Arrow, Colon, OpenBrace, CloseBrace,
        OpenBracket, CloseBracket, Pipe, Comment, Direction, Newline, EOF,
        // Sequence
        Activate, Deactivate, Loop, Alt, Else, Opt, Par, Note, End,
        // ClassDiagram
        Visibility, ReturnType, Relationship,
        // Extras
        Semicolon, Comma, AnnotationOpen, AnnotationClose
    }

    public class MermaidToken
    {
        public TokenKind Kind { get; set; }
        public string Value { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }

        public MermaidToken(TokenKind kind, string value, int line = 0, int column = 0)
        {
            Kind = kind;
            Value = value;
            Line = line;
            Column = column;
        }

        public override string ToString() => $"[{Kind}] \"{Value}\" ({Line}:{Column})";
    }
}
