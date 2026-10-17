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
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
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

    private static unsafe Entity FireTarget(EntityRepository world, Entity unit)
    {
        if (!world.HasComponent<WeaponChannel>(unit)) return Entity.Null;
        var ch = world.GetComponent<WeaponChannel>(unit);
        return ((Fdp.Toolkit.Combat.Executors.AimAndFireParams*)ch.Params)->Target;
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

        // Hysteresis: Health held ⇒ the winner never flips BACK (A → B → A) — a flicker. ⚠ One forward switch while Health is
        //   held is not a flicker: the fight itself moves on (measured in-process 2026-10-06: one switch in ~5 s after the
        //   last heal) — so sample every frame and assert no reversal, and print the sequence.
        Assert.True(TryDecision(cgf, rifleman, "Combat posture", out var held));
        var seq = new System.Collections.Generic.List<Posture?> { Winner() };
        for (int f = 0; f < 300; f++)
        {
            harness.PumpFrames(1);
            var w = Winner();
            if (w != seq[^1]) seq.Add(w);
        }
        Assert.True(TryDecision(cgf, rifleman, "Combat posture", out var later));
        var hostile = ByName(cgf, "Armed Hostile");
        _out.WriteLine($"held at 100 HP: winners {string.Join(" → ", seq)}; hostile hp={(hostile.IsNull || !cgf.HasComponent<Health>(hostile) ? -1 : cgf.GetComponent<Health>(hostile).Current)}; {State()}");
        Assert.True(later.EvalCount > held.EvalCount, $"the decision keeps evaluating; {State()}");
        for (int i = 2; i < seq.Count; i++)
            Assert.False(seq[i] == seq[i - 2], $"flicker: {string.Join(" → ", seq)}; {State()}");
    }

    /// <summary>
    /// ⭐ <c>CE-2233</c> CIV-0 — <c>ua-civ-neutral-gate</c> in-process (the live check's twin; 📄 docs/DESIGN_Civilians_And_Crowd_Control.md
    /// §5c): the rifleman SEES an unarmed and an ARMED neutral (the armed one nearest of all), kills the Hostile, and neither neutral
    /// loses a point of health; with only neutrals left he spends no round; once the armed neutral's side flips to Hostile — through
    /// the one engine write path (<c>EntityWriteRouter</c>, CE-3221) — he is engaged.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task CE2233_CivNeutralGate_NeutralsSeenNeverHit_TheHostileKilled_AFlippedSideIsEngaged()
    {
        using var harness = await StartShippedScenario("ua-civ-neutral-gate");
        var cgf = harness.Cgf!.World!;
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Rifleman").IsNull && !ByName(cgf, "Armed Neutral").IsNull, timeoutFrames: 2000),
            "the cast must spawn on CGF");
        var rifle = ByName(cgf, "Rifleman");
        var civ = ByName(cgf, "Civilian");
        var armed = ByName(cgf, "Armed Neutral");
        var hostile = ByName(cgf, "Hostile");
        float Hp(Entity e) => cgf.IsAlive(e) && cgf.HasComponent<Health>(e) ? cgf.GetComponent<Health>(e).Current : -1f;
        float civFull = Hp(civ), armedFull = Hp(armed), civLow = civFull, armedLow = armedFull;
        void Sample() { civLow = Math.Min(civLow, Hp(civ)); armedLow = Math.Min(armedLow, Hp(armed)); }
        unsafe bool Remembers(Entity e)
        {
            if (!cgf.HasComponent<TargetMemory>(rifle)) return false;
            var m = cgf.GetComponent<TargetMemory>(rifle);
            for (int i = 0; i < m.Count; i++) if (m.EntityIds[i] == (long)e.PackedValue) return true;
            return false;
        }
        string State() => $"civ {Hp(civ)} (low {civLow}) · armed neutral {Hp(armed)} (low {armedLow}) · hostile {Hp(hostile)}";

        Assert.True(harness.PumpUntil(() => { Sample(); return Remembers(civ) && Remembers(armed); }, timeoutFrames: 6000),
            $"① the rifleman must see both neutrals; {State()}");
        // ② out of the fight, not "health 0": an incapacitated man bleeds out and is not shot again (Injury.IsInTheFight, I4)
        Assert.True(harness.PumpUntil(() => { Sample(); return !Fdp.Toolkit.Combat.Injury.IsInTheFight(cgf, hostile); }, timeoutFrames: 12000),
            $"② the Hostile must be put out of the fight; {State()}");
        Assert.Equal(civFull, civLow);
        Assert.Equal(armedFull, armedLow);

        int Ammo() => cgf.HasComponent<WeaponState>(rifle) ? cgf.GetComponent<WeaponState>(rifle).Ammo : -1;
        // ⑤ settle first: the burst at the falling Hostile runs on for a few rounds after he drops (measured live) — then nothing
        int lastAmmo = Ammo(), still = 0;
        Assert.True(harness.PumpUntil(() => { Sample(); int a = Ammo(); still = a == lastAmmo ? still + 1 : 0; lastAmmo = a; return still >= 180; },
            timeoutFrames: 3000), $"⑤ his fire must stop once the Hostile is down; {State()}");
        int a0 = Ammo();
        harness.PumpUntil(() => { Sample(); return false; }, timeoutFrames: 480);   // ~8 s at 60 Hz
        Assert.Equal(a0, Ammo());                                                   // ⑤ nothing left to shoot
        Assert.Equal(armedFull, armedLow);

        var route = Fdp.Toolkit.Replication.Attributes.EntityWriteRouter.For(cgf).Write(armed, new[]
        {
            new Fdp.Toolkit.Replication.Patching.EntityAttributeChange
            {
                AttributeId = Fdp.Toolkit.Replication.Patching.AttributeIds.Affiliation,
                Value       = Fdp.Toolkit.Replication.Patching.AttributeValue.FromInt((int)Fdp.Toolkit.Replication.ForceIdentifier.Opposing),
            },
        });
        _out.WriteLine($"flip route {route}; {State()}");
        Assert.True(harness.PumpUntil(() => cgf.GetComponentRO<EntityInfo>(armed).ForceId == ForceId.Hostile, timeoutFrames: 2000),
            $"⑥ the side flip must land on CGF (route {route})");
        Assert.True(harness.PumpUntil(() => Hp(armed) < armedFull, timeoutFrames: 6000), $"⑥ once Hostile he must be engaged; {State()}");
        Assert.Equal(civFull, Hp(civ));
        _out.WriteLine(State());
    }

    /// <summary>
    /// ⭐ <c>CE-2235</c> CIV-1 — <c>ua-civ-routine-alarm</c> in-process (the live check's twin; 📄 docs/DESIGN_Civilians_And_Crowd_Control.md
    /// §5c, §5d): six civilians of the new types run <c>CivilianHsm</c> from their type's default and start in Routine; the first shots
    /// alarm the two 40 m from the shooter and none of the four 170 m away; quiet again, the two go back to Routine; one wounded to
    /// 40 % is a Casualty and stays where he fell.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task CE2235_CivRoutineAlarm_HeardShotsAlarmTheNearOnly_QuietRestoresTheRoutine_AWoundIsACasualty()
    {
        using var harness = await StartShippedScenario("ua-civ-routine-alarm");
        var cgf = harness.Cgf!.World!;
        string[] nearNames = { "Near Civilian 1", "Near Civilian 2" };
        string[] farNames = { "Far Civilian 1", "Far Civilian 2", "Far Civilian 3", "Far Civilian 4" };
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Rifleman").IsNull && !ByName(cgf, "Far Civilian 4").IsNull, timeoutFrames: 2000),
            "the cast must spawn on CGF");
        var rifle = ByName(cgf, "Rifleman");
        var hostile = ByName(cgf, "Hostile");
        var near = nearNames.Select(n => ByName(cgf, n)).ToArray();
        var far = farNames.Select(n => ByName(cgf, n)).ToArray();
        Fdp.Toolkit.Civilians.CivilianMode Mode(Entity e) => Fdp.Toolkit.Civilians.CivilianMind.ModeOf(cgf, e);
        bool farAlarmed = false;
        var nearAlarmed = new System.Collections.Generic.HashSet<Entity>();   // ⭐ CE-2236 — sampled every frame: past PanicAt he moves on to Panic
        void Sample()
        {
            foreach (var f in far) farAlarmed |= Mode(f) is not (Fdp.Toolkit.Civilians.CivilianMode.Routine or Fdp.Toolkit.Civilians.CivilianMode.None);
            foreach (var n in near) if (Mode(n) == Fdp.Toolkit.Civilians.CivilianMode.Alarmed) nearAlarmed.Add(n);
        }
        string State() => "near [" + string.Join(", ", near.Select(Mode)) + "] far [" + string.Join(", ", far.Select(Mode)) + "]";

        Assert.True(harness.PumpUntil(() => near.Concat(far).All(e => Mode(e) == Fdp.Toolkit.Civilians.CivilianMode.Routine), timeoutFrames: 1200),
            $"① every civilian starts in Routine; {State()}");
        int Ammo() => cgf.HasComponent<WeaponState>(rifle) ? cgf.GetComponent<WeaponState>(rifle).Ammo : -1;
        int a0 = Ammo();
        Assert.True(harness.PumpUntil(() => { Sample(); return Ammo() < a0; }, timeoutFrames: 6000), $"② the rifleman opens fire; {State()}");
        Assert.True(harness.PumpUntil(() => { Sample(); return nearAlarmed.Count == near.Length; }, timeoutFrames: 150),
            $"② both near civilians are Alarmed within 2.5 s of the first shot (more shots may panic them on: CE-2236); {State()}");
        harness.PumpUntil(() => { Sample(); return !Fdp.Toolkit.Combat.Injury.IsInTheFight(cgf, hostile); }, timeoutFrames: 12000);
        Assert.True(harness.PumpUntil(() => { Sample(); return near.All(e => Mode(e) == Fdp.Toolkit.Civilians.CivilianMode.Routine); }, timeoutFrames: 6000),
            $"③ quiet again, both near civilians go back to Routine; {State()}");
        Assert.False(farAlarmed, $"② no far civilian (beyond hearing) is ever alarmed; {State()}");

        var victim = near[0];
        cgf.GetComponentRW<Health>(victim).Current = 40f;   // ⭐ CGF owns Health (the Brain group): the owner's write
        Assert.True(harness.PumpUntil(() => Mode(victim) == Fdp.Toolkit.Civilians.CivilianMode.Casualty, timeoutFrames: 300),
            $"④ wounded to 40 %, he is a Casualty; {State()}");
        harness.PumpUntil(() => false, timeoutFrames: 60);   // ⭐ a walking man takes a moment to stop: measure from where he came to rest
        var at = cgf.GetComponent<SimTransform>(victim).Position;
        harness.PumpUntil(() => false, timeoutFrames: 240);
        Assert.Equal(Fdp.Toolkit.Civilians.CivilianMode.Casualty, Mode(victim));
        float drift = Vector3.Distance(at, cgf.GetComponent<SimTransform>(victim).Position);
        Assert.True(drift < 0.5f, $"④ …and stays where he fell (moved {drift:F2} m in 4 s)");
        _out.WriteLine(State());
    }

    /// <summary>A terrain door's state as <paramref name="world"/> holds it (its door entity), or null while that node has none.</summary>
    private static Fdp.Toolkit.Terrain.TerrainDoorState? DoorOf(EntityRepository world, string key)
    {
        var d = Fdp.Toolkit.Terrain.TerrainObjects.Find(world, key);
        return !d.IsNull && world.HasComponent<Fdp.Toolkit.Terrain.DoorState>(d) ? world.GetComponent<Fdp.Toolkit.Terrain.DoorState>(d).State : null;
    }

    /// <summary>
    /// ⭐ <c>CE-2236</c> CIV-2 — <c>ua-civ-shelter</c> in-process (the live check's twin; 📄 docs/DESIGN_Civilians_And_Crowd_Control.md
    /// §5c, §5e): on bt-range a burst 28 m away panics four civilians; each runs through House A's closed (not locked) door to a point
    /// inside its footprint, none enters the locked House B, and each deletes himself about 20 s (the scenario's hide time) after going in
    /// — gone on CGF, SimHost and IG. Sampled every frame, so <c>Panic</c> itself is asserted (the live check's poll accepts Panic or beyond).
    /// </summary>
    [Fact(Timeout = 900_000)]
    public async Task CE2236_CivShelter_PanicRunsThroughAnOpenableDoor_NeverIntoTheLockedHouse_GoneAfterTheHideTime()
    {
        using var harness = await StartShippedScenario("ua-civ-shelter");
        var cgf = harness.Cgf!.World!;
        var sim = harness.SimHost.World!;
        var ig = harness.Ig.App.World;
        string[] names = { "Civilian 1", "Civilian 2", "Civilian 3", "Civilian 4" };
        const string aFront = "bt-range/House A/front", aBack = "bt-range/House A/back", bFront = "bt-range/House B/front", bBack = "bt-range/House B/back";
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Rifleman").IsNull && names.All(n => !ByName(cgf, n).IsNull && !ByName(sim, n).IsNull && !ByName(ig, n).IsNull),
            timeoutFrames: 2000), "the cast must spawn on CGF, SimHost and IG");
        Assert.True(harness.PumpUntil(() => DoorOf(cgf, aFront) != null && DoorOf(cgf, bFront) != null, timeoutFrames: 2000), "the doors must exist as entities");
        Assert.Equal(Fdp.Toolkit.Terrain.TerrainDoorState.Closed, DoorOf(cgf, aFront));   // ⓪ the scenario's TerrainObjects
        Assert.Equal(Fdp.Toolkit.Terrain.TerrainDoorState.Closed, DoorOf(cgf, aBack));
        Assert.Equal(Fdp.Toolkit.Terrain.TerrainDoorState.Locked, DoorOf(cgf, bFront));
        Assert.Equal(Fdp.Toolkit.Terrain.TerrainDoorState.Locked, DoorOf(cgf, bBack));

        var rifle = ByName(cgf, "Rifleman");
        var civs = names.Select(n => ByName(cgf, n)).ToArray();
        Fdp.Toolkit.Civilians.CivilianMode Mode(Entity e) => cgf.IsAlive(e) ? Fdp.Toolkit.Civilians.CivilianMind.ModeOf(cgf, e) : Fdp.Toolkit.Civilians.CivilianMode.None;
        var panicked = new System.Collections.Generic.HashSet<Entity>();
        var insideAt = new System.Collections.Generic.Dictionary<Entity, (double At, Vector3 Where)>();
        var goneAt = new System.Collections.Generic.Dictionary<Entity, double>();
        var inB = new System.Collections.Generic.HashSet<Entity>();
        var opened = new System.Collections.Generic.HashSet<string>();
        bool Sample()
        {
            double now = Fdp.Toolkit.Combat.HitModel.Now(cgf);
            foreach (var e in civs)
            {
                if (goneAt.ContainsKey(e)) continue;
                if (!cgf.IsAlive(e)) { goneAt[e] = now; continue; }
                var m = Mode(e);
                if (m == Fdp.Toolkit.Civilians.CivilianMode.Panic) panicked.Add(e);
                var p = cgf.GetComponent<SimTransform>(e).Position;
                if (m == Fdp.Toolkit.Civilians.CivilianMode.Inside && !insideAt.ContainsKey(e)) insideAt[e] = (now, p);
                if (p.X >= 160f && p.X <= 168f && p.Y >= 100f && p.Y <= 110f) inB.Add(e);
            }
            foreach (var (host, w) in new[] { ("CGF", cgf), ("SimHost", sim), ("IG", ig) })
                if (DoorOf(w, aFront) == Fdp.Toolkit.Terrain.TerrainDoorState.Open || DoorOf(w, aBack) == Fdp.Toolkit.Terrain.TerrainDoorState.Open) opened.Add(host);
            return goneAt.Count == civs.Length;
        }
        string State() => string.Join(", ", names.Zip(civs, (n, e) => $"{n}: {Mode(e)}{(insideAt.TryGetValue(e, out var i) ? $" in@{i.At:F1}s {i.Where.X:F1},{i.Where.Y:F1}" : "")}{(goneAt.TryGetValue(e, out var g) ? $" gone@{g:F1}s" : "")}"))
                          + $" · door opened on [{string.Join(" ", opened)}]";

        Assert.True(harness.PumpUntil(() => civs.All(e => Mode(e) == Fdp.Toolkit.Civilians.CivilianMode.Routine), timeoutFrames: 1200),
            $"① the 4 civilians start in Routine; {State()}");
        int Ammo() => cgf.HasComponent<WeaponState>(rifle) ? cgf.GetComponent<WeaponState>(rifle).Ammo : -1;
        int a0 = Ammo();
        Assert.True(harness.PumpUntil(() => { Sample(); return Ammo() < a0; }, timeoutFrames: 6000), $"① the rifleman opens fire, 28 m away; {State()}");
        bool done = harness.PumpUntil(Sample, timeoutFrames: 9000);
        _out.WriteLine(State());

        Assert.True(panicked.Count == civs.Length, $"① each civilian enters Panic; {State()}");
        Assert.True(insideAt.Count == civs.Length, $"② each reaches Inside; {State()}");
        Assert.True(insideAt.Values.All(i => i.Where.X > 100f && i.Where.X < 110f && i.Where.Y > 100f && i.Where.Y < 108f),
            $"② …standing inside House A's footprint (100–110 × 100–108); {State()}");
        Assert.True(opened.SetEquals(new[] { "CGF", "SimHost", "IG" }), $"② a door of House A opens to let them in, on every node; {State()}");
        Assert.True(inB.Count == 0, $"③ none enters the locked House B; {State()}");
        Assert.True(done, $"④ each deletes himself after hiding; {State()}");
        Assert.True(goneAt.All(g => g.Value - insideAt[g.Key].At is >= 18.0 and <= 23.0), $"④ …about 20 s (the scenario's hide time) after going in; {State()}");
        Assert.True(harness.PumpUntil(() => names.All(n => ByName(sim, n).IsNull && ByName(ig, n).IsNull), timeoutFrames: 600),
            $"④ …and is gone on SimHost and IG too: [{string.Join(", ", names.Where(n => !ByName(sim, n).IsNull || !ByName(ig, n).IsNull))}]");
    }

    /// <summary>
    /// ⭐ <c>CE-2236</c> CIV-2 — <c>ua-civ-cower</c> in-process (the live check's twin; 📄 docs/DESIGN_Civilians_And_Crowd_Control.md
    /// §5c, §5e): on test-town (every building solid) a firefight 15–25 m away panics three civilians, and with no building to enter each
    /// cowers — <c>EffectState.Cowering</c> on CGF, SimHost and IG, standing still; once the hostile is down and fear fades each leaves
    /// Cower (for Alarmed, never back to Panic) and the flag clears on every node.
    /// </summary>
    [Fact(Timeout = 900_000)]
    public async Task CE2236_CivCower_NoBuildingToEnter_EachCowersOnEveryNode_AndRisesWhenTheFiringStops()
    {
        using var harness = await StartShippedScenario("ua-civ-cower");
        var cgf = harness.Cgf!.World!;
        var sim = harness.SimHost.World!;
        var ig = harness.Ig.App.World;
        string[] names = { "Civilian 1", "Civilian 2", "Civilian 3" };
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Rifleman").IsNull && !ByName(cgf, "Hostile").IsNull
                                         && names.All(n => !ByName(cgf, n).IsNull && !ByName(sim, n).IsNull && !ByName(ig, n).IsNull), timeoutFrames: 2000),
            "the cast must spawn on CGF, SimHost and IG");
        var hostile = ByName(cgf, "Hostile");
        var civs = names.Select(n => ByName(cgf, n)).ToArray();
        Fdp.Toolkit.Civilians.CivilianMode Mode(Entity e) => Fdp.Toolkit.Civilians.CivilianMind.ModeOf(cgf, e);
        bool CowersOn(EntityRepository w, string n) { var e = ByName(w, n); return !e.IsNull && Effects.IsCowering(w, e); }
        var hosts = new[] { ("CGF", cgf), ("SimHost", sim), ("IG", ig) };
        var panicked = new System.Collections.Generic.HashSet<Entity>();
        bool rePanicked = false;
        void Sample() { foreach (var e in civs) if (Mode(e) == Fdp.Toolkit.Civilians.CivilianMode.Panic) panicked.Add(e); }
        string State() => "[" + string.Join(", ", civs.Select(Mode)) + "] cowering " + string.Join(" ", hosts.Select(h => $"{h.Item1}:{names.Count(n => CowersOn(h.Item2, n))}"));

        Assert.True(harness.PumpUntil(() => civs.All(e => Mode(e) == Fdp.Toolkit.Civilians.CivilianMode.Routine), timeoutFrames: 1200),
            $"① the 3 civilians start in Routine; {State()}");
        Assert.True(harness.PumpUntil(() => { Sample(); return civs.All(e => Mode(e) == Fdp.Toolkit.Civilians.CivilianMode.Cower); }, timeoutFrames: 6000),
            $"① under fire, with no building to enter, each cowers; {State()}");
        Assert.True(panicked.Count == civs.Length, $"① …through Panic (the only way into Cower); {State()}");
        Assert.True(harness.PumpUntil(() => hosts.All(h => names.All(n => CowersOn(h.Item2, n))), timeoutFrames: 120),
            $"① EffectState.Cowering is set on every node; {State()}");
        var at = civs.Select(e => cgf.GetComponent<SimTransform>(e).Position).ToArray();
        harness.PumpUntil(() => false, timeoutFrames: 90);
        float moved = civs.Select((e, i) => Vector3.Distance(at[i], cgf.GetComponent<SimTransform>(e).Position)).Max();
        Assert.True(civs.All(e => Mode(e) == Fdp.Toolkit.Civilians.CivilianMode.Cower) && moved < 0.3f, $"① …and stands still (moved {moved:F2} m in 1.5 s); {State()}");

        Assert.True(harness.PumpUntil(() => !Fdp.Toolkit.Combat.Injury.IsInTheFight(cgf, hostile), timeoutFrames: 12000), $"the hostile is put down; {State()}");
        Assert.True(harness.PumpUntil(() =>
        {
            foreach (var e in civs) rePanicked |= Mode(e) == Fdp.Toolkit.Civilians.CivilianMode.Panic;
            return civs.All(e => Mode(e) is not (Fdp.Toolkit.Civilians.CivilianMode.Cower or Fdp.Toolkit.Civilians.CivilianMode.Panic));
        }, timeoutFrames: 3600), $"② the firing over, each leaves Cower as his fear fades; {State()}");
        Assert.False(rePanicked, $"② …for Alarmed, never back to Panic (§5e: Cower → Alarmed); {State()}");
        Assert.True(harness.PumpUntil(() => hosts.All(h => names.All(n => !CowersOn(h.Item2, n))), timeoutFrames: 120),
            $"② …and Cowering is cleared on every node; {State()}");
        _out.WriteLine(State());
    }

    /// <summary>
    /// ⭐ <c>CE-2237</c> CIV-3 — <c>ua-civ-gathering</c> in-process (the live check's twin; 📄 docs/DESIGN_Civilians_And_Crowd_Control.md
    /// §5c, §5f): a hurt civilian at test-town's crossroads draws the twelve calm civilians 30–60 m up the four streets; each Gathers, their
    /// mean distance to him falls below 15 m within 60 s, they stand on a ring round him (not on top of him), and one crowd holds at least 8.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task CE2237_CivGathering_AHurtManDrawsTheOnlookers_ToARingRoundHim_AsOneCrowd()
    {
        using var harness = await StartShippedScenario("ua-civ-gathering");
        var cgf = harness.Cgf!.World!;
        var names = Enumerable.Range(1, 12).Select(i => $"Onlooker {i}").ToArray();
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Casualty").IsNull && names.All(n => !ByName(cgf, n).IsNull), timeoutFrames: 2000),
            "the cast must spawn on CGF");
        var casualty = ByName(cgf, "Casualty");
        var onlookers = names.Select(n => ByName(cgf, n)).ToArray();
        Fdp.Toolkit.Civilians.CivilianMode Mode(Entity e) => Fdp.Toolkit.Civilians.CivilianMind.ModeOf(cgf, e);
        Fdp.Toolkit.Civilians.CivilianState Mind(Entity e) => cgf.GetComponent<Fdp.Toolkit.Civilians.CivilianState>(e);
        Vector3 Pos(Entity e) => cgf.GetComponent<SimTransform>(e).Position;
        Assert.True(harness.PumpUntil(() => Mode(casualty) == Fdp.Toolkit.Civilians.CivilianMode.Casualty, timeoutFrames: 600),
            "⓪ the hurt civilian (health 40) is a Casualty — the incident");
        var focus = Pos(casualty);
        float Dist(Entity e) => Vector2.Distance(new Vector2(Pos(e).X, Pos(e).Y), new Vector2(focus.X, focus.Y));
        var gathered = new System.Collections.Generic.HashSet<Entity>();
        double t0 = Fdp.Toolkit.Combat.HitModel.Now(cgf);
        string State() => string.Join(", ", onlookers.Select(e => $"{Mode(e)}@{Dist(e):F1}m#{Mind(e).CrowdId}"));

        Assert.True(harness.PumpUntil(() =>
        {
            foreach (var e in onlookers) if (Mode(e) == Fdp.Toolkit.Civilians.CivilianMode.Gather) gathered.Add(e);
            return onlookers.Average(Dist) < 15f;
        }, timeoutFrames: 5400), $"② their mean distance to him falls below 15 m; {State()}");
        double took = Fdp.Toolkit.Combat.HitModel.Now(cgf) - t0;
        // ⭐ the mean falls under 15 m while the 60 m ones are still walking (measured live: 25 s) — let the last ones arrive
        Assert.True(harness.PumpUntil(() =>
        {
            foreach (var e in onlookers) if (Mode(e) == Fdp.Toolkit.Civilians.CivilianMode.Gather) gathered.Add(e);
            return onlookers.All(e => Dist(e) <= 11f);
        }, timeoutFrames: 3600), $"④ every onlooker arrives (within 11 m of him); {State()}");
        _out.WriteLine($"mean < 15 m after {took:F1} s; {State()}");

        Assert.Equal(onlookers.Length, gathered.Count);   // ① each is drawn to it
        Assert.True(took <= 60.0, $"② …within 60 s (took {took:F1} s)");
        Assert.True(onlookers.All(e => Dist(e) is >= 2.5f and <= 11f), $"④ they stand on a ring round him, not on top of him; {State()}");
        var ids = onlookers.Select(e => Mind(e).CrowdId).ToArray();
        int best = ids.GroupBy(i => i).OrderByDescending(g => g.Count()).First().Key;
        Assert.True(best != 0 && ids.Count(i => i == best) >= 8, $"③ one crowd holds at least 8 of them; {State()}");
    }

    /// <summary>
    /// ⭐ <c>CE-2237</c> CIV-3 — <c>ua-civ-contagion</c> in-process (the live check's twin; 📄 docs/DESIGN_Civilians_And_Crowd_Control.md
    /// §5c, §5f): fifteen civilians stand as ONE crowd along test-town's street, 30–142 m from a firefight at its west end. The near ones
    /// hear it and panic; the far ones hear NOTHING (<c>HeardAt</c> stays 0, sampled every frame) — and at least three of them panic
    /// anyway, caught from the crowd.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task CE2237_CivContagion_FearReachesTheOnesWhoHeardNothing_ThroughTheCrowd()
    {
        using var harness = await StartShippedScenario("ua-civ-contagion");
        var cgf = harness.Cgf!.World!;
        var names = Enumerable.Range(1, 15).Select(i => $"Civilian {i}").ToArray();
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Rifleman").IsNull && names.All(n => !ByName(cgf, n).IsNull), timeoutFrames: 2000),
            "the cast must spawn on CGF");
        var rifle = ByName(cgf, "Rifleman");
        var civs = names.Select(n => ByName(cgf, n)).ToArray();
        Fdp.Toolkit.Civilians.CivilianMode Mode(Entity e) => Fdp.Toolkit.Civilians.CivilianMind.ModeOf(cgf, e);
        Fdp.Toolkit.Civilians.CivilianState Mind(Entity e) => cgf.GetComponent<Fdp.Toolkit.Civilians.CivilianState>(e);
        Assert.True(harness.PumpUntil(() => civs.All(e => cgf.HasComponent<Fdp.Toolkit.Civilians.CivilianState>(e)) && civs.All(e => Mind(e).CrowdId != 0)
                                         && civs.Select(e => Mind(e).CrowdId).Distinct().Count() == 1, timeoutFrames: 1200),
            "⓪ the fifteen stand as ONE crowd along the street");

        var heard = new System.Collections.Generic.HashSet<Entity>();
        var panicked = new System.Collections.Generic.HashSet<Entity>();
        void Sample()
        {
            foreach (var e in civs)
            {
                if (!cgf.IsAlive(e)) continue;
                if (Mind(e).HeardAt > 0) heard.Add(e);
                if (Mode(e) == Fdp.Toolkit.Civilians.CivilianMode.Panic) panicked.Add(e);
            }
        }
        string State() => string.Join(", ", civs.Select((e, i) => $"{i + 1}:{Mode(e)}{(heard.Contains(e) ? "h" : "")}{(cgf.IsAlive(e) ? $"{Mind(e).Fear:F2}" : "")}"));
        int Ammo() => cgf.HasComponent<WeaponState>(rifle) ? cgf.GetComponent<WeaponState>(rifle).Ammo : -1;
        int a0 = Ammo();
        Assert.True(harness.PumpUntil(() => { Sample(); return Ammo() < a0; }, timeoutFrames: 6000), $"① the rifleman opens fire at the west end; {State()}");
        for (int f = 0; f < 2400; f += 60)   // 40 s, logged each second: who caught it, from whom, in which crowd
        {
            harness.PumpUntil(() => { Sample(); return false; }, timeoutFrames: 60);
            _out.WriteLine($"t+{f / 60}s " + State() + " crowds [" + string.Join(",", civs.Select(e => cgf.IsAlive(e) ? Mind(e).CrowdId : -1)) + "]"
                           + " x [" + string.Join(",", civs.Select(e => cgf.IsAlive(e) ? (int)cgf.GetComponent<SimTransform>(e).Position.X : -1)) + "]");
        }

        var deaf = civs.Where(e => !heard.Contains(e)).ToArray();
        Assert.True(heard.Count >= 5, $"① the near ones hear it ({heard.Count} of 15); {State()}");
        Assert.True(deaf.Length >= 4, $"⓪ the far ones (100+ m from the fire) hear nothing ({deaf.Length} never heard); {State()}");
        int caught = deaf.Count(panicked.Contains);
        Assert.True(caught >= 3, $"② at least three who heard nothing panic, from the crowd ({caught}); {State()}");
    }

    /// <summary>
    /// ⭐ <c>AW-20</c> (R-2026) — <c>ua-armed-shield</c> in-process (the live check's twin; 📄 docs/DESIGN_Civilians_And_Crowd_Control.md
    /// §5c, §5h): a rifleman 60 m from an insurgent with four civilians standing in the line between them and two beside him. Sampled every
    /// frame on CGF: ① he holds with <c>HoldNeutralOnLine</c> at least once, and every round he spends leaves with no living civilian's body
    /// on the line (the plain geometry, no spread — a check the rule's own code does not share); ② the insurgent goes down; ③ no civilian is
    /// hurt.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task AW20_ArmedShield_NoRoundWhileACivilianIsInTheLine_TheInsurgentStillGoesDown_NoCivilianHurt()
    {
        using var harness = await StartShippedScenario("ua-armed-shield");
        var cgf = harness.Cgf!.World!;
        var names = Enumerable.Range(1, 6).Select(i => $"Civilian {i}").ToArray();
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Rifleman").IsNull && !ByName(cgf, "Insurgent").IsNull
                                         && names.All(n => !ByName(cgf, n).IsNull), timeoutFrames: 2000), "the cast must spawn on CGF");
        var rifle = ByName(cgf, "Rifleman");
        var insurgent = ByName(cgf, "Insurgent");
        var civs = names.Select(n => ByName(cgf, n)).ToArray();
        int Ammo() => cgf.HasComponent<WeaponState>(rifle) ? cgf.GetComponent<WeaponState>(rifle).Ammo : -1;
        float Hp(Entity e) => cgf.IsAlive(e) && cgf.HasComponent<Health>(e) ? cgf.GetComponent<Health>(e).Current : -1f;
        bool CivilianOnTheLine()
        {
            var a = cgf.GetComponent<SimTransform>(rifle).Position;
            var b = cgf.GetComponent<SimTransform>(insurgent).Position;
            var ab = new Vector2(b.X - a.X, b.Y - a.Y);
            foreach (var c in civs)
            {
                if (!cgf.IsAlive(c) || Hp(c) <= 0f) continue;
                var p = cgf.GetComponent<SimTransform>(c).Position;
                var ap = new Vector2(p.X - a.X, p.Y - a.Y);
                float t = Vector2.Dot(ap, ab) / ab.LengthSquared();
                if (t <= 0f || t >= 1f) continue;
                float r = cgf.HasComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>(c)
                    ? cgf.GetComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>(c).Radius : 0.3f;
                if (Vector2.Distance(ab * t, ap) < r) return true;
            }
            return false;
        }

        int held = 0, rounds = 0, throughCivilian = 0, onLineFrames = 0, frame = 0;
        var reasons = new System.Collections.Generic.Dictionary<ActionReason, int>();
        var hurt = new System.Collections.Generic.HashSet<string>();
        int last = Ammo();
        bool onLineBefore = CivilianOnTheLine();
        void Sample()
        {
            frame++;
            if (cgf.HasComponent<ActionStatus>(rifle))
            {
                var why = cgf.GetComponent<ActionStatus>(rifle).Weapon.Reason;
                if (onLineBefore) reasons[why] = reasons.TryGetValue(why, out int k) ? k + 1 : 1;
                if (why == ActionReason.HoldNeutralOnLine) held++;
            }
            if (onLineBefore) onLineFrames++;
            if (frame % 120 == 0)
                _out.WriteLine($"f{frame} onLine={onLineBefore} reason={(cgf.HasComponent<ActionStatus>(rifle) ? cgf.GetComponent<ActionStatus>(rifle).Weapon.Reason : ActionReason.None)} ammo={Ammo()} "
                    + string.Join(" ", civs.Select(c => cgf.IsAlive(c) ? $"({cgf.GetComponent<SimTransform>(c).Position.X:F0},{cgf.GetComponent<SimTransform>(c).Position.Y:F1})" : "-")));
            int now = Ammo();
            if (now >= 0 && last >= 0 && now < last)
            {
                rounds += last - now;
                if (onLineBefore) throughCivilian++;   // the round left on a frame that began with a civilian in the line
            }
            last = now;
            for (int i = 0; i < civs.Length; i++)
                if (Hp(civs[i]) is var h && h >= 0f && h < 100f) hurt.Add(names[i]);
            onLineBefore = cgf.IsAlive(insurgent) && CivilianOnTheLine();
        }
        string State() => $"held {held} frames, rounds {rounds}, through a civilian {throughCivilian}, on the line {onLineFrames} frames "
                          + $"(his reasons then: {string.Join(",", reasons.Select(kv => $"{kv.Key}={kv.Value}"))}), insurgent hp {Hp(insurgent)}, "
                          + $"hurt [{string.Join(",", hurt)}], civilians hp [{string.Join(",", civs.Select(Hp))}]";

        bool down = harness.PumpUntil(() => { Sample(); return Hp(insurgent) <= 0f; }, timeoutFrames: 9000);   // 150 s
        harness.PumpUntil(() => { Sample(); return false; }, timeoutFrames: 300);
        _out.WriteLine(State());
        Assert.True(held > 0, $"① he holds with a civilian in the line at least once; {State()}");
        Assert.True(throughCivilian == 0, $"① no round leaves while a civilian stands in the line; {State()}");
        Assert.True(down, $"② the insurgent is engaged and goes down; {State()}");
        Assert.Empty(hurt);   // ③
    }

    /// <summary>
    /// ⭐ <c>CE-2240</c> CIV-3 — <c>ua-civ-protest</c> in-process (the live check's twin; 📄 docs/DESIGN_Civilians_And_Crowd_Control.md
    /// §5c, §5g): twelve civilians told to protest in front of a test-town building reach <c>Protest</c>, stand as one crowd round the
    /// point, and show it on CGF, SimHost and IG — at least three fists raised and three signs held on each node; in 120 s nobody turns
    /// Hostile and nobody riots.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task CE2240_CivProtest_TheCrowdProtests_ItsGesturesShowOnEveryNode_NobodyRiots()
    {
        using var harness = await StartShippedScenario("ua-civ-protest");
        var cgf = harness.Cgf!.World!;
        var sim = harness.SimHost.World!;
        var ig = harness.Ig.App.World;
        var names = Enumerable.Range(1, 12).Select(i => $"Protester {i}").ToArray();
        Assert.True(harness.PumpUntil(() => names.All(n => !ByName(cgf, n).IsNull && !ByName(sim, n).IsNull && !ByName(ig, n).IsNull), timeoutFrames: 2000),
            "the cast must spawn on CGF, SimHost and IG");
        var civs = names.Select(n => ByName(cgf, n)).ToArray();
        Fdp.Toolkit.Civilians.CivilianMode Mode(Entity e) => Fdp.Toolkit.Civilians.CivilianMind.ModeOf(cgf, e);
        var hosts = new[] { ("CGF", cgf), ("SimHost", sim), ("IG", ig) };
        var fists = hosts.ToDictionary(h => h.Item1, _ => new System.Collections.Generic.HashSet<string>());
        var signs = hosts.ToDictionary(h => h.Item1, _ => new System.Collections.Generic.HashSet<string>());
        var bad = new System.Collections.Generic.HashSet<string>();
        void Sample()
        {
            foreach (var (host, w) in hosts)
                foreach (var n in names)
                {
                    var e = ByName(w, n);
                    if (e.IsNull) continue;
                    var g = Fdp.Toolkit.Combat.Gestures.Of(w, e);
                    if (g == Fdp.Toolkit.Combat.GestureKind.FistRaised) fists[host].Add(n);
                    if (g == Fdp.Toolkit.Combat.GestureKind.HoldSign) signs[host].Add(n);
                }
            foreach (var e in civs)
            {
                if (Mode(e) == Fdp.Toolkit.Civilians.CivilianMode.Riot) bad.Add($"riot {e}");
                if (cgf.GetComponentRO<EntityInfo>(e).ForceId != ForceId.Neutral) bad.Add($"side {e}");
            }
        }
        string State() => "[" + string.Join(", ", civs.Select(Mode)) + "] " + string.Join(" ", hosts.Select(h => $"{h.Item1}: fist {fists[h.Item1].Count} sign {signs[h.Item1].Count}"));

        Assert.True(harness.PumpUntil(() => { Sample(); return civs.All(e => Mode(e) == Fdp.Toolkit.Civilians.CivilianMode.Protest); }, timeoutFrames: 1800),
            $"① the crowd reaches Protest; {State()}");
        harness.PumpUntil(() => { Sample(); return false; }, timeoutFrames: 7200);   // 120 s
        _out.WriteLine(State());
        var ids = civs.Select(e => cgf.GetComponent<Fdp.Toolkit.Civilians.CivilianState>(e).CrowdId).ToArray();
        int best = ids.Where(i => i != 0).GroupBy(i => i).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        Assert.True(best != 0 && ids.Count(i => i == best) >= 10, $"① …standing as one crowd round the point [{string.Join(",", ids)}]");
        foreach (var (host, _) in hosts)
            Assert.True(fists[host].Count >= 3 && signs[host].Count >= 3, $"② on {host} ≥ 3 fists raised and ≥ 3 signs held; {State()}");
        Assert.Empty(bad);   // ③ nobody turns Hostile, nobody riots
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

    /// <summary>
    /// ⭐ <c>CE-3082</c> / <c>CE-3083</c> — U3 <c>ua-three-hosts</c> in-process (the live check's twin, R-197): the same Health edits ⇒
    /// the same fight / defend class at every step. ⭐ <c>CE-3160</c> (R-266): the HSM and blueprint hosts are retired — the three
    /// riflemen all run the ONE C#-built <c>CombatPosture</c> (Decision Layer §3.3h). 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §4 U3.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task CE3082_U3_OneDecision_ThreeHosts_TheSameChoices()
    {
        using var harness = await StartShippedScenario("ua-three-hosts");
        var cgf = harness.Cgf!.World!;
        string[] names = { "Rifleman A", "Rifleman B", "Rifleman C" };
        Assert.True(harness.PumpUntil(() => names.All(n => !ByName(cgf, n).IsNull), timeoutFrames: 2000), "the three riflemen must spawn on CGF");
        var units = names.Select(n => ByName(cgf, n)).ToArray();
        foreach (var u in units) Observe(cgf, u);

        Posture? Winner(Entity u) => TryDecision(cgf, u, "Combat posture", out var s) && s.Winner != 0 ? (Posture)s.Winner : null;
        string State() => string.Join(", ", names.Select((n, i) => $"{n}={Winner(units[i])}"));

        Assert.True(harness.PumpUntil(() => units.All(u => Winner(u) == Posture.Suppress), timeoutFrames: 6000), $"every host at Suppress; {State()}");
        Posture[] fight = { Posture.Suppress, Posture.AdvanceAndAttack }, defend = { Posture.TakeCover, Posture.Flee };
        foreach (var (hp, want, what) in new[] { (40f, defend, "hurt"), (100f, fight, "healed"), (10f, defend, "near death"), (100f, fight, "healed again") })
        {
            foreach (var u in units) cgf.GetComponentRW<Health>(u).Current = hp;
            Assert.True(harness.PumpUntil(() => units.All(u => Winner(u) is { } w && Array.IndexOf(want, w) >= 0), timeoutFrames: 4000),
                $"{what} ({hp} HP) ⇒ every host in the same class; {State()}");
            _out.WriteLine($"{what}: {State()}");
        }
    }

    /// <summary>
    /// ⭐ <c>CE-3084</c> — U4 <c>ua-attack-approach</c> in-process: the hostile shows itself north of the High Wall and walks behind it;
    /// the advancing rifleman, out of sight of an IDENTIFIED target, takes a Flank or a FiringPosition approach and fires from there.
    /// 📄 docs/DESIGN_Utility_AI_Demo_Scenarios.md §4 U4; Decision Layer §3.3e.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task CE3084_U4_OutOfSight_TheAdvanceFlanks_AndFiresFromThere()
    {
        using var harness = await StartShippedScenario("ua-attack-approach");
        var cgf = harness.Cgf!.World!;
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Rifleman").IsNull, timeoutFrames: 2000), "the rifleman must spawn on CGF");
        var rifleman = ByName(cgf, "Rifleman");
        Observe(cgf, rifleman);
        Approach? ApproachWinner() => TryDecision(cgf, rifleman, "Attack approach", out var s) && s.Winner != 0 ? (Approach)s.Winner : null;
        int Ammo() => cgf.GetComponent<WeaponState>(rifleman).Ammo;
        string Hostile()
        {
            var h = ByName(cgf, "Hidden Hostile");
            if (h.IsNull) return "hostile not on CGF";
            string hp = cgf.HasComponent<Health>(h) ? $"{cgf.GetComponent<Health>(h).Current}" : "?";
            string pos = cgf.HasComponent<SimTransform>(h) ? $"{cgf.GetComponent<SimTransform>(h).Position}" : "?";
            string caps = cgf.HasComponent<ActorCapabilityState>(h) ? $"{cgf.GetComponent<ActorCapabilityState>(h).Capabilities}" : "?";
            return $"hostile hp={hp} pos={pos} caps={caps}; rifleman ammo={Ammo()}";
        }

        Assert.True(harness.PumpUntil(() => ApproachWinner() is Approach.Flank or Approach.FiringPosition, timeoutFrames: 12000),
            $"out of sight ⇒ Flank or FiringPosition; approach {ApproachWinner()}; {Hostile()}");
        int ammo = Ammo();
        _out.WriteLine($"approach {ApproachWinner()}, ammo {ammo}");
        Assert.True(harness.PumpUntil(() => Ammo() < ammo, timeoutFrames: 12000), $"…and fires from there; ammo {Ammo()} (was {ammo})");
    }

    /// <summary>House A's window firing positions, both storeys (the cover database, `bt-range`; measured by the P-8 probe).</summary>
    private static readonly Vector3[] HouseAWindows =
    {
        new(102.1f, 100.9f, 0f), new(109.1f, 103.6f, 0f), new(102.1f, 100.9f, 3f), new(107.1f, 100.9f, 3f), new(105.4f, 107.1f, 3f),
    };

    /// <summary>
    /// ⭐⭐ <c>CE-3136</c> P-8 (D10) — <c>bt-window-duel</c> in-process (the live check's twin): A at an upstairs window of House A,
    /// B in the street with two parked vans for cover, each remembering the other. Both fire (aimed or a blind burst — the ammunition
    /// falls), A fires from at least two windows (either storey) (the exposure count moves it on), and B bounds to Van 2's cover.
    /// 📄 docs/DESIGN_Peek_And_Fire.md §2, D10, D14–D15 (CE-3144: the duel stood still before them).
    /// </summary>
    [Fact(Timeout = 900_000)]
    public async Task CE3136_P8_WindowDuel_BothFire_ARotatesWindows_BBoundsToTheSecondVan()
    {
        using var harness = await StartShippedScenario("bt-window-duel");
        var cgf = harness.Cgf!.World!;
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Window Rifleman").IsNull && !ByName(cgf, "Street Rifleman").IsNull, timeoutFrames: 2000),
            "both riflemen must spawn on CGF");
        Entity a = ByName(cgf, "Window Rifleman"), b = ByName(cgf, "Street Rifleman");
        int Ammo(Entity e) => cgf.HasComponent<WeaponState>(e) ? cgf.GetComponent<WeaponState>(e).Ammo : -1;
        Vector3 Pos(Entity e) => cgf.HasComponent<SimTransform>(e) ? cgf.GetComponent<SimTransform>(e).Position : default;
        int a0 = Ammo(a), b0 = Ammo(b);
        var windows = new System.Collections.Generic.HashSet<(int, int, int)>();
        var van2 = new Vector2(113f, 76f);
        bool bounded = false;
        float Hp(Entity e) => cgf.HasComponent<Health>(e) ? cgf.GetComponent<Health>(e).Current : -1f;
        unsafe string Peek(Entity e)
        {
            if (!cgf.IsAlive(e)) return "dead";
            var m = Fdp.Toolkit.Behavior.UnitMemory.Get<Fdp.Toolkit.Combat.FiringPositionMemory>(cgf, e);
            var slots = "";
            for (int i = 0; i < m.Count; i++) slots += $"({m.X[i]:F1},{m.Y[i]:F1},{m.Z[i]:F0})u{m.Uses[i]}h{m.Heat[i]:F1}{(m.Burned[i] != 0 ? "B" : "")} ";
            var answers = "";
            var sensor = EqsChildSensor.Find(cgf, e, Hrot.AI.Behaviors.Brains.PeekAndFireNodes.CoverSite);
            if (!sensor.IsNull && cgf.HasComponent<EqsCognitiveBuffer>(sensor))
                foreach (var r in cgf.GetComponentRO<EqsCognitiveBuffer>(sensor).GetSpanRO())
                    answers += $"({r.PositionX:F1},{r.PositionY:F1},{r.PositionZ:F0}){r.Score:F2} ";
            var face = cgf.HasComponent<SimTransform>(e) ? Vector3.Transform(Vector3.UnitX, cgf.GetComponent<SimTransform>(e).Rotation) : default;
            return $"phase={(Fdp.Toolkit.Combat.PeekPhase)m.Phase} hide={m.HidePoint} facing=({face.X:F2},{face.Y:F2}) slots[{slots}] cover[{answers}]";
        }
        string State() => $"A {Pos(a)} hp {Hp(a)} ammo {a0}→{Ammo(a)} windows [{string.Join(" ", windows)}] {Peek(a)} · B {Pos(b)} hp {Hp(b)} ammo {b0}→{Ammo(b)} bounded={bounded}";

        float lastZ = Pos(a).Z;
        for (int f = 0; f < 36000; f++)
        {
            harness.PumpFrames(1);
            var pa = Pos(a);
            if (f % 30 == 0 && f > 2000 && f < 3000) _out.WriteLine($"f{f}: A {pa} {Peek(a)}");
            if (MathF.Abs(pa.Z - lastZ) > 0.5f) { _out.WriteLine($"f{f}: A z {lastZ:F2} -> {pa.Z:F2} at {pa}; {Peek(a)}"); lastZ = pa.Z; }
            foreach (var w in HouseAWindows)   // at a window (either storey), not on the way between two
                if (MathF.Abs(pa.Z - w.Z) < 1f && Vector2.Distance(new Vector2(pa.X, pa.Y), new Vector2(w.X, w.Y)) <= 0.75f)
                    windows.Add(((int)MathF.Round(w.X), (int)MathF.Round(w.Y), (int)w.Z));
            var pb = Pos(b);
            bounded |= Vector2.Distance(new Vector2(pb.X, pb.Y), van2) <= 4f;
            if (f % 600 == 0) _out.WriteLine($"f{f}: {State()}");
            if (Ammo(a) < a0 && Ammo(b) < b0 && windows.Count >= 2 && bounded) break;
        }
        _out.WriteLine($"end: {State()}");
        Assert.True(Ammo(a) < a0, $"A fires; {State()}");
        Assert.True(Ammo(b) < b0, $"B fires; {State()}");
        Assert.True(windows.Count >= 2, $"A fires from at least two windows; {State()}");
        Assert.True(bounded, $"B bounds to Van 2's cover; {State()}");
    }

    /// <summary>
    /// ⭐ <c>CE-3094</c> — the "universal soldier" (<c>ua-universal-soldier</c>): a rifleman on a two-leg MISSION of
    /// <c>CombatPosture</c> along Main Street (ROE FireAtWill + StayOnTask, SOP <c>BasicInfantrySop</c>) meets a GROUP of three
    /// armed hostiles advancing on him. 📄 <c>docs/TUTORIAL_Universal_Soldier.md</c> · <c>docs/TUTORIAL_Behaviour_Composition.md</c> §8.
    /// </summary>
    [Fact(Timeout = 900_000)]
    public async Task CE3094_UniversalSoldier_MissionOfPostureLegs_AgainstAGroup()
    {
        using var harness = await StartShippedScenario("ua-universal-soldier");
        var cgf = harness.Cgf!.World!;
        string[] hostileNames = { "Hostile 1", "Hostile 2", "Hostile 3" };
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Rifleman").IsNull && hostileNames.All(n => !ByName(cgf, n).IsNull), timeoutFrames: 2000),
            "the rifleman and the three hostiles must spawn on CGF");
        var rifleman = ByName(cgf, "Rifleman");
        Observe(cgf, rifleman);

        Posture? Winner() => TryDecision(cgf, rifleman, "Combat posture", out var s) && s.Winner != 0 ? (Posture)s.Winner : null;
        Approach? ApproachWinner() => TryDecision(cgf, rifleman, "Attack approach", out var s) && s.Winner != 0 ? (Approach)s.Winner : null;
        float Hp(Entity e) => !e.IsNull && cgf.IsAlive(e) && cgf.HasComponent<Health>(e) ? cgf.GetComponent<Health>(e).Current : 0f;
        Vector3 Pos(Entity e) => !e.IsNull && cgf.IsAlive(e) && cgf.HasComponent<SimTransform>(e) ? cgf.GetComponent<SimTransform>(e).Position : default;
        int Leg() => cgf.HasComponent<MissionPlanQueue>(rifleman) ? cgf.GetComponent<MissionPlanQueue>(rifleman).CurrentPhase : -1;
        int HostilesUp() => hostileNames.Count(n => Hp(ByName(cgf, n)) > 0f);
        string State()
        {
            string ammo = cgf.HasComponent<WeaponState>(rifleman) ? $"{cgf.GetComponent<WeaponState>(rifleman).Ammo}" : "?";
            string hostiles = string.Join(" ", hostileNames.Select(n => { var h = ByName(cgf, n); return $"{n[^1]}:{Hp(h):F0}@{Pos(h).X:F0},{Pos(h).Y:F0}"; }));
            return $"leg={Leg()} posture={Winner()} approach={ApproachWinner()} pos={Pos(rifleman).X:F0},{Pos(rifleman).Y:F0} hp={Hp(rifleman):F0} ammo={ammo} | {hostiles}";
        }

        var postures = new System.Collections.Generic.List<Posture?>();
        var approaches = new System.Collections.Generic.List<Approach?>();
        var final = new Vector3(200, 330, 0);
        // 📐 the exchange, measured where the bullets live: per shooter, every bullet's spawn and last-seen position.
        var shotWorlds = new (string Name, EntityRepository World)[] { ("simhost", harness.SimHost.World!), ("cgf", cgf) };
        var bullets = new System.Collections.Generic.Dictionary<(string, int, ushort), (string Shooter, Vector3 Spawn, Vector3 Last, int Frame)>();
        string ShooterName(EntityRepository w, Entity e)
        {
            if (e.IsNull || !w.IsAlive(e)) return "?";
            if (w.HasComponent<EntityInfo>(e)) return w.GetComponent<EntityInfo>(e).Name.ToString();
            return w.HasComponent<NetworkIdentity>(e) ? $"net{w.GetComponent<NetworkIdentity>(e).Value}" : $"e{e.Index}";
        }
        void TrackBullets(int frame)
        {
            foreach (var (wn, w) in shotWorlds)
            {
                if (!w.IsComponentTypeRegistered<BallisticProjectile>()) continue;
                foreach (var b in w.Query().With<BallisticProjectile>().With<SimTransform>().Build())
                {
                    var pos = w.GetComponent<SimTransform>(b).Position;
                    var key = (wn, b.Index, b.Generation);
                    if (bullets.TryGetValue(key, out var seen)) bullets[key] = seen with { Last = pos };
                    else bullets[key] = (ShooterName(w, w.GetComponent<BallisticProjectile>(b).Shooter), pos, pos, frame);
                }
            }
        }
        // ⭐ CE-3097 / CE-3159 probe — per 150 frames: sim time advanced on SimHost, the unit's REAL speed (displacement / sim time),
        //   the speed the brain ASKED for (CGF NavigationIntent.TargetSpeed), and the mover's own state on SimHost.
        var lastMotion = new System.Collections.Generic.Dictionary<string, (Vector3 Pos, double T)>();
        void Motion(int f, string name)
        {
            var ce = ByName(cgf, name);
            if (ce.IsNull || harness.Cgf!.GhostEntityMap is not { } gm || !gm.TryGetNetworkId(ce, out long nid)) return;
            var shw = harness.SimHost.World!;
            if (!harness.SimHost.App.TestHook_EntityMap.TryGetEntity(nid, out var se) || !shw.IsAlive(se)) return;
            double t = shw.HasSingleton<GlobalTime>() ? shw.GetSingleton<GlobalTime>().TotalTime : 0d;
            float dt = shw.HasSingleton<GlobalTime>() ? shw.GetSingleton<GlobalTime>().DeltaTime : 0f;
            var pos = shw.HasComponent<SimTransform>(se) ? shw.GetComponent<SimTransform>(se).Position : default;
            string real = lastMotion.TryGetValue(name, out var last) && t > last.T
                ? $"{Vector2.Distance(new Vector2(pos.X, pos.Y), new Vector2(last.Pos.X, last.Pos.Y)) / (t - last.T):F2} m/s over {t - last.T:F2} s"
                : "-";
            lastMotion[name] = (pos, t);
            string asked = cgf.IsComponentTypeRegistered<Fdp.Toolkit.Navigation.NavigationIntent>() && cgf.HasComponent<Fdp.Toolkit.Navigation.NavigationIntent>(ce)
                ? $"{cgf.GetComponent<Fdp.Toolkit.Navigation.NavigationIntent>(ce).Mode}@{cgf.GetComponent<Fdp.Toolkit.Navigation.NavigationIntent>(ce).TargetSpeed:F2}" : "none";
            string veh = shw.IsComponentTypeRegistered<CarKinem.Core.VehicleState>() && shw.HasComponent<CarKinem.Core.VehicleState>(se)
                ? $"veh speed={shw.GetComponent<CarKinem.Core.VehicleState>(se).Speed:F2} accel={shw.GetComponent<CarKinem.Core.VehicleState>(se).Accel:F2}" : "no VehicleState";
            string nav = shw.IsComponentTypeRegistered<CarKinem.Core.NavState>() && shw.HasComponent<CarKinem.Core.NavState>(se)
                ? $"nav {shw.GetComponent<CarKinem.Core.NavState>(se).Mode} target={shw.GetComponent<CarKinem.Core.NavState>(se).TargetSpeed:F2} arrived={shw.GetComponent<CarKinem.Core.NavState>(se).HasArrived} blocked={shw.GetComponent<CarKinem.Core.NavState>(se).IsBlocked}" : "no NavState";
            string crowd = shw.IsComponentTypeRegistered<Fdp.Toolkit.Navigation.CrowdAgent>() && shw.HasComponent<Fdp.Toolkit.Navigation.CrowdAgent>(se) ? "crowd" : "no crowd";
            string vel = shw.HasComponent<SimVelocity>(se) ? $"{shw.GetComponent<SimVelocity>(se).Linear.Length():F2}" : "-";
            _out.WriteLine($"   motion {name}: simT={t:F2} dt={dt:F4} real={real} asked={asked} | {veh} | {nav} | {crowd} | |vel|={vel}");
        }
        // ⭐ CE-3159 probe — the fight-from-cover node's phase, points and stance, logged on every phase change (its unit memory mirror).
        byte lastPhase = 255;
        void PeekProbe(int f)
        {
            if (!cgf.IsAlive(rifleman)) return;
            var m = Fdp.Toolkit.Behavior.UnitMemory.Get<Fdp.Toolkit.Combat.FiringPositionMemory>(cgf, rifleman);
            if (m.Phase == lastPhase) return;
            lastPhase = m.Phase;
            string stance = cgf.IsComponentTypeRegistered<Hrot.MuscleCharacter.Animation.Components.StanceIntent>() && cgf.HasComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(rifleman)
                ? cgf.GetComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(rifleman).TargetStance.ToString() : "-";
            _out.WriteLine($"   peek f{f}: phase={(Fdp.Toolkit.Combat.PeekPhase)m.Phase} hide={m.HidePoint.X:F1},{m.HidePoint.Y:F1},{m.HidePoint.Z:F1} peek={m.PeekPoint.X:F1},{m.PeekPoint.Y:F1} until={m.PhaseUntil:F1} stance={stance} pos={Pos(rifleman).X:F1},{Pos(rifleman).Y:F1} posture={Winner()}");
        }
        int maxLeg = -1, firstHitFrame = -1; Vector3 posAtFirstHit = default; float movedAfterHit = 0f; bool defensiveAfterHit = false;
        // ⭐ CE-2155 (R-2011 I6) — CE-3092's "keeps moving" is a claim about a WOUNDED man (hurt, still in the fight); once incapacitated he
        //   is MEANT to stop. Measured per window, on CGF sim time, so a burst that puts him down within a second does not read as a freeze.
        double SimT() => cgf.HasSingleton<GlobalTime>() ? cgf.GetSingleton<GlobalTime>().TotalTime : 0d;
        double woundedSince = -1d, woundedSeconds = 0d; Vector3 posAtWound = default; float movedWhileWounded = 0f;
        Vector3 posAtDown = default; bool wentDown = false, settled = false; double downAt = 0d; float movedWhileDown = 0f, stopSlide = 0f;
        int firedFromCover = 0;   // ⭐ CE-3158 G-6 / CE-3157 — rounds he fires while his posture is TakeCover, after the first hit
        int lastAmmo = cgf.HasComponent<WeaponState>(rifleman) ? cgf.GetComponent<WeaponState>(rifleman).Ammo : -1;   // ⭐ CE-3136 P-5: the spawned load, not a literal 30
        for (int f = 0; f < 24000; f++)
        {
            harness.PumpFrames(1);
            TrackBullets(f);
            int ammoNow = cgf.HasComponent<WeaponState>(rifleman) ? cgf.GetComponent<WeaponState>(rifleman).Ammo : -1;
            if (ammoNow != lastAmmo)
            {
                if (firstHitFrame >= 0 && ammoNow < lastAmmo && Winner() is Posture.TakeCover) firedFromCover += lastAmmo - ammoNow;
                var t = FireTarget(cgf, rifleman);
                string tn = ShooterName(cgf, t);
                string tm = harness.Cgf!.GhostEntityMap is { } m && m.TryGetNetworkId(t, out long tid) ? $"net{tid}" : "UNMAPPED";
                _out.WriteLine($"f{f}: rifleman ammo {lastAmmo}→{ammoNow}, fire target e{t.Index}:{t.Generation} '{tn}' {tm} alive={cgf.IsAlive(t)}");
                var sh = harness.SimHost.World!; var shMap = harness.SimHost.App.TestHook_EntityMap;
                string Sh(long net) => shMap.TryGetEntity(net, out var se) ? $"e{se.Index} alive={sh.IsAlive(se)} weapon={(sh.IsAlive(se) && sh.HasComponent<WeaponState>(se))} xf={(sh.IsAlive(se) && sh.HasComponent<SimTransform>(se))}" : "UNMAPPED";
                long shooterNet = harness.Cgf!.GhostEntityMap!.TryGetNetworkId(rifleman, out long rn) ? rn : -1;
                _out.WriteLine($"   simhost: shooter net{shooterNet} {Sh(shooterNet)} · target {tm} {(tm.StartsWith("net") ? Sh(long.Parse(tm[3..])) : "-")}");
                lastAmmo = ammoNow;
            }
            if (Winner() is { } w && (postures.Count == 0 || postures[^1] != w)) postures.Add(w);
            if (ApproachWinner() is { } a && (approaches.Count == 0 || approaches[^1] != a)) approaches.Add(a);
            maxLeg = Math.Max(maxLeg, Leg());
            if (firstHitFrame < 0 && Hp(rifleman) is > 0f and < 100f) { firstHitFrame = f; posAtFirstHit = Pos(rifleman); }
            if (firstHitFrame >= 0 && Hp(rifleman) > 0f)
            {
                movedAfterHit = MathF.Max(movedAfterHit, Vector3.Distance(Pos(rifleman), posAtFirstHit));
                if (Winner() is Posture.TakeCover or Posture.Flee or Posture.HoldProne) defensiveAfterHit = true;
                if (Injury.IsInTheFight(cgf, rifleman))
                {
                    if (woundedSince < 0d) { woundedSince = SimT(); posAtWound = Pos(rifleman); }
                    woundedSeconds = SimT() - woundedSince;
                    movedWhileWounded = MathF.Max(movedWhileWounded, Vector3.Distance(Pos(rifleman), posAtWound));
                }
                else if (Injury.IsIncapacitated(cgf, rifleman))
                {
                    if (!wentDown) { wentDown = true; downAt = SimT(); posAtDown = Pos(rifleman); _out.WriteLine($"f{f}: RIFLEMAN INCAPACITATED — {State()} after {woundedSeconds:F1} s wounded, moved {movedWhileWounded:F1} m"); }
                    // the stop crosses to SimHost and he decelerates: measured 0.5 m of slide — the claim starts 1 s after he fell
                    if (!settled && SimT() - downAt >= 1.0) { settled = true; stopSlide = Vector3.Distance(Pos(rifleman), posAtDown); posAtDown = Pos(rifleman); }
                    if (settled) movedWhileDown = MathF.Max(movedWhileDown, Vector3.Distance(Pos(rifleman), posAtDown));
                }
            }
            if (f % 150 == 0) { _out.WriteLine($"f{f}: {State()}"); Motion(f, "Rifleman"); Motion(f, "Hostile 1"); }
            PeekProbe(f);
            if (Hp(rifleman) <= 0f) { _out.WriteLine($"f{f}: RIFLEMAN DOWN — {State()}"); break; }
            if (Leg() >= 1 && Vector3.Distance(Pos(rifleman), final) <= 3.5f) { _out.WriteLine($"f{f}: FINAL OBJECTIVE — {State()}"); break; }
        }
        _out.WriteLine($"postures: {string.Join(" → ", postures)}");
        _out.WriteLine($"CE-3157: rounds fired from cover after the first hit = {firedFromCover}");
        _out.WriteLine($"approaches: {string.Join(" → ", approaches)}");
        _out.WriteLine($"end: {State()} hostiles up={HostilesUp()}");
        var cgfMap = harness.Cgf!.GhostEntityMap;
        foreach (var n in hostileNames.Prepend("Rifleman"))
        {
            var e = ByName(cgf, n);
            string auth = !e.IsNull && cgf.HasComponent<NetworkAuthority>(e) ? $"{cgf.GetComponent<NetworkAuthority>(e).PrimaryOwnerId}/{cgf.GetComponent<NetworkAuthority>(e).LocalNodeId}" : "none";
            string mapped = cgfMap != null && cgfMap.TryGetNetworkId(e, out long nid) ? $"net{nid}" : "UNMAPPED";
            _out.WriteLine($"cgf {n}: e{e.Index} authority(owner/local)={auth} hasAuthority={Fdp.Toolkit.Replication.Extensions.AuthorityExtensions.HasAuthority(cgf, e)} map={mapped}");
        }
        {
            var shw = harness.SimHost.World!; var shm = harness.SimHost.App.TestHook_EntityMap;
            foreach (var n in hostileNames.Prepend("Rifleman"))
            {
                var ce = ByName(cgf, n);
                if (cgfMap == null || !cgfMap.TryGetNetworkId(ce, out long nid) || !shm.TryGetEntity(nid, out var se)) { _out.WriteLine($"simhost {n}: unmapped"); continue; }
                string col = shw.IsComponentTypeRegistered<Fdp.Toolkit.Physics.Components.PhysicsCollider>() && shw.HasComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>(se)
                    ? $"radius={shw.GetComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>(se).Radius} layer={shw.GetComponent<Fdp.Toolkit.Physics.Components.PhysicsCollider>(se).CollisionLayer}" : "NO COLLIDER";
                var sp = shw.HasComponent<SimTransform>(se) ? shw.GetComponent<SimTransform>(se).Position : default;
                _out.WriteLine($"simhost {n}: e{se.Index} pos={sp.X:F1},{sp.Y:F1},{sp.Z:F1} collider {col}");
            }
        }
        foreach (var g in bullets.GroupBy(kv => (kv.Key.Item1, kv.Value.Shooter)))
            _out.WriteLine($"shots[{g.Key.Item1}] {g.Key.Shooter}: {g.Count()} — " +
                string.Join(" ", g.Select(kv => $"f{kv.Value.Frame}:{kv.Value.Spawn.X:F0},{kv.Value.Spawn.Y:F0},{kv.Value.Spawn.Z:F1}→{kv.Value.Last.X:F0},{kv.Value.Last.Y:F0},{kv.Value.Last.Z:F1}")));

        // ⭐ CE-3094 — locked on what the composition does today (measured 2026-10-10, after AQ85 hit chance and CE-3095):
        //   hidden until ~f5950, both legs reached, 2 rounds both on Hostile 1, wounded ⇒ Flee → TakeCover → HoldProne, then
        //   killed in cover by a second hostile closing from another bearing (CE-3157). It does NOT assert that he wins.
        //   ⭐ Re-measured 2026-10-13 under CE-2155 (band 0.3): first hit ~f4300-f4900, three rounds from two hostiles inside a second
        //   (100 → 25 HP) put him DOWN, not dead; he stops where he fell, bleeds (0.25 HP/s) and a later round kills him where he lies.
        Assert.True(firstHitFrame < 0 || firstHitFrame > 4000,
            $"the approach behind Block C must keep him unseen for most of the walk; first hit at f{firstHitFrame}");
        Assert.True(maxLeg >= 1, $"the mission must advance to its second leg (reached {maxLeg})");
        Assert.True(bullets.Any(kv => kv.Value.Shooter == "Rifleman"),
            "CE-3095: the rifleman's rounds must reach SimHost as bullets");
        Assert.True(hostileNames.Any(n => Hp(ByName(cgf, n)) < 100f), "he must hurt at least one hostile");
        if (firstHitFrame >= 0)
        {
            Assert.True(defensiveAfterHit, $"wounded, he must pick a defensive posture (postures: {string.Join(" → ", postures)})");
            _out.WriteLine($"CE-3092: wounded for {woundedSeconds:F1} s, moved {movedWhileWounded:F1} m (after the first hit {movedAfterHit:F1} m); down={wentDown}, slid {stopSlide:F2} m then moved {movedWhileDown:F2} m while down");
            // ⭐ CE-3092 — wounded and still in the fight, he keeps moving: asserted when he stayed wounded long enough to cover 2 m
            //   (3 s of sim time — the posture switch, the path request and the walk). ⛔ Was "≥ 2 m after the first hit", which read a
            //   man put DOWN by a burst (CE-2155: three rifle hits in a second, 100 → 25 HP) as a frozen one.
            if (woundedSeconds >= 3.0)
                Assert.True(movedWhileWounded >= 2f, $"CE-3092: a wounded rifleman keeps moving (moved {movedWhileWounded:F1} m in {woundedSeconds:F1} s wounded)");
            // ⚠ Not asserted: "once incapacitated he stays where he fell" (CE-2155 I6). Red-proofed and it stayed GREEN with the strip removed
            //   — something else stops him here too — so it cannot tell the strip apart. The strip's rail is InjuryTests (unit), red-proofed.
        }
    }
}
