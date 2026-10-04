using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐ <c>CE-2082</c> — the shipped DEMO SOP scenario (<c>Hrot.AI.Behaviors/Recipes/Scenarios/sop-demo</c>) loads into a live
/// cluster and shows the SOP end to end: an idle unit runs its SOP's idle choice; a squad on a move order that is shot at
/// takes cover (a reaction that PAUSES the move); a squad whose ROE says "stay on task" is shot at and keeps moving.
/// 📄 <c>docs/DESIGN_Decision_Layer.md</c> §4.8. ⭐ Every unit's task, SOP and ROE come from the scenario's snapshot keys
/// (<c>CE-3042</c>); the shooting is the real kill chain (fire → SimHost damage → CGF health → <c>Hit</c> sense).
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class SopDemoScenarioTests : IDisposable
{
    private const int DomainBase = 227;   // CycloneDDS accepts 0–232; 227 is unused by every other rail (grep, 2026-10-04; 220–226 is ExternalHostConformance)
    private static int _domainSeq = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    private readonly string _scenarioId = "sop_demo_" + Guid.NewGuid().ToString("N");

    public void Dispose() => NasScenarioStaging.Remove(_scenarioId);

    private static string RecipePath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var p = Path.Combine(dir.FullName, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Scenarios", "sop-demo", "scenario.json");
            if (File.Exists(p)) return p;
        }
        throw new FileNotFoundException("sop-demo recipe not found above " + AppContext.BaseDirectory);
    }

    private static Entity ByName(EntityRepository world, string prefix)
    {
        for (int i = 0; i <= world.MaxEntityIndex; i++)
        {
            var e = world.GetEntityByIndex(i);
            if (!world.IsAlive(e) || !world.HasComponent<EntityInfo>(e)) continue;
            if (world.GetComponent<EntityInfo>(e).Name.ToString().StartsWith(prefix, StringComparison.Ordinal)) return e;
        }
        return Entity.Null;
    }

    [Fact(Timeout = 180_000)]
    public async Task CE2082_TheSopDemo_ReactsWhereAllowed_StaysOnTaskWhereNot_AndIdlesWithoutAnOrder()
    {
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(_scenarioId));
        File.Copy(RecipePath(), Path.Combine(NasScenarioStaging.DirectoryOf(_scenarioId), "scenario.json"), overwrite: true);

        using var harness = new HrotRunnerHarness("simhost,ig,excon,cgf", NextDomainId());
        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;
        var rosterDeadline = DateTime.UtcNow.AddSeconds(10.0);
        while (master.NodeRoster.ActiveNodes.Count == 0 && DateTime.UtcNow < rosterDeadline)
        {
            harness.PumpFrames(1);
            Thread.Sleep(10);
        }
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = JsonSerializer.Serialize(new { TargetState = nameof(Hrot.NED.Descriptors.Orchestration.ClusterState.OperatingLive), ScenarioId = _scenarioId }),
        }).ConfigureAwait(false);
        Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == 31, timeoutFrames: 4000),
            $"cluster must reach OperatingLive; at {(int)master.CurrentClusterState}");

        var cgf = harness.Cgf!.World!;
        var registry = harness.Cgf!.TestHook_BehaviorRegistry!;
        Assert.True(harness.PumpUntil(() => ByName(cgf, "Unit C") != Entity.Null && ByName(cgf, "Squad B") != Entity.Null, timeoutFrames: 2000),
            "the demo's units must spawn on CGF");
        Entity idle = ByName(cgf, "Unit C"), squadA = ByName(cgf, "Squad A"), squadB = ByName(cgf, "Squad B");

        string? Task(Entity e) => registry.TryGetName(cgf.GetComponent<BehaviorState>(e).ActiveBehaviorHash, out var n) ? n : null;

        // ① no order ⇒ the SOP's idle choice
        Assert.True(harness.PumpUntil(() => Task(idle) == "Idle" && cgf.GetComponent<BehaviorState>(idle).Origin == BehaviorOrigin.Sop,
            timeoutFrames: 600), $"Unit C should idle through its SOP; runs {Task(idle)}");

        // ② shot at with reactions allowed ⇒ cover, the move paused; ③ shot at under StayOnTask ⇒ keeps moving
        bool aReacted = false, bWasHit = false, bReacted = false;
        harness.PumpUntil(() =>
        {
            var a = cgf.GetComponent<BehaviorState>(squadA);
            if (a.Origin == BehaviorOrigin.Reaction && BehaviorIngressSystem.PausedTaskOf(cgf, squadA)?.BehaviorName == "MoveToLocation")
                aReacted = true;
            bWasHit |= cgf.HasComponent<RecentSenses>(squadB)
                       && cgf.GetComponent<RecentSenses>(squadB).TryGetLast(Fdp.Toolkit.Perception.Events.SensorChange.Hit, out _);
            bReacted |= cgf.GetComponent<BehaviorState>(squadB).Origin == BehaviorOrigin.Reaction;
            return aReacted && bWasHit;
        }, timeoutFrames: 6000);

        Assert.True(aReacted, $"Squad A should take cover when hit; runs {Task(squadA)} at {cgf.GetComponent<BehaviorState>(squadA).Origin}");
        Assert.True(bWasHit, "Squad B must actually be hit, or the StayOnTask claim below proves nothing");
        Assert.False(bReacted, "Squad B's ROE says stay on task — it must never react");
        Assert.Equal("MoveToLocation", Task(squadB));
    }
}
