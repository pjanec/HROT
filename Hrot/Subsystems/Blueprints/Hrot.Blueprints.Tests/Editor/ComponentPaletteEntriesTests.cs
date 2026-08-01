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
}
