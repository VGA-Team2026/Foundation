using UnityEditor.Experimental.GraphView;

namespace Ars.MermaidGraphView
{
    public class MermaidGroup : Group
    {
        public string MermaidId { get; private set; }

        public MermaidGroup(string id, string label)
        {
            MermaidId = id;
            title = label;
            capabilities &= ~Capabilities.Deletable;
        }
    }
}
