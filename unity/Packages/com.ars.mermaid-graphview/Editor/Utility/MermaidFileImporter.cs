using UnityEditor.AssetImporters;
using UnityEngine;

namespace Ars.MermaidGraphView
{
    [ScriptedImporter(1, "mmd")]
    public class MermaidFileImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var text = System.IO.File.ReadAllText(ctx.assetPath);
            var asset = ScriptableObject.CreateInstance<MermaidAsset>();
            asset.mermaidSource = text;
            ctx.AddObjectToAsset("mermaid", asset);
            ctx.SetMainObject(asset);
        }
    }
}
