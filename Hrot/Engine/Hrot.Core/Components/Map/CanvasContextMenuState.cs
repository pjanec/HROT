using Fdp.Core;
using Hrot.Map.Definitions;

namespace Hrot.IG.Components
{
    /// <summary>
    /// Managed singleton acting as the Single Source of Truth for the
    /// empty-map-space context menu.
    ///
    /// <para>Written each frame by <c>CanvasMenuUpdateSystem</c> (or a subsystem-specific
    /// variant). Read by <c>CanvasContextMenuGizmo</c>, which projects a
    /// <c>ContextMenuBinding</c> meta-primitive keyed by anchor ID <c>-1L</c>.</para>
    /// </summary>
    // ⭐ D7 (restore design) — content / service / index, never world state: a keyframe must not carry it, and an apply must
    //   not replace the loaded instance (R-3043).
    [DataPolicy(DataPolicy.NoScenario | DataPolicy.NoReplay)]
    [ComponentId(HrotComponentIds.CanvasContextMenuState)]
    public sealed class CanvasContextMenuState
    {
        public string MenuJson { get; set; } = string.Empty;
    }
}
