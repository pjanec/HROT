using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Hrot.CGF;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Unit tests for <see cref="CgfComponentRegistry"/> introduced by PACK3-C001.
///
/// <para>Verifies that all three registration tiers produce queryable component
/// tables without throwing, using a bare <see cref="EntityRepository"/>.</para>
/// </summary>
public class CgfComponentRegistryTests
{
    // ── Tier 1 (Foundation via HrotSharedComponentRegistry) ──────────────────

    [Fact]
    public void CgfComponentRegistry_RegisterAll_DoesNotThrow()
    {
        using var world = new EntityRepository();
        var ex = Record.Exception(() => CgfComponentRegistry.RegisterAll(world));
        Assert.Null(ex);
    }

    // ── Tier 2: Cognitive components ──────────────────────────────────────────

    [Fact]
    public void CgfComponentRegistry_RegisterAll_RegistersBrainBTreeState()
    {
        using var world = new EntityRepository();
        CgfComponentRegistry.RegisterAll(world);

        // Cognitive tier marker: BrainBTreeState must be queryable.
        Assert.Null(Record.Exception(() => world.GetComponentTable<BrainBTreeState>()));
    }

    // ── Tier 2: Kinematic components ──────────────────────────────────────────

    [Fact]
    public void CgfComponentRegistry_RegisterAll_RegistersVehicleState()
    {
        using var world = new EntityRepository();
        CgfComponentRegistry.RegisterAll(world);

        // Kinematic tier marker: VehicleState must be queryable.
        Assert.Null(Record.Exception(() => world.GetComponentTable<VehicleState>()));
        Assert.Null(Record.Exception(() => world.GetComponentTable<NavState>()));
        Assert.Null(Record.Exception(() => world.GetComponentTable<NavigationIntent>()));
    }

    // ── Tier 3: IG presentation components ───────────────────────────────────

    [Fact]
    public void CgfComponentRegistry_RegisterAll_RegistersEntityInfo()
    {
        using var world = new EntityRepository();
        CgfComponentRegistry.RegisterAll(world);

        // IG presentation tier marker: EntityInfo must be queryable.
        Assert.Null(Record.Exception(() => world.GetComponentTable<EntityInfo>()));
    }
}
