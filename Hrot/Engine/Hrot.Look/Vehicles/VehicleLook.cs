using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Physics.BodyParts;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Look.Vehicles
{
    /// <summary>⭐ <c>CE-4670</c> (VA-6, §3a) — what a renderer draws for one part of a vehicle.</summary>
    public enum PartLook : byte
    {
        /// <summary>No damage (level 0), or the vehicle has no parts.</summary>
        Intact = 0,
        /// <summary>Hit, under its zone's threshold.</summary>
        Damaged = 1,
        /// <summary>At or over its zone's threshold (a destroyed track, a dead engine).</summary>
        Destroyed = 2,
        /// <summary>A burning fire spot covers it (only the overload that is given the part's centre can say so).</summary>
        Burning = 3,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-4670</c> (vehicle appearance VA-2, VA-6, VA-7, VA-10 — VA-S1; docs/designs/vehicle-appearance/DESIGN_Vehicle_Appearance_State.md §3a, §6c) — THE ONE
    /// READER of how a vehicle's damage LOOKS: our 3D map and an external renderer's gateway call it, so they cannot disagree. Pure, over replicated state
    /// only (<see cref="PartDamage"/>, <see cref="Health"/>, <see cref="FireState"/>) and the type's resolved body (zones, stowage, smoulder seconds) — every node
    /// and a late joiner see the same. ⛔ Nothing here is stored. 0 B per call (index loops; the body is cached per type).
    /// </summary>
    public static class VehicleLook
    {
        /// <summary>The look of <paramref name="part"/> from its damage: <see cref="PartLook.Intact"/> · <see cref="PartLook.Damaged"/> ·
        /// <see cref="PartLook.Destroyed"/> (its level against its zone's threshold, HL-12 — the same threshold that makes the mobility / firepower kill).</summary>
        public static PartLook PartState(EntityRepository world, Entity vehicle, byte part)
        {
            if (!world.IsComponentTypeRegistered<PartDamage>() || !world.HasComponent<PartDamage>(vehicle)) return PartLook.Intact;
            byte level = world.GetComponentRO<PartDamage>(vehicle).LevelOf(part);
            if (level == 0) return PartLook.Intact;
            return level >= DestroyedAt(BodyZones.Of(world, vehicle), part) ? PartLook.Destroyed : PartLook.Damaged;
        }

        /// <summary>As <see cref="PartState(EntityRepository, Entity, byte)"/>, and <see cref="PartLook.Burning"/> when a fire spot burning at
        /// <paramref name="now"/> covers <paramref name="partCentre"/> — the part's centre in the VEHICLE's frame (the renderer knows its model; the
        /// simulation keeps no part geometry it could send).</summary>
        public static PartLook PartState(EntityRepository world, Entity vehicle, byte part, Vector3 partCentre, double now)
        {
            if (world.IsComponentTypeRegistered<FireState>() && world.HasComponent<FireState>(vehicle))
            {
                ref readonly var fire = ref world.GetComponentRO<FireState>(vehicle);
                for (int i = 0; i < fire.Count && i < FireState.Capacity; i++)
                {
                    var spot = fire.Get(i);
                    if (!spot.IsBurning(now)) continue;
                    var d = new Vector3(spot.X, spot.Y, spot.Z) - partCentre;
                    if (d.LengthSquared() <= spot.Radius * spot.Radius) return PartLook.Burning;
                }
            }
            return PartState(world, vehicle, part);
        }

        /// <summary>How the vehicle was destroyed (VA-7) — <see cref="DestructionKind.Catastrophic"/> throws the turret; None while it lives.</summary>
        public static DestructionKind Destruction(EntityRepository world, Entity vehicle)
            => world.IsComponentTypeRegistered<Health>() && world.HasComponent<Health>(vehicle) && world.GetComponentRO<Health>(vehicle).Current <= 0f
               ? world.GetComponentRO<Health>(vehicle).Destruction : DestructionKind.None;

        /// <summary>Is the turret thrown (a <see cref="DestructionKind.Catastrophic"/> kill)?</summary>
        public static bool TurretThrown(EntityRepository world, Entity vehicle) => Destruction(world, vehicle) == DestructionKind.Catastrophic;

        /// <summary>Does it burn at <paramref name="now"/> (any fire spot)?</summary>
        public static bool Burning(EntityRepository world, Entity vehicle, double now)
            => world.IsComponentTypeRegistered<FireState>() && world.HasComponent<FireState>(vehicle)
               && world.GetComponentRO<FireState>(vehicle).IsBurning(now);

        /// <summary>
        /// VA-10 — does the WRECK smoke at <paramref name="now"/>: dead, not burning, and within the type's smoulder seconds after its last spot went out
        /// (or, never lit, after its time of death — <see cref="Health.StateSince"/>).
        /// </summary>
        public static bool Smoking(EntityRepository world, Entity vehicle, double now)
        {
            if (Destruction(world, vehicle) == DestructionKind.None) return false;
            if (BodyZones.Of(world, vehicle) is not { SmoulderSeconds: > 0f } body) return false;
            double outAt = world.GetComponentRO<Health>(vehicle).StateSince;
            if (world.IsComponentTypeRegistered<FireState>() && world.HasComponent<FireState>(vehicle))
            {
                ref readonly var fire = ref world.GetComponentRO<FireState>(vehicle);
                for (int i = 0; i < fire.Count && i < FireState.Capacity; i++)
                {
                    var spot = fire.Get(i);
                    if (spot.Seconds <= 0f) continue;
                    if (spot.IsBurning(now)) return false;   // flames, not smoke
                    double end = spot.Since.Seconds + spot.Seconds;
                    if (end <= now && end > outAt) outAt = end;
                }
            }
            return now >= outAt && now < outAt + body.SmoulderSeconds;
        }

        /// <summary>The level at which <paramref name="part"/> counts as destroyed: its zone's threshold, 255 when it is in no zone or the type has no body.</summary>
        private static int DestroyedAt(ResolvedBody? body, byte part)
        {
            if (body == null) return 255;
            var zones = body.Zones;
            for (int i = 0; i < zones.Count; i++)
                if (zones[i].Has(part)) return zones[i].WoundedAt;
            return 255;
        }
    }
}
