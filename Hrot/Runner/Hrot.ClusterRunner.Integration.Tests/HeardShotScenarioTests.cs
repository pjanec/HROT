using System;
using System.IO;
using System.Numerics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Terrain;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using Xunit.Abstractions;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐ <c>CE-2106</c> — the shipped <c>tt-heard-shot</c> scenario on <c>test-town</c>, live: a rifleman (its SOP
/// <c>BasicInfantrySop</c>) stands south of the Tower; north of it a hostile fires at a decoy. The rifleman sees NEITHER — it only
/// HEARS the shots (the soldier types' authored sound, <c>UrbanCombatTkbCatalog</c>, and their implicit ears). The heard contact is
/// a FirstThreat, the SOP reacts with TakeCover, and TakeCover hides from the heard POINT (🔒 user, K2: "hide from a point is OK").
/// 📄 <c>docs/DESIGN_Thermal_And_Acoustic_Sensing.md</c> §8 / §8.1.
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class HeardShotScenarioTests : IDisposable
{
    private const int DomainBase = 54;    // CycloneDDS accepts 0–232; 54–55 used by no other rail (grep of domain ids, 2026-10-05)
    private static int _domainSeq = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    private readonly string _scenarioId = "tt_heard_shot_" + Guid.NewGuid().ToString("N");
    private readonly ITestOutputHelper _out;

    public HeardShotScenarioTests(ITestOutputHelper output) => _out = output;

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
    public async Task CE2106_ARiflemanHearsAHiddenShooter_AndTakesCoverFromWhereTheShotsCameFrom()
    {
        var root = RepoRoot();
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(_scenarioId));
        File.Copy(Path.Combine(root, "Hrot", "Subsystems", "Hrot.AI.Behaviors", "Recipes", "Scenarios", "tt-heard-shot", "scenario.json"),
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
        Assert.True(harness.PumpUntil(() => !ByName(sim, "Rifleman").IsNull && !ByName(sim, "Hidden Shooter").IsNull && !ByName(sim, "Decoy").IsNull
                                         && !ByName(cgf, "Rifleman").IsNull && !ByName(cgf, "Hidden Shooter").IsNull, timeoutFrames: 2000),
            "all three units must spawn on SimHost, the soldiers on CGF");
        Entity rifleman = ByName(cgf, "Rifleman"), shooter = ByName(cgf, "Hidden Shooter");
        Entity simRifleman = ByName(sim, "Rifleman"), simShooter = ByName(sim, "Hidden Shooter"), simDecoy = ByName(sim, "Decoy");

        Assert.True(harness.PumpUntil(() => sim.HasSingletonManaged<TerrainWorld>(), timeoutFrames: 2000), "test-town must be loaded on SimHost");
        var town = sim.GetSingletonManaged<TerrainWorld>();
        Vector3 Pos(Entity e) => sim.GetComponent<SimTransform>(e).Position;
        bool HiddenFrom(Entity viewer, Entity target)
        {
            var eye = Pos(viewer) + new Vector3(0, 0, EqsTerrainSight.Mount(sim, viewer).Standing);
            var aim = Pos(target) + new Vector3(0, 0, EqsTerrainSight.Mount(sim, target).Crouched);
            return town.SegmentBlocked(eye, aim);
        }
        Assert.True(HiddenFrom(simShooter, simRifleman), "precondition: the Tower hides the rifleman from the shooter");
        Assert.True(HiddenFrom(simDecoy, simRifleman), "precondition: … and from the decoy");
        Assert.False(HiddenFrom(simShooter, simDecoy), "precondition: the shooter sees the decoy it fires at");
        var start = Pos(simRifleman);

        string? Task() => registry.TryGetName(cgf.GetComponent<BehaviorState>(rifleman).ActiveBehaviorHash, out var n) ? n : null;
        unsafe string Memory()
        {
            if (!cgf.HasComponent<TargetMemory>(rifleman)) return "no memory";
            var m = cgf.GetComponent<TargetMemory>(rifleman);
            var s = $"memory {m.Count}:";
            for (int i = 0; i < m.Count; i++)
                s += $" [{(TargetMemory.IsAnonymous(in m, i) ? "heard" : "seen")} id={m.EntityIds[i]} at ({m.PositionsX[i]:F0},{m.PositionsY[i]:F0}) r={m.Radius[i]:F1} cls={m.SourceClass[i]}]";
            return s;
        }
        int ShooterAmmo() => cgf.HasComponent<WeaponState>(shooter) ? cgf.GetComponent<WeaponState>(shooter).Ammo : -1;
        unsafe string Shooter()
        {
            var t = cgf.HasComponent<BehaviorState>(shooter) && registry.TryGetName(cgf.GetComponent<BehaviorState>(shooter).ActiveBehaviorHash, out var n) ? n : "none";
            var s = $"shooter task={t}";
            if (cgf.HasComponent<TargetMemory>(shooter)) { var m = cgf.GetComponent<TargetMemory>(shooter); s += $" memory={m.Count}"; for (int i = 0; i < m.Count; i++) s += $" id={m.EntityIds[i]}"; }
            if (cgf.HasComponent<WeaponChannel>(shooter)) { var w = cgf.GetComponent<WeaponChannel>(shooter); s += $" weapon={w.ActiveAction}/{w.Status}"; }
            var d = ByName(cgf, "Decoy");
            s += $" decoyOnCgf={(d.IsNull ? "no" : d.PackedValue.ToString())}";
            return s;
        }
        float maxShot = 0f;   // the loudest shot state seen on the SimHost emitter
        int manual = -1; string manualInfo = "";
        int simSounds = 0, cgfSounds = 0;   // SoundContactEvents seen on each node's bus
        string Ears()
        {
            var s = "";
            foreach (var (w, e, tag) in new[] { (sim, simRifleman, "sim"), (cgf, rifleman, "cgf") })
            {
                var ear = Fdp.Toolkit.Perception.Sensors.UnitSensors.Of(w, e, SensorModality.Acoustic);
                s += $" {tag}Ears={(ear.IsNull ? "none" : ear.ToString())}";
                if (!ear.IsNull && w.HasComponent<EqsCognitiveBuffer>(ear))
                {
                    var b = w.GetComponentRO<EqsCognitiveBuffer>(ear);
                    s += $"(ready={b.IsReady} count={b.Count} tick={b.LastUpdateTick} bp={w.GetComponentRO<EqsSensor>(ear).BlueprintId:X8})";
                }
            }
            s += sim.HasComponent<Fdp.Toolkit.Perception.Signatures.AcousticEmitter>(simShooter)
                ? $" emitter(fire={sim.GetComponent<Fdp.Toolkit.Perception.Signatures.AcousticEmitter>(simShooter).FiringAudibleRange} maxShotLeft={maxShot:F2})"
                : " emitter=none";
            return s + $" soundEvents sim={simSounds} cgf={cgfSounds} manual:" + manualInfo;
        }
        string State() => $"task={Task()} {Memory()} shooterAmmo={ShooterAmmo()} pos={Pos(simRifleman)} start={start} | {Shooter()} |{Ears()}";

        // ① the shooter fires (the decoy is in its sight).
        Assert.True(harness.PumpUntil(() => ShooterAmmo() >= 0 && ShooterAmmo() < 30, timeoutFrames: 3000), $"the hidden shooter must fire; {State()}");

        // ② the rifleman HEARS it: a heard (anonymous) contact, and it never SEES the shooter.
        unsafe bool Heard()
        {
            if (!cgf.HasComponent<TargetMemory>(rifleman)) return false;
            var m = cgf.GetComponent<TargetMemory>(rifleman);
            for (int i = 0; i < m.Count; i++) if (TargetMemory.IsAnonymous(in m, i)) return true;
            return false;
        }
        bool HeardOrTrack()
        {
            if (sim.HasComponent<Fdp.Toolkit.Perception.Signatures.AcousticEmitter>(simShooter))
                maxShot = Math.Max(maxShot, sim.GetComponent<Fdp.Toolkit.Perception.Signatures.AcousticEmitter>(simShooter).ShotTimeLeft);
            simSounds += ((ISimulationView)sim).ReadEvents<Fdp.Toolkit.Perception.Events.SoundContactEvent>().Length;
            cgfSounds += ((ISimulationView)cgf).ReadEvents<Fdp.Toolkit.Perception.Events.SoundContactEvent>().Length;
            if (manual < 0 && maxShot > 0.3f)   // ⚠ CE-2106 diagnosis: the generator on the LIVE SimHost world, mid-shot
            {
                var ear = Fdp.Toolkit.Perception.Sensors.UnitSensors.Of(sim, simRifleman, SensorModality.Acoustic);
                var reg = sim.GetSingletonManaged<IEqsTemplateRegistry>()!;
                var cfgEar = sim.GetComponent<EqsSensor>(ear);
                if (reg.TryGetTemplate(cfgEar.BlueprintId, out var t))
                {
                    var buf = new EqsResult[t.MaxCandidates];
                    manual = t.Generator.Generate(ear, ref cfgEar, sim, buf);
                    manualInfo = $"gen={manual} self={EqsContext.Self(sim, ear, cfgEar)} ears={Fdp.Toolkit.Perception.Sensors.AcousticPerception.Ears(sim, ear, out var rr, out _)}/{rr}";
                    // the SAME on a snapshot, as the background solver reads it (OnDemandProvider: SyncFrom, all snapshotable)
                    using var snap = new EntityRepository();
                    snap.SyncFrom(sim);
                    var cfgSnap = snap.GetComponent<EqsSensor>(ear);
                    var t2 = reg.TryGetTemplate(cfgSnap.BlueprintId, out var tt) ? tt : t;
                    int genSnap = t2.Generator.Generate(ear, ref cfgSnap, snap, new EqsResult[t.MaxCandidates]);
                    var em = snap.HasComponent<Fdp.Toolkit.Perception.Signatures.AcousticEmitter>(simShooter) ? snap.GetComponent<Fdp.Toolkit.Perception.Signatures.AcousticEmitter>(simShooter) : default;
                    manualInfo += $" | SNAP gen={genSnap} self={EqsContext.Self(snap, ear, cfgSnap)} ears={Fdp.Toolkit.Perception.Sensors.AcousticPerception.Ears(snap, ear, out var r2, out _)}/{r2}"
                                + $" emitter={snap.HasComponent<Fdp.Toolkit.Perception.Signatures.AcousticEmitter>(simShooter)} shotLeft={em.ShotTimeLeft:F2} info={snap.HasComponent<EntityInfo>(simShooter)} tr={snap.HasComponent<SimTransform>(simShooter)}";
                }
                else manualInfo = "no template";
            }
            return Heard();
        }
        Assert.True(harness.PumpUntil(HeardOrTrack, timeoutFrames: 3000), $"the rifleman must hear the shots; {State()}");
        _out.WriteLine("heard: " + State());

        // ③ the SOP reacts with TakeCover, its sensor pointed at a POINT (no entity).
        Assert.True(harness.PumpUntil(() => Task() == "TakeCover", timeoutFrames: 3000), $"the SOP must react to the heard shots with TakeCover; {State()}");
        Entity sensor = Entity.Null;
        Assert.True(harness.PumpUntil(() => !(sensor = EqsChildSensor.Find(cgf, rifleman, Hrot.AI.Behaviors.Brains.EqsTacticsNodes.TakeCoverSite)).IsNull,
            timeoutFrames: 600), $"TakeCover must create its sensor; {State()}");
        var cfg = cgf.GetComponentRO<EqsSensor>(sensor);
        _out.WriteLine($"sensor slot1={cfg.ContextSlot1} mask={cfg.ContextPointMask} point={cfg.ContextPoint1}");
        Assert.True(cfg.ContextSlot1.IsNull, $"a heard contact is not an entity; {State()}");
        Assert.Equal(EqsSensor.Point1Bit, cfg.ContextPointMask);
        Assert.True(Vector2.Distance(new Vector2(cfg.ContextPoint1.X, cfg.ContextPoint1.Y), new Vector2(Pos(simShooter).X, Pos(simShooter).Y)) < 40f,
            $"the point is where the shots came from (shooter at {Pos(simShooter)}); {State()}");

        // ④ it moves to cover and stays hidden from the shooter.
        bool moved = harness.PumpUntil(() => Vector3.Distance(start, Pos(simRifleman)) > 1f, timeoutFrames: 3000);
        harness.PumpFrames(120);
        _out.WriteLine("end: " + State());
        Assert.True(moved, $"the rifleman must move to cover; {State()}");
        Assert.True(HiddenFrom(simShooter, simRifleman), $"it must end hidden from the shooter; {State()}");
    }
}
