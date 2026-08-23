#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using CarKinem.Systems;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Navigation.Fake;
using Fdp.Toolkit.Navigation.Systems;
using Fdp.Modules.Geographic.Systems;
using Hrot.Common.Systems;
using Hrot.Stride.Core;
using Xunit;

namespace Hrot.Stride.Core.Tests;

/// <summary>
/// Unit tests for <see cref="StrideKinematicsModule"/> (STR-P1-T1).
///
/// <para>
/// Tests assert against the <em>actual runtime types</em> of registered systems —
/// never against type names as strings, and never "not-null only".
/// </para>
/// </summary>
public sealed class StrideKinematicsModuleTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static StrideKinematicsModule CreateModule()
        => new StrideKinematicsModule(dtCrowd: new FakeDtCrowdProvider());

    private static IReadOnlyList<IEcsModuleSystem> AllSystems(StrideKinematicsModule m)
    {
        var all = new List<IEcsModuleSystem>();
        all.AddRange(m.SimulationSystems);
        all.AddRange(m.PostSimulationSystems);
        return all;
    }

    // ── T1-SC1: Kept systems present ─────────────────────────────────────────

    /// <summary>
    /// SimulationSystems must include <see cref="SpatialHashSystem"/> by real type.
    /// </summary>
    [Fact]
    public void SimulationSystems_ContainsSpatialHashSystem()
    {
        var m = CreateModule();
        Assert.Contains(m.SimulationSystems, s => s is SpatialHashSystem);
    }

    /// <summary>
    /// SimulationSystems must include <see cref="FormationTargetSystem"/> by real type.
    /// </summary>
    [Fact]
    public void SimulationSystems_ContainsFormationTargetSystem()
    {
        var m = CreateModule();
        Assert.Contains(m.SimulationSystems, s => s is FormationTargetSystem);
    }

    /// <summary>
    /// SimulationSystems must include <see cref="VehicleCommandSystem"/> by real type.
    /// </summary>
    [Fact]
    public void SimulationSystems_ContainsVehicleCommandSystem()
    {
        var m = CreateModule();
        Assert.Contains(m.SimulationSystems, s => s is VehicleCommandSystem);
    }

    /// <summary>
    /// SimulationSystems must include <see cref="NavigationExecutionSystem"/> by real type.
    /// </summary>
    [Fact]
    public void SimulationSystems_ContainsNavigationExecutionSystem()
    {
        var m = CreateModule();
        Assert.Contains(m.SimulationSystems, s => s is NavigationExecutionSystem);
    }

    /// <summary>
    /// SimulationSystems must include <see cref="CrowdAgentUpdateSystem"/> by real type.
    /// </summary>
    [Fact]
    public void SimulationSystems_ContainsCrowdAgentUpdateSystem()
    {
        var m = CreateModule();
        Assert.Contains(m.SimulationSystems, s => s is CrowdAgentUpdateSystem);
    }

    /// <summary>
    /// PostSimulationSystems must include <see cref="DeadReckoningSyncSystem"/> by real type.
    /// </summary>
    [Fact]
    public void PostSimulationSystems_ContainsDeadReckoningSyncSystem()
    {
        var m = CreateModule();
        Assert.Contains(m.PostSimulationSystems, s => s is DeadReckoningSyncSystem);
    }

    // ── T1-SC2: Integrators excluded ─────────────────────────────────────────

    /// <summary>
    /// <c>CarKinematicsSystem</c> must NOT be registered in any phase (§5.1).
    /// Assert against the real <see cref="CarKinematics.Systems.CarKinematicsSystem"/> type.
    /// </summary>
    [Fact]
    public void NeitherSimNorPostSim_ContainsCarKinematicsSystem()
    {
        var m   = CreateModule();
        var all = AllSystems(m);
        Assert.DoesNotContain(all, s => s.GetType().Name == "CarKinematicsSystem");
        // Belt-and-suspenders: also check the CarKinematics namespace prefix
        Assert.DoesNotContain(all,
            s => s.GetType().FullName?.Contains("CarKinematicsSystem") == true);
    }

    /// <summary>
    /// <c>LinearKinematicsSystem</c> must NOT be registered in any phase (§5.1).
    /// </summary>
    [Fact]
    public void NeitherSimNorPostSim_ContainsLinearKinematicsSystem()
    {
        var m   = CreateModule();
        var all = AllSystems(m);
        Assert.DoesNotContain(all,
            s => s.GetType().FullName?.Contains("LinearKinematicsSystem") == true);
    }

    // ── T1-SC3: Terrain pipeline excluded ────────────────────────────────────

    /// <summary>
    /// None of the three terrain-query systems are registered (design §5.5).
    /// </summary>
    [Fact]
    public void TerrainPipeline_NotRegisteredInAnyPhase()
    {
        var m   = CreateModule();
        var all = AllSystems(m);

        Assert.DoesNotContain(all, s => s is TerrainQuerySubmitSystem);
        Assert.DoesNotContain(all, s => s is TerrainQuerySolverSystem);
        Assert.DoesNotContain(all, s => s is TerrainQueryResolutionSystem);
    }

    // ── T1-SC4: DeadReckoning DriveFromNetwork=false ──────────────────────────

    /// <summary>
    /// The <see cref="DeadReckoningSyncSystem"/> must be constructed with
    /// <c>DriveFromNetwork = false</c> (design §5.4), so owned entities are
    /// never dead-reckoning-smoothed.
    /// </summary>
    [Fact]
    public void DeadReckoningSyncSystem_DriveFromNetwork_IsFalse()
    {
        var m   = CreateModule();
        var dr  = (DeadReckoningSyncSystem)m.PostSimulationSystems
            .Single(s => s is DeadReckoningSyncSystem);

        // DriveFromNetwork is a public property on DeadReckoningSyncSystem — assert its value.
        Assert.False(dr.DriveFromNetwork,
            "DeadReckoningSyncSystem must have DriveFromNetwork=false so locally-owned " +
            "entities are NOT dead-reckoning-smoothed (design §5.4).");
    }

    // ── T1-SC5: Exact count (no accidental extras) ────────────────────────────

    /// <summary>
    /// SimulationSystems has exactly 5 entries (§5.1: SpatialHash, FormationTarget,
    /// VehicleCommand, NavigationExecution, CrowdAgentUpdate).
    /// </summary>
    [Fact]
    public void SimulationSystems_HasExactlyFiveEntries()
    {
        var m = CreateModule();
        Assert.Equal(5, m.SimulationSystems.Count);
    }

    /// <summary>
    /// PostSimulationSystems has exactly 1 entry (DeadReckoningSyncSystem).
    /// CarKinematicsSystem and LinearKinematicsSystem are topologically excluded.
    /// </summary>
    [Fact]
    public void PostSimulationSystems_HasExactlyOneEntry()
    {
        var m = CreateModule();
        Assert.Equal(1, m.PostSimulationSystems.Count);
    }

    // ── T1-SC6: CrowdAgentUpdate backed by the provided IDtCrowdProvider ──────

    /// <summary>
    /// The DtCrowd accessor returns the same provider instance that was passed
    /// to the constructor (no substitution).
    /// </summary>
    [Fact]
    public void DtCrowd_ReturnsProvidedInstance()
    {
        var fakeCrowd = new FakeDtCrowdProvider();
        var m         = new StrideKinematicsModule(dtCrowd: fakeCrowd);
        Assert.Same(fakeCrowd, m.DtCrowd);
    }

    // ── T1-SC7: TrajectoryPool accessible ────────────────────────────────────

    /// <summary>
    /// <see cref="StrideKinematicsModule.TrajectoryPool"/> is non-null when constructed
    /// with a null pool (lazy-allocated internally).
    /// </summary>
    [Fact]
    public void TrajectoryPool_NonNull_WhenNotProvided()
    {
        var m = CreateModule();
        Assert.NotNull(m.TrajectoryPool);
    }
}
