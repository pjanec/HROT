using System;
using System.Numerics;
using Raylib_cs;
using Fdp.Kernel;
using FDP.Toolkit.Vis2D.Abstractions;
using CarKinem.Core;
using CarKinem.Formation;
using Fdp.ModuleHost.Core.Abstractions;

namespace Hrot.SimHost.Visualization
{
    /// <summary>
    /// Renders each vehicle entity as a rotated rectangle with a forward-direction
    /// indicator, colour-coded by navigation mode / formation role.
    /// Closely mirrors <c>Fdp.Examples.CarKinem.Visualization.VehicleVisualizer</c>.
    /// </summary>
    public class SimHostVehicleVisualizer : IVisualizerAdapter
    {
        // ── Colour palette (matches CarKinem example) ─────────────────────────
        private static readonly Color ColFormationMember  = new(0, 200, 255, 255);   // cyan
        private static readonly Color ColFormationLeader  = new(255, 0,   255, 255); // magenta
        private static readonly Color ColRoadNav          = new(50,  100, 255, 255); // blue
        private static readonly Color ColTrajectoryNav    = new(173, 255, 47,  255); // green-yellow
        private static readonly Color ColDefault          = new(200, 200, 200, 255); // light-grey

        // ── IVisualizerAdapter ────────────────────────────────────────────────

        public Vector2? GetPosition(ISimulationView view, Entity entity)
        {
            if (!view.HasComponent<SimTransform>(entity)) return null;
            var tf = view.GetComponentRO<SimTransform>(entity);
            return new Vector2(tf.Position.X, tf.Position.Y);
        }

        public float GetHitRadius(ISimulationView view, Entity entity)
        {
            if (view.HasComponent<VehicleParams>(entity))
                return view.GetComponentRO<VehicleParams>(entity).Length / 2f;
            return 1.5f;
        }

        public void Render(ISimulationView view, Entity entity, Vector2 position, RenderContext ctx, bool isSelected, bool isHovered)
        {
            if (!view.HasComponent<SimTransform>(entity) || !view.HasComponent<VehicleParams>(entity))
                return;

            ref readonly var tf     = ref view.GetComponentRO<SimTransform>(entity);
            ref readonly var prm    = ref view.GetComponentRO<VehicleParams>(entity);

            Color color = ChooseColor(view, entity, prm);

            if (isHovered && !isSelected)
                color = new Color(
                    (byte)Math.Min(color.R + 50, 255),
                    (byte)Math.Min(color.G + 50, 255),
                    (byte)Math.Min(color.B + 50, 255),
                    (byte)255);

            // Rotated body rectangle
            Vector3 fwd3d    = Vector3.Transform(Vector3.UnitX, tf.Rotation);
            Vector2 forward  = new(fwd3d.X, fwd3d.Y);
            float   rotDeg   = MathF.Atan2(forward.Y, forward.X) * (180f / MathF.PI);

            var rec    = new Rectangle(position.X, position.Y, prm.Length, prm.Width);
            var origin = new Vector2(prm.Length / 2f, prm.Width / 2f);
            Raylib.DrawRectanglePro(rec, origin, rotDeg, color);

            // Front indicator line
            Vector2 front = position + forward * (prm.Length / 2f);
            Raylib.DrawLineEx(position, front, 1.5f, Color.Black);

            // Selection ring
            if (isSelected)
                Raylib.DrawCircleLines((int)position.X, (int)position.Y,
                    MathF.Max(prm.Length, prm.Width) * 0.7f,
                    Color.White);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Color ChooseColor(ISimulationView view, Entity entity, in VehicleParams prm)
        {
            if (view.HasComponent<FormationRoster>(entity))  return ColFormationLeader;
            if (view.HasComponent<FormationMember>(entity))  return ColFormationMember;

            if (view.HasComponent<NavState>(entity))
            {
                var nav = view.GetComponentRO<NavState>(entity);
                return nav.Mode switch
                {
                    KinematicsMode.RoadGraph       => ColRoadNav,
                    KinematicsMode.CustomTrajectory => ColTrajectoryNav,
                    _                              => ColDefault,
                };
            }

            // Fall back to class palette
            var (r, g, b) = VehiclePresets.GetColor(prm.Class);
            return new Color(r, g, b, (byte)255);
        }
    }
}
