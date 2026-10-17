using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-3235</c> (B-13, AW-8 / AW-9; docs/DESIGN_Civilians_And_Crowd_Control.md §7.10 PU-4) — a man asks for a LYING round (a stone,
    /// a grenade) he stands at. Raised by the behaviour on the man's brain node (<see cref="RoundPickUp.Ask"/>); the interaction transport
    /// carries it to the node that flies the round (kind 9), where <see cref="RoundPickUpSystem"/> grants it or not.
    /// </summary>
    [EventId(CombatConstants.PickUpRoundEventId)]
    [StructLayout(LayoutKind.Sequential)]
    public struct PickUpRoundEvent
    {
        /// <summary>The lying round.</summary>
        public Entity Round;
        /// <summary>The man who asks.</summary>
        public Entity Man;
        /// <summary>Came off the network: never sent again.</summary>
        public bool IsRemote;
    }

    /// <summary>
    /// ⭐ <c>CE-3235</c> (B-13, §7.10 PU-4/PU-6) — the round is his: the round's owner removed it and sends what he now holds to his brain's
    /// owner (kind 10), whose <see cref="RoundPickUpSystem"/> puts it in his hands (<see cref="PersonHands.Give"/>).
    /// </summary>
    [EventId(CombatConstants.PickUpGrantEventId)]
    [StructLayout(LayoutKind.Sequential)]
    public struct PickUpGrantEvent
    {
        /// <summary>The man it goes to.</summary>
        public Entity Man;
        /// <summary>The munition type.</summary>
        public long Ammo;
        /// <summary>How many (a lying round is one).</summary>
        public int Rounds;
        /// <summary>The fuze it had left (s) — a live round keeps burning in his hand; 0 = inert.</summary>
        public float FuzeSeconds;
        /// <summary>Came off the network: never sent again.</summary>
        public bool IsRemote;
    }

    /// <summary>
    /// ⭐ <c>CE-3235</c> (B-13, §7.10 PU-6) — a LIVE round in a man's hands (a picked-up grenade): its munition and the fuze it has left.
    /// On the MAN, on his brain's node (where his hands are); one live round at a time. <see cref="HandsFuzeSystem"/> burns it.
    /// </summary>
    [ComponentId(GlobalComponentIds.HeldFuze)]
    [DataPolicy(DataPolicy.NoScenario)]
    [StructLayout(LayoutKind.Sequential)]
    public struct HeldFuze
    {
        /// <summary>The munition type of the live round.</summary>
        public long Ammo;
        /// <summary>Seconds left (≤ 0 = gone).</summary>
        public float Remaining;
    }

    /// <summary>
    /// ⭐ <c>CE-3235</c> (B-13, §7.10) — picking a lying round up: what the behaviour asks, what the round's owner checks. One rule for both
    /// sides; the round's owner is the node that FLIES it (it alone holds its <see cref="BallisticProjectile"/>).
    /// </summary>
    public static class RoundPickUp
    {
        /// <summary>How far a man reaches for a round on the ground (m, horizontal) — embark's reach.</summary>
        public const float ReachM = 1.5f;

        /// <summary>How far above or below his feet it may lie (m).</summary>
        public const float ReachHeightM = 2f;

        /// <summary>A replicated round slower than this (m/s) lies — the map's rule (<c>ProjectileLayer</c>).</summary>
        public const float LyingSpeed = 0.2f;

        /// <summary>Does this node fly <paramref name="round"/> (so it alone may give it away)?</summary>
        public static bool OwnsRound(EntityRepository world, Entity round)
            => world.IsAlive(round) && world.IsComponentTypeRegistered<BallisticProjectile>() && world.HasComponent<BallisticProjectile>(round);

        /// <summary>The munition type of <paramref name="round"/> as this node knows it (its flight, else its replicated identity), 0 if none.</summary>
        public static long AmmoOf(EntityRepository world, Entity round)
        {
            if (!world.IsAlive(round)) return 0L;
            if (OwnsRound(world, round)) return world.GetComponentRO<BallisticProjectile>(round).Ammo;
            return world.IsComponentTypeRegistered<TkbIdentity>() && world.HasComponent<TkbIdentity>(round)
                ? world.GetComponentRO<TkbIdentity>(round).TkbType : 0L;
        }

        /// <summary>
        /// Does <paramref name="round"/> lie on the ground: on its owner, <see cref="WarheadRound.Landed"/> and not spent; on a viewer (a
        /// lightweight replica), still.
        /// </summary>
        public static bool IsLying(EntityRepository world, Entity round)
        {
            if (OwnsRound(world, round))
            {
                byte bits = world.GetComponentRO<BallisticProjectile>(round).Warhead;
                return (bits & WarheadRound.Landed) != 0 && (bits & WarheadRound.Spent) == 0;
            }
            if (!world.IsAlive(round) || !world.IsComponentTypeRegistered<LightweightRound>() || !world.HasComponent<LightweightRound>(round)) return false;
            return !world.IsComponentTypeRegistered<SimVelocity>() || !world.HasComponent<SimVelocity>(round)
                   || world.GetComponentRO<SimVelocity>(round).Linear.LengthSquared() < LyingSpeed * LyingSpeed;
        }

        /// <summary>May a hand pick <paramref name="round"/> up — its munition is a hand item (<see cref="HandItemDto"/>)?</summary>
        public static bool IsHandItem(EntityRepository world, Entity round)
            => CombatTkb.HandItemOf(world, AmmoOf(world, round)) != null;

        /// <summary>Is <paramref name="round"/> within <paramref name="man"/>'s reach?</summary>
        public static bool InReach(EntityRepository world, Entity man, Entity round)
        {
            if (!world.IsAlive(man) || !world.IsAlive(round) || !world.HasComponent<SimTransform>(man) || !world.HasComponent<SimTransform>(round)) return false;
            var d = world.GetComponentRO<SimTransform>(round).Position - world.GetComponentRO<SimTransform>(man).Position;
            return d.X * d.X + d.Y * d.Y <= ReachM * ReachM && MathF.Abs(d.Z) <= ReachHeightM;
        }

        /// <summary>
        /// ⭐ The ASKER's check (the brain's node, §7.10 PU-5): the round lies, is a hand item, is in reach, and his hands have room for it
        /// (the round's owner cannot see his hands). ⛔ Not a grant — another man may be faster.
        /// </summary>
        public static bool CanAsk(EntityRepository world, Entity man, Entity round)
            => IsLying(world, round) && IsHandItem(world, round) && InReach(world, man, round) && Alive(world, man)
               && PersonHands.HasRoom(world, man, AmmoOf(world, round));

        /// <summary>Ask for <paramref name="round"/> (publishes <see cref="PickUpRoundEvent"/>); false when <see cref="CanAsk"/> says no.</summary>
        public static bool Ask(EntityRepository world, Entity man, Entity round)
        {
            if (!world.Bus.IsRegistered<PickUpRoundEvent>() || !CanAsk(world, man, round)) return false;
            world.Bus.Publish(new PickUpRoundEvent { Round = round, Man = man });
            return true;
        }

        /// <summary>
        /// ⭐ The OWNER's check (§7.10 PU-5): this node flies <paramref name="round"/>, it lies, it is a hand item, and the man is alive and in
        /// reach. Then the grant: what he gets, the round's fuze left (a live round) or 0.
        /// </summary>
        public static bool TryGrant(EntityRepository world, Entity man, Entity round, out PickUpGrantEvent grant)
        {
            grant = default;
            if (!OwnsRound(world, round) || !IsLying(world, round) || !IsHandItem(world, round) || !Alive(world, man) || !InReach(world, man, round))
                return false;
            ref readonly var flight = ref world.GetComponentRO<BallisticProjectile>(round);
            bool live = (flight.Warhead & WarheadRound.TimeFuze) != 0;
            grant = new PickUpGrantEvent { Man = man, Ammo = flight.Ammo, Rounds = 1, FuzeSeconds = live ? MathF.Max(flight.FuzeRemaining, 0.01f) : 0f };
            return true;
        }

        /// <summary>The fuze left on the live round of <paramref name="ammo"/> in <paramref name="man"/>'s hands (s), 0 when none.</summary>
        public static float FuzeLeft(EntityRepository world, Entity man, long ammo)
        {
            if (ammo == 0 || !world.IsComponentTypeRegistered<HeldFuze>() || !world.HasComponent<HeldFuze>(man)) return 0f;
            ref readonly var held = ref world.GetComponentRO<HeldFuze>(man);
            return held.Ammo == ammo && held.Remaining > 0f ? held.Remaining : 0f;
        }

        /// <summary>A round of <paramref name="ammo"/> left <paramref name="man"/>'s hands: a live one of that kind is no longer held.</summary>
        public static void Thrown(EntityRepository world, Entity man, long ammo)
        {
            if (FuzeLeft(world, man, ammo) > 0f) world.RemoveComponent<HeldFuze>(man);
        }

        /// <summary>Put a granted round in <paramref name="grant"/>'s man's hands; a live one keeps its fuze (one at a time).</summary>
        public static bool Receive(EntityRepository world, in PickUpGrantEvent grant)
        {
            if (!PersonHands.Give(world, grant.Man, grant.Ammo, grant.Rounds)) return false;
            if (grant.FuzeSeconds > 0f && world.IsComponentTypeRegistered<HeldFuze>() && !world.HasComponent<HeldFuze>(grant.Man))
                world.AddComponent(grant.Man, new HeldFuze { Ammo = grant.Ammo, Remaining = grant.FuzeSeconds });
            return true;
        }

        private static bool Alive(EntityRepository world, Entity man)
            => world.IsAlive(man) && (!world.IsComponentTypeRegistered<Health>() || !world.HasComponent<Health>(man) || world.GetComponentRO<Health>(man).Current > 0f);
    }
}
