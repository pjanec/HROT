using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.AI.Behaviors
{
    /// <summary>
    /// Component-access demo ECS component (CA-01..CA-06 visual test). A small blittable component
    /// carrying a handful of primitive fields, attached to entities purely so the
    /// <c>GetComponentDemo</c>/<c>GetComponentTargetDemo</c>/<c>SetComponentDemo</c> blueprints have
    /// something concrete to read/write via the new <c>GetComponent</c>/<c>SetComponent</c> nodes.
    /// <c>[BlueprintWritable]</c> makes it eligible for the editor's <c>SetComponent</c> write
    /// picker (CA-04); ordinary system-output components must NOT carry this attribute (see
    /// <see cref="BlueprintWritableAttribute"/>'s doc comment) but a demo-only component owned by no
    /// system is exactly the safe case it is meant for.
    /// </summary>
    [ComponentId(188)] // Hrot application-level block (160-199, HrotComponentIds); next free after 187 (InitialBlueprintsIntent).
    [BlueprintWritable]
    [StructLayout(LayoutKind.Sequential)]
    public struct BpComponentDemo
    {
        public int Health;
        public int Ammo;
        public float Speed;
    }
}
