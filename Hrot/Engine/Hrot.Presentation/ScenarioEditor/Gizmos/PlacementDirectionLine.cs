using System;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.ScenarioEditor.Gizmos
{
    /// <summary>
    /// ⭐ U-8 (CE-4339; 📄 docs/UX/DESIGN_Map_Interaction.md — the armed tool and a pending pick are VISIBLE; the user's report: "Paste arms the placement tool; a click-and-drag sets the orientation but NO direction gizmo is drawn") — the ONE direction line
    /// of the placement gesture (<see cref="PlacementSession"/>): while the left button is DOWN and dragged past <see cref="PlacementSession.DragThresholdM"/>, a line from the placement centre (the press point) toward the cursor, with an arrow head at the cursor
    /// end — so the person sees which way the entity (or the pasted group) will face BEFORE releasing. Drawn by every tool that owns a <see cref="PlacementSession"/> (the entity placement tool and Paste), in 2-D and in 3-D (the same primitives; the 3-D map drapes
    /// nothing here: the points carry the picked height). ⛔ Hot path: nothing is allocated — lines only.
    /// </summary>
    public static class PlacementDirectionLine
    {
        /// <summary>The line's colour (amber: the armed-tool colour of the map's hints).</summary>
        public static readonly Rgba32 Colour = new(0xF2, 0xC0, 0x33, 255);   // ⭐ B-17 P7 ① — the look's amber

        /// <summary>Line width in SCREEN pixels (a placement aid, not a world object: the same at every zoom).</summary>
        public const float ThicknessPx = 1.6f;

        /// <summary>Draws the line when a drag is in progress; true when it drew. <paramref name="cursor"/> is where the cursor is NOW (the same point <see cref="PlacementSession.Pending"/> reads).</summary>
        public static bool Draw(IDebugDrawBuilder draw, PlacementSession session, Vector3 cursor)
        {
            var (at, yaw) = session.Pending(cursor);
            if (yaw is null) return false;
            var from = at;
            var to = cursor;
            var d = new Vector2(to.X - from.X, to.Y - from.Y);
            float len = d.Length();
            if (len < 1e-3f) return false;
            draw.DrawLine(from, to, Colour, ThicknessPx, style: LineStyle.Dashed);   // ⭐ B-17 P7 ① — restyled in place (dashed amber 1.6 px), still the ONE drawer
            // the arrow head: two short strokes back from the tip, ±25° off the line (a fifth of its length, 1–8 m)
            float head = Math.Clamp(len * 0.2f, 1f, 8f);
            var u = d / len;
            foreach (float sign in stackalloc float[] { 1f, -1f })
            {
                float a = sign * 25f * (MathF.PI / 180f);
                float c = MathF.Cos(a), s = MathF.Sin(a);
                var back = new Vector2(-(u.X * c - u.Y * s), -(u.X * s + u.Y * c)) * head;
                draw.DrawLine(to, new Vector3(to.X + back.X, to.Y + back.Y, to.Z), Colour, ThicknessPx);
            }
            return true;
        }
    }
}
