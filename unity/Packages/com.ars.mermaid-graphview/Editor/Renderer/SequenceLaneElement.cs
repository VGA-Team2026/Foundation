using UnityEngine;
using UnityEngine.UIElements;

namespace Ars.MermaidGraphView
{
    public class SequenceParticipantElement : VisualElement
    {
        public string ParticipantId { get; private set; }

        public SequenceParticipantElement(string id, string label, float x, float y, float width)
        {
            ParticipantId = id;
            AddToClassList("sequence-participant-box");

            var nameLabel = new Label(label);
            nameLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            Add(nameLabel);

            style.position = Position.Absolute;
            style.left = x;
            style.top = y;
            style.width = width;
            style.height = 40;
        }
    }

    public class SequenceLifelineElement : VisualElement
    {
        public SequenceLifelineElement(float x, float startY, float endY)
        {
            AddToClassList("sequence-lifeline");

            style.position = Position.Absolute;
            style.left = x - 1;
            style.top = startY;
            style.width = 2;
            style.height = endY - startY;
        }
    }

    public class SequenceArrowElement : VisualElement
    {
        public SequenceArrowElement(float fromX, float toX, float y, string message, bool isDotted, bool isOpen)
        {
            AddToClassList("sequence-arrow");

            style.position = Position.Absolute;
            float minX = Mathf.Min(fromX, toX);
            float maxX = Mathf.Max(fromX, toX);
            float width = maxX - minX;

            style.left = minX;
            style.top = y - 10;
            style.width = width;
            style.height = 30;

            if (!string.IsNullOrEmpty(message))
            {
                var msgLabel = new Label(message);
                msgLabel.AddToClassList("sequence-arrow-label");
                msgLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                msgLabel.style.position = Position.Absolute;
                msgLabel.style.left = 0;
                msgLabel.style.right = 0;
                msgLabel.style.top = -14;
                Add(msgLabel);
            }

            if (isDotted)
            {
                AddToClassList("sequence-arrow-dotted");
            }

            generateVisualContent += ctx => DrawArrow(ctx, fromX - minX, toX - minX, width, isDotted, isOpen);
        }

        private static void DrawArrow(MeshGenerationContext ctx, float fromLocalX, float toLocalX, float width, bool isDotted, bool isOpen)
        {
            var painter = ctx.painter2D;
            float lineY = 15f;

            painter.strokeColor = new Color(0.8f, 0.8f, 0.8f);
            painter.lineWidth = isDotted ? 1.5f : 2f;

            if (isDotted)
            {
                float dashLen = 6f;
                float gapLen = 4f;
                float startX = Mathf.Min(fromLocalX, toLocalX);
                float endX = Mathf.Max(fromLocalX, toLocalX);
                float pos = startX;

                while (pos < endX)
                {
                    float dashEnd = Mathf.Min(pos + dashLen, endX);
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(pos, lineY));
                    painter.LineTo(new Vector2(dashEnd, lineY));
                    painter.Stroke();
                    pos = dashEnd + gapLen;
                }
            }
            else
            {
                painter.BeginPath();
                painter.MoveTo(new Vector2(fromLocalX, lineY));
                painter.LineTo(new Vector2(toLocalX, lineY));
                painter.Stroke();
            }

            float arrowSize = 8f;
            float dir = toLocalX > fromLocalX ? -1f : 1f;

            painter.BeginPath();
            painter.MoveTo(new Vector2(toLocalX, lineY));
            painter.LineTo(new Vector2(toLocalX + dir * arrowSize, lineY - arrowSize * 0.5f));

            if (!isOpen)
            {
                painter.LineTo(new Vector2(toLocalX + dir * arrowSize, lineY + arrowSize * 0.5f));
                painter.ClosePath();
                painter.fillColor = new Color(0.8f, 0.8f, 0.8f);
                painter.Fill();
            }
            else
            {
                painter.Stroke();
                painter.BeginPath();
                painter.MoveTo(new Vector2(toLocalX, lineY));
                painter.LineTo(new Vector2(toLocalX + dir * arrowSize, lineY + arrowSize * 0.5f));
                painter.Stroke();
            }
        }
    }

    public class SequenceBlockElement : VisualElement
    {
        public SequenceBlockElement(string kind, string label, Rect rect)
        {
            AddToClassList("sequence-block-rect");

            style.position = Position.Absolute;
            style.left = rect.x;
            style.top = rect.y;
            style.width = rect.width;
            style.height = rect.height;

            var kindLabel = new Label(kind);
            kindLabel.AddToClassList("sequence-block-kind");
            kindLabel.style.position = Position.Absolute;
            kindLabel.style.left = 4;
            kindLabel.style.top = 2;
            Add(kindLabel);

            if (!string.IsNullOrEmpty(label))
            {
                var condLabel = new Label($"[{label}]");
                condLabel.AddToClassList("sequence-block-label");
                condLabel.style.position = Position.Absolute;
                condLabel.style.left = 50;
                condLabel.style.top = 2;
                Add(condLabel);
            }
        }
    }
}
