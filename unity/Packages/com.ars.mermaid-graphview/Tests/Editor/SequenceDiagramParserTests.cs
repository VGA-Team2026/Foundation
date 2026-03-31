using System.Linq;
using NUnit.Framework;

namespace Ars.MermaidGraphView.Tests
{
    [TestFixture]
    public class SequenceDiagramParserTests
    {
        private SequenceDiagramDocument ParseSequenceDiagram(string source)
        {
            var parser = ParserFactory.Create(source);
            var doc = parser.Parse(source);
            Assert.AreEqual(DiagramType.SequenceDiagram, doc.DiagramType);
            Assert.IsNotNull(doc.Content);
            return (SequenceDiagramDocument)doc.Content;
        }

        [Test]
        public void Parse_ParticipantDeclarations_CreatesParticipants()
        {
            var source = @"sequenceDiagram
    participant A
    participant B";

            var diagram = ParseSequenceDiagram(source);

            Assert.AreEqual(2, diagram.Participants.Count);
            Assert.IsNotNull(diagram.Participants.FirstOrDefault(p => p.Id == "A"));
            Assert.IsNotNull(diagram.Participants.FirstOrDefault(p => p.Id == "B"));
        }

        [Test]
        public void Parse_Messages_CreatesMessageElements()
        {
            var source = @"sequenceDiagram
    participant Alice
    participant Bob
    Alice->>Bob: Hello
    Bob-->>Alice: Hi back";

            var diagram = ParseSequenceDiagram(source);

            var messages = diagram.Elements.OfType<SequenceMessage>().ToList();
            Assert.That(messages.Count, Is.GreaterThanOrEqualTo(2));

            var hello = messages.FirstOrDefault(m => m.Text != null && m.Text.Contains("Hello"));
            Assert.IsNotNull(hello, "Should have a message containing 'Hello'");
            Assert.AreEqual("Alice", hello.FromId);
            Assert.AreEqual("Bob", hello.ToId);
        }

        [Test]
        public void Parse_LoopBlock_CreatesSequenceBlock()
        {
            var source = @"sequenceDiagram
    participant A
    participant B
    loop Every minute
        A->>B: Ping
        B-->>A: Pong
    end";

            var diagram = ParseSequenceDiagram(source);

            var blocks = diagram.Elements.OfType<SequenceBlock>().ToList();
            Assert.That(blocks.Count, Is.GreaterThanOrEqualTo(1));

            var loopBlock = blocks.FirstOrDefault(b => b.Kind == BlockKind.Loop);
            Assert.IsNotNull(loopBlock, "Should have a loop block");
            Assert.That(loopBlock.Label, Does.Contain("Every minute"));
        }

        [Test]
        public void Parse_AltElseBlock_CreatesSections()
        {
            var source = @"sequenceDiagram
    participant A
    participant B
    alt Success
        A->>B: OK
    else Failure
        A->>B: Error
    end";

            var diagram = ParseSequenceDiagram(source);

            var blocks = diagram.Elements.OfType<SequenceBlock>().ToList();
            Assert.That(blocks.Count, Is.GreaterThanOrEqualTo(1));

            var altBlock = blocks.FirstOrDefault(b => b.Kind == BlockKind.Alt);
            Assert.IsNotNull(altBlock, "Should have an alt block");
            Assert.That(altBlock.Sections.Count, Is.GreaterThanOrEqualTo(2),
                "Alt block should have at least 2 sections (alt + else)");
        }

        [Test]
        public void Parse_Notes_CreatesNoteElements()
        {
            var source = @"sequenceDiagram
    participant A
    participant B
    Note right of A: Important note
    A->>B: Message";

            var diagram = ParseSequenceDiagram(source);

            var notes = diagram.Elements.OfType<SequenceNote>().ToList();
            Assert.That(notes.Count, Is.GreaterThanOrEqualTo(1));

            var note = notes[0];
            Assert.That(note.Text, Does.Contain("Important"));
        }

        [Test]
        public void Parse_DottedArrow_SetsMessageStyle()
        {
            var source = @"sequenceDiagram
    participant A
    participant B
    A-->>B: Dotted message";

            var diagram = ParseSequenceDiagram(source);

            var messages = diagram.Elements.OfType<SequenceMessage>().ToList();
            Assert.That(messages.Count, Is.GreaterThanOrEqualTo(1));

            var dottedMessage = messages.FirstOrDefault(m => m.Text != null && m.Text.Contains("Dotted"));
            Assert.IsNotNull(dottedMessage, "Should have a dotted message");
            Assert.AreEqual(MessageStyle.Dotted, dottedMessage.Style);
        }

        [Test]
        public void Parse_SolidArrow_SetsMessageStyle()
        {
            var source = @"sequenceDiagram
    participant A
    participant B
    A->>B: Solid message";

            var diagram = ParseSequenceDiagram(source);

            var messages = diagram.Elements.OfType<SequenceMessage>().ToList();
            Assert.That(messages.Count, Is.GreaterThanOrEqualTo(1));

            var solidMessage = messages.FirstOrDefault(m => m.Text != null && m.Text.Contains("Solid"));
            Assert.IsNotNull(solidMessage, "Should have a solid message");
            Assert.AreEqual(MessageStyle.Solid, solidMessage.Style);
        }

        [Test]
        public void Parse_ParticipantAlias_SetsAlias()
        {
            var source = @"sequenceDiagram
    participant A as Alice
    participant B as Bob
    A->>B: Hello";

            var diagram = ParseSequenceDiagram(source);

            var participantA = diagram.Participants.FirstOrDefault(p => p.Id == "A");
            Assert.IsNotNull(participantA);
            Assert.AreEqual("Alice", participantA.Alias);
        }

        [Test]
        public void Parse_ActorDeclaration_SetsIsActor()
        {
            var source = @"sequenceDiagram
    actor User
    participant System
    User->>System: Request";

            var diagram = ParseSequenceDiagram(source);

            var user = diagram.Participants.FirstOrDefault(p => p.Id == "User");
            Assert.IsNotNull(user);
            Assert.IsTrue(user.IsActor, "User should be marked as actor");
        }
    }
}
