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

/// <summary>

/// <summary>
/// Rails for the OWNERSHIP-HANDOVER BUS CONTRACT — the events the split-authority handshake
/// publishes must be registered by the one Hrot-wide registry every node bootstrapper calls.
///
/// <para>
/// ⭐⭐⭐ These exist because the absence was invisible for as long as nobody ran a cluster.
/// 📐 Measured 2026-09-04 on a live <c>--mode all</c> run: <c>hill-attack</c> loaded (8 entities,
/// <c>OperatingLive</c>), 50 s of sim time elapsed, and <b>0 of 8 entities moved</b>. The run log
/// carried, every frame, <c>"Strict Mode Violation: Unmanaged event type 'OwnershipUpdate'
/// (ID: 9030) was published without being explicitly registered"</c> from
/// <c>DeferredTakeoverSystem.ExecuteTakeover</c>. After registering the two events the same run
/// reported <c>DeferredTakeover executed</c> ×8, zero violations, and <b>4 of 8 entities moved
/// 62-85 m</b>.
/// </para>
///
/// <para>
/// ⛔⛔ <b>THE FIRST VERSION OF THESE RAILS WAS VACUOUS AND IS WORTH RECORDING.</b> They published on
/// a bare <see cref="EntityRepository"/> and asserted no throw — and they stayed GREEN with both
/// registrations deleted. Cause: the guard is behind <c>FdpConfig.EnforceExplicitEventRegistration</c>,
/// which <b>defaults to false</b> and is turned on by production entry-points only. ⇒ a rail that does
/// not set it cannot see this defect at all. That is the design's §7 rail-blindness pattern, caught by
/// running the red-proof rather than assuming it.
/// </para>
///
/// <para>
/// ⚠ Each rail therefore enables strict mode explicitly and restores the previous value in a
/// <c>finally</c>, so it exercises the same guard production does without leaking global state into
/// its neighbours.
/// 📄 docs/HROT architecture.md §444, §508-512 · docs/DESIGN_Subsystem_Composition_Unification.md §4.1U.
/// </para>
/// </summary>
public class OwnershipHandoverEventRegistrationRails
{
    /// <summary>Runs <paramref name="body"/> with the production strict-mode guard ON.</summary>
    private static Exception? UnderStrictMode(Action body)
    {
        bool previous = Fdp.Core.FdpConfig.EnforceExplicitEventRegistration;
        Fdp.Core.FdpConfig.EnforceExplicitEventRegistration = true;
        try   { return Record.Exception(body); }
        finally { Fdp.Core.FdpConfig.EnforceExplicitEventRegistration = previous; }
    }

    /// <summary>
    /// Anti-vacuity guard for the two rails below: with strict mode ON, publishing an event the
    /// registry does NOT register must throw. Without this, a future change that silently disables
    /// the guard would turn both rails green and the defect would return unnoticed.
    /// </summary>
    [Fact]
    public void StrictModeReallyBites_AnUnregisteredEventThrows()
    {
        using var world = new EntityRepository();
        Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(world);

        var ex = UnderStrictMode(() => world.Bus.Publish(default(UnregisteredProbeEvent)));

        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("Strict Mode Violation", ex!.Message);
    }

    /// <summary>An event nothing registers, used only by the anti-vacuity guard above.</summary>
    [Fdp.Core.EventId(9987)]
    private struct UnregisteredProbeEvent { public int Unused; }

    /// <summary>
    /// <c>DeferredTakeoverSystem</c> and <c>OwnershipEgressSystem</c> both publish
    /// <c>OwnershipUpdate</c>; the shared registry must make that legal on every node.
    ///
    /// <para>⛔ INVERSE-EDIT RED-PROOF: delete the
    /// <c>RegisterEvent&lt;Replication.Messages.OwnershipUpdate&gt;()</c> line from
    /// <c>HrotSharedComponentRegistry.RegisterAll</c> and this rail fails with the exact
    /// Strict Mode Violation the live cluster produced.</para>
    /// </summary>
    [Fact]
    public void TheSharedRegistryMakesPublishingOwnershipUpdateLegal()
    {
        using var world = new EntityRepository();
        Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(world);

        var ex = UnderStrictMode(() =>
            world.Bus.Publish(new Fdp.Toolkit.Replication.Messages.OwnershipUpdate
            {
                NetworkId      = new Fdp.Toolkit.Replication.Components.NetworkIdentity(1000),
                PackedKey      = 0,
                NewOwnerNodeId = 1,
                OriginNodeId   = 400,
            }));

        Assert.Null(ex);
    }

    /// <summary>
    /// The sibling half, and the one that hid behind a NAME COLLISION: two distinct types are called
    /// <c>DescriptorAuthorityChanged</c> — <c>Replication.Components</c> (EventId 9010, registered,
    /// published by nothing) and <c>Replication.Messages</c> (EventId 9031, published by
    /// <c>OwnershipIngressSystem</c>, registered by nothing). The registry had the wrong namesake.
    ///
    /// <para>⛔ INVERSE-EDIT RED-PROOF: delete the
    /// <c>RegisterEvent&lt;Replication.Messages.DescriptorAuthorityChanged&gt;()</c> line and this
    /// rail fails — and the <c>Components</c> registration still sitting above it does NOT save it,
    /// which is exactly the point.</para>
    /// </summary>
    [Fact]
    public void TheSharedRegistryRegistersTheMessagesDescriptorAuthorityChanged_NotOnlyItsNamesake()
    {
        using var world = new EntityRepository();
        Hrot.Map.Common.HrotSharedComponentRegistry.RegisterAll(world);

        var ex = UnderStrictMode(() =>
            world.Bus.Publish(new Fdp.Toolkit.Replication.Messages.DescriptorAuthorityChanged
            {
                Entity          = default,
                PackedKey       = 0,
                IsAuthoritative = true,
            }));

        Assert.Null(ex);
    }
}
