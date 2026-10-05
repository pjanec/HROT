using System;
using System.Linq;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.Common;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using Xunit.Abstractions;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐ <c>CE-2094</c> — the shipped <c>tt-take-cover</c> scenario on <c>test-town</c>, live: a rifleman in the open whose SOP
/// is <c>BasicInfantrySop</c> sees a hostile, the SOP reacts with the REAL <c>TakeCover</c> tree (D6), and the rifleman
/// ends where the terrain hides it from the hostile — checked with the terrain sight, as
/// <c>EqsDistributedTests.FindCoverFromTarget_OverTheTerrain_*</c> does. 📄 <c>docs/DESIGN_Eqs_Consuming_Behaviours.md</c> §4 D5.
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class TakeCoverScenarioTests : IDisposable
{
    private const int DomainBase = 208;   // CycloneDDS accepts 0–232; 208 is used by no other rail (grep of fixed ids, DomainBase and counter starts, 2026-10-05)
    private static int _domainSeq = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    private readonly string _scenarioId = "tt_take_cover_" + Guid.NewGuid().ToString("N");
    private readonly ITestOutputHelper _out;

    public TakeCoverScenarioTests(ITestOutputHelper output) => _out = output;

    public void Dispose() => NasScenarioStaging.Remove(_scenarioId);

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "Hrot", "Subsystems"))) return dir.FullName;
        throw new DirectoryNotFoundException("repo root not found above " + AppContext.BaseDirectory);
    }

    private static string Recipe(string root)
        => Path.Combine(root, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Scenarios", "tt-take-cover", "scenario.json");

    /// <summary>By name, as <c>SopDemoScenarioTests</c> — a load assigns network ids, so the file's are not the runtime's.</summary>
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

    /// <summary>The chain, link by link, for a failure message: sensor → answer → MoveTo → NavigationIntent → motion.</summary>
    private static string Diagnose(EntityRepository cgf, EntityRepository sim, Entity rifleman, Entity simRifleman, int site, uint template)
    {
        var sb = new System.Text.StringBuilder();
        var sensor = EqsChildSensor.Find(cgf, rifleman, site);
        sb.Append($"sensor={(sensor.IsNull ? "none" : sensor.ToString())}");
        if (!sensor.IsNull)
        {
            var cfg = cgf.GetComponentRO<EqsSensor>(sensor);
            sb.Append($" bp={cfg.BlueprintId:X8} epoch={cfg.Epoch:X8} slot1={cfg.ContextSlot1}");
            if (cgf.HasComponent<EqsCognitiveBuffer>(sensor))
            {
                ref readonly var b = ref cgf.GetComponentRO<EqsCognitiveBuffer>(sensor);
                sb.Append($" ready={b.IsReady} count={b.Count} tick={b.LastUpdateTick}");
                if (b.Count > 0) sb.Append($" top=({b.GetTop().PositionX:F1},{b.GetTop().PositionY:F1})");
            }
        }
        if (cgf.HasComponent<LocomotionChannel>(rifleman))
        {
            var ch = cgf.GetComponent<LocomotionChannel>(rifleman);
            sb.Append($" | cgf loco action={ch.ActiveAction} status={ch.Status} inst={ch.ActionInstanceId}");
        }
        foreach (var (w, e, tag) in new[] { (cgf, rifleman, "cgf"), (sim, simRifleman, "sim") })
            if (w.HasComponent<Fdp.Toolkit.Navigation.NavigationIntent>(e))
            {
                var ni = w.GetComponent<Fdp.Toolkit.Navigation.NavigationIntent>(e);
                sb.Append($" | {tag} nav mode={ni.Mode} dest={ni.FinalDestination} id={ni.IntentId}");
            }
        // The Muscle side, stage by stage (as EqsDistributedTests.DiagnoseCover).
        foreach (var c in sim.Query().With<PartMetadata>().With<EqsSensor>().WithLifecycle(EntityLifecycle.All).Build())
        {
            var cfg = sim.GetComponent<EqsSensor>(c);
            if (cfg.BlueprintId != template) continue;
            var self = EqsContext.Self(sim, c, cfg);
            sb.Append($" | muscle carrier {c.Index} self={(self.IsNull ? "null" : sim.GetComponent<SimTransform>(self).Position.ToString())} slot1={cfg.ContextSlot1.Index} cover={sim.GetSingletonManaged<ICoverProvider>()?.GetType().Name ?? "none"}");
            if (!sim.GetSingletonManaged<IEqsTemplateRegistry>()!.TryGetTemplate(cfg.BlueprintId, out var t)) { sb.Append(" no template"); continue; }
            var buf = new EqsResult[t.MaxCandidates];
            int n = t.Generator.Generate(c, ref cfg, sim, buf);
            sb.Append($" gen={n}");
            if (n <= 0) continue;
            var span = buf.AsSpan(0, n);
            int Kept(Span<EqsResult> sp) { int k = 0; foreach (var r in sp) if (r.EntityId != -1L) k++; return k; }
            foreach (var x in t.FilterCheap ?? Array.Empty<IEqsTest>()) x.ExecuteBatch(c, ref cfg, sim, span);
            sb.Append($" afterLos={Kept(span)}");
            foreach (var x in t.ScoreCheap ?? Array.Empty<IEqsTest>()) x.ExecuteBatch(c, ref cfg, sim, span);
            foreach (var x in t.ScoreExpensive ?? Array.Empty<IEqsTest>()) { x.ExecuteBatch(c, ref cfg, sim, span); sb.Append($" after{x.GetType().Name}={Kept(span)}"); }
        }
        if (sim.HasComponent<Fdp.Toolkit.Navigation.NavigationStatus>(simRifleman))
        {
            var ns = sim.GetComponent<Fdp.Toolkit.Navigation.NavigationStatus>(simRifleman);
            sb.Append($" | sim navstatus={ns.Result} id={ns.IntentId}");
        }
        return sb.ToString();
    }

    [Fact(Timeout = 240_000)]
    public Task CE2094_ARiflemanInTheOpen_SeesTheHostile_TakesCover_AndEndsHiddenFromIt()
        => RunAsync(holdFire: false, expectedReaction: "TakeCover");

    /// <summary>D5's second variant: under HoldFire the SOP's row 2 withdraws — the real <c>FallBack</c> tree — and the
    /// rifleman must end hidden too.</summary>
    [Fact(Timeout = 240_000)]
    public Task CE2094_UnderHoldFire_TheRiflemanFallsBack_AndEndsHiddenFromTheHostile()
        => RunAsync(holdFire: true, expectedReaction: "FallBack");

    /// <summary>
    /// ⭐⭐ <c>CE-2101</c> — a SECOND live load starts from the FILE (🔒 user, 2026-10-05: a production run "is started from
    /// scenario file always, never from memory"). The first run moves the rifleman into cover; after Idle and a second
    /// live load it must be back at its authored position, exist ONCE on every host, and perceive the hostile again.
    /// 🔴 Before the fix nothing cleared the world at the load: the ids restart at 1000, the respawn was dropped as
    /// already mapped (<c>NetworkSpawningSystem</c>), and the run continued on the old entities.
    /// 📄 <c>docs/DESIGN_Deterministic_Network_Ids.md</c> §11b (the world is cleared at every load entered from Idle).
    /// </summary>
    [Fact(Timeout = 300_000)]
    public async Task CE2101_ASecondLiveLoad_StartsFromTheFile_OnEveryHost()
    {
        var root = RepoRoot();
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(_scenarioId));
        File.Copy(Recipe(root), Path.Combine(NasScenarioStaging.DirectoryOf(_scenarioId), "scenario.json"), overwrite: true);

        using var harness = new HrotRunnerHarness("simhost,ig,excon,cgf", NextDomainId());
        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;
        var rosterDeadline = DateTime.UtcNow.AddSeconds(10.0);
        while (master.NodeRoster.ActiveNodes.Count == 0 && DateTime.UtcNow < rosterDeadline)
        {
            harness.PumpFrames(1);
            Thread.Sleep(10);
        }

        async Task Transition(Hrot.NED.Descriptors.Orchestration.ClusterState target)
        {
            await master.HandleClusterOpRequestAsync(new ClusterOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = ClusterOpType.TransitionState,
                PayloadJson   = JsonSerializer.Serialize(new { TargetState = target.ToString(), ScenarioId = _scenarioId }),
            }).ConfigureAwait(false);
            Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == (int)target, timeoutFrames: 4000),
                $"cluster must reach {target}; at {(int)master.CurrentClusterState}");
        }

        var cgf = harness.Cgf!.World!;
        var sim = harness.SimHost.World!;
        int Named(EntityRepository w, string name)
        {
            int n = 0;
            for (int i = 0; i <= w.MaxEntityIndex; i++)
            {
                var e = w.GetEntityByIndex(i);
                if (w.IsAlive(e) && w.HasComponent<EntityInfo>(e) && w.GetComponent<EntityInfo>(e).Name.ToString() == name) n++;
            }
            return n;
        }
        bool Remembers() { var r = ByName(cgf, "Rifleman"); return !r.IsNull && cgf.HasComponent<TargetMemory>(r) && cgf.GetComponent<TargetMemory>(r).Count > 0; }

        static int Alive(EntityRepository w) { int n = 0; for (int i = 0; i <= w.MaxEntityIndex; i++) if (w.IsAlive(w.GetEntityByIndex(i))) n++; return n; }
        _out.WriteLine($"boot (before any load): sim={Alive(sim)} cgf={Alive(cgf)}");

        // ① the first live run: spawn, perceive, move off the authored spot.
        await Transition(Hrot.NED.Descriptors.Orchestration.ClusterState.OperatingLive);
        Assert.True(harness.PumpUntil(() => !ByName(sim, "Rifleman").IsNull && !ByName(cgf, "Rifleman").IsNull, timeoutFrames: 2000),
            "first load: the rifleman must spawn on SimHost and CGF");
        var authored = sim.GetComponent<SimTransform>(ByName(sim, "Rifleman")).Position;
        Assert.True(harness.PumpUntil(Remembers, timeoutFrames: 3000), "first load: the rifleman must perceive the hostile");
        Assert.True(harness.PumpUntil(() => Vector3.Distance(authored, sim.GetComponent<SimTransform>(ByName(sim, "Rifleman")).Position) > 2f,
            timeoutFrames: 3000), "first load: the rifleman must move off its authored spot (TakeCover)");

        // ② back to Idle, then a second live load of the same file.
        await Transition(Hrot.NED.Descriptors.Orchestration.ClusterState.Idle);
        _out.WriteLine($"at Idle after the first run: sim={Alive(sim)} cgf={Alive(cgf)}");
        await Transition(Hrot.NED.Descriptors.Orchestration.ClusterState.OperatingLive);
        // ⭐ CE-3068: Stop paused the clock, so the second run needs Play — exactly what an operator does.
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.ResumeTime, PayloadJson = string.Empty,
        }).ConfigureAwait(false);
        Assert.True(harness.PumpUntil(() => !ByName(sim, "Rifleman").IsNull && !ByName(cgf, "Rifleman").IsNull, timeoutFrames: 2000),
            "second load: the rifleman must exist on SimHost and CGF");
        harness.PumpFrames(30);

        // ③ from the FILE: once per host, at the authored position, perceiving again.
        Assert.Equal(1, Named(sim, "Rifleman"));
        Assert.Equal(1, Named(cgf, "Rifleman"));
        Assert.Equal(1, Named(sim, "Hostile"));
        var again = sim.GetComponent<SimTransform>(ByName(sim, "Rifleman")).Position;
        Assert.True(Vector3.Distance(authored, again) < 1f,
            $"second load: the rifleman must start at its authored position {authored}, not where the first run left it ({again})");
        string Chain()
        {
            var sb = new System.Text.StringBuilder();
            var r = ByName(cgf, "Rifleman"); var sr = ByName(sim, "Rifleman");
            long rid = cgf.HasComponent<NetworkIdentity>(r) ? cgf.GetComponent<NetworkIdentity>(r).Value : -1;
            sb.Append($"cgf rifleman {r} net={rid} tracks={(cgf.HasComponent<ActiveSensorTracks>(r) ? cgf.GetComponent<ActiveSensorTracks>(r).Count : -1)}");
            foreach (var c in cgf.Query().With<PartMetadata>().With<EqsSensor>().Build())
            {
                var m = cgf.GetComponent<PartMetadata>(c); if (!m.ParentEntity.Equals(r)) continue;
                var cfg = cgf.GetComponent<EqsSensor>(c);
                sb.Append($" | cgf child part={m.InstanceId} bp={cfg.BlueprintId:X8} ep={cfg.Epoch:X8} susp={cfg.Suspended} buf={(cgf.HasComponent<EqsCognitiveBuffer>(c) ? cgf.GetComponent<EqsCognitiveBuffer>(c).Count.ToString() : "none")}");
            }
            foreach (var c in sim.Query().With<PartMetadata>().With<EqsSensor>().Build())
            {
                var m = sim.GetComponent<PartMetadata>(c); if (!m.ParentEntity.Equals(sr)) continue;
                sb.Append($" | sim carrier part={m.InstanceId} ep={sim.GetComponent<EqsSensor>(c).Epoch:X8}");
            }
            int simSensors = 0; foreach (var _ in sim.Query().With<PartMetadata>().With<EqsSensor>().Build()) simSensors++;
            sb.Append($" | sim sensors total={simSensors} alive sim={Alive(sim)} cgf={Alive(cgf)}");
            if (!sr.IsNull)
                sb.Append($" | sim rifleman {sr} lifecycle={sim.GetLifecycleState(sr)} tkb={(sim.HasComponent<TkbIdentity>(sr) ? sim.GetComponent<TkbIdentity>(sr).TkbType.ToString() : "none")} transform={sim.HasComponent<SimTransform>(sr)}");
            return sb.ToString();
        }
        bool again2 = harness.PumpUntil(Remembers, timeoutFrames: 3000);
        _out.WriteLine(Chain());
        Assert.True(again2, "second load: the rifleman must perceive the hostile again — " + Chain());
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2101</c> follow-up — PLAY FROM EDIT is a PREVIEW, a dry run on the in-memory edited world (🔒 user,
    /// 2026-10-05: "never silently forget about the stuff the user is just editing … the play is assumed to be a 'dry run'").
    /// Opened for edit, then Preview: the rifleman must perceive the hostile and take cover exactly as on a live load — the
    /// clock-produced state (RecentSenses, the grown blackboard, the behaviour's sensors) appears once time runs; nothing
    /// in the engine is edit-specific. Stop returns to Edit and REWINDS it to where it stood.
    /// 📄 docs/projects/Hrot/Subsystems/Hrot.Editor.md §Preview · DESIGN_Deterministic_Network_Ids §11c (preview is not cleared).
    /// </summary>
    [Fact(Timeout = 300_000)] // ⭐ CE-3067's acceptance: the Stop rewind must restore the entity index whole (EntityIndex.SyncFrom)
    public async Task CE2101_PreviewFromEdit_PerceivesAndTakesCover_AndStopRewindsToTheEdit()
    {
        var root = RepoRoot();
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(_scenarioId));
        File.Copy(Recipe(root), Path.Combine(NasScenarioStaging.DirectoryOf(_scenarioId), "scenario.json"), overwrite: true);

        using var harness = new HrotRunnerHarness("simhost,ig,excon,cgf", NextDomainId());
        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;
        var rosterDeadline = DateTime.UtcNow.AddSeconds(10.0);
        while (master.NodeRoster.ActiveNodes.Count == 0 && DateTime.UtcNow < rosterDeadline)
        {
            harness.PumpFrames(1);
            Thread.Sleep(10);
        }
        async Task Transition(Hrot.NED.Descriptors.Orchestration.ClusterState target)
        {
            await master.HandleClusterOpRequestAsync(new ClusterOpRequest
            {
                RequestId     = Guid.NewGuid(),
                OperationType = ClusterOpType.TransitionState,
                PayloadJson   = JsonSerializer.Serialize(new { TargetState = target.ToString(), ScenarioId = _scenarioId }),
            }).ConfigureAwait(false);
            Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == (int)target, timeoutFrames: 4000),
                $"cluster must reach {target}; at {(int)master.CurrentClusterState}");
        }

        var cgf = harness.Cgf!.World!;
        var sim = harness.SimHost.World!;
        var registry = harness.Cgf!.TestHook_BehaviorRegistry!;

        await Transition(Hrot.NED.Descriptors.Orchestration.ClusterState.OperatingEdit);
        Assert.True(harness.PumpUntil(() => !ByName(sim, "Rifleman").IsNull && !ByName(sim, "Hostile").IsNull
                                         && !ByName(cgf, "Rifleman").IsNull, timeoutFrames: 2000),
            "edit: both units must exist on SimHost and the rifleman on CGF");
        Assert.True(harness.PumpUntil(() => sim.HasSingletonManaged<TerrainWorld>(), timeoutFrames: 2000), "edit: test-town resident");
        var town = sim.GetSingletonManaged<TerrainWorld>();
        Vector3 Pos(string name) => sim.GetComponent<SimTransform>(ByName(sim, name)).Position;
        var inEdit = Pos("Rifleman");
        {
            var r = ByName(cgf, "Rifleman");
            for (int i = 0; i <= cgf.MaxEntityIndex; i++)
            {
                var e = cgf.GetEntityByIndex(i);
                if (!cgf.IsAlive(e) || !cgf.HasComponent<Fdp.Toolkit.Behavior.Components.SopState>(e)) continue;
                var sop = cgf.GetComponent<Fdp.Toolkit.Behavior.Components.SopState>(e);
                _out.WriteLine($"edit: cgf {e} name={(cgf.HasComponent<EntityInfo>(e) ? cgf.GetComponent<EntityInfo>(e).Name.ToString() : "?")} sopHash={sop.SopHash} sopRun={sop.SopInstanceId} tier={sop.SopBrainTier} " +
                    $"bb256={cgf.HasComponent<Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard256>(e)} bb1024={cgf.HasComponent<Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard1024>(e)} " +
                    $"task={(cgf.HasComponent<BehaviorState>(e) ? cgf.GetComponent<BehaviorState>(e).ActiveBehaviorHash : -1)}");
            }
        }

        // ▶ Play from Edit = Preview. ⭐ CE-3068: Edit is paused (as in production), so Play resumes the clock.
        await Transition(Hrot.NED.Descriptors.Orchestration.ClusterState.OperatingPreview);
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId = Guid.NewGuid(), OperationType = ClusterOpType.ResumeTime, PayloadJson = string.Empty,
        }).ConfigureAwait(false);
        _out.WriteLine("preview: entered");
        Entity Rifleman() => ByName(cgf, "Rifleman");
        Assert.True(harness.PumpUntil(() => !Rifleman().IsNull && cgf.HasComponent<TargetMemory>(Rifleman())
                                         && cgf.GetComponent<TargetMemory>(Rifleman()).Count > 0, timeoutFrames: 3000),
            "preview: the rifleman must perceive the hostile");
        string? Task() => registry.TryGetName(cgf.GetComponent<BehaviorState>(Rifleman()).ActiveBehaviorHash, out var n) ? n : null;
        Assert.True(harness.PumpUntil(() => Task() == "TakeCover", timeoutFrames: 3000), $"preview: the SOP must react with TakeCover; runs {Task()}");
        bool Hidden()
        {
            Entity h = ByName(sim, "Hostile"), r = ByName(sim, "Rifleman");
            var eye = Pos("Hostile") + new Vector3(0, 0, EqsTerrainSight.Mount(sim, h).Standing);
            var aim = Pos("Rifleman") + new Vector3(0, 0, EqsTerrainSight.Mount(sim, r).Crouched);
            return town.SegmentBlocked(eye, aim);
        }
        Assert.True(harness.PumpUntil(Hidden, timeoutFrames: 3000), $"preview: the rifleman must end hidden; it is at {Pos("Rifleman")}");
        Assert.True(Vector3.Distance(inEdit, Pos("Rifleman")) > 1f, "preview: it must have moved");

        // ■ Stop = back to Edit, rewound to the edited world.
        await Transition(Hrot.NED.Descriptors.Orchestration.ClusterState.OperatingEdit);
        harness.PumpFrames(30);
        Assert.True(Vector3.Distance(inEdit, Pos("Rifleman")) < 0.5f,
            $"stop: the rifleman must be back where it stood in Edit ({inEdit}); it is at {Pos("Rifleman")}");
    }

    private async Task RunAsync(bool holdFire, string expectedReaction)
    {
        var root = RepoRoot();
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(_scenarioId));
        var staged = Path.Combine(NasScenarioStaging.DirectoryOf(_scenarioId), "scenario.json");
        var doc = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Recipe(root)))!;
        if (holdFire)
            doc["entities"]!["2a940000-0000-4000-8000-000000000001"]!["Roe"] =
                System.Text.Json.Nodes.JsonNode.Parse("{\"Fire\":\"HoldFire\",\"Reactions\":\"React\",\"SetBy\":\"Superior\"}");
        File.WriteAllText(staged, doc.ToJsonString());
        int site = holdFire ? Hrot.AI.Behaviors.Brains.EqsTacticsNodes.FallBackSite : Hrot.AI.Behaviors.Brains.EqsTacticsNodes.TakeCoverSite;
        uint template = holdFire ? FindSafeRetreatPoint.BlueprintId : FindCoverFromTarget.BlueprintId;

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
        Assert.True(harness.PumpUntil(() => !ByName(sim, "Rifleman").IsNull && !ByName(sim, "Hostile").IsNull
                                         && !ByName(cgf, "Rifleman").IsNull, timeoutFrames: 2000),
            "both units must spawn on SimHost and the rifleman on CGF");
        Entity rifleman = ByName(cgf, "Rifleman"), simRifleman = ByName(sim, "Rifleman"), simHostile = ByName(sim, "Hostile");

        // The terrain the scenario names must be resident where the sight is judged.
        Assert.True(harness.PumpUntil(() => sim.HasSingletonManaged<TerrainWorld>(), timeoutFrames: 2000),
            "test-town must be loaded on SimHost (the scenario header names it)");
        var town = sim.GetSingletonManaged<TerrainWorld>();

        Vector3 Pos(Entity e) => sim.GetComponent<SimTransform>(e).Position;
        bool Hidden()
        {
            var eye = Pos(simHostile) + new Vector3(0, 0, EqsTerrainSight.Mount(sim, simHostile).Standing);
            var aim = Pos(simRifleman) + new Vector3(0, 0, EqsTerrainSight.Mount(sim, simRifleman).Crouched);
            return town.SegmentBlocked(eye, aim);
        }
        Assert.False(Hidden(), "precondition: the rifleman starts in the hostile's view");

        // ① the measurement moved from CE-2092: infantry perception fills the rifleman's memory on a live run.
        Assert.True(harness.PumpUntil(() => cgf.HasComponent<TargetMemory>(rifleman) && cgf.GetComponent<TargetMemory>(rifleman).Count > 0,
            timeoutFrames: 3000), "the rifleman must remember the hostile it can see (perception → TargetMemory on CGF)");

        string? Task() => registry.TryGetName(cgf.GetComponent<BehaviorState>(rifleman).ActiveBehaviorHash, out var n) ? n : null;

        // ② the SOP's contact row reacts with the real tree (row 3: TakeCover; under HoldFire row 2: FallBack).
        Assert.True(harness.PumpUntil(() => Task() == expectedReaction, timeoutFrames: 3000),
            $"the SOP should react with {expectedReaction}; runs {Task()} ({cgf.GetComponent<BehaviorState>(rifleman).Origin})");
        var start = Pos(simRifleman);

        // ③ the rifleman walks into the hostile's blind spot.
        bool hidden = harness.PumpUntil(Hidden, timeoutFrames: 3000);
        // FallBack finishes on arrival; take the verdict from the end position after the move settles.
        if (hidden) harness.PumpFrames(60);
        _out.WriteLine($"rifleman {start} → {Pos(simRifleman)}, hostile {Pos(simHostile)}, task {Task()}");
        _out.WriteLine(Diagnose(cgf, sim, rifleman, simRifleman, site, template));
        Assert.True(hidden && Hidden(), $"the rifleman must end hidden from the hostile; it is at {Pos(simRifleman)} (started {start}), task {Task()} — {Diagnose(cgf, sim, rifleman, simRifleman, site, template)}");
        Assert.True(Vector3.Distance(start, Pos(simRifleman)) > 1f, "it must have MOVED into cover, not been hidden in place");
    }
}
