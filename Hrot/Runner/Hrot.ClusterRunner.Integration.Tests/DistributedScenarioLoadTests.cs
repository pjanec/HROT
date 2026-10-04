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
using Hrot.Map.Common;
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

    // Offline staging IDs, given EXPLICITLY. ⛔ They used to be auto-allocated (1000/1001) — but the live cluster's
    // allocator also starts at 1000 (deterministic network ids), so offline and live ids coincided and the remap this
    // test exists to prove could not be observed. Ids outside the live range make the remap visible.
    private const long OfflineAttackerId = 5000L;
    private const long OfflineTargetId   = 5001L;

    private readonly string _scenarioId;

    public DistributedScenarioLoadTests()
    {
        _scenarioId = "test_dist_load_" + Guid.NewGuid().ToString("N");
    }

    public void Dispose() => NasScenarioStaging.Remove(_scenarioId);

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
            // ⭐⭐ QA-027 — the enum NAME, not its integer. TransitionPayloadDto's TargetState carries
            //    [JsonConverter(typeof(StrictStringEnumConverter))] and OrchestrationJsonOptions
            //    documents itself as rejecting integer enum values "to avoid silent integer-as-enum
            //    bugs". An int here deserialised to null ⇒ the adapter threw ⇒ ClusterMaster caught it
            //    into a Warn log ⇒ the cluster silently stayed at state 0.
            PayloadJson   = JsonSerializer.Serialize(
                new
                {
                    TargetState = nameof(Hrot.NED.Descriptors.Orchestration.ClusterState.OperatingLive),
                    ScenarioId  = _scenarioId,
                }),
        }).ConfigureAwait(false);

        // Pump until the cluster master reaches OperatingLive (state 31).
        // 4000 frames * 5 ms sleep = 20 s; well within the 90 s fact timeout.
        bool reachedLive = harness.PumpUntil(
            () => (int)master.CurrentClusterState == 31,
            timeoutFrames: 4000);

        Assert.True(reachedLive,
            $"Cluster must reach OperatingLive (31). Current: {(int)master.CurrentClusterState}.");

        // Extra frames to let entity creation requests propagate through the genesis pipeline
        // after the cluster state transition is committed.
        harness.PumpFrames(10);

        // ── Phase 3: Assertions ───────────────────────────────────────────────

        var cgfWorld = harness.Cgf!.World!;
        Assert.NotNull(cgfWorld);

        // Pump until the CGF world has exactly 2 UNITS (scenario entities loaded).
        // ⭐ CE-3036 (S3): each unit's TKB sensors are CHILD entities (PartMetadata) — counted out, not as units.
        bool entitiesLoaded = harness.PumpUntil(
            () => CountUnits(cgfWorld) == 2,
            timeoutFrames: 2000);

        Assert.True(entitiesLoaded,
            $"CGF world must contain exactly 2 units after scenario load. Actual: {CountUnits(cgfWorld)} " +
            $"({cgfWorld.EntityCount} entities with their part children).");

        // Find the attacker (has ActiveMissionPlan) and target entities.
        Entity attackerEntity = Entity.Null;
        Entity targetEntity   = Entity.Null;

        for (int i = 0; i <= cgfWorld.MaxEntityIndex; i++)
        {
            var e = cgfWorld.GetEntityByIndex(i);
            if (e == Entity.Null || !cgfWorld.IsAlive(e) || IsPart(cgfWorld, e)) continue;

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
        Assert.Equal("FireAtTarget", task.BehaviorName);
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

        // Spawn the attacker (offline ID 5000).
        harness.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType    = TkbEntityTypes.Tank_M1Abrams,   // ⛔ was 1L — not in the CLUSTER's TKB: CGF rejected both creates
            NetworkId  = OfflineAttackerId,
            OwnerNodeId = 0,
            InitType   = ReliableInitType.None,
        });

        // Spawn the target (offline ID 5001).
        harness.Bus.PublishManaged(new SpawnEntityCommand
        {
            TkbType    = TkbEntityTypes.Tank_M1Abrams,   // ⛔ was 1L — not in the CLUSTER's TKB: CGF rejected both creates
            NetworkId  = OfflineTargetId,
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
                        BehaviorName      = "FireAtTarget",
                        BehaviorParams  = behaviorParams,
                    },
                },
            },
        };

        harness.Repo.SetManagedComponent(attackerEntity, missionPlan);
        // ⭐ Its runtime queue too — production never has one without the other (MissionControlExecutionSystem adds the
        //   queue when it assigns a plan; BehaviorTkbTranslator at spawn), and MissionPlanTranslator saves them as a pair.
        if (!harness.Repo.HasComponent<MissionPlanQueue>(attackerEntity))
            harness.Repo.AddComponent(attackerEntity, new MissionPlanQueue());

        // Pump a couple of frames so the component assignment is flushed.
        harness.PumpFrames(2);

        // ⭐ Stage the authored scenario where the cluster loads it from. ⛔ Not harness.Editor.SaveScenarioAs: since the
        //   distributed save it writes no file — it publishes a storage request that only a cluster serves (NasScenarioStaging).
        NasScenarioStaging.Write(harness.FileService, harness.Repo, _scenarioId);
    }

    // ── Private DTO for assertion ─────────────────────────────────────────────

    private sealed class FireAtTargetParamsDto
    {
        [JsonPropertyName("targetNetworkId")]
        public long TargetNetworkId { get; set; }
    }

    private static bool IsPart(EntityRepository w, Entity e)
        => w.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.PartMetadata>()
           && w.HasComponent<Fdp.Toolkit.Replication.Components.PartMetadata>(e);

    private static int CountUnits(EntityRepository w)
    {
        int n = 0;
        for (int i = 0; i <= w.MaxEntityIndex; i++)
        {
            var e = w.GetEntityByIndex(i);
            if (e != Entity.Null && w.IsAlive(e) && !IsPart(w, e)) n++;
        }
        return n;
    }
}
