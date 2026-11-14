using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Motion
{
    /// <summary>
    /// ⭐ <c>R-3039</c> S1 — this aircraft's crew has ejected, so <c>EjectionSystem</c> never ejects it again. On the aircraft, on the node owning
    /// its Health; recorded, so a checkpoint restored into a fresh process still knows — 📐 as a field of the system it was lost there, and the
    /// wreck (which still qualifies, CE-3215) ejected a SECOND pilot. 📄 docs/DESIGN_System_State_Inventory.md §6 #1.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.EjectionDone)]
    [DataPolicy(DataPolicy.NoScenario)]
    public struct EjectionDone
    {
    }
}
