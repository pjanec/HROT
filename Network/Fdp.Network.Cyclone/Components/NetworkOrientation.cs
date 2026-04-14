using System.Numerics;
using Fdp.Core;

namespace Fdp.Network.Cyclone.Components
{
    [ComponentId(GlobalComponentIds.NetworkOrientation)]
    public struct NetworkOrientation
    {
        public Quaternion Value;
    }
}
