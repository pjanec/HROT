using System.Numerics;
using Raylib_cs;
using FDP.Toolkit.Vis2D.Abstractions;
using FDP.Toolkit.ImGui.Abstractions;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using CarKinem.Core;
using CarKinem.Trajectory;

namespace Bagira.SimHost.Visualization
{
    /// <summary>
    /// Draws the trajectory path for the currently selected entity.
    /// Mirrors <c>Fdp.Examples.CarKinem.Visualization.TrajectoryMapLayer</c>.
    /// </summary>
    public class SimHostTrajectoryLayer : IMapLayer
    {
        private readonly TrajectoryPoolManager _pool;
        private readonly ISimulationView       _view;
        private readonly IInspectorContext     _inspector;

        public string Name        => "Trajectories";
        public int    LayerBitIndex => -1; // always-visible overlay

        public SimHostTrajectoryLayer(TrajectoryPoolManager pool, ISimulationView view, IInspectorContext inspector)
        {
            _pool      = pool;
            _view      = view;
            _inspector = inspector;
        }

        public void Update(float dt) { }

        public void Draw(RenderContext ctx)
        {
            var sel = _inspector.SelectedEntity;
            if (sel == null || !_view.IsAlive(sel.Value)) return;
            if (!_view.HasComponent<NavState>(sel.Value))  return;

            var nav = _view.GetComponentRO<NavState>(sel.Value);
            if (nav.Mode == KinematicsMode.CustomTrajectory)
                RenderTrajectory(nav.TrajectoryId, nav.ProgressS, new Color(180, 180, 180, 160));
        }

        private void RenderTrajectory(int id, float progressS, Color color)
        {
            if (!_pool.TryGetTrajectory(id, out var traj)) return;
            if (!traj.Waypoints.IsCreated || traj.Waypoints.Length < 2) return;
            if (traj.IsLooped == 0 && progressS >= traj.TotalLength - 0.01f) return;

            for (int i = 0; i < traj.Waypoints.Length - 1; i++)
            {
                Raylib.DrawLineEx(
                    traj.Waypoints[i].Position,
                    traj.Waypoints[i+1].Position,
                    1.5f, color);
            }

            // Highlight current progress point
            float clamped = System.Math.Clamp(progressS / System.Math.Max(traj.TotalLength, 0.001f), 0f, 1f);
            int idx = (int)(clamped * (traj.Waypoints.Length - 1));
            idx = System.Math.Clamp(idx, 0, traj.Waypoints.Length - 1);
            Raylib.DrawCircleV(traj.Waypoints[idx].Position, 3f, Color.Orange);
        }

        public bool HandleInput(Vector2 worldPos, MouseButton button, bool pressed) => false;
        public Entity? PickEntity(Vector2 worldPos) => null;
    }
}
