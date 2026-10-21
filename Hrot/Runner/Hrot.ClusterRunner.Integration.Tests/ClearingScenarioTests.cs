using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Command;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using Xunit.Abstractions;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐ The building-clearing demos (📄 docs/designs/group-maneuvers/DESIGN_Building_Clearing.md §8), each the in-process twin of its
/// live check: the shipped scenario on the four hosts, the squad's reports read from the node's report log (the record every node holds,
/// §8.2), the people read from the CGF world.
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class ClearingScenarioTests : IDisposable
{
    private const int DomainBase = 196;   // CycloneDDS accepts 0–232; 196–199 used by no other rail (grep of DomainBase, 2026-10-20)
    private static int _domainSeq = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    private readonly List<string> _staged = new();
    private readonly ITestOutputHelper _out;

    public ClearingScenarioTests(ITestOutputHelper output) => _out = output;

    public void Dispose()
    {
        foreach (var id in _staged) NasScenarioStaging.Remove(id);
    }

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

    /// <summary>Stages <c>scenarios/&lt;name&gt;</c>, boots the four hosts and takes the cluster live.</summary>
    private async Task<HrotRunnerHarness> Live(string scenario)
    {
        var id = scenario.Replace('-', '_') + "_" + Guid.NewGuid().ToString("N");
        _staged.Add(id);
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(id));
        File.Copy(Path.Combine(RepoRoot(), "scenarios", scenario, "scenario.json"),
            Path.Combine(NasScenarioStaging.DirectoryOf(id), "scenario.json"), overwrite: true);

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
            PayloadJson   = JsonSerializer.Serialize(new { TargetState = nameof(ClusterState.OperatingLive), ScenarioId = id }),
        }).ConfigureAwait(false);
        Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == 31, timeoutFrames: 4000),
            $"cluster must reach OperatingLive; at {(int)master.CurrentClusterState}");
        return harness;
    }

    /// <summary>
    /// The PROGRESS reports a squad has made, in order: (subtype, where). ⚠ A squad with NO superior never SENDS (its reports wait in
    /// its own <see cref="ReportOutbox"/>, CE-2304) — so the node's log stays empty for a lone squad and the record is its outbox, which
    /// keeps the newest eight: <see cref="Collect"/> samples it often and keeps each report once (by its sequence number).
    /// </summary>
    private sealed class Reports
    {
        private readonly SortedDictionary<uint, CommandReport> _seen = new();
        public void Collect(EntityRepository cgf, Entity squad)
        {
            foreach (var e in SquadReportLog.Peek(cgf)?.Recent(SquadReportLog.Capacity) ?? Enumerable.Empty<SquadReportLogEntry>())
                if (e.FromUnit.Equals(squad)) _seen.TryAdd(e.Report.Seq, e.Report);
            if (squad.IsNull || !cgf.IsAlive(squad) || !cgf.HasComponent<ReportOutbox>(squad)) return;
            var outbox = cgf.GetComponent<ReportOutbox>(squad);
            for (int i = 0; i < outbox.Queued.Count; i++) _seen.TryAdd(outbox.Queued.Items[i].Report.Seq, outbox.Queued.Items[i].Report);
        }
        public List<(ProgressKind Kind, Vector3 At)> Progress()
            => _seen.Values.Where(r => r.Kind == ReportKind.Progress).Select(r => ((ProgressKind)r.Subtype, r.At)).ToList();
    }

    /// <summary>
    /// ⭐ Every round that struck a person, by name: who was hit, by whom (SimHost fires every round, and its <see cref="ShotLog"/> names
    /// the shooter). Sampled often, each kept once (by its sequence number) — the log is a ring.
    /// </summary>
    private sealed class Hits
    {
        private readonly SortedDictionary<long, (string Hit, string By, string Text)> _seen = new();
        private readonly HashSet<long> _rounds = new();
        private readonly SortedDictionary<string, int> _firedBy = new();
        /// <summary>Every round fired, by shooter name — a hit list alone cannot tell "never fired" from "always missed".</summary>
        public string Fired => string.Join(" ", _firedBy.Select(kv => $"{kv.Key}:{kv.Value}"));
        public void Collect(EntityRepository sim)
        {
            var log = ShotLog.Peek(sim);
            if (log == null) return;
            foreach (var r in log.Recent(ShotLog.Capacity))
            {
                if (_rounds.Add(r.Seq))
                {
                    var shooter = !r.Shooter.IsNull && sim.IsAlive(r.Shooter) && sim.HasComponent<EntityInfo>(r.Shooter) ? sim.GetComponent<EntityInfo>(r.Shooter).Name.ToString() : "?";
                    _firedBy[shooter] = _firedBy.TryGetValue(shooter, out var n) ? n + 1 : 1;
                }
                if (_seen.ContainsKey(r.Seq) || r.HitEntity.IsNull || !sim.IsAlive(r.HitEntity) || !sim.HasComponent<EntityInfo>(r.HitEntity)) continue;
                var hit = sim.GetComponent<EntityInfo>(r.HitEntity).Name.ToString();
                var by = !r.Shooter.IsNull && sim.IsAlive(r.Shooter) && sim.HasComponent<EntityInfo>(r.Shooter) ? sim.GetComponent<EntityInfo>(r.Shooter).Name.ToString() : "?";
                _seen[r.Seq] = (hit, by, $"{hit}<-{by}@{r.Tick}({r.ArrivingDamage:F0})");
            }
        }
        public List<(string Hit, string By, string Text)> Where(Func<string, string, bool> hitBy) => _seen.Values.Where(h => hitBy(h.Hit, h.By)).ToList();
        public static string Text(IEnumerable<(string Hit, string By, string Text)> hits) => string.Join(" ", hits.Select(h => h.Text));
    }

    /// <summary>Pumps <paramref name="frames"/> in one-second steps, collecting the squad's reports after each.</summary>
    private static void Pump(HrotRunnerHarness harness, EntityRepository cgf, Entity squad, Reports reports, int frames)
    {
        for (int f = 0; f < frames; f += 60) { harness.PumpFrames(60); reports.Collect(cgf, squad); }
    }

    /// <summary>The squad commander's drill: phase, since when, and each member's order (kind @ point).</summary>
    private static string Drill(EntityRepository w, Entity squad)
    {
        var t = SquadDrillTrace.Of(w, squad);
        if (!t.Active) return "none";
        return $"k{t.Kind} p{t.Phase}@{t.PhaseSince:F0} [{string.Join(" ", t.Orders.Select(o => $"{o.Kind}({o.Point.X:F1},{o.Point.Y:F1})"))}] "
             + $"hist [{string.Join(" ", t.History.Select(h => $"{h.Phase}/{h.Event}@{h.At:F0}"))}]";
    }

    /// <summary>The CQB house's doors as the brain's node sees them: each door entity's live state (by its terrain-object key), and how
    /// many door entities the node holds.</summary>
    private static string Doors(EntityRepository w, params string[] ids)
    {
        int count = 0;
        for (int i = 0; i <= w.MaxEntityIndex; i++)
        {
            var e = w.GetEntityByIndex(i);
            if (w.IsAlive(e) && w.HasComponent<Fdp.Toolkit.Terrain.DoorState>(e)) count++;
        }
        return $"doors={count} " + string.Join(" ", ids.Select(id =>
        {
            var door = new Fdp.Toolkit.Terrain.TerrainObjectRef("bt-range/CQB House/" + id).Resolve(w);
            return id + "=" + (door.IsNull || !w.HasComponent<Fdp.Toolkit.Terrain.DoorState>(door) ? "none" : w.GetComponent<Fdp.Toolkit.Terrain.DoorState>(door).State.ToString());
        }));
    }

    /// <summary>How SimHost moves him: mode, destination, arrival radius, arrived / held, speed, and the navigation result.</summary>
    private static string Nav(EntityRepository sim, string name)
    {
        var e = ByName(sim, name);
        if (e.IsNull || !sim.HasComponent<CarKinem.Core.NavState>(e)) return "";
        var n = sim.GetComponent<CarKinem.Core.NavState>(e);
        float v = sim.HasComponent<SimVelocity>(e) ? sim.GetComponent<SimVelocity>(e).Linear.Length() : -1f;
        string r = sim.HasComponent<Fdp.Toolkit.Navigation.NavigationStatus>(e) ? sim.GetComponent<Fdp.Toolkit.Navigation.NavigationStatus>(e).Result.ToString() : "-";
        return $"{name.Replace("Blue ", "")}:{n.Mode}/t{n.TrajectoryId}/s{n.ProgressS:F1}→({n.FinalDestination.X:F1},{n.FinalDestination.Y:F1})r{n.ArrivalRadius:F1}"
             + $"{(n.HasArrived != 0 ? "/ARR" : "")}{(n.IsBlocked != 0 ? "/HELD" : "")}/v{v:F2}/{r}";
    }

    /// <summary>The man's locomotion channel: its action and status (m0 = none).</summary>
    private static string Moving(EntityRepository w, Entity e)
    {
        if (!w.HasComponent<LocomotionChannel>(e)) return "";
        var c = w.GetComponent<LocomotionChannel>(e);
        return $"/m{c.ActiveAction}{c.Status.ToString()[0]}";
    }

    private static bool InTheFight(EntityRepository w, Entity e) => !e.IsNull && w.IsAlive(e) && Injury.IsInTheFight(w, e);

    private static string Where(EntityRepository w, Entity e)
        => !e.IsNull && w.IsAlive(e) && w.HasComponent<SimTransform>(e)
            ? $"({w.GetComponent<SimTransform>(e).Position.X:F1},{w.GetComponent<SimTransform>(e).Position.Y:F1}) {(InTheFight(w, e) ? "up" : "DOWN")}"
            : "gone";

    // ── bc-room-clear (BC-1, CE-2257; §8.1) ────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <c>CE-2257</c> BC-1 — <c>bc-room-clear</c> in-process: a six-man squad at room A's north side door of the CQB house
    /// (<c>bt-range</c>, SW corner (40,100)) is ordered <c>ClearRoom</c> A; two insurgents (<c>CombatPosture</c>, staying) hold its south
    /// corners. ③ both insurgents down, no friend hit BY A FRIEND (AQ99 B, R-2065 — the casualties are reported) · ④ a <c>RoomClear</c> report for A (its point inside A).
    /// </summary>
    [Fdp.Testing.FlakyFact("CE-2341: two alert defenders aimed at the door kill all four entry men in about 1 run of 4 (AQ99 B's accepted cost; BC-4/BC-5 make the entry bloodless)", Timeout = 600_000), Trait("Category", "Flaky")]
    public async Task CE2257_BcRoomClear_TheSquadClearsRoomA_BothInsurgentsDown_RoomClearReported()
    {
        using var harness = await Live("bc-room-clear");
        var cgf = harness.Cgf!.World!;
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Insurgent 2").IsNull && !ByName(cgf, "Blue Rifleman 5").IsNull, timeoutFrames: 2000),
            "the squad and the insurgents spawn on CGF");
        var squad = ByName(cgf, "Blue Squad");
        var reports = new Reports();
        var insurgents = new[] { ByName(cgf, "Insurgent 1"), ByName(cgf, "Insurgent 2") };
        var blue = new[] { "Blue Leader", "Blue Rifleman 1", "Blue Rifleman 2", "Blue Rifleman 3", "Blue Rifleman 4", "Blue Rifleman 5" }
            .Select(n => ByName(cgf, n)).ToArray();
        var registry = harness.Cgf!.TestHook_BehaviorRegistry!;
        string Runs(Entity e) => !e.IsNull && cgf.IsAlive(e) && cgf.HasComponent<BehaviorState>(e)
            && registry.TryGetName(cgf.GetComponent<BehaviorState>(e).ActiveBehaviorHash, out var n) ? n + Moving(cgf, e) : "-";
        string State() => $"insurgents [{string.Join(" ", insurgents.Select(e => Where(cgf, e) + ":" + Runs(e)))}] blue [{string.Join(" ", blue.Select(e => Where(cgf, e) + ":" + Runs(e)))}] "
                        + $"nav [{string.Join(" ", new[] { "Blue Leader", "Blue Rifleman 1", "Blue Rifleman 2", "Blue Rifleman 3", "Blue Rifleman 4" }.Select(n => Nav(harness.SimHost.World!, n)))}] "
                        + $"squad {Runs(squad)} cgf {Doors(cgf, "north", "a")} sim {Doors(harness.SimHost.World!, "north", "a")} drill {Drill(cgf, squad)} progress [{string.Join(" ", reports.Progress().Select(p => $"{p.Kind}@({p.At.X:F1},{p.At.Y:F1})"))}]";

        bool RoomAClear() => reports.Progress().Any(p => p.Kind == ProgressKind.RoomClear && p.At.X is > 40f and < 47f && p.At.Y is > 106f and < 110f);
        var sim = harness.SimHost.World!;
        var hits = new Hits();
        static bool Blue(string name) => name.StartsWith("Blue");
        for (int f = 0; f < 60 * 240 && !RoomAClear(); f += 600)
        {
            for (int g = 0; g < 600; g += 60) { harness.PumpFrames(60); reports.Collect(cgf, squad); hits.Collect(sim); }
            _out.WriteLine($"f{f}: {State()}");
        }
        _out.WriteLine("end: " + State());
        // ⭐ AQ99 B (R-2065) — the casualties are REPORTED, not asserted: two alert defenders aimed at the door cost the entry men (BC-4's
        //   grenade and BC-5's contact drill are what make it bloodless); what BC-1 owns is that no friend is hit BY A FRIEND
        var lost = blue.Where(e => !InTheFight(cgf, e)).Select(e => cgf.IsAlive(e) ? cgf.GetComponent<EntityInfo>(e).Name.ToString() : "gone").ToList();
        _out.WriteLine($"casualties: {lost.Count} [{string.Join(", ", lost)}] · hits on blue [{Hits.Text(hits.Where((h, _) => Blue(h)))}] · hits by blue [{Hits.Text(hits.Where((_, by) => Blue(by)))}] · fired [{hits.Fired}]");
        Assert.True(insurgents.All(e => !InTheFight(cgf, e)), $"③ both insurgents down; {State()}");
        Assert.True(RoomAClear(), $"④ RoomClear for room A; {State()}");
        var friendlyFire = hits.Where((h, by) => Blue(h) && Blue(by));
        Assert.True(friendlyFire.Count == 0, $"③ no friend hit by a friend: [{Hits.Text(friendlyFire)}]; {State()}");
    }

    // ── bc-house-clear (BC-3, CE-2259; §8.1) ───────────────────────────────────────────────────────────────────────────────

    /// <summary>The CQB house's rooms (world, SW corner (40,100)): hallway, A, B, C, D, E — the quads the RoomClear points must fall in.</summary>
    private static readonly (string Name, float X0, float Y0, float X1, float Y1)[] CqbRooms =
    {
        ("hall", 40f, 104f, 54f, 106f), ("A", 40f, 106f, 47f, 110f), ("B", 47f, 106f, 54f, 110f),
        ("C", 40f, 100f, 49f, 104f), ("D", 49f, 100f, 51.8f, 104f), ("E", 51.8f, 100f, 54f, 104f),
    };

    private static string RoomAt(Vector3 at)
    {
        foreach (var r in CqbRooms)
            if (at.X >= r.X0 && at.X <= r.X1 && at.Y >= r.Y0 && at.Y <= r.Y1) return r.Name;
        return "?";
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2259</c> BC-3 — <c>bc-house-clear</c> in-process: the same six-man squad at the CQB house's FRONT door is ordered
    /// <c>ClearBuilding</c>; insurgents hold room A's far corner and the closet E; civilians stand in B and C. ① a <c>RoomClear</c> for
    /// each of hallway, A, B, C, D, E — each ONCE, E after D · ③ both insurgents down, both civilians unhurt, no friend hit BY A FRIEND (AQ99 B — the casualties reported) · ④ <c>BuildingClear</c> last.
    /// ⏭ "held" (§8.1 ③) is BC-2's; "the teams swap" (②) is read from the drill trace by the live check, not here.
    /// </summary>
    [Fact(Timeout = 900_000)]
    public async Task CE2259_BcHouseClear_TheSquadClearsEveryRoomOnce_InsurgentsDown_CiviliansUnhurt_BuildingClearLast()
    {
        using var harness = await Live("bc-house-clear");
        var cgf = harness.Cgf!.World!;
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Civilian 2").IsNull && !ByName(cgf, "Blue Rifleman 5").IsNull, timeoutFrames: 2000),
            "the squad, the insurgents and the civilians spawn on CGF");
        var squad = ByName(cgf, "Blue Squad");
        var reports = new Reports();
        var insurgents = new[] { ByName(cgf, "Insurgent 1"), ByName(cgf, "Insurgent 2") };
        var civilians = new[] { ByName(cgf, "Civilian 1"), ByName(cgf, "Civilian 2") };
        // ⭐ by NAME each time: the civilian's entity can be replaced (measured: the first handle died at ~165 s, no round had hit him)
        float Hp(Entity _, int i) { var e = ByName(cgf, $"Civilian {i + 1}"); return !e.IsNull && cgf.HasComponent<Health>(e) ? cgf.GetComponent<Health>(e).Current : -1f; }
        string Civ(int i) { var e = ByName(cgf, $"Civilian {i + 1}"); return e.IsNull ? "gone" : $"{Hp(e, i):F0}{Where(cgf, e)}{(e.Equals(civilians[i]) ? "" : "(new entity)")}"; }
        string Rooms() => string.Join(" ", reports.Progress().Select(p => p.Kind == ProgressKind.RoomClear ? RoomAt(p.At) : p.Kind.ToString()));
        var blue = new[] { "Blue Leader", "Blue Rifleman 1", "Blue Rifleman 2", "Blue Rifleman 3", "Blue Rifleman 4", "Blue Rifleman 5" }
            .Select(n => ByName(cgf, n)).ToArray();
        var registry = harness.Cgf!.TestHook_BehaviorRegistry!;
        string Runs(Entity e) => !e.IsNull && cgf.IsAlive(e) && cgf.HasComponent<BehaviorState>(e)
            && registry.TryGetName(cgf.GetComponent<BehaviorState>(e).ActiveBehaviorHash, out var n) ? n + Moving(cgf, e) : "-";
        string State() => $"insurgents [{string.Join(" ", insurgents.Select(e => Where(cgf, e)))}] civilians [{Civ(0)} {Civ(1)}] "
                        + $"blue [{string.Join(" ", blue.Select(e => Where(cgf, e) + ":" + Runs(e)))}] nav [{string.Join(" ", new[] { "Blue Leader", "Blue Rifleman 1", "Blue Rifleman 2", "Blue Rifleman 3", "Blue Rifleman 4" }.Select(n => Nav(harness.SimHost.World!, n)))}] squad {Runs(squad)} cgf {Doors(cgf, "front", "a", "c", "d", "e")} sim {Doors(harness.SimHost.World!, "front")} drill {Drill(cgf, squad)} progress [{Rooms()}]";

        bool Done() => reports.Progress().Any(p => p.Kind is ProgressKind.BuildingClear);
        // ⭐ who hit whom (SimHost fires every round: its shot log names the shooter)
        var sim = harness.SimHost.World!;
        var hits = new Hits();
        List<(string Hit, string By, string Text)> CivilianHits() => hits.Where((h, _) => h.StartsWith("Civilian"));
        static bool Blue(string name) => name.StartsWith("Blue");
        for (int f = 0; f < 60 * 480 && !Done(); f += 600)
        {
            for (int g = 0; g < 600; g += 60) { harness.PumpFrames(60); reports.Collect(cgf, squad); hits.Collect(sim); }
            _out.WriteLine($"f{f}: {State()} civhits [{Hits.Text(CivilianHits())}]");
        }
        _out.WriteLine("end: " + State());
        var progress = reports.Progress();
        var cleared = progress.Where(p => p.Kind == ProgressKind.RoomClear).Select(p => RoomAt(p.At)).ToList();
        Assert.True(insurgents.All(e => !InTheFight(cgf, e)), $"③ both insurgents down; {State()}");
        // ⭐ unhurt = no round struck him, and if he is still here his health is full. ⚠ A civilian panicked INSIDE the house hides and
        //   then deletes himself (CE-2236, R-4035 — the crowd model): "gone" is not "hurt" — BC-2 owns holding him (CE-2337)
        Assert.True(CivilianHits().Count == 0 && Enumerable.Range(0, 2).All(i => Hp(default, i) is < 0f or >= 100f), $"③ both civilians unhurt; {State()} civhits [{Hits.Text(CivilianHits())}]");
        var lost = blue.Where(e => !InTheFight(cgf, e)).Select(e => cgf.IsAlive(e) ? cgf.GetComponent<EntityInfo>(e).Name.ToString() : "gone").ToList();
        _out.WriteLine($"casualties: {lost.Count} [{string.Join(", ", lost)}] · hits on blue [{Hits.Text(hits.Where((h, _) => Blue(h)))}] · hits by blue [{Hits.Text(hits.Where((_, by) => Blue(by)))}] · fired [{hits.Fired}]");
        var friendlyFire = hits.Where((h, by) => Blue(h) && Blue(by));
        Assert.True(friendlyFire.Count == 0, $"③ (AQ99 B) no friend hit by a friend: [{Hits.Text(friendlyFire)}]; {State()}");
        foreach (var room in new[] { "hall", "A", "B", "C", "D", "E" })
            Assert.True(cleared.Count(r => r == room) == 1, $"① {room} cleared exactly once; {State()}");
        Assert.True(cleared.IndexOf("E") > cleared.IndexOf("D"), $"① E (reached only through D) after D; {State()}");
        Assert.Equal(ProgressKind.BuildingClear, progress[^1].Kind);
    }
}
