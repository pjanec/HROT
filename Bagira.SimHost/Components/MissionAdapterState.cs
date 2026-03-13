using Fdp.Kernel;

namespace Bagira.SimHost.Components
{
    [ComponentId(129)]
    public struct MissionAdapterState
    {
        public byte LastPhase;
        public uint LastPlanVersion;
    }
}