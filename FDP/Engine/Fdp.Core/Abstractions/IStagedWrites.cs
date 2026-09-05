using System;
using Fdp.Core;

namespace Fdp.ModuleHost.Abstractions
{
    /// <summary>
    /// ⭐⭐⭐ <b>The staged-live-write seam.</b> 📄 <c>DESIGN_Staged_Live_Write.md</c> §5.
    ///
    /// <para>An editor stages a live variable edit; the value reaches the repository only when the sim
    /// tick loop DRAINS it, at the next advancing tick *(<c>R-126</c>: a PULL from the tick loop, never a
    /// release event — so no path can forget to raise what is never raised)*. This interface is the
    /// contract between the two sides so neither lane depends on the other's concrete type:</para>
    /// <list type="bullet">
    ///   <item>the kernel's PreFrame drain system *(TIME lane, <c>W1</c>/<c>W2</c>)* consumes
    ///   <see cref="HasPending"/>, <see cref="IsRewound"/> and <see cref="DrainInto"/>;</item>
    ///   <item>the variable table's yellow "pending" display *(UI lane, <c>W4</c>)* consumes
    ///   <see cref="TryGetPending"/> — the ONE staged set is the single source of the yellow and the
    ///   shown value *(<c>R-130</c>: pending ⟺ a mutation for this field sits un-drained)*;</item>
    ///   <item><c>DataBreakpointManager</c> *(UI lane)* is the production implementer.</item>
    /// </list>
    /// </summary>
    public interface IStagedWrites
    {
        /// <summary>⭐ True while any staged mutation is waiting to be applied.</summary>
        bool HasPending { get; }

        /// <summary>
        /// ⭐ True while a breakpoint has REWOUND the live repository *(the pre-tick snapshot is the
        /// active view)*. ⛔ The drain must SKIP while this holds — 📌 <c>R-63</c>: the resume path
        /// restores the post-tick snapshot and drains itself; draining here would be lost.
        /// </summary>
        bool IsRewound { get; }

        /// <summary>
        /// ⭐⭐ Applies every staged mutation into <paramref name="view"/> *(via its command buffer)* and
        /// empties the pending set. ⛔ Call only when the clock is ADVANCING and <see cref="IsRewound"/>
        /// is false. No-op when nothing is pending.
        /// </summary>
        void DrainInto(ISimulationView view);

        /// <summary>
        /// ⭐⭐⭐ The UI query behind the 🟡 yellow: is a mutation staged for this exact field, and what are
        /// its bytes? ⭐ On <c>true</c>, <paramref name="bytes"/> is the staged payload *(the value to show
        /// in yellow, shared by every surface — Details AND Watch)*; on <c>false</c>, it is empty.
        /// </summary>
        bool TryGetPending(Entity entity, int typeId, int byteOffset, out byte[] bytes);
    }
}
