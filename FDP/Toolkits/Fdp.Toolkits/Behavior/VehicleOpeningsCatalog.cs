using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// ⭐ <c>CE-4670</c> (VA-S2, VA-8; docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §4) — a type's hatches, doors and ramps, resolved ONCE per
    /// type: its class template (the body profile matched by DIS class, <see cref="Fdp.Toolkit.Physics.BodyParts.BodyZones.ClassOpeningsOf"/>) with the type's own
    /// sparse <c>Gen.Openings</c> merged over it by id (replace · <see cref="OpeningDto.Remove"/> drops · a new id adds), capped at
    /// <see cref="VehicleOpenings.Capacity"/>, in id order. The ORDER is the <see cref="VehicleOpenings"/> slot order — every node resolves the same list.
    /// </summary>
    public static class VehicleOpeningsCatalog
    {
        private static readonly ConditionalWeakTable<TkbTemplate, OpeningDto[]> s_cache = new();

        /// <summary>The openings of <paramref name="template"/>'s type; empty when it has none (a person, a type with no class template).</summary>
        public static OpeningDto[] Of(TkbTemplate? template)
            => template == null ? Array.Empty<OpeningDto>() : s_cache.GetValue(template, Resolve);

        private static OpeningDto[] Resolve(TkbTemplate t)
            => MergeById(Fdp.Toolkit.Physics.BodyParts.BodyZones.ClassOpeningsOf(t.DisType), t.GetDescriptor<VehicleOpeningsDto>()?.Openings,
                         static o => o.Id, static o => o.Remove, VehicleOpenings.Capacity);

        /// <summary>
        /// ⭐ THE class-template merge (HL-12's rule, ONE copy — openings and lights share it, CE-4670): the class's entries by id, then the type's — an entry with
        /// a class id replaces it, a <paramref name="remove"/>d one drops it, a new id adds; in id order, capped at <paramref name="cap"/>.
        /// </summary>
        internal static T[] MergeById<T>(IReadOnlyList<T>? cls, IReadOnlyList<T>? own, Func<T, byte> id, Func<T, bool> remove, int cap)
        {
            var byId = new SortedDictionary<byte, T>();
            if (cls != null) for (int i = 0; i < cls.Count; i++) byId[id(cls[i])] = cls[i];
            if (own != null)
                for (int i = 0; i < own.Count; i++)
                {
                    if (remove(own[i])) byId.Remove(id(own[i]));
                    else byId[id(own[i])] = own[i];
                }
            if (byId.Count == 0) return Array.Empty<T>();
            var list = new List<T>(byId.Values);
            if (list.Count > cap) list.RemoveRange(cap, list.Count - cap);
            return list.ToArray();
        }

        /// <summary>The bit mask of the openings in <paramref name="openings"/> that serve seat role <paramref name="role"/> (bit i = slot i).</summary>
        public static int ServingMask(OpeningDto[] openings, SeatRole role)
        {
            int mask = 0;
            for (int i = 0; i < openings.Length; i++)
            {
                var roles = openings[i].Roles;
                for (int r = 0; r < roles.Count; r++)
                    if (roles[r] == role) { mask |= 1 << i; break; }
            }
            return mask;
        }
    }

    /// <summary>
    /// ⭐ <c>CE-4670</c> (VA-S3, VA-8) — a type's light GROUPS, resolved once per type exactly as <see cref="VehicleOpeningsCatalog"/> resolves openings: the class
    /// template (<see cref="Fdp.Toolkit.Physics.BodyParts.BodyZones.ClassLightsOf"/>) with the type's sparse <c>Gen.Lights</c> merged by id (the shared
    /// <see cref="VehicleOpeningsCatalog.MergeById"/>), in id order = the bit order of <see cref="VehicleLights.OnMask"/>, ≤ 32.
    /// </summary>
    public static class VehicleLightsCatalog
    {
        private static readonly ConditionalWeakTable<TkbTemplate, LightDto[]> s_cache = new();

        /// <summary>The light groups of <paramref name="template"/>'s type; empty when it has none.</summary>
        public static LightDto[] Of(TkbTemplate? template)
            => template == null ? Array.Empty<LightDto>() : s_cache.GetValue(template, static t => VehicleOpeningsCatalog.MergeById(
                   Fdp.Toolkit.Physics.BodyParts.BodyZones.ClassLightsOf(t.DisType), t.GetDescriptor<VehicleLightsDto>()?.Lights,
                   static l => l.Id, static l => l.Remove, 32));
    }
}
