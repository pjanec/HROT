using Fdp.Core;
using Hrot.AI.Behaviors;
using Hrot.AI.Behaviors.Brains;
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

    // CA-05 (Slice 1b): a genuinely MANAGED (class) component -- distinct from
    // ManagedFieldTestComponent above (a STRUCT that merely contains a managed FIELD).
    private sealed class ManagedTestComponentClass
    {
        public int Health;
        public string? Label;
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

    // ── IsManagedComponent (CA-05, Slice 1b): component-LEVEL managed check ──────────────

    [Fact]
    public void IsManagedComponent_ClassComponent_True()
        => Assert.True(ComponentFieldReflector.IsManagedComponent(typeof(ManagedTestComponentClass).FullName!));

    [Fact]
    public void IsManagedComponent_UnmanagedStructComponent_False()
        => Assert.False(ComponentFieldReflector.IsManagedComponent(typeof(UnmanagedTestComponent).FullName!));

    [Fact]
    public void IsManagedComponent_StructWithManagedField_StillFalse()
        // A struct containing a reference-typed FIELD is still a value type -- IsManagedComponent
        // answers "is the component itself a class", not "does it contain a reference anywhere"
        // (that's ReflectedComponentField.IsManaged's job, per-field).
        => Assert.False(ComponentFieldReflector.IsManagedComponent(typeof(ManagedFieldTestComponent).FullName!));

    [Fact]
    public void IsManagedComponent_UnresolvableFqn_False()
        => Assert.False(ComponentFieldReflector.IsManagedComponent("Totally.Unknown.Namespace.NoSuchType"));

    [Fact]
    public void IsManagedComponent_NullOrEmptyFqn_False()
    {
        Assert.False(ComponentFieldReflector.IsManagedComponent(null));
        Assert.False(ComponentFieldReflector.IsManagedComponent(""));
    }

    // ── TryReflectCollections (CA-07a, R1 curated-accessor) ───────────────────

    // ── test-only accessor pairs for the malformed/mismatched-signature cases ────

    private struct LoneAccessorTestComponent
    {
        public int Value;
    }

    private static class LoneCountOnlyOps
    {
        // No matching [BlueprintCollectionItem] for "Items" -- a lone Count accessor declares NO
        // collection at all.
        [BlueprintCollection(typeof(LoneAccessorTestComponent), "Items")]
        public static int Count(in LoneAccessorTestComponent c) => 1;
    }

    private struct BadSignatureTestComponent
    {
        public int Value;
    }

    private static class BadSignatureOps
    {
        // Count's first parameter is NOT byref (not "in"/"ref") -- an invalid Count signature, so
        // even though a well-formed Item exists for the same Name, no collection is emitted.
        [BlueprintCollection(typeof(BadSignatureTestComponent), "Bad")]
        public static int Count(BadSignatureTestComponent c) => 1;

        [BlueprintCollectionItem(typeof(BadSignatureTestComponent), "Bad")]
        public static int Item(in BadSignatureTestComponent c, int i) => 0;
    }

    private struct VoidItemTestComponent
    {
        public int Value;
    }

    private static class VoidItemOps
    {
        [BlueprintCollection(typeof(VoidItemTestComponent), "Voidy")]
        public static int Count(in VoidItemTestComponent c) => 1;

        // Item returns void -- an invalid Item signature (element type would be meaningless), so no
        // collection is emitted even though Count is well-formed.
        [BlueprintCollectionItem(typeof(VoidItemTestComponent), "Voidy")]
        public static void Item(in VoidItemTestComponent c, int i) { }
    }

    [Fact]
    public void TryReflectCollections_BpCollectionDemo_DiscoversValuesCollection_WithCorrectMetadata()
    {
        var fqn = typeof(BpCollectionDemo).FullName!;

        var collections = ComponentFieldReflector.TryReflectCollections(fqn);

        var values = Assert.Single(collections, c => c.Name == "Values");
        Assert.Equal(typeof(int).FullName, values.ElementTypeId);
        Assert.Equal($"{typeof(BpCollectionDemoOps).FullName}.Count", values.CountAccessorFqn);
        Assert.Equal($"{typeof(BpCollectionDemoOps).FullName}.Item", values.ItemAccessorFqn);
    }

    [Fact]
    public void TryReflectCollections_LoneCountAccessor_NoMatchingItem_IsIgnored()
    {
        var fqn = typeof(LoneAccessorTestComponent).FullName!;

        var collections = ComponentFieldReflector.TryReflectCollections(fqn);

        Assert.Empty(collections);
    }

    [Fact]
    public void TryReflectCollections_CountAccessorNotByRef_IsIgnored()
    {
        var fqn = typeof(BadSignatureTestComponent).FullName!;

        var collections = ComponentFieldReflector.TryReflectCollections(fqn);

        Assert.Empty(collections);
    }

    [Fact]
    public void TryReflectCollections_ItemAccessorReturnsVoid_IsIgnored()
    {
        var fqn = typeof(VoidItemTestComponent).FullName!;

        var collections = ComponentFieldReflector.TryReflectCollections(fqn);

        Assert.Empty(collections);
    }

    [Fact]
    public void TryReflectCollections_UnresolvableOrEmptyFqn_ReturnsEmptyList()
    {
        Assert.Empty(ComponentFieldReflector.TryReflectCollections("Totally.Unknown.Namespace.NoSuchType"));
        Assert.Empty(ComponentFieldReflector.TryReflectCollections(null));
        Assert.Empty(ComponentFieldReflector.TryReflectCollections(""));
    }

    [Fact]
    public void TryReflectCollections_ComponentWithNoCollectionAccessors_ReturnsEmptyList()
        => Assert.Empty(ComponentFieldReflector.TryReflectCollections(typeof(UnmanagedTestComponent).FullName!));
}
