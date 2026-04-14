using System.Runtime.InteropServices;
using Fbt;
using Fhsm.Kernel.Data;
using Fdp.Core;

namespace Fdp.Toolkit.Behavior.Components
{
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.BrainBTreeState)]
    public struct BrainBTreeState
    {
        public BehaviorTreeState State;
    }

    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.BrainHsm64)]
    public struct BrainHsm64
    {
        public HsmInstance64 State;
    }

    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.BrainHsm128)]
    public struct BrainHsm128
    {
        public HsmInstance128 State;
    }
}
