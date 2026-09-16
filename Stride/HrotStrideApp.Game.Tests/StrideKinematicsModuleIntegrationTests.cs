#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using CarKinem.Systems;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation.Systems;
using Hrot.Common.Systems;
using Hrot.Core.Network;
using Hrot.Stride.Core;
using HrotStrideApp;
using Xunit;

namespace HrotStrideApp.Tests;

/// <summary>
/// Integration tests for the P1 <see cref="StrideKinematicsModule"/> wiring inside
/// <see cref="EditorStrideSubsystem"/> (STR-P1-T1).
///
/// <para>
/// Tests run <b>headlessly</b> — no GPU, no Stride window, no DDS.
/// All assertions verify actual runtime types and values.
/// </para>
/// </summary>
public sealed class StrideKinematicsModuleIntegrationTests : IDisposable
{
    private readonly EditorStrideSubsystem _sut;

    public StrideKinematicsModuleIntegrationTests()
    {
        _sut = new EditorStrideSubsystem();
        _sut.Initialize();
    }

    public void Dispose() => _sut.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool ContainsType<T>(IReadOnlyList<IEcsModuleSystem> systems)
    {
        foreach (var s in systems)
            if (s is T) return true;
        return false;
    }

    private static bool ContainsFullNameFragment(
        IReadOnlyList<IEcsModuleSystem> systems, string fragment)
    {
        foreach (var s in systems)
            if (s.GetType().FullName?.Contains(fragment) == true) return true;
        return false;
    }

    private static bool AnyListContainsFullNameFragment(
        IReadOnlyList<IEcsModuleSystem> a,
        IReadOnlyList<IEcsModuleSystem> b,
        string fragment)
        => ContainsFullNameFragment(a, fragment) || ContainsFullNameFragment(b, fragment);

    // Spawn via the Brain path and pump 3 frames to materialise the entity.
    private void SpawnAndPump(long tkbType, SimTransform tf, SimVelocity vel)
    {
        _sut.ScenarioSource.Enqueue(new EntityCreationRequest
        {
            RequestId          = Guid.NewGuid(),
            OwnerAppInstanceId = 0,
            TkbType            = tkbType,
            InitialComponents  = new List<object> { tf, vel },
        });
        _sut.Tick(1f / 60f);
        _sut.Tick(1f / 60f);
        _sut.Tick(1f / 60f);
    }

    // ── T1-INT-1: KinematicsModule wired ────────────────────────────────────

    [Fact]
    public void KinematicsModule_IsWired_AfterInitialize()
    {
        Assert.NotNull(_sut.KinematicsModule);
    }

    [Fact]
    public void KinematicsModule_SimulationSystems_ContainsAllFiveKeptSystems()
    {
        var m = _sut.KinematicsModule!;
        Assert.True(ContainsType<SpatialHashSystem>(m.SimulationSystems),
            "SpatialHashSystem must be in SimulationSystems");
        Assert.True(ContainsType<FormationTargetSystem>(m.SimulationSystems),
            "FormationTargetSystem must be in SimulationSystems");
        Assert.True(ContainsType<VehicleCommandSystem>(m.SimulationSystems),
            "VehicleCommandSystem must be in SimulationSystems");
        Assert.True(ContainsType<NavigationExecutionSystem>(m.SimulationSystems),
            "NavigationExecutionSystem must be in SimulationSystems");
        Assert.True(ContainsType<CrowdAgentUpdateSystem>(m.SimulationSystems),
            "CrowdAgentUpdateSystem must be in SimulationSystems");
    }

    [Fact]
    public void KinematicsModule_ExcludesIntegratorSystems()
    {
        var m = _sut.KinematicsModule!;
        Assert.False(AnyListContainsFullNameFragment(
            m.SimulationSystems, m.PostSimulationSystems, "CarKinematicsSystem"),
            "CarKinematicsSystem must NOT be registered in any phase.");
        Assert.False(AnyListContainsFullNameFragment(
            m.SimulationSystems, m.PostSimulationSystems, "LinearKinematicsSystem"),
            "LinearKinematicsSystem must NOT be registered in any phase.");
    }

    [Fact]
    public void KinematicsModule_DeadReckoning_DriveFromNetworkIsFalse()
    {
        var m  = _sut.KinematicsModule!;
        DeadReckoningSyncSystem? dr = null;
        foreach (var s in m.PostSimulationSystems)
            if (s is DeadReckoningSyncSystem drs) { dr = drs; break; }

        Assert.NotNull(dr);
        Assert.False(dr!.DriveFromNetwork,
            "DeadReckoningSyncSystem must have DriveFromNetwork=false in editor_stride (§5.4).");
    }

    // ── T1-INT-2: Integrators-off via position invariant ─────────────────────

    /// <summary>
    /// <b>The key P1 integrator-off invariant:</b> an owned entity with a non-zero
    /// <c>SimVelocity.Linear</c> must NOT move its <c>SimTransform.Position</c>
    /// after several pump frames.
    ///
    /// <para>
    /// If <c>LinearKinematicsSystem</c> were still registered (P0 behaviour), velocity
    /// 10 m/s for 10 frames at 60 Hz would advance X by ~1.67 m. With
    /// <c>StrideKinematicsModule</c> the integrator is absent so X must be frozen.
    /// </para>
    /// </summary>
    [Fact]
    public void SpawnedOwnedEntity_WithNonZeroVelocity_PositionIsUnchanged_AfterPump()
    {
        var spawnPos = new Vector3(100f, 200f, 5f);
        SpawnAndPump(1001L,  // CivilianPedestrian
            new SimTransform { Position = spawnPos },
            new SimVelocity  { Linear   = new Vector3(10f, 0f, 0f) });

        Assert.Equal(1, _sut.World.EntityCount);

        var entity = _sut.World.Query().With<SimTransform>().Build().FirstOrNull();
        Assert.False(entity == Entity.Null,
            "Spawned entity must exist in the world.");

        var posBeforePump = _sut.World.GetComponent<SimTransform>(entity).Position;

        // Pump 10 more frames.  LinearKinematicsSystem would move X ~1.67m.
        for (int i = 0; i < 10; i++)
            _sut.Tick(1f / 60f);

        var posAfterPump = _sut.World.GetComponent<SimTransform>(entity).Position;

        // Position X must be unchanged — the FDP integrator is topologically gone.
        Assert.Equal(posBeforePump.X, posAfterPump.X, precision: 2);
        Assert.Equal(posBeforePump.Y, posAfterPump.Y, precision: 2);
    }

    // ── T1-INT-3: Components retained ────────────────────────────────────────

    [Fact]
    public void SpawnedEntity_StillHasSimTransformAndSimVelocity_AfterPump()
    {
        SpawnAndPump(2002L,  // InfantrySoldier
            new SimTransform(),
            new SimVelocity { Linear = new Vector3(5f, 0f, 0f) });

        Assert.Equal(1, _sut.World.EntityCount);
        var entity = _sut.World.Query().With<SimTransform>().Build().FirstOrNull();
        Assert.False(entity == Entity.Null);

        Assert.True(_sut.World.HasComponent<SimTransform>(entity),
            "Entity must still carry SimTransform after pump.");
        Assert.True(_sut.World.HasComponent<SimVelocity>(entity),
            "Entity must still carry SimVelocity after pump (topological exclusion only).");
    }

    // ── T1-INT-4: DeadReckoning does not smooth owned entities ───────────────

    [Fact]
    public void DeadReckoning_DoesNotMutate_OwnedEntityTransform()
    {
        SpawnAndPump(1001L,
            new SimTransform { Position = new Vector3(50f, 75f, 1f) },
            new SimVelocity  { Linear   = new Vector3(3f,  0f,  0f) });

        var entity    = _sut.World.Query().WithOwned<SimTransform>().Build().FirstOrNull();
        Assert.False(entity == Entity.Null,
            "Spawned entity must be WithOwned<SimTransform>.");

        var posBefore = _sut.World.GetComponent<SimTransform>(entity).Position;

        for (int i = 0; i < 5; i++)
            _sut.Tick(1f / 60f);

        var posAfter = _sut.World.GetComponent<SimTransform>(entity).Position;

        // DriveFromNetwork=false → owned entity is never dead-reckoned.
        Assert.Equal(posBefore.X, posAfter.X, precision: 3);
        Assert.Equal(posBefore.Y, posAfter.Y, precision: 3);
        Assert.Equal(posBefore.Z, posAfter.Z, precision: 3);
    }
}
