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
        return e;
    }

    private static int IdOf(EditorHarness h, string behaviour)
        => h.Behaviors.TryGetId(behaviour, out var id) ? id : throw new InvalidOperationException($"{behaviour} not registered");

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
        Assert.Equal(IdOf(h, BehaviorNames.Idle), bs.ActiveBehaviorHash);
        Assert.Equal(BehaviorOrigin.Operator, bs.Origin);

        var order = svc.GetOrder(UnitId);
        Assert.Equal("running", order["state"]!.GetValue<string>());
        Assert.Equal(BehaviorNames.Idle, order["behavior"]!.GetValue<string>());
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
        Assert.Equal(BehaviorNames.MoveToLocation, order["behavior"]!.GetValue<string>());
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
}
