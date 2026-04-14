using System.Numerics;
using Raylib_cs;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Presentation.Abstractions; 
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using CarKinem.Core;
using CarKinem.Trajectory;
using CarKinem.Formation;

namespace Fdp.Examples.CarKinem.Visualization
{
    public class TrajectoryMapLayer : IMapLayer
    {
        private readonly TrajectoryPoolManager _pool;
        private readonly ISimulationView _view;
        private readonly IInspectorContext _inspector;

        public string Name => "Trajectories";
        public int LayerBitIndex => -1; // Always visible overlay

        public TrajectoryMapLayer(TrajectoryPoolManager pool, ISimulationView view, IInspectorContext inspector)
        {
            _pool = pool;
            _view = view;
            _inspector = inspector;
        }

        public void Update(float dt) { }

        public void Draw(RenderContext ctx)
        {
            var selectedEntity = _inspector.SelectedEntity;
            if (selectedEntity == null) return;

            // Render Active Trajectory for selected entity
            if (!_view.HasComponent<NavState>(selectedEntity.Value)) return;
            
            var nav = _view.GetComponentRO<NavState>(selectedEntity.Value);
            
            if (nav.Mode == KinematicsMode.CustomTrajectory)
            {
                RenderTrajectory(nav.TrajectoryId, nav.ProgressS, ctx, new Color(180, 180, 180, 128));
            }
            else if (nav.Mode == KinematicsMode.Formation)
            {
                if (_view.HasComponent<FormationMember>(selectedEntity.Value))
                {
                    var member = _view.GetComponentRO<FormationMember>(selectedEntity.Value);
                    
                    // Leader entity is just an index (int). We must assume generation to find it?
                    // But we don't have generation info in FormationMember.
                    // This is a design flaw in the example, but we work with what we have.
                    // Let's assume active entity at that index.
                    
                    // We can query component for that index if we had a way.
                    // ISimulationView doesn't easily allow random access without generation check.
                    // However, we can try to guess or skip if invalid.
                    
                    // Actually, we can assume the leader might have a trajectory.
                    // But without Entity struct, we can't safely access components via View usually...
                    // Wait, View.GetComponentRO<T>(Entity e) requires Entity.
                    // We can reconstruct an Entity if we trust the index is valid.
                    
                    // Let's iterate all entities and find the one with that index? Too slow.
                    // For now, let's skip Formation trajectory rendering unless we solve this.
                    // Or cheat:
                    // var leader = new Entity(member.LeaderEntityId, 0); // Risky
                }
            }
        }
        
        private void RenderTrajectory(int trajectoryId, float progressS, RenderContext ctx, Color color)
        {
            if (!_pool.TryGetTrajectory(trajectoryId, out var trajectory)) return;
            if (!trajectory.Waypoints.IsCreated) return;

            // If finished and not looped, draw nothing
            if (trajectory.IsLooped == 0 && progressS >= trajectory.TotalLength - 0.01f) return;

            if (trajectory.Interpolation == TrajectoryInterpolation.Linear)
            {
                RenderLinear(trajectory, progressS, color);
            }
            else
            {
                // Orange-ish for spline
                RenderHermiteSmooth(trajectory, progressS, new Color(255, 161, 0, 200)); 
            }
        }

        private void RenderLinear(CustomTrajectory trajectory, float progressS, Color color)
        {
            int nextIndex = -1;
            for (int i = 0; i < trajectory.Waypoints.Length; i++)
            {
                if (trajectory.Waypoints[i].CumulativeDistance > progressS)
                {
                    nextIndex = i;
                    break;
                }
            }

            if (nextIndex != -1)
            {
                var (currentPos, _, _) = _pool.SampleTrajectory(trajectory.Id, progressS);
                Raylib.DrawLineEx(currentPos, trajectory.Waypoints[nextIndex].Position, 0.15f, color);
                
                for (int i = nextIndex; i < trajectory.Waypoints.Length - 1; i++)
                {
                    Vector2 p1 = trajectory.Waypoints[i].Position;
                    Vector2 p2 = trajectory.Waypoints[i + 1].Position;
                    Raylib.DrawLineEx(p1, p2, 0.15f, color);
                }
            }
        }

        private void RenderHermiteSmooth(CustomTrajectory trajectory, float progressS, Color color)
        {
            const float stepSize = 1.0f;
            float currentDist = progressS;
            float totalLen = trajectory.TotalLength;

            Vector2 prevPos;
            {
                var (pos, _, _) = _pool.SampleTrajectory(trajectory.Id, currentDist);
                prevPos = pos;
            }

            // Limit iterations to prevent freezing on huge paths or bugs
            int maxSteps = 1000;
            int steps = 0;

            while (currentDist < totalLen && steps < maxSteps)
            {
                currentDist += stepSize;
                if (currentDist > totalLen) currentDist = totalLen;

                var (nextPos, _, _) = _pool.SampleTrajectory(trajectory.Id, currentDist);
                Raylib.DrawLineEx(prevPos, nextPos, 0.15f, color);
                prevPos = nextPos;
                
                steps++;
                if (currentDist >= totalLen) break;
            }
        }
        
        public bool OnMouseClick(Vector2 worldPos, RenderContext ctx) => false;
        public bool HandleInput(Vector2 worldPos, MouseButton button, bool isPressed)
        {
            return false;
        }

        public Entity? PickEntity(Vector2 worldPos)
        {
            // Trajectories are currently visual overlays for valid objects 
            // and don't support direct picking themselves.
            return null;
        }
    }
}
