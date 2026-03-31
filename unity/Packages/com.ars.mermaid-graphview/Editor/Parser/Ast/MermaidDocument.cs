namespace Ars.MermaidGraphView
{
    public enum DiagramType { Flowchart, StateDiagram, ClassDiagram, SequenceDiagram }

    public class MermaidDocument
    {
        public DiagramType DiagramType { get; set; }
        public object Content { get; set; } // The typed AST root (FlowchartDocument, etc.)
    }
}
