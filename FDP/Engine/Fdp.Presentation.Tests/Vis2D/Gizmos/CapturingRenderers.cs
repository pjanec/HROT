using System;
using System.Collections.Generic;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis2D.Abstractions;
using GizmoMap.Presentation.Shapes;
using Raylib_cs;

namespace Fdp.Toolkit.Vis2D.Tests.Gizmos
{
    /// <summary>
    /// ⭐⭐⭐ <b>R4 (docs/DESIGN_Gizmo_Renderer_Seam.md §6) — the test doubles, moved onto the ONE seam.</b>
    ///
    /// <para>⛔⛔ <b>What was wrong before, measured 2026-09-10.</b> These doubles used to subclass
    /// <c>Fdp.Toolkit.Vis2D.Gizmos.DebugPrimitiveRenderer2D</c> — the 43-line WRAPPER — and override a
    /// <c>DispatchShape(in DebugPrimitive, RenderContext)</c> hook the wrapper had invented for itself.
    /// That hook ran in an <b>unconditional loop BEFORE</b> the real renderer, so:</para>
    /// <list type="number">
    ///   <item><description>⛔ the doubles captured <b>unfiltered</b> primitives — <c>SC-GZ011-1</c>
    ///   asserted a <c>TargetView.None</c> primitive was skipped and it was <b>captured anyway</b>
    ///   (measured: <c>Assert.Equal() 0 vs 1</c>). The filter/sort/EntityLocal rails were <b>vacuous</b>;</description></item>
    ///   <item><description>🔴 and the real renderer <b>still ran Raylib</b> underneath, so the first
    ///   primitive that reached a draw call <b>killed the test host with SIGSEGV</b> (exit 139) — which is
    ///   why only ~90 of ~185 tests in this project were ever reported (<c>CE-259aa</c>).</description></item>
    /// </list>
    ///
    /// <para>⭐⭐ <b>The seam was already there, one layer down and one comment away:</b>
    /// <c>GizmoMap.Presentation.DebugPrimitiveRenderer2D:193-195</c> declares
    /// <c>protected virtual DispatchShape(in DebugPrimitive, Camera2D, float)</c> and its own comment says
    /// <i>"Override in test subclasses to capture dispatches without Raylib"</i>.
    /// ⭐ <c>GizmoMap.Presentation.Tests.CapturingRenderer</c> has always used it, and that suite runs
    /// 41/41 headless. ⇒ these doubles now do the same thing, so filtering, sorting and
    /// <c>SpatialAnchor</c> resolution are all exercised <b>for real</b> and no Raylib call is reached.</para>
    ///
    /// <para>⚠ <b>Do not add a capture hook to the wrapper again.</b> One concept, one seam — the wrapper's
    /// job is mapping <see cref="RenderContext"/> onto <c>(Camera2D, zoom)</c>, nothing else.</para>
    /// </summary>
    internal sealed class CapturingRenderer2D : GizmoMap.Presentation.DebugPrimitiveRenderer2D
    {
        public readonly List<DebugPrimitive> Dispatched = new();

        public CapturingRenderer2D(IEntityShapeLibrary? shapeLibrary = null) : base(shapeLibrary) { }

        protected override void DispatchShape(in DebugPrimitive prim, Camera2D camera, float zoom)
            => Dispatched.Add(prim);
    }

    /// <summary>
    /// ⭐ Captures the effective geometric parameters (post <c>geomScale</c>) per dispatch.
    /// ⚠ It still recomputes <c>geomScale</c> rather than observing the renderer's own — the renderer
    /// applies it inside its private draw helpers, below the seam — but it now does so from the
    /// <b>renderer's</b> zoom, and only for primitives that actually survived filtering.
    /// </summary>
    internal sealed class GeomScaleCapturingRenderer2D : GizmoMap.Presentation.DebugPrimitiveRenderer2D
    {
        public record DrawRecord(
            DebugPrimitiveShape Shape,
            float EffectiveRadius,
            float EffectiveHeadSize,
            float EffectiveExtentX,
            float EffectiveExtentY);

        public readonly List<DrawRecord> Records = new();

        public GeomScaleCapturingRenderer2D(IEntityShapeLibrary? shapeLibrary = null) : base(shapeLibrary) { }

        protected override void DispatchShape(in DebugPrimitive prim, Camera2D camera, float zoom)
        {
            float z  = zoom > 0f ? zoom : 1f;
            float gs = prim.SizeMode == SizeMode.ScreenPixels ? 1f / z : 1f;
            Records.Add(new DrawRecord(
                prim.Shape,
                prim.SphereRadius  * gs,
                prim.ArrowHeadSize * gs,
                prim.BoxExtentX    * gs,
                prim.BoxExtentY    * gs));
        }
    }

    /// <summary>
    /// ⭐⭐ Lets a rail keep saying <c>renderer.Render(primitives, ctx)</c> while the renderer it drives is
    /// the real one. It composes through the production wrapper's <b>injection</b> constructor (R0), so
    /// the <see cref="RenderContext"/> → <c>(Camera2D, zoom)</c> mapping under test is production's own
    /// and not a copy — the mistake this whole design exists to undo.
    /// </summary>
    internal static class HeadlessRenderExtensions
    {
        public static void Render(
            this GizmoMap.Presentation.DebugPrimitiveRenderer2D inner,
            ReadOnlySpan<DebugPrimitive> primitives,
            RenderContext ctx)
            => new Fdp.Toolkit.Vis2D.Gizmos.DebugPrimitiveRenderer2D(inner).Render(primitives, ctx);

        /// <summary>
        /// ⭐ Wrap a capturing inner renderer so it can be handed to <c>DebugGizmoLayer</c>, which
        /// legitimately takes the wrapper type. ⭐⭐ ONE double serves both needs (constraint C3): the
        /// rail asserts on <c>Dispatched</c> and the layer draws through the same instance.
        /// </summary>
        public static Fdp.Toolkit.Vis2D.Gizmos.DebugPrimitiveRenderer2D AsLayerRenderer(
            this GizmoMap.Presentation.DebugPrimitiveRenderer2D inner)
            => new(inner);
    }
}
