using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Perception.Signatures
{
    /// <summary>
    /// ⭐ <c>CE-4658</c> (thermal drivers TD-S2; docs/designs/thermal/DESIGN_Thermal_Drivers.md §4e) — the thermal type data a type gets when it authors none, from its
    /// body (<see cref="LocomotionClass"/>). 📐 Measured `2026-11-18`: NO shipped type set <c>SignaturesDto.Thermal</c>, so every unit was cold to a thermal sight
    /// (DESIGN_Thermal_And_Acoustic_Sensing §8.7). ⭐ By construction, not per type: every person, animal, vehicle and aircraft is warm the same way, and a type that
    /// authors its own keeps it. Starter values (a person's base above a cold hull's; an engine warms in ~100 s and cools over ~8 min; a body in ~20 s / ~50 s).
    /// </summary>
    public static class ThermalDefaults
    {
        private static readonly ThermalSignatureDto Body = new()
        {
            BaseSignature = 0.5f, IdleHeat = 0.2f, RunningHeat = 0.7f, ReferenceSpeed = 3f, RunningHeatPerSecond = 0.05f, CooldownPerSecond = 0.02f,
        };

        private static readonly ThermalSignatureDto Animal = Body with { BaseSignature = 0.45f, IdleHeat = 0.15f, RunningHeat = 0.6f, ReferenceSpeed = 4f };

        private static readonly ThermalSignatureDto Engine = new()
        {
            HasPowerPlant = true, BaseSignature = 0.2f, IdleHeat = 0.4f, RunningHeat = 0.8f,
            RunningHeatPerSecond = 0.01f, CooldownPerSecond = 0.002f,
        };

        private static readonly ThermalSignatureDto Wheeled = Engine with
        {
            PowerClass = ThermalPowerClass.Car, HotEnd = HeatMarkEnd.Front, IdleHeat = 0.35f, RunningHeat = 0.75f, ReferenceSpeed = 10f, FireSeconds = 600f,
        };

        private static readonly ThermalSignatureDto Tracked = Engine with
        {
            PowerClass = ThermalPowerClass.Tank, HotEnd = HeatMarkEnd.Rear, ReferenceSpeed = 8f, RunningHeat = 0.85f, FireSeconds = 1800f,
        };

        private static readonly ThermalSignatureDto Boat = Engine with { PowerClass = ThermalPowerClass.Truck, HotEnd = HeatMarkEnd.Rear, ReferenceSpeed = 6f, FireSeconds = 1200f };

        private static readonly ThermalSignatureDto Aircraft = Engine with
        {
            PowerClass = ThermalPowerClass.Aircraft, HotEnd = HeatMarkEnd.Rear, BaseSignature = 0.25f, IdleHeat = 0.5f, RunningHeat = 0.9f,
            ReferenceSpeed = 60f, FireSeconds = 900f,
        };

        /// <summary>
        /// ⭐ <c>CE-4660</c> — the thermal data <paramref name="t"/> has: its own (<c>SignaturesDto.Thermal</c>), else its body's default — the body read from its
        /// <see cref="LocomotionDto"/>, else its <c>VehicleParametersDto.VehicleClass</c> (📐 the urban catalogue's hand-built types — the civilian car, the
        /// APC — carry no <c>LocomotionDto</c>, so CE-4658's stamp left them cold and a destroyed car never burned). ONE rule for the stamp and the fire.
        /// </summary>
        public static ThermalSignatureDto? Of(Fdp.Interfaces.TkbTemplate? t)
        {
            if (t == null) return null;
            if (t.GetDescriptor<SignaturesDto>()?.Thermal is { } own) return own;
            var cls = t.GetDescriptor<LocomotionDto>()?.Class ?? LocomotionClass.Unknown;
            // a stated vehicle class; else the kinematics' own default (VehicleKinematicsTkbTranslator: null ⇒ PersonalCar) for a hull that MOVES —
            // 📐 the civilian and SUMO traffic cars state none; a zero-speed prop (the archery target) is no vehicle and stays cold
            if (cls == LocomotionClass.Unknown && t.GetDescriptor<VehicleParametersDto>() is { } vp && (vp.VehicleClass != null || vp.MaxSpeedFwd > 0f))
                cls = Fdp.Toolkit.Motion.Locomotions.FromVehicleClass(vp.VehicleClass ?? global::CarKinem.Core.VehicleClass.PersonalCar);
            return For(cls);
        }

        /// <summary>The default thermal data of a body of class <paramref name="c"/>; null for <see cref="LocomotionClass.Unknown"/> (an area, a marker, an obstacle).
        /// ⭐ The same instance per class — never mutate it (a record: every field is init-only).</summary>
        public static ThermalSignatureDto? For(LocomotionClass c) => c switch
        {
            LocomotionClass.Pedestrian or LocomotionClass.Rope or LocomotionClass.Parachute => Body,
            LocomotionClass.Quadruped or LocomotionClass.Bird => Animal,
            LocomotionClass.Wheeled => Wheeled,
            LocomotionClass.Tracked or LocomotionClass.Amphibious => Tracked,
            LocomotionClass.Surface => Boat,
            LocomotionClass.Rotorcraft or LocomotionClass.FixedWing => Aircraft,
            _ => null,
        };
    }
}
