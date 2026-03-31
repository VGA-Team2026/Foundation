namespace MermaidGraphView
{
    public interface IMermaidParser
    {
        MermaidDocument Parse(string source);
    }

    public class MermaidParseException : System.Exception
    {
        public MermaidParseException(string message) : base(message) { }
    }

    public static class ParserFactory
    {
        public static IMermaidParser Create(string source)
        {
            var firstLine = GetFirstMeaningfulLine(source);
            return firstLine switch
            {
                var l when l.StartsWith("flowchart") || l.StartsWith("graph")
                    => new FlowchartParser(),
                var l when l.StartsWith("stateDiagram")
                    => new StateDiagramParser(),
                var l when l.StartsWith("classDiagram")
                    => new ClassDiagramParser(),
                var l when l.StartsWith("sequenceDiagram")
                    => new SequenceDiagramParser(),
                _ => throw new MermaidParseException($"Unknown diagram type: {firstLine}")
            };
        }

        private static string GetFirstMeaningfulLine(string source)
        {
            foreach (var line in source.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith("%%"))
                    return trimmed;
            }
            throw new MermaidParseException("Empty or comment-only source");
        }
    }
}
