using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ <c>CE-1318</c> (Brain View §12d BV-52, R-1039) — THE one way a line to a contact is drawn: from the one who knows of it to where
    /// it is, red, fading toward the contact; a HEARD (estimated) contact dashed with its uncertainty circle. A soldier's own contacts
    /// (<see cref="LineOfSightGizmo"/>) and a commander's units' contacts (the squad-contacts gizmo) both draw through here, so the map
    /// reads one way. ⛔ Not a second drawing for the commander (two drift — the reason BV-52 exists).
    /// </summary>
    public static class ContactLine
    {
        public static void Draw(IDebugDrawBuilder draw, Vector3 from, Vector3 to, float fade, bool heard, float radius, byte layer)
        {
            byte startAlpha = (byte)(255 * fade), endAlpha = (byte)(64 * fade);
            draw.DrawLineGradient(from, to,
                new Rgba32(255, 60, 60, startAlpha), new Rgba32(255, 60, 60, endAlpha),
                thickness: 1.5f, sizeMode: SizeMode.ScreenPixels, target: PipelineTarget.Map2D, layer: layer,
                style: heard ? LineStyle.Dashed : LineStyle.Solid);
            if (heard && radius > 0f)
                draw.DrawSphere(to, radius, new Rgba32(255, 60, 60, startAlpha), thickness: 1f,
                    target: PipelineTarget.Map2D, layer: layer, style: LineStyle.Dashed);
        }
    }
}
