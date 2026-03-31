using Fdp.Kernel;

namespace Hrot.SimHost.Components
{
    [ComponentId(129)]
    public struct MissionAdapterState
    {
        public byte LastPhase;
        public uint LastPlanVersion;
    }
}