using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;
using Hrot.NED.Common;
using CoreGeoPoint = Hrot.Core.Mission.GeoPoint;
using Hrot.Map.Common;
using FDP.Toolkit.Replication.Components;
using Fdp.ModuleHost.Core.Abstractions;
using Xunit;
using DdsMissionTrigger = Hrot.NED.Descriptors.MissionTrigger;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// Integration tests that verify the end-to-end pathway for entity selection (Task 32A)
/// and mission commit acknowledgement (Task 32B).
///
/// <para>
/// <b>Task 32A root cause:</b> <c>IgApplication</c> never published
/// <c>SelectionChangedEvent</c> when the operator clicked on an entity; the ExCon
/// "Selection &amp; Mission" panel always showed "No selection" because
/// <c>ExConLogic.SelectedEntityId</c> was never updated.
/// <br/><b>Fix:</b> added <c>_selectionWriter</c> field to <c>IgApplication</c> and
/// publish <c>SelectionChangedEvent</c> from <c>OnCanvasClicked</c>.
/// </para>
///
/// <para>
/// <b>Task 32B root cause:</b> <c>MissionEditorService</c> was constructed without an
/// <c>ackQueue</c> and was absent from <c>ingressHandlers</c>, so
/// <c>CommitMissionAsync</c> always timed out after the 5 s default — the
/// <c>MissionControlAck</c> was read off DDS but never forwarded to the pending task.
/// <br/><b>Fix:</b> wired <c>missionAckQueue</c> and <c>MissionControlAckIngressHandler</c>
/// in <c>IosSubsystem.Initialize</c> and added the service to <c>ingressHandlers</c> so
/// <c>Poll()</c> is called every frame.
/// </para>
/// </summary>
public class SelectionAndMissionIntegrationTests
{
    private const int SpawnTimeoutFrames    = 150;
    private const int SelectionTimeoutFrames = 100;
    private const int CommitTimeoutFrames   = 200;

    private static readonly CoreGeoPoint BerlinGeo = new CoreGeoPoint
    {
        Latitude  = 52.521,
        Longitude = 13.406,
        Altitude  = 0,
    };

    // ── Task 32A ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that when the IG operator clicks directly on a network entity, IG
    /// publishes a <c>SelectionChangedEvent</c> via DDS, which ExCon receives and uses to
    /// update <see cref="Hrot.ExCon.ExConLogic.SelectedEntityId"/>.
    ///
    /// <para>
    /// Before the fix, <c>IgApplication</c> had no <c>_selectionWriter</c> so
    /// <c>SelectionChangedEvent</c> was never written to DDS and
    /// <c>SelectedEntityId</c> stayed 0 forever.
    /// </para>
    /// </summary>
    [Fact]
    public void IG_EntityClick_IosReceivesSelectionAndUpdatesSelectedEntityId()
    {
        using var harness = new HrotRunnerHarness();

        long networkId = harness.SimHost.TestHook_SpawnEntity(TkbEntityTypes.Tank_M1Abrams, BerlinGeo);

        // Wait for IG to receive the entity over DDS.
        bool igReady = harness.PumpUntil(
            () => IgHasNetworkEntity(harness, networkId),
            SpawnTimeoutFrames);
        Assert.True(igReady,
            $"IG entity networkId={networkId} did not appear within {SpawnTimeoutFrames} frames.");

        // Simulate the operator clicking on the entity in the IG map view.
        // After the Task 32A fix this calls OnCanvasClicked with the entity as the hit,
        // which publishes SelectionChangedEvent { SelectedEntityIds = [(int)networkId] }.
        harness.Ig.App.TestHook_SimulateEntityClick(networkId);

        // ExCon must receive the DDS event and update SelectedEntityId within a few frames.
        bool selected = harness.PumpUntil(
            () => harness.ExCon.Logic.SelectedEntityId == (int)networkId,
            SelectionTimeoutFrames);

        Assert.True(selected,
            $"ExCon SelectedEntityId was not updated to {networkId} within {SelectionTimeoutFrames} frames. " +
            $"Current value: {harness.ExCon.Logic.SelectedEntityId}. " +
            $"Check that _selectionWriter is present in IgApplication (Task 32A fix) and " +
            $"SelectionChangedIngressHandler is polling correctly.");
    }

    // ── Task 32B ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that <see cref="Hrot.ExCon.Services.IMissionEditorService.CommitMissionAsync"/>
    /// resolves successfully, receiving a <c>MissionControlAck</c> from SimHost within
    /// <see cref="CommitTimeoutFrames"/> frames — which is far less than the 5 s wall-clock
    /// timeout the old broken wiring would always hit.
    /// </summary>
    [Fact]
    public async Task ExCon_CommitMissionAsync_ResolvesWithAck_NotTimeout()
    {
        using var harness = new HrotRunnerHarness();

        long networkId = harness.SimHost.TestHook_SpawnEntity(TkbEntityTypes.Tank_M1Abrams, BerlinGeo);

        // Wait for the entity to be live on SimHost so MissionControlRequestSystem can handle it.
        bool simHostReady = harness.PumpUntil(
            () => SimHostHasEntity(harness, networkId),
            SpawnTimeoutFrames);
        Assert.True(simHostReady,
            $"SimHost entity networkId={networkId} was not ready within {SpawnTimeoutFrames} frames.");

        // Build a minimal single-task WanderMilitary mission plan — the same shape used by
        // MiniExConPanelState.SubmitWithWanderMissionViaGateway.
        var taskId = Guid.NewGuid();
        var plan = new Hrot.Core.Mission.MissionPlan
        {
            ActiveTaskId = taskId,
            Tasks = new List<Hrot.Core.Mission.MissionTask>
            {
                new Hrot.Core.Mission.MissionTask
                {
                    TaskId          = taskId,
                    ExecutingEngine = "CGFX",
                    BehaviorId      = "WanderMilitary",
                    BehaviorParams  = string.Empty,
                    State           = Hrot.Core.Mission.eTaskState.TASK_PLANNED,
                    Triggers        = new List<Hrot.Core.Mission.MissionTrigger>(),
                }
            }
        };

        // Kick off the async commit; this publishes MissionControlRequest to DDS.
        var commitTask =
            harness.ExCon.Logic.MissionEditorService.CommitMissionAsync(networkId, plan, 0);

        // Pump frames: each frame runs IosSubsystem.Update() →  ExConLogic.Update() →
        // ingress handlers Poll() (including MissionEditorService.Poll()) which drains
        // the ack queue and resolves the pending TaskCompletionSource.
        bool completed = harness.PumpUntil(() => commitTask.IsCompleted, CommitTimeoutFrames);

        Assert.True(completed,
            $"CommitMissionAsync did not complete within {CommitTimeoutFrames} frames. " +
            $"MissionControlAck was not received — verify that missionAckQueue is wired to " +
            $"MissionControlAckIngressHandler and MissionEditorService is in ingressHandlers " +
            $"(Task 32B fix in IosSubsystem.Initialize).");

        var result = await commitTask;
        Assert.True(result.Success,
            $"CommitMissionAsync completed but returned failure: " +
            $"ErrorCode={result.ErrorCode}  ErrorMessage={result.ErrorMessage}");
        Assert.True(result.NewVersion > 0,
            $"Successful commit should return NewVersion > 0, got {result.NewVersion}.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool IgHasNetworkEntity(HrotRunnerHarness harness, long networkId)
    {
        var entityMap = harness.Ig.App.TestHook_EntityMap;
        if (!entityMap.TryGetEntity(networkId, out var entity))
            return false;
        return harness.Ig.App.World.IsAlive(entity);
    }

    private static bool SimHostHasEntity(HrotRunnerHarness harness, long networkId)
    {
        var view  = (ISimulationView)harness.SimHost.World!;
        var query = harness.SimHost.World!
            .Query()
            .IncludeAll()
            .With<NetworkIdentity>()
            .Build();

        foreach (var entity in query)
        {
            var id = view.GetComponentRO<NetworkIdentity>(entity);
            if (id.Value == networkId)
                return true;
        }

        return false;
    }
}
