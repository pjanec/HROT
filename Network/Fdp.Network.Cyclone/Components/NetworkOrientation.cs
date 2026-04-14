using System.Numerics;
using Fdp.Kernel;

namespace Fdp.ModuleHost.Network.Cyclone.Components
{
    [ComponentId(GlobalComponentIds.NetworkOrientation)]
    public struct NetworkOrientation
    {
        public Quaternion Value;
    }
}
