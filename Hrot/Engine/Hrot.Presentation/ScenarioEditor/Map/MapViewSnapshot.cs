using System;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Vis2D;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Components;
using Fdp.Toolkit.Vis3D;

namespace Hrot.ScenarioEditor.Map
{
    /// <summary>
    /// ⭐ CE-4388 (docs/DESIGN_Map_View_Filter.md, R-1049) — what ONE map shows this frame: in 2-D its world rectangle, in 3-D its camera's frustum (the planes it
    /// will draw with — <see cref="MapCamera3D.TryFrustumPlanes"/>), and its scale. Written once a frame by <see cref="MapViewSnapshotLayer"/> from the canvas's
    /// CURRENT camera (so 3-D culls by the 3-D view — today's <c>MapCameraViewport</c> held the 2-D rectangle even in 3-D); read by the gizmo system as its
    /// <see cref="IGizmoViewFilter"/>. Never written (a headless host, before the first frame) ⇒ not <see cref="Active"/> ⇒ everything in view (D-7).
    /// ⛔ No allocation after construction.
    /// </summary>
    public sealed class MapViewSnapshot : IGizmoViewFilter
    {
        /// <summary>The least margin round the view (m): a body's reach from its reference point (the 3-D bodies' cull cube, CE-4388).</summary>
        public const float MarginMetres = 40f;

        /// <summary>In 2-D the margin also covers a symbol's screen-sized decoration (a label to its right): this many pixels at the map's scale.</summary>
        public const float MarginPixels2D = 256f;

        private readonly Vector4[] _planes = new Vector4[6];
        private bool _is3D;
        private Vector2 _min, _max;

        public bool Active { get; private set; }
        public float MetresPerPixel { get; private set; }

        /// <summary>The 2-D map: its world rectangle and scale.</summary>
        public void Write2D(Vector2 min, Vector2 max, float metresPerPixel)
        {
            float m = MathF.Max(MarginMetres, MarginPixels2D * metresPerPixel);
            _min = Vector2.Min(min, max) - new Vector2(m); _max = Vector2.Max(min, max) + new Vector2(m);
            MetresPerPixel = metresPerPixel; _is3D = false;
            Active = float.IsFinite(_min.X) && float.IsFinite(_max.Y) && _max.X > _min.X && _max.Y > _min.Y;
        }

        /// <summary>The 3-D map: its camera's frustum and scale at the look point.</summary>
        public void Write3D(MapCamera3D camera)
        {
            _is3D = true;
            MetresPerPixel = camera.MetresPerPixel;
            Active = camera.TryFrustumPlanes(_planes);
        }

        /// <summary>The current camera of <paramref name="canvas"/>, whichever it is.</summary>
        public void Write(MapCanvas canvas)
        {
            if (canvas.Camera is MapCamera3D c3 && c3.Is3D) { Write3D(c3); return; }
            if (canvas.Camera is MapCamera c2 && c2.Zoom > 0f)
            {
                var a = c2.ScreenToWorld(Vector2.Zero);
                var b = c2.ScreenToWorld(c2.Viewport);
                Write2D(a, b, 1f / c2.Zoom);
                return;
            }
            Active = false;
        }

        /// <summary>Back to "everything in view".</summary>
        public void Clear() => Active = false;

        public bool InView(ISimulationView view, Entity entity)
        {
            if (!Active) return true;
            // ⭐ CE-4388 — measured: through the ISimulationView interface's GENERIC methods (three generic-virtual calls per entity) this test cost ≈ 6 ms a
            //   frame over 12 550 entity slots; on the concrete repository the same reads are direct calls (MapCullingSystem reads ~14 000 positions in 0.28 ms)
            Vector3 p;
            if (view is EntityRepository repo)
            {
                if (!repo.IsComponentTypeRegistered<SimTransform>() || !repo.HasComponent<SimTransform>(entity)) return true;
                p = repo.GetComponentRO<SimTransform>(entity).Position;
            }
            else
            {
                if (!view.HasComponent<SimTransform>(entity)) return true;
                p = view.GetComponentRO<SimTransform>(entity).Position;
            }
            return _is3D ? ViewFrustum.AroundInside(_planes, p, MarginMetres)
                         : p.X >= _min.X && p.X <= _max.X && p.Y >= _min.Y && p.Y <= _max.Y;
        }
    }

    /// <summary>
    /// ⭐ CE-4388 — writes the map's <see cref="MapViewSnapshot"/> once a frame (in <see cref="Update"/>, which the hosts run before the kernel), from the canvas's
    /// current camera. Draws nothing, picks nothing. Attached by <c>MapInteractionPack.AttachMapLayers</c> — every map host, by construction.
    /// </summary>
    public sealed class MapViewSnapshotLayer : IMapLayer
    {
        private readonly MapViewSnapshot _snapshot;
        private readonly MapCanvas _canvas;

        public MapViewSnapshotLayer(MapViewSnapshot snapshot, MapCanvas canvas)
        {
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        }

        public string Name => "Map view";
        public int LayerBitIndex => -1;
        public void Update(float dt) => _snapshot.Write(_canvas);
        public void Draw(RenderContext ctx) { }
        public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
        public Entity? PickEntity(Vector2 worldPos) => null;
    }
}
