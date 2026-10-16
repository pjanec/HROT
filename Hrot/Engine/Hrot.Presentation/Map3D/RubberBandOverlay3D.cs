using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Hrot.ScenarioEditor.Gizmos;
using Raylib_cs;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐⭐ CE-1136 (docs/DESIGN_Map_3D_Mode.md §6z) — the 3-D map's multi-selection marquee: while the map shows 3-D it lends the shared
/// <see cref="RubberBandState"/> the camera's world → screen projection (so <c>SelectionInteractionSystem</c> selects what projects inside
/// the dragged SCREEN rectangle), and draws that rectangle over the view. In 2-D it takes the projection away — the band stays the
/// ground rectangle the 2-D map draws. 🔒 User, 2026-10-16: <i>"the 3d map has no multi-selection marquee, one would be very useful."</i>
/// The state is the one the map's selection system holds (<c>MapInteractionPack.Build</c> keys it to the world, <c>MapUiStates</c>).
/// </summary>
public sealed class RubberBandOverlay3D : IMapLayer
{
    private static readonly Color Fill = new(0, 120, 215, 50);
    private static readonly Color Border = new(0, 120, 215, 220);
    private readonly MapViewSwitch _view;
    private readonly Func<EntityRepository?> _world;

    public RubberBandOverlay3D(MapViewSwitch view, Func<EntityRepository?> world)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    public string Name => "RubberBand3D";
    public int LayerBitIndex => -1;

    /// <summary>The band this layer serves (the world's), or null.</summary>
    public RubberBandState? State
        => Hrot.ScenarioEditor.Map.MapUiStates.BandOf(_world());

    public void Update(float dt)
    {
        if (State is not { } band) return;
        var camera = _view.Camera3D;
        band.Projector = _view.Is3D && camera.Driver == null ? p => Project(camera, p) : null;   // ⛔ not in first person
    }

    /// <summary>A world point's pixel through <paramref name="camera"/>, or null behind the eye.</summary>
    public static Vector2? Project(MapCamera3D camera, Vector3 world)
    {
        if (Vector3.Dot(world - camera.Eye, camera.Forward) <= 0.05f) return null;
        var s = camera.WorldToScreen(world);
        return float.IsFinite(s.X) && float.IsFinite(s.Y) ? s : null;
    }

    public void Draw(RenderContext ctx) { }
    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;

    public void DrawOverlay(RenderContext ctx)
    {
        if (!_view.Is3D || State is not { IsActive: true, InScreen: true } band) return;
        var min = Vector2.Min(band.ScreenStart, band.ScreenCurrent);
        var max = Vector2.Max(band.ScreenStart, band.ScreenCurrent);
        var rect = new Rectangle(min.X, min.Y, max.X - min.X, max.Y - min.Y);
        Raylib.DrawRectangleRec(rect, Fill);
        Raylib.DrawRectangleLinesEx(rect, 1f, Border);
    }
}
