using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Terrain;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Components;

namespace Hrot.UI.Common.Map2D;

/// <summary>
/// ⭐⭐ CE-1462 (W-B1; backend's note §3 ②) — <b>the 2-D map frames its terrain when it is loaded</b>, on EVERY host by construction: <c>MapInteractionPack.AttachMapLayers</c> adds this layer, so no host can lack it. Nothing framed a terrain
/// before: <c>GET /view</c>'s <c>rect2D</c> stayed at the default pixel rectangle, so <c>demo-city</c> only looked framed because it falls inside it and <c>cqb-village</c> (330 × 232 m) did not.
/// <para>Rule (<see cref="MapFraming.Framer"/>): the FIRST frame a terrain is resident, and again whenever <see cref="TerrainDefinition.ResidentName"/> changes (an unload forgets it, so the next load frames again) — the terrain's bounds plus a 5 % margin
/// (<see cref="MapFraming.Fit"/>). ⛔ Never otherwise: the user's pans and zooms win. ⛔ Never a camera that is not the 2-D map's right now — the 3-D map on screen, a followed unit, a possession station driving the view — that terrain is consumed unframed.
/// The 3-D camera has no framing of its own (measured: no terrain use in <c>MapCamera3D</c>); it takes the 2-D camera's pose when the map is switched to 3-D (<c>OverheadMatching</c>), so a framed 2-D map frames the first 3-D view too.</para>
/// </summary>
public sealed class TerrainFramingLayer : IMapLayer
{
    private readonly Func<EntityRepository?> _world;
    private readonly Func<MapCamera> _camera2D;
    private readonly Func<bool> _leaveAlone;
    private readonly Func<bool> _switching;
    private readonly MapFraming.Framer _framer = new();

    /// <summary>How many times it framed — a probe for the rails.</summary>
    public int Framings { get; private set; }

    /// <param name="camera2D">The 2-D map's camera (the one it frames).</param>
    /// <param name="leaveAlone">True while the 3-D map is on screen, a unit is followed or a station drives the view.</param>
    /// <param name="switching">True while the 2-D↔3-D switch is mid-flight.</param>
    public TerrainFramingLayer(Func<EntityRepository?> world, Func<MapCamera> camera2D, Func<bool> leaveAlone, Func<bool> switching)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _camera2D = camera2D ?? throw new ArgumentNullException(nameof(camera2D));
        _leaveAlone = leaveAlone ?? throw new ArgumentNullException(nameof(leaveAlone));
        _switching = switching ?? throw new ArgumentNullException(nameof(switching));
    }

    public string Name => "TerrainFraming";
    public int LayerBitIndex => -1;

    /// <summary>The terrain's ground bounds in <paramref name="world"/>, or null (none loaded, or it has no extent).</summary>
    public static (Vector2 Min, Vector2 Max)? BoundsOf(EntityRepository? world)
    {
        if (world is null || !world.HasSingletonManaged<TerrainWorld>() || world.GetSingletonManaged<TerrainWorld>() is not { } t) return null;
        return t.BoundsMax.X > t.BoundsMin.X && t.BoundsMax.Y > t.BoundsMin.Y ? (t.BoundsMin, t.BoundsMax) : null;
    }

    public void Update(float dt)
    {
        var w = _world();
        var bounds = BoundsOf(w);
        if (!_framer.Step(TerrainDefinition.ResidentName(w), bounds.HasValue, _switching(), _leaveAlone())) return;
        var camera = _camera2D();
        var area = OpenMapArea.Current is { } a ? new Vector2(a.W, a.H) : camera.Viewport;
        if (MapFraming.Fit(bounds!.Value.Min, bounds.Value.Max, area, camera.ViewCentre, MapFraming.TerrainMargin, camera.MinZoom, camera.MaxZoom) is not { } view) return;
        camera.ApplyCameraView(view);
        Framings++;
    }

    public void Draw(RenderContext ctx) { }
    public void DrawOverlay(RenderContext ctx) { }
    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;
}
