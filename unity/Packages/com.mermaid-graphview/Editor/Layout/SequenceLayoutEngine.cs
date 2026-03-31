using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MermaidGraphView
{
    public class SequenceLayoutEngine : ILayoutEngine
    {
        private const float ParticipantSpacing = 200f;
        private const float RowHeight = 50f;
        private const float ParticipantWidth = 120f;
        private const float ParticipantHeight = 40f;
        private const float NoteWidth = 120f;
        private const float BlockPadding = 10f;
        private const float TopMargin = 60f;

        public LayoutResult Calculate(LayoutGraph graph, LayoutConfig config)
        {
            var seqGraph = graph as SequenceLayoutGraph;
            if (seqGraph == null)
                return new LayoutResult();

            var result = new LayoutResult();

            // Place participants horizontally
            var participantX = new Dictionary<string, float>();
            for (int i = 0; i < seqGraph.Participants.Count; i++)
            {
                var p = seqGraph.Participants[i];
                float x = i * ParticipantSpacing;
                participantX[p.Id] = x;

                result.NodePositions[p.Id] = new Vector2(x, 0);
            }

            // Process elements top to bottom
            float currentY = TopMargin;
            ProcessElements(seqGraph.Elements, participantX, result, ref currentY);

            result.TotalHeight = currentY + ParticipantHeight;

            return result;
        }

        private void ProcessElements(List<SequenceElementLayout> elements,
            Dictionary<string, float> participantX, LayoutResult result, ref float currentY)
        {
            foreach (var element in elements)
            {
                if (element is SequenceMessageLayout message)
                {
                    ProcessMessage(message, participantX, result, ref currentY);
                }
                else if (element is SequenceBlockLayout block)
                {
                    ProcessBlock(block, participantX, result, ref currentY);
                }
                else if (element is SequenceNoteLayout note)
                {
                    ProcessNote(note, participantX, result, ref currentY);
                }
            }
        }

        private void ProcessMessage(SequenceMessageLayout message,
            Dictionary<string, float> participantX, LayoutResult result, ref float currentY)
        {
            message.Y = currentY;

            // Self-message gets extra height
            if (message.FromId == message.ToId)
            {
                currentY += RowHeight * 1.5f;
            }
            else
            {
                currentY += RowHeight;
            }

            result.Messages.Add(message);
        }

        private void ProcessBlock(SequenceBlockLayout block,
            Dictionary<string, float> participantX, LayoutResult result, ref float currentY)
        {
            block.StartY = currentY;
            currentY += RowHeight * 0.5f; // Space for block header

            // Calculate horizontal bounds from all participants
            if (participantX.Count > 0)
            {
                block.MinX = participantX.Values.Min() - BlockPadding;
                block.MaxX = participantX.Values.Max() + ParticipantWidth + BlockPadding;
            }

            // Process sections
            if (block.Sections.Count > 0)
            {
                for (int i = 0; i < block.Sections.Count; i++)
                {
                    if (i > 0)
                    {
                        // Add section divider
                        block.Sections[i].Y = currentY;
                        currentY += RowHeight * 0.3f;
                    }
                }
            }

            // Process inner elements
            ProcessElements(block.InnerElements, participantX, result, ref currentY);

            currentY += RowHeight * 0.3f; // Bottom padding
            block.EndY = currentY;

            result.Blocks.Add(block);
        }

        private void ProcessNote(SequenceNoteLayout note,
            Dictionary<string, float> participantX, LayoutResult result, ref float currentY)
        {
            note.Y = currentY;
            note.Width = NoteWidth;

            // X is already set by the converter based on participant position,
            // but if it's zero, try to place it based on context
            if (note.X == 0 && participantX.Count > 0)
            {
                note.X = participantX.Values.Max() + ParticipantWidth + 20f;
            }

            currentY += RowHeight;

            result.Notes.Add(note);
        }
    }
}
