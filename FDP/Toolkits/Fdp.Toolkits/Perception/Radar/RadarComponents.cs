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
        public SimInstant OnSince;

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

    /// <summary>⭐ <c>CE-4617</c> (B-4) — what an aircraft's radar warning receiver says.</summary>
    public enum RadarWarningLevel : byte
    {
        None = 0,
        /// <summary>A radar of another side radiates with this aircraft in its range and line of sight.</summary>
        Searched = 1,
        /// <summary>A radar of another side TRACKS this aircraft (its <see cref="RadarEmission.TrackedTarget"/>).</summary>
        Tracked = 2,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4617</c> (B-4, RS-6 / RS-10 / RV-8; docs/designs/air-attack/DESIGN_Radar_SAM.md) — an aircraft's RADAR WARNING, written by its
    /// OWN node (<c>RadarWarningReceiverSystem</c>, derived from every emitter's replicated <see cref="RadarEmission"/>) on a change only; read
    /// by its attack behaviour (the escape). Not replicated: every node that needs it derives it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.RadarWarning)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct RadarWarning
    {
        public RadarWarningLevel Level;
        /// <summary>The emitter (the tracking one, else the nearest searching one).</summary>
        public Entity Emitter;
        /// <summary>Its antenna, and the bearing to it (degrees, 0 = north, clockwise).</summary>
        public System.Numerics.Vector3 From;
        public float BearingDeg;
        /// <summary>Sim time the level or the emitter last changed.</summary>
        public SimInstant Since;
    }

    /// <summary>⭐ <c>CE-4617</c> — the ONE reader and the ONE writer of <see cref="RadarEmission"/>.</summary>
    public static class RadarEmissions
    {
        /// <summary>The radar cross-section of <paramref name="target"/> (its <see cref="RadarSignature"/>; none ⇒ the 10 m² reference — L-2).</summary>
        public static float CrossSectionOf(ISimulationView view, Entity target)
        {
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<RadarSignature>()) return Sensors.RadarPerception.ReferenceRcsM2;
            return view.HasComponent<RadarSignature>(target) ? view.GetComponentRO<RadarSignature>(target).RcsM2 : Sensors.RadarPerception.ReferenceRcsM2;
        }

        /// <summary>The stated range (m) of the radar <paramref name="unit"/>'s TKB lists (its <c>SensorCapabilitiesDto.Sensors</c>), 0 = none —
        /// whether it carries one at all (the brain's node may hold no sensor child: perception solves elsewhere, RV-4).</summary>
        public static float RangeOf(EntityRepository repo, Entity unit)
        {
            if (!Fdp.Toolkit.Combat.CombatTkb.TryGetTemplate(repo, unit, out var t)) return 0f;
            var list = t.GetDescriptor<Fdp.Toolkit.Tkb.Domain.SensorCapabilitiesDto>()?.Sensors;
            if (list == null) return 0f;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Kind == Components.SensorModality.Radar && list[i].Radar is { } radar) return radar.Range;
            return 0f;
        }

        /// <summary>Is <paramref name="unit"/>'s radar radiating (false with no component, or a world that does not register it)?
        /// ⭐ A vehicle out of the fight (destroyed) radiates nothing, whatever its last state says (<see cref="Radiates"/>).</summary>
        public static bool IsOn(ISimulationView view, Entity unit)
        {
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<RadarEmission>()) return false;
            return !unit.IsNull && view.IsAlive(unit) && view.HasComponent<RadarEmission>(unit) && Radiates(view, unit, view.GetComponentRO<RadarEmission>(unit));
        }

        /// <summary>⭐ <c>CE-4617</c> (found by <c>sam-site</c>: a SAM destroyed while tracking kept "radiating") — THE one rule every consumer reads
        /// (the radar sensor, the warning receiver, the direction finding): on, and its vehicle still in the fight.</summary>
        public static bool Radiates(ISimulationView view, Entity unit, in RadarEmission e)
            => e.IsOn && Fdp.Toolkit.Combat.Injury.IsInTheFight(view, unit);

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
                repo.AddComponent(unit, new RadarEmission { Mode = mode, TrackedTarget = tracked, EmitterClass = emitterClass, RangeM = rangeM, OnSince = (SimInstant)now });
                return true;
            }
            var was = repo.GetComponentRO<RadarEmission>(unit);
            if (was.Mode == mode && was.TrackedTarget == tracked && was.EmitterClass == emitterClass && was.RangeM == rangeM) return true;
            ref var e = ref repo.GetComponentRW<RadarEmission>(unit);
            if (!was.IsOn && mode != RadarMode.Off) e.OnSince = (SimInstant)now;
            e.Mode = mode;
            e.TrackedTarget = tracked;
            e.EmitterClass = emitterClass;
            e.RangeM = rangeM;
            return true;
        }
    }
}
