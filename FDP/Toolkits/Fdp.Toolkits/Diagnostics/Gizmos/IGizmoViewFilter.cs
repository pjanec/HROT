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
    }
}
