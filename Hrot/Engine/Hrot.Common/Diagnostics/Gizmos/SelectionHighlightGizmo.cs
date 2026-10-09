using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.IG.Components;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// Stateless gizmo projector that emits selection-highlight rings for every
    /// entity whose <see cref="SelectionState.IsSelected"/> flag is <c>true</c>.
    ///
    /// Primitives are emitted into the <see cref="IDebugDrawBuilder"/> pipeline so
    /// they flow through the DDS transport and are rendered by any connected consumer.
    ///
    /// Visual contract:
    /// <list type="bullet">
    ///   <item>
    ///     Primary selection: solid green wireframe ring (screen-space, 20 px radius, 2 px thick).
    ///   </item>
    ///   <item>
    ///     Secondary selection: yellow wireframe ring (same size).
    ///   </item>
    /// </list>
    /// </summary>
    [GizmoProjector(typeof(SelectionState), typeof(SimTransform))]
    public sealed class SelectionHighlightGizmo : IStatelessGizmo
    {
        // ⭐⭐ CE-3147 — radius in SCREEN PIXELS, 20 → 100 (5×). 🔒 User, 2026-10-09: "the green selection circle
        //   marker drawn on selected entity is extremely small - it should be 5 times bigger."
        //   ⚠ Why it only now reads as tiny: it is screen-sized, so raising the zoom ceiling 10× (CE-3150, MaxZoom
        //   10 → 100) grew everything measured in world metres on screen while this ring stayed at 20 px.
        //   ⚠ Deliberately NOT wired to EntityPresentationGizmoShared.EntityPickExtentPx (40 px, the clickable
        //   half-extent): a MARKER and a HIT AREA are different concerns, and this assembly cannot see that one
        //   anyway — Hrot.Common and Hrot.Presentation are siblings, neither references the other.
        private const float SelectionRadiusPx = 100f;
        private const float RingThicknessPx   = 2f;

        private static readonly Rgba32 PrimaryOutline = Rgba32.Green;
        private static readonly Rgba32 Secondary      = Rgba32.Yellow;

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!view.HasComponent<SelectionState>(entity)) return;

            ref readonly var sel = ref view.GetComponentRO<SelectionState>(entity);
            if (!sel.IsSelected) return;

            if (!view.HasComponent<SimTransform>(entity)) return;

            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
            var pos = new Vector3(tf.Position.X, tf.Position.Y, 0f);

            var color = sel.IsPrimarySelection ? PrimaryOutline : Secondary;
            draw.DrawSphere(pos, SelectionRadiusPx, color, thickness: RingThicknessPx, sizeMode: SizeMode.ScreenPixels);
        }
    }
}
