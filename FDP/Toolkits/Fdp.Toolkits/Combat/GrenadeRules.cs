using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Perception;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Tkb.Domain;

namespace Fdp.Toolkit.Combat
{
    /// <summary>⭐ <c>CE-2360</c> I-4 — what a hand grenade is, for every rule that picks one.</summary>
    public enum GrenadeKind : byte
    {
        /// <summary>Neither a stun nor a fragmenting warhead (smoke, gas…).</summary>
        None = 0,
        Stun = 1,
        Fragmentation = 2,
    }

    /// <summary>
    /// ⭐⭐ <c>CE-2360</c> I-4 (AQ102 R2-4, R-2073) — the grenade rules every thrower shares: what a warhead is (<see cref="KindOf"/>), how far it
    /// kills (<see cref="LethalRadius"/>), whether the burst is INSIDE a building (<see cref="Indoors"/>) and whether a civilian the thrower KNOWS
    /// of is in its reach (<see cref="KnownNeutralNear"/>). The room drill (<c>GrenadeCheck</c>, behaviors) and the weapon brain's hidden-target
    /// throw (<see cref="HiddenTargetThrow"/>) read them, so a grenade into a room and a grenade over a wall follow one law.
    /// 📄 docs/blueprints/Architect_Question_102_Responsive_Fighting_Reflexes.md §9 R2-4 · docs/designs/group-maneuvers/DESIGN_Building_Clearing.md §7.3.
    /// </summary>
    public static class GrenadeRules
    {
        /// <summary>CB-70 — a fragmentation grenade's lethal radius, as a share of its fragment reach (an M67: 15 m reach, 5 m lethal).</summary>
        public const float LethalShareOfReach = 1f / 3f;

        /// <summary>The kind <paramref name="warhead"/> is: a stun warhead, a fragmenting one, or neither.</summary>
        public static GrenadeKind KindOf(WarheadDto? warhead)
            => warhead == null ? GrenadeKind.None
             : warhead.Kind == WarheadKind.Stun ? GrenadeKind.Stun
             : warhead.FragmentRadiusM > 0f ? GrenadeKind.Fragmentation
             : GrenadeKind.None;

        /// <summary>The damage that takes a standard man down: 70 of his 100 — below the 30 % line he is incapacitated (CE-2155).</summary>
        public const float DownDamage = 70f;

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WarheadDto, StrongBox> s_lethal = new();
        private sealed class StrongBox { public float Value; }

        /// <summary>
        /// CB-70 — how far <paramref name="warhead"/> takes a man down: its blast-lethal radius, or where a standing man in the open expects
        /// <see cref="DownDamage"/> from its fragments by the fragment LAW (<see cref="AreaEffect.Fragments"/>, the one the burst applies),
        /// whichever is more; computed once per warhead. ⭐⭐ CE-2360 — 📐 MEASURED `2026-10-24`: since CE-3203 an M67's
        /// <c>FragmentRadiusM</c> is the law's HAZARD radius (54.4 m, where a man expects &lt; 1), so the old "a third of the reach" read
        /// 18.1 m — the thrower 18 m off was inside his own burst, and CB-70's own note says an M67 is "5 m lethal". A warhead the law
        /// cannot weigh (no fragment count or wound) keeps the old share of its reach.
        /// </summary>
        public static float LethalRadius(WarheadDto warhead)
        {
            if (s_lethal.TryGetValue(warhead, out var box)) return box.Value;
            float r = warhead.FragmentCount > 0f && warhead.FragmentWoundDamage > 0f
                ? MathF.Max(warhead.BlastLethalRadiusM, FragmentDownRadius(warhead))
                : MathF.Max(warhead.BlastLethalRadiusM, warhead.FragmentRadiusM * LethalShareOfReach);
            s_lethal.AddOrUpdate(warhead, new StrongBox { Value = r });
            return r;
        }

        /// <summary>Where a standing man in the open (the hazard radius's own body, <see cref="AreaEffect.FragmentHazardRadius"/>) expects
        /// <see cref="DownDamage"/> from <paramref name="w"/>'s fragments.</summary>
        private static float FragmentDownRadius(WarheadDto w)
        {
            var open = w with { FragmentRadiusM = 0f };
            float Damage(float d) => AreaEffect.Fragments(open, AreaEffect.FragmentTarget.Person, 0.7f, d, 0f, 1f)?.Damage ?? 0f;
            if (Damage(0.5f) < DownDamage) return 0f;
            float lo = 0.5f, hi = 1f;
            while (hi < 500f && Damage(hi) >= DownDamage) { lo = hi; hi *= 2f; }
            for (int i = 0; i < 30; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (Damage(mid) >= DownDamage) lo = mid; else hi = mid;
            }
            return MathF.Round(lo, 1);
        }

        /// <summary>True when <paramref name="point"/> lies inside a building's footprint (a room, through its door or window);
        /// <paramref name="building"/> is its index. No terrain ⇒ outdoors.</summary>
        public static bool Indoors(EntityRepository world, Vector3 point, out int building)
        {
            building = -1;
            if (!world.HasSingletonManaged<TerrainWorld>()) return false;
            var terrain = world.GetSingletonManaged<TerrainWorld>();
            return terrain != null && InsideTest.IsInside(terrain, new Vector2(point.X, point.Y), out building);
        }

        /// <summary>
        /// A civilian <paramref name="unit"/> KNOWS of — an identified neutral in his memory, alive and in the fight — within
        /// <paramref name="radius"/> of <paramref name="point"/> where his memory places him, or (<paramref name="building"/> ≥ 0) anywhere inside
        /// that building. ⭐ What he knows, not the truth: a civilian he never saw does not stop him (R-2073 — "a KNOWN civilian").
        /// </summary>
        public static unsafe bool KnownNeutralNear(EntityRepository world, Entity unit, Vector3 point, float radius, int building = -1)
        {
            if (!world.IsComponentTypeRegistered<TargetMemory>() || !world.HasComponent<TargetMemory>(unit)) return false;
            ref readonly var mem = ref world.GetComponentRO<TargetMemory>(unit);
            var terrain = building >= 0 && world.HasSingletonManaged<TerrainWorld>() ? world.GetSingletonManaged<TerrainWorld>() : null;
            float r2 = radius * radius;
            for (int i = 0; i < mem.Count && i < PerceptionConstants.MaxTrackedTargets; i++)
            {
                if (mem.EntityIds[i] <= 0 || TargetMemory.IsAnonymous(in mem, i)) continue;
                var e = new Entity((ulong)mem.EntityIds[i]);
                if (!world.IsAlive(e) || !ThreatDanger.IsNeutral(world, e) || ThreatDanger.IsOutOfTheFight(world, e)) continue;
                var at = new Vector2(mem.PositionsX[i], mem.PositionsY[i]);
                if (Vector2.DistanceSquared(at, new Vector2(point.X, point.Y)) < r2) return true;
                if (terrain != null && InsideTest.IsInside(terrain, at, out int theirs) && theirs == building) return true;
            }
            return false;
        }
    }
}
