using System.Numerics;
using Fdp.Core;

namespace Fdp.Network.Cyclone.Components
{
    [ComponentId(GlobalComponentIds.NetworkOrientation)]
    [DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
    public struct NetworkOrientation
    {
        public Quaternion Value;
    }
}
