using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.MuscleCharacter.Animation.Components;
// Disambiguate from GizmoMap.Contracts.Fdp.Toolkit.Diagnostics.Gizmos.FixedString32.
using FixedString32 = Fdp.Core.FixedString32;

namespace Hrot.MuscleCharacter.Animation.Stance
{
    /// <summary>
    /// ⭐ <c>CE-2121</c> — the body's stance as a text line under the entity: <c>Prone</c> / <c>Crouched</c>, or <c>→ Prone</c> while
    /// the transition runs; NOTHING while standing (the common case stays uncluttered). Reads the Muscle's
    /// <see cref="StanceStatus"/> — never the Brain's request, which would show a stance the body has not reached or refused.
    /// Auto-discovered on every map host (<c>GizmoReflectionRegistrar</c>). 📄 docs/DESIGN_Decision_Layer.md §3.3g.
    /// </summary>
    [GizmoProjector(typeof(StanceStatus))]
    public sealed class StanceGizmo : IStatelessGizmo
    {
        private static readonly Rgba32 Color = new(180, 220, 255, 255);

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!view.HasComponent<StanceStatus>(entity) || !view.HasComponent<SimTransform>(entity)) return;
            ref readonly var status = ref view.GetComponentRO<StanceStatus>(entity);
            string? text = Text(view, entity, in status);
            if (text == null) return;
            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
            draw.DrawText(tf.Position.X, tf.Position.Y, new FixedString32(text), Color, fontSizePx: 12f, lineOffsetPx: 14f);
        }

        /// <summary>The line shown, or null for none. Public for the rail.</summary>
        public static string? Text(ISimulationView view, Entity entity, in StanceStatus status)
        {
            if (status.Phase == StanceTransitionPhase.Transitioning && view.HasComponent<StanceIntent>(entity))
                return "→ " + view.GetComponentRO<StanceIntent>(entity).TargetStance;
            return status.CurrentStance == StanceId.Standing ? null : status.CurrentStance.ToString();
        }
    }
}
