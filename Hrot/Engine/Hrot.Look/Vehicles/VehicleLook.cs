using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Signatures;
using Fdp.Toolkit.Physics.BodyParts;
using Fdp.Toolkit.Replication.Components;
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

        /// <summary>
        /// ⭐ CE-4670 (§6a head out, §7d D-3 / D-5) — is a crewman's head out of opening <paramref name="id"/> (a hatch marked <see cref="OpeningSlot.CrewExposed"/>
        /// by the boarding arbiter)? By the opening's TKB id, like <see cref="OpeningProgress"/>; false when there is no such opening.
        /// </summary>
        public static bool CrewExposed(EntityRepository world, Entity vehicle, byte id)
        {
            if (!world.IsComponentTypeRegistered<VehicleOpenings>() || !world.HasComponent<VehicleOpenings>(vehicle)) return false;
            var openings = VehicleOpeningsCatalog.Of(TypeOf(world, vehicle));
            ref readonly var state = ref world.GetComponentRO<VehicleOpenings>(vehicle);
            for (int i = 0; i < openings.Length && i < state.Count; i++)
                if (openings[i].Id == id) return state.Slots[i].CrewExposed;
            return false;
        }

        /// <summary>
        /// VA-4 — opening <paramref name="id"/>'s animation progress at <paramref name="now"/> (0 closed … 1 open), for the opening with that TKB id (a renderer
        /// maps its model by id / socket, never by slot). Delegates to <see cref="VehicleOpenings.Progress"/> — THE one progress rule — with the type's open seconds.
        /// 0 when the vehicle has no openings, or no opening with that id.
        /// </summary>
        public static float OpeningProgress(EntityRepository world, Entity vehicle, byte id, double now)
        {
            if (!world.IsComponentTypeRegistered<VehicleOpenings>() || !world.HasComponent<VehicleOpenings>(vehicle)) return 0f;
            var openings = VehicleOpeningsCatalog.Of(TypeOf(world, vehicle));
            ref readonly var state = ref world.GetComponentRO<VehicleOpenings>(vehicle);
            for (int i = 0; i < openings.Length; i++)
                if (openings[i].Id == id) return state.ProgressAt(i, openings[i].OpenSeconds, now);
            return 0f;
        }

        /// <summary>
        /// VA-3 / VA-2 — the EFFECTIVE light mask (bit i = the type's light group i, <see cref="VehicleLightsCatalog"/> order): the stored
        /// <see cref="VehicleLights.OnMask"/>, plus every <see cref="LightKind.Brake"/> group while <paramref name="brakes"/> says the vehicle is braking — only under
        /// <see cref="LightDiscipline.Normal"/> (a blacked-out convoy shows no stop light). ⛔ Brake lights are never stored: they flicker at driving rate, so a
        /// reader derives them from the speed it sees (<see cref="BrakeLights"/>, one per renderer). <paramref name="brakes"/> null = the stored mask only.
        /// </summary>
        public static uint Lights(EntityRepository world, Entity vehicle, double now, BrakeLights? brakes)
        {
            if (!world.IsComponentTypeRegistered<VehicleLights>() || !world.HasComponent<VehicleLights>(vehicle)) return 0u;
            ref readonly var lights = ref world.GetComponentRO<VehicleLights>(vehicle);
            uint mask = lights.OnMask;
            if (brakes == null || lights.Discipline != LightDiscipline.Normal || !brakes.IsBraking(world, vehicle, now)) return mask;
            var groups = VehicleLightsCatalog.Of(TypeOf(world, vehicle));
            for (int i = 0; i < groups.Length && i < 32; i++)
                if (groups[i].Kind == LightKind.Brake) mask |= 1u << i;
            return mask;
        }

        /// <summary>The vehicle's TKB type (the lookup <c>VehicleOpeningsSystem</c> / <c>LightRuleSystem</c> make), or null.</summary>
        internal static TkbTemplate? TypeOf(EntityRepository world, Entity vehicle)
            => world.IsComponentTypeRegistered<TkbIdentity>() && world.HasComponent<TkbIdentity>(vehicle)
               && world.HasSingletonManaged<ITkbDatabase>() && world.GetSingletonManaged<ITkbDatabase>() is { } db
               && db.TryGetByType(world.GetComponentRO<TkbIdentity>(vehicle).TkbType, out var t) ? t : null;

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
