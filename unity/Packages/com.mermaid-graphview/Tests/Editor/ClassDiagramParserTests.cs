using System.Linq;
using NUnit.Framework;

namespace MermaidGraphView.Tests
{
    [TestFixture]
    public class ClassDiagramParserTests
    {
        private ClassDiagramDocument ParseClassDiagram(string source)
        {
            var parser = ParserFactory.Create(source);
            var doc = parser.Parse(source);
            Assert.AreEqual(DiagramType.ClassDiagram, doc.DiagramType);
            Assert.IsNotNull(doc.Content);
            return (ClassDiagramDocument)doc.Content;
        }

        [Test]
        public void Parse_ClassWithFieldsAndMethods_ExtractsMembers()
        {
            var source = @"classDiagram
    class Animal {
        +String name
        +int age
        +eat(food) void
        +sleep() void
    }";

            var diagram = ParseClassDiagram(source);

            Assert.AreEqual(1, diagram.Classes.Count);
            var animal = diagram.Classes[0];
            Assert.AreEqual("Animal", animal.Name);

            Assert.That(animal.Fields.Count, Is.GreaterThanOrEqualTo(2),
                "Should have at least 2 fields");
            Assert.That(animal.Methods.Count, Is.GreaterThanOrEqualTo(2),
                "Should have at least 2 methods");
        }

        [Test]
        public void Parse_VisibilityMarkers_SetsCorrectVisibility()
        {
            var source = @"classDiagram
    class MyClass {
        +publicField String
        -privateField int
        #protectedField bool
        ~internalField float
    }";

            var diagram = ParseClassDiagram(source);

            var cls = diagram.Classes[0];
            Assert.That(cls.Fields.Count, Is.GreaterThanOrEqualTo(4));

            var publicMember = cls.Fields.FirstOrDefault(f => f.Name.Contains("publicField"));
            Assert.IsNotNull(publicMember);
            Assert.AreEqual(MemberVisibility.Public, publicMember.Visibility);

            var privateMember = cls.Fields.FirstOrDefault(f => f.Name.Contains("privateField"));
            Assert.IsNotNull(privateMember);
            Assert.AreEqual(MemberVisibility.Private, privateMember.Visibility);

            var protectedMember = cls.Fields.FirstOrDefault(f => f.Name.Contains("protectedField"));
            Assert.IsNotNull(protectedMember);
            Assert.AreEqual(MemberVisibility.Protected, protectedMember.Visibility);

            var internalMember = cls.Fields.FirstOrDefault(f => f.Name.Contains("internalField"));
            Assert.IsNotNull(internalMember);
            Assert.AreEqual(MemberVisibility.Internal, internalMember.Visibility);
        }

        [Test]
        public void Parse_InheritanceRelation_SetsCorrectRelationType()
        {
            var source = @"classDiagram
    Animal <|-- Dog";

            var diagram = ParseClassDiagram(source);

            Assert.That(diagram.Relations.Count, Is.GreaterThanOrEqualTo(1));

            var relation = diagram.Relations[0];
            Assert.AreEqual(RelationType.Inheritance, relation.Type);

            // The relation connects Animal and Dog
            Assert.That(
                (relation.FromClass == "Animal" && relation.ToClass == "Dog") ||
                (relation.FromClass == "Dog" && relation.ToClass == "Animal"),
                Is.True,
                "Inheritance relation should connect Animal and Dog");
        }

        [Test]
        public void Parse_MultipleRelations_CreatesAllRelations()
        {
            var source = @"classDiagram
    Animal <|-- Dog
    Animal <|-- Cat
    Dog --> Bone";

            var diagram = ParseClassDiagram(source);

            Assert.That(diagram.Relations.Count, Is.GreaterThanOrEqualTo(3),
                "Should have at least 3 relations");
        }

        [Test]
        public void Parse_Stereotype_CapturesAnnotation()
        {
            var source = @"classDiagram
    class IFlyable {
        <<interface>>
        +fly() void
    }";

            var diagram = ParseClassDiagram(source);

            var cls = diagram.Classes.FirstOrDefault(c => c.Name == "IFlyable");
            Assert.IsNotNull(cls);
            Assert.AreEqual("interface", cls.Stereotype);
        }

        [Test]
        public void Parse_MultipleClasses_CreatesAllClasses()
        {
            var source = @"classDiagram
    class Dog {
        +bark() void
    }
    class Cat {
        +meow() void
    }";

            var diagram = ParseClassDiagram(source);

            Assert.AreEqual(2, diagram.Classes.Count);
            Assert.IsNotNull(diagram.Classes.FirstOrDefault(c => c.Name == "Dog"));
            Assert.IsNotNull(diagram.Classes.FirstOrDefault(c => c.Name == "Cat"));
        }

        [Test]
        public void Parse_RelationWithLabel_CapturesLabel()
        {
            var source = @"classDiagram
    Customer --> Order : places";

            var diagram = ParseClassDiagram(source);

            Assert.That(diagram.Relations.Count, Is.GreaterThanOrEqualTo(1));
            var relation = diagram.Relations[0];
            Assert.AreEqual("places", relation.Label);
        }
    }
}
