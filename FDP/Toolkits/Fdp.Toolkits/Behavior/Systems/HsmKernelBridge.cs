using System;
using Fdp.Core;
using Fhsm.Kernel.Data;

namespace Fdp.Toolkit.Behavior.Systems
{
    /// <summary>
    /// Minimal unmanaged bridge passed to <c>HsmKernel.Update</c>.
    /// Must satisfy <c>where TContext : unmanaged</c> — cannot hold managed references.
    /// <c>WorldHandle</c> is an <see cref="System.IntPtr"/> (unmanaged) holding the GCHandle
    /// table index for the <see cref="EntityRepository"/>; recover with
    /// <c>GCHandle.FromIntPtr(bridge->WorldHandle).Target</c>.
    /// See DEBT-007-HSM-ANALYSIS.md for full explanation.
    ///
    /// <para>⭐⭐ <b><c>O7c</c>-④b (<c>2026-09-23</c>): this type MOVED OUT of <c>HsmTickSystem.cs</c>,
    /// which was deleted, and it did not change.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c>
    /// §31.16. ⛔ <b>It is a GENERATED-CODE CONTRACT, not a detail of the system that happened to host
    /// it</b> — <c>HsmActionGenerator</c> emits <c>HsmKernelBridge*</c> into every
    /// <c>HsmActionRegistrar.g.cs</c>, and <c>AiPrimitiveEmitter</c> emits it into every blueprint
    /// thunk. ⇒ its namespace and name are ABI for emitted code: a rename re-emits every registrar.
    /// ⭐ Giving it its own file is what makes that visible, rather than leaving it to be noticed when
    /// the next tick-system refactor moves it by accident.</para>
    /// </summary>
    public unsafe struct HsmKernelBridge
    {
        public Entity Self;
        public IntPtr WorldHandle;   // IntPtr is unmanaged; holds GCHandle table index

        /// <summary>
        /// Optional pointer to a stack-local <see cref="HsmTraceContext"/> built each
        /// tick over the entity's <c>HsmTraceWorkingMemory1024</c> component. Null
        /// when tracing is disabled. User-authored HSM actions/guards may write
        /// domain errors via <c>bridge.TraceContext-&gt;WriteError(...)</c>.
        /// </summary>
        public HsmTraceContext* TraceContext;
    }
}
