using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
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
/// of the scene.</summary>
public sealed record FilmCaptureRequest(string Path, int Width, int Height, int Supersample = 1, string? Caption = null, string? Card = null);

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

    /// <summary>The shot the director holds, or null when the view is the operator's.</summary>
    public static FilmShot? Shot { get { lock (Gate) return _shot; } }

    /// <summary>Holds the view on <paramref name="shot"/> from the next frame.</summary>
    public static void Direct(FilmShot shot) { lock (Gate) _shot = shot ?? throw new ArgumentNullException(nameof(shot)); }

    /// <summary>Gives the view back (the free camera keeps the last pose).</summary>
    public static void Release() { lock (Gate) _shot = null; }

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

    /// <summary>Hands the camera to the director while a shot is held, back when released.</summary>
    private void Direct()
    {
        var camera = _view.Camera3D;
        if (FilmStudio.Shot is { } shot)
        {
            _director.Shot = shot;
            if (!_view.Is3D && !_view.IsSwitching)
            {
                camera.Driver = null;                // ⚠ Set finishes its tilt through the camera's own Update — no driver may hold it
                _view.Set(true, animate: false);
            }
            if (_view.Is3D && !ReferenceEquals(camera.Driver, _director)) camera.Driver = _director;
        }
        else if (ReferenceEquals(camera.Driver, _director))
        {
            camera.Driver = null;
        }
    }

    private void Serve(FilmCaptureRequest request, TaskCompletionSource<FilmFrame> done)
    {
        try
        {
            if (!_view.Is3D && request.Card == null) throw new InvalidOperationException("the map is in 2-D — hold a shot (POST /film/shot) or switch the map to 3-D first");
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
            try
            {
                Raylib.BeginTextureMode(_target);
                Raylib.ClearBackground(Color.Black);
                if (request.Card != null) FilmText.Card(request.Card, w, h);
                else
                {
                    _canvas.DrawScene();
                    if (!string.IsNullOrWhiteSpace(request.Caption)) FilmText.Caption(request.Caption!, w, h);
                }
                Raylib.EndTextureMode();
            }
            finally { camera.ViewportOverride = viewport; }

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
    public void DrawOverlay(RenderContext ctx) { }
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
    /// <summary>A caption at the bottom of a <paramref name="w"/>×<paramref name="h"/> picture.</summary>
    public static void Caption(string text, int w, int h)
    {
        int size = Math.Max(12, h / 30);
        var lines = Wrap(text, size, (int)(w * 0.86f));
        int lineH = (int)(size * 1.25f);
        int band = lineH * lines.Count + size;
        int top = h - band - h / 18;
        Raylib.DrawRectangle(0, top, w, band, new Color(0, 0, 0, 150));
        for (int i = 0; i < lines.Count; i++)
        {
            int tw = Raylib.MeasureText(lines[i], size);
            Raylib.DrawText(lines[i], (w - tw) / 2, top + size / 2 + i * lineH, size, Color.RayWhite);
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
