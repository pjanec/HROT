using System;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Hrot.Map.Common;
using Hrot.SimHost.Integration.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace Hrot.SimHost.Integration.Tests
{
    /// <summary>
    /// ⭐⭐⭐ <b>DIAGNOSTIC PROBE — why does a MISSION not move an entity, when a direct
    /// <c>NavState</c> write does?</b>
    ///
    /// <para>📐 <b>The measurement that scopes this</b> (<c>2026-09-01</c>): in
    /// <c>MissionExecutionFlowTests</c> the sibling rail
    /// <c>MoveToLocation_TankNavigates_GeoSpatialChangesAfter10s</c> — same fixture, same class,
    /// same 10 s, same &gt;50 m assertion — <b>PASSES</b>. It writes <c>NavState</c> directly.
    /// ⇒ ⛔ the physics chain, the clock and the harness tick order are all INNOCENT, and this is
    /// <b>not</b> the <c>CE-103</c> shape (a frozen clock / an unticked bridge). The defect is
    /// upstream of <c>NavState</c>, in the MISSION → BEHAVIOUR tier.</para>
    ///
    /// <para>⭐⭐ That tier is a five-hop chain and the failing assertion ("moved 0.0m") cannot say
    /// which hop broke:</para>
    /// <list type="number">
    ///   <item><c>PublishEntityMission</c> → <c>MissionPlanQueue</c> + managed <c>ActiveMissionPlan</c>;
    ///     the queue's <c>Phases[i].BehaviorId</c> comes from
    ///     <c>BehaviorRegistry.TryGetId(name)</c> and is <b>0 when the name is unknown</b>;</item>
    ///   <item><c>MissionAdapterSystem</c> → publishes <c>AssignTacticalIntentEvent</c>, but ONLY when
    ///     <c>task.BehaviorName</c> is non-blank and the phase actually CHANGED;</item>
    ///   <item><c>TacticalIntentResolutionSystem</c> → <c>AssignBehaviorEvent</c>;</item>
    ///   <item><c>BehaviorIngressSystem</c> → <c>BehaviorState</c> + the blackboard params;</item>
    ///   <item>the behaviour itself → <c>NavigationIntent</c> → <c>NavState</c>.</item>
    /// </list>
    ///
    /// <para>⚠ This probe asserts <b>nothing</b> about movement. It DUMPS each hop so the next
    /// session reads a cause instead of re-deriving one, and it is a rail rather than a scratch
    /// script so the measurement is repeatable.</para>
    /// </summary>
    public sealed class WhyDoesTheMissionNotMoveProbe : IDisposable
    {
        private readonly ITestOutputHelper _out;
        private readonly SimHostInstance   _host;
        private int _ticksRun;

        public WhyDoesTheMissionNotMoveProbe(ITestOutputHelper output)
        {
            _out  = output;
            _host = new SimHostInstance();
        }

        public void Dispose() => _host.Dispose();

        [Fact]
        [Trait("Category", "Diagnostic")]
        public void DumpEveryHopOfTheMissionChain()
        {
            var ack = _host.CreateEntity(TkbEntityTypes.Tank_M1Abrams);
            Assert.Equal(0, ack.StatusCode);
            Assert.True(_host.EntityMap.TryGetEntity(ack.EntityId, out var e),
                "probe precondition: the spawned entity must be in the entity map");

            var world = _host.World;

            _out.WriteLine("── GATE 0: what does this harness actually SCHEDULE? ───────");
            DumpSystems("input  ", _host.TestHook_InputSystems);
            DumpSystems("sim    ", _host.TestHook_SimulationSystems);
            DumpSystems("postSim", _host.TestHook_PostSimSystems);

            _out.WriteLine("── GATE 1: does the BehaviorRegistry know 'MoveToLocation'? ─");
            bool known = _host.TestHook_BehaviorRegistry.TryGetId("MoveToLocation", out int moveId);
            _out.WriteLine($"  TryGetId(\"MoveToLocation\") = {known}, id = {moveId}"
                         + (known ? "" : "   ⛔ UNKNOWN ⇒ MissionPhase.BehaviorId is seeded 0"));

            // ── Publish the mission exactly as EntityMission_MovesEntity does ────────
            var dest = _host.CartesianToGeo(new Vector3(500, 0, 0));
            var mission = new Hrot.NED.Descriptors.EntityMission
            {
                EntityId = ack.EntityId,
                Plan = new Hrot.NED.Descriptors.MissionPlan
                {
                    Tasks = new System.Collections.Generic.List<Hrot.NED.Descriptors.MissionTask>
                    {
                        new Hrot.NED.Descriptors.MissionTask
                        {
                            BehaviorId = "MoveToLocation",
                            BehaviorParams =
                                $"{{\"TargetLat\":{Inv(dest.Latitude)},\"TargetLon\":{Inv(dest.Longitude)}," +
                                 "\"Speed\":15.0,\"ArrivalRadius\":5.0}",
                        },
                    },
                },
            };
            _host.PublishEntityMission(mission);

            _out.WriteLine("── GATE 2: what did PublishEntityMission put on the entity? ─");
            _out.WriteLine($"  MissionPlanQueue present  = {world.HasComponent<MissionPlanQueue>(e)}");
            _out.WriteLine($"  BehaviorState    present  = {world.HasComponent<BehaviorState>(e)}"
                         + (world.HasComponent<BehaviorState>(e)
                            ? ""
                            : "   ⛔ MissionAdapterSystem's query REQUIRES it — the entity is skipped"));
            if (world.HasComponent<MissionPlanQueue>(e))
            {
                var q = world.GetComponent<MissionPlanQueue>(e);
                _out.WriteLine($"    PhaseCount={q.PhaseCount} CurrentPhase={q.CurrentPhase} "
                             + $"Phases[0].BehaviorId={q.Phases[0].BehaviorId} "
                             + $"Trigger={q.Phases[0].Trigger} TriggerParam={q.Phases[0].TriggerParam}");
            }
            var plan = world.GetComponent<ActiveMissionPlan>(e);
            _out.WriteLine($"  ActiveMissionPlan present = {plan != null}");
            if (plan?.Plan?.Tasks is { Count: > 0 } tasks)
                _out.WriteLine($"    Tasks[0].BehaviorName='{tasks[0].BehaviorName}' "
                             + $"ParamsLen={tasks[0].BehaviorParams?.Length ?? -1}"
                             + (string.IsNullOrWhiteSpace(tasks[0].BehaviorName)
                                ? "   ⛔ BLANK ⇒ MissionAdapterSystem publishes NOTHING"
                                : ""));

            _out.WriteLine("── GATE 2b: is the DESTINATION the test asked for even reachable? ─");
            _out.WriteLine($"  CartesianToGeo(500,0,0) = ({Inv(dest.Latitude)}, {Inv(dest.Longitude)})");
            var destBack = _host.GeoToCartesian(new Hrot.NED.Common.GeoPoint
            {
                Latitude = dest.Latitude, Longitude = dest.Longitude, Altitude = dest.Altitude,
            });
            _out.WriteLine($"  …GeoToCartesian back    = ({destBack.X:F2}, {destBack.Y:F2})"
                         + "   (should be ~500,0 — if it is, the test's own target is fine)");

            // ── Run tick-by-tick so a WRITE-THEN-RESET is distinguishable from a NO-WRITE ──
            _out.WriteLine("── GATE 2c: per-tick trace of intent vs NavState ───────────");
            var deltaProbeQuery = world.Query().With<NavigationIntent>().With<CarKinem.Core.NavState>().Build();
            foreach (int upTo in new[] { 1, 2, 3, 4, 5, 10, 30, 60 })
            {
                while (_ticksRun < upTo)
                {
                    // ⭐ Snapshot the version at the START of the tick, then ask — after the tick —
                    //   the EXACT question the scheduled bridge asks: does QueryDelta see this entity?
                    uint vAtTickStart = world.GlobalVersion;
                    _host.RunForTicks(1); _ticksRun++;
                    int seen = 0;
                    foreach (var _d in world.QueryDelta(deltaProbeQuery, vAtTickStart)) seen++;
                    if (_ticksRun <= 5)
                        _out.WriteLine($"    [delta] tick {_ticksRun}: version {vAtTickStart}→{world.GlobalVersion}, "
                                     + $"QueryDelta(since tick-start) matched {seen}");
                }
                var ni = world.HasComponent<NavigationIntent>(e)
                    ? world.GetComponent<NavigationIntent>(e) : default;
                var ns = world.HasComponent<CarKinem.Core.NavState>(e)
                    ? world.GetComponent<CarKinem.Core.NavState>(e) : default;
                var tf0 = world.GetComponent<SimTransform>(e);
                _out.WriteLine($"  t={upTo,3}  intent[Mode={ni.Mode} Id={ni.IntentId} "
                             + $"Dest=({ni.FinalDestination.X:F1},{ni.FinalDestination.Y:F1}) "
                             + $"Spd={ni.TargetSpeed} Arr={ni.ArrivalRadius}]  "
                             + $"nav[Mode={ns.Mode} Spd={ns.TargetSpeed} "
                             + $"Dest=({ns.FinalDestination.X:F1},{ns.FinalDestination.Y:F1}) "
                             + $"Arrived={ns.HasArrived}]  pos=({tf0.Position.X:F2},{tf0.Position.Y:F2})");
            }

            // ── Run and watch the downstream hops ────────────────────────────────────
            _host.RunForSeconds(10f);

            _out.WriteLine("── GATE 3: did the adapter actually FIRE? ──────────────────");
            bool hasAdapterState = world.HasComponent<Hrot.CGF.Components.MissionAdapterState>(e);
            _out.WriteLine($"  MissionAdapterState present = {hasAdapterState}"
                         + (hasAdapterState
                            ? ""
                            : "   ⛔ the adapter never reached this entity at all"));
            if (hasAdapterState)
            {
                var st = world.GetComponent<Hrot.CGF.Components.MissionAdapterState>(e);
                _out.WriteLine($"    LastPhase={st.LastPhase} LastPlanVersion={st.LastPlanVersion}");
            }

            _out.WriteLine("── GATE 4: did a BEHAVIOUR get assigned? ───────────────────");
            if (world.HasComponent<BehaviorState>(e))
            {
                var bs = world.GetComponent<BehaviorState>(e);
                _out.WriteLine($"  BehaviorState -> {bs}");
            }

            _out.WriteLine("── GATE 5: did the behaviour produce an INTENT? ────────────");
            bool hasIntent = world.HasComponent<NavigationIntent>(e);
            _out.WriteLine($"  NavigationIntent present = {hasIntent}");
            if (hasIntent)
            {
                var ni = world.GetComponent<NavigationIntent>(e);
                _out.WriteLine($"    Mode={ni.Mode} Dest=({ni.FinalDestination.X:F1},{ni.FinalDestination.Y:F1}) "
                             + $"TargetSpeed={ni.TargetSpeed} IntentId={ni.IntentId}");
            }

            _out.WriteLine("── GATE 6: did the intent reach NavState (what physics reads)? ─");
            if (world.HasComponent<CarKinem.Core.NavState>(e))
            {
                var ns = world.GetComponent<CarKinem.Core.NavState>(e);
                _out.WriteLine($"  NavState.Mode={ns.Mode} TargetSpeed={ns.TargetSpeed} "
                             + $"Dest=({ns.FinalDestination.X:F1},{ns.FinalDestination.Y:F1}) "
                             + $"HasArrived={ns.HasArrived}"
                             + (ns.Mode == CarKinem.Core.KinematicsMode.None
                                ? "   ⛔ Mode=None ⇒ CarKinematicsSystem drives nothing"
                                : ""));
            }
            else _out.WriteLine("  ⛔ no NavState component at all");

            _out.WriteLine("── GATE 6b: is it the bridge's QUERY, its DELTA FILTER, or its LOGIC? ─");
            var bridgeQuery = world.Query().With<NavigationIntent>().With<CarKinem.Core.NavState>().Build();
            int plain = 0; foreach (var _x in bridgeQuery) plain++;
            int deltaFromZero = 0; foreach (var _x in world.QueryDelta(bridgeQuery, 0)) deltaFromZero++;
            _out.WriteLine($"  plain .With<NavigationIntent>().With<NavState>() = {plain}");
            _out.WriteLine($"  QueryDelta(since 0)                              = {deltaFromZero}");
            if (plain > 0 && deltaFromZero == 0)
                _out.WriteLine("  ⛔⛔ the entity matches the QUERY but not the DELTA ⇒ the bridge's "
                             + "QueryDelta can never reach it.");
            // Drive a FRESH bridge by hand: its _lastScanTick is 0 and its caches are empty, so this
            // isolates the MAPPING LOGIC from the scheduled instance's filter state.
            new Fdp.Toolkit.Navigation.Systems.NavigationIntentBridgeSystem().Execute(world, 0.016f);
            var navByHand = world.GetComponent<CarKinem.Core.NavState>(e);
            _out.WriteLine($"  NavState after ONE hand-driven Execute: Mode={navByHand.Mode} "
                         + $"Spd={navByHand.TargetSpeed} Dest=({navByHand.FinalDestination.X:F1},"
                         + $"{navByHand.FinalDestination.Y:F1})");
            // ⚠ Read this against GATE 6. If GATE 6 already showed Mode != None the scheduled bridge is
            //   working and this line merely rewrites the same value — it proves nothing on its own.
            //   It is decisive only when GATE 6 showed None: a fresh instance starts with
            //   _lastScanTick = 0 and empty caches, so a write HERE but not THERE isolates the
            //   scheduled instance's FILTER STATE from the mapping LOGIC. That is exactly how the
            //   frozen-GlobalVersion cause was found on 2026-09-01.
            _out.WriteLine(navByHand.Mode != CarKinem.Core.KinematicsMode.None
                ? "  ⇒ the mapping LOGIC works (decisive only if GATE 6 above said Mode=None)."
                : "  ⭐ the bridge ran fresh and still wrote nothing ⇒ the defect is INSIDE its logic.");

            // ⭐⭐ THE DISCRIMINATOR: a hand-written NavState now exists. Run ONE more scheduled tick.
            //   If it SURVIVES, the scheduled bridge simply never fires (a filter-state problem).
            //   If it is WIPED, some later system in the tick resets NavState every frame — a very
            //   different defect, and the per-tick trace above cannot tell them apart on its own.
            _host.RunForTicks(1);
            var navAfterOneTick = world.GetComponent<CarKinem.Core.NavState>(e);
            _out.WriteLine($"  NavState after ONE more SCHEDULED tick: Mode={navAfterOneTick.Mode} "
                         + $"Spd={navAfterOneTick.TargetSpeed} Arrived={navAfterOneTick.HasArrived}");
            _out.WriteLine(navAfterOneTick.Mode == CarKinem.Core.KinematicsMode.None
                ? "  ⛔⛔ WIPED ⇒ a LATER system in the tick resets NavState — not a bridge-filter problem."
                : "  ⭐ SURVIVED ⇒ nothing resets it; the scheduled bridge simply never applies the intent.");
            var tfAfter = world.GetComponent<SimTransform>(e);
            _out.WriteLine($"  position after that tick = ({tfAfter.Position.X:F3}, {tfAfter.Position.Y:F3})");

            _out.WriteLine("── THE OUTCOME ────────────────────────────────────────────");
            var tf = world.GetComponent<SimTransform>(e);
            _out.WriteLine($"  SimTransform.Position = ({tf.Position.X:F3}, {tf.Position.Y:F3}, {tf.Position.Z:F3})");
            var geo = _host.ReadGeoSpatial(ack.EntityId);
            if (geo != null)
            {
                var cart = _host.GeoToCartesian(geo.Value.Pos);
                _out.WriteLine($"  distance from origin  = {Vector2.Distance(cart, Vector2.Zero):F2} m");
            }
        }

        private void DumpSystems(string label, System.Collections.Generic.IReadOnlyList<Fdp.ModuleHost.Abstractions.IEcsModuleSystem> systems)
        {
            _out.WriteLine($"  {label} ({systems.Count}): "
                         + string.Join(", ", systems.Select(s => s.GetType().Name)));
        }

        private static string Inv(double v) =>
            v.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
