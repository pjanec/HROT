using System.Numerics;
using Raylib_cs;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.ImGui.Abstractions;
using Fdp.Kernel;
using Fdp.ModuleHost.Abstractions;
using CarKinem.Core;
using CarKinem.Trajectory;
using Hrot.Map.Common.Components;
using Hrot.Map.Common;

namespace Hrot.SimHost.Visualization
{
    /// <summary>
    /// Draws the trajectory path for the currently selected entity.
    ///
    /// <para>
    /// Extended in ROUTES1-T011 to also render:
    /// <list type="bullet">
    ///   <item>The personal route waypoints when the vehicle has a <see cref="PersonalRouteRef"/>.</item>
    ///   <item>The shared route waypoints when the vehicle follows a trajectory whose ID
    ///         matches a <see cref="RouteTrajectoryCache"/> on a route entity.</item>
    /// </list>
    /// </para>
    /// </summary>
    public class SimHostTrajectoryLayer : IMapLayer
    {
        private readonly TrajectoryPoolManager _pool;
        private readonly ISimulationView       _view;
        private readonly IInspectorContext     _inspector;

        // CT-3: built once in the constructor; reused every Draw() to avoid per-frame
        // query allocation on the shared-route fallback path.
        private readonly EntityQuery           _routeQuery;

        public string Name        => "Trajectories";
        public int    LayerBitIndex => -1; // always-visible overlay

        // ── Test hooks ──────────────────────────────────────────────────────────

        /// <summary>
        /// When <c>true</c> Raylib draw calls are skipped; counter fields are still updated.
        /// </summary>
        public bool TestHook_SkipRaylibCalls { get; set; }

        /// <summary>Line-segment draw calls in the last <see cref="Draw"/> pass.</summary>
        public int TestHook_LineDrawCount { get; private set; }

        /// <summary>Circle draw calls (progress marker + waypoint handles) in the last <see cref="Draw"/> pass.</summary>
        public int TestHook_CircleDrawCount { get; private set; }

        public SimHostTrajectoryLayer(TrajectoryPoolManager pool, ISimulationView view, IInspectorContext inspector)
        {
            _pool       = pool;
            _view       = view;
            _inspector  = inspector;
            _routeQuery = _view.Query()
                .With<RouteTrajectoryCache>()
                .WithManaged<RoutePlan>()
                .Build();
        }

        public void Update(float dt) { }

        public void Draw(RenderContext ctx)
        {
            TestHook_LineDrawCount   = 0;
            TestHook_CircleDrawCount = 0;

            var sel = _inspector.SelectedEntity;
            if (sel == null || !_view.IsAlive(sel.Value)) return;
            if (!_view.HasComponent<NavState>(sel.Value))  return;

            var nav = _view.GetComponentRO<NavState>(sel.Value);

            // ── 1. Raw trajectory from pool ─────────────────────────────────────
            if (nav.Mode == KinematicsMode.CustomTrajectory)
                RenderTrajectory(nav.TrajectoryId, nav.ProgressS, new Color(180, 180, 180, 160));

            // ── 2. Personal route waypoints ─────────────────────────────────────
            if (_view.HasComponent<PersonalRouteRef>(sel.Value))
            {
                ref readonly var routeRef = ref _view.GetComponentRO<PersonalRouteRef>(sel.Value);
                if (_view.IsAlive(routeRef.RouteEntity)
                 && _view.HasManagedComponent<RoutePlan>(routeRef.RouteEntity))
                {
                    var plan = _view.GetManagedComponentRO<RoutePlan>(routeRef.RouteEntity);
                    RenderRoutePlanWaypoints(plan, Color.Orange);
                }
            }
            // ── 3. Shared route waypoints (vehicle follows a named route entity) ─
            else if (nav.Mode == KinematicsMode.CustomTrajectory && nav.TrajectoryId > 0)
            {
                foreach (var routeEntity in _routeQuery)
                {
                    ref readonly var cache = ref _view.GetComponentRO<RouteTrajectoryCache>(routeEntity);
                    if (cache.TrajectoryId != nav.TrajectoryId) continue;

                    var plan = _view.GetManagedComponentRO<RoutePlan>(routeEntity);
                    RenderRoutePlanWaypoints(plan, new Color(0xFF, 0xD7, 0x00, 0xC0)); // translucent yellow
                    break;
                }
            }
        }

        private void RenderTrajectory(int id, float progressS, Color color)
        {
            if (!_pool.TryGetTrajectory(id, out var traj)) return;
            if (!traj.Waypoints.IsCreated || traj.Waypoints.Length < 2) return;
            if (traj.IsLooped == 0 && progressS >= traj.TotalLength - 0.01f) return;

            for (int i = 0; i < traj.Waypoints.Length - 1; i++)
            {
                if (!TestHook_SkipRaylibCalls)
                    Raylib.DrawLineEx(
                        traj.Waypoints[i].Position,
                        traj.Waypoints[i + 1].Position,
                        1.5f, color);
                TestHook_LineDrawCount++;
            }

            // Highlight current progress point.
            float clamped = System.Math.Clamp(progressS / System.Math.Max(traj.TotalLength, 0.001f), 0f, 1f);
            int idx = System.Math.Clamp((int)(clamped * (traj.Waypoints.Length - 1)), 0, traj.Waypoints.Length - 1);

            if (!TestHook_SkipRaylibCalls)
                Raylib.DrawCircleV(traj.Waypoints[idx].Position, 3f, Color.Orange);
            TestHook_CircleDrawCount++;
        }

        /// <summary>
        /// Draws waypoint positions from a <see cref="RoutePlan"/> as connected line segments
        /// and vertex circles in <paramref name="color"/>.
        /// </summary>
        private void RenderRoutePlanWaypoints(RoutePlan plan, Color color)
        {
            if (plan.Waypoints == null || plan.Waypoints.Count < 2) return;

            int n = plan.Waypoints.Count;
            int segCount = plan.IsLoop ? n : n - 1;

            for (int i = 0; i < segCount; i++)
            {
                var a = new Vector2(plan.Waypoints[i].Position.X, plan.Waypoints[i].Position.Z);
                var b = new Vector2(plan.Waypoints[(i + 1) % n].Position.X, plan.Waypoints[(i + 1) % n].Position.Z);

                if (!TestHook_SkipRaylibCalls)
                    Raylib.DrawLineEx(a, b, 1.5f, color);
                TestHook_LineDrawCount++;
            }

            for (int i = 0; i < n; i++)
            {
                var pos = new Vector2(plan.Waypoints[i].Position.X, plan.Waypoints[i].Position.Z);

                if (!TestHook_SkipRaylibCalls)
                    Raylib.DrawCircleV(pos, 4f, color);
                TestHook_CircleDrawCount++;
            }
        }

        public bool HandleInput(Vector2 worldPos, MouseButton button, bool pressed) => false;
        public Entity? PickEntity(Vector2 worldPos) => null;
    }
}

