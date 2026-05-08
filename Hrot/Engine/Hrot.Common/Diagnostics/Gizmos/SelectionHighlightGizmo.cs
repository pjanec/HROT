using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.IG.Components;

namespace Hrot.Common.Diagnostics.Gizmos
{
    /// <summary>
    /// Stateless gizmo projector that emits selection-highlight circles for every
    /// entity whose <see cref="SelectionState.IsSelected"/> flag is <c>true</c>.
    ///
    /// Complements <see cref="Hrot.ScenarioEditor.Rendering.SelectionRenderSystem"/> for
    /// headless / cluster-runner scenarios where the Raylib presentation layer is absent.
    /// The primitives are emitted into the <see cref="IDebugDrawBuilder"/> pipeline so
    /// they flow through the DDS transport and are rendered by any connected consumer.
    ///
    /// Visual contract (matches SelectionRenderConstants):
    /// <list type="bullet">
    ///   <item>
    ///     Primary selection: semi-transparent green filled sphere (alpha 50)
    ///     overlaid with a full-opacity green sphere.
    ///   </item>
    ///   <item>
    ///     Secondary selection: yellow sphere.
    ///   </item>
    /// </list>
    /// </summary>
    [GizmoProjector(typeof(SelectionState), typeof(SimTransform))]
    public sealed class SelectionHighlightGizmo : IStatelessGizmo
    {
        // Radius in screen pixels; matches SelectionRenderConstants.SelectionRadiusPx = 20.
        private const float SelectionRadiusPx = 20f;

        private static readonly Rgba32 PrimaryFill    = new Rgba32(0, 255, 0, 50);
        private static readonly Rgba32 PrimaryOutline = Rgba32.Green;
        private static readonly Rgba32 Secondary      = Rgba32.Yellow;

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!view.HasComponent<SelectionState>(entity)) return;

            ref readonly var sel = ref view.GetComponentRO<SelectionState>(entity);
            if (!sel.IsSelected) return;

            if (!view.HasComponent<SimTransform>(entity)) return;

            ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
            var center = new Vector3(tf.Position.X, tf.Position.Y, 0f);

            if (sel.IsPrimarySelection)
            {
                // Semi-transparent green fill + full-opacity green outline.
                draw.DrawSphere(center, SelectionRadiusPx, PrimaryFill);
                draw.DrawSphere(center, SelectionRadiusPx, PrimaryOutline);
            }
            else
            {
                // Yellow indicator for secondary selection.
                draw.DrawSphere(center, SelectionRadiusPx, Secondary);
            }
        }
    }
}
