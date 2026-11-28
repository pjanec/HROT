using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Devices;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.Weather;
using Raylib_cs;

namespace Hrot.UI.Common.Effects;

/// <summary>
/// ⭐⭐ SD-S6a (docs/designs/devices/DESIGN_Simulated_Devices.md §3.8a D-161, §3n D-31 – D-35; mockup <c>designs/devices/mockups/laser-beam-visual.svg</c>) —
/// <b>a laser's BEAM and its SPOTS on the 3-D map</b>, as this viewer's sensor sees them. Per emitter whose <see cref="LaserPointing"/> is on: the beam from
/// the device's world origin (<see cref="DevicePoses.TryWorld"/> ∘ <see cref="LaserPointing.Direction"/>) through its <see cref="LaserSpot"/> points (or to
/// <see cref="LaserSpots.MaxRangeM"/> with none) — a thin ADDITIVE line whose brightness is its scatter off the air's haze, fading along it; and a glow at each
/// spot. ⭐ The layer decides NOTHING about who sees what: it asks <see cref="LaserVisibility"/> (the one table the AI and the IG ask too). Under night vision
/// every draw goes through <see cref="SceneSensors.Emissive"/> (white, blooming); under thermal nothing is drawn. A <see cref="LaserSpot.Preview"/> spot (paused)
/// is drawn too — the map observes, it never acts on it (R5-10).
/// <para>On every map host (<c>MapInteractionPack.AttachMapLayers</c>, beside <see cref="ProjectileLayer"/>); stateless over recorded components, so a pause
/// freezes the beam and a replay shows it as it was (D-35). ⚠ On a cluster a station sees another node's emitter once SD-C replicates the two components
/// (D-162); the trainee's own beam from his LOCAL look (D-34) waits for that too — in the editor and replay the replicated pose IS the local one.</para>
/// <para>⚠ Hot path: 0 B per steady frame — one query per world, two reused buffers (grown, never shrunk).</para>
/// </summary>
public sealed class LaserLayer3D : ISceneLayer   // the world: filmed
{
    private readonly Func<EntityRepository?> _world;
    private readonly Func<MapCamera3D?>? _camera;

    public LaserLayer3D(Func<EntityRepository?> world, Func<MapCamera3D?>? camera = null)
    {
        _world = world;
        _camera = camera;
    }

    public string Name => "Lasers";
    public int LayerBitIndex => -1;
    public bool Has3D => true;

    /// <summary>A visible (band 0) laser's colour to the eye: a 532 nm green.</summary>
    public static readonly Color VisibleColour = new(60, 255, 106, 255);

    /// <summary>The beam's width on screen (px) and the spot glow's radius (px) at full brightness.</summary>
    public const float BeamPixels = 1.6f, SpotPixels = 7f;

    /// <summary>Each leg of a beam is drawn in this many pieces, so its scatter fades along it (<see cref="LaserVisibility.FadeAlong"/>).</summary>
    public const int PiecesPerLeg = 6;

    /// <summary>A piece of a beam: from → to, its brightness (0…1) at its middle.</summary>
    public readonly record struct BeamPiece(Vector3 From, Vector3 To, float Intensity, byte Band);

    /// <summary>A spot's glow: where, how bright (0…1), its band.</summary>
    public readonly record struct SpotGlow(Vector3 At, float Intensity, byte Band);

    private readonly List<BeamPiece> _pieces = new();
    private readonly List<SpotGlow> _spots = new();

    /// <summary>The last frame's beam pieces / spot glows (rails; railable without a GL context through <see cref="Resolve"/>).</summary>
    public IReadOnlyList<BeamPiece> Pieces => _pieces;
    public IReadOnlyList<SpotGlow> Spots => _spots;

    /// <summary>Emitters whose beam or spot was drawn in the last 3-D frame (rails).</summary>
    public int EmittersDrawn { get; private set; }

    /// <summary>The map's sensor view as the laser table names it.</summary>
    public static LaserSensor SensorOf(SceneSensor s) => s switch
    {
        SceneSensor.NightVision => LaserSensor.NightVision,
        SceneSensor.Thermal => LaserSensor.Thermal,
        _ => LaserSensor.Eye,
    };

    private EntityRepository? _queryWorld;
    private EntityQuery? _query;

    /// <summary>Fills <see cref="Pieces"/> and <see cref="Spots"/> for <paramref name="sensor"/> from the world's emitters. Nothing seen ⇒ both empty.</summary>
    public void Resolve(LaserSensor sensor)
    {
        _pieces.Clear(); _spots.Clear(); EmittersDrawn = 0;
        var w = _world();
        if (w == null || sensor == LaserSensor.Thermal || !w.IsComponentTypeRegistered<LaserPointing>()) return;
        if (!ReferenceEquals(w, _queryWorld)) { _queryWorld = w; _query = w.Query().With<LaserPointing>().Build(); }
        bool spots = w.IsComponentTypeRegistered<LaserSpot>();
        foreach (var e in _query!)
        {
            ref readonly var p = ref w.GetComponentRO<LaserPointing>(e);
            if (p.On == 0 || p.Power <= 0f || LaserVisibility.Table(p.Band, sensor) <= 0f) continue;
            if (!DevicePoses.TryWorld(w, e, out var origin, out var rotation)) continue;
            var dirLocal = p.Direction.LengthSquared() > 1e-12f ? Vector3.Normalize(p.Direction) : Vector3.UnitX;
            var dir = Vector3.Transform(dirLocal, rotation);
            var weather = WeatherField.At(w, origin);
            float beam = LaserVisibility.Beam(p.Band, p.Power, sensor, LaserVisibility.Aerosol(weather.VisibilityM, weather.RelativeHumidity));
            int count = 0;
            if (spots && w.HasComponent<LaserSpot>(e))
            {
                ref readonly var s = ref w.GetComponentRO<LaserSpot>(e);
                count = Math.Min((int)s.Count, LaserSpotPoints.Count);
                var from = origin; float travelled = 0f;
                for (int i = 0; i < count; i++)
                {
                    var pt = s.Points[i];
                    Leg(from, pt.Point, travelled, beam, weather.VisibilityM, p.Band);
                    travelled += Vector3.Distance(from, pt.Point);
                    from = pt.Point;
                    float glow = LaserVisibility.Spot(p.Band, pt.Strength, sensor);
                    if (glow > 0f) _spots.Add(new SpotGlow(pt.Point, glow, p.Band));
                }
            }
            if (count == 0) Leg(origin, origin + dir * LaserSpots.MaxRangeM, 0f, beam, weather.VisibilityM, p.Band);
            EmittersDrawn++;
        }
    }

    private void Leg(Vector3 a, Vector3 b, float startM, float beam, float visibilityM, byte band)
    {
        if (beam <= 0f) return;
        float length = Vector3.Distance(a, b);
        if (length < 1e-4f) return;
        for (int k = 0; k < PiecesPerLeg; k++)
        {
            float t0 = (float)k / PiecesPerLeg, t1 = (float)(k + 1) / PiecesPerLeg;
            float intensity = beam * LaserVisibility.FadeAlong(startM + length * (t0 + t1) * 0.5f, visibilityM);
            if (intensity < 1f / 255f) break;   // faded out: the rest is darker still
            _pieces.Add(new BeamPiece(Vector3.Lerp(a, b, t0), Vector3.Lerp(a, b, t1), intensity, band));
        }
    }

    public void Update(float dt) { }
    public void Draw(RenderContext ctx) { }   // 2-D: no beam on the plan
    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;

    /// <summary>The colour a band is drawn in at <paramref name="intensity"/> for this view: the band's own colour (band 0's green; an invisible band, seen only
    /// through a tube, white) to the eye; white through <see cref="SceneSensors.Emissive"/> under night vision. The intensity rides on the alpha (additive blend).</summary>
    public static Color ColourOf(in RenderContext ctx, byte band, float intensity)
    {
        var c = ctx.Emissive(band == LaserBands.Visible ? VisibleColour : new Color(255, 255, 255, 255));
        return new Color(c.R, c.G, c.B, (byte)Math.Clamp((int)MathF.Round(255f * intensity), 0, 255));
    }

    public void Draw3D(RenderContext ctx)
    {
        Resolve(SensorOf(ctx.Sensor));
        if (_pieces.Count == 0 && _spots.Count == 0) return;
        var camera = _camera?.Invoke();
        Raylib.BeginBlendMode(BlendMode.Additive);
        for (int i = 0; i < _pieces.Count; i++)
        {
            var piece = _pieces[i];
            float width = PixelsAt(camera, Vector3.Lerp(piece.From, piece.To, 0.5f), BeamPixels, 0.01f);
            var colour = ColourOf(ctx, piece.Band, piece.Intensity);
            Raylib.DrawCylinderEx(HrotToRaylib.Position(piece.From), HrotToRaylib.Position(piece.To), width * 0.5f, width * 0.5f, 5, colour);
        }
        for (int i = 0; i < _spots.Count; i++)
        {
            var spot = _spots[i];
            float radius = PixelsAt(camera, spot.At, SpotPixels * (0.5f + 0.5f * spot.Intensity), 0.03f);
            var at = HrotToRaylib.Position(spot.At);
            Raylib.DrawSphereEx(at, radius * 0.45f, 6, 8, ColourOf(ctx, spot.Band, spot.Intensity));            // the hot core
            Raylib.DrawSphereEx(at, radius, 6, 8, ColourOf(ctx, spot.Band, spot.Intensity * 0.35f));            // the glow round it
        }
        Raylib.EndBlendMode();
    }

    /// <summary><paramref name="pixels"/> on screen as metres at <paramref name="at"/> (at least <paramref name="minMetres"/>); <paramref name="minMetres"/> with no camera.</summary>
    private static float PixelsAt(MapCamera3D? camera, Vector3 at, float pixels, float minMetres)
        => camera == null ? minMetres
           : MathF.Max(minMetres, pixels * camera.MetresPerPixel / MathF.Max(1f, camera.Distance) * Vector3.Distance(camera.Eye, at));
}
