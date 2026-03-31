using System.Linq;
using NUnit.Framework;

namespace MermaidGraphView.Tests
{
    [TestFixture]
    public class FlowchartParserTests
    {
        private FlowchartParser _parser;

        [SetUp]
        public void SetUp()
        {
            _parser = new FlowchartParser();
        }

        private FlowchartDocument ParseFlowchart(string source)
        {
            var doc = _parser.Parse(source);
            Assert.AreEqual(DiagramType.Flowchart, doc.DiagramType);
            Assert.IsNotNull(doc.Content);
            return (FlowchartDocument)doc.Content;
        }

        [Test]
        public void Parse_BasicFlowchart_ExtractsNodesEdgesAndDirection()
        {
            var source = @"flowchart TD
    A[Start] --> B[Process]
    B --> C[End]";

            var flowchart = ParseFlowchart(source);

            Assert.AreEqual(FlowDirection.TD, flowchart.Direction);
            Assert.AreEqual(3, flowchart.Nodes.Count);
            Assert.AreEqual(2, flowchart.Edges.Count);

            Assert.IsNotNull(flowchart.Nodes.FirstOrDefault(n => n.Id == "A"));
            Assert.IsNotNull(flowchart.Nodes.FirstOrDefault(n => n.Id == "B"));
            Assert.IsNotNull(flowchart.Nodes.FirstOrDefault(n => n.Id == "C"));

            var nodeA = flowchart.Nodes.First(n => n.Id == "A");
            Assert.AreEqual("Start", nodeA.Label);
        }

        [Test]
        public void Parse_Subgraph_CreatesSubgraphWithNodeIds()
        {
            var source = @"flowchart TD
    subgraph sg1 [My Group]
        A --> B
    end
    C --> A";

            var flowchart = ParseFlowchart(source);

            Assert.AreEqual(1, flowchart.Subgraphs.Count);
            Assert.AreEqual("sg1", flowchart.Subgraphs[0].Id);
            Assert.AreEqual("My Group", flowchart.Subgraphs[0].Label);
            Assert.That(flowchart.Subgraphs[0].NodeIds, Contains.Item("A"));
            Assert.That(flowchart.Subgraphs[0].NodeIds, Contains.Item("B"));
        }

        [Test]
        public void Parse_EdgeLabel_PipeSyntax()
        {
            var source = @"flowchart TD
    A -->|Yes| B
    A -->|No| C";

            var flowchart = ParseFlowchart(source);

            Assert.AreEqual(2, flowchart.Edges.Count);

            var yesEdge = flowchart.Edges.FirstOrDefault(e => e.Label == "Yes");
            Assert.IsNotNull(yesEdge, "Should have an edge with label 'Yes'");
            Assert.AreEqual("A", yesEdge.SourceId);
            Assert.AreEqual("B", yesEdge.TargetId);

            var noEdge = flowchart.Edges.FirstOrDefault(e => e.Label == "No");
            Assert.IsNotNull(noEdge, "Should have an edge with label 'No'");
            Assert.AreEqual("A", noEdge.SourceId);
            Assert.AreEqual("C", noEdge.TargetId);
        }

        [Test]
        public void Parse_NodeShapes_RecognizesVariousShapes()
        {
            var source = @"flowchart TD
    A[Rectangle]
    B(Rounded)
    C{Diamond}
    D((Circle))";

            var flowchart = ParseFlowchart(source);

            var nodeA = flowchart.Nodes.First(n => n.Id == "A");
            Assert.AreEqual(NodeShape.Rectangle, nodeA.Shape);

            var nodeB = flowchart.Nodes.First(n => n.Id == "B");
            Assert.AreEqual(NodeShape.Rounded, nodeB.Shape);

            var nodeC = flowchart.Nodes.First(n => n.Id == "C");
            Assert.AreEqual(NodeShape.Diamond, nodeC.Shape);

            var nodeD = flowchart.Nodes.First(n => n.Id == "D");
            Assert.AreEqual(NodeShape.Circle, nodeD.Shape);
        }

        [Test]
        public void Parse_AutoCreateNodes_FromEdgeReferences()
        {
            var source = @"flowchart LR
    X --> Y --> Z";

            var flowchart = ParseFlowchart(source);

            Assert.AreEqual(FlowDirection.LR, flowchart.Direction);
            Assert.AreEqual(3, flowchart.Nodes.Count);
            Assert.IsNotNull(flowchart.Nodes.FirstOrDefault(n => n.Id == "X"));
            Assert.IsNotNull(flowchart.Nodes.FirstOrDefault(n => n.Id == "Y"));
            Assert.IsNotNull(flowchart.Nodes.FirstOrDefault(n => n.Id == "Z"));

            Assert.AreEqual(2, flowchart.Edges.Count);
        }

        [Test]
        public void Parse_MultipleEdgesFromSameNode()
        {
            var source = @"flowchart TD
    A --> B
    A --> C
    A --> D";

            var flowchart = ParseFlowchart(source);

            var edgesFromA = flowchart.Edges.Where(e => e.SourceId == "A").ToList();
            Assert.AreEqual(3, edgesFromA.Count);

            var targets = edgesFromA.Select(e => e.TargetId).OrderBy(t => t).ToList();
            Assert.AreEqual("B", targets[0]);
            Assert.AreEqual("C", targets[1]);
            Assert.AreEqual("D", targets[2]);
        }

        [Test]
        public void Parse_DottedAndThickEdges_SetsEdgeStyle()
        {
            var source = @"flowchart TD
    A -.-> B
    C ==> D";

            var flowchart = ParseFlowchart(source);

            var dottedEdge = flowchart.Edges.FirstOrDefault(e => e.SourceId == "A" && e.TargetId == "B");
            Assert.IsNotNull(dottedEdge);
            Assert.AreEqual(EdgeStyle.Dotted, dottedEdge.Style);

            var thickEdge = flowchart.Edges.FirstOrDefault(e => e.SourceId == "C" && e.TargetId == "D");
            Assert.IsNotNull(thickEdge);
            Assert.AreEqual(EdgeStyle.Thick, thickEdge.Style);
        }

        [Test]
        public void Parse_CommentsAreIgnored()
        {
            var source = @"flowchart TD
    %% This is a comment
    A --> B";

            var flowchart = ParseFlowchart(source);

            Assert.AreEqual(2, flowchart.Nodes.Count);
            Assert.AreEqual(1, flowchart.Edges.Count);
        }

        [Test]
        public void Parse_DirectionLR_SetsLeftToRight()
        {
            var source = @"flowchart LR
    A --> B";

            var flowchart = ParseFlowchart(source);
            Assert.AreEqual(FlowDirection.LR, flowchart.Direction);
        }
    }
}
