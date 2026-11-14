using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Perception.Signatures
{
    /// <summary>
    /// ⭐ <c>CE-3061</c> — an entity's heat (docs/DESIGN_Thermal_And_Acoustic_Sensing.md §5.2, R-205). Stamped from the TKB
    /// <c>ThermalSignatureDto</c>; <see cref="Heat"/> is written by <see cref="ThermalHeatSystem"/> on the node that moves and
    /// fires the entity and read by the thermal sensor's signature filter. Not saved: heat is derived, a loaded entity starts cold.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.ThermalState)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct ThermalState
    {
        public float BaseSignature;
        public float RunningHeatPerSecond;
        public float FiringHeatPerShot;
        public float CooldownPerSecond;
        public float ReferenceSpeed;

        /// <summary>Accumulated heat, 0..1.</summary>
        public float Heat;

        // The position last tick — speed is measured as distance moved, so every kinematics path counts.
        public float LastX, LastY, LastZ;
        [MarshalAs(UnmanagedType.I1)] public bool HasLast;

        /// <summary>⭐ <c>CE-1358</c> (DESIGN_Weapon_Layers §5k) — the hottest BARREL of this unit, 0..1 (<c>WeaponState.Heat</c> of its own
        /// mounts and the station weapons it carries), written by <see cref="ThermalHeatSystem"/>. Appended.</summary>
        public float BarrelHeat;

        /// <summary>What a thermal sensor sees: <c>Base + max(Heat, BarrelHeat) × (1 − Base)</c>, 0..1 — the body and the gun are two
        /// sources, the sensor sees the hotter.</summary>
        public readonly float Signature => BaseSignature + System.MathF.Max(Heat, BarrelHeat) * (1f - BaseSignature);
    }
}
