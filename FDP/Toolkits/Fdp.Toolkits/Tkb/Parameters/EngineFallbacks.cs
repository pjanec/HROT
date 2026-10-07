namespace Fdp.Toolkit.Tkb.Parameters
{
    /// <summary>
    /// ⭐⭐ THE ENGINE FALLBACKS — every number the engine uses when a TKB type states nothing, in ONE file
    /// (buildings programme Stage 0; 📄 docs/DESIGN_Terrain_Combat_Tuning.md §2, §6 T-0).
    /// <para>⛔ Before, these lived as private constants in the systems that read them (damage 25 in
    /// <c>CombatConstants</c>, muzzle velocity 800 and collider radius 2.5 in <c>CombatTkbTranslator</c>, eye heights in
    /// <c>TerrainWorldLosStrategy</c> and <c>PerceptionTkbTranslator</c>, health = front armour × 5 in the NED builder), so
    /// "why does this round do 25 damage?" had no answer. ⭐ The values are UNCHANGED — Stage 0 moves no number.</para>
    /// <para>⭐ Read through <see cref="ParameterResolver"/>, which reports where each value came from; the runtime calls the
    /// same resolver rules, so the API and the simulation cannot disagree.</para>
    /// </summary>
    public static class EngineFallbacks
    {
        /// <summary>Damage of one hit when the mount names no munition damage (<c>WeaponMountDto.DamagePerHit</c> ≤ 0).</summary>
        public const float BulletDamage = 25f;

        /// <summary>Muzzle velocity (m/s) when the mount states none and no range to derive it from.</summary>
        public const float MuzzleVelocity = 800f;

        /// <summary>Muzzle velocity per metre of effective range — the NED builder's derivation (<c>range × 0.5</c>).</summary>
        public const float MuzzleVelocityPerRangeMetre = 0.5f;

        /// <summary>Collider radius (m) of a combat platform no kinematics descriptor sized.</summary>
        public const float ColliderRadius = 2.5f;

        /// <summary>Hit points of a combat platform with no armour, as the NED builder sets them.</summary>
        public const float HealthWithoutArmour = 100f;

        /// <summary>Hit points per mm of front armour — the NED builder's derivation (<c>armourFront × 5</c>).</summary>
        public const float HealthPerArmourMm = 5f;

        /// <summary>Eye heights (m above Z) of an entity whose TKB states none — a standing, crouched and prone soldier.</summary>
        public const float EyeHeightStanding = 1.7f;
        /// <inheritdoc cref="EyeHeightStanding"/>
        public const float EyeHeightCrouched = 1.1f;
        /// <inheritdoc cref="EyeHeightStanding"/>
        public const float EyeHeightProne    = 0.35f;

        /// <summary>The NED builder's health rule: <c>armourFront × 5</c>, else 100.</summary>
        public static float HealthFromArmour(float armourFront)
            => armourFront > 0f ? armourFront * HealthPerArmourMm : HealthWithoutArmour;

        /// <summary>The NED builder's muzzle-velocity rule: <c>range × 0.5</c>, else 800.</summary>
        public static float MuzzleVelocityFromRange(float range)
            => range > 0f ? range * MuzzleVelocityPerRangeMetre : MuzzleVelocity;

        /// <summary>The runtime damage rule: the mount's damage when stated (&gt; 0), else <see cref="BulletDamage"/>.</summary>
        public static float DamageOrFallback(float damagePerHit) => damagePerHit > 0f ? damagePerHit : BulletDamage;

        /// <summary>The runtime muzzle-velocity rule: the mount's value when stated (&gt; 0), else <see cref="MuzzleVelocity"/>.</summary>
        public static float MuzzleVelocityOrFallback(float muzzleVelocity) => muzzleVelocity > 0f ? muzzleVelocity : MuzzleVelocity;

        /// <summary>
        /// ⭐ Buildings §3d P2 (R-217) — the penetration (mm RHA) a round of UNKNOWN penetration is assumed to carry when it meets
        /// TERRAIN (a wall, a fence, a floor). ⛔ Not used against armour: an unknown round still ignores armour
        /// (<c>ArmorModel.HitDamage</c>). Without it an unknown round would be stopped by chain-link (any resistance &gt; 0 defeats
        /// a 0 mm round). The value is the light rifle round the built-in catalogs declare (5 mm).
        /// </summary>
        public const float UnknownRoundTerrainPenetrationMm = 5f;

        /// <summary>The terrain penetration rule: the round's own value when known (&gt; 0), else <see cref="UnknownRoundTerrainPenetrationMm"/>.</summary>
        public static float TerrainPenetrationOrFallback(float penetration) => penetration > 0f ? penetration : UnknownRoundTerrainPenetrationMm;
    }
}
