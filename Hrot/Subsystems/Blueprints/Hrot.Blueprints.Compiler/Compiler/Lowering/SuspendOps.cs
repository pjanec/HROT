using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Lowering;

/// <summary>
/// ⭐⭐ <b>THE set of IR operations that suspend a graph</b> (S5d, <c>DESIGN_Unified_Behaviour_Run</c>). 📐 It used to be spelled
/// four times (<c>LocalStorage.CanSuspend</c> and two filters in each WaitLowering); adding Run Behaviour to three of them and
/// missing the fourth left an <c>IrTerm_Suspend</c> unlowered — the same shape as I13's three latent-node lists. ⇒ one owner.
/// </summary>
internal static class SuspendOps
{
    public static bool Is(IrOperation op)
        => op is IrOp_LatentDelay or IrOp_WaitForChannel or IrOp_WaitForEvent or IrOp_InlineActionCall or IrOp_RunBehavior;
}
