using System.Collections.Generic;

namespace MermaidGraphView
{
    public enum MemberVisibility
    {
        Public,
        Private,
        Protected,
        Internal
    }

    public enum RelationType
    {
        Inheritance,
        Composition,
        Aggregation,
        Association,
        Dependency,
        Realization,
        Link
    }

    public class ClassDiagramDocument
    {
        public List<ClassNode> Classes { get; set; } = new List<ClassNode>();
        public List<ClassRelation> Relations { get; set; } = new List<ClassRelation>();
    }

    public class ClassNode
    {
        public string Name { get; set; }
        public string Stereotype { get; set; }
        public List<ClassMember> Fields { get; set; } = new List<ClassMember>();
        public List<ClassMember> Methods { get; set; } = new List<ClassMember>();

        public ClassNode() { }

        public ClassNode(string name)
        {
            Name = name;
        }
    }

    public class ClassMember
    {
        public MemberVisibility Visibility { get; set; } = MemberVisibility.Public;
        public string Name { get; set; }
        public string Type { get; set; }
        public bool IsMethod { get; set; }

        public ClassMember() { }

        public ClassMember(string name, string type, bool isMethod, MemberVisibility visibility = MemberVisibility.Public)
        {
            Name = name;
            Type = type;
            IsMethod = isMethod;
            Visibility = visibility;
        }
    }

    public class ClassRelation
    {
        public string FromClass { get; set; }
        public string ToClass { get; set; }
        public RelationType Type { get; set; } = RelationType.Association;
        public string Label { get; set; }
        public string FromCardinality { get; set; }
        public string ToCardinality { get; set; }

        public ClassRelation() { }

        public ClassRelation(string fromClass, string toClass, RelationType type, string label = null)
        {
            FromClass = fromClass;
            ToClass = toClass;
            Type = type;
            Label = label;
        }
    }
}
