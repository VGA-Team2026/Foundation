namespace Ars.MermaidGraphView
{
    public interface ILayoutEngine
    {
        LayoutResult Calculate(LayoutGraph graph, LayoutConfig config);
    }
}
