using System;
using System.Globalization;
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;

namespace Hrot.ScenarioEditor.Map
{
    /// <summary>
    /// ⭐ <c>CE-1289</c> (folds in <c>CE-3151</c>; docs/DESIGN_Map_3D_Mode.md §6ac) — where the cursor is on the map:
    /// the local flat point (X east, Y north, Z up — metres) and, when the host has a geographic transform, the same point as WGS-84.
    /// </summary>
    public readonly record struct MapCursorSample(Vector3 Local, bool HasGeo, double Latitude, double Longitude, double Altitude);

    /// <summary>
    /// ⭐ <c>CE-1289</c> — the map's cursor reading, shared by the status bar. One per map canvas (a <see cref="MapCanvas"/> resource,
    /// put there by <see cref="MapInteractionPack.AttachMapLayers"/>), written by <see cref="MapCursorLayer"/>, read by
    /// <see cref="Hrot.UI.Common.Panels.MapCursorStatusBarSection"/>.
    ///
    /// <para>⭐ The reading HOLDS when the cursor leaves the map (over a panel, over the status bar itself): the status bar is not the
    /// map, so blanking it on the way to look at it would make it unreadable. It is empty only until the cursor has first been over the map.</para>
    /// </summary>
    public sealed class MapCursorReadout
    {
        private MapCursorSample? _latest;
        private string _flat = "";
        private string _geo = "";
        private string _text = "";

        /// <summary>The last reading; <c>null</c> until the cursor has been over the map.</summary>
        public MapCursorSample? Latest => _latest;

        /// <summary>The local flat reading, e.g. <c>x 1234.5  y -567.8  z 12.3 m</c>; empty until the first reading.</summary>
        public string FlatText => _flat;

        /// <summary>The geodetic reading, e.g. <c>52.520008° N  13.404954° E  alt 34.0 m</c>; empty when the host has no transform.</summary>
        public string GeoText => _geo;

        /// <summary>The whole status-bar text: the flat reading, then (when there is one) the geodetic one; empty until the first reading.</summary>
        public string Text => _text;

        /// <summary>Stores a reading (the strings are formatted only when it differs from the last — the cursor often rests).</summary>
        public void Set(MapCursorSample sample)
        {
            if (_latest is { } last && last.Equals(sample)) return;
            _latest = sample;
            _flat = MapCursorFormat.Flat(sample.Local);
            _geo = sample.HasGeo ? MapCursorFormat.Geo(sample.Latitude, sample.Longitude, sample.Altitude) : "";
            _text = _geo.Length == 0 ? _flat : _flat + "   |   " + _geo;
        }
    }

    /// <summary>⭐ <c>CE-1289</c> — the text of a cursor reading (pure; invariant culture, so a decimal point is a point on every machine).</summary>
    public static class MapCursorFormat
    {
        /// <summary>The local flat point: metres east (x), north (y), up (z), one decimal.</summary>
        public static string Flat(Vector3 local)
            => string.Create(CultureInfo.InvariantCulture, $"x {local.X:F1}  y {local.Y:F1}  z {local.Z:F1} m");

        /// <summary>A WGS-84 position: degrees to six decimals (about 0.1 m) with the hemisphere letter, and the altitude in metres.</summary>
        public static string Geo(double latitudeDeg, double longitudeDeg, double altitudeM)
            => string.Create(CultureInfo.InvariantCulture,
                $"{Math.Abs(latitudeDeg):F6}° {(latitudeDeg < 0 ? 'S' : 'N')}  {Math.Abs(longitudeDeg):F6}° {(longitudeDeg < 0 ? 'W' : 'E')}  alt {altitudeM:F1} m");
    }

    /// <summary>
    /// ⭐ <c>CE-1289</c> — measures the ground point under the cursor each frame, in BOTH views, and hands it to the
    /// <see cref="MapCursorReadout"/>. Attached by <see cref="MapInteractionPack.AttachMapLayers"/> on every map host.
    ///
    /// <para>The point is the camera's own pick — <c>MapCamera.ScreenToWorld3D</c>: in 2-D the <c>ScreenToWorld</c> point (its height is
    /// the terrain's at that spot, <paramref name="groundHeight"/>), in 3-D the ray's hit (terrain, entity box, or the ground plane;
    /// <c>MapCamera3D.Pick</c>) with its own height — the same call the click and hover already make, so it costs a cached lookup.
    /// The geodetic form goes through the host's <see cref="Fdp.Modules.Geographic.IGeographicTransform"/> world singleton, whose origin the
    /// terrain sets (<c>DESIGN_Geo_Origin.md</c> §4).</para>
    ///
    /// <para>Nothing is measured while the mouse belongs to a UI surface (<c>IInputProvider.IsMouseCaptured</c>) — the reading holds.</para>
    /// </summary>
    public sealed class MapCursorLayer : IMapLayer
    {
        private readonly MapCanvas _canvas;
        private readonly MapCursorReadout _readout;
        private readonly Func<EntityRepository?> _world;
        private readonly Func<float, float, float>? _groundHeight;

        public MapCursorLayer(MapCanvas canvas, MapCursorReadout readout, Func<EntityRepository?> world, Func<float, float, float>? groundHeight)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
            _readout = readout ?? throw new ArgumentNullException(nameof(readout));
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _groundHeight = groundHeight;
        }

        public string Name => "MapCursor";
        public int LayerBitIndex => -1;

        public void Update(float dt)
        {
            var input = _canvas.Input;
            if (input.IsMouseCaptured) return;   // over a panel / the status bar: hold the last reading

            var local = Measure(_canvas.Camera, input.MousePosition, _groundHeight);
            if (!local.HasValue) return;

            var p = local.Value;
            var geo = _world() is { } w && w.HasSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>()
                ? w.GetSingletonManaged<Fdp.Modules.Geographic.IGeographicTransform>() : null;
            if (geo is null) { _readout.Set(new MapCursorSample(p, false, 0, 0, 0)); return; }
            var (lat, lon, alt) = geo.ToGeodetic(p);
            _readout.Set(new MapCursorSample(p, true, lat, lon, alt));
        }

        /// <summary>
        /// The local point under <paramref name="screen"/> for <paramref name="camera"/> (<c>null</c>: not finite — a camera looking at the
        /// sky, or a poisoned one). The pure part, so a rail drives it with a fake camera.
        /// </summary>
        public static Vector3? Measure(Fdp.Toolkit.Vis2D.Components.MapCamera camera, Vector2 screen, Func<float, float, float>? groundHeight)
        {
            var p = camera.ScreenToWorld3D(screen);
            if (!camera.Is3D) p = new Vector3(p.X, p.Y, groundHeight?.Invoke(p.X, p.Y) ?? 0f);   // 2-D: the ground under the point
            return float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z) ? p : null;
        }

        public void Draw(RenderContext ctx) { }
        public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
        public Entity? PickEntity(Vector2 worldPos) => null;
    }
}
