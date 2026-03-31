using UnityEngine;

namespace Ars.MermaidGraphView
{
    public class NodeVisualStyle
    {
        public string ShapeClass { get; set; } = "rectangle";
        public Color Color { get; set; } = new Color(0.2f, 0.3f, 0.5f);
    }

    public static class NodeStyleResolver
    {
        public static NodeVisualStyle Resolve(NodeShape shape)
        {
            var style = new NodeVisualStyle();

            switch (shape)
            {
                case NodeShape.Rectangle:
                    style.ShapeClass = "rectangle";
                    style.Color = new Color(0.2f, 0.3f, 0.5f);
                    break;
                case NodeShape.Rounded:
                    style.ShapeClass = "rounded";
                    style.Color = new Color(0.2f, 0.35f, 0.5f);
                    break;
                case NodeShape.Stadium:
                    style.ShapeClass = "stadium";
                    style.Color = new Color(0.2f, 0.4f, 0.5f);
                    break;
                case NodeShape.Cylinder:
                    style.ShapeClass = "cylinder";
                    style.Color = new Color(0.3f, 0.3f, 0.45f);
                    break;
                case NodeShape.Circle:
                    style.ShapeClass = "circle";
                    style.Color = new Color(0.25f, 0.35f, 0.5f);
                    break;
                case NodeShape.Diamond:
                    style.ShapeClass = "diamond";
                    style.Color = new Color(0.4f, 0.3f, 0.2f);
                    break;
                case NodeShape.Hexagon:
                    style.ShapeClass = "hexagon";
                    style.Color = new Color(0.3f, 0.4f, 0.3f);
                    break;
                case NodeShape.Parallelogram:
                    style.ShapeClass = "parallelogram";
                    style.Color = new Color(0.35f, 0.3f, 0.4f);
                    break;
                case NodeShape.Subroutine:
                    style.ShapeClass = "subroutine";
                    style.Color = new Color(0.3f, 0.25f, 0.45f);
                    break;
                case NodeShape.Trapezoid:
                    style.ShapeClass = "trapezoid";
                    style.Color = new Color(0.35f, 0.35f, 0.3f);
                    break;
                default:
                    style.ShapeClass = "rectangle";
                    style.Color = new Color(0.2f, 0.3f, 0.5f);
                    break;
            }

            return style;
        }

        public static NodeVisualStyle ResolveState(StateKind kind)
        {
            var style = new NodeVisualStyle();

            switch (kind)
            {
                case StateKind.Start:
                    style.ShapeClass = "circle";
                    style.Color = new Color(0.1f, 0.6f, 0.1f);
                    break;
                case StateKind.End:
                    style.ShapeClass = "circle";
                    style.Color = new Color(0.6f, 0.1f, 0.1f);
                    break;
                case StateKind.Fork:
                case StateKind.Join:
                    style.ShapeClass = "rectangle";
                    style.Color = new Color(0.15f, 0.15f, 0.15f);
                    break;
                case StateKind.Choice:
                    style.ShapeClass = "diamond";
                    style.Color = new Color(0.4f, 0.3f, 0.2f);
                    break;
                default:
                    style.ShapeClass = "rounded";
                    style.Color = new Color(0.2f, 0.3f, 0.5f);
                    break;
            }

            return style;
        }

        public static NodeVisualStyle ResolveClass(string stereotype)
        {
            var style = new NodeVisualStyle
            {
                ShapeClass = "rectangle"
            };

            if (string.IsNullOrEmpty(stereotype))
            {
                style.Color = new Color(0.2f, 0.3f, 0.5f);
                return style;
            }

            var lower = stereotype.ToLowerInvariant();

            if (lower == "interface")
            {
                style.Color = new Color(0.2f, 0.35f, 0.6f);
            }
            else if (lower == "abstract")
            {
                style.Color = new Color(0.35f, 0.25f, 0.5f);
            }
            else if (lower == "enum")
            {
                style.Color = new Color(0.3f, 0.4f, 0.3f);
            }
            else
            {
                style.Color = new Color(0.2f, 0.3f, 0.5f);
            }

            return style;
        }
    }
}
