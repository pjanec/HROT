using System.Runtime.InteropServices;
using Fbt;
using Fbt.Compiler;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Perception.Events;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>⭐ <c>CE-4670</c> D-10 A — the crew's hatch rule's state: what it last asked (0 nothing yet · 1 out · 2 in) and its quiet window.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CrewHatchParams
    {
        /// <summary>The commander goes back out after this long with nothing threatening (s).</summary>
        public float QuietSeconds;
        /// <summary>Runtime: 0 nothing asked yet · 1 head out · 2 head in.</summary>
        public byte Asked;

        public CrewHatchParams() { QuietSeconds = VehicleCrewNodes.QuietSeconds; Asked = 0; }
    }

    /// <summary>⭐ <c>CE-4670</c> D-10 B — the lights rule's state: the standing discipline, the order override, and what was last asked
    /// (each 0 = none, else 1 + the <see cref="LightDiscipline"/>), and the task instance the override was read for.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CrewLightsParams
    {
        public byte Standing;
        public byte Override;
        public byte Asked;
        /// <summary>The discipline last seen on the vehicle (an outside change is one that is neither seen nor ours).</summary>
        public byte Seen;
        public uint ReadFor;
    }

    /// <summary>⭐ <c>CE-4670</c> VA-S5 — "pop smoke": the cool-down between salvos, and what was last answered (a launch's time, a hit's time)
    /// and when the last salvo left.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CrewSmokeParams
    {
        /// <summary>No second salvo within this long of the last (s).</summary>
        public float CooldownSeconds;
        /// <summary>Runtime: the launch warning last answered (sim s) · the hit last answered · the last salvo.</summary>
        public double AnsweredLaunch, AnsweredHit, LastSalvo;

        public CrewSmokeParams() { CooldownSeconds = VehicleCrewNodes.SmokeCooldownSeconds; AnsweredLaunch = AnsweredHit = -1d; LastSalvo = -1e9; }   // finite: a blackboard is written as JSON
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4670</c> D-10 A (user-approved `2026-11-19`; Vehicle Appearance VA-4: <i>"a crew member's head out (observing, an order, the SOP) is a
    /// request"</i>) — the vehicle's SOP decides its commander's head-out: OUT while calm, IN (buttoned up) on a hit, a near miss, a first threat or a
    /// new contact, OUT again after <see cref="QuietSeconds"/> with none of them. ONE ask per change (<see cref="VehicleRequests.HeadOut"/>, an event:
    /// the boarding arbiter applies it, so nothing here writes another entity in the parallel phase). It never takes the vehicle's task.
    /// 📄 docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §6a.
    /// </summary>
    public static class VehicleCrewNodes
    {
        /// <summary>The quiet spell after which the commander puts his head out again (s) — the stress calm's order of magnitude.</summary>
        public const float QuietSeconds = 30f;

        /// <summary>Is the vehicle threatened now: a hit, a near miss, a first threat or a new contact within <paramref name="seconds"/>?</summary>
        public static bool Threatened(EntityRepository world, Entity vehicle, double seconds)
            => RecentSensesOf.Within(world, vehicle, SensorChange.Hit, seconds)
               || RecentSensesOf.Within(world, vehicle, SensorChange.NearMiss, seconds)
               || RecentSensesOf.Within(world, vehicle, SensorChange.FirstThreat, seconds)
               || RecentSensesOf.Within(world, vehicle, SensorChange.Acquired, seconds);

        /// <summary>The man in the vehicle's Commander seat, or null (no such seat, or it is empty).</summary>
        public static Entity Commander(EntityRepository world, Entity vehicle)
        {
            if (!world.IsComponentTypeRegistered<PassengerBuffer>() || !world.HasComponent<PassengerBuffer>(vehicle)
                || !world.IsComponentTypeRegistered<CarrierSeats>() || !world.HasComponent<CarrierSeats>(vehicle)) return Entity.Null;
            ref readonly var list = ref world.GetComponentRO<PassengerBuffer>(vehicle);
            ref readonly var seats = ref world.GetComponentRO<CarrierSeats>(vehicle);
            for (int seat = 0; seat < PassengerBuffer.Capacity; seat++)
                if (seats.Roles[seat] == SeatRole.Commander && !list.Passengers[seat].IsNull) return list.Passengers[seat];
            return Entity.Null;
        }

        /// <summary>
        /// The rule, run each tick by <see cref="BasicVehicleSop"/> before its rows: asks the commander OUT or IN when what he should be differs from
        /// what was last asked. Always Success (the SOP wraps it so its rows still choose the reaction).
        /// </summary>
        [SharedAiAction]
        public static NodeStatus HatchByThreat(ref CrewHatchParams p, Entity self, EntityRepository world)
        {
            var commander = Commander(world, self);
            if (commander.IsNull) return NodeStatus.Success;
            byte want = Threatened(world, self, p.QuietSeconds > 0f ? p.QuietSeconds : QuietSeconds) ? (byte)2 : (byte)1;
            if (want == p.Asked) return NodeStatus.Success;
            if (VehicleRequests.HeadOut(world, self, commander, want == 1)) p.Asked = want;
            return NodeStatus.Success;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-4670</c> D-10 B (user-approved `2026-11-19`) — the light discipline: the vehicle's STANDING one is its authored
        /// <see cref="VehicleLights.Discipline"/> (Normal unless the scenario set it — ⚠ the decision said "an SOP parameter"; an SOP takes no params
        /// (CE-2132), so the per-unit default is the authored component, which a trigger or an operator may also change); while an ORDER whose params
        /// carry <c>Lights</c> runs (<c>MoveOrder</c> / <c>ConvoyOrder</c>), that one is asked for; when it ends, the standing one again. One ask per change
        /// (<see cref="VehicleRequests.SwitchLights"/>, an event). The order's JSON is read once per task instance — never per frame. Always Success.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus LightsByOrder(ref CrewLightsParams p, Entity self, EntityRepository world)
        {
            if (!world.IsComponentTypeRegistered<VehicleLights>() || !world.HasComponent<VehicleLights>(self)) return NodeStatus.Success;
            byte now = (byte)(1 + (byte)world.GetComponentRO<VehicleLights>(self).Discipline);
            if (p.Standing == 0) { p.Standing = now; p.Seen = now; }
            ReadOverride(ref p, self, world);
            if (now != p.Seen)
            {
                if (now != p.Asked && p.Override == 0) p.Standing = now;   // changed from OUTSIDE (a trigger, an operator) — not our ask: the new standing
                p.Seen = now;
            }
            byte want = p.Override != 0 ? p.Override : p.Standing;
            if (want == now || want == p.Asked) return NodeStatus.Success;   // already so, or asked (the owner applies it next frame)
            if (VehicleRequests.SwitchLights(world, self, (LightDiscipline)(want - 1), self)) p.Asked = want;
            return NodeStatus.Success;
        }

        /// <summary>No second smoke salvo within this long of the last (s) — a screen lasts a minute; half of it is the crew's patience.</summary>
        public const float SmokeCooldownSeconds = 30f;
        /// <summary>A launch or a hit older than this is history, never a reason to pop smoke (s) — the SOP's "fresh".</summary>
        public const double SmokeFreshSeconds = 3d;

        /// <summary>
        /// ⭐⭐ <c>CE-4670</c> VA-S5 (VA-9: the discharger is backend's mount, "pop smoke" a behaviour) — the vehicle screens itself when THREATENED:
        /// a guided missile launched at it (its brain's <see cref="Fdp.Toolkit.Combat.Components.MissileWarning"/> — the crew sees the launch) or a
        /// hit it survived (its <c>SensorChange.Hit</c>, towards its top known threat). ONE salvo of its smoke dischargers
        /// (<see cref="Fdp.Toolkit.Combat.Countermeasures.MountOfKind"/>) at the threat — the fan centred on its bearing — then nothing for
        /// <see cref="SmokeCooldownSeconds"/>. ⭐ It never takes the weapon channel (the gun's own fire goes on): the round is shot directly
        /// (<see cref="Fdp.Toolkit.Combat.Executors.FireAtPointExecutor.ShootAtPoint"/>, the dispenser's own way out). The move out of sight is the
        /// fight-from-cover cycle's (it withdraws on a launch or a hit). Always Success. 📄 DESIGN_Vehicle_Appearance_State.md §6f.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus SmokeByThreat(ref CrewSmokeParams p, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<RecentSenses>(self)) return NodeStatus.Success;
            double now = world.HasSingleton<GlobalTime>() ? world.GetSingleton<GlobalTime>().TotalTime : 0d;
            ref readonly var senses = ref world.GetComponentRO<RecentSenses>(self);
            System.Numerics.Vector3 at = default;
            bool threat = false;
            if (senses.TryGetLast(SensorChange.MissileWarning, out double launch) && launch > p.AnsweredLaunch && now - launch <= SmokeFreshSeconds
                && world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.MissileWarning>()
                && world.HasComponent<Fdp.Toolkit.Combat.Components.MissileWarning>(self))
            {
                p.AnsweredLaunch = launch;
                at = world.GetComponentRO<Fdp.Toolkit.Combat.Components.MissileWarning>(self).From;
                threat = true;
            }
            else if (senses.TryGetLast(SensorChange.Hit, out double hit) && hit > p.AnsweredHit && now - hit <= SmokeFreshSeconds)
            {
                p.AnsweredHit = hit;
                // ⭐⭐ CE-4698 (user: "build the remaining three too") — a survived HIT is screened only by a hull that can back OUT of the cloud:
                //   one standing and fighting (no hide, or it cannot drive — VehicleFightFromCover's NoHide) would only blind its own gun.
                //   📐 ua-tank-ammo: hit at 12.8 s in the open desert, the M1 smoked and sat in its cloud — blind past 60 s, one round fired.
                //   A launch at it (above) still smokes: the screen breaks the gunner's guidance wherever the hull stands.
                threat = UnitMemory.Get<Fdp.Toolkit.Combat.FiringPositionMemory>(world, self).NoHide == 0
                         && EqsTacticsNodes.TopAim(world, self, default, out var aim) && EqsTacticsNodes.ThreatPosition(world, self, in aim, out at);
            }
            if (!threat || now - p.LastSalvo < (p.CooldownSeconds > 0f ? p.CooldownSeconds : SmokeCooldownSeconds)) return NodeStatus.Success;
            if (!Fdp.Toolkit.Combat.Countermeasures.MountOfKind(world, self, Fdp.Toolkit.Tkb.Domain.WeaponCategory.SmokeDischarger,
                                                                 out var mountEntity, out int mount, out _)) return NodeStatus.Success;
            ref var weapon = ref world.GetComponentRW<Fdp.Toolkit.Combat.Components.WeaponState>(mountEntity);
            if (weapon.Ammo <= 0) return NodeStatus.Success;
            if (Fdp.Toolkit.Combat.Executors.FireAtPointExecutor.ShootAtPoint(world, self, mount, ref weapon, at, cooldownSeconds: 0f, dt: 0f,
                    obeyRoe: true, obeyArc: false) == Fdp.Toolkit.Behavior.Diagnostics.ActionReason.Firing)
                p.LastSalvo = now;
            return NodeStatus.Success;
        }

        /// <summary>The running order's <c>Lights</c>, read once per task instance from its start record (0 = no order, or it carries none).</summary>
        private static void ReadOverride(ref CrewLightsParams p, Entity self, EntityRepository world)
        {
            if (!world.HasComponent<BehaviorState>(self)) { p.Override = 0; return; }
            ref readonly var bs = ref world.GetComponentRO<BehaviorState>(self);
            if (bs.InstanceId == p.ReadFor) return;
            p.ReadFor = bs.InstanceId;
            p.Override = 0;
            if (!Orders.CurrentOrders.IsOrderOrigin(bs.Origin) || !world.TryGetTable(typeof(BehaviorStartRecord), out _)
                || !world.HasManagedComponent<BehaviorStartRecord>(self)) return;
            var record = ((Fdp.ModuleHost.Abstractions.ISimulationView)world).GetManagedComponentRO<BehaviorStartRecord>(self);
            if (record == null || record.InstanceId != bs.InstanceId || record.JsonParams.IndexOf("\"Lights\"", System.StringComparison.Ordinal) < 0) return;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(record.JsonParams);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("Lights", out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String
                    && System.Enum.TryParse<LightDiscipline>(v.GetString(), ignoreCase: true, out var d))
                    p.Override = (byte)(1 + (byte)d);
            }
            catch (System.Text.Json.JsonException) { }
        }
    }
}
