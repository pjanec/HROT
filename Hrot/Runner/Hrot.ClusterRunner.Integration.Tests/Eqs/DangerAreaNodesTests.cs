using System.Numerics;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Sensors;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Squad.DangerArea;
using Hrot.AI.Behaviors.Brains;
using Hrot.SimHost;
using Xunit;

namespace Hrot.ClusterRunner.Integration.Tests.Eqs;

/// <summary>
/// ⭐ <c>CE-3079</c> H4 — the danger-crossing nodes (<see cref="DangerAreaNodes"/>), called directly against a HAND-FILLED
/// <see cref="DangerAreaCognitiveBuffer"/> on the run's own sensor child — the real solver (B3) and producer (B4) fill the same
/// component later, so no node changes. 📄 <c>docs/DESIGN_Utility_AI_Demo_Scenarios.md</c> §10.4.
/// </summary>
[Collection("EqsIntegrationTests")]
public sealed class DangerAreaNodesTests : System.IDisposable
{
    private readonly EntityRepository _repo = new();
    private readonly Entity _unit;

    public DangerAreaNodesTests()
    {
        SimHostComponentRegistry.RegisterAll(_repo);
        CognitiveComponentRegistry.RegisterAll(_repo);
        if (!_repo.IsComponentTypeRegistered<DangerAreaSensor>()) _repo.RegisterComponent<DangerAreaSensor>();
        if (!_repo.IsComponentTypeRegistered<DangerAreaCognitiveBuffer>()) _repo.RegisterComponent<DangerAreaCognitiveBuffer>();
        _unit = _repo.CreateEntity();
        _repo.AddComponent(_unit, new LocomotionChannel());
    }

    public void Dispose() => _repo.Dispose();

    private static readonly Vector3 Objective = new(300f, 300f, 0f);

    /// <summary>Ensures the run's sensor through the node, as the tree's first step does.</summary>
    private Entity Sensor()
    {
        var p = new DangerSensorParams { RouteTo = Objective };
        var ws = default(DangerSensorState);
        Assert.Equal(NodeStatus.Success, DangerAreaNodes.EnsureSensor(ref p, ref ws, _unit, _repo));
        return ws.Sensor.ChildId;
    }

    /// <summary>Writes the processed answer by hand: <paramref name="areas"/> in route order; stamp <paramref name="tick"/>.</summary>
    private void Answer(Entity sensor, uint tick, params DangerAreaDescriptor[] areas)
    {
        var buf = new DangerAreaCognitiveBuffer { Count = areas.Length, LastUpdateTick = tick };
        for (int i = 0; i < areas.Length; i++) buf.GetSpanRW()[i] = areas[i];
        _repo.SetComponent(sensor, buf);
    }

    private static DangerAreaDescriptor Area(uint id, float threat, float along, DangerAreaKind kind = DangerAreaKind.StreetCrossing)
        => new()
        {
            FeatureId = id, ThreatRating = threat, Kind = kind, DistanceAlongRoute = along,
            NearSideHandle = new Vector3(100f + id, 100f, 0f), FarSideHandle = new Vector3(120f + id, 100f, 0f),
        };

    private unsafe Vector3 Destination()
    {
        ref readonly var ch = ref _repo.GetComponentRO<LocomotionChannel>(_unit);
        fixed (byte* src = ch.Params) return ((MoveToParams*)src)->Destination;
    }

    private uint MoveInstance => _repo.GetComponentRO<LocomotionChannel>(_unit).ActionInstanceId;

    [Fact]
    public void CE3079_EnsureSensor_MakesTheRunsDangerSensor_WatchingUnitToTheObjective_Once()
    {
        var child = Sensor();
        Assert.Equal(child, UnitSensors.Of(_repo, _unit, SensorModality.DangerArea));
        var settings = _repo.GetComponentRO<DangerAreaSensor>(child).Settings;
        Assert.Equal(DangerRouteSource.ToPoint, settings.RouteSource);   // ③ — never the unit's own move (§10.2)
        Assert.Equal(Objective, settings.RoutePoint);
        Assert.Equal(child, Sensor());                                     // find-or-create
    }

    [Fact]
    public void CE3079_DangerAhead_ChecksTheNextAreasThreat_Distance_AndKind()
    {
        var sensor = Sensor();
        var hold  = new DangerAheadParams { MinThreat = 0.5f, WithinMetres = 60f };
        Assert.False(DangerAreaNodes.DangerAhead(ref hold, _unit, _repo), "no answer yet");

        Answer(sensor, 1, Area(1, 0.8f, 40f));
        Assert.True(DangerAreaNodes.DangerAhead(ref hold, _unit, _repo));
        var hotter = new DangerAheadParams { MinThreat = 0.9f, WithinMetres = 60f };
        Assert.False(DangerAreaNodes.DangerAhead(ref hotter, _unit, _repo), "0.8 is below 0.9");
        var nearer = new DangerAheadParams { MinThreat = 0.5f, WithinMetres = 30f };
        Assert.False(DangerAreaNodes.DangerAhead(ref nearer, _unit, _repo), "40 m along the route is beyond 30 m");
        var crossings = new DangerAheadParams { WithinMetres = 60f, KindMask = 1u << (int)DangerAreaKind.StreetCrossing };
        Assert.True(DangerAreaNodes.DangerAhead(ref crossings, _unit, _repo));
        var intersections = new DangerAheadParams { WithinMetres = 60f, KindMask = 1u << (int)DangerAreaKind.Intersection };
        Assert.False(DangerAreaNodes.DangerAhead(ref intersections, _unit, _repo), "a crossing is not an intersection");

        Answer(sensor, 2);   // a clear route
        Assert.False(DangerAreaNodes.DangerAhead(ref hold, _unit, _repo));
    }

    /// <summary>🔴 HoldShort issues ONE move to the near side and then holds (no re-issue every tick); it ends only below
    /// <c>MinThreat − 0.1</c> (hysteresis), and its abort stops the move.</summary>
    [Fact]
    public void CE3079_HoldShort_OneMoveToTheNearSide_HoldsWithHysteresis_AndAbortStops()
    {
        var sensor = Sensor();
        Answer(sensor, 1, Area(1, 0.8f, 40f));
        var p = new HoldShortParams { MinThreat = 0.5f, Speed = 2f, ArrivalRadius = 1f };
        var ws = default(HoldShortState);

        Assert.Equal(NodeStatus.Running, DangerAreaNodes.HoldShort(ref p, ref ws, _unit, _repo));
        Assert.Equal(NavigationConstants.ActionIdMoveTo, _repo.GetComponentRO<LocomotionChannel>(_unit).ActiveAction);
        Assert.Equal(new Vector3(101f, 100f, 0f), Destination());
        uint move = MoveInstance;
        for (int i = 0; i < 5; i++) DangerAreaNodes.HoldShort(ref p, ref ws, _unit, _repo);
        Assert.Equal(move, MoveInstance);                               // no re-issue while it holds

        _repo.GetComponentRW<LocomotionChannel>(_unit).Status = NodeStatus.Success;   // arrived at the near side
        Answer(sensor, 2, Area(1, 0.45f, 2f));
        Assert.Equal(NodeStatus.Running, DangerAreaNodes.HoldShort(ref p, ref ws, _unit, _repo));   // 0.45 ≥ 0.4: still holds
        Assert.Equal(move, MoveInstance);
        Answer(sensor, 3, Area(1, 0.35f, 2f));
        Assert.Equal(NodeStatus.Success, DangerAreaNodes.HoldShort(ref p, ref ws, _unit, _repo));   // below 0.4: released

        var again = default(HoldShortState);
        Answer(sensor, 4, Area(2, 0.9f, 30f));
        DangerAreaNodes.HoldShort(ref p, ref again, _unit, _repo);
        DangerAreaNodes.Deactivate_HoldShort(ref p, ref again, _unit, _repo);
        Assert.Equal(0, _repo.GetComponentRO<LocomotionChannel>(_unit).ActiveAction);   // the abort stops the move
    }

    /// <summary>⚠ "Ready, 0 areas" is a clear route, not "waiting" (B0, §10.6a): the hold ends at once.</summary>
    [Fact]
    public void CE3079_HoldShort_OnAClearRoute_Succeeds_NotWaits()
    {
        var sensor = Sensor();
        var p = new HoldShortParams { MinThreat = 0.5f, Speed = 2f, ArrivalRadius = 1f };
        var ws = default(HoldShortState);
        Assert.Equal(NodeStatus.Running, DangerAreaNodes.HoldShort(ref p, ref ws, _unit, _repo));   // no answer yet
        Answer(sensor, 1);
        Assert.Equal(NodeStatus.Success, DangerAreaNodes.HoldShort(ref p, ref ws, _unit, _repo));
    }

    /// <summary>🔴 Cross goes to the near side, then across to the FAR side at its speed, and ends there — with the handles it
    /// captured at the start (the answer moves on once the unit is past the area).</summary>
    [Fact]
    public void CE3079_Cross_NearThenFarSide_AtRushSpeed_EndsAtTheFarSide()
    {
        var sensor = Sensor();
        Answer(sensor, 1, Area(1, 0f, 10f));
        var p = new CrossParams { Speed = 5f, ArrivalRadius = 1f };
        var ws = default(CrossState);

        Assert.Equal(NodeStatus.Running, DangerAreaNodes.Cross(ref p, ref ws, _unit, _repo));
        Assert.Equal(new Vector3(101f, 100f, 0f), Destination());
        _repo.GetComponentRW<LocomotionChannel>(_unit).Status = NodeStatus.Success;   // at the near side
        Answer(sensor, 2, Area(9, 0.9f, 80f));                                        // the next area is another one now
        Assert.Equal(NodeStatus.Running, DangerAreaNodes.Cross(ref p, ref ws, _unit, _repo));
        Assert.Equal(new Vector3(121f, 100f, 0f), Destination());                     // the CAPTURED far side
        unsafe
        {
            ref readonly var ch = ref _repo.GetComponentRO<LocomotionChannel>(_unit);
            fixed (byte* src = ch.Params) Assert.Equal(5f, ((MoveToParams*)src)->Speed);
        }
        _repo.GetComponentRW<LocomotionChannel>(_unit).Status = NodeStatus.Success;   // at the far side
        Assert.Equal(NodeStatus.Success, DangerAreaNodes.Cross(ref p, ref ws, _unit, _repo));
    }

    [Fact]
    public void CE3079_LeavingTheTree_ReleasesTheSensor_AndAbortingACrossStopsIt()
    {
        var p = new DangerSensorParams { RouteTo = Objective };
        var ws = default(DangerSensorState);
        DangerAreaNodes.EnsureSensor(ref p, ref ws, _unit, _repo);
        var child = ws.Sensor.ChildId;
        Answer(child, 1, Area(1, 0f, 10f));

        var cp = new CrossParams { Speed = 5f, ArrivalRadius = 1f };
        var cws = default(CrossState);
        DangerAreaNodes.Cross(ref cp, ref cws, _unit, _repo);
        DangerAreaNodes.Deactivate_Cross(ref cp, ref cws, _unit, _repo);
        Assert.Equal(0, _repo.GetComponentRO<LocomotionChannel>(_unit).ActiveAction);

        DangerAreaNodes.Deactivate_EnsureSensor(ref p, ref ws, _unit, _repo);
        _repo.FlushCommandBuffers();
        Assert.False(_repo.IsAlive(child), "the run's sensor goes with the tree");
    }
}
