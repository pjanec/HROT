using Fdp.Testing;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Maneuvers;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using Xunit.Abstractions;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐ <c>CE-2123</c> (S1) — the shipped <c>tt-suppress-flank</c> scenario on <c>test-town</c>, live on the cluster: a leader ordered to
/// <c>SuppressAndManeuver</c> waits for contact, splits its four riflemen into a base of fire and an assault, asks the terrain for a
/// flank (its own EQS query, solved on SimHost), sends the assault there and then through the enemy's position.
/// 📄 <c>docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md</c> §3.5.
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class SquadTacticsScenarioTests : IDisposable
{
    private const int DomainBase = 57;    // CycloneDDS accepts 0–232; 57 used by no other rail (grep of domain ids, 2026-10-11)
    private static int _domainSeq = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    private readonly string _scenarioId = "tt_suppress_flank_" + Guid.NewGuid().ToString("N");
    private readonly ITestOutputHelper _out;

    public SquadTacticsScenarioTests(ITestOutputHelper output) => _out = output;

    public void Dispose() => NasScenarioStaging.Remove(_scenarioId);

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
    public async Task CE2123_ASquadSuppressesFromWhereItStands_FlanksWhereTheLeaderAsks_AndAssaultsThrough()
    {
        var root = TestRepo.Root;
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(_scenarioId));
        File.Copy(Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.ScenarioRecipes(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!), "tt-suppress-flank", "scenario.json"),
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
        string[] names = { "Rifleman 1", "Rifleman 2", "Rifleman 3", "Rifleman 4" };
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Squad").IsNull && !ByName(cgf, "Hostile").IsNull
                                         && names.All(n => !ByName(cgf, n).IsNull) && names.All(n => !ByName(sim, n).IsNull), timeoutFrames: 2000),
            "the squad and the hostile must spawn");
        var leader = ByName(cgf, "Squad");   // ⭐ CE-2125 (R-272) — the body-less commander; the leader soldier is a member
        var hostileSim = ByName(sim, "Hostile");
        Assert.True(harness.PumpUntil(() => cgf.HasComponent<UnitRoster>(leader) && cgf.GetComponent<UnitRoster>(leader).Count == 5, timeoutFrames: 2000),
            "the leader soldier and the four riflemen are the squad");

        SquadCognitiveState Squad() => cgf.GetComponent<SquadCognitiveState>(leader);
        Entity Member(int i) => cgf.GetComponent<UnitRoster>(leader).SubordinateEntities[i];
        Vector3 SimAt(int i) => sim.GetComponent<SimTransform>(ByName(sim, cgf.GetComponent<EntityInfo>(Member(i)).Name.ToString())).Position;
        Vector3 Threat() => sim.GetComponent<SimTransform>(hostileSim).Position;
        int Ammo(int i) => cgf.HasComponent<WeaponState>(Member(i)) ? cgf.GetComponent<WeaponState>(Member(i)).Ammo : -1;
        string State()
        {
            var sq = Squad();
            var s = $"kind={sq.ManeuverKind} phase={sq.PhaseId} pool={sq.Contacts.Count}";
            for (int i = 0; i < 5; i++) s += $" | m{i} el={sq.Elements.MemberElements[i]} at={SimAt(i)} ammo={Ammo(i)}";
            return s;
        }

        // ① contact ⇒ the drill starts (the leader waited until the squad pooled the enemy).
        Assert.True(harness.PumpUntil(() => Squad().ManeuverKind == SuppressAndManeuverManeuver.ManeuverKind, timeoutFrames: 3000),
            $"the leader must start suppress-and-manoeuvre on contact; {State()}");
        var sq0 = Squad();
        var bases = Enumerable.Range(0, 5).Where(i => sq0.Elements.MemberElements[i] == SuppressAndManeuverManeuver.ElementBaseOfFire).ToArray();
        var assault = Enumerable.Range(0, 5).Where(i => sq0.Elements.MemberElements[i] == SuppressAndManeuverManeuver.ElementAssault).ToArray();
        Assert.NotEmpty(bases);
        Assert.NotEmpty(assault);
        var baseStart = bases.ToDictionary(i => i, SimAt);
        var baseAmmo = bases.ToDictionary(i => i, Ammo);
        _out.WriteLine("started: " + State());

        // ② the assault reaches the flank ⇒ Assaulting.
        Assert.True(harness.PumpUntil(() => Squad().PhaseId == SuppressAndManeuverManeuver.PhaseAssaulting, timeoutFrames: 4000),
            $"the assault must reach its flank; {State()}");
        Vector3 Centre(int[] idx) => idx.Select(SimAt).Aggregate(Vector3.Zero, (a, b) => a + b) / idx.Length;
        var threat = Threat();
        var toBase = Vector2.Normalize(new Vector2(Centre(bases).X - threat.X, Centre(bases).Y - threat.Y));
        var toAssault = Vector2.Normalize(new Vector2(Centre(assault).X - threat.X, Centre(assault).Y - threat.Y));
        float degrees = MathF.Acos(Math.Clamp(Vector2.Dot(toBase, toAssault), -1f, 1f)) * 180f / MathF.PI;
        _out.WriteLine($"flank: {degrees:F0}° round the enemy from the base · {State()}");
        Assert.True(degrees >= 45f, $"the assault must come at the enemy from a different side (≥ 45°): {degrees:F0}°; {State()}");

        // ③ the base of fire fired from where it stood.
        foreach (var i in bases)
        {
            Assert.True(Ammo(i) < baseAmmo[i], $"base member {i} must fire; {State()}");
            // horizontal: the body's height follows the ground under it (measured: a member standing still went z 0 → 9)
            var moved = new Vector2(SimAt(i).X - baseStart[i].X, SimAt(i).Y - baseStart[i].Y).Length();
            Assert.True(moved < 5f, $"base member {i} must hold its ground ({moved:F1} m); {State()}");
        }

        // ④ the assault goes through the enemy's position ⇒ the drill ends.
        bool through = harness.PumpUntil(() => Squad().PhaseId == SuppressAndManeuverManeuver.PhaseAssaultComplete
                                              || Vector3.Distance(Centre(assault), Threat()) < 10f, timeoutFrames: 4000);
        _out.WriteLine("end: " + State());
        Assert.True(through, $"the assault must go through the enemy's position; {State()}");
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2123</c> (S2) — the shipped <c>tt-react-contact</c> scenario: a leader with the squad standing orders
    /// (<c>BasicSquadSop</c>) bounds its squad toward an enemy. Contact ⇒ the squad edge on the leader ⇒ its SOP reacts: the bound is
    /// PAUSED, the squad holds and returns fire, the leader decides (four riflemen against one ⇒ assault) and runs the drill; when it
    /// ends, the bound RESUMES. 📄 <c>docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md</c> §4.6.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task CE2123_ABoundingSquadMeetsTheEnemy_ItsLeaderReacts_HoldsThenAssaults_ThenTheBoundResumes()
    {
        var root = TestRepo.Root;
        var scenarioId = "tt_react_contact_" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(scenarioId));
        try
        {
            File.Copy(Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.ScenarioRecipes(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!), "tt-react-contact", "scenario.json"),
                Path.Combine(NasScenarioStaging.DirectoryOf(scenarioId), "scenario.json"), overwrite: true);

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
                PayloadJson   = JsonSerializer.Serialize(new { TargetState = nameof(Hrot.NED.Descriptors.Orchestration.ClusterState.OperatingLive), ScenarioId = scenarioId }),
            }).ConfigureAwait(false);
            Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == 31, timeoutFrames: 4000),
                $"cluster must reach OperatingLive; at {(int)master.CurrentClusterState}");

            var cgf = harness.Cgf!.World!;
            Assert.True(harness.PumpUntil(() => !ByName(cgf, "Squad").IsNull && cgf.HasComponent<UnitRoster>(ByName(cgf, "Squad"))
                                             && cgf.GetComponent<UnitRoster>(ByName(cgf, "Squad")).Count == 5, timeoutFrames: 3000),
                "the squad's commander and its five men (the leader soldier among them) must spawn");
            var leader = ByName(cgf, "Squad");   // ⭐ CE-2125 (R-272) — the body-less commander
            SquadCognitiveState Squad() => cgf.GetComponent<SquadCognitiveState>(leader);
            bool Paused() => Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem.PausedTaskOf(cgf, leader) != null;
            var kinds = new System.Collections.Generic.List<ushort>();
            void Note() { var k = Squad().ManeuverKind; if (kinds.Count == 0 || kinds[^1] != k) kinds.Add(k); }
            var sim = harness.SimHost.World!;
            string Where()
            {
                var r = "";
                var roster = cgf.GetComponent<UnitRoster>(leader);
                for (int i = 0; i < roster.Count; i++)
                {
                    var name = cgf.GetComponent<EntityInfo>(roster.SubordinateEntities[i]).Name.ToString();
                    var body = ByName(sim, name);
                    r += $" | m{i} el={Squad().Elements.MemberElements[i]} at={(body.IsNull ? "?" : sim.GetComponent<SimTransform>(body).Position.ToString("F0", null))}";
                }
                return r;
            }
            double SimNow() => cgf.HasSingleton<GlobalTime>() ? cgf.GetSingleton<GlobalTime>().TotalTime : -1;
            string State() => $"t={SimNow():F1}s kinds=[{string.Join(",", kinds)}] phase={Squad().PhaseId} pool={Squad().Contacts.Count} paused={Paused()} "
                             + $"flankQuery={!Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(cgf, leader, Hrot.AI.Behaviors.Brains.SuppressAndManeuverPlay.FlankSite).IsNull}{Where()}";

            // ① the leader's task bounds the squad.
            Assert.True(harness.PumpUntil(() => { Note(); return kinds.Contains(BoundingOverwatchManeuver.ManeuverKind); }, timeoutFrames: 3000),
                $"the leader must start its bound; {State()}");
            // ② contact ⇒ the SOP's reaction pauses the bound and the squad holds and returns fire …
            Assert.True(harness.PumpUntil(() => { Note(); return kinds.Contains(SquadHoldManeuver.ManeuverKind) && Paused(); }, timeoutFrames: 6000),
                $"on contact the leader's standing orders must pause the bound and hold; {State()}");
            // ③ … then the leader decides — four riflemen against one ⇒ assault: ⭐ CE-2158 (§7.6.3) the enemy is ~75 m off, so the
            //   assault first CLOSES by fire and movement (kind 12), then flanks (suppress and manoeuvre).
            Assert.True(harness.PumpUntil(() => { Note(); return kinds.Contains(SuppressAndManeuverManeuver.ManeuverKind); }, timeoutFrames: 12000),
                $"the leader must decide to assault; {State()}");
            Assert.True(kinds.IndexOf(Fdp.Toolkit.Squad.Maneuvers.FireAndMovementManeuver.ManeuverKind) is >= 0 and var fm
                        && fm < kinds.IndexOf(SuppressAndManeuverManeuver.ManeuverKind), $"the assault closes by fire and movement first; {State()}");
            _out.WriteLine("assault: " + State());
            // ④ the drill ends ⇒ the bound resumes, with its own squad state.
            Assert.True(harness.PumpUntil(() => { Note(); return !Paused() && Squad().ManeuverKind == BoundingOverwatchManeuver.ManeuverKind
                                                              && kinds.IndexOf(SuppressAndManeuverManeuver.ManeuverKind) < kinds.Count - 1; }, timeoutFrames: 30000),   // ⚠ ~150 frames per sim second here
                $"after the drill the bound must resume; {State()}");
            _out.WriteLine("end: " + State());

            // ⭐⭐ CE-2125 (R-272) — the commander stands at its live men's centre, on SimHost too (the owner's write replicated) …
            var simSquad = ByName(sim, "Squad");
            Vector3 Centre()
            {
                var roster = cgf.GetComponent<UnitRoster>(leader);
                var sum = Vector3.Zero; int n = 0;
                for (int i = 0; i < roster.Count; i++)
                {
                    var body = ByName(sim, cgf.GetComponent<EntityInfo>(roster.SubordinateEntities[i]).Name.ToString());
                    if (body.IsNull || (sim.HasComponent<Health>(body) && sim.GetComponent<Health>(body).Current <= 0f)) continue;
                    sum += sim.GetComponent<SimTransform>(body).Position; n++;
                }
                return n == 0 ? Vector3.Zero : sum / n;
            }
            Assert.True(harness.PumpUntil(() => Vector2.Distance(new Vector2(sim.GetComponent<SimTransform>(simSquad).Position.X, sim.GetComponent<SimTransform>(simSquad).Position.Y),
                                                               new Vector2(Centre().X, Centre().Y)) < 3f, timeoutFrames: 600),
                $"the squad's commander must stand at its men's centre on SimHost: at {sim.GetComponent<SimTransform>(simSquad).Position}, centre {Centre()}");
            // … and the enemy never perceives it.
            var hostile = ByName(cgf, "Hostile");
            if (cgf.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(hostile))
            {
                var mem = cgf.GetComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(hostile);
                unsafe { for (int i = 0; i < mem.Count; i++) Assert.NotEqual((long)leader.PackedValue, mem.EntityIds[i]); }
            }
        }
        finally { NasScenarioStaging.Remove(scenarioId); }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2123</c> (S3) — the shipped <c>tt-squad-march</c> scenario (📐 its leader soldier stands in the riflemen's line:
    /// at the S1/S2 layout's (285, 268) it stands at z 9 and no move ever works from there — measured on the first live run): the squad's commander orders a WEDGE march to a point
    /// ~50 m away on the S1 layout. Every member arrives (a slot that does not work falls back to the lead's trail), and the run
    /// MEASURES what the handoff left open: what the march's re-orders cost — every move order is one SimHost path request. 📄 <c>docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md</c> §6.1.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task CE2123_ASquadMarchesInAWedge_EveryoneArrives_AndThePathRequestCostIsBounded()
    {
        var root = TestRepo.Root;
        var scenarioId = "tt_squad_march_" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(scenarioId));
        try
        {
            File.Copy(Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.ScenarioRecipes(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!), "tt-squad-march", "scenario.json"),
                Path.Combine(NasScenarioStaging.DirectoryOf(scenarioId), "scenario.json"), overwrite: true);

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
                PayloadJson   = JsonSerializer.Serialize(new { TargetState = nameof(Hrot.NED.Descriptors.Orchestration.ClusterState.OperatingLive), ScenarioId = scenarioId }),
            }).ConfigureAwait(false);
            Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == 31, timeoutFrames: 4000),
                $"cluster must reach OperatingLive; at {(int)master.CurrentClusterState}");

            var cgf = harness.Cgf!.World!;
            var sim = harness.SimHost.World!;
            Assert.True(harness.PumpUntil(() => !ByName(cgf, "Squad").IsNull && cgf.HasComponent<UnitRoster>(ByName(cgf, "Squad"))
                                             && cgf.GetComponent<UnitRoster>(ByName(cgf, "Squad")).Count == 5, timeoutFrames: 3000),
                "the squad's commander and its five men must spawn");
            var leader = ByName(cgf, "Squad");
            var objective = new Vector2(340f, 300f);
            double SimNow() => cgf.HasSingleton<GlobalTime>() ? cgf.GetSingleton<GlobalTime>().TotalTime : -1;
            Vector3[] Bodies()
            {
                var roster = cgf.GetComponent<UnitRoster>(leader);
                var r = new Vector3[roster.Count];
                for (int i = 0; i < roster.Count; i++)
                {
                    var body = ByName(sim, cgf.GetComponent<EntityInfo>(roster.SubordinateEntities[i]).Name.ToString());
                    r[i] = body.IsNull ? new Vector3(float.NaN) : sim.GetComponent<SimTransform>(body).Position;
                }
                return r;
            }
            string State() => $"t={SimNow():F1}s kind={cgf.GetComponent<SquadCognitiveState>(leader).ManeuverKind} | "
                             + string.Join(" | ", Bodies().Select((p, i) => $"m{i} at={p.ToString("F0", null)} d={Vector2.Distance(new Vector2(p.X, p.Y), objective):F0}"));

            // ⭐ the cost: every MOVE order is one AdvanceTo → one MoveTo → one new PathToPoint intent, which SimHost's
            //   NavigationIntentBridgeSystem turns into one RequestPath (an infantry move is a crowd path, not a PathRequestBatch).
            int moves = 0;
            double lastLog = 0;
            void Count()
            {
                foreach (var o in cgf.Bus.ReadManaged<Fdp.Toolkit.Behavior.Events.AssignTacticalIntentEvent>())
                    if (o.IntentId == Hrot.Map.Definitions.Behavior.BehaviorNames.AdvanceTo) moves++;
                if (SimNow() - lastLog >= 20) { lastLog = SimNow(); _out.WriteLine($"  {State()} · moves so far {moves}"); }
            }

            // ① the commander's task marches the squad.
            Assert.True(harness.PumpUntil(() => { Count(); return cgf.GetComponent<SquadCognitiveState>(leader).ManeuverKind == SquadMarchManeuver.ManeuverKind; }, timeoutFrames: 3000),
                $"the commander must start the march; {State()}");
            double t0 = SimNow();
            int before = moves;
            _out.WriteLine("start: " + State());

            // ② every member arrives near the objective (a wedge slot is at most ~10 m from the lead).
            bool arrived = harness.PumpUntil(() => { Count(); return Bodies().All(p => Vector2.Distance(new Vector2(p.X, p.Y), objective) < 15f); },
                timeoutFrames: 30000);   // ⚠ ~150 frames per sim second here
            double seconds = SimNow() - t0;
            int cost = moves - before;
            _out.WriteLine($"end: {State()} · move orders (= SimHost path requests) {cost} in {seconds:F1} sim s = {cost / Math.Max(seconds, 1e-3):F2}/s for 5 men");
            Assert.True(arrived, $"every member must reach the objective; {State()}");

            // ④ they arrive as a formation, not a crowd: no two closer than half the spacing (the wedge keeps 5 m).
            var at = Bodies();
            float closest = float.MaxValue;
            for (int i = 0; i < at.Length; i++)
                for (int j = i + 1; j < at.Length; j++)
                    closest = MathF.Min(closest, Vector2.Distance(new Vector2(at[i].X, at[i].Y), new Vector2(at[j].X, at[j].Y)));
            _out.WriteLine($"closest pair at arrival: {closest:F1} m");
            Assert.True(closest >= 2.5f, $"the squad must keep its spacing (closest pair {closest:F1} m); {State()}");

            // ③ the measured cost stays bounded: re-orders go per moved slot, not per tick (the handoff's estimate was ≈ 4/s per 8 men).
            Assert.True(cost / Math.Max(seconds, 1e-3) <= 5.0, $"the march must cost at most 5 move orders a sim second: {cost} in {seconds:F1} s");
        }
        finally { NasScenarioStaging.Remove(scenarioId); }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2141</c> (D2) + <c>CE-2142</c> (D3) — the MOVE order, live: <c>tt-squad-march</c> with the commander's authored march
    /// removed, then the order <c>Order.Move</c> sent at run time (the squad resolves it to its own <c>InfantrySquadMove</c> by its DIS
    /// class) with a GEODETIC location (lat/lon from the node's own transform, as the map menu
    /// will send it). The squad marches there in its own shape (a Wedge off-road), and when the drill ends every member's drill
    /// order is RELEASED (AQ90 A5): no member is left running the AdvanceTo / CombatPosture it was given.
    /// 📄 <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §7.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task CE2141_AMoveOrder_WithAGeodeticPoint_MarchesTheSquadThere_AndReleasesItsMembersAtTheEnd()
    {
        var root = TestRepo.Root;
        var scenarioId = "tt_squad_move_" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(scenarioId));
        try
        {
            var doc = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(
                Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.ScenarioRecipes(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!), "tt-squad-march", "scenario.json")))!;
            foreach (var (_, e) in doc["entities"]!.AsObject())
                if ((string?)e!["EntityInfo"]?["Name"] == "Squad") e.AsObject().Remove("Behavior");   // the order comes at run time
            File.WriteAllText(Path.Combine(NasScenarioStaging.DirectoryOf(scenarioId), "scenario.json"), doc.ToJsonString());

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
                PayloadJson   = JsonSerializer.Serialize(new { TargetState = nameof(Hrot.NED.Descriptors.Orchestration.ClusterState.OperatingLive), ScenarioId = scenarioId }),
            }).ConfigureAwait(false);
            Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == 31, timeoutFrames: 4000),
                $"cluster must reach OperatingLive; at {(int)master.CurrentClusterState}");

            var cgf = harness.Cgf!.World!;
            var sim = harness.SimHost.World!;
            Assert.True(harness.PumpUntil(() => !ByName(cgf, "Squad").IsNull && cgf.HasComponent<UnitRoster>(ByName(cgf, "Squad"))
                                             && cgf.GetComponent<UnitRoster>(ByName(cgf, "Squad")).Count == 5, timeoutFrames: 3000),
                "the squad's commander and its five men must spawn");
            var leader = ByName(cgf, "Squad");
            var objective = new Vector2(340f, 300f);
            double SimNow() => cgf.HasSingleton<GlobalTime>() ? cgf.GetSingleton<GlobalTime>().TotalTime : -1;
            Entity[] Members() { var r = cgf.GetComponent<UnitRoster>(leader); return Enumerable.Range(0, r.Count).Select(i => r.SubordinateEntities[i]).ToArray(); }
            Vector3[] Bodies() => Members().Select(m =>
            {
                var body = ByName(sim, cgf.GetComponent<EntityInfo>(m).Name.ToString());
                return body.IsNull ? new Vector3(float.NaN) : sim.GetComponent<SimTransform>(body).Position;
            }).ToArray();
            int Running(Entity e) => cgf.HasComponent<BehaviorState>(e) ? cgf.GetComponent<BehaviorState>(e).ActiveBehaviorHash : 0;
            string State() => $"t={SimNow():F1}s kind={cgf.GetComponent<SquadCognitiveState>(leader).ManeuverKind} task={Running(leader)} | "
                             + string.Join(" | ", Bodies().Select((p, i) => $"m{i} at={p.ToString("F0", null)} d={Vector2.Distance(new Vector2(p.X, p.Y), objective):F0} runs={Running(Members()[i])}"));

            // ① the order, as the map menu sends it: a geodetic point, through the one tactical-intent path.
            var geo = cgf.GetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>();
            var (lat, lon, _) = geo.ToGeodetic(new Vector3(objective, 0f));
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            cgf.Bus.PublishManaged(new Fdp.Toolkit.Behavior.Events.AssignTacticalIntentEvent
            {
                // ⭐ CE-2142 — the ORDER, not the behaviour: the squad resolves Order.Move to its own InfantrySquadMove by its class.
                Entity = leader, IntentId = Hrot.Map.Definitions.Behavior.Orders.OrderAttribute.IntentFor(nameof(Hrot.AI.Behaviors.Orders.OrderKind.Move)), Origin = BehaviorOrigin.Operator,
                JsonParams = "{\"Location\":[" + lat.ToString("R", inv) + "," + lon.ToString("R", inv) + "]}",
            });
            int moveHash = Fdp.Toolkit.Behavior.BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.InfantrySquadMove);
            Assert.True(harness.PumpUntil(() => cgf.GetComponent<SquadCognitiveState>(leader).ManeuverKind == SquadMarchManeuver.ManeuverKind
                                             && Running(leader) == moveHash, timeoutFrames: 3000),
                $"the commander must take the order and march; {State()}");
            _out.WriteLine("start: " + State());

            // ② everyone arrives (the same bar as the authored march).
            Assert.True(harness.PumpUntil(() => Bodies().All(p => Vector2.Distance(new Vector2(p.X, p.Y), objective) < 15f), timeoutFrames: 30000),
                $"every member must reach the ordered point; {State()}");
            _out.WriteLine("arrived: " + State());

            // ③ the drill ends by itself, and its members' orders are released (A5).
            Assert.True(harness.PumpUntil(() => Running(leader) != moveHash, timeoutFrames: 6000), $"the march must end; {State()}");
            int advance = Fdp.Toolkit.Behavior.BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.AdvanceTo);
            int hold    = Fdp.Toolkit.Behavior.BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.CombatPosture);
            bool released = harness.PumpUntil(() => Members().All(m => Running(m) != advance && Running(m) != hold), timeoutFrames: 900);
            _out.WriteLine("end: " + State());
            Assert.True(released, $"no member may still run the drill's order after the drill ended; {State()}");
        }
        finally { NasScenarioStaging.Remove(scenarioId); }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-1056</c> I-2d — the shipped <c>tt-convoy-ambush</c> scenario (catalogue P1 / D8): a squad rides an APC; an insurgent
    /// fires an RPG at the APC ⇒ the carrier's hit puts the squad under fire ⇒ the squad SOP's first row dismounts it (nobody aboard,
    /// on CGF AND SimHost) ⇒ the next row reacts to contact. 📄 docs/designs/group-maneuvers/DESIGN_Squad_Tactics.md §5a.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task CE1056_AConvoyIsAmbushed_TheSquadDismounts_ThenReactsToContact()
    {
        var root = TestRepo.Root;
        var scenarioId = "tt_convoy_ambush_" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(scenarioId));
        try
        {
            File.Copy(Path.Combine(Fdp.Toolkit.Orchestration.ContentLayout.ScenarioRecipes(Fdp.Toolkit.Orchestration.ContentLayout.TryFindSourceTree()!), "tt-convoy-ambush", "scenario.json"),
                Path.Combine(NasScenarioStaging.DirectoryOf(scenarioId), "scenario.json"), overwrite: true);

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
                PayloadJson   = JsonSerializer.Serialize(new { TargetState = nameof(Hrot.NED.Descriptors.Orchestration.ClusterState.OperatingLive), ScenarioId = scenarioId }),
            }).ConfigureAwait(false);
            Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == 31, timeoutFrames: 4000),
                $"cluster must reach OperatingLive; at {(int)master.CurrentClusterState}");

            var cgf = harness.Cgf!.World!;
            var sim = harness.SimHost.World!;
            Assert.True(harness.PumpUntil(() => !ByName(cgf, "Squad").IsNull && cgf.HasComponent<UnitRoster>(ByName(cgf, "Squad"))
                                             && cgf.GetComponent<UnitRoster>(ByName(cgf, "Squad")).Count == 5
                                             && !ByName(cgf, "Convoy APC").IsNull, timeoutFrames: 3000),
                "the squad's commander, its five men and the APC must spawn");
            var commander = ByName(cgf, "Squad");
            var apc = ByName(cgf, "Convoy APC");
            int Aboard(EntityRepository w)
            {
                var v = ByName(w, "Convoy APC");
                return v.IsNull || !w.HasComponent<PassengerBuffer>(v) ? -1 : w.GetComponent<PassengerBuffer>(v).Count;
            }
            bool Paused() => Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem.PausedTaskOf(cgf, commander) != null;
            var kinds = new System.Collections.Generic.List<ushort>();
            void Note() { var k = cgf.GetComponent<SquadCognitiveState>(commander).ManeuverKind; if (kinds.Count == 0 || kinds[^1] != k) kinds.Add(k); }
            float ApcHealth() => cgf.HasComponent<Health>(apc) ? cgf.GetComponent<Health>(apc).Current : -1f;
            double SimNow() => cgf.HasSingleton<GlobalTime>() ? cgf.GetSingleton<GlobalTime>().TotalTime : -1;
            var rpg = ByName(cgf, "Insurgent RPG");
            string Insurgent()
            {
                if (rpg.IsNull) return "rpg=?";
                int run = cgf.HasComponent<BehaviorState>(rpg) ? cgf.GetComponent<BehaviorState>(rpg).ActiveBehaviorHash : 0;
                int mem = -1; bool seesApc = false;
                if (cgf.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(rpg))
                {
                    var m = cgf.GetComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(rpg);
                    mem = m.Count;
                    unsafe { for (int i = 0; i < m.Count; i++) if (m.EntityIds[i] == (long)apc.PackedValue) seesApc = true; }
                }
                string weapon = cgf.HasComponent<WeaponState>(rpg) ? JsonSerializer.Serialize(cgf.GetComponent<WeaponState>(rpg), new JsonSerializerOptions { IncludeFields = true }) : "none";
                float simHp = ByName(sim, "Convoy APC") is var sa && !sa.IsNull && sim.HasComponent<Health>(sa) ? sim.GetComponent<Health>(sa).Current : -1f;
                string Bs(Entity e) => e.IsNull ? "?" : !cgf.HasComponent<BehaviorState>(e) ? "noBehaviorState"
                    : $"inst={cgf.GetComponent<BehaviorState>(e).InstanceId} tier={cgf.GetComponent<BehaviorState>(e).BrainTier} origin={cgf.GetComponent<BehaviorState>(e).Origin} since={cgf.GetComponent<BehaviorState>(e).RunSince:F1} auth={cgf.HasAuthority<BehaviorState>(e)}";
                var rifle = ByName(cgf, "Insurgent rifleman");
                return $"rpg[{Bs(rpg)}] rifle[run={(rifle.IsNull || !cgf.HasComponent<BehaviorState>(rifle) ? 0 : cgf.GetComponent<BehaviorState>(rifle).ActiveBehaviorHash)} {Bs(rifle)}] rpg run={run} (posture={Fdp.Toolkit.Behavior.BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.CombatPosture)} fire={Fdp.Toolkit.Behavior.BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.FireAtTarget)}) mem={mem} seesApc={seesApc} weapon={weapon} simApcHp={simHp:F0}";
            }
            string State() => $"t={SimNow():F1}s kinds=[{string.Join(",", kinds)}] paused={Paused()} aboard cgf={Aboard(cgf)} sim={Aboard(sim)} apcHp={ApcHealth():F0} | {Insurgent()}";

            // ① the squad rides the APC (loaded aboard by the scenario).
            Assert.True(harness.PumpUntil(() => Aboard(cgf) == 5 && Aboard(sim) == 5, timeoutFrames: 3000), $"all five must start aboard; {State()}");
            float hp0 = ApcHealth();
            // ② the ambush: the APC is hit …
            Assert.True(harness.PumpUntil(() => { Note(); return ApcHealth() < hp0; }, timeoutFrames: 15000), $"the insurgent must hit the APC; {State()}");
            _out.WriteLine("hit: " + State());
            // ③ … the squad dismounts — nobody aboard, on the brain node and on SimHost …
            Assert.True(harness.PumpUntil(() => { Note(); return kinds.Contains(SquadDismountManeuver.ManeuverKind) && Aboard(cgf) == 0 && Aboard(sim) == 0; },
                                          timeoutFrames: 9000),
                $"the squad must dismount; {State()}");
            _out.WriteLine("out: " + State());
            // ④ … then fights: a contact reaction (hold / assault / break contact) pauses the task after the dismount.
            Assert.True(harness.PumpUntil(() => { Note(); return Paused() && cgf.GetComponent<SquadCognitiveState>(commander).ManeuverKind != SquadDismountManeuver.ManeuverKind
                                                              && kinds.LastIndexOf(SquadDismountManeuver.ManeuverKind) < kinds.Count - 1; }, timeoutFrames: 15000),
                $"after the dismount the squad must react to contact; {State()}");
            _out.WriteLine("react: " + State());
        }
        finally { NasScenarioStaging.Remove(scenarioId); }
    }

    // ── CE-2294 CC-D — the first command chain, cc-platoon-bound (Command Chain §8; the live check's in-process twin) ─────────

    /// <summary>
    /// ⭐⭐ <c>CE-2294</c> CC-D — <c>scenarios/cc-platoon-bound</c> in-process (the twin of <c>utility-demo-check.py cc-platoon-bound</c>;
    /// 📄 docs/designs/group-maneuvers/DESIGN_Command_Chain_Example.md §8, §8a): ① Bravo is ordered first and its SET reaches the
    /// platoon's picture · ② only then is Alpha ordered · ③ the enemies turn hostile and Alpha's staff SPOTs them as ONE element · ④ the
    /// platoon's SOP reacts with <c>PlatoonHold</c>, both squads hold; the enemies die, the lost SPOT ends the hold and the task resumes
    /// in its leg — Alpha ordered on, Bravo not — and Alpha's SET completes it · ⑤ every report is in the node's report log.
    /// ⚠ The side flip and the deaths are written on every host's world directly (the live check goes through the HTTP routes).
    /// </summary>
    [Fact(Timeout = 900_000)]
    public async Task CE2294_CcPlatoonBound_SupportThenAssault_HoldOnASpot_ResumeInTheLeg()
    {
        var scenarioId = "cc_platoon_bound_" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(scenarioId));
        try
        {
            File.Copy(Path.Combine(TestRepo.Root, "scenarios", "cc-platoon-bound", "scenario.json"),
                Path.Combine(NasScenarioStaging.DirectoryOf(scenarioId), "scenario.json"), overwrite: true);
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
                PayloadJson   = JsonSerializer.Serialize(new { TargetState = nameof(Hrot.NED.Descriptors.Orchestration.ClusterState.OperatingLive), ScenarioId = scenarioId }),
            }).ConfigureAwait(false);
            Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == 31, timeoutFrames: 4000),
                $"cluster must reach OperatingLive; at {(int)master.CurrentClusterState}");

            var cgf = harness.Cgf!.World!;
            var sim = harness.SimHost.World!;
            var ig = harness.Ig.App.World;
            string[] enemyNames = { "Enemy 1", "Enemy 2", "Enemy 3" };
            Assert.True(harness.PumpUntil(() => new[] { "Platoon", "Alpha", "Bravo" }.Concat(enemyNames).All(n => !ByName(cgf, n).IsNull && !ByName(sim, n).IsNull),
                timeoutFrames: 3000), "the platoon, its squads and the enemies must spawn on CGF and SimHost");
            var platoon = ByName(cgf, "Platoon");
            var alpha = ByName(cgf, "Alpha");
            var bravo = ByName(cgf, "Bravo");
            int bound = Fdp.Toolkit.Behavior.BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.PlatoonBound);
            int hold = Fdp.Toolkit.Behavior.BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.PlatoonHold);
            int squadMove = Fdp.Toolkit.Behavior.BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.InfantrySquadMove);
            int squadHold = Fdp.Toolkit.Behavior.BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.InfantrySquadHold);
            int Task(Entity e) => cgf.HasComponent<BehaviorState>(e) ? cgf.GetComponentRO<BehaviorState>(e).ActiveBehaviorHash : 0;
            Fdp.Toolkit.Squad.Command.ProgressKind Progress(Entity squad)
            {
                if (!cgf.HasComponent<Fdp.Toolkit.Squad.Command.CommandPicture>(platoon)) return default;
                var pic = cgf.GetComponentRO<Fdp.Toolkit.Squad.Command.CommandPicture>(platoon);
                int row = Fdp.Toolkit.Squad.Command.CommandPictureOps.RowOf(pic, Fdp.Toolkit.Squad.Command.CommandPictureOps.NetIdOf(cgf, squad));
                return row < 0 ? default : pic.Subordinates.Items[row].Progress;
            }
            string State() => $"platoon {Task(platoon)} alpha {Task(alpha)} bravo {Task(bravo)} · Alpha {Progress(alpha)} Bravo {Progress(bravo)}";

            // ①② Bravo first; Alpha only after Bravo's SET is in the picture
            Assert.True(harness.PumpUntil(() => Task(platoon) == bound && Task(bravo) == squadMove, timeoutFrames: 3000), $"Bravo is ordered first; {State()}");
            Assert.NotEqual(squadMove, Task(alpha));
            bool alphaEarly = false;
            Assert.True(harness.PumpUntil(() => { if (Task(alpha) == squadMove && Progress(bravo) != Fdp.Toolkit.Squad.Command.ProgressKind.Set) alphaEarly = true;
                                                   return Progress(bravo) == Fdp.Toolkit.Squad.Command.ProgressKind.Set; }, timeoutFrames: 40000),
                $"① Bravo reaches the support point and its SET reaches the platoon; {State()}");
            Assert.False(alphaEarly, "② Alpha was ordered before Bravo's SET");
            Assert.True(harness.PumpUntil(() => Task(alpha) == squadMove, timeoutFrames: 2000), $"② then Alpha is ordered; {State()}");

            // ③ the enemies turn hostile: ONE element from Alpha
            foreach (var w in new[] { cgf, sim, ig })
                foreach (var n in enemyNames)
                {
                    var e = ByName(w, n);
                    if (e.IsNull) continue;
                    var info = w.GetComponentRO<EntityInfo>(e);
                    info.ForceId = ForceId.Hostile;
                    w.SetComponent(e, info);
                }
            Assert.True(harness.PumpUntil(() => Task(platoon) == hold, timeoutFrames: 6000), $"④ the platoon's SOP reacts with PlatoonHold; {State()}");
            var picture = cgf.GetComponentRO<Fdp.Toolkit.Squad.Command.CommandPicture>(platoon);
            long alphaId = Fdp.Toolkit.Squad.Command.CommandPictureOps.NetIdOf(cgf, alpha);
            int fromAlpha = 0;
            for (int i = 0; i < picture.Elements.Count; i++) if (picture.Elements.Items[i].FromNetId == alphaId) fromAlpha++;
            Assert.Equal(1, fromAlpha);                                                    // ③ one element, not one per contact
            Assert.True(harness.PumpUntil(() => Task(alpha) == squadHold && Task(bravo) == squadHold, timeoutFrames: 2000), $"④ both squads hold; {State()}");

            // ④ the enemies die: the lost SPOT ends the hold, the task resumes in its leg
            foreach (var w in new[] { cgf, sim, ig })
                foreach (var n in enemyNames)
                {
                    var e = ByName(w, n);
                    if (e.IsNull || !w.HasComponent<Health>(e)) continue;
                    var h = w.GetComponentRO<Health>(e);
                    h.Current = 0f;
                    w.SetComponent(e, h);
                }
            Assert.True(harness.PumpUntil(() => Task(platoon) == bound && Task(alpha) == squadMove, timeoutFrames: 6000),
                $"④ the hold ends and the task resumes, ordering Alpha on; {State()}");
            Assert.NotEqual(squadMove, Task(bravo));
            Assert.True(harness.PumpUntil(() => Task(platoon) != bound && Task(platoon) != hold, timeoutFrames: 40000),
                $"Alpha reaches the objective, reports SET, and the task completes; {State()}");

            // ⑤ the node's report log holds them, each with its ObservedAt
            var log = Fdp.Toolkit.Squad.Command.SquadReportLog.Peek(cgf);
            Assert.NotNull(log);
            Assert.True(log!.Count >= 4, $"the report log holds the run's reports ({log.Count})");
            Assert.All(log.Recent(64), e => Assert.True(e.Report.ObservedAt > 0f));
            _out.WriteLine("done: " + State());
        }
        finally { NasScenarioStaging.Remove(scenarioId); }
    }
}
