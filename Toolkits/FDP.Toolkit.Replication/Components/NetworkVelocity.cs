using System.Numerics;
using Fdp.Kernel;

namespace FDP.Toolkit.Replication.Components
{
    [DataPolicy(DataPolicy.NoRecord)]
    [ComponentId(GlobalComponentIds.NetworkVelocity)]
    public struct NetworkVelocity
    {
        public Vector3 Value;
    }
}
