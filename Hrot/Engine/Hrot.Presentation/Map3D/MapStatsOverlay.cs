using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Hrot.ScenarioEditor.Map;
using Raylib_cs;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ CE-1145 / CE-1132 (docs/DESIGN_Map_3D_Mode.md §6z) — two small overlays in the VISIBLE map's top-left corner
/// (<see cref="OpenMapArea"/>): in 3-D a frame counter (<c>FPS 58 · 17.2 ms</c>), and while the RTS command tool is armed its controls
/// legend, with a red reticle at the cursor while Ctrl (attack) is held. It also feeds <see cref="RenderTelemetry"/> every frame.
/// 🔒 User, 2026-10-16: <i>"a small FPS indicator in the 3d map view's left upper corner would be nice"</i> · <i>"i need some cheat sheet
/// how to control it"</i> · <i>"changed the selection gizmo to 'attack target selection' shape while the modifier key kept pressed"</i>.
/// </summary>
public sealed class MapStatsOverlay : IMapLayer
{
    private readonly MapViewSwitch _view;
    private readonly Func<EntityRepository?> _world;

    public MapStatsOverlay(MapViewSwitch view, Func<EntityRepository?> world)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    public string Name => "MapStats";
    public int LayerBitIndex => -1;

    /// <summary>The text size of the overlays (px).</summary>
    public const int FontSize = 16;

    public void Update(float dt)
    {
        RenderTelemetry.Frame(dt);
        var cam = _view.Camera3D;
        string mode = !_view.Is3D ? "2D" : cam.Driver != null ? "FPS" : "3D";
        RenderTelemetry.Camera(mode, cam.Eye, cam.Forward, cam.Yaw, cam.Pitch, cam.Distance, cam.EyeHeightAboveGround(), cam.Viewport);
    }

    /// <summary>The counter's text.</summary>
    public static string CounterText()
    {
        var (fps, mean, _, _) = RenderTelemetry.Stats();
        return $"FPS {fps:0}  {mean:0.0} ms";
    }

    public void DrawOverlay(RenderContext ctx)
    {
        var viewport = _view.Camera3D.Viewport;
        float y = 0f;
        if (_view.Is3D)
        {
            var text = CounterText();
            var at = OpenMapArea.Corner(viewport, left: true, top: true, Raylib.MeasureText(text, FontSize), FontSize);
            Raylib.DrawRectangle((int)at.X - 4, (int)at.Y - 2, Raylib.MeasureText(text, FontSize) + 8, FontSize + 4, new Color(0, 0, 0, 120));
            Raylib.DrawText(text, (int)at.X, (int)at.Y, FontSize, new Color(170, 255, 170, 255));
            y = FontSize + 10;
        }
        if (MapUiStates.RtsOf(_world()) is { Armed: true } rts)
            DrawRtsLegend(viewport, y, rts);
    }

    private static void DrawRtsLegend(Vector2 viewport, float below, RtsModeState rts)
    {
        int width = 0;
        foreach (var line in RtsModeState.Legend) width = Math.Max(width, Raylib.MeasureText(line, FontSize));
        int height = RtsModeState.Legend.Length * (FontSize + 4);
        var at = OpenMapArea.Corner(viewport, left: true, top: true, width, height) + new Vector2(0f, below);
        Raylib.DrawRectangle((int)at.X - 6, (int)at.Y - 4, width + 12, height + 6, new Color(0, 0, 0, 140));
        for (int i = 0; i < RtsModeState.Legend.Length; i++)
        {
            bool attack = rts.ForceAttack && RtsModeState.Legend[i].StartsWith("Ctrl", StringComparison.Ordinal);
            var colour = i == 0 ? new Color(255, 210, 120, 255) : attack ? new Color(255, 90, 80, 255) : Color.RayWhite;
            Raylib.DrawText(RtsModeState.Legend[i], (int)at.X, (int)at.Y + i * (FontSize + 4), FontSize, colour);
        }
        if (rts.ForceAttack)
        {
            var m = Raylib.GetMousePosition();
            var red = new Color(255, 60, 50, 255);
            Raylib.DrawCircleLines((int)m.X, (int)m.Y, 14f, red);
            Raylib.DrawLine((int)m.X - 20, (int)m.Y, (int)m.X - 6, (int)m.Y, red);
            Raylib.DrawLine((int)m.X + 6, (int)m.Y, (int)m.X + 20, (int)m.Y, red);
            Raylib.DrawLine((int)m.X, (int)m.Y - 20, (int)m.X, (int)m.Y - 6, red);
            Raylib.DrawLine((int)m.X, (int)m.Y + 6, (int)m.X, (int)m.Y + 20, red);
        }
    }

    public void Draw(RenderContext ctx) { }
    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;
}
