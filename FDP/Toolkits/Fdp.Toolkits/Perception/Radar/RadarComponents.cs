using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Perception.Radar
{
    /// <summary>What a radar is doing while it radiates.</summary>
    public enum RadarMode : byte
    {
        /// <summary>Not radiating.</summary>
        Off = 0,
        /// <summary>Radiating, sweeping for targets.</summary>
        Search = 1,
        /// <summary>Radiating, holding one target (<see cref="RadarEmission.TrackedTarget"/>).</summary>
        Track = 2,
    }

    /// <summary>The class of an emitter, as a passive receiver tells it apart.</summary>
    public enum RadarEmitterClass : byte
    {
        Unknown = 0,
        /// <summary>A short-range air-defence search-and-track radar (the SAM vehicle, CE-4617).</summary>
        AirDefence = 1,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4617</c> (B-1; docs/designs/air-attack/DESIGN_Radar_SAM.md RS-10, R-4259 — 🔒 user: <i>"Radar radiation needs to be
    /// published/replicated so other entities and on machines can detect that"</i>) — THE one owner of "is this vehicle's radar radiating".
    /// Written ONLY by the emitting vehicle's owner (its brain, <see cref="RadarEmissions.Set"/>), added with its final value (D-31),
    /// recorded, and replicated as <c>dtRadarEmission</c>. Every consumer DERIVES from it on its own node: the radar sensor runs only while
    /// <see cref="IsOn"/> (<c>RadarPerception</c>), an aircraft's warning receiver, the enemy's direction finding, the spinning mast.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.RadarEmission)]
    [DataPolicy(DataPolicy.NoScenario)]   // runtime state: a loaded vehicle starts silent
    public struct RadarEmission
    {
        public RadarMode Mode;
        public RadarEmitterClass EmitterClass;
        /// <summary>The target held while <see cref="Mode"/> is <see cref="RadarMode.Track"/> (this node's entity; the wire carries its network id).</summary>
        public Entity TrackedTarget;
        /// <summary>How far the radar reaches (m) — what a warning receiver compares its distance with.</summary>
        public float RangeM;
        /// <summary>Sim time (s) the radar last switched ON — the direction finding's "continuous emission" clock.</summary>
        public double OnSince;

        public readonly bool IsOn => Mode != RadarMode.Off;
    }

    /// <summary>
    /// ⭐ <c>CE-4617</c> (B-1, RS-2, RV-12) — an entity's radar cross-section, stamped at spawn from its TKB <c>SignaturesDto.Radar</c> on
    /// every node that registers it (the solver has no TKB). Absent ⇒ the 10 m² reference (an aircraft always reflects something).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.RadarSignature)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct RadarSignature
    {
        /// <summary>Radar cross-section, m².</summary>
        public float RcsM2;
    }

    /// <summary>⭐ <c>CE-4617</c> — the ONE reader and the ONE writer of <see cref="RadarEmission"/>.</summary>
    public static class RadarEmissions
    {
        /// <summary>Is <paramref name="unit"/>'s radar radiating (false with no component, or a world that does not register it)?</summary>
        public static bool IsOn(ISimulationView view, Entity unit)
        {
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<RadarEmission>()) return false;
            return !unit.IsNull && view.IsAlive(unit) && view.HasComponent<RadarEmission>(unit) && view.GetComponentRO<RadarEmission>(unit).IsOn;
        }

        /// <summary>
        /// The owner sets its radar's state. ⭐ Writes only when something changed (the chunk version — and so the recorder and the
        /// egress — moves only on a change); the component is ADDED with its final value (D-31). <see cref="RadarEmission.OnSince"/> is
        /// stamped when the radar switches from off to on. Returns false when the world does not register the component.
        /// </summary>
        public static bool Set(EntityRepository repo, Entity unit, RadarMode mode, Entity tracked, RadarEmitterClass emitterClass, float rangeM, double now)
        {
            if (!repo.IsComponentTypeRegistered<RadarEmission>() || !repo.IsAlive(unit)) return false;
            if (mode != RadarMode.Track) tracked = Entity.Null;
            if (!repo.HasComponent<RadarEmission>(unit))
            {
                if (mode == RadarMode.Off) return true;   // never added just to say "off"
                repo.AddComponent(unit, new RadarEmission { Mode = mode, TrackedTarget = tracked, EmitterClass = emitterClass, RangeM = rangeM, OnSince = now });
                return true;
            }
            var was = repo.GetComponentRO<RadarEmission>(unit);
            if (was.Mode == mode && was.TrackedTarget == tracked && was.EmitterClass == emitterClass && was.RangeM == rangeM) return true;
            ref var e = ref repo.GetComponentRW<RadarEmission>(unit);
            if (!was.IsOn && mode != RadarMode.Off) e.OnSince = now;
            e.Mode = mode;
            e.TrackedTarget = tracked;
            e.EmitterClass = emitterClass;
            e.RangeM = rangeM;
            return true;
        }
    }
}
