using System;
using System.Numerics;
using Fdp.Kernel;
using ModuleHost.Core.Abstractions;
using Fdp.Modules.Geographic.Components;

using PositionGeodetic = Fdp.Modules.Geographic.Components.PositionGeodetic;

namespace Fdp.Modules.Geographic.Systems
{
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public class CoordinateTransformSystem : IEcsModuleSystem
    {
        private readonly IGeographicTransform _geo;
        
        public CoordinateTransformSystem(IGeographicTransform geo)
        {
            _geo = geo;
        }
        
        public void Execute(ISimulationView view, float deltaTime)
        {
            var cmd = view.GetCommandBuffer();
            
            // Outbound: Physics → Geodetic (for locally-owned entities only).
            // .WithOwned<Position>() replaces the legacy manual PrimaryOwnerId == LocalNodeId
            // check that broke split-authority deployments (MOD1-P1T3).
            var outbound = view.Query()
                .WithOwned<Position>()
                .WithManaged<PositionGeodetic>()
                .Build();
            
            foreach (var entity in outbound)
            {
                var localPos = view.GetComponentRO<Position>(entity);
                var geoPos = view.GetManagedComponentRO<PositionGeodetic>(entity);
                
                var (lat, lon, alt) = _geo.ToGeodetic(localPos.Value);
                
                // Only update if changed significantly
                if (Math.Abs(geoPos.Latitude - lat) > 1e-6 ||
                    Math.Abs(geoPos.Longitude - lon) > 1e-6 ||
                    Math.Abs(geoPos.Altitude - alt) > 0.1)
                {
                    var newGeo = new PositionGeodetic
                    {
                        Latitude = lat,
                        Longitude = lon,
                        Altitude = alt
                    };
                    cmd.SetManagedComponent(entity, newGeo);
                }
            }
        }
    }
}
