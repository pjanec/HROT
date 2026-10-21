using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ Weapon systems WS-3 (W-D, W-E, W-H, R-2016; docs/designs/weapons/DESIGN_Weapon_Systems.md §3, §6) — THE one rule deciding WHO
    /// fires a weapon-system entity, and whether it may fire now. The weapon brain asks it twice: its mount list grows by the weapons
    /// a unit CONTROLS (<see cref="Controlled"/>, read by <see cref="WeaponMountQuery.EnumerateFiringMounts"/>), and a controlled weapon
    /// fires only while <see cref="CanFire(EntityRepository, Entity)"/>. Everything else — aim, ammo, turret, muzzle — is the ordinary
    /// firing path on the weapon's own mount 0 and turret part ("no different from a tank", the user).
    /// <para>Pure reads of replicated facts (the passenger list, the seats, the deploy state, health): every node answers the same.</para>
    /// </summary>
    public static class WeaponSystems
    {
        /// <summary>
        /// A controlled weapon's index in its controller's mount list: <c>ControlledMountBase + k</c> for the k-th weapon of
        /// <see cref="Controlled"/>. Above every real mount index (a type has few mounts), within the brain's one-byte mount choice.
        /// </summary>
        public const int ControlledMountBase = 32;

        /// <summary>The most weapons one unit controls (every seat of a type, plus the weapon whose gunner seat it occupies).</summary>
        public const int MaxControlled = Fdp.Toolkit.Behavior.Components.PassengerBuffer.Capacity + 1;

        /// <summary>Is <paramref name="index"/> a controlled weapon's mount index (<see cref="ControlledMountBase"/> + k)?</summary>
        public static bool IsControlledIndex(int index) => index >= ControlledMountBase && index < ControlledMountBase + MaxControlled;

        /// <summary>Is <paramref name="e"/> a weapon system (its type declares <c>Gen.WeaponSystem</c>)?</summary>
        public static bool IsWeaponSystem(EntityRepository world, Entity e)
            => !e.IsNull && world.IsAlive(e) && world.IsComponentTypeRegistered<WeaponSystemParams>() && world.HasComponent<WeaponSystemParams>(e);

        /// <summary>
        /// The ONE controller of <paramref name="weapon"/> (§6, W-D): the HOST when it sits in a host's <see cref="SeatRole.Station"/>
        /// (the host's gunner seat operates it, W-H — a person host only when it fires carried) · the man in its own
        /// <see cref="SeatRole.Gunner"/> seat when it is manned on the ground · ITSELF when automatic (<c>NeedsOperator = false</c>) and
        /// nobody operates it · nobody (<see cref="Entity.Null"/>) otherwise — a manual weapon nobody mans, cargo in a plain seat.
        /// </summary>
        public static Entity ControllerOf(EntityRepository world, Entity weapon)
        {
            if (!IsWeaponSystem(world, weapon)) return Entity.Null;
            ref readonly var p = ref world.GetComponentRO<WeaponSystemParams>(weapon);
            if (Has<IsEmbarkedTag>(world, weapon))
            {
                ref readonly var tag = ref world.GetComponentRO<IsEmbarkedTag>(weapon);
                var host = tag.VehicleEntity;
                if (!world.IsAlive(host) || !IsStation(world, host, tag.Seat, out var station)) return Entity.Null;   // cargo: nobody fires it
                if (Has<PersonCarrier>(world, host)) return p.FireWhileCarried != 0 ? host : Entity.Null;   // carried
                // ⭐ CE-1210 (DESIGN_Entity_Control §8n MS-2, R-1018; amends W-H for POSSESSED gunners only) — the man in the station's
                //   operating seat controls it while a station possesses HIM (seat switching: the vehicle is released and flies its order)
                if (station.OperatedBy >= 0 && Occupant(world, host, station.OperatedBy) is var seated && !seated.IsNull
                    && IsPossessed(world, seated) && !IsPossessed(world, host)) return seated;
                return host;
            }
            var gunner = GunnerOf(world, weapon);
            if (!gunner.IsNull) return gunner;
            return p.NeedsOperator == 0 ? weapon : Entity.Null;
        }

        /// <summary>
        /// May <paramref name="weapon"/> fire now (§6 <c>CanFire</c>, the arc aside)? A controller exists; it is deployed (a station weapon
        /// counts as deployed; a <c>FireWhileCarried</c> weapon fires stowed); a manned weapon's gunner is in the fight; a station that
        /// <c>NeedsGunner</c> has a man in the fight in its operating seat; ⭐ CE-3203 (§5b) it is not DESTROYED (health at 0 — a fragment
        /// or a blast broke it). Why not: <paramref name="reason"/>.
        /// </summary>
        public static bool CanFire(EntityRepository world, Entity weapon, out WeaponHold reason)
        {
            reason = WeaponHold.None;
            if (IsBroken(world, weapon)) { reason = WeaponHold.Destroyed; return false; }
            // ⭐ CE-3233 (B-11b, ST-8) — a CHARGE is detonated (Charges.Detonate), never fired
            if (world.IsComponentTypeRegistered<WeaponSystemParams>() && world.HasComponent<WeaponSystemParams>(weapon)
                && world.GetComponentRO<WeaponSystemParams>(weapon).Charge != 0) { reason = WeaponHold.NotDeployed; return false; }
            var controller = ControllerOf(world, weapon);
            if (controller.IsNull) { reason = WeaponHold.NoController; return false; }
            bool carried = false;
            if (Has<IsEmbarkedTag>(world, weapon))
            {
                ref readonly var tag = ref world.GetComponentRO<IsEmbarkedTag>(weapon);
                var host = tag.VehicleEntity;
                IsStation(world, host, tag.Seat, out var station);
                carried = Has<PersonCarrier>(world, host);
                // ⭐ CE-3233 (B-11, §7.13 ST-5) — a carried weapon on his BACK or in his HIDDEN station is not in his hands: draw it first
                if (carried && Fdp.Toolkit.Behavior.CarryStations.IsStowed(world, weapon)) { reason = WeaponHold.NotDeployed; return false; }
                // ⭐ CE-1206 (Entity Control §8h U-5: "the vehicle fires its station gun; one human drives and shoots, crew abstract") — a
                //   POSSESSED host's station needs no gunner in its seat; the AI keeps the gate.
                if (!carried && station.NeedsGunner != 0 && !IsPossessed(world, host))
                {
                    int seat = station.OperatedBy;
                    if (seat >= 0 && !InTheFight(world, Occupant(world, host, seat))) { reason = WeaponHold.NoGunner; return false; }
                }
                if (!carried) return true;   // a station weapon counts as deployed
            }
            else if (controller != weapon && !InTheFight(world, controller)) { reason = WeaponHold.NoGunner; return false; }
            if (carried && world.GetComponentRO<WeaponSystemParams>(weapon).FireWhileCarried != 0) return true;
            if (world.IsComponentTypeRegistered<WeaponSystemState>() && world.HasComponent<WeaponSystemState>(weapon)
                && world.GetComponentRO<WeaponSystemState>(weapon).Deploy != WeaponDeploy.Deployed)
            {
                reason = WeaponHold.NotDeployed;
                return false;
            }
            return true;
        }

        private static bool IsPossessed(EntityRepository world, Entity host)
            => world.IsComponentTypeRegistered<Fdp.Toolkit.Behavior.Possession.PossessedBy>()
               && world.HasComponent<Fdp.Toolkit.Behavior.Possession.PossessedBy>(host);

        /// <inheritdoc cref="CanFire(EntityRepository, Entity, out WeaponHold)"/>
        public static bool CanFire(EntityRepository world, Entity weapon) => CanFire(world, weapon, out _);

        /// <summary>
        /// §6 — is <paramref name="point"/> within the arc of the station <paramref name="weapon"/> sits in (its traverse and elevation
        /// limits, measured from the host's forward in the host's frame)? True off a station, or for a station that declares no arc — the
        /// weapon's own turret limits are the muscle's (<c>TurretSlew.InArc</c>, an <c>OutOfArc</c> failure).
        /// </summary>
        public static bool InStationArc(EntityRepository world, Entity weapon, Vector3 point)
        {
            if (!Has<IsEmbarkedTag>(world, weapon)) return true;
            ref readonly var tag = ref world.GetComponentRO<IsEmbarkedTag>(weapon);
            var host = tag.VehicleEntity;
            if (!IsStation(world, host, tag.Seat, out var station) || station.HasArc == 0 || !Has<SimTransform>(world, host)) return true;
            var from = Has<SimTransform>(world, weapon) ? world.GetComponentRO<SimTransform>(weapon).Position : world.GetComponentRO<SimTransform>(host).Position;
            var local = Vector3.Transform(point - from, Quaternion.Inverse(world.GetComponentRO<SimTransform>(host).Rotation));
            float azimuth = MathF.Atan2(local.Y, local.X);   // CCW from the host's forward
            float elevation = MathF.Atan2(local.Z, MathF.Sqrt(local.X * local.X + local.Y * local.Y));
            return azimuth >= station.TraverseMin - 1e-4f && azimuth <= station.TraverseMax + 1e-4f
                && elevation >= station.ElevationMin - 1e-4f && elevation <= station.ElevationMax + 1e-4f;
        }

        /// <summary>
        /// The weapon systems <paramref name="unit"/> controls, besides itself (its own mounts are its own): the weapons in its stations,
        /// the one it carries, the one whose gunner seat it occupies — each only when <see cref="ControllerOf"/> names
        /// <paramref name="unit"/>. Written into <paramref name="dest"/> in seat order (stable while nobody boards or leaves); returns the
        /// count. Zero-alloc.
        /// </summary>
        public static int Controlled(EntityRepository world, Entity unit, Span<Entity> dest)
        {
            if (dest.IsEmpty || !world.IsComponentTypeRegistered<WeaponSystemParams>() || !world.IsAlive(unit)) return 0;
            int n = 0;
            if (Has<PassengerBuffer>(world, unit) && Has<CarrierStations>(world, unit))
            {
                ref readonly var list = ref world.GetComponentRO<PassengerBuffer>(unit);
                ref readonly var stations = ref world.GetComponentRO<CarrierStations>(unit);
                for (int i = 0; i < PassengerBuffer.Capacity && n < dest.Length; i++)
                {
                    var w = list.Passengers[i];
                    if (!stations.IsStation(i) || w.IsNull || ControllerOf(world, w) != unit) continue;
                    dest[n++] = w;
                }
            }
            if (n < dest.Length && Has<IsEmbarkedTag>(world, unit))
            {
                var manned = world.GetComponentRO<IsEmbarkedTag>(unit).VehicleEntity;
                if (IsWeaponSystem(world, manned) && ControllerOf(world, manned) == unit) dest[n++] = manned;
                else n += StationsOperatedBy(world, unit, manned, dest.Slice(n));   // ⭐ CE-1210 (MS-2) — a possessed gunner's station
            }
            return n;
        }

        /// <summary>⭐ <c>CE-1210</c> (§8n MS-2) — the station weapons of <paramref name="vehicle"/> whose operating seat <paramref name="man"/>
        /// sits in and which he controls (<see cref="ControllerOf"/> — only while he is possessed).</summary>
        private static int StationsOperatedBy(EntityRepository world, Entity man, Entity vehicle, Span<Entity> dest)
        {
            if (!world.IsAlive(vehicle) || !Has<PassengerBuffer>(world, vehicle) || !Has<CarrierStations>(world, vehicle)) return 0;
            int mySeat = world.GetComponentRO<IsEmbarkedTag>(man).Seat, n = 0;
            ref readonly var list = ref world.GetComponentRO<PassengerBuffer>(vehicle);
            ref readonly var stations = ref world.GetComponentRO<CarrierStations>(vehicle);
            for (int i = 0; i < PassengerBuffer.Capacity && n < dest.Length; i++)
            {
                var w = list.Passengers[i];
                if (!stations.IsStation(i) || w.IsNull || stations.Seats[i].OperatedBy != mySeat || ControllerOf(world, w) != man) continue;
                dest[n++] = w;
            }
            return n;
        }

        /// <summary>⭐ <c>CE-1210</c> (§8n MS-2) — does <paramref name="man"/> fire a station weapon as its possessed gunner? Then he may shoot
        /// aboard though boarding took his <c>CanShoot</c> (a crew seat's man fires nothing for the AI, W-I).</summary>
        public static bool OperatesAStation(EntityRepository world, Entity man)
        {
            if (!world.IsComponentTypeRegistered<WeaponSystemParams>() || !Has<IsEmbarkedTag>(world, man) || !IsPossessed(world, man)) return false;
            Span<Entity> one = stackalloc Entity[1];
            return StationsOperatedBy(world, man, world.GetComponentRO<IsEmbarkedTag>(man).VehicleEntity, one) > 0;
        }

        /// <summary>The weapon of <paramref name="unit"/>'s mount list at <paramref name="mountIndex"/> when it is a controlled one
        /// (<see cref="IsControlledIndex"/>), else <see cref="Entity.Null"/>.</summary>
        public static Entity ControlledAt(EntityRepository world, Entity unit, int mountIndex)
        {
            if (!IsControlledIndex(mountIndex)) return Entity.Null;
            Span<Entity> weapons = stackalloc Entity[MaxControlled];
            int n = Controlled(world, unit, weapons);
            int k = mountIndex - ControlledMountBase;
            return k < n ? weapons[k] : Entity.Null;
        }

        /// <summary>
        /// ⭐ WS-3 — the mount a unit fires when it names its PRIMARY (mount 0) or its choice falls back to it: mount 0 when the unit
        /// carries a weapon of its own, else its first controlled weapon system that is not broken (a bare pickup's M2; ⭐ CE-3203) — else 0
        /// (nothing to fire).
        /// </summary>
        public static int PrimaryMount(EntityRepository world, Entity unit, out Entity mount)
        {
            mount = unit;
            // ⛔ CE-2188 (BS-1-DESIGN §5.4c) — a mount 0 that is only a flare / chaff dispenser is no primary: the doors-off UH-60's
            //   primary stays its station's door gun
            if (world.HasComponent<Components.WeaponState>(unit) && !Countermeasures.DispenserOnly(world, unit)
                && !PersonHands.HandsOnly(world, unit)   // ⭐ CE-3230 (H-3) — nor are a civilian's hands his primary
                && !WeaponLaying.IsLaidDown(world, unit, 0)) return 0;   // ⭐ CE-3232 (B-10) — nor a rifle laid on the ground
            Span<Entity> weapons = stackalloc Entity[MaxControlled];
            int n = Controlled(world, unit, weapons);
            for (int k = 0; k < n; k++)
                if (world.HasComponent<Components.WeaponState>(weapons[k]) && !IsBroken(world, weapons[k])
                    && !Fdp.Toolkit.Behavior.CarryStations.IsStowed(world, weapons[k]))   // ⭐ CE-3233 (B-11, ST-5) — not one he cannot fire
                { mount = weapons[k]; return ControlledMountBase + k; }
            return 0;
        }

        /// <summary>
        /// ⭐ WS-4 — the entity whose <see cref="Components.WeaponState"/> is <paramref name="unit"/>'s PRIMARY weapon: the unit itself when
        /// it carries one, else its first controlled weapon system (a bare pickup's M2 — <see cref="PrimaryMount"/>), else null (unarmed).
        /// ⭐ The one answer to "is this unit armed, and with what?" for readers that used to look for a WeaponState on the unit itself
        /// (the utility inputs, <c>ThreatDanger.OwnStrength</c>): a unit with its own gun reads exactly as before.
        /// </summary>
        /// ⭐ CE-3203 — a DESTROYED weapon system arms nobody: the next controlled one that still works answers, else null.
        public static Entity WeaponOf(EntityRepository world, Entity unit)
        {
            if (!world.IsComponentTypeRegistered<Components.WeaponState>()) return Entity.Null;
            if (world.HasComponent<Components.WeaponState>(unit) && !Countermeasures.DispenserOnly(world, unit)   // ⛔ CE-2188
                && !PersonHands.HandsOnly(world, unit)   // ⭐ CE-3230 (H-3) — an empty hand is no weapon
                && !WeaponLaying.IsLaidDown(world, unit, 0)) return unit;   // ⭐ CE-3232 (B-10) — nor is a laid-down one
            return IsControlledIndex(PrimaryMount(world, unit, out var weapon)) ? weapon : Entity.Null;
        }

        /// <summary>
        /// ⭐ CE-3203 (DESIGN_Weapon_Systems §5b) — is <paramref name="weapon"/> a weapon SYSTEM that has been broken (its health at 0)?
        /// It stays where it is (and in its host's controlled list, so the others' <c>32 + k</c> indices hold) but fires for nobody.
        /// </summary>
        /// <remarks>⭐ <c>CE-3214</c> (hit location §11 S2b-6) — or its WEAPON / TRAVERSE zone is wounded past its threshold (a Firepower
        /// wound, derived from the replicated <c>PartDamage</c>): a round through the receiver or the sight breaks it as surely.</remarks>
        public static bool IsBroken(EntityRepository world, Entity weapon)
            => IsWeaponSystem(world, weapon) && world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>()
               && (CombatLife.IsDestroyed(world, weapon)
                   || Fdp.Toolkit.Physics.BodyParts.Wounds.Has(world, weapon, Fdp.Toolkit.Tkb.Domain.WoundEffect.Firepower));

        /// <summary>The mount index <paramref name="weapon"/> has in <paramref name="unit"/>'s mount list, or −1 when the unit does not
        /// control it.</summary>
        public static int IndexOf(EntityRepository world, Entity unit, Entity weapon)
        {
            Span<Entity> weapons = stackalloc Entity[MaxControlled];
            int n = Controlled(world, unit, weapons);
            for (int k = 0; k < n; k++)
                if (weapons[k] == weapon) return ControlledMountBase + k;
            return -1;
        }

        /// <summary>The man in <paramref name="weapon"/>'s own <see cref="SeatRole.Gunner"/> seat (a manned tripod), else null.</summary>
        public static Entity GunnerOf(EntityRepository world, Entity weapon)
        {
            if (!Has<PassengerBuffer>(world, weapon) || !Has<CarrierSeats>(world, weapon)) return Entity.Null;
            ref readonly var list = ref world.GetComponentRO<PassengerBuffer>(weapon);
            ref readonly var seats = ref world.GetComponentRO<CarrierSeats>(weapon);
            int n = Math.Min((int)seats.SeatCount, PassengerBuffer.Capacity);
            for (int i = 0; i < n; i++)
                if (seats.Roles[i] == SeatRole.Gunner && !list.Passengers[i].IsNull) return list.Passengers[i];
            return Entity.Null;
        }

        private static Entity Occupant(EntityRepository world, Entity host, int seat)
            => seat < 0 || seat >= PassengerBuffer.Capacity || !Has<PassengerBuffer>(world, host)
                ? Entity.Null : world.GetComponentRO<PassengerBuffer>(host).Passengers[seat];

        private static bool InTheFight(EntityRepository world, Entity man)
            => !man.IsNull && world.IsAlive(man)
               && (!world.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.Health>() || Injury.IsInTheFight(world, man));

        private static bool IsStation(EntityRepository world, Entity host, int seat, out StationSeat station)
        {
            station = default;
            if (!Has<CarrierStations>(world, host)) return false;
            ref readonly var stations = ref world.GetComponentRO<CarrierStations>(host);
            if (!stations.IsStation(seat)) return false;
            station = stations.Seats[seat];
            return true;
        }

        private static bool Has<T>(EntityRepository world, Entity e) where T : unmanaged
            => !e.IsNull && world.IsComponentTypeRegistered<T>() && world.IsAlive(e) && world.HasComponent<T>(e);
    }

    /// <summary>⭐ WS-3 — why a weapon system may not fire now (<see cref="WeaponSystems.CanFire(EntityRepository, Entity, out WeaponHold)"/>).</summary>
    public enum WeaponHold : byte
    {
        None = 0,
        /// <summary>Nobody controls it: a manual weapon nobody mans, cargo, or another unit's.</summary>
        NoController = 1,
        /// <summary>Its gunner (its station's operating seat, or its own gunner seat) is empty, down or dead.</summary>
        NoGunner = 2,
        /// <summary>Stowed, deploying or packing.</summary>
        NotDeployed = 3,
        /// <summary>⭐ CE-3203 — broken: its health is at 0 (a fragment or a blast, DESIGN_Weapon_Systems §5b).</summary>
        Destroyed = 4,
    }
}
