using System;
using System.Collections.Generic;
using Fdp.Kernel;
using FDP.Toolkit.Replication.Services;
using FDP.Toolkit.Replication.Systems;
using Hrot.Network.Replication;
using Hrot.Common.Systems;
using Hrot.Map.Common;
using Hrot.Common;
using Fdp.ModuleHost_Core.Abstractions;
using Fdp.ModuleHost_Core.Scheduling;
using Fdp.ModuleHost.Network.Cyclone.Systems;
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
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static NedReplicationModule BuildModule(NodeRole role)
    {
        var entityMap    = new NetworkEntityMap();
        var geoTransform = Hrot.Map.Common.HrotEnvironment.CreateGeoTransform();
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
        // DeadReckoning must NOT be registered for pure Muscle
        Assert.DoesNotContain(typeof(DeadReckoningSyncSystem), registry.RegisteredTypes);
    }

    // ── SC2 — ImageGenerator role ─────────────────────────────────────────────

    [Fact]
    public void ImageGenerator_RegistersDeadReckoningSystem()
    {
        var module   = BuildModule(NodeRole.ImageGenerator);
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
        var module   = BuildModule(NodeRole.MuscleGround | NodeRole.ImageGenerator);
        var registry = new CapturingRegistry();
        module.RegisterSystems(registry);

        Assert.Contains(typeof(GhostCreationSystem),     registry.RegisteredTypes);
        Assert.Contains(typeof(SmartEgressSystem),       registry.RegisteredTypes);
        Assert.Contains(typeof(DeadReckoningSyncSystem), registry.RegisteredTypes);

        // Confirm DriveFromNetwork = false (local entities must not be overridden by DR)
        Assert.False(module.DriveFromNetwork,
            "Combined Muscle+IG role must use driveFromNetwork=false.");
    }

    // ── SC4 — Invalid role throws ─────────────────────────────────────────────

    [Fact]
    public void InvalidRole_Throws_ArgumentException()
    {
        var entityMap    = new NetworkEntityMap();
        var geoTransform = Hrot.Map.Common.HrotEnvironment.CreateGeoTransform();
        var eventBus     = new FdpEventBus();

        Assert.Throws<ArgumentException>(() =>
            new NedReplicationModule(
                participant:  null,
                role:         NodeRole.Perception,   // not a replication role
                entityMap:    entityMap,
                geoTransform: geoTransform,
                eventBus:     eventBus,
                localNodeId:  1,
                domainId:     99));
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
        // DeadReckoning must NOT be registered for Brain-only
        Assert.DoesNotContain(typeof(DeadReckoningSyncSystem), registry.RegisteredTypes);
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
}
