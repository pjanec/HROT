using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Combat.Components
{
    /// <summary>
    /// State of a weapon attachment (gun, launcher, etc.).
    /// Unmanaged; fits in one cache line.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.WeaponState)]
    public struct WeaponState
    {
        /// <summary>Current ammo count. Fire is refused when 0.</summary>
        public int Ammo;

        /// <summary>Remaining cooldown in seconds before the next shot is allowed.</summary>
        public float CooldownSecondsRemaining;

        /// <summary>Muzzle velocity in m/s (copied from behavior at init time).</summary>
        public float MuzzleVelocity;

        /// <summary>Maximum ammo capacity cached from <c>WeaponMountDto.InitialAmmunition</c> at spawn. Never mutated by firing.</summary>
        public int MaxAmmo;

        // ⭐ CE-3136 P-5 (peek-and-fire D9, R-239) — the magazine, beside the TOTAL above (Ammo stays the total carried). The rule
        //   is Combat.Magazine; a mount with MagazineSize 0 has no magazine and fires from its whole load, as before.

        /// <summary>Rounds in the current magazine (0 with no magazine model).</summary>
        public int MagazineRounds;

        /// <summary>Rounds a full magazine holds; <b>0 = no magazine</b> (the whole load).</summary>
        public int MagazineSize;

        /// <summary>Seconds a reload takes (cached from the mount's TKB at spawn, like <see cref="MaxAmmo"/>).</summary>
        public float ReloadSeconds;

        /// <summary>Seconds left of the running reload; 0 = not reloading.</summary>
        public float ReloadSecondsRemaining;
    }

    /// <summary>
    /// Identifies a weapon mount child entity.
    /// Placed on child entities (index 1+) spawned by <c>CombatTkbTranslator</c> for platforms
    /// with multiple weapon mounts. The primary mount (index 0) carries <see cref="WeaponState"/>
    /// on the owner entity directly for back-compatibility with actuators.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.WeaponMountInfo)]
    public struct WeaponMountInfo
    {
        /// <summary>Index into WeaponSuiteDto.Mounts (0 = primary, already on owner entity).</summary>
        public int   MountIndex;
        /// <summary>TKB weapon GUID from mount.WeaponGuid.</summary>
        public ulong WeaponGuid;
        /// <summary>Effective range in metres, from WeaponCapabilitiesDto if present; else 0.</summary>
        public float EffectiveRange;
    }

    /// <summary>
    /// Marks a bullet entity. Added by FireProcessingSystem on spawn.
    /// <see cref="PreviousPosition"/> is updated by BallisticsSystem each frame to build
    /// the swept segment used for swept-sphere raycasting.
    /// </summary>
    /// <remarks>
    /// <b>Phase 0 Adaptation:</b> The original design included a <c>Velocity</c> field.
    /// It has been removed because bullet movement is handled by <c>SimVelocity</c> on
    /// the bullet entity via <c>LinearKinematicsSystem</c>. Only <c>PreviousPosition</c>
    /// (Vector3) is kept — the BallisticsSystem captures the bullet's position before
    /// LinearKinematicsSystem advances it, so the raycast tests the correct swept segment.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.BallisticProjectile)]
    public struct BallisticProjectile
    {
        /// <summary>Entity that fired this bullet (excluded from self-hit).</summary>
        public Entity Shooter;

        /// <summary>
        /// Bullet's SimTransform.Position from the PREVIOUS frame.
        /// Set to origin on spawn; updated by BallisticsSystem BEFORE LinearKinematicsSystem runs.
        /// </summary>
        public Vector3 PreviousPosition;

        /// <summary>Damage of one penetrating hit (⭐ <c>CE-3071</c>: the fired mount's <c>DamagePerHit</c>; 0 = unknown munition).</summary>
        public float Damage;

        /// <summary>Tick at which the bullet was spawned (for lifetime check).</summary>
        public uint SpawnTick;

        /// <summary>⭐ <c>CE-3064</c> — the last unit this bullet near-missed, so one pass is reported once.</summary>
        public Entity LastNearMiss;

        /// <summary>⭐ <c>CE-3071</c> — armour penetration of the fired mount's round, mm RHA (0 = unknown munition).</summary>
        public float Penetration;

        /// <summary>
        /// ⭐ Buildings §3d P2 (R-217) — where the round left the muzzle. <see cref="Damage"/>/<see cref="Penetration"/> stay the MUZZLE
        /// values; whatever needs the round as it arrived somewhere carries it through the terrain from here
        /// (<see cref="TerrainPenetration.Carry"/>) — exact however many ticks a segment's raycast takes to resolve (three: the
        /// request is played back after the next tick's Input, solved in the one after, its result read in the third).
        /// </summary>
        public Vector3 Muzzle;

        /// <summary>⭐ R-217 — the round at its FLIGHT FRONT (after the terrain crossed so far): what <c>BallisticsSystem</c> decides a stop
        /// from, segment by segment. Never read for damage — the hit re-carries from <see cref="Muzzle"/>.</summary>
        public float FrontDamage;

        /// <inheritdoc cref="FrontDamage"/>
        public float FrontPenetration;

        /// <summary>⭐ R-217 — the tick the round stopped in the terrain (0 = flying). A stopped round is frozen at the wall and kept
        /// <see cref="CombatConstants.StoppedRoundGraceTicks"/> ticks, so the raycasts of its last segments still resolve (a unit IN
        /// FRONT of the wall is still hit), then destroyed.</summary>
        public uint StoppedTick;

        /// <summary>⭐ R-217 — bit 0: <see cref="Muzzle"/> and the front values are set (a round built without them — older tests,
        /// examples — starts its front at its first segment and is carried from that segment's start).</summary>
        public byte TerrainFlags;

        /// <summary>⭐ Stage 6 (<c>CE-1032</c>, W-2) — the TKB type id of the round (the fired mount's <c>AmmoGuid</c>; 0 = unknown).
        /// The hit and the detonation carry it; the warhead numbers are looked up from it (<c>ParameterResolver.Warhead</c>), never
        /// copied here.</summary>
        public long Ammo;

        /// <summary>⭐ Stage 6 (<c>CE-1032</c>) — <see cref="WarheadRound"/> bits, set at firing from the round's warhead
        /// (<c>ParameterResolver.Warhead</c>), so the flight never looks the warhead up.</summary>
        public byte Warhead;

        /// <summary>⭐ <c>CE-1032</c> (W-4) — seconds left on a time fuze (counts down from firing; 0 with no time fuze).</summary>
        public float FuzeRemaining;
    }

    /// <summary>⭐ Stage 6 (<c>CE-1032</c>) — the warhead bits of <see cref="BallisticProjectile.Warhead"/>.</summary>
    public static class WarheadRound
    {
        /// <summary>The round has an area effect: a terrain stop DETONATES it (W-3) instead of just ending it.</summary>
        public const byte Area = 1;
        /// <summary>The round flies a gravity arc (W-8).</summary>
        public const byte Arc = 2;
        /// <summary>Time fuze: a terrain stop LANDS it, and it bursts when the fuze runs out (W-4).</summary>
        public const byte TimeFuze = 4;
        /// <summary>Lying where it landed, waiting for its fuze.</summary>
        public const byte Landed = 8;
        /// <summary>Already burst: a late raycast of an earlier segment must not detonate it a second time.</summary>
        public const byte Spent = 16;
    }
}
