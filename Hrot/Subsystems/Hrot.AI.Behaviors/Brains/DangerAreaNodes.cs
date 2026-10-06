using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using FDP.Eqs;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Perception.Sensors;
using Fdp.Toolkit.Squad.DangerArea;
using Fbt.Kernel;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-3079</c> H4 — the behaviour's own danger-area sensor: the route it watches is ALWAYS unit → <see cref="RouteTo"/>
    /// (route source ③ "to a point", §10.2 — never the unit's own move, which the reaction's move would change).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DangerSensorParams
    {
        /// <summary>Where the watched route ends — the behaviour's objective.</summary>
        public Vector3 RouteTo;
        /// <summary>How far either side of the route an area counts (m); 0 = the sensor's default.</summary>
        public float CorridorHalfWidth;
        /// <summary>How often the route is re-asked (s); 0 = the sensor's default.</summary>
        public float RefreshSeconds;
        /// <summary>At most this many areas (≤ 8); 0 = the sensor's default.</summary>
        public byte MaxAreas;
    }

    /// <summary>⭐ <c>CE-3079</c> H4 — the sensor this run owns.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DangerSensorState
    {
        /// <summary>The run's danger-area sensor child.</summary>
        public EqsSensorHandle Sensor;
    }

    /// <summary>⭐ <c>CE-3079</c> H4 — "is the NEXT area ahead one to react to?"</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DangerAheadParams
    {
        /// <summary>The area's rated threat is at least this (0 = any area).</summary>
        public float MinThreat;
        /// <summary>…and it starts within this many metres along the route (0 = any distance).</summary>
        public float WithinMetres;
        /// <summary>…and its kind is one of these (bit <c>1 &lt;&lt; (int)DangerAreaKind</c>; 0 = any kind).</summary>
        public uint KindMask;
    }

    /// <summary>⭐ <c>CE-3079</c> H4 — how <see cref="DangerAreaNodes.HoldShort"/> holds.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HoldShortParams
    {
        /// <summary>The hold ends once the area's threat is below <c>MinThreat − 0.1</c> (hysteresis), or no area is ahead.</summary>
        public float MinThreat;
        /// <summary>Travel speed to the near side (m/s).</summary>
        public float Speed;
        /// <summary>Distance from the near side that counts as arrived (m).</summary>
        public float ArrivalRadius;
    }

    /// <summary>⭐ <c>CE-3079</c> H4 — the hold in progress.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HoldShortState
    {
        /// <summary>The area held for (its <c>FeatureId</c>); a different area first ⇒ a new move.</summary>
        public uint FeatureId;
        /// <summary>1 once the move to the near side was issued.</summary>
        public byte Moving;
    }

    /// <summary>⭐ <c>CE-3079</c> H4 — how <see cref="DangerAreaNodes.Cross"/> crosses.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CrossParams
    {
        /// <summary>Rush speed across (m/s).</summary>
        public float Speed;
        /// <summary>Distance from each handle that counts as arrived (m).</summary>
        public float ArrivalRadius;
    }

    /// <summary>⭐ <c>CE-3079</c> H4 — the crossing in progress (the handles are captured at the start: the answer's entry 0
    /// changes once the unit is past the area).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CrossState
    {
        /// <summary>The far side, captured at the start.</summary>
        public Vector3 FarSide;
        /// <summary>0 = not started · 1 = going to the near side · 2 = going to the far side.</summary>
        public byte Phase;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-3079</c> H4 — the danger-crossing nodes, as shared C# nodes a BTree (and an HSM) binds: the SHAPE of
    /// <see cref="EqsTacticsNodes"/> (self-contained, a run-owned sensor, the one MoveTo write) — ⛔ not its ranked body.
    /// They only READ the processed answer (<see cref="DangerAreaCognitiveBuffer"/> on the sensor child, entry 0 = the next
    /// area ahead, threat already rated — the producer's job, B2); they never rate or sort.
    /// 📄 <c>docs/DESIGN_Utility_AI_Demo_Scenarios.md</c> §10.4. ⚠ Placed here, not in <c>Fdp.Toolkit.Behavior</c>: the move goes
    /// through <see cref="LocomotionMoveTo"/>, which this assembly owns (§10.4a).
    /// </summary>
    public static class DangerAreaNodes
    {
        /// <summary>The sensor site (with the run's owner stamp, CE-485, the run finds its sensor again).</summary>
        public const int SensorSite = 0x30790001;

        /// <summary>Ensures the run's danger-area sensor watching unit → <see cref="DangerSensorParams.RouteTo"/>; Success once it
        /// exists (it lives until the run ends). Failure when it cannot be created.</summary>
        [SharedAiAction]
        public static NodeStatus EnsureSensor(ref DangerSensorParams p, ref DangerSensorState ws, Entity self, EntityRepository world)
        {
            if (ws.Sensor.IsValid && world.IsAlive(ws.Sensor.ChildId)) return NodeStatus.Success;
            var settings = DangerAreaSettings.ToPoint(p.RouteTo);
            if (p.CorridorHalfWidth > 0f) settings.CorridorHalfWidth = p.CorridorHalfWidth;
            if (p.RefreshSeconds > 0f) settings.RefreshSeconds = p.RefreshSeconds;
            if (p.MaxAreas > 0) settings.MaxAreas = p.MaxAreas;
            var child = DangerAreaChildSensor.Ensure(world, self, SensorSite, in settings);
            if (child.IsNull) return NodeStatus.Failure;
            ws.Sensor = new EqsSensorHandle(child);
            return NodeStatus.Success;
        }

        /// <summary>True when the next area ahead is rated at least <see cref="DangerAheadParams.MinThreat"/>, starts within
        /// <see cref="DangerAheadParams.WithinMetres"/> along the route and is of a kind in <see cref="DangerAheadParams.KindMask"/>.</summary>
        [SharedAiCondition]
        public static bool DangerAhead(ref DangerAheadParams p, Entity self, EntityRepository world)
            => NextArea(world, self, out var area)
               && area.ThreatRating >= p.MinThreat
               && (p.WithinMetres <= 0f || area.DistanceAlongRoute <= p.WithinMetres)
               && (p.KindMask == 0 || (p.KindMask & (1u << (int)area.Kind)) != 0);

        /// <summary>
        /// Moves to the next area's NEAR side — ONE move, then holds there (no re-issue every tick; a different area first or a
        /// failed move issues it again). Success once the area's threat is below <c>MinThreat − 0.1</c>, or no area is ahead
        /// (an answer with NO areas is a clear route, not "waiting"). Running while it holds or the sensor has not answered yet.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus HoldShort(ref HoldShortParams p, ref HoldShortState ws, Entity self, EntityRepository world)
        {
            if (!UnitSensors.TryGetResults<DangerAreaCognitiveBuffer>(world, self, SensorModality.DangerArea, out var buffer)
                || !buffer.IsReady)
                return NodeStatus.Running;
            if (buffer.Count == 0 || buffer.GetSpanRO()[0].ThreatRating < p.MinThreat - 0.1f)
            {
                End(ref ws.Moving, self, world);
                ws = default;
                return NodeStatus.Success;
            }
            var area = buffer.GetSpanRO()[0];
            bool reissue = ws.Moving == 0 || area.FeatureId != ws.FeatureId
                        || LocomotionMoveTo.Status(world, self) == NodeStatus.Failure;
            if (reissue)
            {
                if (!LocomotionMoveTo.Issue(world, self, area.NearSideHandle, p.Speed, p.ArrivalRadius)) return NodeStatus.Failure;
                ws.Moving = 1;
                ws.FeatureId = area.FeatureId;
            }
            return NodeStatus.Running;
        }

        /// <summary>
        /// Crosses the next area: to its near side, then across to its far side at <see cref="CrossParams.Speed"/>. Success at
        /// the far side, or at once when no area is ahead. The handles are captured at the start.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus Cross(ref CrossParams p, ref CrossState ws, Entity self, EntityRepository world)
        {
            if (ws.Phase == 0)
            {
                if (!UnitSensors.TryGetResults<DangerAreaCognitiveBuffer>(world, self, SensorModality.DangerArea, out var buffer)
                    || !buffer.IsReady)
                    return NodeStatus.Running;
                if (buffer.Count == 0) return NodeStatus.Success;
                var area = buffer.GetSpanRO()[0];
                if (!LocomotionMoveTo.Issue(world, self, area.NearSideHandle, p.Speed, p.ArrivalRadius)) return NodeStatus.Failure;
                ws.FarSide = area.FarSideHandle;
                ws.Phase = 1;
                return NodeStatus.Running;
            }
            var status = LocomotionMoveTo.Status(world, self);
            if (status == NodeStatus.Running) return NodeStatus.Running;
            if (status == NodeStatus.Failure) { ws = default; return NodeStatus.Failure; }
            if (ws.Phase == 1)
            {
                if (!LocomotionMoveTo.Issue(world, self, ws.FarSide, p.Speed, p.ArrivalRadius)) return NodeStatus.Failure;
                ws.Phase = 2;
                return NodeStatus.Running;
            }
            ws = default;   // at the far side
            return NodeStatus.Success;
        }

        /// <summary>Leaving the node: the run's sensor goes.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.DangerAreaNodes.EnsureSensor")]
        public static void Deactivate_EnsureSensor(ref DangerSensorParams p, ref DangerSensorState ws, Entity self, EntityRepository world)
        {
            if (ws.Sensor.IsValid && world.IsAlive(ws.Sensor.ChildId)) DangerAreaChildSensor.Release(world, ws.Sensor.ChildId);
            ws = default;
        }

        /// <summary>Leaving the hold: the move it issued stops.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.DangerAreaNodes.HoldShort")]
        public static void Deactivate_HoldShort(ref HoldShortParams p, ref HoldShortState ws, Entity self, EntityRepository world)
        {
            End(ref ws.Moving, self, world);
            ws = default;
        }

        /// <summary>Leaving the crossing: the move it issued stops.</summary>
        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.DangerAreaNodes.Cross")]
        public static void Deactivate_Cross(ref CrossParams p, ref CrossState ws, Entity self, EntityRepository world)
        {
            if (ws.Phase != 0) LocomotionMoveTo.Stop(world, self);
            ws = default;
        }

        /// <summary>The next area ahead (entry 0 of the processed answer); false when there is no answer or no area.</summary>
        private static bool NextArea(EntityRepository world, Entity self, out DangerAreaDescriptor area)
        {
            area = default;
            if (!UnitSensors.TryGetResults<DangerAreaCognitiveBuffer>(world, self, SensorModality.DangerArea, out var buffer)
                || !buffer.IsReady || buffer.Count == 0)
                return false;
            area = buffer.GetSpanRO()[0];
            return true;
        }

        private static void End(ref byte moving, Entity self, EntityRepository world)
        {
            if (moving == 1) LocomotionMoveTo.Stop(world, self);
            moving = 0;
        }
    }
}
