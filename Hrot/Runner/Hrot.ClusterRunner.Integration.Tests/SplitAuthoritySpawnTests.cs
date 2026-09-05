using System;
using System.Threading;
using Fdp.Core;
using Fdp.Toolkit.Replication.Components;
using Hrot.SimHost;
using Hrot.CGF;
using Hrot.Map.Common;
using Hrot.Core.Mission;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// SPLIT-AUTH-IT: Split-authority spawn integration tests.
///
/// <para>
/// Validates that when the CGF (Brain) node creates an entity and delegates
/// the WorldPos descriptor to the SimHost (Muscle) node via
/// <c>DeferredTakeOwnership</c>, the SimHost ghost receives
/// <c>SimTransform</c> / <c>NetworkTransform</c> / <c>NetworkVelocity</c>
/// authority flags after the entity transitions from Ghost → Constructing.
/// </para>
///
/// <para>Domain range: 230–239 (after DistributedBrainMuscleIntegrationTests 219–221).</para>
    /// </summary>
    public sealed class SplitAuthoritySpawnTests
    {
        private static int _domainCounter = 229;

    private const int PropagationTimeoutMs = 5_000;
    private const int AuthorityTimeoutMs   = 8_000;
    private const int PumpSleepMs          = 5;

    // ── IT-SA-1 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Brain spawns an entity with WorldPos delegated to Muscle.
    /// The entity should appear as a ghost on Muscle within the propagation window.
    /// </summary>
    [Fact]
    public void BrainSpawn_EntityArrives_OnMuscle()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var simHost = new HrotRunnerHarness("simhost", domainId);
        using var cgf     = new CgfHarness(domainId);

        cgf.PumpFrames(20);

        // SimHost node ID is 1 (SimHostNetworkConstants.LocalNodeId)
        long networkId = cgf.CgfSvc.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        bool arrived = PumpBothUntil(simHost, cgf,
            () =>
            {
                var map = simHost.SimHost.TestHook_EntityMap;
                return map.TryGetEntity(networkId, out _);
            },
            PropagationTimeoutMs);

        Assert.True(arrived,
            $"Entity {networkId} should appear on SimHost ghost map within {PropagationTimeoutMs} ms");
    }

    // ── IT-SA-2 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// After the entity arrives and is promoted on Muscle, the ghost should have
    /// <c>SimTransform</c> authority set to <c>true</c>.
    ///
    /// <para>
    /// The <c>DeferredTakeoverSystem</c> runs BeforeSync and processes
    /// <c>PendingAuthorityGrants</c> on Constructing entities by calling
    /// <c>SetAuthority(entity, componentId, true)</c> for each component
    /// registered in the <c>DescriptorOwnershipMap</c> for the WorldPos descriptor.
    /// </para>
    /// </summary>
    [Fact]
    public void BrainSpawn_WithWorldPosDelegation_MuscleTakesSimTransformAuthority()
    {
        // ⭐⭐⭐ RUN UNDER THE PRODUCTION GUARD. Hrot.ClusterRunner/Program.cs:52 sets this process-wide
        //    and NOTHING in this suite did, which is the whole reason the handover could be broken on
        //    every node while IT-SA-1..3 stayed green: the publish that throws in production is a
        //    silent no-op here. Same save/restore idiom as EditorSubsystemBootTests:224 and
        //    HrotNodeBuilderTests:105 — reused, not reinvented. 📄 §4.1U.
        bool previousStrictMode = FdpConfig.EnforceExplicitEventRegistration;
        FdpConfig.EnforceExplicitEventRegistration = true;
        try
        {

        int domainId = Interlocked.Increment(ref _domainCounter);
        using var simHost = new HrotRunnerHarness("simhost", domainId);
        using var cgf     = new CgfHarness(domainId);

        cgf.PumpFrames(20);

        // SimHost node ID is 1 (SimHostNetworkConstants.LocalNodeId)
        long networkId = cgf.CgfSvc.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        // Wait for SimTransform authority to become true on the Muscle side.
        bool hasAuthority = PumpBothUntil(simHost, cgf,
            () =>
            {
                var world = simHost.SimHost.World;
                var map   = simHost.SimHost.TestHook_EntityMap;
                if (world == null) return false;

                if (!map.TryGetEntity(networkId, out Entity entity)) return false;
                if (!world.IsAlive(entity)) return false;

                if (!world.HasComponent<SimTransform>(entity)) return false;

                return world.HasAuthority<SimTransform>(entity);
            },
            AuthorityTimeoutMs);

        Assert.True(hasAuthority,
            $"SimHost should have SimTransform authority for entity {networkId} " +
            $"within {AuthorityTimeoutMs} ms after split-authority spawn");

        // ⭐⭐⭐ ADDED 2026-09-04 — and the assertion above is NOT enough on its own.
        //
        // 📐 Measured: the handover was broken on EVERY node in production for as long as this suite
        //    has existed, and IT-SA-1..3 all stayed GREEN. Cause: ExecuteTakeover threw at step 2b
        //    (Bus.Publish(OwnershipUpdate) — the event was registered by no host), but step 2a
        //    (SetAuthority) had ALREADY run. ⇒ the assertion above tests exactly the half that still
        //    worked, and the two steps after the throw — SetManagedComponent(ownership) and
        //    RemoveManagedComponent<PendingAuthorityGrants> — never ran at all.
        //
        // ⭐⭐ The grant is the honest completion witness: it is attached by
        //    DeferredTakeOwnershipIngressTranslator and removed ONLY by the LAST line of
        //    ExecuteTakeover. So it is stripped if and only if the whole method completed.
        // ⛔ Not vacuous: `hasAuthority` above is true only because 2a ran, and 2a only runs FROM a
        //    grant — so the component provably existed before this waits for it to disappear.
        // 📌 On the live cluster before the fix it stayed attached and the system retried every frame
        //    forever, while the Brain kept its own authority bits and nothing moved.
        // ⛔ INVERSE-EDIT RED-PROOF: delete RegisterEvent<Replication.Messages.OwnershipUpdate>()
        //    from HrotSharedComponentRegistry.RegisterAll and THIS assertion fails while the one
        //    above it still passes.
        // 📄 docs/DESIGN_Subsystem_Composition_Unification.md §4.1U.
        bool grantStripped = PumpBothUntil(simHost, cgf,
            () =>
            {
                var world = simHost.SimHost.World;
                var map   = simHost.SimHost.TestHook_EntityMap;
                if (world == null) return false;
                if (!map.TryGetEntity(networkId, out Entity entity)) return false;
                if (!world.IsAlive(entity)) return false;
                return !world.HasManagedComponent<PendingAuthorityGrants>(entity);
            },
            AuthorityTimeoutMs);

        Assert.True(grantStripped,
            $"DeferredTakeoverSystem must strip PendingAuthorityGrants for entity {networkId} within " +
            $"{AuthorityTimeoutMs} ms — still attached means ExecuteTakeover never reached its last " +
            "line, so the Brain was never told to yield and the descriptor ownership was never recorded.");

        }
        finally { FdpConfig.EnforceExplicitEventRegistration = previousStrictMode; }
    }

    // ── IT-SA-3 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The CGF (Brain) ghost should NOT have SimTransform authority
    /// — that descriptor belongs to SimHost (Muscle).
    /// </summary>
    [Fact]
    public void BrainSpawn_Brain_DoesNotHaveSimTransformAuthority()
    {
        int domainId = Interlocked.Increment(ref _domainCounter);
        using var simHost = new HrotRunnerHarness("simhost", domainId);
        using var cgf     = new CgfHarness(domainId);

        cgf.PumpFrames(20);

        long networkId = cgf.CgfSvc.TestHook_SpawnEntityWithSplitAuthority(
            TkbEntityTypes.Tank_M1Abrams, muscleNodeId: 1);

        // First wait for the entity to arrive on Muscle so we know propagation completed.
        bool arrived = PumpBothUntil(simHost, cgf,
            () =>
            {
                var map = simHost.SimHost.TestHook_EntityMap;
                return map.TryGetEntity(networkId, out _);
            },
            PropagationTimeoutMs);

        Assert.True(arrived, "Entity must reach SimHost before checking Brain authority");

        // Wait until OwnershipUpdate returns from Muscle and Brain drops SimTransform authority.
        bool released = PumpBothUntil(simHost, cgf,
            () =>
            {
                var cgfWorld = cgf.World;
                var cgfMap   = cgf.CgfSvc.GhostEntityMap;
                if (cgfWorld == null || cgfMap == null) return true;

                if (!cgfMap.TryGetEntity(networkId, out Entity cgfEntity)) return true;
                if (!cgfWorld.IsAlive(cgfEntity)) return true;
                if (!cgfWorld.HasComponent<SimTransform>(cgfEntity)) return true;

                return !cgfWorld.HasAuthority<SimTransform>(cgfEntity);
            },
            AuthorityTimeoutMs);

        Assert.True(released,
            $"CGF Brain should release SimTransform authority for entity {networkId} " +
            $"within {AuthorityTimeoutMs} ms when WorldPos is delegated to Muscle");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool PumpBothUntil(
        HrotRunnerHarness simHost,
        CgfHarness        cgf,
        Func<bool>        condition,
        int               timeoutMs)
    {
        if (condition()) return true;
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            simHost.PumpFrames(1);
            cgf.PumpFrames(1);
            if (condition()) return true;
            Thread.Sleep(PumpSleepMs);
        }
        return false;
    }

    private static void PumpBothFrames(HrotRunnerHarness simHost, CgfHarness cgf, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            simHost.PumpFrames(1);
            cgf.PumpFrames(1);
            Thread.Sleep(PumpSleepMs);
        }
    }

}
