using Hrot.Blueprints.Editor.NodeDrawers;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// CA-02 (Slice 1a) — headless tests for <see cref="ComponentFieldReflector"/>. Mirrors
/// <c>SharedStructFieldReflector</c>'s reflection tests, but proves the deliberate DIFFERENCES:
/// no <c>Marshal.OffsetOf</c> / no per-field bail-out (managed fields are KEPT, not dropped) and
/// no whole-type bail-out for non-value-type/non-blittable shapes.
/// </summary>
public sealed class ComponentFieldReflectorTests
{
    // ── test-only component-shaped types (never registered with ComponentTypeRegistry --
    //    reflection only cares about the [ComponentId]-free CLR shape here) ──────────────

    private struct UnmanagedTestComponent
    {
        public int Health;
        public float Speed;
    }

    private struct ManagedFieldTestComponent
    {
        public int Ammo;
        public string Label; // managed (reference) field -- must be KEPT, not dropped.
    }

    private struct TagTestComponent
    {
        // Deliberately no public instance fields (zero-size "tag" component).
    }

    // ── TryReflect: field shape ───────────────────────────────────────────────

    [Fact]
    public void TryReflect_UnmanagedFields_AllFlaggedNotManaged()
    {
        var fqn = typeof(UnmanagedTestComponent).FullName!;

        var fields = ComponentFieldReflector.TryReflect(fqn);

        Assert.NotNull(fields);
        Assert.Equal(2, fields!.Count);
        Assert.All(fields, f => Assert.False(f.IsManaged));
        Assert.Contains(fields, f => f.Name == "Health" && f.TypeId == typeof(int).FullName);
        Assert.Contains(fields, f => f.Name == "Speed"  && f.TypeId == typeof(float).FullName);
    }

    [Fact]
    public void TryReflect_ManagedField_IsKeptNotDropped_AndFlaggedManaged()
    {
        var fqn = typeof(ManagedFieldTestComponent).FullName!;

        var fields = ComponentFieldReflector.TryReflect(fqn);

        Assert.NotNull(fields);
        // Both fields kept -- unlike SharedStructFieldReflector, a managed field is never a
        // reason to bail the whole type out.
        Assert.Equal(2, fields!.Count);

        var label = Assert.Single(fields, f => f.Name == "Label");
        Assert.True(label.IsManaged);
        Assert.Equal(typeof(string).FullName, label.TypeId);

        var ammo = Assert.Single(fields, f => f.Name == "Ammo");
        Assert.False(ammo.IsManaged);
    }

    [Fact]
    public void TryReflect_ResolvableZeroFieldTagComponent_ReturnsEmptyListNotNull()
    {
        var fqn = typeof(TagTestComponent).FullName!;

        var fields = ComponentFieldReflector.TryReflect(fqn);

        // Resolved (not null) but empty -- distinguishes "nothing to read" from "unresolved".
        Assert.NotNull(fields);
        Assert.Empty(fields!);
    }

    // ── TryReflect: unresolvable / degenerate input ───────────────────────────

    [Fact]
    public void TryReflect_UnresolvableFqn_ReturnsNull()
        => Assert.Null(ComponentFieldReflector.TryReflect("Totally.Unknown.Namespace.NoSuchType"));

    [Fact]
    public void TryReflect_NullOrEmptyFqn_ReturnsNull()
    {
        Assert.Null(ComponentFieldReflector.TryReflect(null));
        Assert.Null(ComponentFieldReflector.TryReflect(""));
    }

    // ── ResolveType: existence-only check (used by BlueprintNodeModel's stale-ref guard) ──

    [Fact]
    public void ResolveType_FindsLoadedType()
        => Assert.NotNull(ComponentFieldReflector.ResolveType(typeof(UnmanagedTestComponent).FullName!));

    [Fact]
    public void ResolveType_UnknownFqn_ReturnsNull()
        => Assert.Null(ComponentFieldReflector.ResolveType("Totally.Unknown.Namespace.NoSuchType"));

    [Fact]
    public void ResolveType_ResolvesZeroFieldTagComponent_NotConfusedWithUnresolved()
        => Assert.NotNull(ComponentFieldReflector.ResolveType(typeof(TagTestComponent).FullName!));
}
