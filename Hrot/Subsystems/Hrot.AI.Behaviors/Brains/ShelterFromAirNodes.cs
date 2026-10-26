using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using FDP.Eqs;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.Map.Definitions.Behavior;
using Fdp.Toolkit.Behavior.Diagnostics;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-2165</c> V8 — the phases of <see cref="ShelterFromAirNodes.ShelterFromAir"/>.</summary>
    public enum ShelterPhase : byte { Find = 0, ToShelter, Holding, Evading }

    /// <summary>⭐ <c>CE-2165</c> V8 — the parameters of <see cref="ShelterFromAirNodes.ShelterFromAir"/>; a zero means the default.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ShelterFromAirParams
    {
        /// <summary>Done once no air contact has been sensed for this long (s; default 10).</summary>
        public float ClearSeconds;
        /// <summary>With no shelter answer for this long, evade instead (s; default 3).</summary>
        public float FindSeconds;
        /// <summary>Drive speed (m/s; 0 ⇒ the vehicle's own).</summary>
        public float EvadeSpeed;
        /// <summary>How far the shelter query looks (m; default 60) · one evade leg (m; default 40).</summary>
        public float SearchRadius, EvadeLegMetres;
    }

    /// <summary>⭐ <c>CE-2165</c> V8 — the working state (scratch, lost on every exit).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct ShelterFromAirState
    {
        public ShelterPhase Phase;
        /// <summary>1 while a move this node issued runs · the side the next evade leg swerves to (0 / 1).</summary>
        public byte Moving, Side;
        public EqsSensorHandle Sensor;
        public Vector3 Shelter, DroneAt;
        /// <summary>The drone's memory slot (the query's aim key, as <c>PeekAndFireNodes.SensorAim</c>) · where the query was last pointed.</summary>
        public long DroneSlot, SensorSlot;
        public Vector3 SensorAt;
        /// <summary>The newest air sighting / hearing tick seen so far (an increase = sensed again).</summary>
        public uint AirTick;
        /// <summary>When the node began · when an air contact was last sensed · when a failed fire may be retried (sim s).</summary>
        public double StartedAt, LastAirAt, RetryFireAt;
        public EngageState Fire;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2165</c> V8 (D-H; 📄 docs/designs/vehicle-tactics/DESIGN_Vehicle_Exposure_Cycle.md §8) — <c>ShelterFromAir</c>: a vehicle
    /// with a drone about drives under overhead cover (<see cref="FindShelter"/>, reverse allowed) and holds there; with no shelter found
    /// in <c>FindSeconds</c> it EVADES — keeps moving, leg after leg, away from the drone's last position, never stopping. Throughout it
    /// fires at the drone when it is SEEN (the weapon brain fails a mount that cannot elevate — a tank gun — <c>OutOfArc</c>; an HMG on a
    /// pintle can). Success once no air contact has been sensed for <c>ClearSeconds</c>.
    /// </summary>
    public static class ShelterFromAirNodes
    {
        /// <summary>The shelter sensor's site.</summary>
        public const int ShelterSite = 0x21650003;

        public const float ArrivalRadius = 2f;

        /// <summary>A failed fire (out of arc, no weapon for it) is retried after this (s) — not every frame.</summary>
        public const double RetryFireSeconds = 2.0;

        public static readonly ShelterFromAirParams Defaults = new()
        {
            ClearSeconds = 10f, FindSeconds = 3f, SearchRadius = 60f, EvadeLegMetres = 40f,
        };

        public static ShelterFromAirParams Effective(in ShelterFromAirParams a)
        {
            var d = Defaults;
            var e = a;
            if (e.ClearSeconds <= 0f) e.ClearSeconds = d.ClearSeconds;
            if (e.FindSeconds <= 0f) e.FindSeconds = d.FindSeconds;
            if (e.SearchRadius <= 0f) e.SearchRadius = d.SearchRadius;
            if (e.EvadeLegMetres <= 0f) e.EvadeLegMetres = d.EvadeLegMetres;
            return e;
        }

        /// <summary>Running until the sky is clear (Success); Failure when the unit cannot move.</summary>
        [SharedAiAction]
        public static NodeStatus ShelterFromAir(ref ShelterFromAirParams authored, ref ShelterFromAirState ws, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<LocomotionChannel>(self)) return NodeStatus.Failure;
            var p = Effective(in authored);
            double now = Now(world);
            if (ws.StartedAt == 0d) { ws.StartedAt = now; ws.LastAirAt = now; }

            var drone = SenseAir(ref ws, world, self, now);
            if (now - ws.LastAirAt >= p.ClearSeconds)
            {
                Release(ref ws, self, world);
                return NodeStatus.Success;   // the sky is clear
            }
            KeepSensor(ref ws, in p, self, world);
            FireAtDrone(ref ws, self, world, drone, now);

            switch (ws.Phase)
            {
                case ShelterPhase.Find:
                    if (PickShelter(in ws, world, out var shelter)) GoTo(ref ws, in p, self, world, shelter);
                    else if (now - ws.StartedAt >= p.FindSeconds) Evade(ref ws, in p, self, world);
                    break;
                case ShelterPhase.ToShelter:
                {
                    var status = LocomotionMoveTo.Status(world, self);
                    if (status == NodeStatus.Running) break;
                    ws.Moving = 0;
                    if (status == NodeStatus.Success) ws.Phase = ShelterPhase.Holding;
                    else Evade(ref ws, in p, self, world);   // the way there failed: keep moving instead
                    break;
                }
                case ShelterPhase.Holding:
                    break;   // under cover until clear
                case ShelterPhase.Evading:
                {
                    if (PickShelter(in ws, world, out var late)) { GoTo(ref ws, in p, self, world, late); break; }   // a shelter after all
                    var status = LocomotionMoveTo.Status(world, self);
                    if (ws.Moving == 1 && status == NodeStatus.Running) break;
                    ws.Moving = 0;
                    Evade(ref ws, in p, self, world);   // the leg ended (or failed): the next one — never stop
                    break;
                }
            }
            return NodeStatus.Running;
        }

        [BTreeDeactivator("Hrot.AI.Behaviors.Brains.ShelterFromAirNodes.ShelterFromAir")]
        public static void Deactivate_ShelterFromAir(ref ShelterFromAirParams p, ref ShelterFromAirState ws, Entity self, EntityRepository world)
            => Release(ref ws, self, world);

        /// <summary>
        /// The newest air contact in memory (<see cref="AirContacts"/>: a flyer by its <c>Locomotion</c>, heard rotors — the sense's own rule):
        /// its position goes to <c>DroneAt</c>, and a newer sighting / hearing than any before moves <c>LastAirAt</c> to now. Returns the
        /// drone's entity when it is an identified one (Null for heard rotors).
        /// </summary>
        internal static Entity SenseAir(ref ShelterFromAirState ws, EntityRepository world, Entity self, double now)
            => AirSense.Newest(world, self, now, ref ws.AirTick, ref ws.LastAirAt, out ws.DroneAt, out ws.DroneSlot);

        private static void KeepSensor(ref ShelterFromAirState ws, in ShelterFromAirParams p, Entity self, EntityRepository world)
        {
            var cfg = new PostureSensorsParams { SearchRadius = p.SearchRadius, ScoreDeltaThreshold = 0.05f };
            if (ws.DroneSlot == 0) return;   // nothing in the air remembered yet: nothing to shelter from
            // the drone's remembered spot as a POINT keyed by its slot (D14, as PeekAndFire) — set ONCE, when the sensor starts.
            // ⭐ CE-2212: never re-pointed as the drone moves. FindShelter scores from the self alone (slot 0: the grid, the overhead
            //   line, the distance, the path), so the point changes nothing in its answer — and every re-point bumps the epoch and
            //   clears the buffer (EqsChildSensor.Refresh). ⛔ It was re-pointed past 2 m (EqsTacticsNodes.Moved): a 30 m/s drone
            //   moved that far every perception update, so the query never answered and the tank by the deck evaded instead.
            var aim = new ThreatAim(Entity.Null, ws.DroneSlot, ws.DroneAt);
            bool retarget = ws.SensorSlot == 0;
            ws.Sensor = PostureNodes.Keep(ws.Sensor, world, self, ShelterSite, FindShelter.BlueprintId, in cfg, in aim, retarget);
            if (retarget) { ws.SensorSlot = ws.DroneSlot; ws.SensorAt = ws.DroneAt; }
        }

        /// <summary>The shelter query's best answer (the sensor ranks; the first is the best).</summary>
        private static bool PickShelter(in ShelterFromAirState ws, EntityRepository world, out Vector3 point)
        {
            point = default;
            if (!ws.Sensor.IsValid || !world.IsAlive(ws.Sensor.ChildId) || !world.HasComponent<EqsCognitiveBuffer>(ws.Sensor.ChildId)) return false;
            ref readonly var buffer = ref world.GetComponentRO<EqsCognitiveBuffer>(ws.Sensor.ChildId);
            if (!buffer.IsReady || buffer.Count == 0) return false;
            var r = buffer.GetSpanRO()[0];
            point = new Vector3(r.PositionX, r.PositionY, r.PositionZ);
            return true;
        }

        private static void GoTo(ref ShelterFromAirState ws, in ShelterFromAirParams p, Entity self, EntityRepository world, Vector3 shelter)
        {
            ws.Shelter = shelter;
            ws.Moving = (byte)(LocomotionMoveTo.Issue(world, self, shelter, p.EvadeSpeed, ArrivalRadius, reverseAllowed: true, source: MoveSource.Flee) ? 1 : 0);
            ws.Phase = ShelterPhase.ToShelter;
        }

        /// <summary>One evade leg: <c>EvadeLegMetres</c> away from the drone's last position, swerving 35° to alternate sides so the path
        /// is never a straight line a diving drone can lead.</summary>
        private static void Evade(ref ShelterFromAirState ws, in ShelterFromAirParams p, Entity self, EntityRepository world)
        {
            var me = world.HasComponent<SimTransform>(self) ? world.GetComponentRO<SimTransform>(self).Position : Vector3.Zero;
            var away = new Vector2(me.X - ws.DroneAt.X, me.Y - ws.DroneAt.Y);
            if (away.LengthSquared() < 1e-4f) away = new Vector2(1f, 0f);
            away = Vector2.Normalize(away);
            float swerve = (ws.Side == 0 ? 1f : -1f) * 35f * MathF.PI / 180f;
            var dir = new Vector2(away.X * MathF.Cos(swerve) - away.Y * MathF.Sin(swerve), away.X * MathF.Sin(swerve) + away.Y * MathF.Cos(swerve));
            ws.Side ^= 1;
            var to = new Vector3(me.X + dir.X * p.EvadeLegMetres, me.Y + dir.Y * p.EvadeLegMetres, me.Z);
            ws.Moving = (byte)(LocomotionMoveTo.Issue(world, self, to, p.EvadeSpeed, ArrivalRadius, source: MoveSource.Flee) ? 1 : 0);
            ws.Phase = ShelterPhase.Evading;
        }

        /// <summary>Aimed fire at the drone while it is SEEN (<see cref="AirSense.FireAt"/>).</summary>
        private static void FireAtDrone(ref ShelterFromAirState ws, Entity self, EntityRepository world, Entity drone, double now)
            => AirSense.FireAt(world, self, ref ws.Fire, ref ws.RetryFireAt, drone, now);

        private static void Release(ref ShelterFromAirState ws, Entity self, EntityRepository world)
        {
            PostureNodes.StopFiring(world, self, ref ws.Fire);
            PostureNodes.Drop(world, ws.Sensor);
            if (ws.Moving == 1) LocomotionMoveTo.Stop(world, self);
            ws = default;
        }

        private static double Now(EntityRepository world)
            => world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;
    }

    /// <summary>⭐ <c>CE-2165</c> V8 — the library behaviour <c>ShelterFromAir</c> (a reaction: <c>BasicVehicleSop</c>'s AirThreat row).</summary>
    public static class ShelterFromAirBehavior
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct ShelterFromAirBlackboard
        {
            public ShelterFromAirParams Shelter;
            public ShelterFromAirState ShelterState;
        }

        [BehaviorResolver(BehaviorNames.ShelterFromAir)]
        public static void Resolve(in NoParams authored, ref ShelterFromAirBlackboard block, EntityRepository world, Entity self)
            => block = default;

        [Behavior(BehaviorNames.ShelterFromAir, Params = typeof(NoParams), Categories = BehaviorCategory.MilitaryApc)]   // it takes nothing (P1): the defaults
        [BTreeDefinition(BehaviorNames.ShelterFromAir, Curated = true, Group = "Library/Vehicle", ParamsType = typeof(ShelterFromAirBlackboard))]
        public static BTreeBuilder<ShelterFromAirBlackboard, BTreeContext> BuildTree()
            => new BTreeBuilder<ShelterFromAirBlackboard, BTreeContext>()
                .StatefulAction(bb => bb.Shelter, bb => bb.ShelterState, ShelterFromAirNodes.ShelterFromAir);
    }
}
