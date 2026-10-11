using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Diagnostics.Gizmos;

namespace Hrot.ScenarioEditor.Gizmos
{
    // GZ-PROJ: Draws a yellow streak from the previous-frame position to the current
    // position of each live BallisticProjectile entity.
    // Both positions are in world-space XY (Z=0 for the 2D map canvas).
    // ⭐ CE-3123 (R-228) — lives in Hrot.Presentation (was the Hrot.IG assembly, which only IG and the Editor reference): every
    //   host discovers it without relying on the runner pre-loading Hrot.IG. Its namespace moved with it (the generator emits one registrar per namespace per assembly).
    // ⭐ P1 (docs/DESIGN_Visual_Effects.md §8 PR-C) — the ONE 2-D projector of rounds, extended rather than duplicated: a round whose
    //   munition has a look (Munition.Visual — a grenade, a mortar bomb, a rocket) also gets a small filled marker (its STATE: flying or armed), and a
    //   round LYING on the ground with its time fuze burning (a thrown grenade) gets the seconds left beside it — before this, a lying
    //   grenade's streak had zero length and it was invisible.
    [GizmoProjector(typeof(BallisticProjectile), typeof(SimTransform))]
    public sealed class ProjectilePresentationGizmo : IStatelessGizmo
    {
        private static readonly Rgba32 StreakColor = new Rgba32(255, 255, 0, 255);
        private static readonly Rgba32 MarkerOutline = new Rgba32(40, 40, 40, 255);
        // ⭐ the marker shows STATE, not paint: an olive grenade on the grey/green map disappeared in the Xvfb frame rail (2026-10-11).
        private static readonly Rgba32 FlyingFill = new Rgba32(255, 236, 140, 255);
        private static readonly Rgba32 ArmedFill = new Rgba32(255, 80, 40, 255);
        private static readonly Rgba32 FuzeText = new Rgba32(255, 110, 60, 255);

        /// <summary>The marker's radius on screen (px); a lying round's is larger, so the danger reads.</summary>
        public const float MarkerPixels = 4f, LyingMarkerPixels = 6.5f;

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            ref readonly var proj = ref view.GetComponentRO<BallisticProjectile>(entity);
            ref readonly var tf   = ref view.GetComponentRO<SimTransform>(entity);

            var start = new Vector3(proj.PreviousPosition.X, proj.PreviousPosition.Y, 0f);
            var end   = new Vector3(tf.Position.X,           tf.Position.Y,           0f);

            draw.DrawLine(start, end, StreakColor, thickness: 2f, SizeMode.ScreenPixels);

            if (proj.Ammo == 0 || (proj.Warhead & WarheadRound.Spent) != 0) return;
            var tkb = view is EntityRepository repo && repo.HasSingletonManaged<ITkbDatabase>() ? repo.GetSingletonManaged<ITkbDatabase>() : null;
            if (Hrot.Core.Tkb.MunitionTkbCatalog.VisualOf(tkb, proj.Ammo) is not { } look) return;   // an ordinary bullet: the streak only
            if (look.Shape == Fdp.Toolkit.Tkb.Domain.MunitionShape.Bullet)
            {
                // ⭐ P2 (§8 PR-F) — a tracer bullet: its streak again, wider and in its glow; no marker (it is a bullet)
                if (look.Tracer) draw.DrawLine(start, end, TracerGlow(look.TracerColorHex), thickness: 3f, SizeMode.ScreenPixels);
                return;
            }
            bool lying = (proj.Warhead & WarheadRound.Landed) != 0 || proj.StoppedTick != 0;
            bool armed = lying && (proj.Warhead & WarheadRound.TimeFuze) != 0 && proj.FuzeRemaining > 0f;
            draw.DrawSphere(end, lying ? LyingMarkerPixels : MarkerPixels, MarkerOutline, thickness: 1.5f, SizeMode.ScreenPixels,
                            fillColor: armed ? ArmedFill : FlyingFill);
            if (armed)
                draw.DrawText(end.X, end.Y, new Fdp.Core.FixedString32(FuzeLabel(proj.FuzeRemaining)), FuzeText, fontSizePx: 20f, lineOffsetPx: -18f);
        }

        private static Rgba32 TracerGlow(string? hex)
        {
            var s = (hex ?? "").Trim().TrimStart('#');
            return s.Length == 6 && uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var v)
                ? new Rgba32((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255)
                : new Rgba32(255, 154, 48, 255);
        }

        /// <summary>The seconds left on a fuze, rounded UP like a countdown: 2.7 ⇒ "3 s". ⚠ Whole seconds on purpose — the map's
        /// default font drops a decimal point at this size (the Xvfb frame rail read "2.7 s" as "27 s").</summary>
        public static string FuzeLabel(float seconds)
            => $"{(int)System.MathF.Ceiling(System.MathF.Max(0f, seconds))} s";

    }
}
