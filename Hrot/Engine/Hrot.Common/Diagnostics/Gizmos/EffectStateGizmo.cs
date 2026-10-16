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
    /// text line under him: <c>DOWN</c> (knocked down), <c>STUNNED</c>, <c>GASSED</c> (irritated), joined by " · "; NOTHING when none runs.
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
            if (!down && !stunned && !gassed) return null;
            var parts = new System.Collections.Generic.List<string>(3);
            if (down) parts.Add("DOWN");
            if (stunned) parts.Add("STUNNED");
            if (gassed) parts.Add("GASSED");
            return string.Join(" · ", parts);
        }
    }
}
