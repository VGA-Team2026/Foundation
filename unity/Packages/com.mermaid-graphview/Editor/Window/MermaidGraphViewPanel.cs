using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;

namespace MermaidGraphView
{
    public class MermaidGraphViewPanel : GraphView
    {
        public MermaidGraphViewPanel()
        {
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            this.AddManipulator(new ContentZoomer());

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            style.flexGrow = 1;
        }

        public void Render(MermaidDocument document, LayoutResult layout)
        {
            ClearAll();

            var renderer = RendererFactory.Create(document.DiagramType);
            renderer.Render(this, document, layout);
        }

        private void ClearAll()
        {
            graphElements.ForEach(e => RemoveElement(e));

            var toRemove = new List<VisualElement>();
            foreach (var child in contentContainer.Children())
            {
                if (child is SequenceLifelineElement
                    || child is SequenceArrowElement
                    || child is SequenceBlockElement
                    || child is SequenceParticipantElement)
                {
                    toRemove.Add(child);
                }
            }

            foreach (var el in toRemove)
            {
                el.RemoveFromHierarchy();
            }
        }
    }
}
