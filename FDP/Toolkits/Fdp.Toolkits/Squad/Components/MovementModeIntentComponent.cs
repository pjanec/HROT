using System.Runtime.InteropServices;
using Fdp.Core;
using Fdp.Toolkit.Squad;

namespace Fdp.Toolkit.Squad.Components
{
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.MovementModeIntent)]
    public struct MovementModeIntent
    {
        public MovementMode Mode;
    }
}
