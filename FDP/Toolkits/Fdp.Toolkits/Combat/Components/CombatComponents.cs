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

        /// <summary>Damage dealt on hit.</summary>
        public float Damage;

        /// <summary>Tick at which the bullet was spawned (for lifetime check).</summary>
        public uint SpawnTick;
    }
}
