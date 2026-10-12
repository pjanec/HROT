using System;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Components;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests;

/// <summary>
/// ⭐⭐ CE-3167 / CE-3168 (docs/DESIGN_Entity_Control.md §7 S1, R-270 / R-271 (backend)) — orders over the debug API, railed in
/// the NETWORKLESS editor world first (§2.3): the station, the brain (CgfLogicPack) and the muscle share one world, so an order
/// published on the bus is resolved by the same brain code a cluster would reach through the intent translators.
/// </summary>
[Collection("EditorOfflineTests")]
public sealed class DebugApiOrdersTests
{
    private const long UnitId = 1000;

    private static Entity Unit(EditorHarness h, long id, ForceId side, Vector3 at)
    {
        var e = h.Repo.CreateEntity();
        h.Repo.AddComponent(e, new BehaviorState());
        h.Repo.SetAuthority<BehaviorState>(e, true);
        h.Repo.AddComponent(e, new EntityInfo { ForceId = side });
        h.Repo.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
        h.EntityMap.Register(id, e);
        // ⭐ CE-2143 — a SOLDIER (DIS 3.1.225.1, class Person): an order resolves to its class's behaviour (PersonHold …).
        h.Repo.SetDisType(e, new DISEntityType { Value = (3UL << 56) | (1UL << 48) | (225UL << 32) | (1UL << 24) });
        return e;
    }

    private static int IdOf(EditorHarness h, string behaviour)
        => h.Behaviors.TryGetId(behaviour, out var id) ? id : throw new InvalidOperationException($"{behaviour} not registered");

    /// <summary>⭐ CE-2143 (D4-G, D4-C) — ANY order by name with its DTO in <c>params</c> (here Move with a Location), resolved by the
    /// unit to its class's behaviour; an order the unit's class does not fulfil is refused (409) naming why; an unknown order is a
    /// bad request listing the orders.</summary>
    [Fact]
    public void CE2143_AnyOrderByName_WithItsParams_ARefusalSaysWhy_AnUnknownOrderListsTheOrders()
    {
        using var h = new EditorHarness(productionBehaviors: true);
        var svc = h.BuildDebugApiService();
        var unit = Unit(h, UnitId, ForceId.Friend, Vector3.Zero);

        var (result, status, error, _) = svc.PostOrder(UnitId, new JsonObject
        {
            ["kind"] = "Move", ["params"] = new JsonObject { ["Location"] = new JsonArray(0.0, 30.0), ["Speed"] = 2f },
        });
        Assert.Null(error);
        Assert.Equal(200, status);
        Assert.Equal("Order.Move", result!["behavior"]!.GetValue<string>());
        h.PumpFrames(4);
        // the soldier's class ran ITS move — still running, or already ended (this world drives no navigation)
        Assert.Equal(BehaviorNames.PersonMove, svc.GetOrder(UnitId)["behavior"]!.GetValue<string>());

        var munition = Unit(h, 3000, ForceId.Friend, Vector3.Zero);
        h.Repo.SetDisType(munition, new DISEntityType { Value = 2UL << 56 });   // no class of a munition has an order behaviour
        (_, status, error, _) = svc.PostOrder(3000, new JsonObject { ["kind"] = "hold" });
        Assert.Equal(409, status);
        Assert.StartsWith("NotFulfilled", error);
        Assert.Contains("Munition", error);

        (_, status, error, _) = svc.PostOrder(UnitId, new JsonObject { ["kind"] = "dance" });
        Assert.Equal(400, status);
        Assert.Contains("Move", error);
    }

    [Fact]
    public void CE3167_AHoldOrder_RunsIdleAtOperatorOrigin_AndReadsBackRunning()
    {
        using var h = new EditorHarness(productionBehaviors: true);
        var svc = h.BuildDebugApiService();
        var unit = Unit(h, UnitId, ForceId.Friend, Vector3.Zero);

        var (result, status, error, _) = svc.PostOrder(UnitId, new JsonObject { ["kind"] = "hold" });
        Assert.Null(error);
        Assert.Equal(200, status);
        Assert.True(result!["accepted"]!.GetValue<bool>());

        h.PumpFrames(4);
        var bs = h.Repo.GetComponentRO<BehaviorState>(unit);
        Assert.Equal(IdOf(h, BehaviorNames.PersonHold), bs.ActiveBehaviorHash);   // ⭐ CE-2143 — the soldier's HOLD
        Assert.Equal(BehaviorOrigin.Operator, bs.Origin);

        var order = svc.GetOrder(UnitId);
        Assert.Equal("running", order["state"]!.GetValue<string>());
        Assert.Equal(BehaviorNames.PersonHold, order["behavior"]!.GetValue<string>());
    }

    [Fact]
    public void CE3167_AnOrder_ClearsTheUnitsMissionPlan()
    {
        using var h = new EditorHarness(productionBehaviors: true);
        var svc = h.BuildDebugApiService();
        var unit = Unit(h, UnitId, ForceId.Friend, Vector3.Zero);
        h.Repo.AddComponent(unit, new MissionPlanQueue { PhaseCount = 2 });

        svc.PostOrder(UnitId, new JsonObject { ["kind"] = "move", ["x"] = 30f, ["y"] = 0f });
        h.PumpFrames(4);

        Assert.Equal(0, h.Repo.GetComponentRO<MissionPlanQueue>(unit).PhaseCount);
        // The move ran as the operator's order — still running, or already ended (this world drives no navigation), and then
        // its outcome is the recorded one (OperatorOrderOutcome).
        var order = svc.GetOrder(UnitId);
        Assert.Equal(BehaviorNames.PersonMove, order["behavior"]!.GetValue<string>());   // ⭐ CE-2143 — the soldier's MOVE
        Assert.Contains(order["state"]!.GetValue<string>(), new[] { "running", "succeeded", "failed" });
    }

    [Fact]
    public void CE3167_ReleasingAUnit_EndsTheOperatorOrder()
    {
        using var h = new EditorHarness(productionBehaviors: true);
        var svc = h.BuildDebugApiService();
        var unit = Unit(h, UnitId, ForceId.Friend, Vector3.Zero);
        svc.PostOrder(UnitId, new JsonObject { ["kind"] = "hold" });
        h.PumpFrames(4);
        Assert.Equal(BehaviorOrigin.Operator, h.Repo.GetComponentRO<BehaviorState>(unit).Origin);

        var (_, status, error, _) = svc.DeleteOrder(UnitId);
        Assert.Null(error);
        Assert.Equal(200, status);
        h.PumpFrames(4);

        Assert.Equal(BehaviorIds.None, h.Repo.GetComponentRO<BehaviorState>(unit).ActiveBehaviorHash);
        Assert.NotEqual("running", svc.GetOrder(UnitId)["state"]!.GetValue<string>());
    }

    [Fact]
    public void CE3167_TheRefusals_UnknownUnit_KilledUnit_OtherSide_UnknownBehaviour()
    {
        using var h = new EditorHarness(productionBehaviors: true);
        var svc = h.BuildDebugApiService();
        var dead = Unit(h, UnitId, ForceId.Friend, Vector3.Zero);
        h.Repo.AddComponent(dead, new Health { Current = 0, Max = 100 });
        Unit(h, 1001, ForceId.Hostile, Vector3.Zero);
        Unit(h, 1002, ForceId.Friend, Vector3.Zero);

        Assert.Equal(404, svc.PostOrder(4242, new JsonObject { ["kind"] = "hold" }).status);
        Assert.Equal(409, svc.PostOrder(UnitId, new JsonObject { ["kind"] = "hold" }).status);
        Assert.Equal(400, svc.PostOrder(1002, new JsonObject { ["kind"] = "behavior", ["behavior"] = "NoSuchBehaviour" }).status);
        Assert.Equal(400, svc.PostOrder(1002, new JsonObject { ["kind"] = "move" }).status);   // no target

        Assert.Null(svc.SetSideLock(new JsonObject { ["side"] = "Friend" }).error);
        Assert.Equal(409, svc.PostOrder(1001, new JsonObject { ["kind"] = "hold" }).status);
        Assert.Equal(200, svc.PostOrder(1002, new JsonObject { ["kind"] = "hold" }).status);
    }

    [Fact]
    public void CE3167_TheBatch_AnswersEachOrderOnItsOwn()
    {
        using var h = new EditorHarness(productionBehaviors: true);
        var svc = h.BuildDebugApiService();
        Unit(h, UnitId, ForceId.Friend, Vector3.Zero);

        var (result, error) = svc.PostOrders(new JsonObject
        {
            ["orders"] = new JsonArray(
                new JsonObject { ["networkId"] = UnitId, ["kind"] = "hold" },
                new JsonObject { ["networkId"] = 4242, ["kind"] = "hold" }),
        });
        Assert.Null(error);
        var rows = result!["results"]!.AsArray();
        Assert.True(rows[0]!["accepted"]!.GetValue<bool>());
        Assert.False(rows[1]!["accepted"]!.GetValue<bool>());
    }

    /// <summary>⭐ CE-3170 (C-5) — an attack order runs CombatPosture at origin Operator with the target designated, and reads back
    /// SUCCEEDED once the target is down; a missing / unknown / killed target is refused.</summary>
    [Fact]
    public void CE3170_AnAttackOrder_DesignatesTheTarget_AndSucceedsWhenItIsDown()
    {
        using var h = new EditorHarness(productionBehaviors: true);
        var svc = h.BuildDebugApiService();
        var unit = Unit(h, UnitId, ForceId.Friend, Vector3.Zero);
        h.Repo.AddComponent(unit, new TargetMemory());
        var enemy = Unit(h, 2000, ForceId.Hostile, new Vector3(60, 0, 0));
        h.Repo.AddComponent(enemy, new Health { Current = 100, Max = 100 });

        var (result, status, error, _) = svc.PostOrder(UnitId, new JsonObject { ["kind"] = "attack", ["targetId"] = 2000L });
        Assert.Null(error);
        Assert.Equal(200, status);
        Assert.Equal("Order.Attack", result!["behavior"]!.GetValue<string>());   // ⭐ CE-2143 — what was SENT is the order

        h.PumpFrames(4);
        var bs = h.Repo.GetComponentRO<BehaviorState>(unit);
        Assert.Equal(IdOf(h, BehaviorNames.PersonAttack), bs.ActiveBehaviorHash);   // ⭐ CE-2143 — what RUNS is the class's ATTACK
        Assert.Equal(BehaviorOrigin.Operator, bs.Origin);
        Assert.True(DesignatedTarget.Of(h.Repo, unit, out var designated));
        Assert.Equal(enemy, designated);
        Assert.Equal("running", svc.GetOrder(UnitId)["state"]!.GetValue<string>());

        h.Repo.GetComponentRW<Health>(enemy).Current = 0;
        h.PumpFrames(4);
        Assert.Equal("succeeded", svc.GetOrder(UnitId)["state"]!.GetValue<string>());

        Assert.Equal(400, svc.PostOrder(UnitId, new JsonObject { ["kind"] = "attack" }).status);                          // no target
        Assert.Equal(400, svc.PostOrder(UnitId, new JsonObject { ["kind"] = "attack", ["targetId"] = 4242L }).status);     // unknown
        Assert.Equal(400, svc.PostOrder(UnitId, new JsonObject { ["kind"] = "attack", ["targetId"] = 2000L }).status);     // killed
    }

    /// <summary>⭐ C-9 — the sitrep shows what the side KNOWS: a hostile in a friendly unit's target memory is a contact, a
    /// hostile nobody remembers is not; truth=true lists both.</summary>
    [Fact]
    public void CE3167_TheSitrep_ShowsOnlyWhatTheSideKnows_UnlessTruthIsAsked()
    {
        using var h = new EditorHarness(productionBehaviors: true);
        var svc = h.BuildDebugApiService();
        var friend = Unit(h, UnitId, ForceId.Friend, Vector3.Zero);
        var known = Unit(h, 2000, ForceId.Hostile, new Vector3(50, 0, 0));
        Unit(h, 2001, ForceId.Hostile, new Vector3(-50, 0, 0));

        var memory = new TargetMemory();
        TargetMemory.AddOrUpdateTarget(ref memory, (long)known.PackedValue, 10f, 0f, 0.5f, tick: 1);
        h.Repo.AddComponent(friend, memory);

        var (fog, error) = svc.GetSitrep("Friend", truth: false);
        Assert.Null(error);
        Assert.Single(fog!["units"]!.AsArray());
        var contacts = fog["contacts"]!.AsArray().Select(c => c!["networkId"]?.GetValue<long>()).ToArray();
        Assert.Contains(2000L, contacts);
        Assert.DoesNotContain(2001L, contacts);

        var (truth, _) = svc.GetSitrep("Friend", truth: true);
        var all = truth!["contacts"]!.AsArray().Select(c => c!["networkId"]!.GetValue<long>()).ToArray();
        Assert.Contains(2000L, all);
        Assert.Contains(2001L, all);
    }

    /// <summary>⭐ CE-3168 — /entities/command binds the payload's FIELDS (they used to be dropped, publishing an all-default
    /// event), takes an entity as its network id, and refuses a key that names no field.</summary>
    [Fact]
    public void CE3168_TheCommandPayload_BindsFields_EntityByNetworkId_AndRefusesAnUnknownKey()
    {
        using var h = new EditorHarness();
        var svc = h.BuildDebugApiService();
        var unit = Unit(h, UnitId, ForceId.Friend, Vector3.Zero);
        _ = EventType<Hrot.Common.Events.CenterOnEntityCommand>.Id;
        _ = EventType<ClearBehaviorEvent>.Id;

        h.Repo.Bus.SwapBuffers();   // drop anything earlier
        Assert.Null(svc.SendCommand("CenterOnEntityCommand", new JsonObject { ["networkId"] = 42L }, wait: false).error);
        Assert.Null(svc.SendCommand("ClearBehaviorEvent",
            new JsonObject { ["entity"] = UnitId, ["origin"] = "Operator" }, wait: false).error);
        h.Repo.Bus.SwapBuffers();

        Assert.Equal(42L, h.Repo.Bus.Read<Hrot.Common.Events.CenterOnEntityCommand>().ToArray().Single().NetworkId);
        var clear = h.Repo.Bus.Read<ClearBehaviorEvent>().ToArray().Single();
        Assert.Equal(unit, clear.Entity);
        Assert.Equal(BehaviorOrigin.Operator, clear.Origin);

        Assert.NotNull(svc.SendCommand("CenterOnEntityCommand", new JsonObject { ["targetId"] = 42L }, wait: false).error);
        Assert.NotNull(svc.SendCommand("ClearBehaviorEvent", new JsonObject { ["entity"] = 999999L }, wait: false).error);
    }

    // ── CE-3172 / CE-3173 — possession (docs/DESIGN_Entity_Control.md §8b) ─────────────────────────────

    /// <summary>A walking body the muscle can drive (pedestrian preset, nav components the bridge and kinematics read).</summary>
    private static Entity Walker(EditorHarness h, long id)
    {
        var e = Unit(h, id, ForceId.Friend, Vector3.Zero);
        var r = h.Repo;
        if (!r.IsComponentTypeRegistered<Fdp.Toolkit.Navigation.NavigationIntent>()) r.RegisterComponent<Fdp.Toolkit.Navigation.NavigationIntent>();
        if (!r.IsComponentTypeRegistered<CarKinem.Core.NavState>()) r.RegisterComponent<CarKinem.Core.NavState>();
        if (!r.IsComponentTypeRegistered<CarKinem.Core.VehicleState>()) r.RegisterComponent<CarKinem.Core.VehicleState>();
        if (!r.IsComponentTypeRegistered<CarKinem.Core.VehicleParams>()) r.RegisterComponent<CarKinem.Core.VehicleParams>();
        if (!r.IsComponentTypeRegistered<SimVelocity>()) r.RegisterComponent<SimVelocity>();
        r.AddComponent(e, new Fdp.Toolkit.Navigation.NavigationIntent());
        r.SetAuthority<Fdp.Toolkit.Navigation.NavigationIntent>(e, true);
        r.AddComponent(e, new CarKinem.Core.NavState());
        r.AddComponent(e, new CarKinem.Core.VehicleState());
        r.AddComponent(e, CarKinem.Core.VehiclePresets.GetPreset(CarKinem.Core.VehicleClass.Pedestrian));
        r.AddComponent(e, new SimVelocity());
        r.SetAuthority<SimTransform>(e, true);
        return e;
    }

    [Fact]
    public void CE3172_Possession_PausesTheOrder_DrivesTheBody_RefusesOrders_AndReleaseResumes()
    {
        using var h = new EditorHarness(productionBehaviors: true);
        var svc = h.BuildDebugApiService();
        var unit = Walker(h, UnitId);
        svc.PostOrder(UnitId, new JsonObject { ["kind"] = "hold" });
        h.PumpFrames(4);
        Assert.Equal(BehaviorOrigin.Operator, h.Repo.GetComponentRO<BehaviorState>(unit).Origin);

        // possess: OperatorControl runs at origin Possession, the hold is PAUSED (not dropped)
        var (_, status, error, _) = svc.PostPossess(UnitId);
        Assert.Null(error);
        Assert.Equal(200, status);
        h.PumpFrames(4);
        var bs = h.Repo.GetComponentRO<BehaviorState>(unit);
        Assert.Equal(IdOf(h, BehaviorNames.OperatorControl), bs.ActiveBehaviorHash);
        Assert.Equal(BehaviorOrigin.Possession, bs.Origin);
        var order = svc.GetOrder(UnitId);
        Assert.Equal("paused", order["state"]!.GetValue<string>());
        Assert.Equal(BehaviorNames.PersonHold, order["behavior"]!.GetValue<string>());

        // a possessed unit takes no order (C-3) — it says why
        var refused = svc.PostOrder(UnitId, new JsonObject { ["kind"] = "move", ["x"] = 10f, ["y"] = 0f });
        Assert.Equal(409, refused.status);
        Assert.Contains("Possessed", refused.error);

        // drive: forward, facing +X — the body walks east (the harness steps 5 ms a frame: 300 frames = 1.5 s of sim time)
        var start = h.Repo.GetComponentRO<SimTransform>(unit).Position;
        for (uint seq = 1; seq <= 300; seq++)
        {
            Assert.Null(svc.PostControl(UnitId, new JsonObject { ["seq"] = seq, ["forward"] = 1f, ["yaw"] = 0f }).error);
            h.PumpFrames(1);
        }
        Assert.Equal(Fdp.Toolkit.Navigation.NavigationMode.Manual, h.Repo.GetComponentRO<Fdp.Toolkit.Navigation.NavigationIntent>(unit).Mode);
        Assert.Equal(1f, h.Repo.GetComponentRO<Fdp.Toolkit.Navigation.ManualDrive>(unit).Forward);
        var moved = h.Repo.GetComponentRO<SimTransform>(unit).Position;
        Assert.True(moved.X - start.X > 0.5f, $"walked east: {start} -> {moved}");
        Assert.InRange(MathF.Abs(moved.Y - start.Y), 0f, 0.1f);

        // an older frame is ignored (newest seq wins)
        svc.PostControl(UnitId, new JsonObject { ["seq"] = 30, ["forward"] = -1f });
        h.PumpFrames(2);
        Assert.Equal(1f, h.Repo.GetComponentRO<Fdp.Toolkit.Navigation.ManualDrive>(unit).Forward);

        // release: the body stops, the motion request is let go, and the hold RESUMES at origin Operator
        (_, status, error, _) = svc.DeletePossess(UnitId);
        Assert.Null(error);
        h.PumpFrames(4);
        bs = h.Repo.GetComponentRO<BehaviorState>(unit);
        Assert.Equal(IdOf(h, BehaviorNames.PersonHold), bs.ActiveBehaviorHash);   // ⭐ CE-2143 — the soldier's HOLD
        Assert.Equal(BehaviorOrigin.Operator, bs.Origin);
        Assert.Equal(0f, h.Repo.GetComponentRO<Fdp.Toolkit.Navigation.ManualDrive>(unit).Forward);
        Assert.NotEqual(Fdp.Toolkit.Navigation.NavigationMode.Manual, h.Repo.GetComponentRO<Fdp.Toolkit.Navigation.NavigationIntent>(unit).Mode);
        Assert.False(h.Repo.HasComponent<Fdp.Toolkit.Behavior.Possession.PossessedBy>(unit));
        Assert.Equal("running", svc.GetOrder(UnitId)["state"]!.GetValue<string>());
    }

    [Fact]
    public void CE3172_AnotherStation_CannotDriveOrRelease_ButCanTakeOver()
    {
        using var h = new EditorHarness(productionBehaviors: true);
        var unit = Walker(h, UnitId);
        var a = new Hrot.Common.Orders.EntityOrders(h.Repo, h.EntityMap, geo: null) { Station = 7 };
        var b = new Hrot.Common.Orders.EntityOrders(h.Repo, h.EntityMap, geo: null) { Station = 9 };
        Assert.True(a.Possess(UnitId).Accepted);
        h.PumpFrames(4);
        Assert.Equal(7L, a.HolderOf(UnitId));

        var f = new Fdp.Toolkit.Behavior.Possession.OperatorControlFrameEvent { Seq = 1, Forward = 1f };
        Assert.Equal(Hrot.Common.Orders.OrderRefusal.Possessed, b.Control(UnitId, f).Refusal);
        Assert.Equal(Hrot.Common.Orders.OrderRefusal.Possessed, b.Unpossess(UnitId).Refusal);

        Assert.True(b.Possess(UnitId).Accepted);   // C-3: a later station takes the unit over
        h.PumpFrames(4);
        Assert.Equal(9L, b.HolderOf(UnitId));
        Assert.Equal(BehaviorOrigin.Possession, h.Repo.GetComponentRO<BehaviorState>(unit).Origin);
        Assert.Equal(Hrot.Common.Orders.OrderRefusal.Possessed, a.Control(UnitId, f).Refusal);
    }

    /// <summary>⭐ CE-3172 S4d (C-2, M-8) — a possessed rifleman on HoldFire fires at the operator's aim point while the trigger is
    /// held, holds when it is released, and stops for good when the possession ends (the weapon action belongs to the run).</summary>
    [Fact]
    public void CE3172_OperatorFire_ShootsAtTheAimPoint_OverridingHoldFire_AndStopsOnRelease()
    {
        using var h = new EditorHarness(productionBehaviors: true);
        var svc = h.BuildDebugApiService();
        var unit = Walker(h, UnitId);
        var r = h.Repo;
        if (!r.IsComponentTypeRegistered<WeaponState>()) r.RegisterComponent<WeaponState>();
        if (!r.IsComponentTypeRegistered<WeaponChannel>()) r.RegisterComponent<WeaponChannel>();
        if (!r.IsComponentTypeRegistered<Roe>()) r.RegisterComponent<Roe>();
        r.AddComponent(unit, new WeaponState { Ammo = 30, MuzzleVelocity = 800f });
        r.AddComponent(unit, new WeaponChannel());
        r.AddComponent(unit, new Roe { Fire = RoeFire.HoldFire });
        if (!r.IsComponentTypeRegistered<ActorCapabilityState>()) r.RegisterComponent<ActorCapabilityState>();
        r.AddComponent(unit, new ActorCapabilityState { Capabilities = ActorCapabilities.CanShoot });   // the dispatcher's gate

        Assert.Null(svc.PostPossess(UnitId).error);
        h.PumpFrames(4);
        var aim = new JsonArray(50f, 0f, 1f);
        uint seq = 1;
        for (int i = 0; i < 20; i++, seq++) { svc.PostControl(UnitId, new JsonObject { ["seq"] = seq, ["aim"] = aim.DeepClone() }); h.PumpFrames(1); }
        Assert.Equal(30, r.GetComponentRO<WeaponState>(unit).Ammo);   // aimed, trigger released: no round

        for (int i = 0; i < 200; i++, seq++) { svc.PostControl(UnitId, new JsonObject { ["seq"] = seq, ["aim"] = aim.DeepClone(), ["trigger"] = true }); h.PumpFrames(1); }
        int afterBurst = r.GetComponentRO<WeaponState>(unit).Ammo;
        Assert.True(afterBurst < 30, $"fired under HoldFire: ammo {afterBurst}");

        Assert.Null(svc.DeletePossess(UnitId).error);
        h.PumpFrames(200);   // no more frames: the possession ended, its weapon action with it
        Assert.Equal(afterBurst, r.GetComponentRO<WeaponState>(unit).Ammo);
    }
}
