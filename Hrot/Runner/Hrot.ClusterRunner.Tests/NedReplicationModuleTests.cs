using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Replication.Systems;
using Hrot.Network.Replication;
using Hrot.Common.Systems;
using Hrot.Map.Common;
using Hrot.Common;
using Fdp.ModuleHost.Abstractions;
using Fdp.ModuleHost.Scheduling;
using Fdp.Network.Cyclone.Systems;
using Xunit;

namespace Hrot.ClusterRunner.Tests;

/// <summary>
/// Unit tests for <see cref="NedReplicationModule"/>.
///
/// Tests use <see cref="CapturingRegistry"/> to assert which system types are registered
/// without actually executing the systems (headless, no DDS).
/// </summary>
public sealed class NedReplicationModuleTests
{
    // ── Spy registry ─────────────────────────────────────────────────────────

    /// <summary>
    /// Captures the runtime types of all registered systems, allowing role-based
    /// registration assertions without a live ECS world.
    /// </summary>
    private sealed class CapturingRegistry : ISystemRegistry
    {
        public List<Type> RegisteredTypes { get; } = new();

        public void RegisterSystem<T>(T system) where T : IEcsModuleSystem
            => RegisteredTypes.Add(system!.GetType());
        public IEcsModuleSystem RegisterManualSystem<T>(T system) where T : IEcsModuleSystem { RegisteredTypes.Add(system!.GetType()); return system; }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static NedReplicationModule BuildModule(NodeRole role)
    {
        var entityMap    = new NetworkEntityMap();
        var geoTransform = Hrot.Map.Common.HrotEnvironment.CreateGeoTransform(52.52, 13.405, 0.0);
        var eventBus     = new FdpEventBus();
        return new NedReplicationModule(
            participant:  null,         // headless — no DDS
            role:         role,
            entityMap:    entityMap,
            geoTransform: geoTransform,
            eventBus:     eventBus,
            localNodeId:  1,
            domainId:     99);
    }

    // ── SC1 — MuscleGround role ───────────────────────────────────────────────

    [Fact]
    public void MuscleGround_RegistersExpectedSystems()
    {
        var module   = BuildModule(NodeRole.MuscleGround);
        var registry = new CapturingRegistry();
        module.RegisterSystems(registry);

        Assert.Contains(typeof(GhostCreationSystem),         registry.RegisteredTypes);
        Assert.Contains(typeof(SmartEgressSystem),           registry.RegisteredTypes);
        Assert.Contains(typeof(CycloneNetworkCleanupSystem), registry.RegisteredTypes);
        Assert.Contains(typeof(DisposalMonitoringSystem),    registry.RegisteredTypes);
        // CE-211 — INVERTED, and this rail is the point of the change.
        // It used to assert that a pure Muscle node does NOT register dead reckoning. That encoded
        // the belief DR is an ImageGenerator feature. It is not: a Muscle node carries replicas of
        // every entity it does not own, and without DR their positions jump between packets — which
        // is what perception and the local grid then read. The narrowing that keeps DR off this
        // node's OWN entities is the driveFromNetwork flag below, not the absence of the system.
        // 📄 docs/DESIGN_Dead_Reckoning.md rule R1 (user ruling D1).
        Assert.Contains(typeof(DeadReckoningSyncSystem), registry.RegisteredTypes);
        // ...and because this node owns entities, DR is narrowed to ghosts rather than driving all.
        Assert.False(module.DriveFromNetwork);
    }

    // ── SC2 — ImageGenerator role ─────────────────────────────────────────────

    [Fact]
    public void ImageGenerator_RegistersDeadReckoningSystem()
    {
        var module   = BuildModule(NodeRole.Map2D);
        var registry = new CapturingRegistry();
        module.RegisterSystems(registry);

        Assert.Contains(typeof(GhostCreationSystem),      registry.RegisteredTypes);
        Assert.Contains(typeof(DeadReckoningSyncSystem),  registry.RegisteredTypes);
        // SmartEgress must NOT be registered for pure IG (no local ownership)
        Assert.DoesNotContain(typeof(SmartEgressSystem), registry.RegisteredTypes);
    }

    // ── SC3 — Combined role (MuscleGround | ImageGenerator) ────────────────

    [Fact]
    public void AllInOne_RegistersBothSmartEgressAndDeadReckoning_WithDriveFromNetworkFalse()
    {
        var module   = BuildModule(NodeRole.MuscleGround | NodeRole.Map2D);
        var registry = new CapturingRegistry();
        module.RegisterSystems(registry);

        Assert.Contains(typeof(GhostCreationSystem),     registry.RegisteredTypes);
        Assert.Contains(typeof(SmartEgressSystem),       registry.RegisteredTypes);
        Assert.Contains(typeof(DeadReckoningSyncSystem), registry.RegisteredTypes);

        // Confirm DriveFromNetwork = false (local entities must not be overridden by DR)
        Assert.False(module.DriveFromNetwork,
            "Combined Muscle+IG role must use driveFromNetwork=false.");
    }

    // ── SC4 — R-4057: EVERY role replicates (was: "Invalid role throws") ────────

    /// <summary>⭐ R-4057 (CE-4013 D14) — the module is built for any role, including one with none of the old
    /// MuscleGround / Map2D / Brain flags. 🔒 User, 2026-10-17: replication "shoukd be enabled by default" on any networked
    /// ECS node. ⛔ Was SC4 "InvalidRole_Throws_ArgumentException" (Perception rejected) — no design record backed it.</summary>
    [Theory]
    [InlineData(NodeRole.Perception)]
    [InlineData(NodeRole.NavigationSolver)]
    [InlineData(NodeRole.Traffic)]
    [InlineData(NodeRole.None)]
    public void AnyRole_BuildsTheModule_WithTheSharedSystems(NodeRole role)
    {
        var module   = BuildModule(role);
        var registry = new CapturingRegistry();
        module.RegisterSystems(registry);

        Assert.Contains(typeof(GhostCreationSystem),     registry.RegisteredTypes);
        Assert.Contains(typeof(DeadReckoningSyncSystem), registry.RegisteredTypes);
        Assert.DoesNotContain(typeof(SmartEgressSystem), registry.RegisteredTypes);   // ⭐ still role-gated inside (R-3015)
    }

    // ── SC5 — Brain role ──────────────────────────────────────────────────────

    [Fact]
    public void Brain_RegistersExpectedSystems()
    {
        var module   = BuildModule(NodeRole.Brain);
        var registry = new CapturingRegistry();
        module.RegisterSystems(registry);

        Assert.Contains(typeof(GhostCreationSystem), registry.RegisteredTypes);
        Assert.Contains(typeof(SmartEgressSystem),   registry.RegisteredTypes);
        // CE-211 — INVERTED for the same reason as the Muscle rail above: every node that carries
        // replicas owes dead reckoning, and a Brain node carries plenty. 📄 R1 / ruling D1.
        Assert.Contains(typeof(DeadReckoningSyncSystem), registry.RegisteredTypes);
        Assert.False(module.DriveFromNetwork);
    }

    // ── Corrective-0 — NetworkLifecycleSystemGroup exposed ────────────────────

    [Fact]
    public void NedReplicationModule_RegistersNetworkLifecycleSystemGroup()
    {
        var module = BuildModule(NodeRole.MuscleGround);

        // NetworkLifecycleGroup must be non-null (created in ctor with GhostCreationSystem)
        Assert.NotNull(module.NetworkLifecycleGroup);

        // Enabled by default — lifecycle gates are open during normal operation
        Assert.True(module.NetworkLifecycleGroup.Enabled,
            "NetworkLifecycleGroup.Enabled should default to true (gate open for normal operation).");
    }

    // ── CE-508 — the pre-genesis yield runs on EVERY NED host ──────────────────────

    /// <summary>
    /// ⭐⭐ <c>CE-508</c> — whichever node creates an entity and grants a group away must give up that group's claim,
    /// so the yield is registered whatever the role (it was pure-Brain only). 📄
    /// <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.6 S2. The system is a private nested type, so it is
    /// matched by name.
    /// </summary>
    [Theory]
    [InlineData(NodeRole.Brain)]
    [InlineData(NodeRole.MuscleGround)]
    [InlineData(NodeRole.Map2D)]
    [InlineData(NodeRole.Brain | NodeRole.MuscleGround)]
    public void EveryRoleRegistersTheLocalAuthorityYield(NodeRole role)
    {
        var registry = new CapturingRegistry();
        BuildModule(role).RegisterSystems(registry);

        Assert.Contains(registry.RegisteredTypes, t => t.Name == "LocalAuthorityYieldSystem");
    }

    // ── S5 — the record recompute runs on EVERY NED host, after the ownership ingress ─────────────────────

    /// <summary>
    /// ⭐ Ownership build S5 (R-159): every node keeps its record equal to its claim after every ownership change, so
    /// the recompute is registered whatever the role, and declared after the ingress it follows. 📄
    /// <c>docs/DESIGN_Ownership_Groups_And_Grants.md</c> §5.6 S5.
    /// </summary>
    [Theory]
    [InlineData(NodeRole.Brain)]
    [InlineData(NodeRole.MuscleGround)]
    [InlineData(NodeRole.Map2D)]
    [InlineData(NodeRole.Brain | NodeRole.MuscleGround)]
    public void EveryRoleRegistersTheRecordRecompute_AfterTheIngress(NodeRole role)
    {
        var registry = new CapturingRegistry();
        BuildModule(role).RegisterSystems(registry);

        Assert.Contains(typeof(Fdp.Toolkit.Replication.Systems.OwnershipRecomputeSystem), registry.RegisteredTypes);
        var after = (Fdp.Core.UpdateAfterAttribute?)System.Attribute.GetCustomAttribute(
            typeof(Fdp.Toolkit.Replication.Systems.OwnershipRecomputeSystem), typeof(Fdp.Core.UpdateAfterAttribute));
        Assert.Equal(typeof(Fdp.Toolkit.Replication.Systems.OwnershipIngressSystem), after?.Target);
    }

    // ── CE-1148 S1 — ONE ingress path on every node (R-3015) ─────────────────────────────────────────────────

    private sealed class InstanceRegistry : ISystemRegistry
    {
        public List<IEcsModuleSystem> Systems { get; } = new();
        public void RegisterSystem<T>(T system) where T : IEcsModuleSystem => Systems.Add(system!);
        public IEcsModuleSystem RegisterManualSystem<T>(T system) where T : IEcsModuleSystem { Systems.Add(system!); return system; }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1148</c> S1 (docs/DESIGN_Node_Roles_And_Policies.md §3.5, R-3015) — what a node RECEIVES never depends on its host kind:
    /// the IG (Map2D) polls the shared pack's ingress like every node — one reader per topic, never a second hand-kept list — so it gets
    /// the pairs the old IG list never carried (manual drive, weapon tasks, fire events) and the ownership yield (seam ⑥).
    /// </summary>
    [Theory]
    [InlineData(NodeRole.Map2D)]
    [InlineData(NodeRole.Brain)]
    [InlineData(NodeRole.MuscleGround)]
    [InlineData(NodeRole.Traffic)]   // ⭐ CE-3246 — the traffic node takes and sends hits through the same pair
    [InlineData(NodeRole.Brain | NodeRole.MuscleGround)]   // ⭐ CE-3246 — the editor's role: one pair, not one per role flag
    public void CE1148_EveryRole_PollsTheSharedIngress_OneReaderPerTopic(NodeRole role)
    {
        using var participant = new CycloneDDS.Runtime.DdsParticipant(0);
        var module = new NedReplicationModule(
            participant:  participant,
            role:         role,
            entityMap:    new NetworkEntityMap(),
            geoTransform: Hrot.Map.Common.HrotEnvironment.CreateGeoTransform(52.52, 13.405, 0.0),
            eventBus:     new FdpEventBus(),
            localNodeId:  1,
            domainId:     0);
        var registry = new InstanceRegistry();
        module.RegisterSystems(registry);

        var polled = new List<string>();
        foreach (var ingress in registry.Systems.OfType<Fdp.Network.Cyclone.Modules.CycloneNetworkIngressSystem>())
            foreach (var t in ingress.Translators) polled.Add(t.GetType().Name);

        Assert.Equal(1, polled.Count(n => n == "EntityMasterIngressTranslator"));
        Assert.Equal(1, polled.Count(n => n == "OwnershipUpdateTranslator"));
        foreach (var name in new[] { "EntityManualDriveIngressTranslator", "EntityWeaponTaskIngressTranslator",
                                     "EntityWeaponTaskStatusIngressTranslator", "FireInteractionEventTranslator", "GeoSpatialIngressTranslator" })
            Assert.Contains(name, polled);
        Assert.DoesNotContain(registry.Systems, sys => sys.GetType().Name == "EntityStatesIngressPack");

        // ⭐ CE-1148 S2 — the world events on every role, each pair exactly once (the ingress drops the node's own samples).
        var sent = new List<string>();
        foreach (var egress in registry.Systems.OfType<Fdp.Network.Cyclone.Systems.CycloneEgressSystem>())
            foreach (var t in egress.Translators) sent.Add(t.GetType().Name);
        foreach (var name in new[] { "WeaponFireIngressTranslator", "MunitionDetonationIngressTranslator", "RoundImpactIngressTranslator",
                                     "EntityHitDamageIngressTranslator" })   // ⭐ CE-3246 (BS-1 §6.4a)
            Assert.Equal(1, polled.Count(n => n == name));
        foreach (var name in new[] { "WeaponFireNotificationEgressTranslator", "MunitionDetonationEgressTranslator", "RoundImpactEgressTranslator",
                                     "DamageAssessedEgressTranslator" })
            Assert.Equal(1, sent.Count(n => n == name));
    }
}
