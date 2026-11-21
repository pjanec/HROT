using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using StructEdit.Core;
using StructEdit.Core.Declared;
using StructEdit.Core.Plugins;

namespace StructEdit.Tests.Session;

public enum DeclMood { Calm, Alert }

public sealed class DeclLeafEditor : ICustomFieldEditor
{
    public Type TargetType => typeof(Guid);
    public EditNode? CreateNode(EditNodeId id, string name, string jsonPath, IValueBinding binding, EditNodeMetadata metadata)
        => new(id, name, jsonPath, EditNodeKind.Custom, typeof(Guid), binding, metadata: metadata);
}

/// <summary>
/// ⭐ S2 (📄 docs/DESIGN_Struct_Editor.md §6 S2, §10.5, CE-3383) — the DECLARED document: a shape from declarations, values in a <see cref="JsonObject"/>. Leaves read the JSON
/// (or the default), a set writes the member, a reset REMOVES it (S7: not set), a structure's field writes back through its parent, a list's in-place move reaches the JSON
/// on Commit, a registered type is ONE leaf, and a steady read allocates nothing.
/// 🔴 Red-proof (compiles): <c>DeclaredEditSession.Commit</c> without its <c>Flush()</c> ⇒ the moved list never reaches the JSON ⇒ <c>AListMove…</c> red.
/// </summary>
public class DeclaredEditDocumentTests
{
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static readonly FieldDeclaration[] Fields =
    {
        new("speed", typeof(float), "Speed", "m/s", JsonValue.Create(4f)),
        new("name", typeof(string)),
        new("mood", typeof(DeclMood)),
        new("home", typeof(Vector3)),
        new("waves", typeof(List<int>)),
        new("id", typeof(Guid)),
    };

    private static DeclaredEditSession Open(JsonObject values)
        => new(new DeclaredEditDocumentBuilder(Options, new Dictionary<Type, ICustomFieldEditor> { [typeof(Guid)] = new DeclLeafEditor() }), Fields, values);

    private static EditNode Row(DeclaredEditSession s, string name) => s.Document.Root.Children.First(c => c.Name == name);

    [Fact]
    public void ALeaf_ReadsTheJson_OrItsDefault_AndASetWritesTheMember()
    {
        var values = new JsonObject { ["name"] = "Alpha", ["mood"] = "Alert" };
        using var s = Open(values);
        Row(s, "speed").Binding!.GetBoxed().Should().Be(4f);   // the declaration's default — not in the JSON
        Row(s, "speed").Metadata.Unit.Should().Be("m/s");
        Row(s, "name").Binding!.GetBoxed().Should().Be("Alpha");
        Row(s, "mood").Kind.Should().Be(EditNodeKind.Enum);
        Row(s, "mood").Binding!.GetBoxed().Should().Be(DeclMood.Alert);
        Row(s, "speed").Binding!.SetBoxed(7.5f);
        ((JsonObject)s.Commit())["speed"]!.GetValue<float>().Should().Be(7.5f);
        s.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void AReset_RemovesTheMember_SoTheDefaultShowsAgain()
    {
        var values = new JsonObject { ["speed"] = 9f };
        using var s = Open(values);
        var b = (JsonMemberBinding)Row(s, "speed").Binding!;
        b.IsSet.Should().BeTrue();
        b.Reset();
        b.IsSet.Should().BeFalse();
        values.ContainsKey("speed").Should().BeFalse();
        b.GetBoxed().Should().Be(4f);
    }

    [Fact]
    public void AStructuresField_WritesBackThroughItsParent_IntoTheJson()
    {
        var values = new JsonObject();
        using var s = Open(values);
        var home = Row(s, "home");
        home.Kind.Should().Be(EditNodeKind.Struct);
        home.Children.Select(c => c.Name).Should().Contain(new[] { "X", "Y", "Z" });
        home.Children.First(c => c.Name == "Y").Binding!.SetBoxed(12f);
        values["home"]!["Y"]!.GetValue<float>().Should().Be(12f);
    }

    [Fact]
    public void AListMove_IsInPlace_AndReachesTheJsonOnCommit_AnInsertRebuilds()
    {
        var values = new JsonObject { ["waves"] = new JsonArray(1, 2, 3) };
        using var s = Open(values);
        var ops = (IContainerOps)Row(s, "waves").Binding!;
        ops.Move(0, 2);
        s.RebuildState.Should().Be(EditRebuildState.Stable);
        ((JsonObject)s.Commit())["waves"]!.ToJsonString().Should().Be("[2,3,1]");
        ops.Insert(0, ops.NewElement());
        s.MarkStructuralChange();
        s.RebuildDocument();
        Row(s, "waves").Children.Should().HaveCount(4);
        values["waves"]!.ToJsonString().Should().Be("[0,2,3,1]");
    }

    [Fact]
    public void ARegisteredType_IsOneLeaf_AndAnUntouchedListStaysUnset()
    {
        var values = new JsonObject();
        using var s = Open(values);
        Row(s, "id").Kind.Should().Be(EditNodeKind.Custom);
        Row(s, "waves").Children.Should().BeEmpty();
        s.Commit();
        values.ContainsKey("waves").Should().BeFalse();   // read, never changed ⇒ still "not set" (S7)
        s.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void Cancel_PutsTheValuesBack()
    {
        var values = new JsonObject { ["name"] = "a" };
        using var s = Open(values);
        Row(s, "name").Binding!.SetBoxed("b");
        s.Cancel();
        values["name"]!.GetValue<string>().Should().Be("a");
        Row(s, "name").Binding!.GetBoxed().Should().Be("a");
    }

    [Fact]
    public void ASteadyRead_AllocatesNothing()
    {
        using var s = Open(new JsonObject { ["speed"] = 2f, ["name"] = "x" });
        var speed = Row(s, "speed").Binding!;
        var name = Row(s, "name").Binding!;
        speed.GetBoxed(); name.GetBoxed();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++) { speed.GetBoxed(); name.GetBoxed(); }
        (GC.GetAllocatedBytesForCurrentThread() - before).Should().Be(0);
    }
}
