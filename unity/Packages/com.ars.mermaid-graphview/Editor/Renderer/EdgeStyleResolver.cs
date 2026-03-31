namespace Ars.MermaidGraphView
{
    public class EdgeVisualStyle
    {
        public string LineStyle { get; set; } = "solid";
        public string Label { get; set; }
        public bool IsReversed { get; set; }
    }

    public static class EdgeStyleResolver
    {
        public static EdgeVisualStyle Resolve(EdgeStyle style, ArrowType arrow, string label)
        {
            var visual = new EdgeVisualStyle
            {
                Label = label
            };

            switch (style)
            {
                case EdgeStyle.Solid:
                    visual.LineStyle = "solid";
                    break;
                case EdgeStyle.Dotted:
                    visual.LineStyle = "dotted";
                    break;
                case EdgeStyle.Thick:
                    visual.LineStyle = "thick";
                    break;
                default:
                    visual.LineStyle = "solid";
                    break;
            }

            return visual;
        }

        public static EdgeVisualStyle ResolveTransition(string label)
        {
            return new EdgeVisualStyle
            {
                LineStyle = "solid",
                Label = label
            };
        }

        public static EdgeVisualStyle ResolveRelation(RelationType type, string label)
        {
            var visual = new EdgeVisualStyle
            {
                Label = label
            };

            switch (type)
            {
                case RelationType.Inheritance:
                case RelationType.Realization:
                    visual.LineStyle = "solid";
                    break;
                case RelationType.Dependency:
                    visual.LineStyle = "dotted";
                    break;
                case RelationType.Composition:
                case RelationType.Aggregation:
                    visual.LineStyle = "solid";
                    break;
                case RelationType.Association:
                case RelationType.Link:
                default:
                    visual.LineStyle = "solid";
                    break;
            }

            return visual;
        }
    }
}
