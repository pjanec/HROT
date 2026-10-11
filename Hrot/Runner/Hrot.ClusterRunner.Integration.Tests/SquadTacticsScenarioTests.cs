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
    public async Task CE2123_ASquadSuppressesFromWhereItStands_FlanksWhereTheLeaderAsks_AndAssaultsThrough()
    {
        var root = RepoRoot();
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(_scenarioId));
        File.Copy(Path.Combine(root, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Scenarios", "tt-suppress-flank", "scenario.json"),
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
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Leader").IsNull && !ByName(cgf, "Hostile").IsNull
                                         && names.All(n => !ByName(cgf, n).IsNull) && names.All(n => !ByName(sim, n).IsNull), timeoutFrames: 2000),
            "the squad and the hostile must spawn");
        var leader = ByName(cgf, "Leader");
        var hostileSim = ByName(sim, "Hostile");
        Assert.True(harness.PumpUntil(() => cgf.HasComponent<UnitRoster>(leader) && cgf.GetComponent<UnitRoster>(leader).Count == 4, timeoutFrames: 2000),
            "the four riflemen are the leader's squad");

        SquadCognitiveState Squad() => cgf.GetComponent<SquadCognitiveState>(leader);
        Entity Member(int i) => cgf.GetComponent<UnitRoster>(leader).SubordinateEntities[i];
        Vector3 SimAt(int i) => sim.GetComponent<SimTransform>(ByName(sim, cgf.GetComponent<EntityInfo>(Member(i)).Name.ToString())).Position;
        Vector3 Threat() => sim.GetComponent<SimTransform>(hostileSim).Position;
        int Ammo(int i) => cgf.HasComponent<WeaponState>(Member(i)) ? cgf.GetComponent<WeaponState>(Member(i)).Ammo : -1;
        string State()
        {
            var sq = Squad();
            var s = $"kind={sq.ManeuverKind} phase={sq.PhaseId} pool={sq.Contacts.Count}";
            for (int i = 0; i < 4; i++) s += $" | m{i} el={sq.Elements.MemberElements[i]} at={SimAt(i)} ammo={Ammo(i)}";
            return s;
        }

        // ① contact ⇒ the drill starts (the leader waited until the squad pooled the enemy).
        Assert.True(harness.PumpUntil(() => Squad().ManeuverKind == SuppressAndManeuverManeuver.ManeuverKind, timeoutFrames: 3000),
            $"the leader must start suppress-and-manoeuvre on contact; {State()}");
        var sq0 = Squad();
        var bases = Enumerable.Range(0, 4).Where(i => sq0.Elements.MemberElements[i] == SuppressAndManeuverManeuver.ElementBaseOfFire).ToArray();
        var assault = Enumerable.Range(0, 4).Where(i => sq0.Elements.MemberElements[i] == SuppressAndManeuverManeuver.ElementAssault).ToArray();
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
        var root = RepoRoot();
        var scenarioId = "tt_react_contact_" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(scenarioId));
        try
        {
            File.Copy(Path.Combine(root, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Scenarios", "tt-react-contact", "scenario.json"),
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
            Assert.True(harness.PumpUntil(() => !ByName(cgf, "Leader").IsNull && cgf.HasComponent<UnitRoster>(ByName(cgf, "Leader"))
                                             && cgf.GetComponent<UnitRoster>(ByName(cgf, "Leader")).Count == 4, timeoutFrames: 3000),
                "the leader and its four riflemen must spawn");
            var leader = ByName(cgf, "Leader");
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
            // ③ … then the leader decides — four riflemen against one ⇒ assault.
            Assert.True(harness.PumpUntil(() => { Note(); return kinds.Contains(SuppressAndManeuverManeuver.ManeuverKind); }, timeoutFrames: 3000),
                $"the leader must decide to assault; {State()}");
            _out.WriteLine("assault: " + State());
            // ④ the drill ends ⇒ the bound resumes, with its own squad state.
            Assert.True(harness.PumpUntil(() => { Note(); return !Paused() && Squad().ManeuverKind == BoundingOverwatchManeuver.ManeuverKind
                                                              && kinds.IndexOf(SuppressAndManeuverManeuver.ManeuverKind) < kinds.Count - 1; }, timeoutFrames: 30000),   // ⚠ ~150 frames per sim second here
                $"after the drill the bound must resume; {State()}");
            _out.WriteLine("end: " + State());
        }
        finally { NasScenarioStaging.Remove(scenarioId); }
    }
}
