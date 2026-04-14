using FDP.Interfaces.Abstractions;
using CycloneDDS.Schema;
using Fdp.Kernel;
// using ModuleHost.Time; // Unused if we use int

namespace Fdp.Examples.NetworkDemo.Components
{
    [FdpDescriptor(200, "TimeModeComponent")]
    [DdsTopic("TimeModeComponent")]
    [ComponentId(200)]
    public partial struct TimeModeComponent
    {
        [DdsKey]
        public long EntityId;
        
        public int TargetMode; // Cast to TimeMode
        public long FrameNumber;
        public double TotalTime;
        public float FixedDelta;
        public long BarrierWallTicks;
    }
}
