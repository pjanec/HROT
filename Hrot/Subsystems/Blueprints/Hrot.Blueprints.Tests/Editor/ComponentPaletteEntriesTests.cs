using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor;
using Hrot.Blueprints.Editor.NodeDrawers;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// CA-02 (Slice 1a) — headless tests for <see cref="ComponentPaletteEntries.GetComponentEntries"/>,
/// mirroring <c>MakeBreakStructPaletteEntries</c>'s / <c>SharedNodePaletteEntriesTests</c>' pattern.
/// A component is ALWAYS multi-pin (no "whole component" pin value), so every entry produced here
/// must bake a full <see cref="GetComponentNode.Fields"/> list -- there is no collapsed/legacy
/// authoring path.
/// </summary>
public sealed class ComponentPaletteEntriesTests
{
    private struct HealthTestComponent
    {
        public int Health;
        public float Armor;
    }

    private struct EmptyTagTestComponent
    {
        // zero public instance fields -- must be SKIPPED by the palette (nothing to read).
    }

    // CA-05 (Slice 1b): a genuinely managed (class) component.
    private sealed class ManagedTestComponentClass
    {
        public int Health;
    }

    private sealed class FakeComponentTypeProvider : IComponentTypeProvider
    {
        private readonly IReadOnlyList<string> _fqns;
        public FakeComponentTypeProvider(params string[] fqns)
            => _fqns = fqns.OrderBy(s => s, StringComparer.Ordinal).ToList();
        public IReadOnlyList<string> GetComponentTypeFqns() => _fqns;
    }

    // ── GetComponentEntries: shape + CreateInstance ──────────────────────────

    [Fact]
    public void GetComponentEntries_KnownComponentType_YieldsOneEntry_UnderComponentCategory()
    {
        var fqn = typeof(HealthTestComponent).FullName!;
        var provider = new FakeComponentTypeProvider(fqn);

        var entry = Assert.Single(ComponentPaletteEntries.GetComponentEntries(provider));

        Assert.Equal($"Component.Get.{fqn}", entry.Kind);
        Assert.Equal(BlueprintNodePaletteEntries.Categories.Component, entry.Category);
        Assert.False(string.IsNullOrWhiteSpace(entry.Tooltip));
        Assert.Contains(nameof(HealthTestComponent), entry.DisplayName);
    }

    [Fact]
    public void CreateInstance_BakesComponentTypeFqnAndFullFieldSet()
    {
        var fqn = typeof(HealthTestComponent).FullName!;
        var entry = ComponentPaletteEntries.GetComponentEntries(new FakeComponentTypeProvider(fqn)).Single();

        var node = Assert.IsType<GetComponentNode>(entry.CreateInstance());

        Assert.Equal(fqn, node.ComponentTypeFqn);
        Assert.NotNull(node.Fields);
        Assert.Equal(2, node.Fields!.Count);
        Assert.Contains(node.Fields, f => f.Name == "Health" && f.TypeId == typeof(int).FullName);
        Assert.Contains(node.Fields, f => f.Name == "Armor"  && f.TypeId == typeof(float).FullName);
        Assert.NotEqual(Guid.Empty, node.Id);
    }

    [Fact]
    public void CreateInstance_TwoCalls_ReturnDistinctIds_AndDistinctFieldsListInstances()
    {
        var fqn = typeof(HealthTestComponent).FullName!;
        var entry = ComponentPaletteEntries.GetComponentEntries(new FakeComponentTypeProvider(fqn)).Single();

        var node1 = (GetComponentNode)entry.CreateInstance();
        var node2 = (GetComponentNode)entry.CreateInstance();

        Assert.NotEqual(node1.Id, node2.Id);
        Assert.NotSame(node1.Fields, node2.Fields); // never share a mutable Fields list across placements
    }

    // ── CA-05 (Slice 1b): GetComponentEntries bakes IsManaged for a managed component ──

    [Fact]
    public void CreateInstance_ManagedComponent_BakesIsManagedTrue()
    {
        var fqn = typeof(ManagedTestComponentClass).FullName!;
        var entry = ComponentPaletteEntries.GetComponentEntries(new FakeComponentTypeProvider(fqn)).Single();

        var node = Assert.IsType<GetComponentNode>(entry.CreateInstance());

        Assert.True(node.IsManaged);
    }

    [Fact]
    public void CreateInstance_UnmanagedComponent_BakesIsManagedFalse()
    {
        var fqn = typeof(HealthTestComponent).FullName!;
        var entry = ComponentPaletteEntries.GetComponentEntries(new FakeComponentTypeProvider(fqn)).Single();

        var node = Assert.IsType<GetComponentNode>(entry.CreateInstance());

        Assert.False(node.IsManaged);
    }

    // ── GetComponentEntries: skip rules ──────────────────────────────────────

    [Fact]
    public void GetComponentEntries_ZeroFieldTagComponent_IsSkipped()
    {
        var fqn = typeof(EmptyTagTestComponent).FullName!;
        var entries = ComponentPaletteEntries.GetComponentEntries(new FakeComponentTypeProvider(fqn));
        Assert.Empty(entries);
    }

    [Fact]
    public void GetComponentEntries_UnresolvableType_IsSkipped()
    {
        var entries = ComponentPaletteEntries.GetComponentEntries(
            new FakeComponentTypeProvider("Totally.Unknown.Namespace.NoSuchType"));
        Assert.Empty(entries);
    }

    [Fact]
    public void GetComponentEntries_NullProvider_ReturnsEmpty()
        => Assert.Empty(ComponentPaletteEntries.GetComponentEntries(null!));

    // ── Full bootstrap registry reachability ─────────────────────────────────

    [Fact]
    public void PaletteRegistry_Construction_DoesNotThrow_AndComponentEntriesAreWellFormed()
    {
        // Real-component discovery depends on which engine assemblies happen to be loaded in this
        // test host, so this only asserts construction succeeds and any discovered entries are
        // well-formed -- the FakeComponentTypeProvider tests above cover the actual entry logic
        // deterministically.
        var registry = BlueprintEditorBootstrap.CreatePaletteRegistry();

        foreach (var descriptor in registry.EnumerateAll().Where(d => d.Kind.StartsWith("Component.Get.", StringComparison.Ordinal)))
        {
            Assert.Equal(BlueprintNodePaletteEntries.Categories.Component, descriptor.Category);
            Assert.IsType<GetComponentNode>(descriptor.CreateInstance());
        }
    }

    // ── CA-04 (Slice W1): SetComponentEntries ─────────────────────────────────

    [Fact]
    public void SetComponentEntries_KnownComponentType_YieldsOneEntry_UnderComponentCategory()
    {
        var fqn = typeof(HealthTestComponent).FullName!;
        var provider = new FakeComponentTypeProvider(fqn);

        var entry = Assert.Single(ComponentPaletteEntries.SetComponentEntries(provider));

        Assert.Equal($"Component.Set.{fqn}", entry.Kind);
        Assert.Equal(BlueprintNodePaletteEntries.Categories.Component, entry.Category);
        Assert.False(string.IsNullOrWhiteSpace(entry.Tooltip));
        Assert.Contains(nameof(HealthTestComponent), entry.DisplayName);
    }

    [Fact]
    public void SetComponentEntries_CreateInstance_BakesComponentTypeFqnAndFullFieldSet()
    {
        var fqn = typeof(HealthTestComponent).FullName!;
        var entry = ComponentPaletteEntries.SetComponentEntries(new FakeComponentTypeProvider(fqn)).Single();

        var node = Assert.IsType<SetComponentNode>(entry.CreateInstance());

        Assert.Equal(fqn, node.ComponentTypeFqn);
        Assert.NotNull(node.Fields);
        Assert.Equal(2, node.Fields!.Count);
        Assert.Contains(node.Fields, f => f.Name == "Health" && f.TypeId == typeof(int).FullName);
        Assert.Contains(node.Fields, f => f.Name == "Armor"  && f.TypeId == typeof(float).FullName);
        Assert.NotEqual(Guid.Empty, node.Id);
        Assert.False(node.IsManaged); // this batch (W1) never bakes managed=true from the palette
    }

    [Fact]
    public void SetComponentEntries_CreateInstance_TwoCalls_ReturnDistinctIds_AndDistinctFieldsListInstances()
    {
        var fqn = typeof(HealthTestComponent).FullName!;
        var entry = ComponentPaletteEntries.SetComponentEntries(new FakeComponentTypeProvider(fqn)).Single();

        var node1 = (SetComponentNode)entry.CreateInstance();
        var node2 = (SetComponentNode)entry.CreateInstance();

        Assert.NotEqual(node1.Id, node2.Id);
        Assert.NotSame(node1.Fields, node2.Fields); // never share a mutable Fields list across placements
    }

    [Fact]
    public void SetComponentEntries_ZeroFieldTagComponent_IsSkipped()
    {
        var fqn = typeof(EmptyTagTestComponent).FullName!;
        var entries = ComponentPaletteEntries.SetComponentEntries(new FakeComponentTypeProvider(fqn));
        Assert.Empty(entries);
    }

    [Fact]
    public void SetComponentEntries_UnresolvableType_IsSkipped()
    {
        var entries = ComponentPaletteEntries.SetComponentEntries(
            new FakeComponentTypeProvider("Totally.Unknown.Namespace.NoSuchType"));
        Assert.Empty(entries);
    }

    [Fact]
    public void SetComponentEntries_NullProvider_ReturnsEmpty()
        => Assert.Empty(ComponentPaletteEntries.SetComponentEntries(null!));

    [Fact]
    public void PaletteRegistry_Construction_DoesNotThrow_AndSetComponentEntriesAreWellFormed()
    {
        // Real writable-component discovery depends on which engine assemblies happen to be loaded
        // and carry [BlueprintWritable] in this test host, so this only asserts construction
        // succeeds and any discovered entries are well-formed -- the FakeComponentTypeProvider tests
        // above cover the actual entry logic deterministically.
        var registry = BlueprintEditorBootstrap.CreatePaletteRegistry();

        foreach (var descriptor in registry.EnumerateAll().Where(d => d.Kind.StartsWith("Component.Set.", StringComparison.Ordinal)))
        {
            Assert.Equal(BlueprintNodePaletteEntries.Categories.Component, descriptor.Category);
            Assert.IsType<SetComponentNode>(descriptor.CreateInstance());
        }
    }

    // ── CA-07c/CA-07d-1: ConsumerEntries (ComponentForEach/ItemGet/ItemCount/Contains/Find) ──

    [Fact]
    public void ConsumerEntries_YieldsExactlyFiveStaticEntries()
        => Assert.Equal(5, ComponentPaletteEntries.ConsumerEntries().Count());

    [Fact]
    public void ConsumerEntries_AllUnderComponentCategory_WithNonEmptyTooltips()
    {
        foreach (var entry in ComponentPaletteEntries.ConsumerEntries())
        {
            Assert.Equal(BlueprintNodePaletteEntries.Categories.Component, entry.Category);
            Assert.False(string.IsNullOrWhiteSpace(entry.Tooltip));
            Assert.False(string.IsNullOrWhiteSpace(entry.DisplayName));
        }
    }

    [Fact]
    public void ConsumerEntries_CreateInstance_ComponentForEach_BlankNode_EmptyBakedProps()
    {
        var entry = ComponentPaletteEntries.ConsumerEntries().Single(e => e.Kind == "Component.ForEach");
        var node = Assert.IsType<ComponentForEachNode>(entry.CreateInstance());

        Assert.Equal("", node.ComponentTypeFqn);
        Assert.Equal("", node.CountAccessorFqn);
        Assert.Equal("", node.ItemAccessorFqn);
        Assert.Equal("", node.ElementTypeFqn);
        Assert.NotEqual(Guid.Empty, node.Id);
    }

    [Fact]
    public void ConsumerEntries_CreateInstance_ComponentItemGet_BlankNode_EmptyBakedProps()
    {
        var entry = ComponentPaletteEntries.ConsumerEntries().Single(e => e.Kind == "Component.ItemGet");
        var node = Assert.IsType<ComponentItemGetNode>(entry.CreateInstance());

        Assert.Equal("", node.ComponentTypeFqn);
        Assert.Equal("", node.ItemAccessorFqn);
        Assert.Equal("", node.ElementTypeFqn);
        Assert.NotEqual(Guid.Empty, node.Id);
    }

    [Fact]
    public void ConsumerEntries_CreateInstance_ComponentItemCount_BlankNode_EmptyBakedProps()
    {
        var entry = ComponentPaletteEntries.ConsumerEntries().Single(e => e.Kind == "Component.ItemCount");
        var node = Assert.IsType<ComponentItemCountNode>(entry.CreateInstance());

        Assert.Equal("", node.ComponentTypeFqn);
        Assert.Equal("", node.CountAccessorFqn);
        Assert.NotEqual(Guid.Empty, node.Id);
    }

    [Fact]
    public void ConsumerEntries_CreateInstance_ComponentContains_BlankNode_EmptyBakedProps()
    {
        var entry = ComponentPaletteEntries.ConsumerEntries().Single(e => e.Kind == "Component.Contains");
        var node = Assert.IsType<ComponentContainsNode>(entry.CreateInstance());

        Assert.Equal("", node.ComponentTypeFqn);
        Assert.Equal("", node.CountAccessorFqn);
        Assert.Equal("", node.ItemAccessorFqn);
        Assert.Equal("", node.ElementTypeFqn);
        Assert.NotEqual(Guid.Empty, node.Id);
    }

    [Fact]
    public void ConsumerEntries_CreateInstance_ComponentFind_BlankNode_EmptyBakedProps()
    {
        var entry = ComponentPaletteEntries.ConsumerEntries().Single(e => e.Kind == "Component.Find");
        var node = Assert.IsType<ComponentFindNode>(entry.CreateInstance());

        Assert.Equal("", node.ComponentTypeFqn);
        Assert.Equal("", node.CountAccessorFqn);
        Assert.Equal("", node.ItemAccessorFqn);
        Assert.Equal("", node.ElementTypeFqn);
        Assert.NotEqual(Guid.Empty, node.Id);
    }

    [Fact]
    public void ConsumerEntries_TwoCalls_ReturnDistinctIds()
    {
        var entry = ComponentPaletteEntries.ConsumerEntries().Single(e => e.Kind == "Component.ForEach");
        var node1 = (ComponentForEachNode)entry.CreateInstance();
        var node2 = (ComponentForEachNode)entry.CreateInstance();
        Assert.NotEqual(node1.Id, node2.Id);
    }

    [Fact]
    public void PaletteRegistry_Construction_DiscoversAllFiveConsumerEntries()
    {
        var registry = BlueprintEditorBootstrap.CreatePaletteRegistry();
        var kinds = registry.EnumerateAll().Select(d => d.Kind).ToList();

        Assert.Contains("Component.ForEach",   kinds);
        Assert.Contains("Component.ItemGet",   kinds);
        Assert.Contains("Component.ItemCount", kinds);
        Assert.Contains("Component.Contains",  kinds);
        Assert.Contains("Component.Find",      kinds);
    }
}
