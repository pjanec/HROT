using System.Linq;
using Hrot.Blueprints.Editor.NodeDrawers;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// BP-62 — component type resolution must not depend on which assemblies happen to be loaded.
///
/// <para>
/// <c>ComponentFieldReflector.ResolveType</c> and <c>ComponentTypeScan</c> used to walk
/// <c>AppDomain.CurrentDomain.GetAssemblies()</c> directly. That returns only assemblies already
/// loaded, and the CLR loads lazily — so a component whose assembly nothing had touched simply did
/// not resolve, and callers read that <c>null</c> as "not a component" rather than "unknown".
/// <c>EditorTypeResolutionScope</c> now force-loads referenced assemblies before any scan.
/// </para>
/// </summary>
public sealed class ComponentTypeResolutionTests
{
    // A component that lives in Hrot.AI.Behaviors -- an assembly this test project references but
    // does not otherwise touch, which is precisely the case that used to fail.
    private const string WritableComponentFqn = "Hrot.AI.Behaviors.BpFixedListDemo";

    [Fact]
    public void ResolveType_FindsComponent_InAReferencedAssembly()
    {
        var type = ComponentFieldReflector.ResolveType(WritableComponentFqn);

        Assert.NotNull(type);
        Assert.Equal(WritableComponentFqn, type!.FullName);
    }

    // ---- the tri-state: the conflation BP-62 removed ---------------------

    [Fact]
    public void GetWritability_WritableComponent_IsWritable()
        => Assert.Equal(ComponentWritability.Writable,
                        ComponentFieldReflector.GetWritability(WritableComponentFqn));

    /// <summary>
    /// The crux of BP-62: an FQN that does not resolve must report <c>Unresolved</c>, NOT
    /// <c>NotWritable</c>. Collapsing the two into one <c>false</c> is what let the collection-write
    /// bake silently no-op on an unloaded assembly instead of reporting a broken reference.
    /// </summary>
    [Fact]
    public void GetWritability_UnresolvableFqn_IsUnresolved_NotNotWritable()
    {
        var result = ComponentFieldReflector.GetWritability("No.Such.Namespace.NoSuchComponent");

        Assert.Equal(ComponentWritability.Unresolved, result);
        Assert.NotEqual(ComponentWritability.NotWritable, result);
    }

    [Fact]
    public void GetWritability_NullOrEmpty_IsUnresolved()
    {
        Assert.Equal(ComponentWritability.Unresolved, ComponentFieldReflector.GetWritability(null));
        Assert.Equal(ComponentWritability.Unresolved, ComponentFieldReflector.GetWritability(""));
    }

    /// <summary>
    /// <c>IsWritableComponent</c> keeps its boolean contract for existing call sites: only
    /// <c>Writable</c> is true, so both <c>NotWritable</c> and <c>Unresolved</c> stay false.
    /// </summary>
    [Fact]
    public void IsWritableComponent_MatchesTriStateForWritableAndUnresolved()
    {
        Assert.True(ComponentFieldReflector.IsWritableComponent(WritableComponentFqn));
        Assert.False(ComponentFieldReflector.IsWritableComponent("No.Such.Namespace.NoSuchComponent"));
    }

    // ---- the picker path shares the same scope ---------------------------

    [Fact]
    public void ComponentTypePicker_DiscoversComponents_FromReferencedAssemblies()
    {
        var fqns = new ReflectionComponentTypeProvider().GetComponentTypeFqns();

        Assert.NotEmpty(fqns);
        Assert.Contains(WritableComponentFqn, fqns);
    }

    [Fact]
    public void WritableComponentTypePicker_IsASubsetOfAllComponents()
    {
        var all      = new ReflectionComponentTypeProvider().GetComponentTypeFqns().ToHashSet();
        var writable = new ReflectionWritableComponentTypeProvider().GetComponentTypeFqns();

        Assert.NotEmpty(writable);
        Assert.All(writable, w => Assert.Contains(w, all));
        Assert.Contains(WritableComponentFqn, writable);
    }

    [Fact]
    public void EnsureReferencedAssembliesLoaded_IsIdempotent()
    {
        EditorTypeResolutionScope.EnsureReferencedAssembliesLoaded();
        var first = EditorTypeResolutionScope.Assemblies().Count;

        EditorTypeResolutionScope.EnsureReferencedAssembliesLoaded();
        var second = EditorTypeResolutionScope.Assemblies().Count;

        Assert.Equal(first, second);
    }
}
