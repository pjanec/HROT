using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.AI.Behaviors.Brains
{
    // ⭐ R-2006 — the plain data SubordinateForEachDemo.bp.json keeps in its fixed-list variables (no logic). They are also the
    // shapes the struct-sizing and diagnostics rails hold: an [InlineArray] of an ENUM (the case Marshal.SizeOf cannot size) and a
    // struct carrying an Entity next to ints and a bool. 📄 docs/DESIGN_Orders_Params_Entity_Classes.md §13.5.

    /// <summary>What the demo has done for one roster slot.</summary>
    public enum SubordinateSlot
    {
        Free = 0,
        Ordered = 1,
        Confirmed = 2,
    }

    /// <summary>One order the demo sent: to whom, at which roster index, out of how many.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SubordinateOrder
    {
        public Entity Unit;
        public int Index;
        public int Count;
        public bool Confirmed;
    }
}
