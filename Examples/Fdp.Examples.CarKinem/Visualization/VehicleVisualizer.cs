using System.Numerics;
using Raylib_cs;
using Fdp.Kernel;
using Fdp.Toolkit.Vis2D.Abstractions;
using CarKinem.Core;
using CarKinem.Formation;
using Fdp.ModuleHost.Abstractions;
using Fdp.Examples.CarKinem.Components; // Ensure components are available
using System;
using System.Diagnostics;
using CarKinem.Trajectory; // Trajectory Pool
using Fdp.Kernel.Collections;
using ExampleCore = Fdp.Examples.CarKinem.Core;

namespace Fdp.Examples.CarKinem.Visualization;

public class VehicleVisualizer : IVisualizerAdapter
{
    public VehicleVisualizer()
    {
    }

    public Vector2? GetPosition(ISimulationView view, Entity entity)
    {
        if (view.HasComponent<SimTransform>(entity))
        {
            var tf = view.GetComponentRO<SimTransform>(entity);
            return new Vector2(tf.Position.X, tf.Position.Y);
        }
        return null;
    }

    public float GetHitRadius(ISimulationView view, Entity entity)
    {
        if (view.HasComponent<VehicleParams>(entity))
        {
             return view.GetComponentRO<VehicleParams>(entity).Length / 2f;
        }
        return 1.0f;
    }

    public void Render(ISimulationView view, Entity entity, Vector2 position, RenderContext ctx, bool isSelected, bool isHovered)
    {
        if (!view.HasComponent<SimTransform>(entity) || !view.HasComponent<VehicleParams>(entity))
            return;

        ref readonly var tf = ref view.GetComponentRO<SimTransform>(entity);
        ref readonly var parameters = ref view.GetComponentRO<VehicleParams>(entity);

        // Use extracted logic for color
        Color color = ExampleCore.ExampleVehiclePresets.GetColorForEntity(view, entity, parameters);

        // Highlight if selected
        if (isSelected)
        {
            // Keep original color but add strong highlight ring (see below)
        }
        else if (isHovered)
        {
            color = new Color((byte)Math.Min(color.R + 50, 255), (byte)Math.Min(color.G + 50, 255), (byte)Math.Min(color.B + 50, 255), (byte)255);
        }

        // Draw rotated rectangle
        // Adjusted for X-Forward convention
        Vector3 fwd3D = Vector3.Transform(Vector3.UnitX, tf.Rotation);
        Vector2 forward = new Vector2(fwd3D.X, fwd3D.Y);
        float rotationDeg = MathF.Atan2(forward.Y, forward.X) * (180.0f / MathF.PI);
        
        Rectangle rec = new Rectangle(position.X, position.Y, parameters.Length, parameters.Width);
        Vector2 origin = new Vector2(parameters.Length / 2, parameters.Width / 2); // Center of rotation
        
        Raylib.DrawRectanglePro(rec, origin, rotationDeg, color);

        // Draw front indicator (triangle)
        // Replicating VehicleRenderer triangle logic slightly simplified
        // Or just a line as before? VehicleRenderer used a triangle.
        // Let's stick to the previous code if it was acceptable, but refine it.
        // Previous VehicleVisualizer used a line. VehicleRenderer used a triangle.
        // Let's use Line for simplicity unless requested otherwise.
        
        Vector2 front = position + forward * (parameters.Length / 2 * 0.8f);
        Raylib.DrawLineEx(position, front, 0.2f, Color.Black);
        
        // Draw selection ring
        if (isSelected)
        {
            Raylib.DrawRing(position, parameters.Length * 0.6f, parameters.Length * 0.75f, 0, 360, 32, new Color(0, 255, 0, 255));
             
            // Draw Nav Diagnostics for selected
            if (view.HasComponent<NavState>(entity))
            {
                var nav = view.GetComponentRO<NavState>(entity);
                
                // Draw Active Trajectory Spline
                var trajectoryPool = ctx.Resources.Get<TrajectoryPoolManager>();
                if (nav.TrajectoryId > 0 && trajectoryPool != null)
                {
                    if (trajectoryPool.TryGetTrajectory(nav.TrajectoryId, out var traj))
                    {
                        // Draw sampled spline or waypoints
                        // For performance, we sample every 2 meters or similar, or just draw waypoints if linear
                        
                        if (traj.Waypoints.IsCreated) // NativeArray check
                        {
                            // Draw raw waypoints first as dots
                            for (int i = 0; i < traj.Waypoints.Length; i++)
                            {
                                Raylib.DrawCircleV(traj.Waypoints[i].Position, 0.3f, new Color(255, 215, 0, 150)); // Gold dots
                            }

                            // Draw continuous path
                            if (traj.Waypoints.IsCreated && traj.Waypoints.Length >= 2)
                            {
                                if (traj.Interpolation == TrajectoryInterpolation.Linear)
                                {
                                    // Linear: Draw straight lines between waypoints
                                    // We can just draw lines from current pos -> next waypoint -> ... -> end
                                    
                                    // Identify next waypoint
                                    int nextIdx = -1;
                                    for(int i=0; i<traj.Waypoints.Length; i++)
                                    {
                                        if (traj.Waypoints[i].CumulativeDistance > nav.ProgressS)
                                        {
                                            nextIdx = i;
                                            break;
                                        }
                                    }
                                    
                                    var (currentPos, _, _) = trajectoryPool.SampleTrajectory(nav.TrajectoryId, nav.ProgressS);
                                    
                                    if (nextIdx != -1)
                                    {
                                        Raylib.DrawLineEx(currentPos, traj.Waypoints[nextIdx].Position, 0.15f, new Color(50, 200, 255, 100)); // Blue-ish
                                        for(int i=nextIdx; i<traj.Waypoints.Length-1; i++)
                                        {
                                            Raylib.DrawLineEx(traj.Waypoints[i].Position, traj.Waypoints[i+1].Position, 0.15f, new Color(50, 200, 255, 100));
                                        }
                                    }
                                }
                                else
                                {
                                    // Hermite/Spline: Sample smoothly
                                    // Use Orange/Gold for splines as requested
                                    Color splineColor = new Color(255, 161, 0, 200); // Legacy Orange
                                    
                                    float step = 1.0f; // 1 meter steps
                                    float startS = nav.ProgressS;
                                    float endS = traj.TotalLength;
                                    
                                    Vector2 prevPos = trajectoryPool.SampleTrajectory(nav.TrajectoryId, startS).pos;
                                    
                                    for (float s = startS + step; s <= endS; s += step)
                                    {
                                        Vector2 currentPos = trajectoryPool.SampleTrajectory(nav.TrajectoryId, s).pos;
                                        Raylib.DrawLineEx(prevPos, currentPos, 0.15f, splineColor);
                                        prevPos = currentPos;
                                    }
                                    // Close final gap
                                    Vector2 finalPos = trajectoryPool.SampleTrajectory(nav.TrajectoryId, endS).pos;
                                    Raylib.DrawLineEx(prevPos, finalPos, 0.15f, splineColor);
                                }
                            }
                        }
                    }
                }
                
                if (nav.Mode == KinematicsMode.None && !nav.HasArrived.Equals(1)) // Using Equals(1) for byte bool? Or just > 0
                {
                    Raylib.DrawLineEx(position, nav.FinalDestination, 0.1f, new Color(0, 255, 255, 100));
                    Raylib.DrawCircleV(nav.FinalDestination, 0.5f, new Color(0, 255, 255, 100));
                }
                else if (nav.Mode == KinematicsMode.Formation && view.HasComponent<FormationTarget>(entity))
                {
                     var target = view.GetComponentRO<FormationTarget>(entity);
                     Raylib.DrawLineEx(position, target.TargetPosition, 0.1f, new Color(200, 200, 200, 100));
                     Raylib.DrawCircleV(target.TargetPosition, 0.3f, new Color(200, 200, 200, 100));
                }
            }
        }
        
        // Draw Formation Leader lines (Legacy functionality)
        if (view.HasComponent<FormationRoster>(entity))
        {
             var roster = view.GetComponentRO<FormationRoster>(entity);
             if (roster.Count > 0)
             {
                  Raylib.DrawRing(position, parameters.Width * 0.6f, parameters.Width * 0.8f, 0, 360, 32, Color.Magenta);
                  
                  for (int i = 1; i < roster.Count; i++)
                  {
                      var follower = roster.GetMember(i);
                      if (view.IsAlive(follower) && view.HasComponent<SimTransform>(follower))
                      {
                          var fTf = view.GetComponentRO<SimTransform>(follower);
                          Raylib.DrawLineEx(position, new Vector2(fTf.Position.X, fTf.Position.Y), 0.1f, new Color(255, 0, 255, 128));
                      }
                  }
             }
        }
    }
    
    public string? GetHoverLabel(ISimulationView view, Entity entity)
    {
        if (view.HasComponent<VehicleParams>(entity))
        {
            var p = view.GetComponentRO<VehicleParams>(entity);
            return $"{p.Class} #{entity.Index}";
        }
        return null;
    }
}
