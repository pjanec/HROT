using System;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ <c>CE-3230</c> (B-8, AW-7 / AW-24; docs/DESIGN_Civilians_And_Crowd_Control.md §7.9) — a PERSON's HANDS: a built-in mount
    /// (<see cref="WeaponMountDto.Hands"/>) appended LAST to every person type, empty at start, whose <see cref="AmmoLoadout"/> holds what he
    /// picked up (stones, a canister, a grenade; up to <see cref="AmmoLoadout.MaxTypes"/> kinds). A throw is <c>FireAtPoint</c> on it. ⛔ An
    /// empty hand is no weapon: a hands-only unit is unarmed to every "armed" reader (H-3), and the weapon brain never CHOOSES the hands
    /// unless it holds a lethal round (H-4).
    /// </summary>
    public static class PersonHands
    {
        /// <summary>The hands mount every person type ends with: a thrown-item mount that names no round of its own.</summary>
        public static WeaponMountDto Mount() => new()
        {
            Hands = true, Category = WeaponCategory.Grenade, MuzzleVelocity = ThrowSpeed, Range = ThrowRange,
        };

        /// <summary>How fast a hand throws (m/s) — the stun grenade's.</summary>
        public const float ThrowSpeed = 15f;

        /// <summary>How far a hand throws (m).</summary>
        public const float ThrowRange = 25f;

        /// <summary>
        /// ⭐ H-1 — append the hands mount to every PERSON type of <paramref name="db"/> (its <see cref="VehicleParametersDto.VehicleClass"/>
        /// is <c>Pedestrian</c>) that does not already end with one; a type with no weapon suite gets one holding only the hands. Idempotent.
        /// Run once a database is complete: the built-in catalogue (<c>HrotEnvironment.RegisterBuiltInTkb</c>) and a file TKB
        /// (<c>KnowledgeBaseLoadStep</c>). Returns how many types it changed.
        /// </summary>
        public static int EnsureOnPersons(ITkbDatabase db)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            int changed = 0;
            foreach (var t in db.GetAll())
            {
                if (t.GetDescriptor<VehicleParametersDto>()?.VehicleClass != global::CarKinem.Core.VehicleClass.Pedestrian) continue;
                var suite = t.GetDescriptor<WeaponSuiteDto>();
                if (suite == null) { suite = new WeaponSuiteDto(); t.AddDescriptor(suite); }
                if (suite.Mounts.Count > 0 && suite.Mounts[^1].Hands) continue;
                suite.Mounts.Add(Mount());
                changed++;
            }
            return changed;
        }

        /// <summary>Is <paramref name="mount"/> the hands?</summary>
        public static bool IsHands(WeaponMountDto? mount) => mount is { Hands: true };

        /// <summary>The index of <paramref name="owner"/>'s hands mount (his type's last), or −1 when his type has none.</summary>
        public static int MountIndex(EntityRepository world, Entity owner)
        {
            if (!CombatTkb.TryGetTemplate(world, owner, out var t) || t.GetDescriptor<WeaponSuiteDto>() is not { } suite) return -1;
            for (int i = suite.Mounts.Count - 1; i >= 0; i--) if (suite.Mounts[i].Hands) return i;
            return -1;
        }

        /// <summary>
        /// ⭐ H-3 — <paramref name="unit"/>'s own <see cref="WeaponState"/> is his HANDS (his mount 0 is the hands: his type has no weapon of its
        /// own — a civilian). Like <see cref="Countermeasures.DispenserOnly"/>: such a unit has no weapon to an "armed" reader.
        /// </summary>
        public static bool HandsOnly(ISimulationView view, Entity unit)
            => view is EntityRepository world && world.IsComponentTypeRegistered<WeaponState>() && world.HasComponent<WeaponState>(unit)
               && IsHands(CombatTkb.MountOf(world, unit, 0));

        /// <summary>The entity holding mount <paramref name="index"/> of <paramref name="owner"/>: the owner for mount 0, else its mount child
        /// (<see cref="WeaponMountInfo"/>), or null.</summary>
        public static Entity MountEntity(EntityRepository world, Entity owner, int index)
        {
            if (index == 0) return world.HasComponent<WeaponState>(owner) ? owner : Entity.Null;
            if (index < 0 || !world.IsComponentTypeRegistered<WeaponMountInfo>()) return Entity.Null;
            Span<Entity> mounts = stackalloc Entity[16];
            int n = WeaponMountQuery.EnumerateMounts(world, owner, mounts);
            for (int i = 0; i < n; i++)
                if (world.HasComponent<WeaponMountInfo>(mounts[i]) && world.GetComponentRO<WeaponMountInfo>(mounts[i]).MountIndex == index) return mounts[i];
            return Entity.Null;
        }

        /// <summary>
        /// Put <paramref name="rounds"/> of munition type <paramref name="ammo"/> into <paramref name="owner"/>'s hands: the slot already
        /// holding that kind, else a free one (up to <see cref="AmmoLoadout.MaxTypes"/> kinds); loaded when the hand held nothing loaded;
        /// the mount's <see cref="WeaponState"/> total grows with it. False when he has no hands mount or all four slots hold other kinds.
        /// ⭐ The one writer — B-13's pick-up grant calls it on the hands' owner.
        /// </summary>
        public static bool Give(EntityRepository world, Entity owner, long ammo, int rounds)
        {
            if (ammo == 0 || rounds <= 0 || !world.IsComponentTypeRegistered<AmmoLoadout>()) return false;
            var mount = MountEntity(world, owner, MountIndex(world, owner));
            if (mount.IsNull || !world.HasComponent<AmmoLoadout>(mount) || !world.HasComponent<WeaponState>(mount)) return false;
            ref var load = ref world.GetComponentRW<AmmoLoadout>(mount);
            int slot = -1;
            for (int i = 0; i < load.Count && slot < 0; i++) if (load.AmmoAt(i) == ammo) slot = i;
            for (int i = 0; i < load.Count && slot < 0; i++) if (load.RoundsAt(i) <= 0) { SetAmmo(ref load, i, ammo); slot = i; }
            if (slot < 0)
            {
                if (load.Count >= AmmoLoadout.MaxTypes) return false;
                slot = load.Count++;
                SetAmmo(ref load, slot, ammo);
            }
            load.SetRounds(slot, load.RoundsAt(slot) + rounds);
            if (load.RoundsAt(load.Loaded) <= 0 || load.Loaded >= load.Count) load.Loaded = (byte)slot;
            ref var weapon = ref world.GetComponentRW<WeaponState>(mount);
            weapon.Ammo += rounds;
            weapon.MaxAmmo = Math.Max(weapon.MaxAmmo, weapon.Ammo);
            return true;
        }

        /// <summary>How many rounds of <paramref name="ammo"/> <paramref name="owner"/> holds in his hands (0 = none, or no hands).</summary>
        public static int Rounds(EntityRepository world, Entity owner, long ammo)
        {
            var mount = MountEntity(world, owner, MountIndex(world, owner));
            if (mount.IsNull || !world.IsComponentTypeRegistered<AmmoLoadout>() || !world.HasComponent<AmmoLoadout>(mount)) return 0;
            ref readonly var load = ref world.GetComponentRO<AmmoLoadout>(mount);
            int n = 0;
            for (int i = 0; i < load.Count; i++) if (load.AmmoAt(i) == ammo) n += load.RoundsAt(i);
            return n;
        }

        /// <summary>
        /// ⭐ H-5 — a hand has no breech: when the loaded kind of <paramref name="mount"/>'s hands is spent, the next kind with rounds is loaded
        /// at once (no switch time). Returns the munition type now in hand, 0 when the hand is empty.
        /// </summary>
        public static long ReadyNext(EntityRepository world, Entity mount)
        {
            if (!world.IsComponentTypeRegistered<AmmoLoadout>() || !world.HasComponent<AmmoLoadout>(mount)) return 0L;
            ref var load = ref world.GetComponentRW<AmmoLoadout>(mount);
            if (load.Loaded < load.Count && load.RoundsAt(load.Loaded) > 0) return load.LoadedAmmo;
            for (int i = 0; i < load.Count; i++)
                if (load.RoundsAt(i) > 0) { load.Loaded = (byte)i; load.SwitchRemaining = 0f; return load.AmmoAt(i); }
            return 0L;
        }

        /// <summary>
        /// The munition types in <paramref name="owner"/>'s hands with rounds, by what his hands mount holds: the local
        /// <see cref="AmmoLoadout"/> where it holds something (the brain's node), else the replicated <see cref="WeaponAmmo"/> breech round
        /// (every other node: the loadout is not on the wire). Calls <paramref name="each"/> per kind; returns false when the hand is empty.
        /// </summary>
        public static bool Holding(EntityRepository world, Entity owner, Func<long, bool> each)
        {
            int index = MountIndex(world, owner);
            if (index < 0) return false;
            var mount = MountEntity(world, owner, index);
            bool any = false;
            if (!mount.IsNull && world.IsComponentTypeRegistered<AmmoLoadout>() && world.HasComponent<AmmoLoadout>(mount))
            {
                var load = world.GetComponentRO<AmmoLoadout>(mount);
                for (int i = 0; i < load.Count; i++)
                {
                    if (load.RoundsAt(i) <= 0) continue;
                    any = true;
                    if (each(load.AmmoAt(i))) return true;
                }
                if (any) return true;
            }
            if (index < WeaponAmmo.MaxMounts && world.IsComponentTypeRegistered<WeaponAmmo>() && world.HasComponent<WeaponAmmo>(owner))
            {
                var wire = world.GetComponentRO<WeaponAmmo>(owner);
                if (wire.LoadedOf(index) != 0 && wire.LoadedRoundsOf(index) > 0) { each(wire.LoadedOf(index)); return true; }
            }
            return any;
        }

        /// <summary>⭐ AW-9 — does <paramref name="owner"/> hold a LETHAL round in his hands (a live grenade)? Stones, a canister: no.</summary>
        public static bool HoldsLethal(EntityRepository world, Entity owner)
        {
            bool lethal = false;
            Holding(world, owner, ammo => lethal = ItemAppearances.IsLethal(world, ammo));
            return lethal;
        }

        private static void SetAmmo(ref AmmoLoadout load, int slot, long ammo)
        {
            switch (slot)
            {
                case 0: load.Ammo0 = ammo; break;
                case 1: load.Ammo1 = ammo; break;
                case 2: load.Ammo2 = ammo; break;
                case 3: load.Ammo3 = ammo; break;
            }
        }
    }
}
