using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace MermaidGraphView.Tests
{
    [TestFixture]
    public class TokenizerTests
    {
        private MermaidTokenizer _tokenizer;

        [SetUp]
        public void SetUp()
        {
            _tokenizer = new MermaidTokenizer();
        }

        [Test]
        public void Tokenize_SimpleFlowchart_ReturnsExpectedTokens()
        {
            var source = "flowchart TD\n    A[Text] --> B";
            var tokens = _tokenizer.Tokenize(source);

            var meaningful = tokens.Where(t => t.Kind != TokenKind.Newline && t.Kind != TokenKind.EOF).ToList();

            Assert.That(meaningful[0].Kind, Is.EqualTo(TokenKind.Keyword));
            Assert.That(meaningful[0].Value, Is.EqualTo("flowchart"));

            Assert.That(meaningful[1].Kind, Is.EqualTo(TokenKind.Direction));
            Assert.That(meaningful[1].Value, Is.EqualTo("TD"));

            Assert.That(meaningful[2].Kind, Is.EqualTo(TokenKind.Identifier));
            Assert.That(meaningful[2].Value, Is.EqualTo("A"));

            Assert.That(meaningful[3].Kind, Is.EqualTo(TokenKind.OpenBracket));
            Assert.That(meaningful[3].Value, Is.EqualTo("["));

            Assert.That(meaningful[4].Kind, Is.EqualTo(TokenKind.Identifier));
            Assert.That(meaningful[4].Value, Is.EqualTo("Text"));

            Assert.That(meaningful[5].Kind, Is.EqualTo(TokenKind.CloseBracket));
            Assert.That(meaningful[5].Value, Is.EqualTo("]"));

            Assert.That(meaningful[6].Kind, Is.EqualTo(TokenKind.Arrow));
            Assert.That(meaningful[6].Value, Is.EqualTo("-->"));

            Assert.That(meaningful[7].Kind, Is.EqualTo(TokenKind.Identifier));
            Assert.That(meaningful[7].Value, Is.EqualTo("B"));
        }

        [Test]
        public void Tokenize_Arrows_IdentifiesDifferentArrowTypes()
        {
            var source = "flowchart TD\nA --> B\nC -.-> D\nE ==> F";
            var tokens = _tokenizer.Tokenize(source);

            var arrows = tokens.Where(t => t.Kind == TokenKind.Arrow).ToList();

            Assert.AreEqual(3, arrows.Count);
            Assert.AreEqual("-->", arrows[0].Value);
            Assert.AreEqual("-.->", arrows[1].Value);
            Assert.AreEqual("==>", arrows[2].Value);
        }

        [Test]
        public void Tokenize_NodeShapes_BracketTypes()
        {
            var source = "flowchart TD\nA[rect]\nB(rounded)\nC{diamond}\nD((circle))";
            var tokens = _tokenizer.Tokenize(source);

            var openBrackets = tokens.Where(t => t.Kind == TokenKind.OpenBracket).ToList();

            Assert.That(openBrackets.Any(t => t.Value == "["), Is.True, "Should have [ bracket");
            Assert.That(openBrackets.Any(t => t.Value == "("), Is.True, "Should have ( bracket");
            Assert.That(openBrackets.Any(t => t.Value == "{"), Is.True, "Should have { brace as OpenBrace");
            Assert.That(openBrackets.Any(t => t.Value == "(("), Is.True, "Should have (( bracket");
        }

        [Test]
        public void Tokenize_Comment_IsSkippedAsCommentToken()
        {
            var source = "flowchart TD\n%% This is a comment\nA --> B";
            var tokens = _tokenizer.Tokenize(source);

            var comments = tokens.Where(t => t.Kind == TokenKind.Comment).ToList();
            Assert.AreEqual(1, comments.Count);
            Assert.That(comments[0].Value, Does.StartWith("%%"));

            // Ensure the comment content is captured
            Assert.That(comments[0].Value, Does.Contain("This is a comment"));
        }

        [Test]
        public void Tokenize_StringLiteral_CapturesQuotedText()
        {
            var source = "sequenceDiagram\nparticipant A as \"Alice Server\"";
            var tokens = _tokenizer.Tokenize(source);

            var stringLiterals = tokens.Where(t => t.Kind == TokenKind.StringLiteral).ToList();

            Assert.AreEqual(1, stringLiterals.Count);
            Assert.AreEqual("Alice Server", stringLiterals[0].Value);
        }

        [Test]
        public void Tokenize_EmptyInput_ReturnsOnlyEOF()
        {
            var tokens = _tokenizer.Tokenize("");

            Assert.AreEqual(1, tokens.Count);
            Assert.AreEqual(TokenKind.EOF, tokens[0].Kind);
        }

        [Test]
        public void Tokenize_WhitespaceOnlyInput_ReturnsOnlyEOF()
        {
            var tokens = _tokenizer.Tokenize("   \t  ");

            // Should contain only EOF (and possibly whitespace-related tokens but no meaningful content)
            var meaningful = tokens.Where(t => t.Kind != TokenKind.Newline && t.Kind != TokenKind.EOF).ToList();
            Assert.AreEqual(0, meaningful.Count);
        }

        [Test]
        public void Tokenize_PipeLabel_CapturesLabelContent()
        {
            var source = "flowchart TD\nA -->|Yes| B";
            var tokens = _tokenizer.Tokenize(source);

            var pipes = tokens.Where(t => t.Kind == TokenKind.Pipe).ToList();

            Assert.AreEqual(1, pipes.Count);
            Assert.AreEqual("Yes", pipes[0].Value);
        }

        [Test]
        public void Tokenize_StarBracket_ForStateDiagram()
        {
            var source = "stateDiagram-v2\n[*] --> State1";
            var tokens = _tokenizer.Tokenize(source);

            var identifiers = tokens.Where(t => t.Kind == TokenKind.Identifier).ToList();
            Assert.That(identifiers.Any(t => t.Value == "[*]"), Is.True,
                "Should tokenize [*] as an identifier");
        }

        [Test]
        public void Tokenize_TracksLineAndColumn()
        {
            var source = "flowchart TD\nA --> B";
            var tokens = _tokenizer.Tokenize(source);

            var flowchartToken = tokens.First(t => t.Value == "flowchart");
            Assert.AreEqual(1, flowchartToken.Line);

            var aToken = tokens.First(t => t.Value == "A");
            Assert.AreEqual(2, aToken.Line);
        }
    }
}
