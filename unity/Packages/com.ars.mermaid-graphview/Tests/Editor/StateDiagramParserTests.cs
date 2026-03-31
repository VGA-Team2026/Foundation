using System.Linq;
using NUnit.Framework;

namespace Ars.MermaidGraphView.Tests
{
    [TestFixture]
    public class StateDiagramParserTests
    {
        private StateDiagramParser _parser;

        [SetUp]
        public void SetUp()
        {
            _parser = new StateDiagramParser();
        }

        private StateDiagramDocument ParseStateDiagram(string source)
        {
            var doc = _parser.Parse(source);
            Assert.AreEqual(DiagramType.StateDiagram, doc.DiagramType);
            Assert.IsNotNull(doc.Content);
            return (StateDiagramDocument)doc.Content;
        }

        [Test]
        public void Parse_BasicTransitions_CreatesStatesAndTransitions()
        {
            var source = @"stateDiagram-v2
    State1 --> State2
    State2 --> State3";

            var diagram = ParseStateDiagram(source);

            Assert.AreEqual(3, diagram.States.Count);
            Assert.AreEqual(2, diagram.Transitions.Count);

            Assert.IsNotNull(diagram.States.FirstOrDefault(s => s.Id == "State1"));
            Assert.IsNotNull(diagram.States.FirstOrDefault(s => s.Id == "State2"));
            Assert.IsNotNull(diagram.States.FirstOrDefault(s => s.Id == "State3"));

            var t1 = diagram.Transitions[0];
            Assert.AreEqual("State1", t1.FromId);
            Assert.AreEqual("State2", t1.ToId);
        }

        [Test]
        public void Parse_StartAndEndStates_RecognizesStarBracket()
        {
            var source = @"stateDiagram-v2
    [*] --> Active
    Active --> [*]";

            var diagram = ParseStateDiagram(source);

            Assert.IsNotNull(diagram.States.FirstOrDefault(s => s.Id == "[*]"),
                "Should have a [*] state");
            Assert.IsNotNull(diagram.States.FirstOrDefault(s => s.Id == "Active"),
                "Should have an Active state");

            Assert.AreEqual(2, diagram.Transitions.Count);

            var startTransition = diagram.Transitions.First(t => t.FromId == "[*]");
            Assert.AreEqual("Active", startTransition.ToId);

            var endTransition = diagram.Transitions.First(t => t.ToId == "[*]");
            Assert.AreEqual("Active", endTransition.FromId);
        }

        [Test]
        public void Parse_CompositeState_CreatesChildStates()
        {
            var source = @"stateDiagram-v2
    state Parent {
        Child1 --> Child2
        Child2 --> Child3
    }";

            var diagram = ParseStateDiagram(source);

            var parent = diagram.States.FirstOrDefault(s => s.Id == "Parent");
            Assert.IsNotNull(parent, "Should have Parent composite state");
            Assert.That(parent.Children.Count, Is.GreaterThanOrEqualTo(2),
                "Parent should have child states");

            Assert.That(parent.InternalTransitions.Count, Is.GreaterThanOrEqualTo(2),
                "Parent should have internal transitions");
        }

        [Test]
        public void Parse_TransitionLabel_CapturesLabelText()
        {
            var source = @"stateDiagram-v2
    Idle --> Processing : Start work
    Processing --> Done : Complete";

            var diagram = ParseStateDiagram(source);

            var t1 = diagram.Transitions.First(t => t.FromId == "Idle");
            Assert.AreEqual("Start work", t1.Label);

            var t2 = diagram.Transitions.First(t => t.FromId == "Processing");
            Assert.AreEqual("Complete", t2.Label);
        }

        [Test]
        public void Parse_ForkState_SetsCorrectKind()
        {
            var source = @"stateDiagram-v2
    state fork1 <<fork>>
    State1 --> fork1
    fork1 --> State2
    fork1 --> State3";

            var diagram = ParseStateDiagram(source);

            var fork = diagram.States.FirstOrDefault(s => s.Id == "fork1");
            Assert.IsNotNull(fork);
            Assert.AreEqual(StateKind.Fork, fork.Kind);
        }

        [Test]
        public void Parse_JoinState_SetsCorrectKind()
        {
            var source = @"stateDiagram-v2
    state join1 <<join>>
    State2 --> join1
    State3 --> join1
    join1 --> State4";

            var diagram = ParseStateDiagram(source);

            var join = diagram.States.FirstOrDefault(s => s.Id == "join1");
            Assert.IsNotNull(join);
            Assert.AreEqual(StateKind.Join, join.Kind);
        }

        [Test]
        public void Parse_ChoiceState_SetsCorrectKind()
        {
            var source = @"stateDiagram-v2
    state check <<choice>>
    State1 --> check
    check --> State2 : Yes
    check --> State3 : No";

            var diagram = ParseStateDiagram(source);

            var choice = diagram.States.FirstOrDefault(s => s.Id == "check");
            Assert.IsNotNull(choice);
            Assert.AreEqual(StateKind.Choice, choice.Kind);
        }

        [Test]
        public void Parse_StateAlias_SetsLabel()
        {
            var source = @"stateDiagram-v2
    state ""Long State Name"" as s1
    s1 --> s2";

            var diagram = ParseStateDiagram(source);

            var s1 = diagram.States.FirstOrDefault(s => s.Id == "s1");
            Assert.IsNotNull(s1);
            Assert.AreEqual("Long State Name", s1.Label);
        }
    }
}
