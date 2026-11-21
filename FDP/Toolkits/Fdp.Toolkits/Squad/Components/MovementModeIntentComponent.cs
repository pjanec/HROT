using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Squad;

namespace Fdp.Toolkit.Squad.Components
{
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.MovementModeIntent)]
    [DataPolicy(DataPolicy.Default)]   // ⭐ CE-3299 D7 — declared: world state: recorded, saved, snapshotted (what it got by default)
    public struct MovementModeIntent
    {
        public MovementMode Mode;
    }
}
