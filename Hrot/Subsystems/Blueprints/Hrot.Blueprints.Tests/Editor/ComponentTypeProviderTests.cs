using Fdp.Core;
using Hrot.Blueprints.Editor.NodeDrawers;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// CA-02 (Slice 1a) — headless tests for <see cref="ReflectionComponentTypeProvider"/>: proves the
/// discovery predicate is <c>[ComponentId]</c> presence (the SAME marker
/// <see cref="ComponentTypeRegistry"/> requires on every ECS component type -- there is no separate
/// marker interface/base class), mirroring <c>ReflectionSharedStructTypeProvider</c>'s tests.
/// </summary>
public sealed class ComponentTypeProviderTests
{
    // A private nested [ComponentId]-marked test type is enough to prove the predicate: the scan
    // walks ALL loaded assemblies (including this test assembly), so it doesn't need a real engine
    // component -- but we ALSO check a real one below for end-to-end confidence.
    [ComponentId(0)]
    private struct MarkedTestComponent
    {
        public int Value;
    }

    private struct UnmarkedTestComponent
    {
        public int Value;
    }

    [Fact]
    public void GetComponentTypeFqns_FindsAttributeMarkedTestType()
    {
        var provider = new ReflectionComponentTypeProvider();

        var fqns = provider.GetComponentTypeFqns();

        Assert.Contains(typeof(MarkedTestComponent).FullName, fqns);
    }

    [Fact]
    public void GetComponentTypeFqns_ExcludesUnmarkedType()
    {
        var provider = new ReflectionComponentTypeProvider();

        var fqns = provider.GetComponentTypeFqns();

        Assert.DoesNotContain(typeof(UnmarkedTestComponent).FullName, fqns);
    }

    [Fact]
    public void GetComponentTypeFqns_FindsRealEngineComponent_SimTransform()
    {
        // Fdp.Core is a direct project reference of this test project (always loaded, unlike the
        // ReflectionSharedStructTypeProvider_FindsSquadRallyState test's flaky lazy-loaded
        // Hrot.AI.Behaviors dependency), so this is reliable end-to-end confidence that a real,
        // production [ComponentId]-marked component is discoverable.
        _ = typeof(Fdp.Core.SimTransform); // force the assembly to be considered loaded

        var provider = new ReflectionComponentTypeProvider();

        var fqns = provider.GetComponentTypeFqns();

        Assert.Contains("Fdp.Core.SimTransform", fqns);
    }

    [Fact]
    public void GetComponentTypeFqns_ResultIsSortedAndDistinct()
    {
        var provider = new ReflectionComponentTypeProvider();

        var fqns = provider.GetComponentTypeFqns();

        Assert.Equal(fqns.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal), fqns);
    }
}
