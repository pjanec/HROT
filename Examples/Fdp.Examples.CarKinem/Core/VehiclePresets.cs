using Raylib_cs;
using CarKinem.Formation;
using CarKinem.Core;
using Fdp.Examples.CarKinem.Components;
using Fdp.ModuleHost_Core.Abstractions;
using System.Numerics;
using Fdp.Kernel; // Added

namespace Fdp.Examples.CarKinem.Core
{
    public static class ExampleVehiclePresets
    {
        // Colors from VehicleRenderer.cs
        public static readonly Color ColorFormationMember = new Color(0, 200, 255, 255); // Cyan
        public static readonly Color ColorFormationLeader = new Color(255, 0, 255, 255); // Magenta
        public static readonly Color ColorRoadNav = new Color(50, 100, 255, 255);        // Blue
        public static readonly Color ColorTrajectoryNav = new Color(173, 255, 47, 255);  // GreenYellow
        public static readonly Color ColorDefaultNav = new Color(200, 200, 200, 255);    // Gray

        public static Color GetColorForEntity(ISimulationView view, Entity entity, VehicleParams parameters)
        {
            // Priority 1: Component Override
            if (view.HasComponent<VehicleColor>(entity))
            {
                var c = view.GetComponentRO<VehicleColor>(entity);
                return new Color(c.R, c.G, c.B, c.A);
            }
            // Priority 2: Inferred Role
            else if (view.HasComponent<FormationMember>(entity))
            {
                return ColorFormationMember;
            }
            else if (view.HasComponent<FormationRoster>(entity))
            {
                 return ColorFormationLeader;
            }
            else if (view.HasComponent<NavState>(entity))
            {
                var nav = view.GetComponentRO<NavState>(entity);
                if (nav.Mode == KinematicsMode.RoadGraph)
                    return ColorRoadNav;
                else if (nav.Mode == KinematicsMode.CustomTrajectory)
                     return ColorTrajectoryNav;
                else
                    return ColorDefaultNav;
            }
            else
            {
                var (r, g, b) = global::CarKinem.Core.VehiclePresets.GetColor(parameters.Class);
                return new Color(r, g, b, (byte)255);
            }
        }
    }
}
