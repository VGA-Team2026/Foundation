namespace MermaidGraphView
{
    public interface IMermaidRenderer
    {
        void Render(MermaidGraphViewPanel view, MermaidDocument document, LayoutResult layout);
    }

    public static class RendererFactory
    {
        public static IMermaidRenderer Create(DiagramType type)
        {
            switch (type)
            {
                case DiagramType.SequenceDiagram:
                    return new SequenceDiagramRenderer();
                default:
                    return new GraphBasedRenderer();
            }
        }
    }
}
