using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.AI.Behaviors.Brains
{
    // ⭐ CE-464 — the ONLY C# the blueprint hill attack (PlatoonHillAttackBp.bp.json) needs: plain data, no logic.
    // 📄 docs/blueprints/DESIGN_Hill_Attack_Blueprint_Behaviour.md §3.1.

    /// <summary>The commander's phase — the Tick's state machine.</summary>
    public enum HillAttackPhase
    {
        Setup = 0,
        ToBaseline = 1,
        AwaitBaseline = 2,
        Query = 3,
        AwaitQuery = 4,
        Dispatch = 5,
        AwaitWave = 6,
        Return = 7,
        AwaitReturn = 8,
    }

    /// <summary>One firing-line slot (the C# <c>BurnedSlotsMask</c> / <c>WaveUsedSlotsMask</c> bits as one value).</summary>
    public enum HillAttackSlot
    {
        Free = 0,
        WaveUsed = 1,
        Burned = 2,
    }

    /// <summary>One tank in the current wave (the C# <c>ActiveEntityPacked</c>/<c>ActiveSlotIndex</c>/… SoA row).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct HillAttackRunner
    {
        public Entity Unit;
        public int FiringSlot;
        public int BaselineSlot;
        public bool Started;
    }
}
