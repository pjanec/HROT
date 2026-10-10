using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb.Parameters;

namespace Fdp.Toolkit.Combat
{
    /// <summary>
    /// ⭐⭐ Buildings §3d P2 (R-217) — a round through the TERRAIN, by the SAME rule as through armour (📄
    /// <c>docs/DESIGN_Building_Interiors.md</c> §3d, §3h): each piece the round crosses (<see cref="TerrainWorld.QueryFire(Vector3, Vector3)"/>)
    /// resists with its material's mm RHA per metre × the path inside it; the round passes with
    /// <see cref="ArmorModel.PenetrationChance"/>; along the line the chances MULTIPLY into the round's damage (the EXPECTED value,
    /// no dice — as <c>ArmorModel</c>), and its penetration is reduced by what it crossed. A crossing it cannot pass at all
    /// (chance ≤ <see cref="StopChance"/>) STOPS it there.
    /// <para>An UNKNOWN round (penetration ≤ 0) meets terrain with <see cref="EngineFallbacks.UnknownRoundTerrainPenetrationMm"/>
    /// and stays unknown afterwards (it still ignores armour — <see cref="ArmorModel.HitDamage"/>). A known round that is spent
    /// keeps <see cref="SpentPenetrationMm"/>, never 0 — 0 would turn it into an unknown round that ignores armour.</para>
    /// </summary>
    public static class TerrainPenetration
    {
        /// <summary>A crossing passed with at most this chance stops the round (the ramp is exactly 0 below pen/armour = 0.8).</summary>
        public const float StopChance = 1e-3f;

        /// <summary>What a KNOWN round that has spent its penetration keeps: it still kills a soft target, never armour.</summary>
        public const float SpentPenetrationMm = 1e-3f;

        [ThreadStatic] private static List<Fdp.Toolkit.World.TraceCrossing>? t_crossings;

        /// <summary>
        /// Carries a round of <paramref name="damage"/> and <paramref name="penetration"/> along <paramref name="from"/> →
        /// <paramref name="to"/> through <paramref name="world"/>, updating both to what arrives at the far end.
        /// </summary>
        /// <param name="stopT">Where the round stopped (0..1 along the segment) when the result is true; 1 otherwise.</param>
        /// <returns>True when a crossing stopped the round.</returns>
        /// <param name="log">⭐ T-4 — when given, each crossing is appended (what, where, the round's chance through it) for the shot log.</param>
        /// <param name="doors">⭐ R-219 — the door states of the caller's view (<see cref="DoorStates.Of(Fdp.ModuleHost.Abstractions.ISimulationView, TerrainWorld)"/>); null = as authored.</param>
        public static bool Carry(Fdp.Toolkit.World.IWorldQuery world, Vector3 from, Vector3 to, ref float damage, ref float penetration, out float stopT,
            List<ShotCrossing>? log = null)
        {
            stopT = 1f;
            var crossings = t_crossings ??= new List<Fdp.Toolkit.World.TraceCrossing>();
            world.Trace(from, to, Fdp.Toolkit.World.TracePurpose.Fire, crossings);   // ⭐ CE-1035 Q1 — through the world query (its doors are bound)
            foreach (var c in crossings)
            {
                float roundPen = EngineFallbacks.TerrainPenetrationOrFallback(penetration);
                bool passed = Cross(c.Loss, ref damage, ref penetration);
                log?.Add(new ShotCrossing(c.Kind, c.Label, c.Material ?? "", c.Loss, roundPen,
                    ArmorModel.PenetrationChance(roundPen, c.Loss), passed, Vector3.Lerp(from, to, Math.Clamp(c.T, 0f, 1f))));
                if (!passed)
                {
                    stopT = Math.Clamp(c.T, 0f, 1f);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// One crossing of <paramref name="resistanceMmRha"/>: the round passes with <see cref="ArmorModel.PenetrationChance"/>, its
        /// damage is scaled by that chance and a known round's penetration reduced by the resistance.
        /// </summary>
        /// <returns>False when the round cannot pass (it stops here).</returns>
        public static bool Cross(float resistanceMmRha, ref float damage, ref float penetration)
        {
            float effective = EngineFallbacks.TerrainPenetrationOrFallback(penetration);
            float chance = ArmorModel.PenetrationChance(effective, resistanceMmRha);
            if (chance <= StopChance) return false;
            damage *= chance;
            if (penetration > 0f) penetration = MathF.Max(penetration - resistanceMmRha, SpentPenetrationMm);
            return true;
        }
    }
}
