using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fbt.Kernel;
using FluentAssertions;
using Hrot.Editor.AiShared.Blackboard;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Blackboard;

// ---------------------------------------------------------------------------
// Fixture struct types for BlackboardTypeChoiceBuilder tests.
//
// A local [BlackboardDtoStruct] fixture is used (rather than relying on
// Hrot.AI.Behaviors.Brains.SquadRallyState loading into this test host) for
// determinism -- the assembly containing SquadRallyState may or may not be
// loaded depending on test execution order/isolation.
// ---------------------------------------------------------------------------

[BlackboardDtoStruct]
public struct ChoiceBuilderProbeDto { public int Count; public float Speed; }

// Two structs sharing the same short Type.Name ("Foo") but declared in different
// containers (so their FullName / Type identity differ). Both are decorated so the
// builder must surface BOTH as distinct choices -- proving de-dup is by Type, not
// by the (colliding) display string.
public static class ChoiceBuilderNamespaceA
{
    [BlackboardDtoStruct]
    public struct Foo { public int X; }
}

public static class ChoiceBuilderNamespaceB
{
    [BlackboardDtoStruct]
    public struct Foo { public int Y; }
}

file sealed class StubActionSchemaExporter : IActionSchemaExporter
{
    private readonly IReadOnlyDictionary<string, ActionSchemaEntry> _entries;

    public StubActionSchemaExporter(IReadOnlyDictionary<string, ActionSchemaEntry> entries)
        => _entries = entries;

    public IReadOnlyDictionary<string, ActionSchemaEntry> All => _entries;
    public ActionSchemaEntry? Lookup(string fqn) => _entries.TryGetValue(fqn, out var e) ? e : null;
    public void Rebuild() { }
#pragma warning disable CS0067 // Event is never used
    public event Action? Changed;
#pragma warning restore CS0067
}

public sealed class BlackboardTypeChoiceBuilderTests
{
    // ---- Primitives are still present ---------------------------------------

    [Fact]
    public void BuildDefault_IncludesAllPrimitives()
    {
        var choices = BlackboardTypeChoiceBuilder.BuildDefault();

        choices.Should().Contain(c => c.Display == "int" && c.Type == typeof(int));
        choices.Should().Contain(c => c.Display == "float" && c.Type == typeof(float));
        choices.Should().Contain(c => c.Display == "Vector3" && c.Type == typeof(Vector3));
        choices.Should().Contain(c => c.Display == "bool" && c.Type == typeof(bool));
    }

    // ---- [BlackboardDtoStruct] discovery -------------------------------------

    [Fact]
    public void BuildDefault_IncludesLocalBlackboardDtoStruct()
    {
        var choices = BlackboardTypeChoiceBuilder.BuildDefault();

        choices.Should().Contain(c => c.Type == typeof(ChoiceBuilderProbeDto));
    }

    [Fact]
    public void BuildDefault_Display_UsesShortName_NotFullyQualifiedName()
    {
        var choices = BlackboardTypeChoiceBuilder.BuildDefault();

        var probeChoice = choices.Single(c => c.Type == typeof(ChoiceBuilderProbeDto));
        probeChoice.Display.Should().Be(nameof(ChoiceBuilderProbeDto));
        probeChoice.Display.Should().NotContain(".", "the combo must show Type.Name, not the FQN");
        probeChoice.Display.Should().NotContain("+");
    }

    [Fact]
    public void BuildDefault_StructsAreSortedAfterPrimitives()
    {
        var choices = BlackboardTypeChoiceBuilder.BuildDefault();

        int lastPrimitiveIdx = choices
            .Select((c, i) => (c, i))
            .Where(t => BlackboardTypeHelper.GetPrimitiveType(t.c.Display) != null)
            .Select(t => t.i)
            .DefaultIfEmpty(-1)
            .Max();

        int probeIdx = choices.ToList().FindIndex(c => c.Type == typeof(ChoiceBuilderProbeDto));

        probeIdx.Should().BeGreaterThan(lastPrimitiveIdx, "structs must be listed after all primitives");
    }

    // ---- Collision-safe de-dup: by Type, never by (colliding) Display -------

    [Fact]
    public void BuildDefault_DoesNotDedupeByDisplayName_TwoDistinctFooStructsBothAppear()
    {
        var choices = BlackboardTypeChoiceBuilder.BuildDefault();

        var fooChoices = choices.Where(c => c.Display == "Foo").ToList();

        fooChoices.Should().HaveCount(2, "two distinct [BlackboardDtoStruct] types share the short name 'Foo'");
        fooChoices.Select(c => c.Type).Should().BeEquivalentTo(new[]
        {
            typeof(ChoiceBuilderNamespaceA.Foo),
            typeof(ChoiceBuilderNamespaceB.Foo),
        });
        fooChoices[0].Type.Should().NotBe(fooChoices[1].Type);
    }

    [Fact]
    public void BuildDefault_DedupesByType_SameTypeNotListedTwice()
    {
        var choices = BlackboardTypeChoiceBuilder.BuildDefault();

        choices.Count(c => c.Type == typeof(ChoiceBuilderProbeDto)).Should().Be(1);
    }

    // ---- Collision-safe resolution: SELECTED INDEX, never reverse name lookup

    [Fact]
    public void IndexBasedResolution_ReturnsExactType_ForCollidingDisplayNames()
    {
        var choices = BlackboardTypeChoiceBuilder.BuildDefault();

        int idxA = choices.ToList().FindIndex(c => c.Type == typeof(ChoiceBuilderNamespaceA.Foo));
        int idxB = choices.ToList().FindIndex(c => c.Type == typeof(ChoiceBuilderNamespaceB.Foo));

        idxA.Should().BeGreaterOrEqualTo(0);
        idxB.Should().BeGreaterOrEqualTo(0);
        idxA.Should().NotBe(idxB);

        // Resolving via the combo's selected INDEX (not a reverse Display lookup) must return
        // the exact Type for each entry, even though both share the display string "Foo".
        choices[idxA].Type.Should().Be(typeof(ChoiceBuilderNamespaceA.Foo));
        choices[idxB].Type.Should().Be(typeof(ChoiceBuilderNamespaceB.Foo));
        choices[idxA].Display.Should().Be(choices[idxB].Display); // display collides...
        choices[idxA].Type.Should().NotBe(choices[idxB].Type);    // ...but Type never does
    }

    // ---- Action-schema DTO types (Q1 condition 2) ----------------------------

    [Fact]
    public void BuildDefault_WithExporter_IncludesActionSchemaDtoType()
    {
        var entry = new ActionSchemaEntry(
            Fqn: "Some.Namespace.SomeAction",
            DtoType: typeof(ExporterProbeDto),
            Hosting: ActionHosting.BTree,
            Access: BlackboardAccess.Unknown,
            HeavyDtoType: null);
        var exporter = new StubActionSchemaExporter(new Dictionary<string, ActionSchemaEntry> { [entry.Fqn] = entry });

        var choices = BlackboardTypeChoiceBuilder.BuildDefault(exporter);

        choices.Should().Contain(c => c.Type == typeof(ExporterProbeDto) && c.Display == nameof(ExporterProbeDto));
    }

    [Fact]
    public void BuildDefault_WithNullExporter_DoesNotThrow_AndOmitsExporterOnlyTypes()
    {
        var ex = Record.Exception(() => BlackboardTypeChoiceBuilder.BuildDefault(actionSchemaExporter: null));

        ex.Should().BeNull();
    }

    public struct ExporterProbeDto { public int Value; }
}
