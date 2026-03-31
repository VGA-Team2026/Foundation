using System.Collections.Generic;
using UnityEngine;

namespace Ars.MermaidGraphView
{
    public class GraphBasedRenderer : IMermaidRenderer
    {
        public void Render(MermaidGraphViewPanel view, MermaidDocument document, LayoutResult layout)
        {
            switch (document.DiagramType)
            {
                case DiagramType.Flowchart:
                    RenderFlowchart(view, document.Content as FlowchartDocument, layout);
                    break;
                case DiagramType.StateDiagram:
                    RenderStateDiagram(view, document.Content as StateDiagramDocument, layout);
                    break;
                case DiagramType.ClassDiagram:
                    RenderClassDiagram(view, document.Content as ClassDiagramDocument, layout);
                    break;
            }
        }

        private void RenderFlowchart(MermaidGraphViewPanel view, FlowchartDocument doc, LayoutResult layout)
        {
            if (doc == null) return;

            var nodeMap = new Dictionary<string, MermaidNode>();

            foreach (var flowNode in doc.Nodes)
            {
                if (!layout.NodePositions.ContainsKey(flowNode.Id)) continue;

                var style = NodeStyleResolver.Resolve(flowNode.Shape);
                var node = new MermaidNode(flowNode.Id, flowNode.Label, style);
                var pos = layout.NodePositions[flowNode.Id];
                node.SetPosition(new Rect(pos.x, pos.y, 0, 0));
                view.AddElement(node);
                nodeMap[flowNode.Id] = node;
            }

            foreach (var edge in doc.Edges)
            {
                if (!nodeMap.ContainsKey(edge.SourceId) || !nodeMap.ContainsKey(edge.TargetId))
                    continue;

                var edgeStyle = EdgeStyleResolver.Resolve(edge.Style, edge.Arrow, edge.Label);
                var mermaidEdge = new MermaidEdge(edgeStyle)
                {
                    output = nodeMap[edge.SourceId].OutputPort,
                    input = nodeMap[edge.TargetId].InputPort
                };
                mermaidEdge.output.Connect(mermaidEdge);
                mermaidEdge.input.Connect(mermaidEdge);
                view.AddElement(mermaidEdge);
            }

            foreach (var subgraph in doc.Subgraphs)
            {
                RenderSubgraph(view, subgraph, nodeMap, layout);
            }
        }

        private void RenderSubgraph(MermaidGraphViewPanel view, FlowSubgraph subgraph, Dictionary<string, MermaidNode> nodeMap, LayoutResult layout)
        {
            var group = new MermaidGroup(subgraph.Id, subgraph.Label);
            view.AddElement(group);

            if (layout.GroupRects.TryGetValue(subgraph.Id, out var rect))
            {
                group.SetPosition(rect);
            }

            foreach (var nodeId in subgraph.NodeIds)
            {
                if (nodeMap.TryGetValue(nodeId, out var node))
                {
                    group.AddElement(node);
                }
            }

            foreach (var child in subgraph.Children)
            {
                RenderSubgraph(view, child, nodeMap, layout);
            }
        }

        private void RenderStateDiagram(MermaidGraphViewPanel view, StateDiagramDocument doc, LayoutResult layout)
        {
            if (doc == null) return;

            var nodeMap = new Dictionary<string, MermaidNode>();

            foreach (var state in doc.States)
            {
                RenderState(view, state, nodeMap, layout);
            }

            foreach (var transition in doc.Transitions)
            {
                if (!nodeMap.ContainsKey(transition.FromId) || !nodeMap.ContainsKey(transition.ToId))
                    continue;

                var edgeStyle = EdgeStyleResolver.ResolveTransition(transition.Label);
                var mermaidEdge = new MermaidEdge(edgeStyle)
                {
                    output = nodeMap[transition.FromId].OutputPort,
                    input = nodeMap[transition.ToId].InputPort
                };
                mermaidEdge.output.Connect(mermaidEdge);
                mermaidEdge.input.Connect(mermaidEdge);
                view.AddElement(mermaidEdge);
            }
        }

        private void RenderState(MermaidGraphViewPanel view, StateNode state, Dictionary<string, MermaidNode> nodeMap, LayoutResult layout)
        {
            if (!layout.NodePositions.ContainsKey(state.Id)) return;

            var style = NodeStyleResolver.ResolveState(state.Kind);
            var node = new MermaidNode(state.Id, state.Label, style);
            var pos = layout.NodePositions[state.Id];
            node.SetPosition(new Rect(pos.x, pos.y, 0, 0));
            view.AddElement(node);
            nodeMap[state.Id] = node;

            if (state.Children != null && state.Children.Count > 0)
            {
                var group = new MermaidGroup(state.Id + "_group", state.Label);
                view.AddElement(group);

                if (layout.GroupRects.TryGetValue(state.Id, out var rect))
                {
                    group.SetPosition(rect);
                }

                group.AddElement(node);

                foreach (var child in state.Children)
                {
                    RenderState(view, child, nodeMap, layout);
                    if (nodeMap.TryGetValue(child.Id, out var childNode))
                    {
                        group.AddElement(childNode);
                    }
                }
            }
        }

        private void RenderClassDiagram(MermaidGraphViewPanel view, ClassDiagramDocument doc, LayoutResult layout)
        {
            if (doc == null) return;

            var nodeMap = new Dictionary<string, MermaidNode>();

            foreach (var cls in doc.Classes)
            {
                if (!layout.NodePositions.ContainsKey(cls.Name)) continue;

                var style = NodeStyleResolver.ResolveClass(cls.Stereotype);
                var node = new MermaidClassNode(cls.Name, cls.Stereotype, cls.Fields, cls.Methods, style);
                var pos = layout.NodePositions[cls.Name];
                node.SetPosition(new Rect(pos.x, pos.y, 0, 0));
                view.AddElement(node);
                nodeMap[cls.Name] = node;
            }

            foreach (var relation in doc.Relations)
            {
                if (!nodeMap.ContainsKey(relation.FromClass) || !nodeMap.ContainsKey(relation.ToClass))
                    continue;

                var edgeStyle = EdgeStyleResolver.ResolveRelation(relation.Type, relation.Label);
                var mermaidEdge = new MermaidEdge(edgeStyle)
                {
                    output = nodeMap[relation.FromClass].OutputPort,
                    input = nodeMap[relation.ToClass].InputPort
                };
                mermaidEdge.output.Connect(mermaidEdge);
                mermaidEdge.input.Connect(mermaidEdge);
                view.AddElement(mermaidEdge);
            }
        }
    }
}
