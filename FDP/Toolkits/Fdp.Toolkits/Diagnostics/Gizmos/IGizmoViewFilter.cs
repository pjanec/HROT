using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ CE-4388 (docs/DESIGN_Map_View_Filter.md, R-1049) — what the map shows THIS frame, for the gizmo system: whether an entity is in view (with a margin) and the
    /// map's scale. Decided ONCE per entity, before any rule (D-1). A filter that is not <see cref="Active"/> (no map, a headless host, before the first frame)
    /// passes everything — today's behaviour (D-7, the CE-131 guard).
    /// </summary>
    public interface IGizmoViewFilter
    {
        /// <summary>True once a view has been written; false ⇒ everything is in view and no zoom gate applies.</summary>
        bool Active { get; }

        /// <summary>The map's scale now, metres per screen pixel (at the look point in 3-D); 0 when unknown.</summary>
        float MetresPerPixel { get; }

        /// <summary>Whether <paramref name="entity"/> is in view (with the filter's margin). An entity with no position is in view.</summary>
        bool InView(ISimulationView view, Entity entity);

        /// <summary>⭐ CE-4388 — the map shows 3-D now (a 2-D-only layer draws nothing then).</summary>
        bool Is3D { get; }

        /// <summary>⭐ CE-4388 — a ground box (HROT x / y, metres) overlaps the view (with the margin); true in 3-D and when not <see cref="Active"/>.</summary>
        bool Overlaps(System.Numerics.Vector2 min, System.Numerics.Vector2 max);
    }

    /// <summary>
    /// ⭐ CE-4388 (docs/DESIGN_Map_View_Filter.md D-6) — the view of the map drawing into a gizmo builder: ONE lookup for every global gizmo, whatever assembly it
    /// lives in (the SUMO roads live in Hrot.Traffic, which cannot see the map's composition). The map registers its view once; a builder with none ⇒ null
    /// (a headless host, a rail) ⇒ the gizmo draws as before.
    /// </summary>
    public static class GizmoViews
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, IGizmoViewFilter> Views = new();

        /// <summary>Registers <paramref name="view"/> as the view of the map drawing into <paramref name="builder"/> (replaces an earlier one).</summary>
        public static void Register(object builder, IGizmoViewFilter view) => Views.AddOrUpdate(builder, view);

        /// <summary>The view of the map drawing into <paramref name="builder"/>, or null.</summary>
        public static IGizmoViewFilter? Of(object builder) => Views.TryGetValue(builder, out var v) ? v : null;
    }
}
