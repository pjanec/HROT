using Fdp.ModuleHost.Abstractions;
using Fdp.Examples.NetworkDemo.Systems;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;

namespace Fdp.Examples.NetworkDemo.Modules
{
    public class RecordingModule : IEcsModule
    {
        public string Name => "Recording";
        public ExecutionPolicy Policy => ExecutionPolicy.Synchronous();

        private readonly AsyncRecorder _recorder;
        private readonly EntityRepository _repo;

        public RecordingModule(AsyncRecorder recorder, EntityRepository repo)
        {
            _recorder = recorder;
            _repo = repo;
        }

        public void RegisterSystems(ISystemRegistry registry)
        {
            registry.RegisterSystem(new TransformSyncSystem(driveFromNetwork: _recorder == null));
            if (_recorder != null)
                registry.RegisterSystem(new RecorderTickSystem(_recorder, _repo));
        }

        public void Tick(ISimulationView view, float dt) { }
    }
}
