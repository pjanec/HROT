using System.Globalization;
using System.Numerics;
using Fdp.Core;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;
using Hrot.ScenarioEditor.Map;
using Hrot.UI.Common.Panels;
using Xunit;

namespace Hrot.Presentation.Tests.Map2D;

/// <summary>
/// ⭐ <c>CE-1289</c> (folds in <c>CE-3151</c>; docs/DESIGN_Map_3D_Mode.md §6ac) — the status bar shows the point under the
/// map cursor: local flat coordinates AND latitude / longitude with altitude, on the 2-D and the 3-D map.
/// 🔒 User, 2026-10-23: <i>"in status bar we should see the coordinates of point under cursor (2d map as well 3d map) - both flat
/// coordinates, lat/long coordinates with altitude"</i>. The rails drive the pure parts and the layer over a fake input; the ImGui text
/// itself needs a frame and is not seen here.
/// </summary>
public sealed class MapCursorTests
{
    private sealed class FakeInput : IInputProvider
    {
        public Vector2 MousePosition { get; set; }
        public Vector2 MouseDelta => Vector2.Zero;
        public float MouseWheelMove => 0f;
        public bool Captured { get; set; }
        public bool IsMouseButtonPressed(MapMouseButton b) => false;
        public bool IsMouseButtonDown(MapMouseButton b) => false;
        public bool IsMouseButtonReleased(MapMouseButton b) => false;
        public bool IsKeyPressed(MapKeyboardKey k) => false;
        public bool IsKeyDown(MapKeyboardKey k) => false;
        public bool IsKeyReleased(MapKeyboardKey k) => false;
        public int GetKeyPressed() => 0;
        public bool IsMouseCaptured => Captured;
        public bool IsKeyboardCaptured => false;
    }

    private static EntityRepository WorldWithGeo(double lat, double lon, double alt)
    {
        var world = new EntityRepository();
        var geo = new WGS84Transform();
        geo.SetOrigin(lat, lon, alt);
        world.SetSingletonManaged<IGeographicTransform>(geo);
        return world;
    }

    /// <summary>A 2-D camera whose screen origin is the world origin: world = (screenX, −screenY) — north-up.</summary>
    private static MapCamera Camera2D() => new() { Zoom = 1f, Target = Vector2.Zero, Offset = Vector2.Zero };

    // ── the text ────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheFlatText_IsMetres_OneDecimal_WithAPointOnEveryMachine()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");   // a decimal COMMA locale
            Assert.Equal("x 1234.5  y -567.8  z 12.3 m", MapCursorFormat.Flat(new Vector3(1234.5f, -567.8f, 12.3f)));
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Theory]
    [InlineData(52.520008, 13.404954, 34.04, "52.520008° N  13.404954° E  alt 34.0 m")]
    [InlineData(-33.8688, 151.2093, 0.0, "33.868800° S  151.209300° E  alt 0.0 m")]
    [InlineData(40.7128, -74.006, -3.26, "40.712800° N  74.006000° W  alt -3.3 m")]
    public void TheGeoText_IsDegreesWithHemisphere_AndTheAltitude(double lat, double lon, double alt, string expected)
        => Assert.Equal(expected, MapCursorFormat.Geo(lat, lon, alt));

    // ── the readout ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheReadout_IsEmptyUntilTheCursorHasBeenOverTheMap_ThenShowsFlatAndGeo()
    {
        var r = new MapCursorReadout();
        Assert.Null(r.Latest);
        Assert.Equal("", r.Text);

        r.Set(new MapCursorSample(new Vector3(10, 20, 3), true, 50.0, 14.0, 203.0));

        Assert.Equal("x 10.0  y 20.0  z 3.0 m   |   50.000000° N  14.000000° E  alt 203.0 m", r.Text);
        Assert.Equal("x 10.0  y 20.0  z 3.0 m", r.FlatText);
        Assert.Equal("50.000000° N  14.000000° E  alt 203.0 m", r.GeoText);
    }

    [Fact]
    public void WithNoGeographicTransform_TheReadoutIsTheFlatPointAlone()
    {
        var r = new MapCursorReadout();
        r.Set(new MapCursorSample(new Vector3(1, 2, 0), false, 0, 0, 0));
        Assert.Equal("x 1.0  y 2.0  z 0.0 m", r.Text);
        Assert.Equal("", r.GeoText);
    }

    // ── the measurement ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void In2D_TheFlatPointIsTheNorthUpWorldPoint_AndTheAltitudeIsTheTerrainsThere()
    {
        var p = MapCursorLayer.Measure(Camera2D(), new Vector2(10, 20), (x, y) => x * 0.1f);

        Assert.NotNull(p);
        Assert.Equal(10f, p!.Value.X, 3);
        Assert.Equal(-20f, p.Value.Y, 3);     // north-up: screen-down is world-south
        Assert.Equal(1f, p.Value.Z, 3);       // the ground height at x = 10
    }

    [Fact]
    public void In2D_WithNoTerrain_TheAltitudeIsZero()
        => Assert.Equal(0f, MapCursorLayer.Measure(Camera2D(), new Vector2(10, 20), groundHeight: null)!.Value.Z);

    [Fact]
    public void ACameraThatCannotSayWhereTheCursorIs_GivesNoReading()
    {
        var camera = Camera2D();
        camera.InnerCamera.Zoom = float.NaN;     // a poisoned camera: every conversion is NaN
        Assert.Null(MapCursorLayer.Measure(camera, new Vector2(10, 20), null));
    }

    /// <summary>In 3-D the point is the camera's own pick WITH its height: looking straight down at a look-at point of the relief, the
    /// centre pixel is that point.</summary>
    [Fact]
    public void In3D_TheCentrePixelIsWhereTheCameraLooks_WithTheReliefHeight()
    {
        var cam = new MapCamera3D
        {
            ViewportOverride = new Vector2(1280, 720),
            GroundHeight = (_, _) => 25f,
            Pose = new CameraPose(new Vector3(100, 200, 25), 100f, MapCamera3D.NorthUpYaw, -1.5f),
        };
        cam.Update(0.016f);

        var p = MapCursorLayer.Measure(cam, new Vector2(640, 360), cam.GroundHeight);

        Assert.NotNull(p);
        Assert.InRange(p!.Value.X, 95f, 105f);
        Assert.InRange(p.Value.Y, 195f, 205f);
        Assert.Equal(25f, p.Value.Z, 1);
    }

    // ── the layer ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Each frame the layer reads the cursor and writes the readout, geodetic through the world's transform (whose origin the terrain sets);
    /// off the map (the mouse captured by a panel or by the status bar itself) the reading HOLDS rather than blanking.
    /// ⛔ Red-proof: drop the <c>IsMouseCaptured</c> guard and the held reading is overwritten with the point under the panel.
    /// </summary>
    [Fact]
    public void TheLayer_WritesTheReading_GeodeticThroughTheWorldsTransform_AndHoldsItOffTheMap()
    {
        var world = WorldWithGeo(50.0, 14.0, 200.0);
        var input = new FakeInput { MousePosition = new Vector2(0, 0) };
        var canvas = new MapCanvas(input) { Camera = Camera2D() };
        var readout = new MapCursorReadout();
        var layer = new MapCursorLayer(canvas, readout, () => world, groundHeight: (_, _) => 12f);

        layer.Update(0.016f);
        var origin = Assert.IsType<MapCursorSample>(readout.Latest);
        Assert.Equal(new Vector3(0, 0, 12), origin.Local);
        Assert.True(origin.HasGeo);
        Assert.Equal(50.0, origin.Latitude, 4);
        Assert.Equal(14.0, origin.Longitude, 4);
        Assert.Equal(212.0, origin.Altitude, 1);      // the origin's altitude + the ground's height

        input.MousePosition = new Vector2(1000, 0);   // 1 km east: the longitude moves, the latitude does not
        layer.Update(0.016f);
        var east = readout.Latest!.Value;
        Assert.Equal(new Vector3(1000, 0, 12), east.Local);
        Assert.True(east.Longitude > origin.Longitude + 0.005, $"longitude {east.Longitude} did not move east");
        Assert.Equal(origin.Latitude, east.Latitude, 4);

        input.Captured = true;                        // the cursor is over a panel (or the status bar)
        input.MousePosition = new Vector2(5000, 5000);
        layer.Update(0.016f);
        Assert.Equal(east, readout.Latest!.Value);    // held, not blanked, not updated
    }

    [Fact]
    public void TheLayer_WithNoTransformInTheWorld_WritesTheFlatPointAlone()
    {
        var input = new FakeInput { MousePosition = new Vector2(30, 40) };
        var canvas = new MapCanvas(input) { Camera = Camera2D() };
        var readout = new MapCursorReadout();

        new MapCursorLayer(canvas, readout, () => new EntityRepository(), null).Update(0.016f);

        Assert.False(readout.Latest!.Value.HasGeo);
        Assert.Equal(new Vector3(30, -40, 0), readout.Latest.Value.Local);
    }

    // ── the status-bar section ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheSection_ShowsAPlaceholderUntilAReading_ThenTheReadoutsText_FoundThroughTheCanvas()
    {
        var canvas = new MapCanvas(new FakeInput()) { Camera = Camera2D() };
        var section = new MapCursorStatusBarSection(() => canvas);
        Assert.Equal("Cursor: -", section.TextNow());                   // no readout on the canvas yet

        var readout = new MapCursorReadout();
        canvas.AddResource(readout);
        Assert.Equal("Cursor: -", section.TextNow());                   // a readout, no reading

        readout.Set(new MapCursorSample(new Vector3(1, 2, 3), false, 0, 0, 0));
        Assert.Equal("x 1.0  y 2.0  z 3.0 m", section.TextNow());

        Assert.Equal("Cursor: -", new MapCursorStatusBarSection(() => null).TextNow());   // a host with no canvas yet
    }
}
