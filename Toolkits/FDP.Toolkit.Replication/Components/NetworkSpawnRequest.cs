using Fdp.Kernel;

namespace FDP.Toolkit.Replication.Components
{
    // Define if not exists, based on requirements.
    // Signals that the primary type information (Master Descriptor) has arrived.
    [ComponentId(GlobalComponentIds.NetworkSpawnRequest)]
    public struct NetworkSpawnRequest
    {
        public ulong DisType;
        public ulong OwnerId;
        public long TkbType; // Assuming we map DisType to TkbType or receive TkbType directly.
    }
}
