using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Utility;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using Xunit.Abstractions;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐ <c>CE-2073</c> — the shipped <c>tt-posture</c> scenario on <c>test-town</c>, live: a healthy, armed rifleman ordered
/// <c>CombatPosture</c> toward an objective sees an UNARMED hostile (a weaker enemy, <c>ThreatDanger</c> 0.3), so the
/// utility step picks AdvanceAndAttack: it fires at the hostile on the way and the posture ENDS at the objective.
/// 📄 <c>docs/DESIGN_Decision_Layer.md</c> §3.3b.
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class PostureScenarioTests : IDisposable
{
    private const int DomainBase = 52;    // CycloneDDS accepts 0–232; 52–53 used by no other rail (grep of domain ids, 2026-10-05 — ⛔ not 188: TheClusterAiDebugSurfaceAnswersTests owns 181–189)
    private static int _domainSeq = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    private static readonly Vector3 Objective = new(325, 300, 0);   // the recipe's "advance.Objective"

    private readonly string _scenarioId = "tt_posture_" + Guid.NewGuid().ToString("N");
    private readonly ITestOutputHelper _out;

    public PostureScenarioTests(ITestOutputHelper output) => _out = output;

    public void Dispose() => NasScenarioStaging.Remove(_scenarioId);

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Hrot", "Subsystems"))) return dir.FullName;
        throw new DirectoryNotFoundException("repo root not found above " + AppContext.BaseDirectory);
    }

    private static Entity ByName(EntityRepository world, string name)
    {
        for (int i = 0; i <= world.MaxEntityIndex; i++)
        {
            var e = world.GetEntityByIndex(i);
            if (!world.IsAlive(e) || !world.HasComponent<EntityInfo>(e)) continue;
            if (world.GetComponent<EntityInfo>(e).Name.ToString() == name) return e;
        }
        return Entity.Null;
    }

    [Fact(Timeout = 240_000)]
    public async Task CE2073_AnArmedRiflemanAgainstAnUnarmedHostile_AdvancesFiring_AndThePostureEndsAtTheObjective()
    {
        var root = RepoRoot();
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(_scenarioId));
        File.Copy(Path.Combine(root, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Scenarios", "tt-posture", "scenario.json"),
            Path.Combine(NasScenarioStaging.DirectoryOf(_scenarioId), "scenario.json"), overwrite: true);

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
        var sim = harness.SimHost.World!;
        var registry = harness.Cgf!.TestHook_BehaviorRegistry!;
        Assert.True(harness.PumpUntil(() => !ByName(sim, "Rifleman").IsNull && !ByName(sim, "Unarmed Hostile").IsNull
                                         && !ByName(cgf, "Rifleman").IsNull, timeoutFrames: 2000),
            "both units must spawn on SimHost and the rifleman on CGF");
        Entity rifleman = ByName(cgf, "Rifleman"), simRifleman = ByName(sim, "Rifleman");
        Vector3 Pos() => sim.GetComponent<SimTransform>(simRifleman).Position;
        var start = Pos();

        string? Task() => cgf.HasComponent<BehaviorState>(rifleman)
                       && registry.TryGetName(cgf.GetComponent<BehaviorState>(rifleman).ActiveBehaviorHash, out var n) ? n : null;
        // AimAndFireExecutor runs on CGF (CgfLogicPack) and spends the round from CGF's WeaponState.
        int Ammo() => cgf.HasComponent<WeaponState>(rifleman) ? cgf.GetComponent<WeaponState>(rifleman).Ammo : -1;
        string State()
        {
            var s = $"task={Task()} pos={Pos()} start={start}";
            if (cgf.HasComponent<TargetMemory>(rifleman)) s += $" memory={cgf.GetComponent<TargetMemory>(rifleman).Count}";
            if (cgf.HasComponent<WeaponChannel>(rifleman)) { var w = cgf.GetComponent<WeaponChannel>(rifleman); s += $" weapon action={w.ActiveAction} status={w.Status}"; }
            if (cgf.HasComponent<LocomotionChannel>(rifleman)) { var l = cgf.GetComponent<LocomotionChannel>(rifleman); s += $" loco action={l.ActiveAction} status={l.Status}"; }
            s += $" ammo={Ammo()}";
            return s;
        }

        // ① the scenario's own Behavior component starts the posture (Origin Superior).
        Assert.True(harness.PumpUntil(() => Task() == "CombatPosture", timeoutFrames: 2000), $"the posture must start; {State()}");

        // ② the rifleman sees the hostile, and the posture fires on it while it advances (AdvanceAndAttack drives both channels).
        Assert.True(harness.PumpUntil(() => cgf.HasComponent<TargetMemory>(rifleman) && cgf.GetComponent<TargetMemory>(rifleman).Count > 0,
            timeoutFrames: 3000), $"the rifleman must remember the hostile; {State()}");
        int full = cgf.HasComponent<WeaponState>(rifleman) ? cgf.GetComponent<WeaponState>(rifleman).MaxAmmo : -1;
        bool fired = harness.PumpUntil(() => Ammo() >= 0 && Ammo() < full, timeoutFrames: 3000);
        _out.WriteLine("on fire: " + State());
        Assert.True(fired, $"AdvanceAndAttack must SPEND rounds at the top threat (magazine {full}); {State()}");

        // ③ it arrives, and the posture ENDS there (RequireOne: AdvanceAndAttack's Success ends the Parallel).
        bool ended = harness.PumpUntil(() => Task() != "CombatPosture", timeoutFrames: 6000);
        _out.WriteLine("at end: " + State());
        Assert.True(ended, $"the posture must end at the objective; {State()}");
        var flat = new Vector2(Pos().X - Objective.X, Pos().Y - Objective.Y);
        Assert.True(flat.Length() <= 6f, $"it must end AT the objective {Objective} (radius 3 + slack); {State()}");
        Assert.True(Vector3.Distance(start, Pos()) > 10f, $"it must have advanced; {State()}");
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3079</c> — the shipped <c>ua-danger-crossing</c> scenario in-process (the live check's twin, design §5.3): the
    /// rifleman's danger-area sensor lists the two crossings, only the watched one rates threatened, the rifleman HOLDS short
    /// of it, the watcher's own two-task mission (Sentry → MoveToLocation) withdraws it out of sight, the rating clears and
    /// the rifleman crosses and arrives. ⛔ Nothing is written into the run. 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §10.5b.
    /// ⭐ B7: the same acceptance twice — the rifleman's task the BTree <c>DangerCrossing</c> (H5) and the BLUEPRINT
    /// <c>DangerCrossingBp</c> (H7); the cast, the watcher's mission and the map are identical.
    /// </summary>
    [Theory(Timeout = 900_000)]
    [InlineData("ua-danger-crossing")]
    [InlineData("ua-danger-crossing-bp")]
    public async Task CE3079_DangerCrossing_HoldsShortOfTheWatchedCrossing_UntilTheWatchersMissionWithdrawsIt_ThenArrives(string scenario)
    {
        var root = RepoRoot();
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(_scenarioId));
        File.Copy(Path.Combine(root, "scenarios", scenario, "scenario.json"),
            Path.Combine(NasScenarioStaging.DirectoryOf(_scenarioId), "scenario.json"), overwrite: true);

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
        var sim = harness.SimHost.World!;
        Assert.True(harness.PumpUntil(() => !ByName(sim, "Rifleman").IsNull && !ByName(sim, "Watcher").IsNull
                                         && !ByName(cgf, "Rifleman").IsNull && !ByName(cgf, "Watcher").IsNull, timeoutFrames: 2000),
            "both units must spawn on SimHost and on CGF");
        Entity rifleman = ByName(cgf, "Rifleman"), simRifleman = ByName(sim, "Rifleman"), simWatcher = ByName(sim, "Watcher");
        Entity cgfWatcher = ByName(cgf, "Watcher");
        Vector3 Pos(Entity e) => sim.GetComponent<SimTransform>(e).Position;
        var watcherStart = Pos(simWatcher);

        Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer Areas()
        {
            Fdp.Toolkit.Perception.Sensors.UnitSensors.TryGetResults<Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer>(
                cgf, rifleman, SensorModality.DangerArea, out var b);
            return b;
        }
        string State()
        {
            var b = Areas();
            var s = $"rifleman={Pos(simRifleman)} watcher={Pos(simWatcher)} areas={b.Count} ready={b.IsReady}";
            for (int i = 0; i < b.Count; i++) s += $" [{b.GetSpanRO()[i].Kind} d={b.GetSpanRO()[i].DistanceAlongRoute:F0} t={b.GetSpanRO()[i].ThreatRating:F2}]";
            if (cgf.HasComponent<MissionPlanQueue>(cgfWatcher)) s += $" watcherPhase={cgf.GetComponent<MissionPlanQueue>(cgfWatcher).CurrentPhase}";
            return s;
        }

        // ① the sensor lists two crossings; ② only one rates threatened
        Assert.True(harness.PumpUntil(() => Areas().Count >= 2, timeoutFrames: 20000), $"two crossings ahead; {State()}");
        Assert.True(harness.PumpUntil(() => Areas().Count > 0 && Areas().GetSpanRO()[0].ThreatRating >= 0.5f, timeoutFrames: 40000),
            $"the next crossing (the watched one) rates threatened; {State()}");
        var hot = Areas().GetSpanRO()[0];
        _out.WriteLine("threatened: " + State());

        // ③ the rifleman holds within 4 m of that crossing's near handle, for ≥ 10 s of sim time
        bool Near() => Vector2.Distance(new Vector2(Pos(simRifleman).X, Pos(simRifleman).Y), new Vector2(hot.NearSideHandle.X, hot.NearSideHandle.Y)) <= 4f;
        Assert.True(harness.PumpUntil(Near, timeoutFrames: 20000), $"the rifleman reaches the near side {hot.NearSideHandle}; {State()}");
        float holdFrom = sim.SimulationTime;
        Assert.True(harness.PumpUntil(() => !Near() || sim.SimulationTime - holdFrom >= 10f, timeoutFrames: 20000) && Near(),
            $"and holds there ≥ 10 s; {State()}");

        // ④ the watcher's mission moves on by itself and it walks away; the rating clears
        Assert.True(harness.PumpUntil(() => Vector3.Distance(watcherStart, Pos(simWatcher)) > 20f, timeoutFrames: 40000),
            $"the watcher's own mission withdraws it; {State()}");
        Assert.True(harness.PumpUntil(() => Areas().Count == 0 || Areas().GetSpanRO()[0].ThreatRating < 0.4f, timeoutFrames: 40000),
            $"the rating clears once the rifleman watched it go; {State()}");

        // ⑤ the rifleman crosses and arrives
        var objective = new Vector2(285f, 220f);
        Assert.True(harness.PumpUntil(() => Vector2.Distance(new Vector2(Pos(simRifleman).X, Pos(simRifleman).Y), objective) <= 6f,
            timeoutFrames: 40000), $"the rifleman crosses and arrives; {State()}");
        _out.WriteLine("arrived: " + State());
    }

    // ── CE-3085 (G11) — the U1 / U2 live checks' in-process twins (design §5.3: the HTTP check is the acceptance, this the gate) ──

    /// <summary>Stages <c>scenarios/&lt;folder&gt;</c>, boots simhost + ig + excon + cgf and brings the cluster to OperatingLive.</summary>
    private async Task<HrotRunnerHarness> StartShippedScenario(string folder)
    {
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(_scenarioId));
        File.Copy(Path.Combine(RepoRoot(), "scenarios", folder, "scenario.json"),
            Path.Combine(NasScenarioStaging.DirectoryOf(_scenarioId), "scenario.json"), overwrite: true);
        var harness = new HrotRunnerHarness("simhost,ig,excon,cgf", NextDomainId());
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
        return harness;
    }

    /// <summary>What <c>POST /trace/observe {on:true}</c> does: arm the unit's trace, which attaches its <see cref="UtilityDecisionLog"/>.</summary>
    private static void Observe(EntityRepository brain, Entity unit)
    {
        if (brain.HasComponent<DebugState>(unit))
            brain.GetComponentRW<DebugState>(unit).Behavior |= BehaviorDebugFlags.EnableTraceBuffer;
        else
            brain.AddComponent(unit, new DebugState { Behavior = BehaviorDebugFlags.EnableTraceBuffer });
    }

    /// <summary>The unit's logged slot for a decision, by its display name (what <c>GET /entities/{id}/utility</c> reads).</summary>
    private static bool TryDecision(EntityRepository brain, Entity unit, string name, out UtilityLogSlot slot)
    {
        slot = default;
        if (!brain.HasComponent<UtilityDecisionLog>(unit)) return false;
        foreach (var s in brain.GetComponentRO<UtilityDecisionLog>(unit).SlotsRO())
            if (s.DecisionId != 0 && UtilityDecisionCatalog.Shared.TryGet(s.DecisionId, out var def, out _) && def?.DebugName == name)
            {
                slot = s;
                return true;
            }
        return false;
    }

    /// <summary>
    /// ⭐ <c>CE-3085</c> — U1 <c>ua-posture</c> in-process (the live check's twin): healthy and armed against a matched enemy ⇒
    /// Suppress; hurt / near death ⇒ a DEFENSIVE posture (TakeCover or Flee — which one depends on the EQS answers where it
    /// stands, measured live); healed ⇒ a FIGHTING posture; twice; and the winner does not flicker while Health is held.
    /// Health is written on the Brain, its authoritative home (S8). 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §4 U1.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task CE3085_U1_Posture_FightsWhenHealthy_DefendsWhenHurt_AndDoesNotFlicker()
    {
        using var harness = await StartShippedScenario("ua-posture");
        var cgf = harness.Cgf!.World!;
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Rifleman").IsNull, timeoutFrames: 2000), "the rifleman must spawn on CGF");
        var rifleman = ByName(cgf, "Rifleman");
        Observe(cgf, rifleman);

        Posture? Winner() => TryDecision(cgf, rifleman, "Combat posture", out var s) && s.Winner != 0 ? (Posture)s.Winner : null;
        string State()
        {
            if (!TryDecision(cgf, rifleman, "Combat posture", out var s)) return "no posture log";
            var r = s.RankedRO();
            var ranked = "";
            for (int i = 0; i < s.Count; i++) ranked += $"{(Posture)r[i].WinningPostureId}={r[i].Score:F2} ";
            return $"winner={Winner()} switches={s.SwitchCount} evals={s.EvalCount} hp={cgf.GetComponent<Health>(rifleman).Current} ranked=[{ranked}]";
        }

        Assert.True(harness.PumpUntil(() => Winner() == Posture.Suppress, timeoutFrames: 6000),
            $"healthy, armed, facing a MATCHED enemy ⇒ Suppress; {State()}");

        Posture[] fight = { Posture.Suppress, Posture.AdvanceAndAttack }, defend = { Posture.TakeCover, Posture.Flee };
        void Step(float hp, Posture[] want, string what)
        {
            cgf.GetComponentRW<Health>(rifleman).Current = hp;
            Assert.True(harness.PumpUntil(() => Winner() is { } w && Array.IndexOf(want, w) >= 0, timeoutFrames: 4000), $"{what}; {State()}");
            _out.WriteLine($"{what}: {State()}");
        }
        Step(40, defend, "hurt (40 HP) ⇒ a defensive posture");
        Step(10, defend, "near death (10 HP) ⇒ a defensive posture");
        Step(100, fight, "healed ⇒ back to fighting");
        Step(10, defend, "near death again ⇒ a defensive posture");
        Step(100, fight, "healed again ⇒ fighting");

        // Hysteresis: Health held ⇒ no switch over ~5 s of frames.
        Assert.True(TryDecision(cgf, rifleman, "Combat posture", out var held));
        harness.PumpFrames(300);
        Assert.True(TryDecision(cgf, rifleman, "Combat posture", out var later));
        Assert.True(later.EvalCount > held.EvalCount, $"the decision keeps evaluating; {State()}");
        Assert.Equal(held.SwitchCount, later.SwitchCount);
    }

    /// <summary>
    /// ⭐ <c>CE-3085</c> — U2 <c>ua-threat-ranking</c> in-process (the live check's twin): once the ranking settles over at
    /// least two remembered contacts with a non-zero top score, the armed, visible, nearest contact ranks first; the
    /// unarmed civilian and the farther armed contact rank below it; the one hidden behind the Tower is never ranked.
    /// 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §4 U2 (CE-3070, CE-3073, CE-3074 are the defects it found).
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task CE3085_U2_ThreatRanking_TheArmedVisibleNearestRanksFirst_TheHiddenNever()
    {
        using var harness = await StartShippedScenario("ua-threat-ranking");
        var cgf = harness.Cgf!.World!;
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Rifleman").IsNull, timeoutFrames: 2000), "the rifleman must spawn on CGF");
        var rifleman = ByName(cgf, "Rifleman");
        Observe(cgf, rifleman);

        string NameOf(long handle)
        {
            var e = new Entity((ulong)handle);
            return handle != 0 && cgf.IsAlive(e) && cgf.HasComponent<EntityInfo>(e) ? cgf.GetComponent<EntityInfo>(e).Name.ToString() : $"#{handle}";
        }
        System.Collections.Generic.List<(string Name, float Score)> Ranked()
        {
            var list = new System.Collections.Generic.List<(string, float)>();
            if (!TryDecision(cgf, rifleman, "Threat ranking", out var s)) return list;
            var r = s.RankedRO();
            for (int i = 0; i < s.Count; i++) list.Add((NameOf(r[i].CandidateHandle), r[i].Score));
            return list;
        }
        string State() => "ranked=[" + string.Join(", ", Ranked().Select(x => $"{x.Name}={x.Score:F2}")) + "]";

        // ⭐ Settled, not first: a contact's score grows while it stays in view (measured live 2026-10-05).
        Assert.True(harness.PumpUntil(() => Ranked() is { Count: >= 2 } r && r[0].Score > 0, timeoutFrames: 6000),
            $"the ranking settles over ≥ 2 remembered contacts with a top score above 0; {State()}");
        // let it settle on the armed near contact (scores grow over a few seconds of contact)
        harness.PumpUntil(() => Ranked() is { Count: > 0 } r && r[0].Name == "Armed Near", timeoutFrames: 3000);
        var ranked = Ranked();
        _out.WriteLine(State());
        var names = ranked.Select(x => x.Name).ToList();
        Assert.Equal("Armed Near", names[0]);
        Assert.DoesNotContain("Armed Hidden", names);
        if (names.Contains("Civilian Near")) Assert.True(names.IndexOf("Civilian Near") > names.IndexOf("Armed Near"), State());
        if (names.Contains("Armed Far"))     Assert.True(names.IndexOf("Armed Far") > names.IndexOf("Armed Near"), State());
    }
}
