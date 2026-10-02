using Fdp.Core;
using Fdp.Toolkit.Vis2D.Components;

namespace Hrot.Presentation.Map;

/// <summary>
/// ⭐⭐⭐ <b><c>UXI-23</c> <c>S1</c> — THE ONE LIST for the map's presentation components.</b>
/// Every ECS-enabled host that presents a map calls this; ⛔ none registers these types itself.
///
/// <para>⚠⚠ <b>Why this is not simply part of <see cref="Hrot.Map.Common.PresentationComponentRegistry"/>,
/// which calls itself "THE ONE LIST".</b> 📐 Measured: <c>MapDisplayComponent</c> is compiled into
/// <b><c>Fdp.Presentation</c></b>, and <c>Hrot.Core</c> — where that registry lives — references
/// <c>Fdp.Core</c> and <c>Fdp.Toolkits</c> but <b>not</b> <c>Fdp.Presentation</c> (the dependency runs
/// <c>Fdp.Presentation → Fdp.Toolkits</c>, one way). ⇒ 🔒 <b>the split is forced by the reference graph,
/// not chosen.</b> ⭐ This is that registry's <c>Fdp.Presentation</c>-layer half, and the two together
/// are still ONE list per assembly layer — ⛔ not two lists for one concern.</para>
///
/// <para>📌 <b>What it replaced, measured 2026-08-28.</b> Three hosts each registered
/// <c>MapDisplayComponent</c> in their own registry — <c>CgfComponentRegistry:25</c>,
/// <c>IgRoleComponentRegistry:48</c>, <c>EditorSubsystem:973</c> — and <b>SimHost registered it
/// nowhere at all</b> (zero source references in the whole project). ⇒ 🔴 SimHost's TKB-built entities
/// carried no <c>MapDisplayComponent</c>, so the shared entity gizmos found nothing to draw and its map
/// showed <c>3</c> non-<c>Line</c> primitives against Scenario's <c>69</c>. ⭐ Three duplicates plus one
/// silent omission is exactly the drift a per-host list produces.</para>
///
/// <para>⚠ <b>Ordering:</b> like every component registration this must run <b>before</b>
/// <c>Kernel.Initialize()</c>, and before any translator that writes one of these types — 📌
/// <c>PresentationTkbTranslator</c> silently early-returns when its component is unregistered.</para>
/// </summary>
public static class MapPresentationRegistry
{
    /// <summary>
    /// Registers the map's presentation components into <paramref name="world"/>.
    /// </summary>
    /// <remarks>
    /// ⭐ Idempotent — <c>RegisterComponent</c> resolves to <c>GetOrCreate…</c>, so a host that reaches
    /// this twice (the editor registers into both its world and its pre-tick snapshot) is fine.
    /// </remarks>
    public static void RegisterAll(EntityRepository world)
    {
        // Written by MapLayerAssignmentSystem; read by the entity gizmos for layer culling.
        world.RegisterComponent<MapDisplayComponent>();
    }
}
