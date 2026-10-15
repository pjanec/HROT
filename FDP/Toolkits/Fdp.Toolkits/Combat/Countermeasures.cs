using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ <c>CE-2188</c> / <c>CE-1111</c> (<c>docs/designs/brain-split/BS-1-DESIGN.md</c> §5.4c) — what a munition IS when it is an
    /// EXPENDABLE (DIS kind 8: flare 8.2.2, chaff 8.2.1). The ONE check every reader of a fire makes: the effects draw the decoy instead of
    /// a flash, sound and heat skip it, the SimHost spawns a decoy instead of a round. Read from the munition's TKB type (its DIS type and
    /// its <see cref="ExpendableDto"/>) — the SimHost and every viewer read the same numbers.
    /// </summary>
    public static class Munitions
    {
        /// <summary>DIS entity kind 8 — an expendable (a flare, chaff, a decoy).</summary>
        public const byte ExpendableKind = 8;
        /// <summary>DIS 8.2.1 — chaff.</summary>
        public const byte ChaffCategory = 1;
        /// <summary>DIS 8.2.2 — a flare.</summary>
        public const byte FlareCategory = 2;

        /// <summary>Is the munition type <paramref name="ammoGuid"/> an expendable (DIS kind 8)? False for 0, an unknown type or no TKB.</summary>
        public static bool IsExpendable(ITkbDatabase? tkb, long ammoGuid)
            => ammoGuid != 0 && tkb != null && tkb.TryGetByType(ammoGuid, out var t) && t != null && t.DisType.Kind == ExpendableKind;

        /// <inheritdoc cref="IsExpendable(ITkbDatabase?, long)"/>
        public static bool IsExpendable(EntityRepository world, long ammoGuid) => IsExpendable(DbOf(world), ammoGuid);

        /// <summary>The decoy an expendable throws — a flare (8.2.2) or chaff (8.2.1) — else null (not an expendable, or another kind).</summary>
        public static DecoyKind? DecoyOf(ITkbDatabase? tkb, long ammoGuid)
        {
            if (ammoGuid == 0 || tkb == null || !tkb.TryGetByType(ammoGuid, out var t) || t == null || t.DisType.Kind != ExpendableKind) return null;
            return t.DisType.Category switch { FlareCategory => DecoyKind.Flare, ChaffCategory => DecoyKind.Chaff, _ => null };
        }

        /// <summary>The expendable's decoy numbers (its <see cref="ExpendableDto"/>; the DTO's defaults when it declares none); null when
        /// <paramref name="ammoGuid"/> is not an expendable.</summary>
        public static ExpendableDto? ExpendableOf(ITkbDatabase? tkb, long ammoGuid)
        {
            if (ammoGuid == 0 || tkb == null || !tkb.TryGetByType(ammoGuid, out var t) || t == null || t.DisType.Kind != ExpendableKind) return null;
            return t.GetDescriptor<ExpendableDto>() ?? new ExpendableDto();
        }

        internal static ITkbDatabase? DbOf(EntityRepository? world)
            => world != null && world.HasSingletonManaged<ITkbDatabase>() ? world.GetSingletonManaged<ITkbDatabase>() : null;
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2188</c> (BS-1-DESIGN §5.4c, F6) — THE path of a decoy: where it is <c>t</c> seconds after it left the aircraft. It leaves
    /// at the aircraft's velocity, slows to the air (time constant <see cref="SlowSeconds"/>) and sinks. ⭐ One function of the fire event:
    /// the SimHost's <see cref="Decoy"/> (what a seeker chases) and every viewer's drawn flare call it with the same numbers — no positions
    /// are streamed.
    /// </summary>
    public static class DecoyFlight
    {
        /// <summary>How fast a decoy loses the aircraft's speed: after this long it keeps e⁻¹ of it (s).</summary>
        public const float SlowSeconds = 1.5f;

        /// <summary>The decoy's position <paramref name="t"/> seconds after release from <paramref name="origin"/> at
        /// <paramref name="velocity"/> (the aircraft's), sinking at <paramref name="sinkMps"/>.</summary>
        public static Vector3 At(Vector3 origin, Vector3 velocity, float sinkMps, float t)
        {
            if (t <= 0f) return origin;
            float carried = SlowSeconds * (1f - MathF.Exp(-t / SlowSeconds));
            return origin + velocity * carried + new Vector3(0f, 0f, -sinkMps * t);
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2188</c> / <c>CE-1111</c> (BS-1-DESIGN §5.4c; 🔒 user: <i>"Number of countermeasures left same as number of weapon rounds
    /// left"</i>, <i>"Chaff launcher can be weapon type and slot could be normal one"</i>) — the BRAIN's one way to release a flare or chaff:
    /// the aircraft's dispenser is an ordinary weapon mount (<see cref="WeaponCategory.Countermeasure"/>) whose loadout holds the
    /// expendables. One release = one round spent (<see cref="Magazine.Spend"/> + the loadout's count) = one ordinary
    /// <see cref="WeaponFireIntent"/> (the dispenser mount, the expendable's <c>AmmoGuid</c>, at the aircraft's own position) — the SimHost's
    /// <c>CountermeasureSystem</c> throws the decoy. A salvo is the caller's count of releases; the interval is the mount's rate of fire.
    /// <para>⛔ A dispenser is NOT a weapon: <see cref="IsDispenser"/> / <see cref="DispenserOnly"/> are what the "armed" readers ask.</para>
    /// </summary>
    public static class Countermeasures
    {
        /// <summary>The release interval when the dispenser mount declares no rate of fire (s).</summary>
        public const float DefaultIntervalSeconds = 0.5f;

        /// <summary>
        /// Releases one <paramref name="kind"/> from <paramref name="unit"/>'s dispenser: true when it left this frame (a round of that kind
        /// spent, the interval started, the fire raised). False — nothing changes — with no dispenser, none of that kind left, or the
        /// interval still running.
        /// </summary>
        public static bool Release(EntityRepository world, Entity unit, DecoyKind kind)
        {
            if (!world.IsAlive(unit) || !world.HasComponent<SimTransform>(unit)) return false;
            if (!Dispenser(world, unit, out var mountEntity, out int mountIndex, out var mount)) return false;
            if (!world.IsComponentTypeRegistered<AmmoLoadout>() || !world.HasComponent<AmmoLoadout>(mountEntity)) return false;
            ref var weapon = ref world.GetComponentRW<WeaponState>(mountEntity);
            if (!Magazine.Ready(in weapon) || weapon.CooldownSecondsRemaining > 0f) return false;
            ref var load = ref world.GetComponentRW<AmmoLoadout>(mountEntity);
            var tkb = Munitions.DbOf(world);
            int slot = -1;
            for (int i = 0; i < load.Count && slot < 0; i++)
                if (load.RoundsAt(i) > 0 && Munitions.DecoyOf(tkb, load.AmmoAt(i)) == kind) slot = i;
            if (slot < 0) return false;

            world.Bus.Publish(new WeaponFireIntent
            {
                Shooter     = unit,
                Target      = Entity.Null,
                WeaponIndex = mountIndex,
                AtPoint     = true,                                              // ⭐ released where the aircraft is
                TargetPoint = world.GetComponentRO<SimTransform>(unit).Position,
                AmmoGuid    = load.AmmoAt(slot),
                RaisedAt    = HitModel.Now(world),
            });
            load.SetRounds(slot, load.RoundsAt(slot) - 1);
            Magazine.Spend(ref weapon);
            weapon.CooldownSecondsRemaining = mount.RateOfFire > 0f ? 60f / mount.RateOfFire : DefaultIntervalSeconds;
            return true;
        }

        /// <summary>The flares (or chaff) <paramref name="unit"/>'s dispenser still holds; 0 with no dispenser.</summary>
        public static int Left(EntityRepository world, Entity unit, DecoyKind kind)
        {
            if (!Dispenser(world, unit, out var mountEntity, out _, out _)
                || !world.IsComponentTypeRegistered<AmmoLoadout>() || !world.HasComponent<AmmoLoadout>(mountEntity)) return 0;
            var load = world.GetComponentRO<AmmoLoadout>(mountEntity);
            var tkb = Munitions.DbOf(world);
            int n = 0;
            for (int i = 0; i < load.Count; i++)
                if (Munitions.DecoyOf(tkb, load.AmmoAt(i)) == kind) n += load.RoundsAt(i);
            return n;
        }

        /// <summary>The unit's dispenser mount — its entity (the unit for mount 0, a mount child otherwise), index and TKB mount.</summary>
        public static bool Dispenser(EntityRepository world, Entity unit, out Entity mountEntity, out int mountIndex, out WeaponMountDto mount)
        {
            mountEntity = Entity.Null; mountIndex = -1; mount = null!;
            if (!world.IsComponentTypeRegistered<WeaponState>()) return false;
            Span<Entity> mounts = stackalloc Entity[8];
            int n = WeaponMountQuery.EnumerateMounts(world, unit, mounts);
            for (int i = 0; i < n; i++)
            {
                int index = mounts[i].Equals(unit) ? 0
                          : world.HasComponent<WeaponMountInfo>(mounts[i]) ? world.GetComponentRO<WeaponMountInfo>(mounts[i]).MountIndex : -1;
                if (index < 0 || CombatTkb.MountOf(world, unit, index) is not { Category: WeaponCategory.Countermeasure } dto) continue;
                mountEntity = mounts[i]; mountIndex = index; mount = dto;
                return true;
            }
            return false;
        }

        /// <summary>Is mount <paramref name="mountIndex"/> of <paramref name="owner"/> a dispenser (never fired at anything)?</summary>
        public static bool IsDispenser(EntityRepository world, Entity owner, int mountIndex)
            => CombatTkb.MountOf(world, owner, mountIndex)?.Category == WeaponCategory.Countermeasure;

        /// <summary>
        /// ⛔ Does <paramref name="unit"/>'s own <see cref="WeaponState"/> belong to a dispenser — its mount 0 is one, i.e. the type has no
        /// weapon (a dispenser is always the LAST mount)? Such a unit is UNARMED to every "armed" reader (threat, strength, weapon choice).
        /// </summary>
        public static bool DispenserOnly(ISimulationView view, Entity unit)
            => view is EntityRepository world && world.IsComponentTypeRegistered<WeaponState>() && world.HasComponent<WeaponState>(unit)
               && IsDispenser(world, unit, 0);
    }
}
