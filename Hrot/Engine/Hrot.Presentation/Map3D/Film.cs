using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Raylib_cs;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐ <c>CE-3289</c> (docs/DESIGN_Map_3D_Mode.md §6ab, R-3032) — what the film camera looks at: a fixed <see cref="Pose"/>, or a unit it
/// <see cref="Follow"/>s (its network id) from <see cref="Distance"/> at <see cref="Yaw"/> / <see cref="Pitch"/>, looking at its position
/// raised by <see cref="Lift"/>. Angles in radians, as <see cref="CameraPose"/> (yaw from east counter-clockwise, pitch negative down).
/// </summary>
public sealed record FilmShot
{
    public CameraPose? Pose { get; init; }
    public long Follow { get; init; }
    public float Distance { get; init; } = 30f;
    public float Yaw { get; init; }
    public float Pitch { get; init; } = -0.35f;
    public float Lift { get; init; } = 1f;

    /// <summary>The vertical field of view (degrees); null keeps the camera's (45°). Narrow = a telephoto lens.</summary>
    public float? FovDeg { get; init; }

    /// <summary>A camera at <paramref name="eye"/> looking at <paramref name="target"/>; null when the two coincide.</summary>
    public static FilmShot? Looking(Vector3 eye, Vector3 target)
    {
        var d = target - eye;
        float len = d.Length();
        if (!(len > 1e-3f)) return null;
        return new FilmShot { Pose = new CameraPose(target, len, MathF.Atan2(d.Y, d.X), MathF.Asin(Math.Clamp(d.Z / len, -1f, 1f))) };
    }
}

/// <summary>A capture asked for: the PNG's path and size; <see cref="Supersample"/> renders that many times larger and scales down.
/// ⭐ CE-4038 B8/B9 — <see cref="Caption"/> is burned in under the picture; a <see cref="Card"/> is a title card (its text on black) instead
/// of the scene. ⭐ CE-1305 — <see cref="Graphics"/> are the film graphics burned over the scene (under the subtitle), laid out as the screen lays them out.</summary>
public sealed record FilmCaptureRequest(string Path, int Width, int Height, int Supersample = 1, Director.SubtitleFrame? Subtitle = null, string? Card = null,
                                        IReadOnlyList<Director.GraphicFrame>? Graphics = null);

/// <summary>A captured frame: where it was written, its size, and the sim time it shows.</summary>
public readonly record struct FilmFrame(string Path, int Width, int Height, double SimTime);

/// <summary>
/// ⭐ <c>CE-3289</c> (§6ab) — the film's mailbox between the HTTP thread and the maps, like <see cref="RenderTelemetry"/>: the shot the
/// director holds (null = released) and the captures waiting for a drawn frame. One per process (the window). Any thread.
/// </summary>
public static class FilmStudio
{
    private static readonly object Gate = new();
    private static readonly Queue<(FilmCaptureRequest Request, TaskCompletionSource<FilmFrame> Done)> Waiting = new();
    private static FilmShot? _shot;
    private static float _fade;

    /// <summary>The shot the director holds, or null when the view is the operator's.</summary>
    public static FilmShot? Shot { get { lock (Gate) return _shot; } }

    /// <summary>Holds the view on <paramref name="shot"/> from the next frame.</summary>
    public static void Direct(FilmShot shot) { lock (Gate) _shot = shot ?? throw new ArgumentNullException(nameof(shot)); }

    /// <summary>
    /// ⭐ <c>T4</c> / TE-8 — the black over the picture (0 = none, 1 = full), set by the director from <c>ProgramAt</c>'s dip. Drawn over the
    /// live picture by the cinema overlay and over every captured scene frame by the film map's capture — one value, both consumers.
    /// </summary>
    public static float Fade { get { lock (Gate) return _fade; } set { lock (Gate) _fade = Math.Clamp(value, 0f, 1f); } }

    private static bool _twoD, _animateViewSwitch = true;
    private static IReadOnlyList<Director.GraphicFrame> _graphics = Array.Empty<Director.GraphicFrame>();

    /// <summary>
    /// ⭐ <c>CE-1304</c> P6 (docs/DESIGN_Director_Timeline_Editor.md §11t V5) — the PROGRAM view's map: true = the 2-D map, false (the default) = the 3-D one. Set by the
    /// director from the take's View track; honoured by the map on screen while it holds the film camera (<see cref="FilmCaptureLayer"/>: in 2-D the map is
    /// centred on the shot's look-at at the shot's scale). Cleared by <see cref="Release"/>.
    /// </summary>
    public static bool TwoD { get { lock (Gate) return _twoD; } set { lock (Gate) _twoD = value; } }

    /// <summary>⭐ CE-1304 P6 (§11t V5) — the map's 2-D ⇄ 3-D switch animates (the existing animated switch) — false: it cuts (a capture: its frames are film frames).</summary>
    public static bool AnimateViewSwitch { get { lock (Gate) return _animateViewSwitch; } set { lock (Gate) _animateViewSwitch = value; } }

    /// <summary>
    /// ⭐ <c>CE-1305</c> P6 (§11t G3) — the film graphics on screen now (world points resolved by the director this frame), drawn over the map by its screen pass
    /// (<see cref="FilmCaptureLayer"/>) through <see cref="FilmText.Graphics"/> — the painter a captured frame uses too. Cleared by <see cref="Release"/>.
    /// </summary>
    public static IReadOnlyList<Director.GraphicFrame> Graphics
    {
        get { lock (Gate) return _graphics; }
        set { lock (Gate) _graphics = value ?? Array.Empty<Director.GraphicFrame>(); }
    }

    /// <summary>Gives the view back (the free camera keeps the last pose; the black clears; ⭐ CE-1304 / CE-1305 — the 3-D map and no graphics again).</summary>
    public static void Release() { lock (Gate) { _shot = null; _fade = 0f; _twoD = false; _graphics = Array.Empty<Director.GraphicFrame>(); } }

    private static bool _pilot;

    /// <summary>
    /// ⭐ <c>CE-1303</c> P2b (docs/DESIGN_Director_Timeline_Editor.md §11o S11) — the PILOT view: while true the maps give the camera back to the operator
    /// (it stays where the program left it, and flies freely) even though the director still holds a shot — the program keeps playing underneath, and
    /// "update key from view" takes what the operator flew. False (the default) = the PROGRAM view. ⛔ A capture always films the program (the runner
    /// clears it).
    /// </summary>
    public static bool Pilot { get { lock (Gate) return _pilot; } set { lock (Gate) _pilot = value; } }

    /// <summary>Asks for the next drawn frame's scene; completes when it is written (or faults with why it could not be).</summary>
    public static Task<FilmFrame> Capture(FilmCaptureRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var done = new TaskCompletionSource<FilmFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Gate) Waiting.Enqueue((request, done));
        return done.Task;
    }

    /// <summary>The oldest capture still wanted (one a caller gave up on is skipped).</summary>
    public static bool TryTake(out FilmCaptureRequest request, out TaskCompletionSource<FilmFrame> done)
    {
        lock (Gate)
        {
            while (Waiting.Count > 0)
            {
                var next = Waiting.Dequeue();
                if (next.Done.Task.IsCompleted) continue;
                (request, done) = next;
                return true;
            }
        }
        request = null!;
        done = null!;
        return false;
    }

    /// <summary>For tests: no shot, nothing waiting.</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _shot = null;
            _fade = 0f;
            _pilot = false;
            _twoD = false;
            _animateViewSwitch = true;
            _graphics = Array.Empty<Director.GraphicFrame>();
            while (Waiting.Count > 0) Waiting.Dequeue().Done.TrySetCanceled();
        }
    }
}

/// <summary>
/// ⭐ <c>CE-3289</c> (§6ab) — the film's camera: a <see cref="ICameraDriver"/> like first person (so the operator's camera and the
/// scenario's saved camera are left alone — <c>MapCameraLayer</c> never saves a driven view), but it looks AT a point
/// (<see cref="ViewsFromEye"/> false: the weather read there, fog starting part way, as the free camera). Input is not its business.
/// </summary>
public sealed class FilmDirector : ICameraDriver
{
    private readonly Func<EntityRepository?> _world;

    public FilmDirector(Func<EntityRepository?> world) => _world = world ?? throw new ArgumentNullException(nameof(world));

    /// <summary>The shot it holds.</summary>
    public FilmShot? Shot { get; set; }

    public bool ViewsFromEye => false;

    private float? _fovBefore;

    public void Update(MapCamera3D camera, float dt)
    {
        // ⭐ CE-4038 — the newest shot, read when the camera updates: a cut lands THIS frame, before the layers (the weather) read the
        //   camera — so a new shot's first frame needs no throwaway settle capture any more
        if (FilmStudio.Shot is { } latest) Shot = latest;
        Place(camera);
    }

    /// <summary>Gives the lens back: the field of view the camera had before a shot changed it.</summary>
    public void Detached(MapCamera3D camera)
    {
        if (_fovBefore is { } fov) camera.FovyDegrees = fov;
        _fovBefore = null;
    }

    public bool HandleInput(MapCamera3D camera, IInputProvider input) => false;

    /// <summary>Puts the camera on the shot; false (the camera left where it was) when there is none or the followed unit is not here.</summary>
    public bool Place(MapCamera3D camera)
    {
        if (Shot is not { } shot) return false;
        _fovBefore ??= camera.FovyDegrees;
        camera.FovyDegrees = shot.FovDeg is { } fov ? Math.Clamp(fov, 1f, 120f) : _fovBefore.Value;
        if (shot.Pose is { } pose) { camera.Pose = pose; return true; }
        var world = _world();
        var unit = NetworkIdResolver.ResolveNetworkId(world, shot.Follow);
        if (world == null || unit.IsNull || !world.HasComponent<SimTransform>(unit)) return false;
        var at = world.GetComponentRO<SimTransform>(unit).Position + new Vector3(0f, 0f, shot.Lift);
        camera.Pose = new CameraPose(at, shot.Distance, shot.Yaw, shot.Pitch);
        return true;
    }
}

/// <summary>
/// ⭐ <c>CE-3289</c> (docs/DESIGN_Map_3D_Mode.md §6ab, R-3032) — films the map: holds the camera on the <see cref="FilmStudio"/>'s shot
/// (through <see cref="FilmDirector"/>) and serves its captures — the scene ALONE (<see cref="MapCanvas.DrawScene"/>: the sky and the
/// <see cref="ISceneLayer"/>s, no gizmos, cards or HUD, and none of the window's panels) into a render texture, written as a PNG.
/// <para>⭐ Only a map the window DREW last frame serves a capture: in <c>--mode all</c> several hosts own a map in one process, and the
/// film is of the one on screen. ⭐ It runs in <see cref="Update"/>, outside the window's <c>BeginDrawing</c>, so its offscreen pass never
/// disturbs the frame. Every map host gets it (<c>MapInteractionPack.AttachMapLayers</c>).</para>
/// </summary>
public sealed class FilmCaptureLayer : IMapLayer, IDisposable
{
    /// <summary>The largest frame side asked for (px), supersampling included.</summary>
    public const int MaxSide = 4096;

    private readonly MapCanvas _canvas;
    private readonly MapViewSwitch _view;
    private readonly Func<EntityRepository?> _world;
    private readonly FilmDirector _director;
    private RenderTexture2D _target;
    private bool _drawn;

    public FilmCaptureLayer(MapCanvas canvas, MapViewSwitch view, Func<EntityRepository?> world)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _director = new FilmDirector(world);
    }

    public string Name => "Film";
    public int LayerBitIndex => -1;
    public bool Has3D => true;

    /// <summary>The director this map's camera is handed while a shot is held.</summary>
    public FilmDirector Director => _director;

    /// <summary>Captures served by this map (rails).</summary>
    public int Served { get; private set; }

    public void Update(float dt)
    {
        bool drawn = _drawn;
        _drawn = false;
        Direct();
        if (!drawn) return;
        while (FilmStudio.TryTake(out var request, out var done)) Serve(request, done);
    }

    private bool _directing, _capturing;

    /// <summary>
    /// Hands the camera to the director while a shot is held, back when released. ⭐ <c>CE-1304</c> P6 (docs/DESIGN_Director_Timeline_Editor.md §11t V5) — the
    /// program's MAP is <see cref="FilmStudio.TwoD"/>'s: in 3-D the film camera drives the 3-D camera (as before); in 2-D the map is put in 2-D through the ONE
    /// switch (<see cref="MapViewSwitch"/>, animated unless <see cref="FilmStudio.AnimateViewSwitch"/> is off) and held on the shot — its look-at the centre,
    /// its distance the scale (<see cref="MapViewSwitch.ShowPose"/> of the overhead pose). The first held frame of a film snaps, as before.
    /// </summary>
    private void Direct()
    {
        var camera = _view.Camera3D;
        if (FilmStudio.Shot is { } shot && !FilmStudio.Pilot)   // ⭐ CE-1303 P2b — the pilot view leaves the camera to the operator
        {
            _director.Shot = shot;
            bool animate = _directing && FilmStudio.AnimateViewSwitch;
            _directing = true;
            if (_view.IsSwitching) return;   // ⚠ the switch finishes its move through the camera's own Update — no driver may hold it
            if (FilmStudio.TwoD)
            {
                if (ReferenceEquals(camera.Driver, _director)) camera.Driver = null;   // the 2-D map has no driver
                if (_view.Is3D) _view.Set(false, animate);
                else if (_director.Place(camera))
                    _view.ShowPose(camera.Pose with { Yaw = MapCamera3D.NorthUpYaw, Pitch = MapCamera3D.OverheadPitch });
                return;
            }
            if (!_view.Is3D)
            {
                camera.Driver = null;
                _view.Set(true, animate);
                if (_view.IsSwitching) return;
            }
            if (!ReferenceEquals(camera.Driver, _director)) camera.Driver = _director;
        }
        else
        {
            _directing = false;
            if (ReferenceEquals(camera.Driver, _director)) camera.Driver = null;
        }
    }

    /// <summary>World → this map's pixels: the 3-D camera's projection (null behind it) or the 2-D map's.</summary>
    private Func<Vector3, Vector2?> Projector()
    {
        if (_view.Is3D)
        {
            var cam = _view.Camera3D;
            return p => cam.WorldToScreen(p) is var s && s.X > -1e5f ? s : null;
        }
        var cam2 = _canvas.Camera;
        return p => cam2.WorldToScreen(new Vector2(p.X, p.Y));
    }

    private void Serve(FilmCaptureRequest request, TaskCompletionSource<FilmFrame> done)
    {
        try
        {
            // ⭐ CE-1304 P6 (§11t V6) — a 2-D map is filmed too (the 2-D map as the screen draws it, its layers the take's); ⛔ not half way through the switch
            if (_view.IsSwitching && request.Card == null) throw new InvalidOperationException("the map is switching between 2-D and 3-D — capture when it has landed");
            int ss = Math.Clamp(request.Supersample, 1, 4);
            int w = request.Width * ss, h = request.Height * ss;
            if (request.Width < 16 || request.Height < 16 || w > MaxSide || h > MaxSide)
                throw new ArgumentOutOfRangeException(nameof(request), $"a frame of {request.Width}×{request.Height} ×{ss} — each side 16…{MaxSide} px");
            var camera = _view.Camera3D;
            if (ReferenceEquals(camera.Driver, _director)) _director.Place(camera);   // the shot may be newer than this frame's camera update
            var full = System.IO.Path.GetFullPath(request.Path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);

            if (_target.Id == 0 || _target.Texture.Width != w || _target.Texture.Height != h)
            {
                if (_target.Id != 0) Raylib.UnloadRenderTexture(_target);
                _target = Raylib.LoadRenderTexture(w, h);
            }
            var viewport = camera.ViewportOverride;
            camera.ViewportOverride = new Vector2(w, h);   // the projection and every pixel-sized thing, at the frame's size
            var camera2D = _view.Is3D ? null : _canvas.Camera;
            var view2D = camera2D?.GetCameraView();
            try
            {
                _capturing = true;
                if (camera2D != null && view2D is { } v2)
                {
                    // ⭐ CE-1304 P6 (§11t V6) — the 2-D map at the frame's size: the screen's centre at the frame's centre, the same ground height in view
                    var screen = new Vector2(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());
                    var centre = camera2D.ScreenToWorld(screen / 2f);
                    float zoom = v2.Zoom * h / MathF.Max(1f, screen.Y);
                    camera2D.ApplyCameraView(new MapCameraView { Target = centre, Offset = new Vector2(w / 2f, h / 2f), Zoom = zoom, SmoothTarget = centre, SmoothZoom = zoom });
                }
                Raylib.BeginTextureMode(_target);
                Raylib.ClearBackground(Color.Black);
                if (request.Card != null) FilmText.Card(request.Card, w, h);
                else
                {
                    if (camera2D != null) _canvas.Draw(); else _canvas.DrawScene();
                    // ⭐ CE-1305 (§11t G3) — the film graphics, laid out through THIS frame's camera, under the subtitle
                    if (request.Graphics is { Count: > 0 } graphics) FilmText.Graphics(graphics, Projector(), h);
                    if (request.Subtitle is { } sub && !string.IsNullOrWhiteSpace(sub.Text)) FilmText.Caption(sub, w, h);
                    if (FilmStudio.Fade is > 0f and var fade) Raylib.DrawRectangle(0, 0, w, h, new Color((byte)0, (byte)0, (byte)0, (byte)Math.Round(fade * 255f)));
                }
                Raylib.EndTextureMode();
            }
            finally
            {
                _capturing = false;
                camera.ViewportOverride = viewport;
                if (camera2D != null && view2D is { } restore) camera2D.ApplyCameraView(restore);
            }

            var image = Raylib.LoadImageFromTexture(_target.Texture);
            try
            {
                Raylib.ImageFlipVertical(ref image);   // GL's rows run bottom-up
                if (ss > 1) Raylib.ImageResize(ref image, request.Width, request.Height);
                if (!Raylib.ExportImage(image, full)) throw new IOException($"could not write {full}");
            }
            finally { Raylib.UnloadImage(image); }

            double simTime = _world() is { } world ? Fdp.Toolkit.Time.SimClock.Of(world).TotalTime : 0d;
            Served++;
            done.TrySetResult(new FilmFrame(full, request.Width, request.Height, simTime));
        }
        catch (Exception ex)
        {
            done.TrySetException(ex);
        }
    }

    public void Draw(RenderContext ctx) => _drawn = true;
    public void Draw3D(RenderContext ctx) => _drawn = true;
    /// <summary>⭐ <c>CE-1305</c> P6 (§11t G3) — the film graphics on screen, over the finished map (2-D or 3-D), through the painter a captured frame uses.</summary>
    public void DrawOverlay(RenderContext ctx)
    {
        if (_capturing || FilmStudio.Graphics is not { Count: > 0 } graphics) return;
        FilmText.Graphics(graphics, Projector(), _view.Camera3D.Viewport.Y);
    }

    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;

    public void Dispose()
    {
        if (_target.Id != 0) Raylib.UnloadRenderTexture(_target);
        _target = default;
        if (ReferenceEquals(_view.Camera3D.Driver, _director)) _view.Camera3D.Driver = null;
    }
}

/// <summary>
/// ⭐ <c>CE-4038</c> B9 (docs/DESIGN_Film_Director.md D-6) — the director's words on the picture, the same on screen and burned into a frame:
/// a caption in a dark band under the picture, a title card centred on black. Sized from the frame's height.
/// </summary>
public static class FilmText
{
    /// <summary>
    /// A subtitle on a <paramref name="w"/>×<paramref name="h"/> picture — at its position (top / middle / bottom) and opacity (its fade), exactly as
    /// the cinema overlay draws it: the band's geometry is <see cref="Director.SubtitleBand"/> (⭐ CE-1370), shared with <c>WindowCinema</c>.
    /// </summary>
    public static void Caption(Director.SubtitleFrame sub, int w, int h)
    {
        int size = (int)Director.SubtitleBand.TextSizeFor(h);
        var lines = Wrap(sub.Text, size, (int)(w * 0.86f));
        int lineH = (int)(size * 1.25f);
        var band = Director.SubtitleBand.Of(sub, h, lineH * lines.Count);
        Raylib.DrawRectangle(0, (int)band.Top, w, (int)band.Height, new Color((byte)0, (byte)0, (byte)0, band.BandAlpha));
        for (int i = 0; i < lines.Count; i++)
        {
            int tw = Raylib.MeasureText(lines[i], size);
            Raylib.DrawText(lines[i], (w - tw) / 2, (int)band.Top + size / 2 + i * lineH, size, new Color((byte)245, (byte)245, (byte)245, band.TextAlpha));
        }
    }

    /// <summary>A title card: each line of <paramref name="text"/> centred, the first one larger.</summary>
    public static void Card(string text, int w, int h)
    {
        Raylib.DrawRectangle(0, 0, w, h, Color.Black);
        var lines = text.Split('\n');
        int big = Math.Max(16, h / 14), small = Math.Max(12, h / 24);
        int total = 0;
        for (int i = 0; i < lines.Length; i++) total += (int)((i == lines.Length - 1 ? big : small) * 1.4f);
        int y = (h - total) / 2;
        for (int i = 0; i < lines.Length; i++)
        {
            int size = i == lines.Length - 1 ? big : small;
            var color = i == lines.Length - 1 ? Color.RayWhite : new Color(200, 200, 200, 255);
            int tw = Raylib.MeasureText(lines[i], size);
            Raylib.DrawText(lines[i], (w - tw) / 2, y, size, color);
            y += (int)(size * 1.4f);
        }
    }

    /// <summary>
    /// ⭐ <c>CE-1305</c> P6 (docs/DESIGN_Director_Timeline_Editor.md §11t G3) — the film graphics on a picture <paramref name="h"/> pixels tall, each laid out by
    /// <see cref="Director.GraphicLayout"/> through <paramref name="project"/> (the camera drawing the picture): a translucent fill, the line, the arrow head,
    /// the text on a dark box. ONE painter for the map's screen pass and a captured frame (the <see cref="Caption"/> pattern, CE-1370).
    /// </summary>
    public static void Graphics(IReadOnlyList<Director.GraphicFrame> graphics, Func<Vector3, Vector2?> project, float h)
    {
        Rlgl.DisableBackfaceCulling();   // a projected triangle's winding is whatever the camera makes it
        try
        {
            foreach (var g in graphics)
            {
                if (Director.GraphicLayout.Of(g, project, h) is not { } plot || plot.Alpha == 0) continue;
                byte r = (byte)(plot.Colour >> 16), gr = (byte)(plot.Colour >> 8), b = (byte)plot.Colour;
                var colour = new Color(r, gr, b, plot.Alpha);
                var fill = new Color(r, gr, b, (byte)(plot.Alpha / 4));
                for (int i = 0; i + 2 < plot.Fill.Count; i += 3) Raylib.DrawTriangle(plot.Fill[i], plot.Fill[i + 1], plot.Fill[i + 2], fill);
                int n = plot.Line.Count;
                for (int i = 0; i + 1 < n; i++) Raylib.DrawLineEx(plot.Line[i], plot.Line[i + 1], plot.Thickness, colour);
                if (plot.Closed && n > 2) Raylib.DrawLineEx(plot.Line[n - 1], plot.Line[0], plot.Thickness, colour);
                if (plot.Head.Count == 3) Raylib.DrawTriangle(plot.Head[0], plot.Head[1], plot.Head[2], colour);
                if (plot.TextAt is { } at && !string.IsNullOrEmpty(plot.Text))
                {
                    int size = (int)plot.TextSize;
                    int tw = Raylib.MeasureText(plot.Text, size);
                    int pad = size / 3;
                    Raylib.DrawRectangle((int)(at.X - tw / 2f) - pad, (int)(at.Y - size / 2f) - pad, tw + 2 * pad, size + 2 * pad,
                                         new Color((byte)0, (byte)0, (byte)0, (byte)(plot.Alpha * 0.6f)));
                    Raylib.DrawText(plot.Text, (int)(at.X - tw / 2f), (int)(at.Y - size / 2f), size, colour);
                }
            }
        }
        finally { Rlgl.EnableBackfaceCulling(); }
    }

    private static List<string> Wrap(string text, int size, int width)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && Raylib.MeasureText(next, size) > width) { lines.Add(line); line = word; }
            else line = next;
        }
        if (line.Length > 0) lines.Add(line);
        return lines;
    }
}
