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

        /// <summary>⭐ CE-1480 G5 — moves whenever anything a view-dependent gizmo picture reads actually changes (the rectangle or the frustum, the scale, the
        /// mode, active): writing the SAME view again does not move it, so a still camera leaves every held picture valid.</summary>
        public long Version { get; private set; }

        private readonly Vector4[] _seenPlanes = new Vector4[6];
        private Vector2 _seenMin, _seenMax;
        private float _seenMpp;
        private bool _seenIs3D, _seenActive;

        private void Touch()
        {
            bool same = _seenActive == Active && _seenIs3D == _is3D && _seenMpp == MetresPerPixel
                        && (!Active || (_is3D ? SamePlanes() : _seenMin == _min && _seenMax == _max));
            if (same) return;
            Version++;
            _seenActive = Active; _seenIs3D = _is3D; _seenMpp = MetresPerPixel; _seenMin = _min; _seenMax = _max;
            System.Array.Copy(_planes, _seenPlanes, 6);
        }

        private bool SamePlanes()
        {
            for (int i = 0; i < 6; i++) if (_seenPlanes[i] != _planes[i]) return false;
            return true;
        }

        /// <summary>The 2-D map: its world rectangle and scale.</summary>
        public void Write2D(Vector2 min, Vector2 max, float metresPerPixel)
        {
            float m = MathF.Max(MarginMetres, MarginPixels2D * metresPerPixel);
            _min = Vector2.Min(min, max) - new Vector2(m); _max = Vector2.Max(min, max) + new Vector2(m);
            MetresPerPixel = metresPerPixel; _is3D = false;
            Active = float.IsFinite(_min.X) && float.IsFinite(_max.Y) && _max.X > _min.X && _max.Y > _min.Y;
            Touch();
        }

        /// <summary>The 3-D map: its camera's frustum and scale at the look point.</summary>
        public void Write3D(MapCamera3D camera)
        {
            _is3D = true;
            MetresPerPixel = camera.LodMetresPerPixel;   // ⭐ CE-4408 — the level of detail reads the camera's REAL distance (the eye's height above the ground along the view), not the pivot's alone
            Active = camera.TryFrustumPlanes(_planes);
            Touch();
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
            Touch();
        }

        public bool Is3D => Active && _is3D;

        /// <summary>⭐ CE-4388 — a ground box overlaps the 2-D view (with the margin); true in 3-D (a conservative answer) and when not active.</summary>
        public bool Overlaps(Vector2 min, Vector2 max)
        {
            if (!Active || _is3D) return true;
            return max.X >= _min.X && min.X <= _max.X && max.Y >= _min.Y && min.Y <= _max.Y;
        }

        /// <summary>Back to "everything in view".</summary>
        public void Clear() { Active = false; Touch(); }

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
            return Contains(p);
        }

        /// <summary>⭐ CE-4388 (D-6) — a HROT-space point is in view (with the margin): the one test, for entities and for a global gizmo's own geometry (a door).
        /// Not <see cref="Active"/> ⇒ true.</summary>
        public bool Contains(Vector3 p)
        {
            if (!Active) return true;
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
