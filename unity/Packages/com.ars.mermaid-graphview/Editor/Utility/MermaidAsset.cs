using UnityEngine;

namespace Ars.MermaidGraphView
{
    public class MermaidAsset : ScriptableObject
    {
        [TextArea(10, 50)]
        public string mermaidSource;
    }
}
