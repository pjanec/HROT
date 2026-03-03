using ModuleHost.Core.Abstractions;
using Fdp.Modules.Geographic.Systems;

namespace Fdp.Modules.Geographic
{
    public class GeographicModule : IModule
    {
        public string Name => "GeographicServices";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly IGeographicTransform _transform;

        public GeographicModule(IGeographicTransform implementation)
        {
            _transform = implementation;
        }

        public void RegisterSystems(ISystemRegistry registry)
        {
            registry.RegisterSystem(new GeodeticSmoothingSystem(_transform));
            registry.RegisterSystem(new Fdp.Modules.Geographic.Systems.CoordinateTransformSystem(_transform));
        }

        public void Tick(ISimulationView view, float deltaTime) { }
    }
}
