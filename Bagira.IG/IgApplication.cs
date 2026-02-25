using System.Numerics;
using Raylib_cs;
using rlImGui_cs;
using FDP.Toolkit.Vis2D;
using FDP.Toolkit.Vis2D.Components;
using FDP.Toolkit.Vis2D.Defaults;

namespace Bagira.IG;

/// <summary>
/// Main application shell for the IG Mock. Owns the Raylib window, MapCanvas, and camera.
/// </summary>
public class IgApplication
{
    // --- Window constants ---
    public const int    WindowWidth  = 1600;
    public const int    WindowHeight = 900;
    public const int    TargetFps    = 60;
    public const string WindowTitle  = "IG Mock";

    // --- Debug overlay layout ---
    private const int DebugFontSize   = 18;
    private const int DebugLineHeight = 22;
    private const int DebugMarginX    = 10;
    private const int DebugMarginY    = 10;

    // --- Runtime state ---
    private MapCanvas _canvas = null!;
    private MapCamera _camera = null!;

    /// <summary>
    /// Tracks the camera target set by arrow-key panning.
    /// Maintained separately from MapCamera._targetTarget so that mouse-drag pan
    /// and keyboard pan do not fight each other.
    /// </summary>
    private Vector2 _keyboardPanTarget;

    // -------------------------------------------------------------------------

    public void Initialize()
    {
        Raylib.InitWindow(WindowWidth, WindowHeight, WindowTitle);
        Raylib.SetTargetFPS(TargetFps);

        rlImGui.Setup(darkTheme: true);

        _camera = new MapCamera
        {
            MinZoom   = IgCameraConstants.MinZoom,
            MaxZoom   = IgCameraConstants.MaxZoom,
            ZoomSpeed = IgCameraConstants.ZoomSpeedPerTick
        };

        // Centre the camera over the initial world position.
        _camera.Target = new Vector2(IgCameraConstants.InitialPositionX, IgCameraConstants.InitialPositionY);
        _camera.Zoom   = IgCameraConstants.InitialZoom;
        // Offset keeps the world origin centred in the window.
        _camera.Offset = new Vector2(WindowWidth / 2f, WindowHeight / 2f);

        _keyboardPanTarget = new Vector2(
            IgCameraConstants.InitialPositionX,
            IgCameraConstants.InitialPositionY);

        _canvas        = new MapCanvas(new RaylibInputProvider());
        _canvas.Camera = _camera;
    }

    // -------------------------------------------------------------------------

    public void Run()
    {
        while (!Raylib.WindowShouldClose())
        {
            float dt = Raylib.GetFrameTime();

            HandleCameraInput(dt);
            _canvas.Update(dt);

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.DarkGray);
            _canvas.Draw();
            DrawDebugOverlay();
            Raylib.EndDrawing();
        }
    }

    // -------------------------------------------------------------------------

    /// <summary>
    /// Processes keyboard camera controls (arrow-key pan, +/- zoom).
    /// Middle-mouse drag pan is handled automatically by MapCanvas/MapCamera.
    /// Mouse-wheel zoom is also handled by MapCanvas/MapCamera via RaylibInputProvider.
    /// </summary>
    private void HandleCameraInput(float dt)
    {
        // --- Arrow-key panning ---
        // panDir is in screen space: Up arrow = -Y (screen Y goes down),
        // which scrolls the view upward as the user expects.
        Vector2 panDir = Vector2.Zero;
        if (Raylib.IsKeyDown(KeyboardKey.Right)) panDir.X += 1f;
        if (Raylib.IsKeyDown(KeyboardKey.Left))  panDir.X -= 1f;
        if (Raylib.IsKeyDown(KeyboardKey.Up))    panDir.Y -= 1f; // screen-up = -Y
        if (Raylib.IsKeyDown(KeyboardKey.Down))  panDir.Y += 1f; // screen-down = +Y

        if (panDir != Vector2.Zero)
        {
            // Accumulate displacement into our tracked target so that multiple
            // consecutive key frames add up correctly, even while the camera is
            // still interpolating toward a prior target.
            _keyboardPanTarget +=
                panDir * IgCameraConstants.ArrowKeyPanSpeedMetersPerSecond * dt;
            _camera.FocusOn(_keyboardPanTarget);
        }
        else
        {
            // Re-sync anchor to current interpolated camera position whenever
            // no arrow key is held, so the next key-press continues from wherever
            // the user has navigated (including via mouse drag).
            _keyboardPanTarget = _camera.Target;
        }

        // --- Keyboard zoom (+/=  and  -  keys) ---
        // Simulate a single wheel tick so the same 1.2× factor is applied.
        bool zoomIn  = Raylib.IsKeyPressed(KeyboardKey.Equal)
                    || Raylib.IsKeyPressed(KeyboardKey.KpAdd);
        bool zoomOut = Raylib.IsKeyPressed(KeyboardKey.Minus)
                    || Raylib.IsKeyPressed(KeyboardKey.KpSubtract);

        Vector2 mousePos = Raylib.GetMousePosition();
        if (zoomIn)  _camera.ProcessInput(1.0f,  mousePos, false, false);
        if (zoomOut) _camera.ProcessInput(-1.0f, mousePos, false, false);
    }

    // -------------------------------------------------------------------------

    /// <summary>Draws camera state and cursor coordinates in screen space (outside Camera.BeginMode).</summary>
    private void DrawDebugOverlay()
    {
        Vector2 worldMousePos = _camera.ScreenToWorld(Raylib.GetMousePosition());

        int y = DebugMarginY;

        Raylib.DrawText(
            $"Camera: ({_camera.Target.X:F1}, {_camera.Target.Y:F1}) m",
            DebugMarginX, y, DebugFontSize, Color.White);
        y += DebugLineHeight;

        Raylib.DrawText(
            $"Zoom: {_camera.Zoom:F4} px/m  ({1f / _camera.Zoom:F2} m/px)",
            DebugMarginX, y, DebugFontSize, Color.White);
        y += DebugLineHeight;

        Raylib.DrawText(
            $"Mouse World: ({worldMousePos.X:F1}, {worldMousePos.Y:F1}) m",
            DebugMarginX, y, DebugFontSize, Color.White);
    }

    // -------------------------------------------------------------------------

    public void Shutdown()
    {
        rlImGui.Shutdown();
        Raylib.CloseWindow();
    }
}
