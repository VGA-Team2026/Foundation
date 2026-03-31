using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Ars.MermaidGraphView.Tests
{
    [TestFixture]
    public class SugiyamaLayoutTests
    {
        private SugiyamaLayoutEngine _engine;

        [SetUp]
        public void SetUp()
        {
            _engine = new SugiyamaLayoutEngine();
        }

        private LayoutGraph CreateGraph(string[] nodeIds, (string from, string to)[] edges)
        {
            var graph = new LayoutGraph();
            foreach (var id in nodeIds)
            {
                graph.Nodes.Add(new LayoutNode
                {
                    Id = id,
                    Size = new Vector2(150, 40)
                });
            }

            int idx = 0;
            foreach (var (from, to) in edges)
            {
                graph.Edges.Add(new LayoutEdge
                {
                    SourceId = from,
                    TargetId = to,
                    Id = $"edge_{idx++}"
                });
            }

            return graph;
        }

        [Test]
        public void Calculate_LinearGraph_AssignsIncreasingLayers()
        {
            var graph = CreateGraph(
                new[] { "A", "B", "C" },
                new[] { ("A", "B"), ("B", "C") }
            );

            var config = new LayoutConfig { Direction = FlowDirection.TB };
            var result = _engine.Calculate(graph, config);

            Assert.AreEqual(3, result.NodePositions.Count);
            Assert.That(result.NodePositions.ContainsKey("A"), Is.True);
            Assert.That(result.NodePositions.ContainsKey("B"), Is.True);
            Assert.That(result.NodePositions.ContainsKey("C"), Is.True);

            // In TB direction, Y should increase for each layer
            float yA = result.NodePositions["A"].y;
            float yB = result.NodePositions["B"].y;
            float yC = result.NodePositions["C"].y;

            Assert.That(yB, Is.GreaterThan(yA), "B should be below A in TB layout");
            Assert.That(yC, Is.GreaterThan(yB), "C should be below B in TB layout");
        }

        [Test]
        public void Calculate_DiamondGraph_AssignsCorrectLayers()
        {
            // A -> B, A -> C, B -> D, C -> D
            var graph = CreateGraph(
                new[] { "A", "B", "C", "D" },
                new[] { ("A", "B"), ("A", "C"), ("B", "D"), ("C", "D") }
            );

            var config = new LayoutConfig { Direction = FlowDirection.TB };
            var result = _engine.Calculate(graph, config);

            Assert.AreEqual(4, result.NodePositions.Count);

            float yA = result.NodePositions["A"].y;
            float yB = result.NodePositions["B"].y;
            float yC = result.NodePositions["C"].y;
            float yD = result.NodePositions["D"].y;

            // A should be at the top
            Assert.That(yB, Is.GreaterThan(yA), "B should be below A");
            Assert.That(yC, Is.GreaterThan(yA), "C should be below A");

            // B and C should be at the same layer
            Assert.AreEqual(yB, yC, "B and C should be at the same layer");

            // D should be below B and C
            Assert.That(yD, Is.GreaterThan(yB), "D should be below B");
        }

        [Test]
        public void Calculate_CyclicGraph_HandlesGracefully()
        {
            // A -> B -> A (cycle)
            var graph = CreateGraph(
                new[] { "A", "B" },
                new[] { ("A", "B"), ("B", "A") }
            );

            var config = new LayoutConfig { Direction = FlowDirection.TB };

            // Should not throw
            LayoutResult result = null;
            Assert.DoesNotThrow(() => result = _engine.Calculate(graph, config));

            Assert.IsNotNull(result);
            Assert.AreEqual(2, result.NodePositions.Count);
            Assert.That(result.NodePositions.ContainsKey("A"), Is.True);
            Assert.That(result.NodePositions.ContainsKey("B"), Is.True);
        }

        [Test]
        public void Calculate_AllNodesGetPositions()
        {
            var graph = CreateGraph(
                new[] { "N1", "N2", "N3", "N4", "N5" },
                new[] { ("N1", "N2"), ("N2", "N3"), ("N3", "N4"), ("N4", "N5") }
            );

            var config = new LayoutConfig { Direction = FlowDirection.TD };
            var result = _engine.Calculate(graph, config);

            Assert.AreEqual(5, result.NodePositions.Count);
            foreach (var nodeId in new[] { "N1", "N2", "N3", "N4", "N5" })
            {
                Assert.That(result.NodePositions.ContainsKey(nodeId), Is.True,
                    $"Node {nodeId} should have a position");
            }
        }

        [Test]
        public void Calculate_LRDirection_AssignsIncreasingX()
        {
            var graph = CreateGraph(
                new[] { "A", "B", "C" },
                new[] { ("A", "B"), ("B", "C") }
            );

            var config = new LayoutConfig { Direction = FlowDirection.LR };
            var result = _engine.Calculate(graph, config);

            float xA = result.NodePositions["A"].x;
            float xB = result.NodePositions["B"].x;
            float xC = result.NodePositions["C"].x;

            Assert.That(xB, Is.GreaterThan(xA), "B should be right of A in LR layout");
            Assert.That(xC, Is.GreaterThan(xB), "C should be right of B in LR layout");
        }

        [Test]
        public void Calculate_EmptyGraph_ReturnsEmptyResult()
        {
            var graph = new LayoutGraph();
            var config = new LayoutConfig { Direction = FlowDirection.TB };
            var result = _engine.Calculate(graph, config);

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.NodePositions.Count);
        }

        [Test]
        public void Calculate_DisconnectedNodes_AllGetPositions()
        {
            var graph = CreateGraph(
                new[] { "A", "B", "C" },
                new (string, string)[] { } // no edges
            );

            var config = new LayoutConfig { Direction = FlowDirection.TB };
            var result = _engine.Calculate(graph, config);

            Assert.AreEqual(3, result.NodePositions.Count);
            Assert.That(result.NodePositions.ContainsKey("A"), Is.True);
            Assert.That(result.NodePositions.ContainsKey("B"), Is.True);
            Assert.That(result.NodePositions.ContainsKey("C"), Is.True);
        }
    }
}
