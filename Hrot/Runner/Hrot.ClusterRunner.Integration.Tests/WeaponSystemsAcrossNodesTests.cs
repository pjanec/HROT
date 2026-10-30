using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Hrot.Common.Orders;
using Hrot.NED.Descriptors.Orchestration;
using Xunit;
using Xunit.Abstractions;
using ClusterOpType = Hrot.NED.Descriptors.Orchestration.ClusterOpType;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐ Weapon systems WS-3 / WS-4 (W-D, W-I; docs/designs/weapons/DESIGN_Weapon_Systems.md §4a, §4d) — the technical on a SPLIT cluster
/// (CGF = the brain, SimHost = the muscle): the scenario names ONE entity, a technical (TKB 104); its seat presets (an M2, a rifleman)
/// are filled where it is created; an Attack order reaches the pickup's brain on CGF, which fires its STATION weapon — the M2's turret
/// task crosses to the muscle node, and the round leaves the M2 there.
/// </summary>
[Collection("HeavyE2ETests")]
public sealed class WeaponSystemsAcrossNodesTests : IDisposable
{
    private const int DomainBase = 91;    // 52–58 posture/shots/weapons/grenade, 60–69 sensors, 71–79 orders, 81 save (grep of DomainBase, 2026-10-14)
    private static int _domainSeq = DomainBase - 1;
    private static int NextDomainId() => Interlocked.Increment(ref _domainSeq);

    private readonly string _scenarioId = "ws_" + Guid.NewGuid().ToString("N");
    private readonly ITestOutputHelper _out;

    public WeaponSystemsAcrossNodesTests(ITestOutputHelper output) => _out = output;
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

    /// <summary>A technical (hostile, TKB 104) and a rifleman (friend) 150 m to its north on the open east side of the range (x = 230: clear of every wall and building) — nothing else. The technical's
    /// memory is seeded with the rifleman (the ua-grenade-or-rifle shape), so the rail does not wait on perception warming up.</summary>
    private const string Scenario = """
        {
          "$meta": { "docType": "Hrot.Scenario", "schemaVersion": 1 },
          "header": { "subsystemType": "Hrot.Scenario", "schemaVersion": "1.0", "terrainName": "bt-range" },
          "entities": {
            "b5000000-0000-4000-8000-000000000001": {
              "SimTransform": { "Position": [ 230, 20, 0 ], "Rotation": [ 0, 0, 0, 1 ] },
              "NetworkIdentity": { "Value": 9101 },
              "TkbIdentity": { "TkbType": 104 },
              "EntityInfo": { "Name": "Technical", "ForceId": "Hostile" },
              "TargetMemory": { "Entries": [ { "Entity": "b5000000-0000-4000-8000-000000000002", "PosX": 230, "PosY": 170, "PosZ": 0, "Score": 500, "Tick": 0, "Modality": 1 } ] }
            },
            "b5000000-0000-4000-8000-000000000002": {
              "SimTransform": { "Position": [ 230, 170, 0 ], "Rotation": [ 0, 0, 0, 1 ] },
              "Health": { "Current": 100, "Max": 100 },
              "NetworkIdentity": { "Value": 9102 },
              "TkbIdentity": { "TkbType": 2002 },
              "EntityInfo": { "Name": "Target", "ForceId": "Friend" }
            }
          }
        }
        """;

    [Fact(Timeout = 600_000)]
    public async Task WS_ATechnicalsStationHmg_FiresOnASplitCluster()
    {
        Directory.CreateDirectory(NasScenarioStaging.DirectoryOf(_scenarioId));
        File.WriteAllText(Path.Combine(NasScenarioStaging.DirectoryOf(_scenarioId), "scenario.json"), Scenario);

        using var harness = new HrotRunnerHarness("simhost,ig,cgf", NextDomainId());
        var master = harness.OrchestratorSvc.TestHook_ClusterMaster!;
        var rosterDeadline = DateTime.UtcNow.AddSeconds(10.0);
        while (master.NodeRoster.ActiveNodes.Count == 0 && DateTime.UtcNow < rosterDeadline) { harness.PumpFrames(1); Thread.Sleep(10); }
        await master.HandleClusterOpRequestAsync(new ClusterOpRequest
        {
            RequestId     = Guid.NewGuid(),
            OperationType = ClusterOpType.TransitionState,
            PayloadJson   = JsonSerializer.Serialize(new { TargetState = nameof(ClusterState.OperatingLive), ScenarioId = _scenarioId }),
        }).ConfigureAwait(false);
        Assert.True(harness.PumpUntil(() => (int)master.CurrentClusterState == 31, timeoutFrames: 4000), "cluster must reach OperatingLive");

        var cgf = harness.Cgf!.World!;
        var sim = harness.SimHost.World!;
        Assert.True(harness.PumpUntil(() => !ByName(cgf, "Technical").IsNull && !ByName(sim, "Technical").IsNull, timeoutFrames: 2000),
            "the technical must exist on both nodes");
        var pickup = ByName(cgf, "Technical");

        // ⭐ W-I — the presets: an M2 in the station (seat 2), a rifleman in the gunner seat (seat 1), on the brain node
        Assert.True(harness.PumpUntil(() =>
        {
            if (!cgf.HasComponent<PassengerBuffer>(pickup)) return false;
            var l = cgf.GetComponentRO<PassengerBuffer>(pickup);
            return !l.Passengers[1].IsNull && !l.Passengers[2].IsNull;
        }, timeoutFrames: 2000), "the technical's gunner and M2 must be boarded on CGF");
        var list = cgf.GetComponentRO<PassengerBuffer>(pickup);
        var m2 = list.Passengers[2];
        Assert.True(WeaponSystems.IsWeaponSystem(cgf, m2), "seat 2 holds a weapon system");
        Assert.Equal(pickup, WeaponSystems.ControllerOf(cgf, m2));
        Assert.False(cgf.HasComponent<WeaponState>(pickup), "the pickup has no gun of its own");
        Assert.True(cgf.HasComponent<SeatPresetsFilled>(pickup), "the pickup is marked filled");
        int belt0 = cgf.GetComponentRO<WeaponState>(m2).Ammo;

        var simMap = harness.SimHost.App.TestHook_EntityMap;
        Assert.True(simMap.TryGetNetworkId(ByName(sim, "Technical"), out long pickupId));
        Assert.True(harness.PumpUntil(() => !ByName(sim, "Target").IsNull, timeoutFrames: 600));
        Assert.True(simMap.TryGetNetworkId(ByName(sim, "Target"), out long targetId));
        var cgfMap = harness.Cgf!.GhostEntityMap!;
        Assert.True(cgfMap.TryGetNetworkId(m2, out long m2Id), "the M2 has a network id");

        // ⭐ the pickup's brain on CGF is given FireAtTarget from the MUSCLE node (the order API, R-278's one path) — the library behaviour
        //   that fires its primary at a named target. ⛔ Not the Attack order: for a land platform that runs VehicleFightFromCover
        //   (behaviours lane), which fires only from a firing point its cover search finds, and an open range offers none.
        var result = new EntityOrders(sim, simMap, geo: null).Behavior(pickupId, "FireAtTarget",
            $"{{\"targetNetworkId\":{targetId},\"maxRounds\":20,\"cooldownSeconds\":0.2}}");
        Assert.True(result.Accepted, result.Detail);

        bool fired = harness.PumpUntil(() =>
        {
            var log = ShotLog.Peek(sim);
            return log != null && log.Recent(ShotLog.Capacity).Any(s => simMap.TryGetNetworkId(s.Shooter, out long id) && id == m2Id);
        }, timeoutFrames: 3000);
        var shots = ShotLog.Peek(sim)?.Recent(ShotLog.Capacity) ?? Array.Empty<ShotRecord>();
        _out.WriteLine($"SimHost shots: {shots.Count}; by the M2: {shots.Count(s => simMap.TryGetNetworkId(s.Shooter, out long id) && id == m2Id)}; " +
                       $"M2 belt on CGF {cgf.GetComponentRO<WeaponState>(m2).Ammo}/{belt0}");
        Diagnose(cgf, sim, pickup, m2, ByName(cgf, "Target"), simMap, m2Id);
        Assert.True(fired, "the round must leave the M2 on the muscle node (SimHost), fired by the pickup's brain on CGF");
        Assert.True(harness.PumpUntil(() => cgf.GetComponentRO<WeaponState>(m2).Ammo < belt0, timeoutFrames: 600),
            "the brain spends the M2's own belt");
    }

    /// <summary>Each link of the chain: the brain's weapon row and choice, the M2's fire gate, its turret's task on both nodes.</summary>
    private void Diagnose(EntityRepository cgf, EntityRepository sim, Entity pickup, Entity m2, Entity target, Fdp.Toolkit.Replication.Services.NetworkEntityMap simMap, long m2Id)
    {
        if (cgf.HasComponent<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(pickup))
        {
            var row = cgf.GetComponentRO<Fdp.Toolkit.Behavior.Diagnostics.ActionStatus>(pickup).Weapon;
            _out.WriteLine($"CGF pickup weapon row: action {row.ActionId} reason {row.Reason} target {row.Target}");
        }
        else _out.WriteLine("CGF pickup: no ActionStatus");
        _out.WriteLine($"CGF pickup memory {(cgf.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(pickup) ? cgf.GetComponentRO<Fdp.Toolkit.Perception.Components.TargetMemory>(pickup).Count : -1)}; " +
                       $"behaviour {(cgf.HasComponent<Fdp.Toolkit.Behavior.Components.BehaviorState>(pickup) ? cgf.GetComponentRO<Fdp.Toolkit.Behavior.Components.BehaviorState>(pickup).ActiveBehaviorHash : -1)}; " +
                       $"armed by {WeaponSystems.WeaponOf(cgf, pickup)}; gunner seat {cgf.GetComponentRO<PassengerBuffer>(pickup).Passengers[1]} " +
                       $"alive {Fdp.Toolkit.Combat.Injury.IsInTheFight(cgf, cgf.GetComponentRO<PassengerBuffer>(pickup).Passengers[1])}");
        var gunnerCgf = cgf.GetComponentRO<PassengerBuffer>(pickup).Passengers[1];
        _out.WriteLine($"CGF sensors on the pickup {Sensors(cgf, pickup)}, on the gunner {Sensors(cgf, gunnerCgf)}; " +
                       $"gunner memory {(cgf.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(gunnerCgf) ? cgf.GetComponentRO<Fdp.Toolkit.Perception.Components.TargetMemory>(gunnerCgf).Count : -1)}");
        if (simMap.TryGetEntity(cgf.HasComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>(pickup) ? cgf.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(pickup).Value : 0, out var pickupSim))
            _out.WriteLine($"SimHost sensors on the pickup {Sensors(sim, pickupSim)}; memory {(sim.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(pickupSim) ? sim.GetComponentRO<Fdp.Toolkit.Perception.Components.TargetMemory>(pickupSim).Count : -1)}");
        _out.WriteLine($"CGF WeaponChoice at the target: {WeaponChoice.Choose(cgf, pickup, target, out _)}; " +
                       $"CanFire(M2) {WeaponSystems.CanFire(cgf, m2, out var hold)} ({hold}); gunner {WeaponSystems.GunnerOf(cgf, m2)}");
        Turret("CGF", cgf, m2);
        if (simMap.TryGetEntity(m2Id, out var m2Sim) && sim.IsAlive(m2Sim))
        {
            Turret("SimHost", sim, m2Sim);
            _out.WriteLine($"SimHost M2 belt {(sim.HasComponent<WeaponState>(m2Sim) ? sim.GetComponentRO<WeaponState>(m2Sim).Ammo : -1)}");
        }
        else _out.WriteLine("SimHost: the M2 is not there");

        static int Sensors(EntityRepository w, Entity owner)
        {
            int n = 0;
            if (owner.IsNull || !w.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Components.SensorTag>()) return -1;
            foreach (var e in w.Query().With<Fdp.Toolkit.Perception.Components.SensorTag>().Build())
                if (w.HasComponent<Fdp.Toolkit.Replication.Components.PartMetadata>(e) && w.GetComponentRO<Fdp.Toolkit.Replication.Components.PartMetadata>(e).ParentEntity == owner) n++;
            return n;
        }

        void Turret(string node, EntityRepository w, Entity weapon)
        {
            var t = TurretParts.Find(w, weapon, 0);
            if (t.IsNull) { _out.WriteLine($"{node} M2 turret: none"); return; }
            string task = w.HasComponent<WeaponTask>(t) ? $"epoch {w.GetComponentRO<WeaponTask>(t).Epoch} mode {w.GetComponentRO<WeaponTask>(t).Mode} mount {w.GetComponentRO<WeaponTask>(t).MountIndex}" : "none";
            string status = w.HasComponent<WeaponTaskStatus>(t) ? $"epoch {w.GetComponentRO<WeaponTaskStatus>(t).Epoch} {w.GetComponentRO<WeaponTaskStatus>(t).State} {w.GetComponentRO<WeaponTaskStatus>(t).Reason} fired {w.GetComponentRO<WeaponTaskStatus>(t).RoundsFired}" : "none";
            _out.WriteLine($"{node} M2 turret {t}: task {task}; status {status}");
        }
    }
}
