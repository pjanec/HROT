using System.Numerics;
using Fdp.Core;

namespace Fdp.Modules.Geographic.Components
{
    [ComponentId(GlobalComponentIds.GeoPosition)]
    public struct Position
    {
        public Vector3 Value;
    }
}
