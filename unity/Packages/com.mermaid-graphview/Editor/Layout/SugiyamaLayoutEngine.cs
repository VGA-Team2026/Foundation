using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MermaidGraphView
{
    public class SugiyamaLayoutEngine : ILayoutEngine
    {
        private const int MaxCrossingIterations = 24;

        public LayoutResult Calculate(LayoutGraph graph, LayoutConfig config)
        {
            if (graph.Nodes.Count == 0)
                return new LayoutResult();

            // Work on copies to avoid mutating the input
            var edges = graph.Edges.Select(e => new LayoutEdge
            {
                SourceId = e.SourceId,
                TargetId = e.TargetId,
                Id = e.Id,
                IsReversed = e.IsReversed
            }).ToList();
            var nodes = graph.Nodes.ToList();

            // Phase 1: Cycle Removal
            var reversedEdges = RemoveCycles(nodes, edges);

            // Phase 2: Layer Assignment
            var layers = AssignLayers(nodes, edges);

            // Phase 3: Insert dummy nodes for long edges
            var dummyNodes = InsertDummyNodes(layers, edges, nodes);

            // Phase 4: Crossing Minimization
            MinimizeCrossings(layers, edges);

            // Phase 5: Coordinate Assignment
            var result = AssignCoordinates(layers, edges, graph, config, dummyNodes);

            // Restore reversed edges
            foreach (var edge in reversedEdges)
            {
                edge.Reverse();
            }

            return result;
        }

        #region Phase 1: Cycle Removal

        private List<LayoutEdge> RemoveCycles(List<LayoutNode> nodes, List<LayoutEdge> edges)
        {
            var reversed = new List<LayoutEdge>();
            var visited = new HashSet<string>();
            var inStack = new HashSet<string>();

            foreach (var node in nodes)
            {
                if (!visited.Contains(node.Id))
                {
                    DfsCycleRemoval(node.Id, edges, visited, inStack, reversed);
                }
            }

            return reversed;
        }

        private void DfsCycleRemoval(string nodeId, List<LayoutEdge> edges,
            HashSet<string> visited, HashSet<string> inStack, List<LayoutEdge> reversed)
        {
            visited.Add(nodeId);
            inStack.Add(nodeId);

            var outEdges = edges.FindAll(e => e.SourceId == nodeId);
            foreach (var edge in outEdges)
            {
                if (inStack.Contains(edge.TargetId))
                {
                    // Back edge found - reverse it
                    edge.Reverse();
                    reversed.Add(edge);
                }
                else if (!visited.Contains(edge.TargetId))
                {
                    DfsCycleRemoval(edge.TargetId, edges, visited, inStack, reversed);
                }
            }

            inStack.Remove(nodeId);
        }

        #endregion

        #region Phase 2: Layer Assignment

        private Dictionary<string, int> AssignLayers(List<LayoutNode> nodes, List<LayoutEdge> edges)
        {
            var layers = new Dictionary<string, int>();
            var nodeIds = new HashSet<string>(nodes.Select(n => n.Id));

            // Find roots (nodes with no incoming edges)
            var hasIncoming = new HashSet<string>(edges
                .Where(e => nodeIds.Contains(e.SourceId) && nodeIds.Contains(e.TargetId))
                .Select(e => e.TargetId));

            var roots = nodes.Where(n => !hasIncoming.Contains(n.Id)).Select(n => n.Id).ToList();

            // If no roots found (all nodes in cycles after reversal), pick the first node
            if (roots.Count == 0 && nodes.Count > 0)
            {
                roots.Add(nodes[0].Id);
            }

            // Longest path from roots
            foreach (var root in roots)
            {
                LongestPathDfs(root, edges, layers, nodeIds, new HashSet<string>());
            }

            // Assign layer 0 to any unvisited nodes
            foreach (var node in nodes)
            {
                if (!layers.ContainsKey(node.Id))
                {
                    layers[node.Id] = 0;
                }
            }

            return layers;
        }

        private int LongestPathDfs(string nodeId, List<LayoutEdge> edges,
            Dictionary<string, int> layers, HashSet<string> nodeIds, HashSet<string> visiting)
        {
            if (layers.ContainsKey(nodeId))
                return layers[nodeId];

            if (visiting.Contains(nodeId))
                return 0;

            visiting.Add(nodeId);

            var inEdges = edges.FindAll(e => e.TargetId == nodeId && nodeIds.Contains(e.SourceId));
            int maxParentLayer = -1;

            foreach (var edge in inEdges)
            {
                int parentLayer = LongestPathDfs(edge.SourceId, edges, layers, nodeIds, visiting);
                if (parentLayer > maxParentLayer)
                    maxParentLayer = parentLayer;
            }

            int layer = maxParentLayer + 1;
            layers[nodeId] = layer;
            visiting.Remove(nodeId);
            return layer;
        }

        #endregion

        #region Phase 3: Dummy Node Insertion

        private List<LayoutNode> InsertDummyNodes(Dictionary<string, int> layers,
            List<LayoutEdge> edges, List<LayoutNode> nodes)
        {
            var dummyNodes = new List<LayoutNode>();
            var edgesToRemove = new List<LayoutEdge>();
            var edgesToAdd = new List<LayoutEdge>();
            int dummyCount = 0;

            foreach (var edge in edges.ToList())
            {
                if (!layers.ContainsKey(edge.SourceId) || !layers.ContainsKey(edge.TargetId))
                    continue;

                int sourceLayer = layers[edge.SourceId];
                int targetLayer = layers[edge.TargetId];
                int span = targetLayer - sourceLayer;

                if (span <= 1)
                    continue;

                // Need dummy nodes
                edgesToRemove.Add(edge);
                string prevId = edge.SourceId;

                for (int i = 1; i < span; i++)
                {
                    string dummyId = $"__dummy_{dummyCount++}";
                    var dummyNode = new LayoutNode
                    {
                        Id = dummyId,
                        Size = new Vector2(0, 0),
                        IsDummy = true
                    };
                    dummyNodes.Add(dummyNode);
                    nodes.Add(dummyNode);
                    layers[dummyId] = sourceLayer + i;

                    edgesToAdd.Add(new LayoutEdge
                    {
                        SourceId = prevId,
                        TargetId = dummyId,
                        Id = edge.Id
                    });

                    prevId = dummyId;
                }

                edgesToAdd.Add(new LayoutEdge
                {
                    SourceId = prevId,
                    TargetId = edge.TargetId,
                    Id = edge.Id
                });
            }

            foreach (var e in edgesToRemove)
                edges.Remove(e);
            edges.AddRange(edgesToAdd);

            return dummyNodes;
        }

        #endregion

        #region Phase 4: Crossing Minimization

        private void MinimizeCrossings(Dictionary<string, int> layers, List<LayoutEdge> edges)
        {
            // Build layer-to-nodes mapping
            var layerNodes = new Dictionary<int, List<string>>();
            foreach (var kvp in layers)
            {
                if (!layerNodes.ContainsKey(kvp.Value))
                    layerNodes[kvp.Value] = new List<string>();
                layerNodes[kvp.Value].Add(kvp.Key);
            }

            if (layerNodes.Count <= 1)
                return;

            int minLayer = layerNodes.Keys.Min();
            int maxLayer = layerNodes.Keys.Max();

            // Build position index for barycenter calculation
            var positions = new Dictionary<string, int>();
            foreach (var kvp in layerNodes)
            {
                for (int i = 0; i < kvp.Value.Count; i++)
                    positions[kvp.Value[i]] = i;
            }

            for (int iter = 0; iter < MaxCrossingIterations; iter++)
            {
                // Sweep down
                for (int layer = minLayer + 1; layer <= maxLayer; layer++)
                {
                    if (!layerNodes.ContainsKey(layer))
                        continue;

                    ReorderLayer(layerNodes[layer], layer, -1, edges, positions, layers);
                    UpdatePositions(layerNodes[layer], positions);
                }

                // Sweep up
                for (int layer = maxLayer - 1; layer >= minLayer; layer--)
                {
                    if (!layerNodes.ContainsKey(layer))
                        continue;

                    ReorderLayer(layerNodes[layer], layer, 1, edges, positions, layers);
                    UpdatePositions(layerNodes[layer], positions);
                }
            }

            // Write back sorted layer assignments
            foreach (var kvp in layerNodes)
            {
                // positions already updated
            }
        }

        private void ReorderLayer(List<string> nodesInLayer, int currentLayer, int direction,
            List<LayoutEdge> edges, Dictionary<string, int> positions, Dictionary<string, int> layers)
        {
            var barycenters = new Dictionary<string, float>();

            foreach (var nodeId in nodesInLayer)
            {
                var connectedPositions = new List<int>();

                if (direction < 0)
                {
                    // Looking at layer above (smaller layer number)
                    var inEdges = edges.FindAll(e => e.TargetId == nodeId
                        && layers.ContainsKey(e.SourceId)
                        && layers[e.SourceId] == currentLayer - 1);
                    foreach (var e in inEdges)
                    {
                        if (positions.ContainsKey(e.SourceId))
                            connectedPositions.Add(positions[e.SourceId]);
                    }
                }
                else
                {
                    // Looking at layer below (larger layer number)
                    var outEdges = edges.FindAll(e => e.SourceId == nodeId
                        && layers.ContainsKey(e.TargetId)
                        && layers[e.TargetId] == currentLayer + 1);
                    foreach (var e in outEdges)
                    {
                        if (positions.ContainsKey(e.TargetId))
                            connectedPositions.Add(positions[e.TargetId]);
                    }
                }

                if (connectedPositions.Count > 0)
                    barycenters[nodeId] = (float)connectedPositions.Average();
                else
                    barycenters[nodeId] = positions.ContainsKey(nodeId) ? positions[nodeId] : 0;
            }

            nodesInLayer.Sort((a, b) => barycenters[a].CompareTo(barycenters[b]));
        }

        private void UpdatePositions(List<string> nodesInLayer, Dictionary<string, int> positions)
        {
            for (int i = 0; i < nodesInLayer.Count; i++)
            {
                positions[nodesInLayer[i]] = i;
            }
        }

        #endregion

        #region Phase 5: Coordinate Assignment

        private LayoutResult AssignCoordinates(Dictionary<string, int> layers, List<LayoutEdge> edges,
            LayoutGraph graph, LayoutConfig config, List<LayoutNode> dummyNodes)
        {
            var result = new LayoutResult();
            bool isHorizontal = config.Direction == FlowDirection.LR || config.Direction == FlowDirection.RL;
            bool isReversed = config.Direction == FlowDirection.BT || config.Direction == FlowDirection.RL;

            // Build layer-to-nodes mapping
            var layerNodes = new Dictionary<int, List<string>>();
            foreach (var kvp in layers)
            {
                if (!layerNodes.ContainsKey(kvp.Value))
                    layerNodes[kvp.Value] = new List<string>();
                layerNodes[kvp.Value].Add(kvp.Key);
            }

            int minLayer = layerNodes.Count > 0 ? layerNodes.Keys.Min() : 0;
            int maxLayer = layerNodes.Count > 0 ? layerNodes.Keys.Max() : 0;

            // Build node lookup
            var nodeLookup = new Dictionary<string, LayoutNode>();
            foreach (var node in graph.Nodes)
                nodeLookup[node.Id] = node;
            foreach (var dummy in dummyNodes)
                nodeLookup[dummy.Id] = dummy;

            float spacingH = config.NodeSpacingH;
            float spacingV = config.NodeSpacingV;

            // Assign positions layer by layer
            for (int layer = minLayer; layer <= maxLayer; layer++)
            {
                if (!layerNodes.ContainsKey(layer))
                    continue;

                var nodesInLayer = layerNodes[layer];
                float offset = 0;

                for (int i = 0; i < nodesInLayer.Count; i++)
                {
                    string nodeId = nodesInLayer[i];
                    var node = nodeLookup.ContainsKey(nodeId) ? nodeLookup[nodeId] : null;
                    Vector2 nodeSize = node != null ? node.Size : new Vector2(150, 40);

                    float primary = layer * (spacingV + 40f); // layer direction
                    float secondary = offset;

                    if (isReversed)
                        primary = (maxLayer - layer) * (spacingV + 40f);

                    Vector2 pos;
                    if (isHorizontal)
                        pos = new Vector2(primary, secondary);
                    else
                        pos = new Vector2(secondary, primary);

                    // Only store positions for non-dummy nodes in result
                    if (node == null || !node.IsDummy)
                    {
                        result.NodePositions[nodeId] = pos;
                    }

                    // Store position for dummy nodes temporarily for waypoint calculation
                    if (node != null && node.IsDummy)
                    {
                        result.NodePositions[nodeId] = pos;
                    }

                    offset += (isHorizontal ? nodeSize.y : nodeSize.x) + spacingH;
                }
            }

            // Calculate edge waypoints
            CalculateEdgeWaypoints(edges, result, graph, dummyNodes);

            // Calculate group bounding boxes
            CalculateGroupRects(graph, result, config);

            // Remove dummy node positions from final result
            foreach (var dummy in dummyNodes)
            {
                // Keep them only in waypoints; remove from positions
                result.NodePositions.Remove(dummy.Id);
            }

            // Calculate total height
            if (result.NodePositions.Count > 0)
            {
                float maxY = result.NodePositions.Values.Max(p => p.y);
                float maxNodeHeight = graph.Nodes.Count > 0 ? graph.Nodes.Max(n => n.Size.y) : 40f;
                result.TotalHeight = maxY + maxNodeHeight;
            }

            return result;
        }

        private void CalculateEdgeWaypoints(List<LayoutEdge> edges, LayoutResult result,
            LayoutGraph graph, List<LayoutNode> dummyNodes)
        {
            var dummyIds = new HashSet<string>(dummyNodes.Select(d => d.Id));

            // Group edges by original edge Id to reconstruct paths through dummy nodes
            var edgeGroups = new Dictionary<string, List<LayoutEdge>>();
            foreach (var edge in edges)
            {
                string key = edge.Id ?? $"{edge.SourceId}->{edge.TargetId}";
                if (!edgeGroups.ContainsKey(key))
                    edgeGroups[key] = new List<LayoutEdge>();
                edgeGroups[key].Add(edge);
            }

            foreach (var kvp in edgeGroups)
            {
                var waypoints = new List<Vector2>();
                var chain = BuildEdgeChain(kvp.Value, dummyIds);

                foreach (var nodeId in chain)
                {
                    if (result.NodePositions.ContainsKey(nodeId))
                    {
                        var pos = result.NodePositions[nodeId];
                        var node = graph.GetNode(nodeId);
                        if (node != null)
                        {
                            // Use center of node
                            pos += node.Size * 0.5f;
                        }
                        waypoints.Add(pos);
                    }
                }

                if (waypoints.Count >= 2)
                {
                    result.EdgeWaypoints[kvp.Key] = waypoints;
                }
            }
        }

        private List<string> BuildEdgeChain(List<LayoutEdge> segmentEdges, HashSet<string> dummyIds)
        {
            if (segmentEdges.Count == 1)
            {
                return new List<string> { segmentEdges[0].SourceId, segmentEdges[0].TargetId };
            }

            // Build adjacency for the segments
            var chain = new List<string>();
            var adjacency = new Dictionary<string, string>();
            var allTargets = new HashSet<string>();

            foreach (var e in segmentEdges)
            {
                adjacency[e.SourceId] = e.TargetId;
                allTargets.Add(e.TargetId);
            }

            // Find the start (source not in targets)
            string start = segmentEdges[0].SourceId;
            foreach (var e in segmentEdges)
            {
                if (!allTargets.Contains(e.SourceId))
                {
                    start = e.SourceId;
                    break;
                }
            }

            string current = start;
            chain.Add(current);
            while (adjacency.ContainsKey(current))
            {
                current = adjacency[current];
                chain.Add(current);
            }

            return chain;
        }

        private void CalculateGroupRects(LayoutGraph graph, LayoutResult result, LayoutConfig config)
        {
            foreach (var group in graph.Groups)
            {
                if (group.NodeIds.Count == 0)
                    continue;

                float minX = float.MaxValue, minY = float.MaxValue;
                float maxX = float.MinValue, maxY = float.MinValue;

                foreach (var nodeId in group.NodeIds)
                {
                    if (!result.NodePositions.ContainsKey(nodeId))
                        continue;

                    var pos = result.NodePositions[nodeId];
                    var node = graph.GetNode(nodeId);
                    Vector2 size = node != null ? node.Size : new Vector2(150, 40);

                    if (pos.x < minX) minX = pos.x;
                    if (pos.y < minY) minY = pos.y;
                    if (pos.x + size.x > maxX) maxX = pos.x + size.x;
                    if (pos.y + size.y > maxY) maxY = pos.y + size.y;
                }

                if (minX == float.MaxValue)
                    continue;

                float padding = config.GroupPadding;
                result.GroupRects[group.Id] = new Rect(
                    minX - padding,
                    minY - padding,
                    (maxX - minX) + padding * 2,
                    (maxY - minY) + padding * 2
                );
            }
        }

        #endregion
    }
}
