using UnityEngine;

namespace MermaidGraphView
{
    public class MermaidAsset : ScriptableObject
    {
        [TextArea(10, 50)]
        public string mermaidSource;
    }
}
