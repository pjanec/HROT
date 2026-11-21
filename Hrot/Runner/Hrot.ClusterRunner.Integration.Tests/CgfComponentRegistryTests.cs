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
    public void CgfComponentRegistry_RegisterAll_RegistersTheOccurrenceTiers()
    {
        using var world = new EntityRepository();
        CgfComponentRegistry.RegisterAll(world);

        // Cognitive tier marker: BrainBTreeState must be queryable.
        // ⛔ O7c-②: BrainBTreeState is retired. What a CGF node must now have for a BTree brain to
        //    run is the TIER ladder — the cursor is a slot inside it (§31).
        Assert.True(Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable
                        .Ascending[0].IsRegistered(world));
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE EXTRACTION RAIL — CGF's registered set is UNCHANGED by the Perception split.</b>
    ///
    /// <para>📄 <c>docs/DESIGN_Role_Affinity_Ownership.md</c> §3.9a. On <c>2026-09-12</c> the EQS set
    /// moved OUT of <c>CognitiveComponentRegistry</c> into <c>PerceptionRoleComponentRegistry</c>
    /// (<c>CE-259bf</c>). CGF obtained those components through the cognitive set, so it must now call
    /// the new registry — ⛔ and <b>this suite had NO EQS coverage at all</b>, so dropping that call
    /// would have gone uncaught here.</para>
    ///
    /// <para>⚠ The loss is SILENT: nothing throws at registration; the solver simply finds no sensors
    /// and every query returns empty, which surfaces as "perception does nothing" far from the cause.</para>
    /// </summary>
    [Fact]
    public void CgfComponentRegistry_RegisterAll_StillRegistersTheEqsSet_AfterTheExtraction()
    {
        using var world = new EntityRepository();
        CgfComponentRegistry.RegisterAll(world);

        Assert.Null(Record.Exception(() => world.GetComponentTable<Fdp.Toolkit.Spatial.Eqs.EqsSensor>()));
        Assert.Null(Record.Exception(() => world.GetComponentTable<Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer>()));
        Assert.Null(Record.Exception(() => world.GetComponentTable<Fdp.Toolkit.Spatial.Eqs.SensorEvalState>()));
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

/// <summary>

/// <summary>
