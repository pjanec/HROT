using System;
using System.Numerics;
using Fdp.Kernel;
using Fdp.ModuleHost_Core.Abstractions;
using Fdp.Modules.Geographic.Components;

using PositionGeodetic = Fdp.Modules.Geographic.Components.PositionGeodetic;

namespace Fdp.Modules.Geographic.Systems
{
    [UpdateInPhase(SystemPhase.Input)]
    public class GeodeticSmoothingSystem : IEcsModuleSystem
    {
        private readonly IGeographicTransform _geo;
        
        public GeodeticSmoothingSystem(IGeographicTransform geo)
        {
            _geo = geo;
        }
        
        public void Execute(ISimulationView view, float deltaTime)
        {
            // Inbound: Geodetic → Physics (for ghost / remote entities only).
            // .WithoutOwned<Position>() replaces the legacy manual
            // PrimaryOwnerId == LocalNodeId check that broke split-authority deployments
            // (MOD1-P1T3).  Ghost entities have Position present but are NOT locally owned,
            // so WithoutOwned correctly selects them while skipping locally-owned entities.
            var inbound = view.Query()
                .With<Position>()
                .WithManaged<PositionGeodetic>()
                .WithoutOwned<Position>()
                .Build();
            
            foreach (var entity in inbound)
            {
                var geoPos = view.GetManagedComponentRO<PositionGeodetic>(entity);
                var currentPos = view.GetComponentRO<Position>(entity);
                
                // Convert latest geodetic to Cartesian target
                var targetCartesian = _geo.ToCartesian(
                    geoPos.Latitude, 
                    geoPos.Longitude, 
                    geoPos.Altitude);
                
                // Smooth interpolation (dead reckoning)
                float t = Math.Clamp(deltaTime * 10.0f, 0f, 1f);
                Vector3 newPos = Vector3.Lerp(currentPos.Value, targetCartesian, t);
                
                if (view is EntityRepository repo)
                {
                    // Direct write optimization (main thread)
                    ref var pos = ref repo.GetComponentRW<Position>(entity);
                    pos.Value = newPos;
                }
                else
                {
                    // Fallback for strict view
                    var cmd = view.GetCommandBuffer();
                    cmd.SetComponent(entity, new Position { Value = newPos });
                }
            }
        }
    }
}
