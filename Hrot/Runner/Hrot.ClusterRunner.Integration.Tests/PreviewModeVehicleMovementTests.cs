using System;
using System.Numerics;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior.Components;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Preview mode vehicle movement regression test.
/// Proves that <see cref="SimulationGroupModule"/> executes simulation systems
/// (specifically <c>LinearKinematicsSystem</c>) during preview mode and that the
/// <see cref="ActiveMissionPlan"/> transient component survives the preview snapshot.
/// </summary>
[Collection("EditorOfflineTests")]
public sealed class PreviewModeVehicleMovementTests
{
    private const int   PumpTimeoutMs    = 5_000;
    private const float MovementThreshold = 0.01f;

    /// <summary>
    /// Regression test: a vehicle (entity) with a valid mission plan and a non-zero
    /// <see cref="SimVelocity"/> must start moving once preview mode is entered.
    ///
    /// Before the fix, <c>SimulationGroupAdapter</c> was registered as a global system
    /// in <c>SystemPhase.BeforeSync</c>.  Because the kernel only executes global phases
    /// {Input, BeforeSync, PostSimulation, Export} via <c>ExecutePhase</c> and dispatches
    /// modules separately, simulation systems (LinearKinematicsSystem, CarKinematicsSystem,
    /// BTreeTickSystem, ...) never executed -- so the vehicle stood still.
    ///
    /// After the fix, <c>SimulationGroupModule</c> is registered as an
    /// <see cref="IEcsModule"/> and its <c>Tick</c> method is called by the module
    /// dispatch pipeline, causing all simulation systems to execute correctly.
    /// </summary>
    [Fact]
    public void VehicleWithActiveMission_StartsMoving_WhenPreviewModeEntered()
    {
        using var harness = new EditorHarness();
        var world = harness.Repo;
        var view  = (ISimulationView)world;

        // Create a minimal entity that LinearKinematicsSystem will process:
        // SimTransform + SimVelocity (non-zero Linear) + no VehicleState.
        var vehicle = world.CreateEntity();
        world.AddComponent(vehicle, new SimTransform { Position = Vector3.Zero });
        world.AddComponent(vehicle, new SimVelocity  { Linear   = new Vector3(5f, 0f, 0f) });

        // Attach an ActiveMissionPlan (transient managed component).
        // The preview snapshot must capture it because PreviewClusterOpHandler uses
        // includeTransient: true in LoadingPreviewCommit.
        world.SetManagedComponent(vehicle, new ActiveMissionPlan
        {
            Plan = new DomainMissionPlan
            {
                Tasks = new List<DomainMissionTask>
                {
                    new DomainMissionTask
                    {
                        TaskId         = Guid.NewGuid(),
                        BehaviorId     = "MoveToLocation",
                        BehaviorParams = "{\"x\":500,\"y\":500,\"Speed\":15}",
                    }
                }
            }
        });

        // Enter preview mode.  This triggers the snapshot (SyncFrom with
        // includeTransient: true) and switches the time controller to Continuous
        // so the kernel runs freely.
        harness.Preview.EnterPreviewMode();
        Assert.True(harness.Preview.IsInPreviewMode);

        // The transient ActiveMissionPlan must survive the snapshot taken on entry.
        Assert.True(world.HasManagedComponent<ActiveMissionPlan>(vehicle),
            "ActiveMissionPlan was lost when entering preview mode. " +
            "Verify that PreviewClusterOpHandler.LoadingPreviewCommit uses " +
            "includeTransient: true in its SyncFrom call.");

        var plan = view.GetManagedComponentRO<ActiveMissionPlan>(vehicle);
        Assert.NotNull(plan?.Plan);

        // Capture position AFTER entering preview (snapshot taken, kernel now running).
        var initialPos = world.GetComponentRO<SimTransform>(vehicle).Position;

        // Pump frames until the vehicle moves, or timeout.
        // LinearKinematicsSystem integrates Position += Linear * dt each frame.
        // With Linear.X = 5 m/s and dt = 5 ms, after 60 frames X ~= 1.5 m >> threshold.
        bool moved = harness.PumpUntil(
            () =>
            {
                ref readonly var tf = ref world.GetComponentRO<SimTransform>(vehicle);
                return (tf.Position - initialPos).Length() > MovementThreshold;
            },
            PumpTimeoutMs);

        Assert.True(moved,
            "Vehicle did not move during preview mode. " +
            "Verify that SimulationGroupModule is registered via Kernel.RegisterModule() " +
            "so that LinearKinematicsSystem executes in the Simulation dispatch pipeline.");
    }
}
