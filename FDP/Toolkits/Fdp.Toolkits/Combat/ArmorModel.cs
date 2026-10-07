using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>Which face of a target a shot strikes.</summary>
    public enum Facing : byte { Front, Side, Rear }

    /// <summary>
    /// ⭐⭐ <c>CE-3071</c> (R-212) — ammunition vs armour: ONE model for the damage the simulation applies AND the effectiveness
    /// the AI expects, so the two can never disagree. 📄 <c>docs/DESIGN_Utility_AI_Demo_Scenarios.md</c> §9.
    /// <para>Facing from the target's yaw (&lt; 60° front, &gt; 120° rear, else side; turrets ignored) · penetration chance
    /// <c>P = clamp((pen / armour − 0.8) / 0.4, 0, 1)</c>, no armour ⇒ 1 · damage of a hit = <c>DamagePerHit × P</c>, the
    /// EXPECTED value with no dice (A2), so replays and the determinism rails stay exact · an unknown munition (no
    /// <c>DamagePerHit</c>) keeps the flat <see cref="CombatConstants.DefaultBulletDamage"/>.</para>
    /// </summary>
    public static class ArmorModel
    {
        /// <summary>Below this angle (degrees) between the target's forward and the shooter, the shot hits the front.</summary>
        public const float FrontArcDegrees = 60f;
        /// <summary>Above this angle (degrees), the shot hits the rear.</summary>
        public const float RearArcDegrees  = 120f;

        /// <summary>The face of <paramref name="target"/> a shot from <paramref name="from"/> strikes (horizontal plane).</summary>
        public static Facing FacingOf(in SimTransform target, Vector3 from)
        {
            var fwd = Vector3.Transform(Vector3.UnitX, target.Rotation);
            var toShooter = new Vector2(from.X - target.Position.X, from.Y - target.Position.Y);
            var f2 = new Vector2(fwd.X, fwd.Y);
            if (toShooter.LengthSquared() < 1e-6f || f2.LengthSquared() < 1e-6f) return Facing.Front;
            float cos = Vector2.Dot(Vector2.Normalize(f2), Vector2.Normalize(toShooter));
            float deg = MathF.Acos(Math.Clamp(cos, -1f, 1f)) * (180f / MathF.PI);
            return deg < FrontArcDegrees ? Facing.Front : deg > RearArcDegrees ? Facing.Rear : Facing.Side;
        }

        /// <summary>The armour (mm) on <paramref name="facing"/>; 0 when the platform declares none.</summary>
        public static float ArmourFor(CombatPlatformDefDto? platform, Facing facing) => platform == null ? 0f : facing switch
        {
            Facing.Front => platform.ArmorFront,
            Facing.Side  => platform.ArmorSide,
            _            => platform.ArmorRear,
        };

        /// <summary>The chance a round of <paramref name="penetration"/> mm defeats <paramref name="armour"/> mm.</summary>
        public static float PenetrationChance(float penetration, float armour)
        {
            if (armour <= 0f) return 1f;
            return Math.Clamp((penetration / armour - 0.8f) / 0.4f, 0f, 1f);
        }

        /// <summary>The expected damage of one hit: <c>DamagePerHit × P</c> (A2 — no dice).</summary>
        public static float ExpectedDamage(float penetration, float damagePerHit, float armour)
            => damagePerHit * PenetrationChance(penetration, armour);

        /// <summary>
        /// The damage one hit does: <see cref="ExpectedDamage"/> for a known munition. An UNKNOWN munition —
        /// <paramref name="penetration"/> ≤ 0: an external detonation, or a weapon whose TKB entry carries no numbers — ignores
        /// armour and does its <paramref name="damagePerHit"/>, or the flat <see cref="CombatConstants.DefaultBulletDamage"/>
        /// when that is unknown too. ⛔ Never 0 for an unknown round: that would make every unknown shot harmless on armour.
        /// </summary>
        public static float HitDamage(float penetration, float damagePerHit, float armour)
        {
            if (penetration <= 0f) return Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.DamageOrFallback(damagePerHit);
            return ExpectedDamage(penetration, Fdp.Toolkit.Tkb.Parameters.EngineFallbacks.DamageOrFallback(damagePerHit), armour);
        }
    }

    /// <summary>
    /// ⭐ <c>CE-3071</c> (R-212, A3 revised) — reads the FIXED combat numbers of a unit from the TKB by its type, where they are
    /// used: armour (<see cref="CombatPlatformDefDto"/>) and a mount's weapon (<see cref="WeaponMountDto"/>). ⛔ Nothing copies
    /// them onto a component — nothing changes them after spawn. The TKB is the world's <see cref="ITkbDatabase"/> singleton
    /// (published by SimHost, CGF and the editor); a world without it, or a unit without <see cref="TkbIdentity"/>, has no
    /// numbers and the callers fall back to today's flat behaviour.
    /// </summary>
    public static class CombatTkb
    {
        /// <summary>The TKB entry of <paramref name="entity"/>'s type, if the world and the entity carry one.</summary>
        public static bool TryGetTemplate(EntityRepository world, Entity entity, out TkbTemplate template)
        {
            template = null!;
            if (world == null || !world.IsAlive(entity)) return false;
            if (!world.HasSingletonManaged<ITkbDatabase>() || !world.IsComponentTypeRegistered<TkbIdentity>()) return false;
            if (!world.HasComponent<TkbIdentity>(entity)) return false;
            var db = world.GetSingletonManaged<ITkbDatabase>();
            return db != null && db.TryGetByType(world.GetComponent<TkbIdentity>(entity).TkbType, out template) && template != null;
        }

        /// <summary>The armour and health declaration of <paramref name="entity"/>'s type, or null.</summary>
        public static CombatPlatformDefDto? PlatformOf(EntityRepository world, Entity entity)
            => TryGetTemplate(world, entity, out var t) ? t.GetDescriptor<CombatPlatformDefDto>() : null;

        /// <summary>Mount <paramref name="mountIndex"/> of <paramref name="owner"/>'s type, or null.</summary>
        public static WeaponMountDto? MountOf(EntityRepository world, Entity owner, int mountIndex)
        {
            if (!TryGetTemplate(world, owner, out var t)) return null;
            var suite = t.GetDescriptor<WeaponSuiteDto>();
            return suite != null && mountIndex >= 0 && mountIndex < suite.Mounts.Count ? suite.Mounts[mountIndex] : null;
        }

        /// <summary>
        /// ⭐ Buildings §3d P1 (R-217) — the penetration (mm RHA) of the round <paramref name="mount"/> fires: its loaded ammo × this
        /// weapon, else the ammo's generic profile, else the mount's own value (<c>ParameterResolver.MountPenetration</c> — the rule
        /// <c>GET /tkb/resolve</c> reports). A world without a TKB database reads the mount's own value.
        /// </summary>
        public static float PenetrationOf(EntityRepository world, WeaponMountDto mount)
        {
            var db = world != null && world.HasSingletonManaged<ITkbDatabase>() ? world.GetSingletonManaged<ITkbDatabase>() : null;
            return Fdp.Toolkit.Tkb.Parameters.ParameterResolver.MountPenetration(db, mount).Value;
        }

        /// <summary>The unit a mount belongs to: the parent of a mount child (<see cref="PartMetadata"/>), else itself.</summary>
        public static Entity OwnerOf(EntityRepository world, Entity mountOrOwner)
            => world.IsComponentTypeRegistered<PartMetadata>() && world.HasComponent<PartMetadata>(mountOrOwner)
                ? world.GetComponent<PartMetadata>(mountOrOwner).ParentEntity
                : mountOrOwner;
    }
}
