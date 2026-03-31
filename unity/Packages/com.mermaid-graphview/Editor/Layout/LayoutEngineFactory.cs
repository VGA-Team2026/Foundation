namespace MermaidGraphView
{
    public static class LayoutEngineFactory
    {
        public static ILayoutEngine Create(DiagramType diagramType)
        {
            switch (diagramType)
            {
                case DiagramType.Flowchart:
                case DiagramType.StateDiagram:
                case DiagramType.ClassDiagram:
                    return new SugiyamaLayoutEngine();
                case DiagramType.SequenceDiagram:
                    return new SequenceLayoutEngine();
                default:
                    return new SugiyamaLayoutEngine();
            }
        }
    }
}
