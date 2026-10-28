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
    /// <summary>⭐ CE-2148 (P5-f, F5) — <c>GET /orders</c> lists what each order needs (Move: a required point), narrowed to what a unit
    /// fulfils; an order whose required field is missing is a 400 naming it.</summary>
    [Fact]
    public void CE2148_GetOrders_SaysWhatEachOrderNeeds_AndAMissingRequiredFieldIsA400()
    {
        using var h = new EditorHarness(productionBehaviors: true);
        var svc = h.BuildDebugApiService();
        Unit(h, UnitId, ForceId.Friend, Vector3.Zero);

        var (all, status, error, _) = svc.GetOrders(null);
        Assert.Null(error);
        var move = all!["orders"]!.AsArray().Single(o => o!["name"]!.GetValue<string>() == "Move")!;
        Assert.Equal("Point", move["pick"]!.GetValue<string>());
        var location = move["fields"]!.AsArray().Single(f => f!["name"]!.GetValue<string>() == "Location")!;
        Assert.True(location["required"]!.GetValue<bool>());
        Assert.Equal("[\"Location\"]", move["paramSchema"]!["required"]!.ToJsonString());

        var (mine, _, _, _) = svc.GetOrders(UnitId);
        Assert.Contains(mine!["orders"]!.AsArray(), o => o!["name"]!.GetValue<string>() == "Hold");
        (_, status, _, _) = svc.GetOrders(424242);
        Assert.Equal(404, status);

        (_, status, error, _) = svc.PostOrder(UnitId, new JsonObject { ["kind"] = "Move", ["params"] = new JsonObject { ["Speed"] = 2f } });
        Assert.Equal(400, status);
        Assert.Contains("Location", error);
    }

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

        // ⭐ CE-2176 — a point is [lat, lon]: a local [y, x] is refused, named, here — 📐 live it was accepted and then thrown on by the
        //   brain's WGS84Transform, and the order vanished without a word
        (_, status, error, _) = svc.PostOrder(UnitId, new JsonObject
        {
            ["kind"] = "Move", ["params"] = new JsonObject { ["Location"] = new JsonArray(-195.0, 720.0) },
        });
        Assert.Equal(400, status);
        Assert.Contains("Location", error);
        Assert.Contains("out of range", error);
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
    public void CE3172_AnotherStation_CannotDriveOrRelease_AndR4096_CannotTakeOver()
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

        // ⭐ R-4096 (C-3′) — SUPERSEDES C-3's "a later station takes the unit over": the second station is refused, naming the holder
        //   (the HTTP route answers 409), and nothing is published, so the first keeps the unit and the operator behaviour
        var refused = b.Possess(UnitId);
        Assert.Equal(Hrot.Common.Orders.OrderRefusal.Possessed, refused.Refusal);
        Assert.Contains("station 7", refused.Detail);
        h.PumpFrames(4);
        Assert.Equal(7L, b.HolderOf(UnitId));
        Assert.Equal(BehaviorOrigin.Possession, h.Repo.GetComponentRO<BehaviorState>(unit).Origin);
        Assert.True(a.Control(UnitId, f).Accepted);   // the holder still drives

        // and the brain enforces it too, for a request that arrives anyway (two stations racing): ignored, the holder keeps the unit
        h.Repo.Bus.Publish(new Fdp.Toolkit.Behavior.Possession.PossessRequestEvent
        {
            Unit = unit, StationNodeId = 9, Mode = Fdp.Toolkit.Behavior.Possession.PossessionMode.Fps,
        });
        h.PumpFrames(4);
        Assert.Equal(7L, a.HolderOf(UnitId));
        Assert.Equal(1u, h.Repo.GetComponentRO<Fdp.Toolkit.Behavior.Possession.PossessedBy>(unit).Epoch);   // never re-taken

        // once the holder lets go, the other station may take it (a free unit is free)
        Assert.True(a.Unpossess(UnitId).Accepted);
        h.PumpFrames(4);
        Assert.True(b.Possess(UnitId).Accepted);
        h.PumpFrames(4);
        Assert.Equal(9L, b.HolderOf(UnitId));
    }

    /// <summary>
    /// ⭐⭐ CE-1074 (docs/DESIGN_Entity_Control.md §13.5) — the operator's stance rides the control frame and the possession behaviour
    /// asks for it through <c>StanceRequest</c> (the brain's one way), so the station's C key crouches, lays down and stands up the held
    /// man; a frame with no stance keeps his stance. 🔒 User, 2026-10-14: "I will need to be able to switch the possessed character to
    /// crouched as well as lying pose (cycling using a key press)".
    /// </summary>
    [Fact]
    public void CE1074_TheControlFrame_CarriesTheStance_AndThePossessionAsksForIt()
    {
        using var h = new EditorHarness(productionBehaviors: true);
        var svc = h.BuildDebugApiService();
        var unit = Walker(h, UnitId);
        var r = h.Repo;
        if (!r.IsComponentTypeRegistered<Hrot.MuscleCharacter.Animation.Components.StanceIntent>())
            r.RegisterComponent<Hrot.MuscleCharacter.Animation.Components.StanceIntent>();
        r.AddComponent(unit, new Hrot.MuscleCharacter.Animation.Components.StanceIntent());
        Assert.Null(svc.PostPossess(UnitId).error);
        h.PumpFrames(4);

        Fdp.Toolkit.Tkb.Domain.StanceId StanceAfter(uint seq, string? stance)
        {
            var frame = new JsonObject { ["seq"] = seq };
            if (stance != null) frame["stance"] = stance;
            Assert.Null(svc.PostControl(UnitId, frame).error);
            h.PumpFrames(3);
            return r.GetComponentRO<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(unit).TargetStance;
        }

        Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Crouched, StanceAfter(1, "crouched"));
        Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Crouched, StanceAfter(2, null));     // no stance in the frame: kept
        Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Prone, StanceAfter(3, "prone"));
        Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Standing, StanceAfter(4, "standing"));
        Assert.Equal(3u, r.GetComponentRO<Hrot.MuscleCharacter.Animation.Components.StanceIntent>(unit).Version);   // one bump per change
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

    /// <summary>A possessed rifleman for operator fire: a one-round magazine with a 0.5 s reload (a harness frame is 5 ms), HoldFire (the operator overrides it).</summary>
    private static Entity Gunner(EditorHarness h, int ammo = 30)
    {
        var unit = Walker(h, UnitId);
        var r = h.Repo;
        if (!r.IsComponentTypeRegistered<WeaponState>()) r.RegisterComponent<WeaponState>();
        if (!r.IsComponentTypeRegistered<WeaponChannel>()) r.RegisterComponent<WeaponChannel>();
        if (!r.IsComponentTypeRegistered<Roe>()) r.RegisterComponent<Roe>();
        r.AddComponent(unit, new WeaponState { Ammo = ammo, MaxAmmo = ammo, MagazineSize = 1, MagazineRounds = 1, ReloadSeconds = 0.5f, MuzzleVelocity = 800f });
        r.AddComponent(unit, new WeaponChannel());
        r.AddComponent(unit, new Roe { Fire = RoeFire.HoldFire });
        if (!r.IsComponentTypeRegistered<ActorCapabilityState>()) r.RegisterComponent<ActorCapabilityState>();
        r.AddComponent(unit, new ActorCapabilityState { Capabilities = ActorCapabilities.CanShoot });
        return unit;
    }

    /// <summary>
    /// ⭐⭐ CE-1078 (docs/DESIGN_Entity_Control.md §8e D-1) — through the real possession and operator behaviour: a trigger PRESS (the
    /// frame's shot counter moving) fires exactly ONE round with no trigger held, and a counter that stays put fires nothing more; a press
    /// made while the weapon reloads is NOT lost — it fires when the weapon is ready; RMB (the cancel counter) drops a waiting shot.
    /// 🔒 User, 2026-10-14: "when i press fire trigger the tank tries aiming to the clicked target and fire once the barrel is aimed".
    /// </summary>
    [Fact]
    public void CE1078_APress_FiresOneRound_APressDuringAReloadWaitsForIt_AndRmbDropsAWaitingShot()
    {
        using var h = new EditorHarness(productionBehaviors: true);
        var svc = h.BuildDebugApiService();
        var unit = Gunner(h);
        var r = h.Repo;
        Assert.Null(svc.PostPossess(UnitId).error);
        h.PumpFrames(4);
        uint seq = 1;
        void Send(int frames, int shot, int cancel = 0)
        {
            for (int i = 0; i < frames; i++, seq++)
            {
                svc.PostControl(UnitId, new JsonObject { ["seq"] = seq, ["aim"] = new JsonArray(50f, 0f, 1f), ["shot"] = shot, ["cancel"] = cancel });
                h.PumpFrames(1);
            }
        }

        Send(20, shot: 0);
        Assert.Equal(30, r.GetComponentRO<WeaponState>(unit).Ammo);    // no press, no round
        Send(60, shot: 1);
        Assert.Equal(29, r.GetComponentRO<WeaponState>(unit).Ammo);    // one press, one round — the counter staying at 1 fires no more
        Assert.True(r.GetComponentRO<WeaponState>(unit).ReloadSecondsRemaining > 0f, "the one-round magazine is reloading");
        for (int i = 0; i < 300 && r.GetComponentRO<WeaponState>(unit).Ammo > 28; i++) Send(1, shot: 2);   // pressed DURING the reload
        Assert.Equal(28, r.GetComponentRO<WeaponState>(unit).Ammo);    // it waited for the reload and fired
        Assert.True(r.GetComponentRO<WeaponState>(unit).ReloadSecondsRemaining > 0f, "reloading again after that round");
        Send(1, shot: 3);                                                // pressed during this reload …
        Send(300, shot: 3, cancel: 1);                                   // … and RMB before it is done
        Assert.Equal(28, r.GetComponentRO<WeaponState>(unit).Ammo);    // the waiting shot was dropped
    }

    /// <summary>
    /// ⭐ CE-1078 (D-1) — a SINGLE-SHOT weapon (here the unit's TKB names a grenade) ignores a held trigger: only a press throws, one per
    /// press; the automatic rifle of <see cref="CE3172_OperatorFire_ShootsAtTheAimPoint_OverridingHoldFire_AndStopsOnRelease"/> still
    /// streams while held.
    /// </summary>
    [Fact]
    public void CE1078_ASingleShotWeapon_IgnoresAHeldTrigger_OnlyAPressFires()
    {
        using var h = new EditorHarness(productionBehaviors: true);
        var svc = h.BuildDebugApiService();
        var unit = Gunner(h);
        var r = h.Repo;
        r.GetComponentRW<WeaponState>(unit).MagazineSize = 0;          // no magazine: only the press decides
        var db = new Fdp.Toolkit.Tkb.TkbDatabase();
        var thrower = new Fdp.Interfaces.TkbTemplate("Thrower", 9300);
        thrower.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts =
            { new Fdp.Toolkit.Tkb.Domain.WeaponMountDto { Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Grenade } } });
        db.Register(thrower);
        r.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
        if (!r.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.TkbIdentity>()) r.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
        if (r.HasComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(unit)) r.SetComponent(unit, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = 9300 });
        else r.AddComponent(unit, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = 9300 });

        Assert.Null(svc.PostPossess(UnitId).error);
        h.PumpFrames(4);
        uint seq = 1;
        for (int i = 0; i < 120; i++, seq++) { svc.PostControl(UnitId, new JsonObject { ["seq"] = seq, ["aim"] = new JsonArray(20f, 0f, 0f), ["trigger"] = true }); h.PumpFrames(1); }
        Assert.Equal(30, r.GetComponentRO<WeaponState>(unit).Ammo);    // held, never pressed: nothing thrown
        for (int i = 0; i < 120; i++, seq++) { svc.PostControl(UnitId, new JsonObject { ["seq"] = seq, ["aim"] = new JsonArray(20f, 0f, 0f), ["trigger"] = true, ["shot"] = 1 }); h.PumpFrames(1); }
        Assert.Equal(29, r.GetComponentRO<WeaponState>(unit).Ammo);    // one press, one throw, however long it is held
    }
}
