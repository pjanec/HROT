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

    // ── IT-SA-4 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <c>CE-2210</c> (Airborne Insertion §7d, D-15) — a man CREATED coming down, through the real request path (what
    /// <c>EjectionSystem</c> enqueues on the jet's Health owner): the Muscle that takes his position must hold his
    /// <c>Descent</c> and the jet's speed, and FLY him — his phase leaves the seat's arc there, and the Brain sees it.
    /// 📐 Measured before the fix: position and velocity crossed, the descent never did (its egress scanned Active entities
    /// only, and the creator owns the descent only while he is Constructing) ⇒ a Pedestrian at a standstill, for ever.
    /// 🔴 Red-proof: drop <c>WithLifecycle(All)</c> from <c>EntityDescentEgressTranslator</c> ⇒ no descent on the Muscle.
    /// <para>Domain 33 — explicit: a fourth shared-counter domain here would be 233, above CycloneDDS's 232.</para>
    /// </summary>
    [Fact]
    public void BrainCreatesAManComingDown_TheMuscleFliesHim()
    {
        bool previousStrictMode = FdpConfig.EnforceExplicitEventRegistration;
        FdpConfig.EnforceExplicitEventRegistration = true;
        try
        {
            using var simHost = new HrotRunnerHarness("simhost", 33);
            using var cgf     = new CgfHarness(33);
            cgf.PumpFrames(20);
            PumpBothFrames(simHost, cgf, 40);

            var cgfWorld = cgf.World!;
            cgf.CgfSvc.ScenarioEntityCreationSource!.Enqueue(new Hrot.Core.Network.EntityCreationRequest
            {
                RequestId          = Guid.NewGuid(),
                OwnerAppInstanceId = Hrot.Core.Network.EntityCreationRouting.DefaultEntityCreationRequestProcessor,
                TkbType            = TkbEntityTypes.Infantry_Pilot,
                InitialComponents  = new System.Collections.Generic.List<object>
                {
                    new SimTransform { Position = new System.Numerics.Vector3(100, 200, 300), Rotation = System.Numerics.Quaternion.Identity },
                    new SimVelocity  { Linear = new System.Numerics.Vector3(0, 150, 40) },
                    new Fdp.Toolkit.Motion.Descent
                    {
                        Kind = Fdp.Toolkit.Motion.DescentKind.Ejection, Phase = Fdp.Toolkit.Motion.DescentPhase.Exit,
                        Canopy = Fdp.Toolkit.Motion.CanopyKind.Round, PhaseSince = Fdp.Toolkit.Motion.DescentRules.Now(cgfWorld),
                    },
                },
            });

            float forwardSpeed = 0f;
            bool descending = PumpBothUntil(simHost, cgf, () =>
            {
                var w = simHost.SimHost.World;
                if (w == null || !w.IsComponentTypeRegistered<Fdp.Toolkit.Motion.Descent>()) return false;
                var pilot = PilotIn(w);
                if (pilot.IsNull || !w.HasComponent<Fdp.Toolkit.Motion.Descent>(pilot) || !w.HasComponent<SimVelocity>(pilot)) return false;
                forwardSpeed = w.GetComponentRO<SimVelocity>(pilot).Linear.Y;
                return w.GetComponentRO<Fdp.Toolkit.Motion.Descent>(pilot).Kind == Fdp.Toolkit.Motion.DescentKind.Ejection;
            }, PropagationTimeoutMs);
            Assert.True(descending, "the Muscle never received the pilot's Descent{Ejection} — he would be a Pedestrian there");
            Assert.True(forwardSpeed > 100f, $"the jet's forward speed must ride with him (got {forwardSpeed:0.#} m/s)");

            bool flown = PumpBothUntil(simHost, cgf, () =>
            {
                var w = simHost.SimHost.World!;
                var pilot = PilotIn(w);
                var brain = PilotIn(cgfWorld);
                return !pilot.IsNull && w.HasComponent<Fdp.Toolkit.Motion.Descent>(pilot)
                    && w.GetComponentRO<Fdp.Toolkit.Motion.Descent>(pilot).Phase != Fdp.Toolkit.Motion.DescentPhase.Exit
                    && !brain.IsNull && cgfWorld.HasComponent<Fdp.Toolkit.Motion.Descent>(brain)
                    && cgfWorld.GetComponentRO<Fdp.Toolkit.Motion.Descent>(brain).Phase != Fdp.Toolkit.Motion.DescentPhase.Exit;
            }, AuthorityTimeoutMs);
            Assert.True(flown, "the Muscle must fly him past the seat's arc, and the Brain must see his new phase");
        }
        finally { FdpConfig.EnforceExplicitEventRegistration = previousStrictMode; }
    }

    // ── IT-SA-5 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐ <c>CE-3216</c> S1-0 (R-3012, C-4; docs/designs/weather/DESIGN_Weather_And_Time.md §5) — the FIRST positionless replicated
    /// entity: a World Environment created on CGF through the real request path carries its <c>ScenarioClock</c> to SimHost with no
    /// <c>SimTransform</c> on either node; both derive UTC = start + their own sim time from the same start; and a runtime edit sent
    /// by the NON-owner (SimHost) is applied by the owner (CGF's <c>UpdateEntityAttributeRequestSystem</c>) as sent and replicates back
    /// (W-8: a new start time). 🔴 Red-proofs: drop the clock egress from <c>SharedTranslatorPack</c> ⇒ the clock never reaches SimHost;
    /// drop the JSON route in <c>AttributeCompilerFactory.Build</c> ⇒ the edit never lands on CGF.
    /// <para>Domain 35 — explicit, like IT-SA-4's 33 (the shared counter would pass CycloneDDS's 232).</para>
    /// </summary>
    [Fact]
    public void CE3216_ThePositionlessWorldEnvironment_CarriesItsClock_AndAnEditFromANonOwnerReachesEveryNode()
    {
        bool previousStrictMode = FdpConfig.EnforceExplicitEventRegistration;
        FdpConfig.EnforceExplicitEventRegistration = true;
        try
        {
            using var simHost = new HrotRunnerHarness("simhost", 35);
            using var cgf     = new CgfHarness(35);
            cgf.PumpFrames(20);
            PumpBothFrames(simHost, cgf, 40);

            var cgfWorld = cgf.World!;
            long start = new DateTime(2024, 3, 1, 4, 30, 0, DateTimeKind.Utc).Ticks;
            cgf.CgfSvc.ScenarioEntityCreationSource!.Enqueue(new Hrot.Core.Network.EntityCreationRequest
            {
                RequestId          = Guid.NewGuid(),
                OwnerAppInstanceId = Hrot.Core.Network.EntityCreationRouting.DefaultEntityCreationRequestProcessor,
                TkbType            = TkbEntityTypes.WorldEnvironment,
                InitialComponents  = new System.Collections.Generic.List<object>
                {
                    new Fdp.Toolkit.Weather.ScenarioClock { StartUtcTicks = start, DisplayUtcOffsetMinutes = 180 },
                },
            });

            // ① the clock arrives on SimHost — an entity with no position, on both nodes
            Assert.True(PumpBothUntil(simHost, cgf, () => simHost.SimHost.World is { } w && !EnvironmentIn(w, start).IsNull, PropagationTimeoutMs),
                "the World Environment's clock never reached SimHost — a positionless entity did not replicate");
            var simWorld = simHost.SimHost.World!;
            var simEnv = EnvironmentIn(simWorld, start);
            var cgfEnv = EnvironmentIn(cgfWorld, start);
            Assert.False(cgfEnv.IsNull, "the creator lost its own World Environment");
            Assert.False(simWorld.HasComponent<SimTransform>(simEnv), "the World Environment must stay positionless on SimHost");
            Assert.False(cgfWorld.HasComponent<SimTransform>(cgfEnv), "the World Environment must stay positionless on CGF");
            Assert.Equal((short)180, simWorld.GetComponentRO<Fdp.Toolkit.Weather.ScenarioClock>(simEnv).DisplayUtcOffsetMinutes);

            // ② both nodes derive UTC = start + their own sim time, from the SAME start (the cluster syncs the sim time)
            foreach (var w in new[] { cgfWorld, simWorld })
            {
                double now = w.GetSingleton<GlobalTime>().TotalTime;
                Assert.Equal(new DateTime(start, DateTimeKind.Utc).AddTicks((long)Math.Round(now * TimeSpan.TicksPerSecond)),
                    Fdp.Toolkit.Weather.ScenarioTime.Utc(w));
            }

            // ③ a runtime edit from the NON-owner: SimHost asks, CGF (the owner) applies it as sent, and the new start replicates back
            long newStart = new DateTime(2024, 3, 1, 22, 0, 0, DateTimeKind.Utc).Ticks;
            Assert.True(Fdp.Toolkit.Replication.Attributes.EntityAttributeChangeRequests.TryPublishJsonPatch(
                simWorld, simEnv, $"{{\"ScenarioStartUtcTicks\":{newStart}}}"), "SimHost could not address the clock's owner");
            Assert.True(PumpBothUntil(simHost, cgf, () =>
                    cgfWorld.GetComponentRO<Fdp.Toolkit.Weather.ScenarioClock>(cgfEnv).StartUtcTicks == newStart
                    && simWorld.GetComponentRO<Fdp.Toolkit.Weather.ScenarioClock>(simEnv).StartUtcTicks == newStart, AuthorityTimeoutMs),
                $"the edit did not round-trip: CGF {new DateTime(cgfWorld.GetComponentRO<Fdp.Toolkit.Weather.ScenarioClock>(cgfEnv).StartUtcTicks):O}, " +
                $"SimHost {new DateTime(simWorld.GetComponentRO<Fdp.Toolkit.Weather.ScenarioClock>(simEnv).StartUtcTicks):O}");
        }
        finally { FdpConfig.EnforceExplicitEventRegistration = previousStrictMode; }
    }

    /// <summary>The World Environment whose clock started at <paramref name="startTicks"/> (Null when none has it).</summary>
    private static Entity EnvironmentIn(EntityRepository w, long startTicks)
    {
        if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Weather.ScenarioClock>() || !w.IsComponentTypeRegistered<TkbIdentity>()) return Entity.Null;
        foreach (var e in w.Query().With<Fdp.Toolkit.Weather.ScenarioClock>().With<TkbIdentity>().WithLifecycle(EntityLifecycle.All).Build())
            if (w.GetComponentRO<TkbIdentity>(e).TkbType == TkbEntityTypes.WorldEnvironment
                && w.GetComponentRO<Fdp.Toolkit.Weather.ScenarioClock>(e).StartUtcTicks == startTicks) return e;
        return Entity.Null;
    }

    private static Entity PilotIn(EntityRepository w)
    {
        if (!w.IsComponentTypeRegistered<TkbIdentity>()) return Entity.Null;
        foreach (var e in w.Query().With<TkbIdentity>().WithLifecycle(EntityLifecycle.All).Build())
            if (w.GetComponentRO<TkbIdentity>(e).TkbType == TkbEntityTypes.Infantry_Pilot) return e;
        return Entity.Null;
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
