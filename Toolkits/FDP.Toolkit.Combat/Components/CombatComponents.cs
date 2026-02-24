using System.Numerics;
using System.Runtime.InteropServices;
using Fdp.Kernel;

namespace FDP.Toolkit.Combat.Components
{
    /// <summary>
    /// State of a weapon attachment (gun, launcher, etc.).
    /// Unmanaged; fits in one cache line.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct WeaponState
    {
        /// <summary>Current ammo count. Fire is refused when 0.</summary>
        public int Ammo;

        /// <summary>Remaining cooldown ticks before the next shot is allowed.</summary>
        public int CooldownTicksRemaining;

        /// <summary>Muzzle velocity in m/s (copied from doctrine at init time).</summary>
        public float MuzzleVelocity;
    }

    /// <summary>
    /// Hit-point pool. <see cref="Current"/> &lt;= 0 means the entity is destroyed/defeated.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Health
    {
        public float Current;
        public float Max;
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
