using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;

namespace MermaidGraphView
{
    public class MermaidClassNode : MermaidNode
    {
        public MermaidClassNode(string name, string stereotype, List<ClassMember> fields, List<ClassMember> methods, NodeVisualStyle style)
            : base(name, name, style)
        {
            if (!string.IsNullOrEmpty(stereotype))
            {
                var stereoLabel = new Label($"\u00AB{stereotype}\u00BB");
                stereoLabel.AddToClassList("mermaid-stereotype");
                titleContainer.Insert(0, stereoLabel);
            }

            var fieldsSection = new VisualElement();
            fieldsSection.AddToClassList("mermaid-class-section");

            if (fields != null)
            {
                foreach (var field in fields)
                {
                    var memberLabel = new Label(FormatMember(field));
                    memberLabel.AddToClassList("mermaid-class-member");

                    if (stereotype != null && stereotype.ToLowerInvariant() == "abstract")
                    {
                        memberLabel.AddToClassList("mermaid-class-member-abstract");
                    }

                    fieldsSection.Add(memberLabel);
                }
            }

            var methodsSection = new VisualElement();
            methodsSection.AddToClassList("mermaid-class-section");

            if (methods != null)
            {
                foreach (var method in methods)
                {
                    var memberLabel = new Label(FormatMember(method));
                    memberLabel.AddToClassList("mermaid-class-member");
                    methodsSection.Add(memberLabel);
                }
            }

            extensionContainer.Add(fieldsSection);
            extensionContainer.Add(methodsSection);
            RefreshExpandedState();
        }

        private static string FormatMember(ClassMember member)
        {
            string visibilityChar = GetVisibilityChar(member.Visibility);
            string typeStr = string.IsNullOrEmpty(member.Type) ? "" : $" : {member.Type}";
            string suffix = member.IsMethod ? "()" : "";
            return $"{visibilityChar} {member.Name}{suffix}{typeStr}";
        }

        private static string GetVisibilityChar(MemberVisibility visibility)
        {
            switch (visibility)
            {
                case MemberVisibility.Public: return "+";
                case MemberVisibility.Private: return "-";
                case MemberVisibility.Protected: return "#";
                case MemberVisibility.Internal: return "~";
                default: return "+";
            }
        }
    }
}
