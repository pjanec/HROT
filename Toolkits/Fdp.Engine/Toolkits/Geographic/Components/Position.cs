using System.Numerics;
using Fdp.Kernel;

namespace Fdp.Modules.Geographic.Components
{
    [ComponentId(GlobalComponentIds.GeoPosition)]
    public struct Position
    {
        public Vector3 Value;
    }
}
