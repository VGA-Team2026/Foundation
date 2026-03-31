using System.Collections.Generic;
using UnityEngine;

namespace MermaidGraphView
{
    public static class LayoutGraphConverter
    {
        public static LayoutGraph Convert(MermaidDocument document)
        {
            switch (document.DiagramType)
            {
                case DiagramType.Flowchart:
                    return ConvertFlowchart((FlowchartDocument)document.Content);
                case DiagramType.StateDiagram:
                    return ConvertStateDiagram((StateDiagramDocument)document.Content);
                case DiagramType.ClassDiagram:
                    return ConvertClassDiagram((ClassDiagramDocument)document.Content);
                case DiagramType.SequenceDiagram:
                    return ConvertSequenceDiagram((SequenceDiagramDocument)document.Content);
                default:
                    return new LayoutGraph();
            }
        }

        #region Flowchart

        public static LayoutGraph ConvertFlowchart(FlowchartDocument doc)
        {
            var graph = new LayoutGraph();

            foreach (var node in doc.Nodes)
            {
                graph.Nodes.Add(new LayoutNode
                {
                    Id = node.Id,
                    Size = EstimateFlowNodeSize(node)
                });
            }

            int edgeIndex = 0;
            foreach (var edge in doc.Edges)
            {
                graph.Edges.Add(new LayoutEdge
                {
                    SourceId = edge.SourceId,
                    TargetId = edge.TargetId,
                    Id = $"edge_{edgeIndex++}"
                });
            }

            foreach (var subgraph in doc.Subgraphs)
            {
                ConvertSubgraph(subgraph, null, graph);
            }

            // Assign group ids to nodes
            foreach (var group in graph.Groups)
            {
                foreach (var nodeId in group.NodeIds)
                {
                    var node = graph.GetNode(nodeId);
                    if (node != null)
                    {
                        node.GroupId = group.Id;
                    }
                }
            }

            return graph;
        }

        private static void ConvertSubgraph(FlowSubgraph subgraph, string parentGroupId, LayoutGraph graph)
        {
            var group = new LayoutGroup
            {
                Id = subgraph.Id,
                Label = subgraph.Label ?? subgraph.Id,
                ParentGroupId = parentGroupId
            };

            foreach (var nodeId in subgraph.NodeIds)
            {
                group.NodeIds.Add(nodeId);
            }

            graph.Groups.Add(group);

            if (subgraph.Children != null)
            {
                foreach (var child in subgraph.Children)
                {
                    ConvertSubgraph(child, subgraph.Id, graph);
                }
            }
        }

        private static Vector2 EstimateFlowNodeSize(FlowNode node)
        {
            string label = node.Label ?? node.Id;
            float width = Mathf.Max(80f, label.Length * 9f + 30f);
            float height = 40f;

            switch (node.Shape)
            {
                case NodeShape.Circle:
                    float diameter = Mathf.Max(width, height);
                    return new Vector2(diameter, diameter);
                case NodeShape.Diamond:
                    return new Vector2(width * 1.4f, height * 1.4f);
                case NodeShape.Hexagon:
                    return new Vector2(width + 30f, height);
                case NodeShape.Cylinder:
                    return new Vector2(width, height + 20f);
                default:
                    return new Vector2(width, height);
            }
        }

        #endregion

        #region State Diagram

        public static LayoutGraph ConvertStateDiagram(StateDiagramDocument doc)
        {
            var graph = new LayoutGraph();

            foreach (var state in doc.States)
            {
                AddStateNode(state, null, graph);
            }

            int edgeIndex = 0;
            foreach (var transition in doc.Transitions)
            {
                graph.Edges.Add(new LayoutEdge
                {
                    SourceId = transition.FromId,
                    TargetId = transition.ToId,
                    Id = $"edge_{edgeIndex++}"
                });
            }

            return graph;
        }

        private static void AddStateNode(StateNode state, string parentGroupId, LayoutGraph graph)
        {
            bool isComposite = state.Children != null && state.Children.Count > 0;

            graph.Nodes.Add(new LayoutNode
            {
                Id = state.Id,
                Size = EstimateStateNodeSize(state),
                GroupId = parentGroupId
            });

            if (isComposite)
            {
                var group = new LayoutGroup
                {
                    Id = state.Id,
                    Label = state.Label ?? state.Id,
                    ParentGroupId = parentGroupId
                };

                group.NodeIds.Add(state.Id);

                foreach (var child in state.Children)
                {
                    group.NodeIds.Add(child.Id);
                    AddStateNode(child, state.Id, graph);
                }

                // Add internal transitions as edges
                if (state.InternalTransitions != null)
                {
                    int internalIdx = 0;
                    foreach (var t in state.InternalTransitions)
                    {
                        graph.Edges.Add(new LayoutEdge
                        {
                            SourceId = t.FromId,
                            TargetId = t.ToId,
                            Id = $"internal_{state.Id}_{internalIdx++}"
                        });
                    }
                }

                graph.Groups.Add(group);
            }
        }

        private static Vector2 EstimateStateNodeSize(StateNode state)
        {
            switch (state.Kind)
            {
                case StateKind.Start:
                case StateKind.End:
                    return new Vector2(30f, 30f);
                case StateKind.Fork:
                case StateKind.Join:
                    return new Vector2(120f, 8f);
                case StateKind.Choice:
                    return new Vector2(60f, 60f);
                default:
                    string label = state.Label ?? state.Id;
                    float width = Mathf.Max(100f, label.Length * 9f + 30f);
                    return new Vector2(width, 40f);
            }
        }

        #endregion

        #region Class Diagram

        public static LayoutGraph ConvertClassDiagram(ClassDiagramDocument doc)
        {
            var graph = new LayoutGraph();

            foreach (var cls in doc.Classes)
            {
                graph.Nodes.Add(new LayoutNode
                {
                    Id = cls.Name,
                    Size = EstimateClassNodeSize(cls)
                });
            }

            int edgeIndex = 0;
            foreach (var relation in doc.Relations)
            {
                graph.Edges.Add(new LayoutEdge
                {
                    SourceId = relation.FromClass,
                    TargetId = relation.ToClass,
                    Id = $"edge_{edgeIndex++}"
                });
            }

            return graph;
        }

        private static Vector2 EstimateClassNodeSize(ClassNode cls)
        {
            int memberCount = cls.Fields.Count + cls.Methods.Count;
            float lineHeight = 18f;
            float headerHeight = 30f;
            float stereotypeHeight = string.IsNullOrEmpty(cls.Stereotype) ? 0f : 18f;

            float height = headerHeight + stereotypeHeight + memberCount * lineHeight + 10f;
            height = Mathf.Max(height, 50f);

            float maxLabelWidth = cls.Name.Length * 9f;
            foreach (var field in cls.Fields)
            {
                float w = (field.Name.Length + (field.Type != null ? field.Type.Length + 2 : 0)) * 7f;
                if (w > maxLabelWidth) maxLabelWidth = w;
            }
            foreach (var method in cls.Methods)
            {
                float w = (method.Name.Length + (method.Type != null ? method.Type.Length + 2 : 0)) * 7f;
                if (w > maxLabelWidth) maxLabelWidth = w;
            }

            float width = Mathf.Max(150f, maxLabelWidth + 40f);
            return new Vector2(width, height);
        }

        #endregion

        #region Sequence Diagram

        public static SequenceLayoutGraph ConvertSequenceDiagram(SequenceDiagramDocument doc)
        {
            var graph = new SequenceLayoutGraph();

            foreach (var participant in doc.Participants)
            {
                graph.Participants.Add(new SequenceParticipantInfo
                {
                    Id = participant.Id,
                    Label = participant.Alias ?? participant.Id
                });

                graph.Nodes.Add(new LayoutNode
                {
                    Id = participant.Id,
                    Size = new Vector2(120f, 40f)
                });
            }

            // Build participant X positions for note placement
            var participantX = new Dictionary<string, float>();
            for (int i = 0; i < doc.Participants.Count; i++)
            {
                participantX[doc.Participants[i].Id] = i * 200f;
            }

            ConvertSequenceElements(doc.Elements, graph.Elements, participantX);

            return graph;
        }

        private static void ConvertSequenceElements(List<SequenceElement> elements,
            List<SequenceElementLayout> output, Dictionary<string, float> participantX)
        {
            foreach (var element in elements)
            {
                if (element is SequenceMessage msg)
                {
                    output.Add(new SequenceMessageLayout
                    {
                        FromId = msg.FromId,
                        ToId = msg.ToId,
                        Text = msg.Text,
                        IsDotted = msg.Style == MessageStyle.Dotted,
                        IsOpen = msg.ArrowHead == ArrowHead.Open
                    });
                }
                else if (element is SequenceBlock block)
                {
                    var blockLayout = new SequenceBlockLayout
                    {
                        Kind = block.Kind.ToString(),
                        Label = block.Label
                    };

                    foreach (var section in block.Sections)
                    {
                        var sectionLayout = new SequenceSectionLayout
                        {
                            Label = section.Label
                        };
                        blockLayout.Sections.Add(sectionLayout);

                        ConvertSequenceElements(section.Elements, blockLayout.InnerElements, participantX);
                    }

                    output.Add(blockLayout);
                }
                else if (element is SequenceNote note)
                {
                    float x = 0f;
                    if (note.OverParticipants != null && note.OverParticipants.Count > 0)
                    {
                        string firstParticipant = note.OverParticipants[0];
                        if (participantX.ContainsKey(firstParticipant))
                        {
                            float participantPos = participantX[firstParticipant];

                            switch (note.Position)
                            {
                                case NotePosition.LeftOf:
                                    x = participantPos - 140f;
                                    break;
                                case NotePosition.RightOf:
                                    x = participantPos + 140f;
                                    break;
                                case NotePosition.Over:
                                    if (note.OverParticipants.Count > 1)
                                    {
                                        string lastParticipant = note.OverParticipants[note.OverParticipants.Count - 1];
                                        if (participantX.ContainsKey(lastParticipant))
                                        {
                                            x = (participantPos + participantX[lastParticipant]) * 0.5f;
                                        }
                                        else
                                        {
                                            x = participantPos;
                                        }
                                    }
                                    else
                                    {
                                        x = participantPos;
                                    }
                                    break;
                            }
                        }
                    }

                    output.Add(new SequenceNoteLayout
                    {
                        Text = note.Text,
                        X = x,
                        Width = 120f
                    });
                }
            }
        }

        #endregion
    }
}
