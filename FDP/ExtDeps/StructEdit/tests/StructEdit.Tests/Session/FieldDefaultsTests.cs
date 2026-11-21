using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using StructEdit.Core;
using StructEdit.Core.Declared;
using StructEdit.Core.Defaults;
using StructEdit.Reflection;

namespace StructEdit.Tests.Session;

public class DefSubject
{
    public float Speed = 4f;
    public string Name = "Alpha";
    public List<int> Waves = new() { 1, 2 };
}

/// <summary>
/// ⭐ S7 (📄 docs/DESIGN_Struct_Editor.md §6 S7, CE-3381) — a node's default and "changed": a C# document against a fresh instance BY PATH, a declared document against its
/// declarations; a list equal by content is not changed; a list item past the defaults' count has no default.
/// 🔴 Red-proof (compiles): <c>FieldValues.Same</c> answering <c>a.Equals(b)</c> only ⇒ an equal list reads "changed" ⇒ red.
/// </summary>
public class FieldDefaultsTests
{
    [Fact]
    public void ACSharpDocument_ComparesWithAFreshInstance_ByPath()
    {
        var svc = new ComponentEditServiceBuilder().Build();
        using var defaults = svc.Open(new DefSubject(), typeof(DefSubject));
        using var s = svc.Open(new DefSubject { Speed = 9f, Waves = { 3 } }, typeof(DefSubject));
        var d = new DocumentDefaults(defaults.Document);
        Node(s, "Speed").Should().Match<EditNode>(n => FieldValues.IsChanged(n, d));
        FieldValues.IsChanged(Node(s, "Name"), d).Should().BeFalse();
        FieldValues.IsChanged(Node(s, "Waves"), d).Should().BeTrue();
        d.TryGetDefault(Node(s, "Waves").Children[2], out _).Should().BeFalse();   // [2] has no counterpart in the defaults
    }

    [Fact]
    public void AnEqualList_IsNotChanged()
    {
        var svc = new ComponentEditServiceBuilder().Build();
        using var defaults = svc.Open(new DefSubject(), typeof(DefSubject));
        using var s = svc.Open(new DefSubject(), typeof(DefSubject));
        FieldValues.IsChanged(Node(s, "Waves"), new DocumentDefaults(defaults.Document)).Should().BeFalse();
    }

    [Fact]
    public void ADeclaredDocument_ComparesWithItsDeclarations()
    {
        var fields = new[] { new FieldDeclaration("speed", typeof(float), Default: JsonValue.Create(4f)), new FieldDeclaration("name", typeof(string)) };
        using var s = new DeclaredEditSession(new DeclaredEditDocumentBuilder(new JsonSerializerOptions()), fields, new JsonObject { ["speed"] = 4f, ["name"] = "x" });
        FieldValues.IsChanged(s.Document.Root.Children[0], DeclaredDefaults.Instance).Should().BeFalse();   // set, but equal to the default
        FieldValues.IsChanged(s.Document.Root.Children[1], DeclaredDefaults.Instance).Should().BeTrue();    // "x" vs ""
    }

    private static EditNode Node(IEditSession s, string name) => s.Document.Root.Children.First(c => c.Name == name);
}
