using System.Numerics;
using Fdp.Core;

namespace Fdp.Modules.Geographic.Components
{
    [ComponentId(GlobalComponentIds.GeoPosition)]
    [DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
    public struct Position
    {
        public Vector3 Value;
    }
}
