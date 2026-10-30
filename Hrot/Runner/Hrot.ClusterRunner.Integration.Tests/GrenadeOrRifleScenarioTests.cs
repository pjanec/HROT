using Fdp.Testing;
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Orchestration;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using Xunit.Abstractions;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐ <c>WL-4</c> (U-W7, W-12e/f — 📄 docs/DESIGN_Weapon_Layers.md §5f) — <c>ua-grenade-or-rifle</c> in-process: a hostile stands
/// 2.5 m behind <c>bt-range</c>'s 1.2 m sandbags for 3 s (both attackers see him), then crouches out of sight. The Rifleman and the
/// Grenadier (rifle + M67) advance on CombatPosture. Expected: the Grenadier closes to a throw (CloseToThrow) and the WEAPON BRAIN
/// throws at the last-known spot (mount 1 spends a grenade); the muscle lobs it over the sandbags (L-1) and the hostile is hurt behind
/// them. 🔴 Red before: nothing threw at a hidden target, and a low throw burst against the wall.
/// <para>⭐ AQ89 (R-269): an equally armed hidden hostile makes the posture SUPPRESS, not advance — the Grenadier is authored BRAVE
/// (<c>"Temperament": {"riskAcceptance": 0.8}</c>), so he closes to throw inside the Suppress branch under his own blind fire.
/// 🔴 Measured reds on the way: Suppress idled until the memory faded; then the temperament was dropped by the CGF staging
/// extractor (the translator was not <c>IsExtractionSafe</c>).</para>
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class GrenadeOrRifleScenarioTests : IDisposable
{
    private const int DomainBase = 58;    // 52–53 posture, 54–55 heard shot, 56 weapon choice, 57 used, 60–69 sensors (grep of DomainBase, 2026-10-11)
    private static int _domainSeq = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    private readonly string _scenarioId = "ua_grenade_" + Guid.NewGuid().ToString("N");
    private readonly ITestOutputHelper _out;

    public GrenadeOrRifleScenarioTests(ITestOutputHelper output) => _out = output;
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

    private static int GrenadesLeft(EntityRepository world, Entity grenadier)
    {
        Span<Entity> mounts = stackalloc Entity[8];
        int n = WeaponMountQuery.EnumerateMounts(world, grenadier, mounts);
        for (int i = 0; i < n; i++)
            if (world.HasComponent<WeaponMountInfo>(mounts[i]) && world.GetComponentRO<WeaponMountInfo>(mounts[i]).MountIndex == 1)
                return world.GetComponentRO<WeaponState>(mounts[i]).Ammo;
        return -1;
    }

    /// <summary>Every condition of the throw (HiddenTargetThrow), as the Brain sees it — the trace a red run needs.</summary>
    private void Trace(EntityRepository cgf, Entity unit, Entity target, int frame)
    {
        if (!cgf.IsAlive(unit)) return;
        var at = cgf.GetComponent<SimTransform>(unit).Position;
        bool mount = HiddenTargetThrow.ThrowableMount(cgf, unit, out var m, out int mi, out var dto, out var wh);
        bool known = HiddenTargetThrow.LastKnown(cgf, unit, target, out var point);
        bool seen = Fdp.Toolkit.Perception.SightNow.Sees(cgf, unit, target);
        var ch = cgf.HasComponent<Fdp.Toolkit.Behavior.Components.WeaponChannel>(unit) ? cgf.GetComponent<Fdp.Toolkit.Behavior.Components.WeaponChannel>(unit) : default;
        var row = cgf.HasComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(unit) ? cgf.GetComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(unit).Weapon : default;
        float cool = mount ? cgf.GetComponentRO<WeaponState>(m).CooldownSecondsRemaining : -1f;
        string posture = "?";
        unsafe
        {
            int hash = Fdp.Toolkit.Behavior.BehaviorHash.FromName("CombatPosture");
            if (Fdp.Toolkit.Behavior.RootParamsAccess.TryGetBlockFor<Hrot.AI.Behaviors.Brains.CombatPostureBehavior.CombatPostureBlackboard>(cgf, unit, hash, out var b))
                posture = $"choice={b->Choice.Winner} approach={b->ApproachChoice.Winner} throwMoving={b->ThrowState.Moving} throwGoal=({b->ThrowState.Goal.X:0.0},{b->ThrowState.Goal.Y:0.0}) flankMoving={b->FlankState.Moving}";
        }
        var loco = cgf.HasComponent<Fdp.Toolkit.Behavior.Components.LocomotionChannel>(unit) ? cgf.GetComponent<Fdp.Toolkit.Behavior.Components.LocomotionChannel>(unit) : default;
        int throwSensor = Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(cgf, unit, Hrot.AI.Behaviors.Brains.PostureNodes.ThrowScoreSite).IsNull ? 0 : 1;
        int closeSensor = Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(cgf, unit, Hrot.AI.Behaviors.Brains.EqsTacticsNodes.CloseToThrowSite).IsNull ? 0 : 1;
        bool hasT = cgf.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Components.CombatTemperament>() && cgf.HasComponent<Fdp.Toolkit.Behavior.Components.CombatTemperament>(unit);
        _out.WriteLine($"   temperament={hasT} risk={Fdp.Toolkit.Behavior.Components.TemperamentOf.RiskAcceptance(cgf, unit)} willing={Fdp.Toolkit.Behavior.Components.TemperamentOf.Willing(cgf, unit, false)}");
        _out.WriteLine($"   {posture} loco={loco.ActiveAction}/{loco.Status} throwScoreSensor={throwSensor} closeSensor={closeSensor}");
        _out.WriteLine($"f{frame} at ({at.X:0.0},{at.Y:0.0}) seen={seen} mount={mount}#{mi} cool={cool:0.0} known={known} pt=({point.X:0.0},{point.Y:0.0}) " +
                       $"dist={System.Numerics.Vector2.Distance(new(at.X, at.Y), new(point.X, point.Y)):0.0} lethal={(wh?.BlastLethalRadiusM ?? -1f):0.0} " +
                       $"friendly={(known && wh != null && HiddenTargetThrow.FriendlyInBurst(cgf, unit, point, wh.BlastLethalRadiusM))} " +
                       $"roe={Fdp.Toolkit.Combat.Executors.AimAndFireExecutor.RoePermitsFire(cgf, unit)} action={ch.ActiveAction} reason={row.Reason} " +
                       $"hp={(cgf.IsAlive(target) ? cgf.GetComponent<Health>(target).Current : -1f)}");
    }

    [Fact(Timeout = 600_000)]
    public async Task WL4_TheGrenadier_ClosesAndThrowsAtTheHiddenHostile_WhoIsHurtBehindTheSandbags()
    {
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(_scenarioId));
        File.Copy(Path.Combine(TestRepo.Root, "scenarios", "ua-grenade-or-rifle", "scenario.json"),
            Path.Combine(NasScenarioStaging.DirectoryOf(_scenarioId), "scenario.json"), overwrite: true);

        using var harness = new HrotRunnerHarness("simhost,ig,cgf", NextDomainId());
        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;
        var rosterDeadline = DateTime.UtcNow.AddSeconds(10.0);
        while (master.NodeRoster.ActiveNodes.Count == 0 && DateTime.UtcNow < rosterDeadline) { harness.PumpFrames(1); Thread.Sleep(10); }
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = JsonSerializer.Serialize(new { TargetState = nameof(Hrot.NED.Descriptors.Orchestration.ClusterState.OperatingLive), ScenarioId = _scenarioId }),
        }).ConfigureAwait(false);
        Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == 31, timeoutFrames: 4000), "cluster must reach OperatingLive");

        var cgf = harness.Cgf!.World!;
        const string Hostile = "Hostile behind the sandbags";
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Grenadier").IsNull && !ByName(cgf, "Rifleman").IsNull && !ByName(cgf, Hostile).IsNull,
            timeoutFrames: 2000), "the three units must spawn on CGF");
        Entity grenadier = ByName(cgf, "Grenadier"), hostile = ByName(cgf, Hostile), rifleman = ByName(cgf, "Rifleman");
        int rifle0 = cgf.GetComponentRO<WeaponState>(rifleman).Ammo;

        int grenades0 = GrenadesLeft(cgf, grenadier);
        Assert.True(grenades0 > 0, $"the Grenadier carries grenades on mount 1 (found {grenades0})");
        float hp0 = cgf.GetComponent<Health>(hostile).Current;

        // ── ⭐ WL-6 (docs/DESIGN_Weapon_Layers.md W-13, R-272) — the weapon brain's posture reaches every node: the Rifleman's, written
        //   on CGF, is seen on the SimHost and the IG (the 3-D figure's reader), aimed at the hostile there. ──
        var simWorld = harness.SimHost.World!;
        var igWorld = harness.Ig.App.World;
        var seen = new System.Collections.Generic.Dictionary<string, WeaponPostureKind>();
        bool igAimsAtTheHostile = false;
        void SamplePostures()
        {
            foreach (var (node, world) in new[] { ("cgf", cgf), ("sim", simWorld), ("ig", igWorld) })
            {
                if (world == null || !world.IsComponentTypeRegistered<WeaponPosture>()) continue;
                var r = ByName(world, "Rifleman");
                if (r.IsNull || !world.HasComponent<WeaponPosture>(r)) continue;
                var p = world.GetComponentRO<WeaponPosture>(r);
                if (!seen.TryGetValue(node, out var best) || p.Kind > best) seen[node] = p.Kind;
                if (node == "ig" && p.Kind == WeaponPostureKind.Aimed && p.Target == ByName(world, Hostile)) igAimsAtTheHostile = true;
            }
        }

        // ── the throw: the weapon brain spends a grenade at the hidden hostile's last-known spot ──
        int frame = 0;
        bool thrown = harness.PumpUntil(() =>
        {
            if (frame % 10 == 0) SamplePostures();
            if (++frame % 60 == 0) Trace(cgf, grenadier, hostile, frame);
            return GrenadesLeft(cgf, grenadier) < grenades0;
        }, timeoutFrames: 9000);
        var g = cgf.GetComponent<SimTransform>(grenadier).Position;
        var h = cgf.GetComponent<SimTransform>(hostile).Position;
        _out.WriteLine($"grenadier at {g}, hostile at {h}, grenades {GrenadesLeft(cgf, grenadier)}/{grenades0}, hostile hp {cgf.GetComponent<Health>(hostile).Current}");
        Assert.True(thrown, "the Grenadier must throw at the hidden hostile");
        _out.WriteLine($"WL-6 rifleman posture (highest seen): {string.Join(", ", seen)}; IG aimed at the hostile: {igAimsAtTheHostile}");
        Assert.Equal(WeaponPostureKind.Aimed, seen.GetValueOrDefault("cgf"));   // the weapon brain aimed him
        Assert.Equal(WeaponPostureKind.Aimed, seen.GetValueOrDefault("sim"));   // replicated (dtWeaponPosture)
        Assert.Equal(WeaponPostureKind.Aimed, seen.GetValueOrDefault("ig"));
        Assert.True(igAimsAtTheHostile, "on the IG the Rifleman's posture must name the hostile as its target");

        // ── ⭐ AQ89 A — the Rifleman (ordinary temperament) holds and SUPPRESSES BLIND: his rounds go at the hidden hostile's last-known
        //   spot, strike the sandbags (no hurt before the grenade) and spoil his aim — UnderFire, stamped by the near miss on the muscle.
        int rifle = cgf.GetComponentRO<WeaponState>(rifleman).Ammo;
        _out.WriteLine($"rifleman rounds fired {rifle0 - rifle}, hostile hp before the burst {cgf.GetComponent<Health>(hostile).Current}");
        Assert.True(rifle < rifle0, $"the Rifleman must fire blind at the hidden hostile (ammo {rifle}/{rifle0})");
        Assert.Equal(hp0, cgf.GetComponent<Health>(hostile).Current);   // his rounds hit the sandbags, not the man
        var sim = harness.SimHost.World!;
        var hostileOnSim = ByName(sim, Hostile);
        Assert.False(hostileOnSim.IsNull, "the hostile exists on SimHost");
        Assert.True(sim.IsComponentTypeRegistered<UnderFire>() && sim.HasComponent<UnderFire>(hostileOnSim)
                    && sim.GetComponentRO<UnderFire>(hostileOnSim).LastTime > 0.0,
            "the blind rounds' near misses must stamp UnderFire on the hostile (AQ85 E) — his aim is spoiled");

        // ── the burst: the lob clears the sandbags and the hostile is hurt behind them ──
        Assert.True(harness.PumpUntil(() => cgf.GetComponent<Health>(hostile).Current < hp0, timeoutFrames: 1200),
            $"the hostile behind the sandbags must be hurt by the grenade; hp {cgf.GetComponent<Health>(hostile).Current}/{hp0}");
    }
}
