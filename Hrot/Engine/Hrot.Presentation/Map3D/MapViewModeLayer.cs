using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Hrot.Common.Diagnostics.Gizmos;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-1033 — the map's View › 2-D / 3-D Map, on every host: drains <see cref="ToggleMap3DEvent"/> (published by the shared
/// action registry when the menu item is clicked) and toggles the canvas' <see cref="MapViewSwitch"/>. A layer, so the canvas'
/// own per-frame <c>Update</c> drives it — no host scheduling to forget. Draws and consumes nothing.
/// </summary>
public sealed class MapViewModeLayer : IMapLayer
{
    private readonly MapViewSwitch _switch;
    private readonly FdpEventBus _bus;

    public MapViewModeLayer(MapViewSwitch viewSwitch, FdpEventBus bus)
    {
        _switch = viewSwitch ?? throw new ArgumentNullException(nameof(viewSwitch));
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    }

    public string Name => "Map view mode";
    public int LayerBitIndex => -1;

    /// <summary>⭐ CE-1201 (docs/DESIGN_Entity_Control.md §8p OS-1) — what View › First Person: Over the Shoulder does: wired by
    /// <c>AttachMapLayers</c> to the host's first-person station. Null: the item does nothing.</summary>
    public Action? ToggleShoulder { get; set; }

    /// <summary>⭐ CE-1293 (docs/DESIGN_Unit_Camera.md UC-2) — what a unit's "Follow with the camera" does: wired by <c>AttachMapLayers</c>
    /// to the 3-D camera. Null: the item does nothing.</summary>
    public Action<Entity>? Follow { get; set; }

    public void Update(float dt)
    {
        foreach (ref readonly var _ in _bus.Read<ToggleMap3DEvent>())
            _switch.Toggle();
        foreach (ref readonly var _ in _bus.Read<ToggleShoulderViewEvent>())
            ToggleShoulder?.Invoke();
        foreach (ref readonly var follow in _bus.Read<FollowUnitEvent>())
            Follow?.Invoke(follow.Target);
    }

    public void Draw(RenderContext ctx) { }

    /// <summary>Nothing to draw in either mode — marked so the canvas does not count it as a layer missing from 3-D.</summary>
    public bool Has3D => true;
    public void Draw3D(RenderContext ctx) { }
    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;
}
