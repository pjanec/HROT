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
        // ⭐⭐⭐ CE-3147 — THE RING IS SIZED IN WORLD METRES, FROM THE ENTITY'S OWN FOOTPRINT.
        //   🔒 User, 2026-10-09: "the green selection circle must change size with zoom, now it is zoom independent
        //   so when i zoom out the circle is enormous in comparison to entity symbol (which scales)."
        //   🔴 It was `SelectionRadiusPx` in SCREEN PIXELS — a constant on-screen size, which is only ever right at
        //   one zoom: I first raised it 20 → 100 to fix "extremely small" zoomed IN, which made it enormous zoomed
        //   OUT. ⛔ Both readings are the same mistake, not two defects. The symbol is drawn from VehicleParams in
        //   METRES (default 5 × 2.5), so the ring must be measured the same way.
        //   ⭐ The rule now lives in ONE place for all three consumers — EntityFootprint — because this assembly
        //   cannot see Hrot.Presentation's gizmos (siblings) and three copies is how they diverged before.
        //   ⭐⭐ CE-3154 — THE STROKE IS SCREEN PIXELS, and that is now the RENDERER'S rule, not this gizmo's.
        //   🔒 User, 2026-10-10: "the green selection circle now scales but is now extremely thick (was single
        //   pixel regardless of zoom before - should be like that)."
        //   🔴 What went wrong: SizeMode governed BOTH the radius and the stroke, so switching the radius to
        //   WorldMeters silently made this "2" mean 2 METRES. There was no way to ask for "world radius, pixel
        //   stroke" — DebugPrimitiveRenderer2D.OutlineStroke is what makes it expressible, for every ring.
        //   ⭐ The value is unchanged from before CE-3147 (2 px, the look the user calls single-pixel).
        private const float RingThicknessPx = 2f;

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
            // ⭐ CE-3147 — the same query the pick areas use, so ring and hit area agree for a truck AND for a man.
            float radiusMetres = Fdp.Toolkit.Diagnostics.Gizmos.EntityFootprint.InteractionRadiusMetres(view, entity);
            draw.DrawSphere(pos, radiusMetres, color, thickness: RingThicknessPx, sizeMode: SizeMode.WorldMeters);
        }
    }
}
