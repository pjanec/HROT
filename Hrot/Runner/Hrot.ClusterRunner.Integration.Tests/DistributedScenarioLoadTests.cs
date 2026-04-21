using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.NetworkSpawning;
using Fdp.Toolkit.NetworkSpawning.Events;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Replication;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// End-to-end integration test for distributed scenario loading (CGF1-S0603).
///
/// <para>
/// Proves that a scenario authored offline in <see cref="EditorHarness"/> can be
/// serialized to disk, loaded by a live cluster via the 2-Phase Commit orchestration
/// pipeline, and that cross-entity network references embedded in mission JSON are
/// patched to the new live network IDs.
/// </para>
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class DistributedScenarioLoadTests : IDisposable
{
    // Domain IDs: 231 is the next unused slot after NetworkGatewayIntegrationTests (230).
    private const int DomainBase = 231;
    private static int _domainSeq = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    // Offline staging IDs assigned by EditorHarness.SequentialIdAllocator (starts at 1000,
    // returns _next++ so first entity gets 1000, second gets 1001).
    private const long OfflineAttackerId = 1000L;
    private const long OfflineTargetId   = 1001L;

    private readonly string _scenarioId;
    private readonly string _stagingDir;

    public DistributedScenarioLoadTests()
    {
        _scenarioId = "test_dist_load_" + Guid.NewGuid().ToString("N");
        _stagingDir = Path.Combine(OrchestrationConstants.DefaultStagingDirectory, _scenarioId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_stagingDir))
        {
            try { Directory.Delete(_stagingDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    /// <summary>
    /// Saves a two-entity scenario (attacker with FireAtTarget mission plan targeting a
    /// second entity) via the offline EditorHarness, then loads it into a live distributed
    /// cluster and verifies that:
    /// <list type="bullet">
    ///   <item>The CGF world contains exactly 2 entities.</item>
    ///   <item>The attacker entity's <see cref="ActiveMissionPlan"/> has its
    ///         <c>targetNetworkId</c> remapped from the offline staging ID to the new
    ///         live network ID allocated by the CGF genesis pipeline.</item>
    /// </list>
    /// </summary>
    [Fact(Timeout = 90_000)]
    public async Task DistributedLoad_TranslatesNetworkIds_AndSpawnsEntitiesWithRemappedMissionPlan()
    {
        // ── Phase 1: Offline authoring ────────────────────────────────────────
        AuthorScenario();

        // ── Phase 2: Live cluster boot & injection ────────────────────────────
        int domainId = NextDomainId();
        using var harness = new HrotRunnerHarness("simhost,ig,excon,cgf", domainId);

        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;

        // Wait for at least one node to appear in the cluster roster before sending
        // the transition request.
        var rosterDeadline = DateTime.UtcNow.AddSeconds(10.0);
        while (master.NodeRoster.ActiveNodes.Count == 0 && DateTime.UtcNow < rosterDeadline)
        {
            harness.PumpFrames(1);
            Thread.Sleep(10);
        }

        Assert.True(master.NodeRoster.ActiveNodes.Count > 0,
            "At least one node must appear in the cluster roster before issuing TransitionState.");

        // Issue TransitionState -> OperatingLive (31) with the authored scenario ID.
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = JsonSerializer.Serialize(new { TargetState = 31, ScenarioId = _scenarioId }),
        }).ConfigureAwait(false);

        // Pump until the cluster master reaches OperatingLive (state 31).
        // 4000 frames * 5 ms sleep = 20 s; well within the 90 s fact timeout.
        bool reachedLive = harness.PumpUntil(
            () => (int)master.CurrentSystemState == 31,
            timeoutFrames: 4000);

        Assert.True(reachedLive,
            $"Cluster must reach OperatingLive (31). Current: {(int)master.CurrentSystemState}.");

        // Extra frames to let entity creation requests propagate through the genesis pipeline
        // after the cluster state transition is committed.
        harness.PumpFrames(10);

        // ── Phase 3: Assertions ───────────────────────────────────────────────

        var cgfWorld = harness.Cgf!.World!;
        Assert.NotNull(cgfWorld);

        // Pump until the CGF world has exactly 2 entities (scenario entities loaded).
        bool entitiesLoaded = harness.PumpUntil(
            () => cgfWorld.EntityCount == 2,
            timeoutFrames: 2000);

        Assert.True(entitiesLoaded,
            $"CGF world must contain exactly 2 entities after scenario load. Actual: {cgfWorld.EntityCount}.");

        // Find the attacker (has ActiveMissionPlan) and target entities.
        Entity attackerEntity = Entity.Null;
        Entity targetEntity   = Entity.Null;

        for (int i = 0; i <= cgfWorld.MaxEntityIndex; i++)
        {
            var e = cgfWorld.GetEntityByIndex(i);
            if (e == Entity.Null || !cgfWorld.IsAlive(e)) continue;

            if (cgfWorld.HasManagedComponent<ActiveMissionPlan>(e))
                attackerEntity = e;
            else
                targetEntity = e;
        }

        Assert.False(attackerEntity == Entity.Null,
            "Attacker entity with ActiveMissionPlan must exist in CGF world after scenario load.");
        Assert.False(targetEntity == Entity.Null,
            "Target entity must exist in CGF world after scenario load.");

        // Obtain new live network IDs from the CGF ghost entity map.
        var cgfMap = harness.Cgf.GhostEntityMap!;

        bool gotAttackerNetId = cgfMap.TryGetNetworkId(attackerEntity, out long newAttackerId);
        bool gotTargetNetId   = cgfMap.TryGetNetworkId(targetEntity,   out long newTargetId);

        Assert.True(gotAttackerNetId, "Attacker entity must be registered in CGF ghost entity map.");
        Assert.True(gotTargetNetId,   "Target entity must be registered in CGF ghost entity map.");

        // Verify that the live IDs differ from the offline staging IDs.
        Assert.NotEqual(OfflineAttackerId, newAttackerId);
        Assert.NotEqual(OfflineTargetId,   newTargetId);

        // Extract the ActiveMissionPlan and verify BehaviorParams remapping.
        int missionPlanTypeId = cgfWorld.GetComponentTypeId(typeof(ActiveMissionPlan));
        var plan = (ActiveMissionPlan)cgfWorld.GetManagedComponentByTypeId(attackerEntity, missionPlanTypeId);

        Assert.NotNull(plan);
        Assert.NotNull(plan.Plan);
        Assert.NotEmpty(plan.Plan.Tasks);

        var task = plan.Plan.Tasks[0];
        Assert.Equal("FireAtTarget", task.BehaviorId);
        Assert.False(string.IsNullOrWhiteSpace(task.BehaviorParams),
            "BehaviorParams must not be empty after scenario load.");

        var paramsDto = JsonSerializer.Deserialize<FireAtTargetParamsDto>(task.BehaviorParams!);
        Assert.NotNull(paramsDto);

        // Core success condition: the targetNetworkId in the mission plan must equal the
        // new live network ID of the target entity, NOT the offline staging ID.
        Assert.Equal(newTargetId, paramsDto!.TargetNetworkId);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Uses an offline <see cref="EditorHarness"/> to author and save a two-entity
    /// scenario.  The attacker entity (offline ID 1000) has a FireAtTarget mission
    /// plan that references the target entity's offline ID (1001).
    /// </summary>
    private void AuthorScenario()
    {
        using var harness = new EditorHarness();

        // Spawn the attacker (offline ID 1000 from SequentialIdAllocator that starts at 1000).
        harness.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType    = 1L,
            NetworkId  = 0,  // auto-allocated; first entity gets 1000
            OwnerNodeId = 0,
            InitType   = ReliableInitType.None,
        });

        // Spawn the target (offline ID 1001).
        harness.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType    = 1L,
            NetworkId  = 0,  // auto-allocated; second entity gets 1001
            OwnerNodeId = 0,
            InitType   = ReliableInitType.None,
        });

        Assert.True(
            harness.PumpUntil(() => harness.Repo.EntityCount == 2, timeoutMs: 5_000),
            "EditorHarness must spawn 2 entities within 5 s.");

        // Find the attacker entity via the network entity map using its offline ID.
        harness.EntityMap.TryGetEntity(OfflineAttackerId, out var attackerEntity);
        Assert.False(attackerEntity == Entity.Null,
            $"Attacker entity (offline ID {OfflineAttackerId}) must be registered in EditorHarness EntityMap.");

        // Build the ActiveMissionPlan with a FireAtTarget task referencing the target.
        var behaviorParams = JsonSerializer.Serialize(
            new { targetNetworkId = OfflineTargetId, maxRounds = 5, cooldownSeconds = 1.0 });

        var missionPlan = new ActiveMissionPlan
        {
            Plan = new DomainMissionPlan
            {
                ActiveTaskId = Guid.NewGuid(),
                Tasks =
                {
                    new DomainMissionTask
                    {
                        TaskId          = Guid.NewGuid(),
                        ExecutingEngine = "CGF",
                        BehaviorId      = "FireAtTarget",
                        BehaviorParams  = behaviorParams,
                    },
                },
            },
        };

        harness.Repo.SetManagedComponent(attackerEntity, missionPlan);

        // Pump a couple of frames so the component assignment is flushed.
        harness.PumpFrames(2);

        harness.Editor.SaveScenarioAs(_scenarioId);
    }

    // ── Private DTO for assertion ─────────────────────────────────────────────

    private sealed class FireAtTargetParamsDto
    {
        [JsonPropertyName("targetNetworkId")]
        public long TargetNetworkId { get; set; }
    }
}
