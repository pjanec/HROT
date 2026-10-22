using System;
using System.Collections.Generic;
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
using Fdp.Toolkit.Squad.Command;
using Fdp.Toolkit.Squad.Maneuvers;
using Fdp.Toolkit.Behavior.Systems;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using Xunit.Abstractions;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐ <c>CE-2296</c> — the platoon demos' in-process twins (📄 <c>docs/designs/group-maneuvers/DESIGN_Platoon_Attack_Defend_Clear.md</c>
/// §12): each shipped <c>scenarios/</c> demo run on the real cluster, asserting what its <c>run_*</c> live check in
/// <c>scripts/utility-demo-check.py</c> asserts.
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class PlatoonScenarioTests : IDisposable
{
    private const int DomainBase = 120;   // CycloneDDS accepts 0–232; 120 used by no other rail (grep of domain ids, 2026-10-19)
    private static int _domainSeq = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    private readonly List<string> _staged = new();
    private readonly ITestOutputHelper _out;

    public PlatoonScenarioTests(ITestOutputHelper output) => _out = output;

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

    // ── sq-defend-house (§12.2 stage B; DESIGN_Defend_And_Attack_Position §8c, CE-2275) ──────────────────────────────────

    private static readonly Vector3 DhOrigin = new(120.9f, 157f, 0f);
    private const float DhLeft = 150f, DhRight = 210f, DhRange = 90f;

    /// <summary>
    /// ⭐⭐ <c>CE-2275</c> — the twin of <c>run_sq_defend_house</c>: a Red squad holding fire (operator) defends OBJ on
    /// <c>cqb-village</c> with its sector south; a Blue fire team walks up from the south. The squad posts · ③ no Red round before the
    /// first Blue is inside the sector (DA-35), then Red fires · D-2h: the machine gunner's visibility fan is answered by the real solver.
    /// ⚠ PROGRESS Set is its own test below (blocked by <c>CE-2304</c>).
    /// </summary>
    [Fact(Timeout = 420_000)]
    public Task CE2275_SqDefendHouse_TheSquadMansOBJ_HoldsFireUntilBlueIsInItsSector_ThenFights() => RunSqDefendHouse(requireSet: false);

    /// <summary>⭐ <c>CE-2275</c> — the same run, and every man reaches his post: PROGRESS Set (D-2d, D-2e built). ⛔ Skipped on
    /// <c>CE-2304</c>: measured live, four of seven men stall 4–6 m from their posts inside OBJ with their moves <c>InProgress</c> for
    /// good (backend, docs/blueprints/batches/NOTE_Behaviors_To_Backend_Interior_Move_Stall.md). Unskip when it lands.</summary>
    [Fact(Timeout = 420_000, Skip = "CE-2304: Set in 4 of 5 live runs; in the 5th three men stop ~6 m from their posts — the failure names them")]
    public Task CE2275_SqDefendHouse_EveryManReachesHisPost_ProgressSet() => RunSqDefendHouse(requireSet: true);

    private async Task RunSqDefendHouse(bool requireSet)
    {
        using var harness = await Live("sq-defend-house");
        var cgf = harness.Cgf!.World!;
        var sim = harness.SimHost.World!;
        string[] reds = new[] { "Red Leader" }.Concat(Enumerable.Range(1, 6).Select(i => $"Red Rifleman {i}")).ToArray();
        string[] blues = Enumerable.Range(1, 4).Select(i => $"Blue {i}").ToArray();
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Red Squad").IsNull && reds.Concat(blues).All(n => !ByName(sim, n).IsNull), timeoutFrames: 2000),
            "the Red squad and the Blue fire team must spawn");
        var squad = ByName(cgf, "Red Squad");

        // the operator gives the squad its sector south (what POST /entities/{id}/sector does)
        Assert.True(EngagementAreaSystem.Apply(cgf, new SetEngagementAreaEvent
        {
            Entity = squad, Origin = BehaviorOrigin.Operator, ArcSet = 1, ArcOrigin = DhOrigin,
            LeftBearing = DhLeft, RightBearing = DhRight, MaxRange = DhRange,
        }), "the sector is applied");

        SquadCognitiveState Squad() => cgf.GetComponent<SquadCognitiveState>(squad);
        Assert.True(harness.PumpUntil(() => cgf.HasComponent<SquadCognitiveState>(squad) && Squad().ManeuverKind == SquadDefendManeuver.ManeuverKind
                                         && Squad().PhaseId == SquadDefendManeuver.PhasePosting, timeoutFrames: 6000),
            "the squad reaches OBJ and starts posting");

        bool Progress(ProgressKind kind)
        {
            if (!cgf.HasComponent<ReportOutbox>(squad)) return false;
            var q = cgf.GetComponentRO<ReportOutbox>(squad).Queued;
            for (int i = 0; i < q.Count; i++)
                if (q.Items[i].Report.Kind == ReportKind.Progress && q.Items[i].Report.Subtype == (byte)kind) return true;
            return false;
        }
        int Ammo(EntityRepository w, string n) => w.HasComponent<WeaponState>(ByName(w, n)) ? w.GetComponent<WeaponState>(ByName(w, n)).Ammo : -1;
        bool InSector(Vector3 p)
        {
            var d = new Vector2(p.X - DhOrigin.X, p.Y - DhOrigin.Y);
            if (d.Length() > DhRange) return false;
            float b = (MathF.Atan2(d.X, d.Y) * 180f / MathF.PI + 360f) % 360f;
            return b >= DhLeft && b <= DhRight;
        }

        var ammo0 = reds.ToDictionary(n => n, n => Ammo(cgf, n));   // the brain spends the rounds (FireDistributionScenarioTests reads them there)
        bool set = false, blueIn = false, earlyRound = false;
        var shooters = new HashSet<string>();
        // ⭐ CE-2304 — the frame the squad came closest to Set (its farthest live man nearest his post), and each man's distance then
        float bestWorst = float.MaxValue; double bestAt = 0; string bestMen = "";
        var nearest = new Dictionary<string, (Vector3 post, float min)>();   // ⭐ DA-58 — each man's nearest approach to his CURRENT post
        void OffPost()
        {
            var tr = Fdp.Toolkit.Squad.SquadDrillTrace.Of(cgf, squad);
            float worst = 0f; var men = new List<string>();
            foreach (var o in tr.Orders)
            {
                if (o.Kind != 5 || !cgf.IsAlive(o.Member) || !cgf.HasComponent<SimTransform>(o.Member)) continue;
                float d = Vector3.Distance(cgf.GetComponent<SimTransform>(o.Member).Position, o.Point);
                worst = MathF.Max(worst, d);
                string who = cgf.GetComponent<EntityInfo>(o.Member).Name.ToString().Replace("Red ", "");
                men.Add($"{who} {d:F1}");
                nearest[who] = nearest.TryGetValue(who, out var n) && Vector3.Distance(n.post, o.Point) < 0.05f ? (o.Point, MathF.Min(n.min, d)) : (o.Point, d);
            }
            if (men.Count > 0 && worst < bestWorst) { bestWorst = worst; bestAt = harness.Cgf!.World!.GetSingleton<GlobalTime>().TotalTime; bestMen = string.Join(", ", men); }
        }
        string NeverReached() => string.Join(", ", nearest.Where(kv => kv.Value.min > Hrot.AI.Behaviors.Brains.SquadDefendPlay.PostRadius)
            .Select(kv => $"{kv.Key} nearest {kv.Value.min:F1} m to ({kv.Value.post.X:F1},{kv.Value.post.Y:F1},{kv.Value.post.Z:F1})"));
        for (int frame = 0; frame < 9000 && !((set || !requireSet) && blueIn && shooters.Count >= 2); frame++)
        {
            harness.PumpFrames(1);
            set |= Progress(ProgressKind.Set);
            OffPost();
            bool inNow = blues.Any(n => InSector(sim.GetComponent<SimTransform>(ByName(sim, n)).Position));
            foreach (var n in reds)
                if (Ammo(cgf, n) < ammo0[n])
                {
                    if (!blueIn && !inNow) earlyRound = true;
                    shooters.Add(n);
                }
            blueIn |= inNow;
        }
        _out.WriteLine($"set={set} blueIn={blueIn} early={earlyRound} shooters={string.Join(",", shooters)}");
        _out.WriteLine($"closest to Set at {bestAt:F1} s — farthest man {bestWorst:F1} m from his post (PostRadius 4): {bestMen}");
        _out.WriteLine($"never reached his current post: {NeverReached()}");
        // ⭐ D-2h (DA-48) — the squad has a machine gunner (Red Rifleman 1), so the post plan asked ONE visibility fan; the real
        //   solver's answer must reach the brain (WorldSurveyApplySystem announces it as SurveyReady on the commander)
        double surveyAt = cgf.HasComponent<Fdp.Toolkit.Behavior.Components.RecentSenses>(squad)
            ? cgf.GetComponentRO<Fdp.Toolkit.Behavior.Components.RecentSenses>(squad).SurveyReady : 0;
        _out.WriteLine($"fan answered at {surveyAt:F2} s");

        if (requireSet) Assert.True(set, $"PROGRESS Set — every man on his post (D-2d, D-2e); never reached: {NeverReached()}");
        Assert.True(blueIn, "the Blue fire team walks into the sector");
        Assert.False(earlyRound, "③ no Red round before the first Blue enters the sector (DA-35)");
        Assert.True(shooters.Count >= 2, $"③ …then Red fires: {shooters.Count} shooters");
        Assert.True(surveyAt > 0, "D-2h — the machine gunner's visibility fan was answered by the real solver (SurveyReady on the commander)");
    }

    // ── pc-defend-house (R-2066; DESIGN_Defend_And_Attack_Position §8f.2, CE-2342…CE-2346) ────────────────────────────────

    /// <summary>
    /// ⭐⭐ R-2066 — the twin of <c>pc-defend-house</c>: Blue's platoon attacks OBJ on <c>cqb-village</c>; its support squad's base of fire is
    /// the 1 m SBF berm (x 90–150, faces at y 39 / 41). A (<c>CE-2342</c>): every support post on the berm is on its SOUTH face, the berm
    /// between the man and OBJ · B (<c>CE-2345</c>): the posts at least 5 m apart · D (<c>CE-2346</c>): once posting, the support squad is
    /// never sent back to "move to the point" when the attack's phase changes. Casualties and the cared-for count are logged (E,
    /// <c>CE-2344</c>, is pinned by its unit rail — a live lull is not deterministic). 📌 Before: posts on the enemy face (run 1), or none and
    /// the men in a 0.9 m clump (run 2); the leader sent back round the berm at t16.
    /// </summary>
    [Fact(Timeout = 420_000)]
    public async Task R2066_PcDefendHouse_TheBaseOfFirePostsBehindTheBerm_Spread_AndIsNotRestarted()
    {
        using var harness = await Live("pc-defend-house");
        var cgf = harness.Cgf!.World!;
        string[] blues = { "Blue Squad 1", "Blue Squad 2", "Blue Squad 3" };
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Blue Platoon").IsNull && blues.All(n => !ByName(cgf, n).IsNull), timeoutFrames: 2000),
            "the Blue platoon and its squads must spawn");
        int defend = Fdp.Toolkit.Behavior.BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.SquadDefend);
        int Task(Entity e) => cgf.HasComponent<BehaviorState>(e) ? cgf.GetComponent<BehaviorState>(e).ActiveBehaviorHash : 0;
        Entity support = Entity.Null;
        Assert.True(harness.PumpUntil(() => (support = blues.Select(n => ByName(cgf, n)).FirstOrDefault(e => Task(e) == defend)) != default
                                             && !support.IsNull, timeoutFrames: 4000), "the support squad is ordered to SquadDefend");
        SquadCognitiveState Squad() => cgf.GetComponent<SquadCognitiveState>(support);
        bool Posting() => cgf.HasComponent<SquadCognitiveState>(support) && Squad().ManeuverKind == SquadDefendManeuver.ManeuverKind
                          && Squad().PhaseId == SquadDefendManeuver.PhasePosting;
        Assert.True(harness.PumpUntil(Posting, timeoutFrames: 6000), "the support reaches the berm and starts posting");

        var posts = new Dictionary<Entity, Vector3>();
        int restarts = 0;
        bool wasPosting = true;
        for (int frame = 0; frame < 5400; frame++)   // 90 s
        {
            harness.PumpFrames(1);
            foreach (var o in Fdp.Toolkit.Squad.SquadDrillTrace.Of(cgf, support).Orders)
                if (o.Kind == 5) posts[o.Member] = o.Point;   // Watch: his post
            bool posting = Posting();
            bool moving = cgf.HasComponent<SquadCognitiveState>(support) && Squad().ManeuverKind == SquadDefendManeuver.ManeuverKind
                          && Squad().PhaseId == SquadDefendManeuver.PhaseMoving;
            if (wasPosting && moving) restarts++;
            wasPosting = posting;
        }

        int down = 0, cared = 0;
        foreach (var n in blues)
        {
            var squad = ByName(cgf, n);
            down += Fdp.Toolkit.Squad.SquadCasualties.Count(cgf, squad);
            cared += System.Numerics.BitOperations.PopCount(Fdp.Toolkit.Squad.SquadCasualties.CaredFor(cgf, squad));
        }
        _out.WriteLine($"support posts: {string.Join(" ", posts.Values.Select(p => $"({p.X:F1},{p.Y:F1})"))} · restarts {restarts} · down {down} · cared for {cared}");

        static bool OnBerm(Vector3 p) => p.X is > 89f and < 151f && p.Y is > 36f and < 44f;
        Assert.True(posts.Count >= 3, $"the support posted its men: {posts.Count} posts");
        Assert.True(posts.Values.Count(OnBerm) >= 2, "A — the support's posts are on the berm");
        Assert.All(posts.Values.Where(OnBerm), p => Assert.True(p.Y < 40f, $"A — ({p.X:F1},{p.Y:F1}) is on the berm's enemy face"));
        var at = posts.Values.ToArray();
        for (int i = 0; i < at.Length; i++)
            for (int j = i + 1; j < at.Length; j++)
                Assert.True(Vector3.Distance(at[i], at[j]) >= Hrot.AI.Behaviors.Brains.DefencePostPicker.CoverSpacingShortMetres - 0.01f,
                    $"B — posts ({at[i].X:F1},{at[i].Y:F1}) and ({at[j].X:F1},{at[j].Y:F1}) are under 5 m apart");
        Assert.Equal(0, restarts);
    }

    // ── pc-defend-dry (§12.2 stage A; DESIGN_Defend_And_Attack_Position §8d, CE-2276) ──────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <c>CE-2276</c> E-1 — the twin of <c>run_pc_defend_dry</c>: a Red platoon (with its default SOP) over two line squads and a
    /// reserve on <c>cqb-village</c>; the squads' Set and the SPOTs are faked (<see cref="ReportInjection"/>, what
    /// <c>POST /entities/{id}/reports</c> calls). ① every squad SquadDefends; the line holds fire, the reserve returns it · ② a SPOT outside
    /// the EA changes nothing · ③ one inside opens every line man's fire; the reserve keeps ReturnFire · ④ the SOP never pauses it.
    /// </summary>
    [Fact(Timeout = 420_000)]
    public async Task CE2276_PcDefendDry_TheLineHoldsFire_UntilAnEnemyIsReportedInsideTheEngagementArea()
    {
        using var harness = await Live("pc-defend-dry");
        var cgf = harness.Cgf!.World!;
        string[] squads = { "Red Squad 1", "Red Squad 2", "Red Reserve" };
        string[] Men(string s) => new[] { $"{s} Leader" }.Concat(Enumerable.Range(1, 3).Select(i => $"{s} Rifleman {i}")).ToArray();
        var line = Men(squads[0]).Concat(Men(squads[1])).ToArray();
        var reserve = Men(squads[2]);
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Red Platoon").IsNull && squads.Concat(line).Concat(reserve).All(n => !ByName(cgf, n).IsNull),
            timeoutFrames: 2000), "the platoon, its squads and their men must spawn");
        var platoon = ByName(cgf, "Red Platoon");
        int defend = Fdp.Toolkit.Behavior.BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.SquadDefend);
        int platoonDefend = Fdp.Toolkit.Behavior.BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.PlatoonDefend);
        int Task(Entity e) => cgf.HasComponent<BehaviorState>(e) ? cgf.GetComponent<BehaviorState>(e).ActiveBehaviorHash : 0;
        RoeFire Fire(string n) => RoeOf.Fire(cgf, ByName(cgf, n));

        Assert.True(harness.PumpUntil(() => squads.All(s => Task(ByName(cgf, s)) == defend), timeoutFrames: 3000),
            "① every squad is ordered to SquadDefend its point");
        Assert.True(harness.PumpUntil(() => line.All(n => Fire(n) == RoeFire.HoldFire) && reserve.All(n => Fire(n) == RoeFire.ReturnFire), timeoutFrames: 600),
            $"① the line holds fire, the reserve returns it (line {string.Join(",", line.Select(Fire))}, reserve {string.Join(",", reserve.Select(Fire))})");

        foreach (var s in squads.Take(2))
            Assert.Null(ReportInjection.Inject(cgf, ByName(cgf, s), new CommandReport { Kind = ReportKind.Progress, Subtype = (byte)ProgressKind.Set }));
        harness.PumpFrames(120);
        Assert.Null(ReportInjection.Inject(cgf, ByName(cgf, squads[0]), new CommandReport
        {
            Kind = ReportKind.Spot, At = new Vector3(120f, 260f, 0f), Count = 3, Size = ElementSize.Team,
        }));
        harness.PumpFrames(240);
        Assert.True(line.All(n => Fire(n) == RoeFire.HoldFire), "② a SPOT north of OBJ — outside the EA — changes nothing");

        Assert.Null(ReportInjection.Inject(cgf, ByName(cgf, squads[1]), new CommandReport
        {
            Kind = ReportKind.Spot, At = new Vector3(120f, 60f, 0f), Count = 3, Size = ElementSize.Team,
        }));
        Assert.True(harness.PumpUntil(() => line.All(n => Fire(n) == RoeFire.FireAtWill), timeoutFrames: 120),
            $"③ a SPOT inside the EA opens every line man's fire ({string.Join(",", line.Select(Fire))})");
        Assert.True(reserve.All(n => Fire(n) == RoeFire.ReturnFire), "③ …the reserve still only returns fire");
        Assert.Equal(platoonDefend, Task(platoon));
        Assert.False(cgf.HasComponent<PausedTask>(platoon), "④ the platoon's SOP did not pause the defence (DA-55)");
        Assert.True(squads.All(s => Task(ByName(cgf, s)) == defend), "④ …no squad was pulled off to hold");
    }
    /// <summary>
    /// ⭐⭐ <c>CE-2284</c> U-1 — <c>pc-urban-move</c>: a Blue platoon on <c>UrbanMovement</c> along Main Street, then south down the Cross Road,
    /// no enemy. ① the lead squad marches with its crossing drill · ② the others trail it along the route (never with it) · ③ all three
    /// end in order at the route's end and the task ends. 📄 Defend and Attack §11b.
    /// </summary>
    [Fact(Timeout = 600_000)]
    public async Task CE2284_PcUrbanMove_TheLeadWalksTheLegs_TheOthersTrailIt_AndTheTaskEnds()
    {
        using var harness = await Live("pc-urban-move");
        var cgf = harness.Cgf!.World!;
        string[] squads = { "Blue Squad 1", "Blue Squad 2", "Blue Squad 3" };
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Blue Platoon").IsNull && squads.All(n => !ByName(cgf, n).IsNull), timeoutFrames: 2000),
            "the platoon and its squads must spawn");
        var platoon = ByName(cgf, "Blue Platoon");
        int urban = Fdp.Toolkit.Behavior.BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.UrbanMovement);
        int march = Fdp.Toolkit.Behavior.BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.SquadMarch);
        int Task(Entity e) => cgf.HasComponent<BehaviorState>(e) ? cgf.GetComponent<BehaviorState>(e).ActiveBehaviorHash : 0;
        Vector3 At(string n) => StaffComposerSystem.CentreOf(cgf, ByName(cgf, n));
        string Where() => string.Join(" ", squads.Select(n => $"{n.Replace("Blue ", "")}({At(n).X:F0},{At(n).Y:F0})"));

        Assert.True(harness.PumpUntil(() => squads.All(n => Task(ByName(cgf, n)) == march), timeoutFrames: 1200),
            "① every squad is ordered to march");
        float closest = float.MaxValue;
        bool ended = harness.PumpUntil(() =>
        {
            closest = MathF.Min(closest, Vector2.Distance(new Vector2(At(squads[0]).X, At(squads[0]).Y), new Vector2(At(squads[1]).X, At(squads[1]).Y)));
            return Task(platoon) != urban;
        }, timeoutFrames: 24000);
        // the squads' last marches end a few seconds after the task (each on its point man's arrival)
        harness.PumpUntil(() => squads.All(n => Task(ByName(cgf, n)) != march), timeoutFrames: 1800);
        // how far each centre stands from the route's end, along the route (Main Street east to the Cross Road, then south)
        float ToEnd(Vector3 p) => p.Y < 190f && MathF.Abs(p.X - 200f) < 12f ? p.Y - 120f : 70f + MathF.Max(0f, 200f - p.X);
        // a squad marches in file and its march ends on its point man's arrival: judge where it got to by its FRONT man
        float Front(string n)
        {
            var squad = ByName(cgf, n);
            if (!cgf.HasComponent<UnitRoster>(squad)) return ToEnd(At(n));
            ref readonly var roster = ref cgf.GetComponentRO<UnitRoster>(squad);
            float best = float.MaxValue;
            for (int i = 0; i < roster.Count; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (cgf.IsAlive(m) && cgf.HasComponent<SimTransform>(m)) best = MathF.Min(best, ToEnd(cgf.GetComponentRO<SimTransform>(m).Position));
            }
            return best == float.MaxValue ? ToEnd(At(n)) : best;
        }
        string Along() => string.Join(" ", squads.Select(n => $"{n.Replace("Blue ", "")}:front {Front(n):F0}/centre {ToEnd(At(n)):F0}"));
        _out.WriteLine($"end: {Where()} · to the end {Along()} · platoon task {Task(platoon)} · closest lead–follower {closest:F1} m");
        Assert.True(ended, $"③ the task ends; {Where()}");
        Assert.True(Vector2.Distance(new Vector2(At(squads[0]).X, At(squads[0]).Y), new Vector2(200f, 120f)) < 16f, $"③ the lead at the route's end; {Where()}");
        Assert.True(Front(squads[1]) > Front(squads[0]) + 15f && Front(squads[2]) > Front(squads[1]) + 15f,
            $"③ in order along the route, each a bound behind the one ahead; {Along()}");
        Assert.True(closest > 12f, $"② the follower never walked with the lead (closest {closest:F1} m)");
    }
    /// <summary>The platoon task's name hash and the squads' own task hashes, sampled each frame, for the U-2 twins.</summary>
    private static (bool Reacted, bool Cleared, bool Resumed) WatchAmbush(HrotRunnerHarness harness, EntityRepository cgf, Entity platoon,
                                                                          string[] squads, int frames, Func<bool>? stop = null,
                                                                          Action<string>? log = null)
    {
        // the task names by hash, for the timeline a failing run prints
        var names = typeof(Hrot.Map.Definitions.Behavior.BehaviorNames).GetFields()
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .GroupBy(n => Fdp.Toolkit.Behavior.BehaviorHash.FromName(n)).ToDictionary(g => g.Key, g => g.First());
        string Name(int h) => h == 0 ? "-" : names.TryGetValue(h, out var n) ? n : h.ToString();
        int frame = 0;
        string last = "";
        int urban = Fdp.Toolkit.Behavior.BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.UrbanMovement);
        int react = Fdp.Toolkit.Behavior.BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.ReactToFireFromBuilding);
        int clear = Fdp.Toolkit.Behavior.BehaviorHash.FromName(Hrot.Map.Definitions.Behavior.BehaviorNames.ClearBuilding);
        int Task(Entity e) => cgf.HasComponent<BehaviorState>(e) ? cgf.GetComponent<BehaviorState>(e).ActiveBehaviorHash : 0;
        bool reacted = false, cleared = false, resumed = false;
        harness.PumpUntil(() =>
        {
            int t = Task(platoon);
            if (t == react) reacted = true;
            else if (reacted && t == urban) resumed = true;
            if (squads.Any(n => Task(ByName(cgf, n)) == clear)) cleared = true;
            if (log != null && frame++ % 120 == 0)
            {
                string now = $"{Name(t)} | " + string.Join(" ", squads.Select(n => $"{n.Replace("Blue ", "")}:{Name(Task(ByName(cgf, n)))}"));
                if (now != last || (frame - 1) % 1200 == 0)
                {
                    log($"f{frame - 1}: {now} @ " + string.Join(" ", squads.Select(n =>
                    {
                        var c = StaffComposerSystem.CentreOf(cgf, ByName(cgf, n));
                        return $"({c.X:F0},{c.Y:F0})";
                    })) + " progress " + string.Join(",", Enumerable.Range(0, squads.Length).Select(i =>
                        Fdp.Toolkit.Squad.Command.CommandPictureOps.ProgressOf(cgf, platoon, i))));
                    last = now;
                }
            }
            return (stop?.Invoke() ?? false) || (reacted && resumed);
        }, timeoutFrames: frames);
        return (reacted, cleared, resumed);
    }

    private static int Down(EntityRepository cgf, string prefix, int count)
        => Enumerable.Range(1, count).Count(i => { var e = ByName(cgf, $"{prefix} {i}"); return e.IsNull || !cgf.IsAlive(e) || !Fdp.Toolkit.Combat.Injury.IsInTheFight(cgf, e); });

    /// <summary>
    /// ⭐⭐ <c>CE-2284</c> U-3 — <c>pc-urban-ambush</c>: moving east down Main Street, the platoon is fired on from H5 beside the route ⇒ the
    /// SOP reacts with <c>ReactToFireFromBuilding</c> (DA-18: within 50 m of the route), a squad clears H5, and the movement resumes.
    /// </summary>
    [Fact(Timeout = 900_000)]
    public async Task CE2284_PcUrbanAmbush_FiredOnFromABuildingBesideTheRoute_ThePlatoonClearsIt_AndMovesOn()
    {
        using var harness = await Live("pc-urban-ambush");
        var cgf = harness.Cgf!.World!;
        string[] squads = { "Blue Squad 1", "Blue Squad 2", "Blue Squad 3" };
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Blue Platoon").IsNull && squads.All(n => !ByName(cgf, n).IsNull), timeoutFrames: 2000),
            "the platoon and its squads must spawn");
        var (reacted, cleared, resumed) = WatchAmbush(harness, cgf, ByName(cgf, "Blue Platoon"), squads, 36000, log: _out.WriteLine);
        _out.WriteLine($"reacted {reacted} · a squad cleared {cleared} · resumed {resumed} · insurgents down {Down(cgf, "Insurgent", 4)}/4");
        Assert.True(reacted, "① the platoon reacts to the fire from H5 with ReactToFireFromBuilding");
        Assert.True(cleared, "② a squad clears H5");
        Assert.True(resumed, "③ the movement resumes");
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2284</c> U-3 — <c>pc-ambush-bypass</c>: the route turns south at the Cross Road; fire from SNP, ~100 m off the route — a
    /// Regular platoon BYPASSES it (DA-18): no <c>ReactToFireFromBuilding</c>, no squad sent into SNP, the movement reaches its end.
    /// </summary>
    [Fact(Timeout = 900_000)]
    public async Task CE2284_PcAmbushBypass_FireFromABuildingOffTheRoute_ThePlatoonBypassesIt()
    {
        using var harness = await Live("pc-ambush-bypass");
        var cgf = harness.Cgf!.World!;
        string[] squads = { "Blue Squad 1", "Blue Squad 2", "Blue Squad 3" };
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Blue Platoon").IsNull && squads.All(n => !ByName(cgf, n).IsNull), timeoutFrames: 2000),
            "the platoon and its squads must spawn");
        var platoon = ByName(cgf, "Blue Platoon");
        bool Ended() => cgf.HasComponent<BehaviorState>(platoon) && cgf.GetComponent<BehaviorState>(platoon).ActiveBehaviorHash == 0;   // the task done
        harness.PumpFrames(600);
        var (reacted, cleared, _) = WatchAmbush(harness, cgf, platoon, squads, 30000, Ended, _out.WriteLine);
        _out.WriteLine($"reacted {reacted} · a squad cleared {cleared} · ended {Ended()}");
        Assert.False(reacted, "① no ReactToFireFromBuilding — SNP is off the route");
        Assert.False(cleared, "② no squad sent into SNP");
        Assert.True(Ended(), "③ the movement reaches its end");
    }
}
