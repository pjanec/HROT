using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Diagnostics.Gizmos;
// Disambiguate from GizmoMap.Contracts.Fdp.Toolkit.Diagnostics.Gizmos.FixedString32.
using FixedString32 = Fdp.Core.FixedString32;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ <c>CE-1164</c> (civilian U-3, docs/DESIGN_Civilians_And_Crowd_Control.md §8 U3-c) — a person's running non-lethal effects as one
    /// text line under him: <c>DOWN</c> (knocked down), <c>STUNNED</c>, <c>GASSED</c> (irritated), ⭐ <c>COWERING</c> (CE-1164 — the CivilianHsm's mode, backend F-3), joined by " · "; NOTHING when none runs.
    /// Reads the replicated <see cref="EffectState"/> (CE-3219) against sim time — the 2-D fallback of the 3-D pose, the shape of
    /// <c>StanceGizmo</c>. On the Labels layer (on by default): an effect is rare and short, and it is what the operator must see.
    /// Auto-discovered on every map host (<c>GizmoReflectionRegistrar</c>).
    /// </summary>
    [GizmoProjector(typeof(EffectState))]
    public sealed class EffectStateGizmo : IStatelessGizmo
    {
        private static readonly Rgba32 Color = new(255, 170, 60, 255);

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (view is not EntityRepository repo || !repo.IsComponentTypeRegistered<SimTransform>() || !view.HasComponent<SimTransform>(entity)) return;
            string? text = Text(repo, entity);
            if (text == null) return;
            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
            draw.DrawText(tf.Position.X, tf.Position.Y, new FixedString32(text), Color, fontSizePx: 12f, lineOffsetPx: 28f,
                          layer: DebugTraceLayers.Labels);
        }

        /// <summary>The line shown, or null for none. Public for the rail.</summary>
        public static string? Text(EntityRepository repo, Entity entity)
        {
            bool down = Effects.IsKnockedDown(repo, entity), stunned = Effects.IsStunned(repo, entity), gassed = Effects.IsIrritated(repo, entity);
            bool cowering = Effects.IsCowering(repo, entity);
            string? gesture = GestureWord(Gestures.Of(repo, entity));   // ⭐ CE-1166 (U6-e) — what his arms show, on the same line
            if (!down && !stunned && !gassed && !cowering && gesture == null) return null;
            var parts = new System.Collections.Generic.List<string>(3);
            if (down) parts.Add("DOWN");
            if (stunned) parts.Add("STUNNED");
            if (gassed) parts.Add("GASSED");
            if (cowering) parts.Add("COWERING");
            if (gesture != null) parts.Add(gesture);
            return string.Join(" · ", parts);
        }

        /// <summary>⭐ CE-1166 (U6-e) — a gesture's word: HANDS UP, or PROTEST for the protest gestures; null for none.</summary>
        public static string? GestureWord(GestureKind kind) => kind switch
        {
            GestureKind.None => null,
            GestureKind.HandsUp => "HANDS UP",
            _ => "PROTEST",
        };
    }

    /// <summary>
    /// ⭐ <c>CE-1166</c> (civilian U-6, docs/DESIGN_Civilians_And_Crowd_Control.md §8.7 U6-e) — the same one line
    /// (<see cref="EffectStateGizmo.Text"/>) for a man who gestures but carries no <see cref="EffectState"/>: a projector runs only for
    /// entities that HAVE its component, so the effect gizmo never sees a protester with no effect. It draws only when there is no
    /// <see cref="EffectState"/> — otherwise <see cref="EffectStateGizmo"/> already draws the line (never two lines under one man).
    /// </summary>
    [GizmoProjector(typeof(Gesture))]
    public sealed class GestureGizmo : IStatelessGizmo
    {
        private readonly EffectStateGizmo _line = new();

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (view is EntityRepository repo && repo.IsComponentTypeRegistered<EffectState>() && view.HasComponent<EffectState>(entity)) return;
            _line.Draw(view, entity, draw);
        }
    }
}
