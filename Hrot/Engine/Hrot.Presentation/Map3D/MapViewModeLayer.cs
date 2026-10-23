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

    /// <summary>⭐ CE-1293 (docs/DESIGN_Unit_Camera.md UC-2) — the world this map draws: set by <c>AttachMapLayers</c>, so a unit's "Follow
    /// with the camera" (and <c>POST /view/follow</c>, through <see cref="ViewControl"/>) can follow its units. Null: following refuses.</summary>
    public Func<EntityRepository?>? World { get; set; }

    /// <summary>⭐ CE-1293 — the drawn bodies, so the follow camera's eyes are the drawn head's.</summary>
    public EntityBodyLayer3D? Bodies { get; set; }

    /// <summary>⭐ CE-1293 — the follow camera now driving the 3-D camera, or null.</summary>
    public FollowCameraDriver? Following => _switch.Camera3D.Driver as FollowCameraDriver;

    private EntityRepository? _registered;

    public void Update(float dt)
    {
        if (World?.Invoke() is { } w && !ReferenceEquals(w, _registered)) { ViewControl.Register(w, this); _registered = w; }
        foreach (ref readonly var _ in _bus.Read<ToggleMap3DEvent>())
            _switch.Toggle();
        foreach (ref readonly var _ in _bus.Read<ToggleShoulderViewEvent>())
            ToggleShoulder?.Invoke();
        foreach (ref readonly var follow in _bus.Read<FollowUnitEvent>())
            FollowUnit(follow.Target);
    }

    /// <summary>
    /// ⭐ CE-1293 (UC-2, UC-10) — the 3-D camera follows <paramref name="target"/> from <paramref name="distance"/> (default
    /// <see cref="FollowCameraDriver.StartDistance"/>), switching the map to 3-D. Returns why it refuses, or null: no such unit, or the
    /// station possesses one (that IS the view).
    /// </summary>
    public string? FollowUnit(Entity target, float? distance = null)
    {
        if (World is not { } world || world() is not { } w || target.IsNull || !w.IsAlive(target) || !w.HasComponent<SimTransform>(target))
            return "No such unit on this map.";
        if (_switch.Camera3D.Driver is FpsCameraDriver)
            return "The station possesses a unit: its view is his eyes. Release it first.";
        _switch.Set(true);
        _switch.Camera3D.Driver = new FollowCameraDriver(world, target, Bodies,
            Math.Clamp(distance ?? FollowCameraDriver.StartDistance, 0f, FollowCameraDriver.MaxDistance));
        return null;
    }

    /// <summary>⭐ CE-1293 — the camera back to the operator, when it follows a unit.</summary>
    public void StopFollowing()
    {
        if (Following != null) _switch.Camera3D.Driver = null;
    }

    public void Draw(RenderContext ctx) { }

    /// <summary>Nothing to draw in either mode — marked so the canvas does not count it as a layer missing from 3-D.</summary>
    public bool Has3D => true;
    public void Draw3D(RenderContext ctx) { }
    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;
}
