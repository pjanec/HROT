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
    public static bool ThermalOffered => false;

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

    public void Update(float dt)
    {
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
    public SceneAtmosphere Apply(in WeatherSample s, bool hasSample, SceneAtmosphere normal)
    {
        var settings = new SensorPostSettings
        {
            Mode = Mode, Palette = Palette, Halo = Halo, SensorResolution = SensorResolution, Noise = Noise, Vignette = Vignette,
            Gain = 1f, NoiseSigma = 0f, SensorLines = NightVisionLines, TargetMean = AutoGain ? 0.36f : 0f,
        };
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
        int kLux = (int)MathF.Round(MathF.Log10(MathF.Max(Lux, 1e-6f)) * 100f), kEl = (int)MoonElevationDeg, kLit = (int)(MoonIllumination * 100f);
        if ((int)Mode != _kMode || _keyVersion != _kKeyVersion || Palette != _kPalette || kLux != _kLux || kEl != _kMoonEl || kLit != _kMoonLit)
        {
            _kMode = (int)Mode; _kKeyVersion = _keyVersion; _kPalette = Palette; _kLux = kLux; _kMoonEl = kEl; _kMoonLit = kLit;
            _legend = (Mode == SceneSensor.NightVision ? NightVisionLegend() : "THERMAL") + (CycleKey is { } k ? $"  -  {k}: next view" : "");
        }
        int fs = 16, w = Raylib.MeasureText(_legend, fs) + 24;
        int x = (int)(vp.X / 2f - w / 2f), y = 34;
        Raylib.DrawRectangle(x, y, w, fs + 12, new Color((byte)16, (byte)16, (byte)20, (byte)225));
        Raylib.DrawRectangleLines(x, y, w, fs + 12, new Color((byte)58, (byte)58, (byte)68, (byte)255));
        Raylib.DrawText(_legend, x + 12, y + 6, fs, new Color((byte)200, (byte)200, (byte)208, (byte)255));
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
