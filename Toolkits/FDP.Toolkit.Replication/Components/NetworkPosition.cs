using System.Numerics;
using Fdp.Kernel;

namespace FDP.Toolkit.Replication.Components
{
    [DataPolicy(DataPolicy.NoRecord)]
    [ComponentId(GlobalComponentIds.NetworkPosition)]
    public struct NetworkPosition
    {
        public Vector3 Value;
    }
}
