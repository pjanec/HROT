using System;
using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <c>CE-4391</c> S-1 (UTM §8 D-4 / D-5, §8.2 B-5, B-6) — THE ONE rule that turns a burst and a wall panel into a breach: static, pure, no
    /// allocation. The Muscle decides (<c>AreaEffectSystem.BreachWalls</c>); the brain never asks (R-2037). Numbers are DATA — <see cref="BreachTable"/>.
    /// </summary>
    public static class BreachRule
    {
        /// <summary>How far from a burst a panel can still be breached: the table's largest scaled distance × cbrt(kg) — the radius of the panel search.</summary>
        public static float Reach(BreachTable table, float explosiveKg)
            => explosiveKg < table.MinKg ? 0f : table.MaxZ * MathF.Cbrt(explosiveKg);

        /// <summary>
        /// The distance (m) from <paramref name="burst"/> to the panel's near face: to the panel's extruded centre line (its segment × its storey),
        /// less half its thickness, never below 0. A burst against the wall (the round bursts <c>AreaEffect.BurstStandOffMetres</c> off it) reads ≈ 0.
        /// </summary>
        public static float DistanceTo(in TerrainWallPanel panel, Vector3 burst)
        {
            var ab = panel.B - panel.A;
            float len2 = ab.LengthSquared();
            var p = new Vector2(burst.X, burst.Y);
            float t = len2 > 1e-8f ? Math.Clamp(Vector2.Dot(p - panel.A, ab) / len2, 0f, 1f) : 0f;
            float dh = Vector2.Distance(p, panel.A + ab * t);
            float dv = burst.Z < panel.BaseZ ? panel.BaseZ - burst.Z : burst.Z > panel.TopZ ? burst.Z - panel.TopZ : 0f;
            return MathF.Max(0f, MathF.Sqrt(dh * dh + dv * dv) - panel.Thickness * 0.5f);
        }

        /// <summary>The scaled distance of a burst of <paramref name="explosiveKg"/> at <paramref name="distance"/> m: d / ∛kg.</summary>
        public static float ScaledDistance(float distance, float explosiveKg) => distance / MathF.Cbrt(explosiveKg);

        /// <summary>The factor a panel's material scales a breach by: √(reference resistance ÷ its resistance), clamped (a wood wall breaks wider, a steel one narrower).</summary>
        public static float MaterialScale(BreachTable table, TerrainMaterial? material)
        {
            float r = material?.ResistanceMmRhaPerMetre ?? 0f;
            if (r <= 0f) return table.ScaleMax;
            return Math.Clamp(MathF.Sqrt(table.ReferenceResistance / r), table.ScaleMin, table.ScaleMax);
        }

        /// <summary>
        /// Decides the breach a burst of <paramref name="explosiveKg"/> at <paramref name="burst"/> makes in <paramref name="panel"/> (global index
        /// <paramref name="panelIndex"/>): the rectangle centred on the burst's projection on the panel (and, unless the row is a passage, on its
        /// height), clipped to the panel's length and storey, its bottom snapped to the floor when within <see cref="BreachTable.FloorSnap"/>.
        /// <paramref name="fuze"/> is the WARHEAD's (<c>WarheadDto.Fuze</c>; the <c>DetonationNotification</c> carries none): an impact round bursts 0.1 m off the
        /// wall it struck (Z ≈ 0), so a row for a placed / time-fuzed charge must not catch it — rows name the fuzes they apply to.
        /// False when nothing breaches (a kinetic round, too small a charge, too far, or a clip too small to count). ⛔ Does NOT test the line to the
        /// panel — the caller does (it needs the world).
        /// </summary>
        public static bool TryDecide(BreachTable table, in TerrainWallPanel panel, int panelIndex, Vector3 burst, float explosiveKg,
            Fdp.Toolkit.Tkb.Domain.FuzeKind fuze, out WallBreach breach)
        {
            breach = default;
            if (explosiveKg <= 0f || explosiveKg < table.MinKg) return false;
            float z = ScaledDistance(DistanceTo(in panel, burst), explosiveKg);
            BreachRow? chosen = null;
            for (int i = 0; i < table.Rows.Count; i++)
            {
                var r = table.Rows[i];
                if (explosiveKg >= r.KgMin && z <= r.ZMax && r.AppliesTo(fuze)) { chosen = r; break; }
            }
            if (chosen is not { } row) return false;

            float len = panel.Length;
            if (len <= 0f) return false;
            float from, to, bottom, top;
            if (row.WholePanel)
            {
                from = 0f; to = len; bottom = panel.BaseZ; top = panel.TopZ;
            }
            else
            {
                float scale = MathF.Cbrt(explosiveKg / table.ReferenceKg) * MaterialScale(table, panel.Material);
                float w = row.Width * scale, h = row.Height * scale;
                var dir = (panel.B - panel.A) / len;
                float s = Vector2.Dot(new Vector2(burst.X, burst.Y) - panel.A, dir);
                from = MathF.Max(0f, s - w * 0.5f);
                to = MathF.Min(len, s + w * 0.5f);
                if (row.Passage) { bottom = panel.BaseZ; top = MathF.Min(panel.TopZ, panel.BaseZ + h); }
                else
                {
                    bottom = MathF.Max(panel.BaseZ, burst.Z - h * 0.5f);
                    top = MathF.Min(panel.TopZ, burst.Z + h * 0.5f);
                    if (bottom - panel.BaseZ <= table.FloorSnap) bottom = panel.BaseZ;
                }
            }
            if (to - from < table.MinSize || top - bottom < table.MinSize) return false;
            breach = new WallBreach(panelIndex, from, to, bottom, top, Classify(in panel, table, from, to, bottom, top));
            return true;
        }

        /// <summary>D-2 — a PASSAGE iff the rectangle reaches the storey floor and is at least <c>passageMinWidth × passageMinHeight</c>; else a HOLE.</summary>
        public static BreachKind Classify(in TerrainWallPanel panel, BreachTable table, float from, float to, float bottom, float top)
            => bottom <= panel.BaseZ + 1e-3f && to - from >= table.PassageMinWidth && top - bottom >= table.PassageMinHeight
                ? BreachKind.Passage : BreachKind.Hole;
    }
}
