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
        if (World?.Invoke() is { } world) Refollow(world);
    }

    private int _seenVersion = -1;
    private bool _awaitingReturn;

    /// <summary>
    /// ⭐ <c>CE-1329</c> (Brain View §12r BV-65) — the followed unit is REMEMBERED in the one per-unit store (by network id, per scenario):
    /// when it comes back — after a preview's rewind, a respawn, or a scenario load whose remembered settings name it — the camera follows it
    /// again. ⚠ Only then: a camera the film or the station took is never pulled back while the unit is still there.
    /// </summary>
    private void Refollow(EntityRepository world)
    {
        if (Fdp.Toolkit.Behavior.Diagnostics.UnitDebugSettings.Peek(world) is not { } store) return;
        if (store.Version != _seenVersion)
        {
            _seenVersion = store.Version;
            if (store.Followed is { } want && (Following is not { } f || Fdp.Toolkit.Behavior.Diagnostics.UnitDebugSettings.NetIdOf(world, f.Unit) != want))
                _awaitingReturn = true;   // loaded / set elsewhere while not followed: follow when it is there
        }
        if (!_awaitingReturn || store.Followed is not { } id || _switch.Camera3D.Driver != null) return;
        foreach (var e in world.Query().With<Fdp.Toolkit.Replication.Components.NetworkIdentity>().Build())
        {
            if (world.GetComponentRO<Fdp.Toolkit.Replication.Components.NetworkIdentity>(e).Value != id) continue;
            if (FollowUnit(e) == null) _awaitingReturn = false;
            return;
        }
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
        long id = Fdp.Toolkit.Behavior.Diagnostics.UnitDebugSettings.NetIdOf(w, target);
        var store = id != 0 ? Fdp.Toolkit.Behavior.Diagnostics.UnitDebugSettings.Of(w) : null;
        _switch.Camera3D.Driver = new FollowCameraDriver(world, target, Bodies,
            Math.Clamp(distance ?? FollowCameraDriver.StartDistance, 0f, FollowCameraDriver.MaxDistance))
        {
            // ⭐ CE-1329 — remembered: the operator letting go forgets it; the unit vanishing (a rewind, a respawn) waits for its return
            Released = () => store?.SetFollowed(null),
            Lost = () => _awaitingReturn = true,
        };
        if (store != null) { store.SetFollowed(id); _seenVersion = store.Version; }
        _awaitingReturn = false;
        return null;
    }

    /// <summary>⭐ CE-1293 — the camera back to the operator, when it follows a unit.</summary>
    public void StopFollowing()
    {
        if (Following is { } f && World?.Invoke() is { } w) Fdp.Toolkit.Behavior.Diagnostics.UnitDebugSettings.Peek(w)?.SetFollowed(null);   // ⭐ CE-1329
        if (Following != null) _switch.Camera3D.Driver = null;
        _awaitingReturn = false;
    }

    public void Draw(RenderContext ctx) { }

    /// <summary>Nothing to draw in either mode — marked so the canvas does not count it as a layer missing from 3-D.</summary>
    public bool Has3D => true;
    public void Draw3D(RenderContext ctx) { }
    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;
}
