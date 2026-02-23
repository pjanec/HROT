using System.Runtime.InteropServices;
using Fbt;
using Fhsm.Kernel.Data;

namespace FDP.Toolkit.Behavior.Components
{
    [StructLayout(LayoutKind.Sequential)]
    public struct BrainBTreeState
    {
        public BehaviorTreeState State;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BrainHsm64
    {
        public HsmInstance64 State;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BrainHsm128
    {
        public HsmInstance128 State;
    }
}
