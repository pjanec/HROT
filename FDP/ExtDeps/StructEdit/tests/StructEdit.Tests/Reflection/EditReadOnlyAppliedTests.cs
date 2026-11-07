using System.Numerics;
using FluentAssertions;
using StructEdit.Core;
using StructEdit.Core.Attributes;
using StructEdit.Reflection;

namespace StructEdit.Tests.Reflection;

public class RoSubject
{
    [EditReadOnly] public int Id;
    [EditReadOnly] public Vector3 Origin;
    public float Speed;
}

/// <summary>
/// ⭐ S8 (📄 docs/DESIGN_Struct_Editor.md §6 S8, CE-3381) — <c>[EditReadOnly]</c> is APPLIED by the builder: the member's node and its whole subtree are read-only; a sibling is not.
/// 🔴 Red-proof (compiles): <c>BuildNode</c> returning <c>node</c> unconditionally ⇒ red.
/// </summary>
public class EditReadOnlyAppliedTests
{
    [Fact]
    public void AnEditReadOnlyMember_AndItsSubtree_AreReadOnly_ASiblingIsNot()
    {
        using var s = new ComponentEditServiceBuilder().Build().Open(new RoSubject(), typeof(RoSubject));
        var rows = s.Document.Root.Children;
        rows.First(r => r.Name == "Id").IsReadOnly.Should().BeTrue();
        var origin = rows.First(r => r.Name == "Origin");
        origin.IsReadOnly.Should().BeTrue();
        origin.Children.Should().NotBeEmpty().And.OnlyContain(c => c.IsReadOnly);
        rows.First(r => r.Name == "Speed").IsReadOnly.Should().BeFalse();
    }
}
