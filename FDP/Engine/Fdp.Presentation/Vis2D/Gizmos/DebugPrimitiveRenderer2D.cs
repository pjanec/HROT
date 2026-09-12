using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Components;
using Raylib_cs;

namespace Fdp.Toolkit.Vis2D.Gizmos
{
    /// <summary>
    /// ⭐⭐ <b>A composition adapter, and nothing more.</b> It maps FDP's <see cref="RenderContext"/> onto
    /// the real renderer's <c>(Camera2D, zoom)</c> signature and forwards. All filtering, sorting,
    /// <c>EntityLocal</c> resolution and drawing belong to
    /// <see cref="GizmoMap.Presentation.DebugPrimitiveRenderer2D"/>.
    ///
    /// <para>📄 <c>docs/DESIGN_Gizmo_Renderer_Seam.md</c> — read §2 before adding anything here.</para>
    ///
    /// <para>⛔⛔ <b>THREE THINGS WERE DELETED FROM THIS CLASS ON 2026-09-10, and none of them should come
    /// back:</b></para>
    /// <list type="number">
    ///   <item><description>⛔ <b>Its own <c>protected virtual DispatchShape(in DebugPrimitive,
    ///   RenderContext)</c>, called from an unconditional loop BEFORE the inner render.</b> It was a
    ///   SECOND seam for a concept the inner renderer already owns
    ///   (<c>GizmoMap.Presentation/Rendering/DebugPrimitiveRenderer2D.cs:193-195</c>, whose comment
    ///   already read <i>"Override in test subclasses to capture dispatches without Raylib"</i>). 📌 A
    ///   test overriding the wrapper's hook saw primitives BEFORE any filtering AND still let Raylib
    ///   draw — so those rails were vacuous where they passed and SIGSEGV where a primitive reached a
    ///   draw call (defect E1 / <c>CE-259aa</c>). ⭐ Nothing in production ever subclassed this.</description></item>
    ///   <item><description>⛔ <b><c>SetLayerMask(ushort) { }</c></b> — an empty body with a LIVE caller,
    ///   every frame. 🔒 Routing it would have been WRONG, not a fix:
    ///   <c>docs/UX/Architect_Question_28_Map_Layers.md:20</c> rules the backend's
    ///   <c>LayerControlMask</c> primitive <i>"the only filter that reaches drawn primitives"</i> and
    ///   authoritative per frame. <c>ctx.VisibleLayersMask</c> is 32 bits against that 256 and would be a
    ///   SECOND authority for one fact (defect E2).</description></item>
    ///   <item><description>⛔ <b>An <c>ISimulationView?</c> parameter that was never stored.</b> 🔒 The
    ///   presentation tier is DESIGNED to need no ECS: <c>.dev/_DONE/gizmos-1/feedback2.md:798</c> —
    ///   <i>"Completely severs the presentation layer's reliance on the heavy simulation ECS (like
    ///   SimTransform or NetworkEntityMap)"</i> — resolution goes through the <c>SpatialAnchor</c>
    ///   two-pass cache instead. ⚠ One production caller PASSED the view it discarded, and that
    ///   parameter is what made <c>CE-259y</c> read the whole <c>EntityLocal</c> path as inert. It is the
    ///   designed end state, not a defect (E3).</description></item>
    /// </list>
    /// </summary>
    public class DebugPrimitiveRenderer2D
    {
        private readonly GizmoMap.Presentation.DebugPrimitiveRenderer2D _inner;

        public DebugPrimitiveRenderer2D(
            GizmoMap.Presentation.Shapes.IEntityShapeLibrary? shapeLibrary = null,
            GizmoMap.Presentation.ImGuiPropertyTreeAdapter? imGuiAdapter = null)
            : this(new GizmoMap.Presentation.DebugPrimitiveRenderer2D(
                       shapeLibrary ?? new GizmoMap.Presentation.Shapes.DefaultEntityShapeLibrary(),
                       imGuiAdapter))
        {
        }

        /// <summary>
        /// ⭐⭐⭐ <b>R0 — inject the renderer instead of subclassing the wrapper.</b> This is the ONE seam a
        /// headless rail needs: hand in a <c>GizmoMap.Presentation.DebugPrimitiveRenderer2D</c> subclass
        /// that overrides <c>DispatchShape</c>, and the rail observes real, filtered, sorted,
        /// anchor-resolved primitives with no Raylib call reached.
        ///
        /// <para>⭐ The same double then serves layer tests too — inject it here and pass this wrapper to
        /// <c>DebugGizmoLayer</c>. One double, one seam (constraint C3).</para>
        /// </summary>
        public DebugPrimitiveRenderer2D(GizmoMap.Presentation.DebugPrimitiveRenderer2D inner)
        {
            _inner = inner ?? throw new System.ArgumentNullException(nameof(inner));
        }

        public void Render(ReadOnlySpan<DebugPrimitive> primitives, RenderContext ctx)
        {
            var zoom = ctx.Zoom > 0f ? ctx.Zoom : 1f;
            var mapCamera = ctx.Resources.Get<MapCamera>();
            Camera2D camera = mapCamera != null ? mapCamera.InnerCamera : default;

            _inner.Render(primitives, camera, zoom);
        }
    }
}
