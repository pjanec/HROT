using System;
using Fdp.Kernel;
using Fdp.Kernel.FlightRecorder;
using Fdp.ModuleHost_Core.Abstractions;

namespace Fdp.Examples.NetworkDemo.Systems
{
    [UpdateInPhase(SystemPhase.PostSimulation)]
    public class RecorderTickSystem : IEcsModuleSystem
    {
        private readonly AsyncRecorder _recorder;
        private readonly EntityRepository _repo;
        private uint _tickCount = 0;

        public RecorderTickSystem(AsyncRecorder recorder, EntityRepository repo)
        {
            _recorder = recorder;
            _repo = repo;
        }

        public void SetMinRecordableId(int minId)
        {
            _recorder.MinRecordableId = minId;
        }

        public void Execute(ISimulationView view, float dt)
        {
            // Sample wall clock once at the call site (frame-locked at the system boundary).
            // Will be replaced by GlobalTime.TotalWallTicks in Phase 3.
            long wallClockTicks = DateTime.UtcNow.Ticks;
            // Capture frame.
            _recorder.CaptureFrame(_repo, _tickCount++, wallClockTicks);
        }
    }
}
