using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// ⭐⭐ <c>CE-2442</c> D-2 (📄 docs/designs/minefields/DESIGN_Minefields.md §13) — what the HQ's <c>Engineers</c> answer derives from the request's
    /// POINT (the request carries no field): the field there, the far side along the requester's line, whether the HQ's side knows it in full,
    /// an engineer unit's clearer and its man. Read-only; the outline buffer is per thread (the brains run as a parallel phase).
    /// </summary>
    public static class EngineersAnswer
    {
        /// <summary>A field counts as the request's when its outline lies this near the point (m) — the request's own radius.</summary>
        public const float FieldReachMetres = 30f;
        /// <summary>The breach ends this far beyond the field's far edge (m).</summary>
        public const float BeyondMetres = 5f;

        [ThreadStatic] private static List<Vector2>? t_outline;

        /// <summary>The nearest minefield whose outline lies within <see cref="FieldReachMetres"/> of <paramref name="at"/> (inside counts 0).</summary>
        public static bool FieldAt(EntityRepository world, Vector3 at, out Entity field, out int netId)
        {
            field = Entity.Null; netId = 0;
            if (!world.IsComponentTypeRegistered<Minefield>()) return false;
            var outline = t_outline ??= new List<Vector2>(32);
            var p = new Vector2(at.X, at.Y);
            float best = FieldReachMetres;
            foreach (var e in world.Query().With<Minefield>().Build())
            {
                if (!MinefieldKnowledgeOps.TryOutline(world, e, outline, out _)) continue;
                float d = DistanceTo(outline, p);
                if (d > best || (d == best && !field.IsNull && e.Index > field.Index)) continue;
                int id = world.HasComponent<NetworkIdentity>(e) ? world.GetComponentRO<NetworkIdentity>(e).Value : 0;
                if (id == 0) continue;
                field = e; netId = id; best = d;
            }
            return !field.IsNull;
        }

        /// <summary>Where the line from <paramref name="from"/> through <paramref name="near"/> LEAVES <paramref name="field"/>'s outline, plus
        /// <see cref="BeyondMetres"/>; no crossing ⇒ the far side of the outline's bounds along that line.</summary>
        public static Vector3 FarSide(EntityRepository world, Entity field, Vector3 from, Vector3 near)
        {
            var outline = t_outline ??= new List<Vector2>(32);
            var a = new Vector2(near.X, near.Y);
            var d = a - new Vector2(from.X, from.Y);
            if (!MinefieldKnowledgeOps.TryOutline(world, field, outline, out var anchor)) return near;
            if (d.LengthSquared() < 1e-4f) d = Centroid(outline) - a;
            if (d.LengthSquared() < 1e-4f) d = Vector2.UnitX;
            d = Vector2.Normalize(d);
            float exit = -1f;
            for (int i = 0; i < outline.Count; i++)
            {
                var p = outline[i]; var q = outline[(i + 1) % outline.Count];
                if (RaySegment(a, d, p, q, out float t) && t > exit) exit = t;
            }
            if (exit < 0f)
            {
                float span = 0f;
                for (int i = 0; i < outline.Count; i++) span = MathF.Max(span, Vector2.Distance(a, outline[i]));
                exit = span;
            }
            var end = a + d * (exit + BeyondMetres);
            return new Vector3(end.X, end.Y, near.Z);
        }

        /// <summary>The HQ's side knows <paramref name="field"/> in full (marked, or known at start).</summary>
        public static bool KnowsInFull(EntityRepository world, Entity hq, Entity field)
            => world.HasComponent<EntityInfo>(hq)
               && PlaceKnowledgeTable.Of(world).Marked((int)world.GetComponentRO<EntityInfo>(hq).ForceId, field);

        /// <summary>An engineer unit: its roster holds a live TKB 116 mine clearer.</summary>
        public static bool HasClearer(EntityRepository world, Entity unit)
        {
            if (!world.HasComponent<UnitRoster>(unit)) return false;
            ref readonly var roster = ref world.GetComponentRO<UnitRoster>(unit);
            for (int i = 0; i < roster.Count; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (!m.IsNull && world.IsAlive(m) && world.HasComponent<TkbIdentity>(m)
                    && world.GetComponentRO<TkbIdentity>(m).TkbType == Hrot.Map.Common.TkbEntityTypes.Engineer_MineClearer) return true;
            }
            return false;
        }

        /// <summary>The engineer unit's first live man on foot (the recon) — a sub-unit's first man when the unit holds teams — or none.</summary>
        public static Entity ManOf(EntityRepository world, Entity unit, int depth = 0)
        {
            if (!world.HasComponent<UnitRoster>(unit)) return Entity.Null;
            ref readonly var roster = ref world.GetComponentRO<UnitRoster>(unit);
            for (int i = 0; i < roster.Count; i++)
            {
                var m = roster.SubordinateEntities[i];
                if (m.IsNull || !world.IsAlive(m) || Fdp.Toolkit.Motion.Locomotions.IsVehicle(world, m)) continue;
                if (SquadManeuverRunner.IsUnit(world, m))
                {
                    if (depth < 2 && ManOf(world, m, depth + 1) is { IsNull: false } man) return man;
                    continue;
                }
                if (Fdp.Toolkit.Combat.Injury.IsInTheFight(world, m)) return m;
            }
            return Entity.Null;
        }

        private static float DistanceTo(List<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            float best = float.MaxValue;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
                var ab = b - a;
                float t = ab.LengthSquared() > 1e-8f ? Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f) : 0f;
                best = MathF.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return inside ? 0f : best;
        }

        private static Vector2 Centroid(List<Vector2> poly)
        {
            var c = Vector2.Zero;
            for (int i = 0; i < poly.Count; i++) c += poly[i];
            return poly.Count > 0 ? c / poly.Count : c;
        }

        private static bool RaySegment(Vector2 o, Vector2 d, Vector2 p, Vector2 q, out float t)
        {
            t = 0f;
            var e = q - p;
            float den = d.X * e.Y - d.Y * e.X;
            if (MathF.Abs(den) < 1e-8f) return false;
            var w = p - o;
            t = (w.X * e.Y - w.Y * e.X) / den;
            float u = (w.X * d.Y - w.Y * d.X) / den;
            return t >= 0f && u >= 0f && u <= 1f;
        }
    }
}
