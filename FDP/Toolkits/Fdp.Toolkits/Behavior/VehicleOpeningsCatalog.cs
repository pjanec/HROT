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
        {
            var byId = new SortedDictionary<byte, OpeningDto>();
            if (Fdp.Toolkit.Physics.BodyParts.BodyZones.ClassOpeningsOf(t.DisType) is { } cls)
                foreach (var o in cls) byId[o.Id] = o;
            if (t.GetDescriptor<VehicleOpeningsDto>() is { } own)
                foreach (var o in own.Openings)
                {
                    if (o.Remove) byId.Remove(o.Id);
                    else byId[o.Id] = o;
                }
            if (byId.Count == 0) return Array.Empty<OpeningDto>();
            var list = new List<OpeningDto>(byId.Values);
            if (list.Count > VehicleOpenings.Capacity) list.RemoveRange(VehicleOpenings.Capacity, list.Count - VehicleOpenings.Capacity);
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
}
