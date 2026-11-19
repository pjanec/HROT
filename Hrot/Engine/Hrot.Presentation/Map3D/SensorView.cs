using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis3D;
using Fdp.Toolkit.Weather;
using Hrot.Common.Constants;
using Hrot.Common.Diagnostics.Gizmos;
using Raylib_cs;

namespace Hrot.UI.Common.Map3D;

/// <summary>
/// ⭐⭐ <c>CE-1984</c> (📄 docs/designs/sensor-view/DESIGN_Sensor_View.md D-1, D-8, D-10, D-11, §4; mockup <c>docs/UX/sensor-view.svg</c>) — the 3-D map's
/// SENSOR VIEW on every map host: the mode (off · night vision · ⏳ thermal, CE-1985) and its options, set from View ▸ Sensor View and
/// <c>POST /view/sensor</c>; each frame it turns the camera's weather sample into the night-vision light (<see cref="NaturalLight"/>, the moon
/// included), the scene's light (<see cref="Apply"/>, called by <see cref="WeatherSkyLayer"/>) and the post-process' settings (<see cref="Post"/>,
/// which <see cref="Fdp.Toolkit.Vis2D.MapCanvas.Draw"/> runs around the 3-D layers); and draws the legend strip on the map, after the post-process
/// (sharp). ⭐ Display only — it changes nothing in the simulation; 0 B per steady frame (the legend's text is rebuilt when its numbers change).
/// </summary>
public sealed class SensorView : IMapLayer
{
    private readonly MapViewSwitch _view;
    private readonly FdpEventBus? _bus;

    public SensorView(MapViewSwitch view, FdpEventBus? bus, Fdp.Toolkit.Input.IChordInput? keys = null, Fdp.Toolkit.Input.Keymap? keymap = null)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _bus = bus;
        _keymap = keymap ?? Fdp.Toolkit.Input.Keymap.Process;
        _keys = SensorViewKeys.Reader(_keymap, keys ?? new RaylibPressedKeys());
    }

    private readonly Fdp.Toolkit.Input.Keymap _keymap;
    private int _keyVersion = -1;
    private string? _cycleKey;

    private readonly Fdp.Toolkit.Input.KeymapReader _keys;   // ⭐ CE-1989 — F4 (a keymap row, rebindable)

    /// <summary>⭐ CE-1989 — the key that cycles the view as the keymap binds it now (the menu and the legend show it), e.g. <c>F4</c>.</summary>
    public string? CycleKey
    {
        get
        {
            if (_keymap.Version != _keyVersion) { _keyVersion = _keymap.Version; _cycleKey = _keys.ChordOf(SensorViewKeys.Next); }   // re-read only on a rebind: 0 B per frame
            return _cycleKey;
        }
    }

    /// <summary>⭐ CE-1989 (DESIGN_Sensor_View §10) — the next view: normal → night vision → thermal (once offered) → normal.</summary>
    public void Next() => SetMode(Mode switch
    {
        SceneSensor.None => SceneSensor.NightVision,
        SceneSensor.NightVision => ThermalOffered ? SceneSensor.Thermal : SceneSensor.None,
        _ => SceneSensor.None,
    });

    /// <summary>Raylib's keyboard as a device for the keymap's rows (only presses are asked), while a window runs.</summary>
    private sealed class RaylibPressedKeys : Fdp.Toolkit.Input.IChordInput
    {
        public NodeEditor.Primitives.KeyModifiers Modifiers => NodeEditor.Primitives.KeyModifiers.None;
        public bool IsDown(NodeEditor.Primitives.EditorKey source) => false;
        public bool IsPressed(NodeEditor.Primitives.EditorKey source, bool allowRepeat = false)
            => Raylib.IsWindowReady() && Fdp.Presentation.Input.RaylibKeyCodes.Of(source) is var code && code != 0 && Raylib.IsKeyPressed((KeyboardKey)code);
        public float Value(NodeEditor.Primitives.EditorKey source) => 0f;
    }

    /// <summary>The shared post-process (the canvas' <see cref="Fdp.Toolkit.Vis2D.MapCanvas.PostProcess"/>).</summary>
    public SensorPostProcess Post { get; } = new();

    public SceneSensor Mode { get; set; }
    /// <summary>Night vision: 0 green (P43), 1 white (P45); thermal: 0 white-hot, 1 black-hot, 2 ironbow.</summary>
    public int Palette { get; set; }
    public bool Halo { get; set; } = true;
    public bool SensorResolution { get; set; } = true;
    public bool Noise { get; set; } = true;
    public bool Vignette { get; set; } = true;
    public bool AutoGain { get; set; } = true;

    /// <summary>⏳ CE-1985 — thermal is offered once its scene pass exists.</summary>
    public static bool ThermalOffered => true;   // ⭐ CE-1985

    /// <summary>The natural light at the camera's point the last frame, lux (night vision; telemetry, the legend, <c>GET /view/sensor</c>).</summary>
    public float Lux { get; private set; } = float.NaN;
    /// <summary>The moon the last frame: above the horizon (deg) and lit fraction.</summary>
    public float MoonElevationDeg { get; private set; }
    public float MoonIllumination { get; private set; }

    /// <summary>The tube's brightest useful light floor, lux: below it the picture dims (gain at its limit).</summary>
    public const float TubeFloorLux = 0.0005f;
    /// <summary>The sensor's lines when <see cref="SensorResolution"/> is on (a Gen-3 tube resolves ~64 lp/mm ≈ a 600-line picture).</summary>
    public const float NightVisionLines = 600f;

    /// <summary>The View ▸ Sensor View state (<see cref="SensorViewMenu.Pack"/>).</summary>
    public int MenuState => SensorViewMenu.Pack((int)Mode, Palette, Halo, SensorResolution, Noise, Vignette, AutoGain, ThermalOffered);

    public string Name => "Sensor view";
    public int LayerBitIndex => -1;

    // ── ⭐ CE-1985 (D-10) — the debugging aids of the thermal view: the spot meter under the pointer, the selected observer's outlines ──────────────

    /// <summary>The map pointer in the canvas' pixels, or null when the pointer is not over the map (a panel holds it). Wired by <c>AttachMapLayers</c>.</summary>
    public Func<Vector2?>? Pointer { get; set; }
    /// <summary>The world the outlines read (the selected observer, the targets). Wired by <c>AttachMapLayers</c>.</summary>
    public Func<EntityRepository?>? World { get; set; }
    /// <summary>The body layer whose boxes the outlines frame (what a 3-D click hits). Wired by <c>AttachMapLayers</c>.</summary>
    public EntityBodyLayer3D? Bodies { get; set; }

    /// <summary>The °C the scene was drawn at under the pointer the last thermal frame (the spot meter); NaN when none.</summary>
    public float SpotC { get; private set; } = float.NaN;
    /// <summary>The selected observer the outlines were drawn for the last frame (<see cref="Entity.Null"/>: none, or it has no thermal sensor).</summary>
    public Entity Observer { get; private set; } = Entity.Null;
    /// <summary>Units outlined the last frame: their signature reaches the observer's <c>MinSignature</c> within its thermal range.</summary>
    public int OutlinesDrawn { get; private set; }

    public void Update(float dt)
    {
        Post.ProbePixel = Mode == SceneSensor.Thermal && Pointer?.Invoke() is { } p ? p : null;   // ⭐ CE-1985 — the spot meter, read back by the next frame's post
        if (_keys.Pressed(SensorViewKeys.Next)) Next();   // ⭐ CE-1989 — F4, on the map and in first person
        if (_bus == null) return;
        foreach (ref readonly var e in _bus.Read<SensorViewMenuEvent>())
            Act(e.Action);
    }

    /// <summary>One View ▸ Sensor View item.</summary>
    public void Act(int action)
    {
        switch (action)
        {
            case GlobalActionIds.SensorViewOff: Mode = SceneSensor.None; break;
            case GlobalActionIds.SensorViewNightVision: SetMode(SceneSensor.NightVision); break;
            case GlobalActionIds.SensorViewThermal: if (ThermalOffered) SetMode(SceneSensor.Thermal); break;
            case GlobalActionIds.SensorPaletteNext: Palette = (Palette + 1) % (Mode == SceneSensor.Thermal ? 3 : 2); break;
            case GlobalActionIds.SensorHalo: Halo = !Halo; break;
            case GlobalActionIds.SensorResolution: SensorResolution = !SensorResolution; break;
            case GlobalActionIds.SensorNoise: Noise = !Noise; break;
            case GlobalActionIds.SensorVignette: Vignette = !Vignette; break;
            case GlobalActionIds.SensorAutoGain: AutoGain = !AutoGain; break;
        }
    }

    /// <summary>Switches the mode (a new mode starts on its first palette). Refuses thermal until it is built (CE-1985).</summary>
    public string? SetMode(SceneSensor mode)
    {
        if (mode == SceneSensor.Thermal && !ThermalOffered) return "Thermal is not built yet (CE-1985).";
        if (mode != Mode) Palette = 0;
        Mode = mode;
        return null;
    }

    /// <summary>
    /// The scene's light for this frame, from the camera's weather sample: the normal atmosphere unchanged while off; under night vision the
    /// light of the sun, the MOON or the stars — the brighter body's direction, the share of direct light by the cloud — exposed so a lit surface
    /// peaks ≈ 0.7 (a self-luminous thing, drawn near-white, stays above it and blooms) — and the post-process' gain and noise from the real lux.
    /// Called by <see cref="WeatherSkyLayer.Update"/>, which owns the sample.
    /// </summary>
    public SceneAtmosphere Apply(in WeatherSample s, bool hasSample, SceneAtmosphere normal, double latDeg = 0, double lonDeg = 0)
    {
        var settings = new SensorPostSettings
        {
            Mode = Mode, Palette = Palette, Halo = Halo, SensorResolution = SensorResolution, Noise = Noise, Vignette = Vignette,
            Gain = 1f, NoiseSigma = 0f, SensorLines = NightVisionLines, TargetMean = AutoGain ? 0.36f : 0f,
        };
        if (Mode == SceneSensor.Thermal) return ApplyThermal(s, hasSample, normal, latDeg, lonDeg, settings);
        SetShaderThermal(false);
        if (Mode != SceneSensor.NightVision)
        {
            Post.Settings = settings;
            return normal;
        }

        float lux = hasSample ? NaturalLight.Lux(s) : NaturalLight.StarlightLux;
        Lux = lux;
        MoonElevationDeg = hasSample ? (float)s.MoonElevationDeg : 0f;
        MoonIllumination = hasSample ? s.MoonIllumination : 0f;
        settings.Gain = Math.Clamp(lux / TubeFloorLux, 0.15f, 1f);                         // the tube at its limit below the floor
        settings.NoiseSigma = Math.Clamp(0.035f / MathF.Sqrt(MathF.Max(lux, 1e-6f) / NaturalLight.FullMoonLux), 0.02f, 0.4f);   // ∝ 1/√photons
        if (!AutoGain) settings.TargetMean = 0f;
        Post.Settings = settings;

        // the light's direction: the sun while it is up, else the moon while it is up (else no direct light — starlight is all-round)
        bool sunUp = hasSample && s.SunElevationDeg > 0;
        bool moonUp = hasSample && s.MoonElevationDeg > 0;
        double el = sunUp ? s.SunElevationDeg : moonUp ? s.MoonElevationDeg : 45, az = sunUp ? s.SunAzimuthDeg : moonUp ? s.MoonAzimuthDeg : 180;
        float elR = (float)(el * Math.PI / 180.0), azR = (float)(az * Math.PI / 180.0);
        var toLight = Vector3.Normalize(new Vector3(MathF.Sin(azR) * MathF.Cos(elR), MathF.Cos(azR) * MathF.Cos(elR), MathF.Sin(elR)));
        float cover = hasSample ? Math.Clamp(s.CloudOctasAbove / 8f, 0f, 1f) : 0f;
        float direct = (sunUp || moonUp) ? 0.45f * (1f - 0.8f * cover) * Math.Clamp(MathF.Sin(elR) / 0.3f, 0.2f, 1f) : 0f;
        float ambient = 0.68f - direct;
        var sky = new Color((byte)70, (byte)70, (byte)70, (byte)255);         // the night sky's glow, as the tube sees it
        var horizon = new Color((byte)105, (byte)105, (byte)105, (byte)255);
        return new SceneAtmosphere(sky, horizon, toLight, ambient, direct, normal.VisibilityM, default) { Clouds = normal.Clouds };
    }

    // ── ⭐ CE-1985 (docs/designs/sensor-view/DESIGN_Sensor_View.md §5) — thermal ─────────────────────────────────────────────────────────────────

    private readonly float[] _flat = new float[LitShader.ThermalClasses], _gain = new float[LitShader.ThermalClasses], _eps = new float[LitShader.ThermalClasses];
    private readonly int[] _codeClass = new int[32];
    private bool _codesFilled, _shaderThermal;
    private Fdp.Toolkit.Perception.Signatures.ThermalScene.DayForcing _forcing;
    private DateTime _forcingDay = DateTime.MinValue;
    private double _forcingLat = double.NaN, _forcingLon = double.NaN;
    private float _forcingCut = -1f;

    /// <summary>The air's °C at the view, the sky's apparent °C and a painted hull's apparent °C the last thermal frame (the legend, units, rails).</summary>
    public float AirC { get; private set; } = 15f;
    public float SkyC { get; private set; }
    public float HullC { get; private set; } = 15f;
    /// <summary>The gain the last thermal frame: the °C shown black and the range to white.</summary>
    public float LevelC { get; private set; }
    public float SpanC { get; private set; } = 30f;

    /// <summary>The apparent °C of a horizontal surface of class <paramref name="c"/> the last thermal frame (rails).</summary>
    public float ApparentOf(Fdp.Toolkit.Perception.Signatures.ThermalClassId c)
        => Fdp.Toolkit.Perception.Signatures.ThermalScene.Apparent(_flat[(int)c], _eps[(int)c], SkyC);

    private SceneAtmosphere ApplyThermal(in WeatherSample s, bool hasSample, SceneAtmosphere normal, double lat, double lon, SensorPostSettings settings)
    {
        if (!_codesFilled) { TerrainLayer3D.ThermalClassTable(_codeClass); _codesFilled = true; }
        var sample = hasSample ? s : new WeatherSample { Utc = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc), GroundTemperatureC = 15f, TemperatureC = 15f, CloudOctas = 4f };
        float cut = Fdp.Toolkit.Perception.Signatures.ThermalScene.CloudCut(sample.CloudOctasAbove);
        // the sun's daily path, once per sim day and place (and when the cloud above changes) — never per frame
        if (sample.Utc.Date != _forcingDay || lat != _forcingLat || lon != _forcingLon || MathF.Abs(cut - _forcingCut) > 0.02f)
        {
            _forcing = Fdp.Toolkit.Perception.Signatures.ThermalScene.DayForcing.Of(sample.Utc, lat, lon, cut);
            _forcingDay = sample.Utc.Date; _forcingLat = lat; _forcingLon = lon; _forcingCut = cut;
        }
        Fdp.Toolkit.Perception.Signatures.ThermalScene.ClassTemperatures(sample, _forcing, _flat, _gain, _eps);
        AirC = sample.GroundTemperatureC;
        SkyC = Fdp.Toolkit.Perception.Signatures.ThermalScene.SkyC(sample);
        HullC = ApparentOf(Fdp.Toolkit.Perception.Signatures.ThermalClassId.PaintedMetal);
        float sunSin = (float)Math.Max(0.0, Math.Sin(sample.SunElevationDeg * Math.PI / 180.0));
        if (LitShader.IsCreated) { LitShader.Shared.SetThermal(true, _flat, _gain, _eps, _codeClass, SkyC, AirC, sunSin); _shaderThermal = true; }

        // the gain (D-8): from the coldest to the warmest class in the scene, and up to body warmth — a man reads near white, a fire saturates
        float lo = float.MaxValue, hi = float.MinValue;
        for (int i = 0; i < Fdp.Toolkit.Perception.Signatures.ThermalScene.ClassCount; i++)
        {
            float c = Fdp.Toolkit.Perception.Signatures.ThermalScene.Apparent(_flat[i], _eps[i], SkyC);
            lo = MathF.Min(lo, c); hi = MathF.Max(hi, c);
        }
        if (AutoGain) { LevelC = lo - 1f; SpanC = MathF.Max(8f, MathF.Max(hi + 2f, Fdp.Toolkit.Perception.Signatures.ThermalScene.ClothingC + 4f) - LevelC); }
        else { LevelC = -10f; SpanC = 60f; }   // a fixed −10…50 °C scale
        settings.AirC = AirC; settings.LevelC = LevelC; settings.SpanC = SpanC;
        settings.TargetMean = 0f; settings.Gain = 1f; settings.NoiseSigma = 0.012f; settings.SensorLines = ThermalLines;
        Post.Settings = settings;
        var sky = LitShader.EncodeC(SkyC);
        return new SceneAtmosphere(sky, sky, normal.ToSun, normal.Ambient, normal.SunLight, normal.VisibilityM, default);   // no clouds, no sun disc in the heat picture
    }

    /// <summary>A thermal camera's lines (a 640×480 microbolometer) when <see cref="SensorResolution"/> is on.</summary>
    public const float ThermalLines = 480f;

    private void SetShaderThermal(bool on)
    {
        if (on == _shaderThermal || !LitShader.IsCreated) return;
        LitShader.Shared.SetThermal(on, _flat, _gain, _eps, _codeClass, SkyC, AirC, 0f);
        _shaderThermal = on;
    }

    /// <summary>
    /// ⭐ CE-1985 (§5b) — a unit's °C for the body layer: its drivers gathered ONCE (<see cref="Fdp.Toolkit.Perception.Sensors.UnitThermalDrivers"/>, the AI sensor's own
    /// gathering) through <see cref="Fdp.Toolkit.Perception.Signatures.ThermalScene.Unit"/>; an engine's hull at the painted-metal surface °C, its hot end and side from
    /// its type (<see cref="Fdp.Toolkit.Perception.Signatures.ThermalState.HotEnd"/>); a body (a person, an animal) at its clothing / fur; a type with no thermal data
    /// at the air's. 0 B.
    /// </summary>
    public (float BodyC, float HotC, int HotSign) UnitHeat(EntityRepository world, Entity e)
    {
        if (!world.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Signatures.ThermalState>() || !world.HasComponent<Fdp.Toolkit.Perception.Signatures.ThermalState>(e))
            return (AirC, AirC, 0);
        ref readonly var ts = ref world.GetComponentRO<Fdp.Toolkit.Perception.Signatures.ThermalState>(e);
        double now = Fdp.Toolkit.Time.SimClock.TotalTimeOf(world);   // ⛔ SimClock.Of allocates a clock per call (measured 24 B per unit per frame)
        var d = Fdp.Toolkit.Perception.Sensors.UnitThermalDrivers.Of(world, e, in ts, now);
        var source = d.HasSource ? d.Source : default;
        var heat = Fdp.Toolkit.Perception.Signatures.ThermalScene.Unit(in ts, in source, d.Speed, now, d.Barrel01, d.Fire01, d.DiedAt, ts.HasPowerPlant ? HullC : AirC);
        int sign = !ts.HasPowerPlant ? 0 : ts.HotEnd == (byte)Fdp.Toolkit.Terrain.HeatMarkEnd.Front ? 1 : ts.HotEnd == (byte)Fdp.Toolkit.Terrain.HeatMarkEnd.Rear ? -1 : 0;
        return (heat.BodyC, MathF.Max(heat.HotEndC, heat.BarrelC), sign);
    }

    public void Draw(RenderContext ctx) { }
    public bool Has3D => true;
    public void Draw3D(RenderContext ctx) { }
    public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
    public Entity? PickEntity(Vector2 worldPos) => null;

    private string _legend = "";
    private int _kMode = -1, _kKeyVersion = -2, _kPalette, _kLux, _kMoonEl, _kMoonLit;

    /// <summary>The legend strip, top centre of the 3-D map, drawn after the post-process (sharp). Its text is rebuilt only when a shown number
    /// changes (0 B per steady frame).</summary>
    public void DrawOverlay(RenderContext ctx)
    {
        if (!_view.Is3D || ctx.Sensor == SceneSensor.None) return;
        var vp = _view.Camera3D.Viewport;
        int kLux = Mode == SceneSensor.Thermal ? (int)MathF.Round(LevelC) * 1000 + (int)MathF.Round(SpanC) : (int)MathF.Round(MathF.Log10(MathF.Max(Lux, 1e-6f)) * 100f);
        int kEl = Mode == SceneSensor.Thermal ? (int)MathF.Round(AirC) : (int)MoonElevationDeg, kLit = (int)(MoonIllumination * 100f);
        if ((int)Mode != _kMode || _keyVersion != _kKeyVersion || Palette != _kPalette || kLux != _kLux || kEl != _kMoonEl || kLit != _kMoonLit)
        {
            _kMode = (int)Mode; _kKeyVersion = _keyVersion; _kPalette = Palette; _kLux = kLux; _kMoonEl = kEl; _kMoonLit = kLit;
            _legend = (Mode == SceneSensor.NightVision ? NightVisionLegend() : ThermalLegend()) + (CycleKey is { } k ? $"  -  {k}: next view" : "");
        }
        int fs = 16, w = Raylib.MeasureText(_legend, fs) + 24;
        int x = (int)(vp.X / 2f - w / 2f), y = 34;
        Raylib.DrawRectangle(x, y, w, fs + 12, new Color((byte)16, (byte)16, (byte)20, (byte)225));
        Raylib.DrawRectangleLines(x, y, w, fs + 12, new Color((byte)58, (byte)58, (byte)68, (byte)255));
        Raylib.DrawText(_legend, x + 12, y + 6, fs, new Color((byte)200, (byte)200, (byte)208, (byte)255));
        if (Mode == SceneSensor.Thermal)
        {
            DrawOutlines();
            DrawSpotMeter();
        }
        else { SpotC = float.NaN; OutlinesDrawn = 0; Observer = Entity.Null; }
    }

    private string _spot = "";
    private int _kSpot = int.MinValue;
    private Fdp.Core.EntityQuery? _selQuery, _heatQuery, _sensorQuery;
    private EntityRepository? _queryWorld;

    /// <summary>Outline colour: the observer can see this unit by its heat.</summary>
    public static readonly Color OutlineColour = new((byte)255, (byte)170, (byte)40, (byte)255);

    /// <summary>⭐ CE-1985 (D-10) — the spot meter: the °C the scene was drawn at under the pointer (before gain, palette and noise), beside the pointer.
    /// The text is rebuilt only when the tenth of a degree changes (0 B per steady frame).</summary>
    private void DrawSpotMeter()
    {
        SpotC = Post.HasProbed ? LitShader.DecodeC(Post.Probed) : float.NaN;
        HoveredUnit = Entity.Null;
        if (Pointer?.Invoke() is not { } p) return;
        if (DrawUnitReadout(p)) return;   // over a unit: its temperature and WHY (the mockup's C row)
        if (float.IsNaN(SpotC)) return;
        int k = (int)MathF.Round(SpotC * 10f);
        if (k != _kSpot) { _kSpot = k; _spot = (k / 10f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " C"; }
        int fs = 14, w = Raylib.MeasureText(_spot, fs) + 10;
        int x = (int)p.X + 14, y = (int)p.Y + 12;
        Raylib.DrawRectangle(x, y, w, fs + 6, new Color((byte)16, (byte)16, (byte)20, (byte)225));
        Raylib.DrawText(_spot, x + 5, y + 3, fs, new Color((byte)240, (byte)240, (byte)240, (byte)255));
        Raylib.DrawCircleLines((int)p.X, (int)p.Y, 4f, new Color((byte)240, (byte)240, (byte)240, (byte)255));
    }

    /// <summary>The unit under the pointer the last thermal frame (its readout was drawn); <see cref="Entity.Null"/> otherwise.</summary>
    public Entity HoveredUnit { get; private set; } = Entity.Null;
    /// <summary>The unit readout's three lines as last drawn (rails; rebuilt only when a shown value changes).</summary>
    public string ReadoutName => _r1;
    public string ReadoutDrivers => _r2;
    public string ReadoutSignature => _r3;
    private string _r1 = "", _r2 = "", _r3 = "";
    /// <summary>Where the unit readout was drawn the last frame (screen pixels; rails).</summary>
    public Rectangle ReadoutBox { get; private set; }
    private int _kReadout = int.MinValue;

    /// <summary>
    /// ⭐ CE-1985 (D-10; mockup <c>docs/UX/sensor-view.svg</c> row C — the hover readout) — over a unit with thermal data: its name, its surface °C and its hot
    /// end's (front / rear), the DRIVERS the AI's sensor reads (the engine or body state and for how long, the barrel, a fire), and its signature against the
    /// selected observer's threshold — SEEN (outlined) or not, or beyond its range. The unit is the 3-D pick under the pointer (entities only). The text is
    /// rebuilt only when a shown value changes (a tenth of a degree, a minute, a hundredth of signature) — 0 B in a steady frame.
    /// </summary>
    private bool DrawUnitReadout(Vector2 p)
    {
        if (World?.Invoke() is not { } world || !world.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Signatures.ThermalState>()) return false;
        var hit = _view.Camera3D.Pick(p, PickFilter.Entities);
        if (hit.Kind != PickKind.Entity || hit.NetworkId <= 0) return false;
        var e = Fdp.Toolkit.Replication.Services.NetworkIdResolver.ResolveNetworkId(world, hit.NetworkId);
        if (e.IsNull || !world.HasComponent<Fdp.Toolkit.Perception.Signatures.ThermalState>(e)) return false;
        HoveredUnit = e;
        ref readonly var ts = ref world.GetComponentRO<Fdp.Toolkit.Perception.Signatures.ThermalState>(e);
        double now = Fdp.Toolkit.Time.SimClock.TotalTimeOf(world);
        var (bodyC, hotC, hotSign) = UnitHeat(world, e);
        var d = Fdp.Toolkit.Perception.Sensors.UnitThermalDrivers.Of(world, e, in ts, now,
            world.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Signatures.HeatSource>(), world.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Signatures.FireState>());
        bool observed = !_eyeObserver.IsNull && _eyeObserver == Observer;
        float contrast = observed ? _eyeContrast
            : Fdp.Toolkit.Perception.Sensors.PerceptionWeather.TrySample(world, e, out var w) ? Fdp.Toolkit.Perception.Sensors.PerceptionWeather.BaseContrast(in w) : 1f;
        float sig = Fdp.Toolkit.Perception.Sensors.ThermalPerception.SignatureOf(world, e, in ts, contrast, now);
        bool inRange = observed && world.HasComponent<SimTransform>(e)
                       && Vector3.DistanceSquared(world.GetComponentRO<SimTransform>(e).Position, _eyeAt) <= _eyeRange * _eyeRange;
        int minutes = d.HasSource ? (int)((now - d.Source.Since.Seconds) / 60.0) : -1;
        int key = HashCode.Combine(e.Index, (int)MathF.Round(bodyC * 10f), (int)MathF.Round(hotC), hotSign,
                                   HashCode.Combine(d.HasSource ? (int)d.Source.State : -1, minutes, (int)MathF.Round(d.Barrel01 * 120f), d.Fire01 > 0f),
                                   (int)MathF.Round(sig * 100f), observed ? _eyeObserver.Index : -1, HashCode.Combine(inRange, (int)MathF.Round(_eyeMin * 100f)));
        if (key != _kReadout)
        {
            _kReadout = key;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string name = Fdp.Toolkit.Squad.Command.CommandReportText.UnitName(world, e) is { Length: > 0 } n ? n
                        : Bodies?.TypeNameOf(world, e) is { Length: > 0 } tn ? tn : "#" + e.Index.ToString(inv);   // the scenario's name, else its type (the mockup's "T-72")
            _r1 = $"{name}  -  {bodyC.ToString("0.0", inv)} C" + (hotSign != 0 ? $"  ({(hotSign > 0 ? "front" : "rear")} {hotC.ToString("0", inv)} C)" : "");
            string source = !d.HasSource ? "no heat source"
                : d.Source.State switch
                {
                    Fdp.Toolkit.Perception.Signatures.HeatSourceState.Off => "engine off",
                    Fdp.Toolkit.Perception.Signatures.HeatSourceState.Idle => "engine idle",
                    Fdp.Toolkit.Perception.Signatures.HeatSourceState.Running => "engine running",
                    Fdp.Toolkit.Perception.Signatures.HeatSourceState.Dead => "destroyed",
                    Fdp.Toolkit.Perception.Signatures.HeatSourceState.Standing => "standing",
                    Fdp.Toolkit.Perception.Signatures.HeatSourceState.Moving => "moving",
                    _ => "no state yet",
                } + (minutes >= 0 && d.Source.State != Fdp.Toolkit.Perception.Signatures.HeatSourceState.None ? $" for {minutes.ToString(inv)} min" : "");
            string barrel = d.Barrel01 > 0.005f ? $"barrel {(bodyC + d.Barrel01 * 120f).ToString("0", inv)} C" : "barrel cold";
            _r2 = $"{source}  -  {barrel}  -  {(d.Fire01 > 0f ? "BURNING" : "no fire")}";
            _r3 = !observed ? $"signature {sig.ToString("0.00", inv)}  -  select a unit with a thermal sensor to compare"
                : !inRange ? $"signature {sig.ToString("0.00", inv)}: beyond the observer's {_eyeRange.ToString("0", inv)} m thermal range"
                : Fdp.Toolkit.Perception.Sensors.ThermalPerception.Reaches(sig, _eyeMin)
                    ? $"signature {sig.ToString("0.00", inv)} >= observer {_eyeMin.ToString("0.00", inv)}: SEEN (outlined)"
                    : $"signature {sig.ToString("0.00", inv)} < observer {_eyeMin.ToString("0.00", inv)}: not seen";
        }
        int fs = 14, lh = fs + 4;
        int wd = Math.Max(Raylib.MeasureText(_r1, fs), Math.Max(Raylib.MeasureText(_r2, fs), Raylib.MeasureText(_r3, fs))) + 16;
        int x = (int)p.X + 14, y = (int)p.Y + 12;
        var vp = _view.Camera3D.Viewport;
        if (x + wd > vp.X) x = Math.Max(0, (int)p.X - 14 - wd);
        if (y + 3 * lh + 10 > vp.Y) y = Math.Max(0, (int)p.Y - 12 - 3 * lh - 10);
        Raylib.DrawRectangle(x, y, wd, 3 * lh + 10, new Color((byte)16, (byte)16, (byte)20, (byte)235));
        Raylib.DrawRectangleLines(x, y, wd, 3 * lh + 10, new Color((byte)58, (byte)58, (byte)68, (byte)255));
        Raylib.DrawText(_r1, x + 8, y + 5, fs, new Color((byte)240, (byte)240, (byte)240, (byte)255));
        Raylib.DrawText(_r2, x + 8, y + 5 + lh, fs, new Color((byte)190, (byte)190, (byte)200, (byte)255));
        Raylib.DrawText(_r3, x + 8, y + 5 + 2 * lh, fs, observed && inRange && Fdp.Toolkit.Perception.Sensors.ThermalPerception.Reaches(sig, _eyeMin) ? OutlineColour
                                                     : new Color((byte)190, (byte)190, (byte)200, (byte)255));
        ReadoutBox = new Rectangle(x, y, wd, 3 * lh + 10);
        return true;
    }

    private Entity _eyeObserver = Entity.Null;
    private float _eyeMin, _eyeRange, _eyeContrast = 1f;
    private Vector3 _eyeAt;

    /// <summary>
    /// ⭐ CE-1985 (D-10) — the units the PRIMARY-selected unit's thermal sensor can see by their heat: within its (weather-capped) thermal range and with a
    /// signature reaching its <c>MinSignature</c> — the AI sensor's own rule (<see cref="Fdp.Toolkit.Perception.Sensors.ThermalPerception.SignatureOf"/>,
    /// ⛔ never a copy). Corner brackets round each one's projected body box, drawn after the post (sharp). ⚠ Range and signature only — no field of view,
    /// no line of sight (the sensor's other tests): it answers "hot enough for this sensor", not "seen now". 0 B per frame (queries built per world).
    /// </summary>
    private void DrawOutlines()
    {
        OutlinesDrawn = 0; Observer = Entity.Null; _eyeObserver = Entity.Null;
        if (World?.Invoke() is not { } world || Bodies == null) return;
        if (!ReferenceEquals(world, _queryWorld))
        {
            _queryWorld = world;
            _selQuery = world.IsComponentTypeRegistered<Hrot.Map.Common.Components.SelectionState>()
                ? world.Query().With<Hrot.Map.Common.Components.SelectionState>().Build() : null;
            _heatQuery = world.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Signatures.ThermalState>()
                ? world.Query().With<Fdp.Toolkit.Perception.Signatures.ThermalState>().With<SimTransform>().Build() : null;
            _sensorQuery = world.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Components.SensorTag>() && world.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.PartMetadata>()
                ? world.Query().With<Fdp.Toolkit.Perception.Components.SensorTag>().With<Fdp.Toolkit.Replication.Components.PartMetadata>().Build() : null;
        }
        if (_selQuery == null || _heatQuery == null) return;
        Entity observer = Entity.Null;
        foreach (var e in _selQuery)
            if (world.GetComponentRO<Hrot.Map.Common.Components.SelectionState>(e).IsPrimarySelection) { observer = e; break; }
        if (observer.IsNull || !world.HasComponent<SimTransform>(observer) || !TryThermalEye(world, observer, out float min, out float range, out float contrast)) return;
        Observer = observer;
        var eye = world.GetComponentRO<SimTransform>(observer).Position;
        _eyeObserver = observer; _eyeMin = min; _eyeRange = range; _eyeContrast = contrast; _eyeAt = eye;   // the unit readout compares against the same eye
        double now = Fdp.Toolkit.Time.SimClock.TotalTimeOf(world);
        bool sources = world.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Signatures.HeatSource>();
        bool fires = world.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Signatures.FireState>();
        var cam = _view.Camera3D;
        foreach (var e in _heatQuery)
        {
            if (e == observer) continue;
            if (Vector3.DistanceSquared(world.GetComponentRO<SimTransform>(e).Position, eye) > range * range) continue;
            ref readonly var ts = ref world.GetComponentRO<Fdp.Toolkit.Perception.Signatures.ThermalState>(e);
            float sig = Fdp.Toolkit.Perception.Sensors.ThermalPerception.SignatureOf(world, e, in ts, contrast, now, sources, fires);
            if (!Fdp.Toolkit.Perception.Sensors.ThermalPerception.Reaches(sig, min)) continue;
            if (!Bodies.TryGetBox(world, e, out var c, out var r, out var h)) continue;
            if (!ScreenBox(cam, c, r, h * 1.1f, out var lo, out var hi)) continue;
            Brackets(lo, hi, OutlineColour);
            OutlinesDrawn++;
        }
    }

    /// <summary>The observer's thermal sensor: on the unit itself, or the first of its sensor PARTS (a <c>SensorTag</c> whose <c>PartMetadata</c> parent is the unit —
    /// how a TKB unit carries its sensors) that is a thermal one.</summary>
    private bool TryThermalEye(EntityRepository world, Entity unit, out float min, out float range, out float contrast)
    {
        if (Fdp.Toolkit.Perception.Sensors.ThermalPerception.TryEyeOf(world, unit, unit, out min, out range, out contrast)) return true;
        if (_sensorQuery != null)
            foreach (var s in _sensorQuery)
                if (world.GetComponentRO<Fdp.Toolkit.Replication.Components.PartMetadata>(s).ParentEntity == unit
                    && Fdp.Toolkit.Perception.Sensors.ThermalPerception.TryEyeOf(world, s, unit, out min, out range, out contrast)) return true;
        return false;
    }

    /// <summary>The screen rectangle round an oriented box's eight corners; false when any corner is behind the camera.</summary>
    private static bool ScreenBox(MapCamera3D cam, Vector3 centre, Quaternion rotation, Vector3 half, out Vector2 lo, out Vector2 hi)
    {
        lo = new Vector2(float.MaxValue); hi = new Vector2(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) == 0 ? -half.X : half.X, (i & 2) == 0 ? -half.Y : half.Y, (i & 4) == 0 ? -half.Z : half.Z);
            var s = cam.WorldToScreen(centre + Vector3.Transform(corner, rotation));
            if (s.X < -1e5f) return false;
            lo = Vector2.Min(lo, s); hi = Vector2.Max(hi, s);
        }
        return true;
    }

    /// <summary>Four corner brackets round a screen rectangle (at least 10 px a side).</summary>
    private static void Brackets(Vector2 lo, Vector2 hi, Color c)
    {
        var mid = (lo + hi) / 2f;
        var half = Vector2.Max((hi - lo) / 2f, new Vector2(5f));
        lo = mid - half; hi = mid + half;
        float l = MathF.Max(4f, MathF.Min(half.X, half.Y) * 0.6f);
        Raylib.DrawLineEx(lo, lo + new Vector2(l, 0), 2f, c); Raylib.DrawLineEx(lo, lo + new Vector2(0, l), 2f, c);
        Raylib.DrawLineEx(new Vector2(hi.X, lo.Y), new Vector2(hi.X - l, lo.Y), 2f, c); Raylib.DrawLineEx(new Vector2(hi.X, lo.Y), new Vector2(hi.X, lo.Y + l), 2f, c);
        Raylib.DrawLineEx(new Vector2(lo.X, hi.Y), new Vector2(lo.X + l, hi.Y), 2f, c); Raylib.DrawLineEx(new Vector2(lo.X, hi.Y), new Vector2(lo.X, hi.Y - l), 2f, c);
        Raylib.DrawLineEx(hi, hi - new Vector2(l, 0), 2f, c); Raylib.DrawLineEx(hi, hi - new Vector2(0, l), 2f, c);
    }

    private string ThermalLegend()
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string palette = Palette switch { 1 => "black-hot", 2 => "ironbow", _ => "white-hot" };
        return $"THERMAL  -  {LevelC.ToString("0", inv)} .. {(LevelC + SpanC).ToString("0", inv)} C  -  air {AirC.ToString("0", inv)} C  -  {palette}";
    }

    private string NightVisionLegend()
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string light = Lux >= 10f ? Lux.ToString("0", inv) : Lux >= 0.1f ? Lux.ToString("0.00", inv) : Lux.ToString("0.0000", inv);
        string moon = MoonElevationDeg > 0
            ? $"moon {(MoonIllumination * 100f).ToString("0", inv)} % lit, {MoonElevationDeg.ToString("0", inv)} deg up"
            : $"moon {(MoonIllumination * 100f).ToString("0", inv)} % lit, below the horizon";
        return $"NIGHT VISION  -  {light} lux  -  {moon}  -  {(Palette == 1 ? "white P45" : "green P43")}";
    }
}
