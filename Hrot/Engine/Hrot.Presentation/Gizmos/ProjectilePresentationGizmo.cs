using System.Numerics;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;

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

        /// <summary>⭐ CE-1203 (docs/DESIGN_Terrain_Combat_Tuning.md §5e FL-7) — the streak and the marker are 2-D SYMBOLS of a round the
        /// 3-D map draws itself (ProjectileLayer's round and tracer, FireStreams' bursts): drawn at z = 0 they lay on the ground UNDER
        /// it there. So never in 3-D, whatever the checkbox — the flag R-4058 made for the SUMO roads. ⭐ The fuze countdown is not a
        /// symbol of anything the 3-D map draws, so it stays in both.</summary>
        public const PipelineTarget SymbolTarget = PipelineTarget.Map2D | PipelineTarget.FlatOnly;

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!draw.IsLayerActive(DebugTraceLayers.Rounds)) return;   // ⭐ CE-1203 — its own checkbox (§5e FL-3): a hidden layer costs no work
            ref readonly var proj = ref view.GetComponentRO<BallisticProjectile>(entity);
            ref readonly var tf   = ref view.GetComponentRO<SimTransform>(entity);

            var start = new Vector3(proj.PreviousPosition.X, proj.PreviousPosition.Y, 0f);
            var end   = new Vector3(tf.Position.X,           tf.Position.Y,           0f);

            if ((proj.Warhead & WarheadRound.Spent) != 0) { draw.DrawLine(start, end, StreakColor, thickness: 2f, SizeMode.ScreenPixels, target: SymbolTarget, layer: DebugTraceLayers.Rounds); return; }
            var tkb = view is EntityRepository repo && repo.HasSingletonManaged<ITkbDatabase>() ? repo.GetSingletonManaged<ITkbDatabase>() : null;
            var look = proj.Ammo == 0 ? null : Hrot.Core.Tkb.MunitionTkbCatalog.VisualOf(tkb, proj.Ammo);
            bool lying = (proj.Warhead & WarheadRound.Landed) != 0 || proj.StoppedTick != 0;
            bool armed = lying && (proj.Warhead & WarheadRound.TimeFuze) != 0 && proj.FuzeRemaining > 0f;
            DrawRound(draw, start, end, look, lying, armed, proj.FuzeRemaining);
        }

        /// <summary>
        /// ⭐ The ONE 2-D drawing of a round (P1/P2, LR-3), shared by this projector (rounds on the node that runs combat) and
        /// <see cref="LightweightRoundGizmo"/> (replicated rounds on a viewer): the yellow 1-frame streak; for a munition with a look,
        /// a STATE marker — yellow flying, red-orange armed (lying, fuze burning) with its "3 s" countdown; a tracer bullet's streak
        /// wider in its glow, no marker.
        /// </summary>
        public static void DrawRound(IDebugDrawBuilder draw, Vector3 start, Vector3 end, Fdp.Toolkit.Tkb.Domain.MunitionVisualDto? look,
                                     bool lying, bool armed, float fuzeLeft)
        {
            draw.DrawLine(start, end, StreakColor, thickness: 2f, SizeMode.ScreenPixels, target: SymbolTarget, layer: DebugTraceLayers.Rounds);
            if (look == null) return;   // an ordinary bullet: the streak only
            if (look.Shape == Fdp.Toolkit.Tkb.Domain.MunitionShape.Bullet)
            {
                // ⭐ P2 (§8 PR-F) — a tracer bullet: its streak again, wider and in its glow; no marker (it is a bullet)
                if (look.Tracer) draw.DrawLine(start, end, TracerGlow(look.TracerColorHex), thickness: 3f, SizeMode.ScreenPixels, target: SymbolTarget, layer: DebugTraceLayers.Rounds);
                return;
            }
            draw.DrawSphere(end, lying ? LyingMarkerPixels : MarkerPixels, MarkerOutline, thickness: 1.5f, SizeMode.ScreenPixels,
                            fillColor: armed ? ArmedFill : FlyingFill, target: SymbolTarget, layer: DebugTraceLayers.Rounds);
            if (armed)
                draw.DrawText(end.X, end.Y, new Fdp.Core.FixedString32(FuzeLabel(fuzeLeft)), FuzeText, fontSizePx: 20f, lineOffsetPx: -18f, layer: DebugTraceLayers.Rounds);
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

    /// <summary>
    /// ⭐ LR-3 (docs/DESIGN_Visual_Effects.md §8.5, R-271) — a REPLICATED round on a viewer (<see cref="LightweightRound"/>: no combat state,
    /// just the munition type and the replicated position and velocity) drawn by the same <see cref="ProjectilePresentationGizmo.DrawRound"/>.
    /// On the node that runs combat the round also has <see cref="BallisticProjectile"/> and the other projector draws it, so this one
    /// stands down there. A viewer knows no fuze (master + position only, LR-C): a round at rest is drawn lying, not armed.
    /// </summary>
    [GizmoProjector(typeof(LightweightRound), typeof(SimTransform))]
    public sealed class LightweightRoundGizmo : IStatelessGizmo
    {
        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!draw.IsLayerActive(DebugTraceLayers.Rounds)) return;   // ⭐ CE-1203 — its own checkbox (§5e FL-3): a hidden layer costs no work
            if (view.HasComponent<BallisticProjectile>(entity) || !view.HasComponent<TkbIdentity>(entity)) return;
            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
            var velocity = view.HasComponent<SimVelocity>(entity) ? view.GetComponentRO<SimVelocity>(entity).Linear : Vector3.Zero;
            var tkb = view is EntityRepository repo && repo.HasSingletonManaged<ITkbDatabase>() ? repo.GetSingletonManaged<ITkbDatabase>() : null;
            var look = Hrot.Core.Tkb.MunitionTkbCatalog.VisualOf(tkb, view.GetComponentRO<TkbIdentity>(entity).TkbType);
            var end = new Vector3(tf.Position.X, tf.Position.Y, 0f);
            var back = velocity * (1f / 60f);   // one frame of flight, as the combat projector's streak
            var start = new Vector3(tf.Position.X - back.X, tf.Position.Y - back.Y, 0f);
            ProjectilePresentationGizmo.DrawRound(draw, start, end, look, velocity.LengthSquared() < 0.04f, armed: false, fuzeLeft: 0f);
        }
    }
}
