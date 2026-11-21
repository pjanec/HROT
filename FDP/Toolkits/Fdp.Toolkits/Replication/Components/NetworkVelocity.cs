using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Replication.Components
{
    [DataPolicy(DataPolicy.NoReplay)]
    [ComponentId(GlobalComponentIds.NetworkVelocity)]
    public struct NetworkVelocity
    {
        public Vector3 Value;
    }
}
