using System;
using System.Text.Json.Nodes;
using Fbt;
using Fbt.Kernel;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication;
using Fdp.Toolkit.ScenarioRules.Actions;

namespace Hrot.AI.Behaviors.Brains.Objectives.Verbs
{
    /// <summary>⭐ <c>CE-4670</c> (VA-4) — <c>HeadOut</c>'s params: the VEHICLE, the seat role whose man heads out (<c>Commander</c> by default — a preset crewman
    /// has no authored id to name him by), out (true) or back in.</summary>
    public struct HeadOutParams : IActionArgsCheck
    {
        public EntityLink Target;
        public FixedString32 Role;
        public bool Out;
        public HeadOutParams() { Target = default; Role = new FixedString32("Commander"); Out = true; }

        public readonly string? Check(JsonObject args)
        {
            foreach (var (k, v) in args)
                if (string.Equals(k, "role", StringComparison.OrdinalIgnoreCase) && v is JsonValue jv && jv.TryGetValue<string>(out var r)
                    && !Enum.TryParse<SeatRole>(r, ignoreCase: true, out _))
                    return $"role '{r}' is not one of {string.Join(", ", Enum.GetNames<SeatRole>())}";
            return null;
        }
    }

    /// <summary>⭐ <c>CE-4670</c> (VA-3) — <c>SetVehicleLights</c>'s params: the vehicle and the discipline (<c>Normal</c> · <c>Convoy</c> · <c>Blackout</c> · <c>Off</c>).</summary>
    public struct SetVehicleLightsParams : IActionArgsCheck
    {
        public EntityLink Target;
        public FixedString32 Discipline;
        public SetVehicleLightsParams() { Target = default; Discipline = new FixedString32("Normal"); }

        public readonly string? Check(JsonObject args)
        {
            foreach (var (k, v) in args)
                if (string.Equals(k, "discipline", StringComparison.OrdinalIgnoreCase) && v is JsonValue jv && jv.TryGetValue<string>(out var d)
                    && !Enum.TryParse<LightDiscipline>(d, ignoreCase: true, out _))
                    return $"discipline '{d}' is not one of {string.Join(", ", Enum.GetNames<LightDiscipline>())}";
            return null;
        }
    }

    public static partial class ScenarioVerbs
    {
        // ═══════════════════════════════════════ HeadOut / SetVehicleLights (CE-4670, VA-3 / VA-4) ═══════════════════════════════════════

        /// <summary>
        /// ⭐ <c>CE-4670</c> (📄 docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §6a) — the man in the vehicle's first occupied seat of
        /// <c>role</c> puts his head out of his hatch (or back in): ASKS the boarding arbiter (<see cref="VehicleRequests.HeadOut"/> — the interaction transport
        /// carries it there). The seat is read in the serial follow-up (<c>RuleAfterPhase</c>), never in the phase. Instant: Success once asked. Failure: the
        /// target does not resolve or the role is unknown; nobody in that role ⇒ asked of nobody (logged by nothing — the hatch simply stays shut).
        /// </summary>
        [SharedAiAction]
        public static NodeStatus HeadOut(ref HeadOutParams p, ref VerbNoState s, Entity self, EntityRepository world)
        {
            var vehicle = p.Target.Ref.Resolve(world);
            if (vehicle.IsNull || !world.IsAlive(vehicle)) return Fail(world, self, "target not found");
            if (!Enum.TryParse<SeatRole>(p.Role.ToString(), ignoreCase: true, out var role)) return Fail(world, self, $"role '{p.Role}' unknown");
            bool @out = p.Out;
            RuleAfterPhase.Run(world, self, w =>
            {
                if (!w.IsAlive(vehicle) || !w.IsComponentTypeRegistered<PassengerBuffer>() || !w.HasComponent<PassengerBuffer>(vehicle)) return;
                bool roles = w.IsComponentTypeRegistered<CarrierSeats>() && w.HasComponent<CarrierSeats>(vehicle);
                ref readonly var list = ref w.GetComponentRO<PassengerBuffer>(vehicle);
                for (int seat = 0; seat < PassengerBuffer.Capacity; seat++)
                {
                    var man = list.Passengers[seat];
                    if (man.IsNull || (roles ? w.GetComponentRO<CarrierSeats>(vehicle).Roles[seat] : SeatRole.Passenger) != role) continue;
                    VehicleRequests.HeadOut(w, vehicle, man, @out);
                    return;
                }
            });
            return NodeStatus.Success;
        }

        /// <summary>
        /// ⭐ <c>CE-4670</c> (§6b) — sets a vehicle's light discipline: ASKS its lights owner (<see cref="VehicleRequests.SwitchLights"/>), which sets it and lights what
        /// the rule allows. Instant: Success once asked. Failure: the target does not resolve or the discipline is unknown.
        /// </summary>
        [SharedAiAction]
        public static NodeStatus SetVehicleLights(ref SetVehicleLightsParams p, ref VerbNoState s, Entity self, EntityRepository world)
        {
            var vehicle = p.Target.Ref.Resolve(world);
            if (vehicle.IsNull || !world.IsAlive(vehicle)) return Fail(world, self, "target not found");
            if (!Enum.TryParse<LightDiscipline>(p.Discipline.ToString(), ignoreCase: true, out var discipline)) return Fail(world, self, $"discipline '{p.Discipline}' unknown");
            RuleAfterPhase.Run(world, self, w => VehicleRequests.SwitchLights(w, vehicle, discipline, self));
            return NodeStatus.Success;
        }
    }
}
