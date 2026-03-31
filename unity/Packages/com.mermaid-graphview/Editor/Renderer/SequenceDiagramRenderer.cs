using UnityEngine;

namespace MermaidGraphView
{
    public class SequenceDiagramRenderer : IMermaidRenderer
    {
        private const float ParticipantWidth = 120f;
        private const float ParticipantHeight = 40f;
        private const float ParticipantSpacing = 160f;
        private const float TopMargin = 20f;
        private const float LifelineStartOffset = 50f;

        public void Render(MermaidGraphViewPanel view, MermaidDocument document, LayoutResult layout)
        {
            if (layout == null) return;

            var participantPositions = layout.NodePositions;

            foreach (var kvp in participantPositions)
            {
                var participant = new SequenceParticipantElement(
                    kvp.Key,
                    kvp.Key,
                    kvp.Value.x - ParticipantWidth * 0.5f,
                    kvp.Value.y,
                    ParticipantWidth
                );
                view.contentContainer.Add(participant);

                float lifelineX = kvp.Value.x;
                float lifelineStartY = kvp.Value.y + ParticipantHeight;
                float lifelineEndY = layout.TotalHeight > 0 ? layout.TotalHeight : lifelineStartY + 400f;

                var lifeline = new SequenceLifelineElement(lifelineX, lifelineStartY, lifelineEndY);
                view.contentContainer.Add(lifeline);
            }

            foreach (var msg in layout.Messages)
            {
                float fromX = 0f;
                float toX = 0f;

                if (participantPositions.TryGetValue(msg.FromId, out var fromPos))
                    fromX = fromPos.x;
                if (participantPositions.TryGetValue(msg.ToId, out var toPos))
                    toX = toPos.x;

                if (msg.FromId == msg.ToId)
                {
                    toX = fromX + 60f;
                }

                var arrow = new SequenceArrowElement(fromX, toX, msg.Y, msg.Text, msg.IsDotted, msg.IsOpen);
                view.contentContainer.Add(arrow);
            }

            foreach (var block in layout.Blocks)
            {
                var rect = new Rect(block.MinX, block.StartY, block.MaxX - block.MinX, block.EndY - block.StartY);
                var blockElement = new SequenceBlockElement(block.Kind, block.Label, rect);
                view.contentContainer.Add(blockElement);
            }

            foreach (var note in layout.Notes)
            {
                var noteElement = new VisualElement();
                noteElement.AddToClassList("sequence-note");
                noteElement.style.position = Position.Absolute;
                noteElement.style.left = note.X;
                noteElement.style.top = note.Y;
                noteElement.style.width = note.Width > 0 ? note.Width : 100f;

                var noteLabel = new UnityEngine.UIElements.Label(note.Text);
                noteElement.Add(noteLabel);
                view.contentContainer.Add(noteElement);
            }
        }
    }
}
