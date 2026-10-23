using System.Numerics;

using Fdp.Core;

namespace Fdp.Modules.Geographic.Components
{
    [ComponentId(230)]
    [DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
    public struct Velocity
    {
        public Vector3 Value;
    }
}
