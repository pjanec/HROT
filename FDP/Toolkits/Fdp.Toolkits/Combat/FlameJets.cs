using System;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐ <c>CE-4635</c> (R-3111; docs/designs/weapons/DESIGN_Flame_Weapons.md F-1, §6) — what a unit's FLAME JET is right now, read from state that is
    /// already on every node: no component of its own. The jet is lit while the unit's ACTIVE mount (<see cref="WeaponPosture.ActiveMount"/>) is a
    /// <see cref="WeaponCategory.Flamethrower"/> whose last fuel shot (<see cref="WeaponAmmo.LastShotAtOf"/>) is younger than <see cref="LitSeconds"/>;
    /// it points at the posture's target (else its <see cref="WeaponPosture.Point"/>); its reach and cone come from the loaded munition's
    /// <see cref="WarheadDto"/>. ⭐ ONE rule — the flame system (who it reaches) and every renderer (what to draw) read this, so they cannot differ.
    /// <para>0 B per call (a struct out; the TKB lookups are cached by <see cref="CombatTkb"/>).</para>
    /// </summary>
    public static class FlameJets
    {
        /// <summary>A jet stays lit this long after its last fuel shot (s) — a flamethrower spends fuel every few frames while it fires.</summary>
        public const double LitSeconds = 0.25;
        /// <summary>A jet at a unit is aimed at its middle, this far above its position (m) — a man's chest; not its feet.</summary>
        public const float TargetMiddleM = 1f;

        /// <summary>A unit's flame jet as every node sees it.</summary>
        public readonly struct Jet
        {
            /// <summary>The unit's mount index (0 = its primary) — where the renderer anchors the jet (its muzzle).</summary>
            public readonly int Mount;
            /// <summary>What the jet is aimed at; <see cref="Entity.Null"/> ⇒ <see cref="AimPoint"/>.</summary>
            public readonly Entity Target;
            /// <summary>The aimed world point (the target's middle — its position + <see cref="TargetMiddleM"/> — when there is one, as of this read).</summary>
            public readonly WorldPoint AimPoint;
            /// <summary>The jet's reach (m) — it stops short where a wall stops it (the flame system's cone law).</summary>
            public readonly float RangeM;
            /// <summary>The cone's half-angle (radians).</summary>
            public readonly float HalfAngleRad;
            /// <summary>0..1 — how hard it burns (a renderer's brightness).</summary>
            public readonly float Intensity;
            /// <summary>When this burst began (sim s) — a renderer's ramp-up.</summary>
            public readonly double BurstStartedAt;
            /// <summary>How long what it lights burns (s; the munition's <see cref="WarheadDto.BurnSeconds"/>, 0 = not stated) — the simulation's.</summary>
            public readonly float BurnSeconds;

            public Jet(int mount, Entity target, WorldPoint aim, float range, float halfAngleRad, float intensity, double burstStartedAt, float burnSeconds = 0f)
            {
                Mount = mount; Target = target; AimPoint = aim; RangeM = range; HalfAngleRad = halfAngleRad; Intensity = intensity; BurstStartedAt = burstStartedAt;
                BurnSeconds = burnSeconds;
            }
        }

        /// <summary>Is <paramref name="unit"/>'s flame jet lit at <paramref name="now"/> (sim s)? False for every other unit, mount or munition.</summary>
        public static bool TryGet(EntityRepository world, Entity unit, double now, out Jet jet)
        {
            jet = default;
            if (!world.IsComponentTypeRegistered<WeaponPosture>() || !world.IsComponentTypeRegistered<WeaponAmmo>()
                || !world.HasComponent<WeaponPosture>(unit) || !world.HasComponent<WeaponAmmo>(unit)) return false;
            ref readonly var posture = ref world.GetComponentRO<WeaponPosture>(unit);
            int mount = posture.ActiveMount;
            if (mount >= WeaponAmmo.MaxMounts) return false;   // 32 + k: a weapon system it controls — not a v1 jet
            ref readonly var ammo = ref world.GetComponentRO<WeaponAmmo>(unit);
            double last = ammo.LastShotAtOf(mount);
            if (!(now - last >= 0.0 && now - last < LitSeconds)) return false;   // never fired (−∞), or out
            if (CombatTkb.MountOf(world, unit, mount) is not { Category: WeaponCategory.Flamethrower } dto) return false;
            // the LOADED munition; 0 = the mount's own round (a mount with no loadout — 📌 measured: the flamethrower soldier's Load0 stays 0)
            long loaded = ammo.LoadedOf(mount);
            var warhead = loaded != 0 ? CombatTkb.WarheadOf(world, loaded) : CombatTkb.WarheadOf(world, dto);
            if (warhead is not { Kind: WarheadKind.Incendiary, FlameRangeM: > 0f } w) return false;
            var aim = posture.Point;
            if (!posture.Target.IsNull && world.IsAlive(posture.Target) && world.HasComponent<SimTransform>(posture.Target))
                aim = (WorldPoint)(world.GetComponentRO<SimTransform>(posture.Target).Position + new System.Numerics.Vector3(0f, 0f, TargetMiddleM));
            jet = new Jet(mount, posture.Target, aim, w.FlameRangeM, w.FlameHalfAngleDeg * (MathF.PI / 180f),
                w.FlameIntensity > 0f ? MathF.Min(w.FlameIntensity, 1f) : 1f, ammo.BurstStartedAtOf(mount), w.BurnSeconds);
            return true;
        }
    }
}
