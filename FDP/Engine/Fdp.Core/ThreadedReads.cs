using System;

namespace Fdp.Core
{
    /// <summary>
    /// ⭐ CE-4445 (R-4210) — a managed-component read is a READ: it stamps no chunk version and allocates no chunk (also <c>TryGetComponent</c>).
    /// This is the DEFAULT. A managed component is immutable and replaced whole (<c>SetManagedComponent</c>, CLAUDE.md), so a read that stamped only
    /// hid in-place mutations — the audit and the runtime detector (<see cref="ManagedMutationCheck"/>) found and fixed them
    /// (<c>docs/blueprints/batches/STATUS_Managed_Reads_Audit.md</c>).
    /// <para>⚠ The old behaviour — every managed read stamps its chunk — stays behind the DIAGNOSTIC switch <c>HROT_MANAGED_READ_STAMP=1</c> for one
    /// release (to tell whether a regression is an in-place mutation that a stamping read used to record). A <c>static readonly</c>, so the JIT
    /// folds it away.</para>
    /// <para>History: POC 2 P-1 built it opt-in (<c>HROT_THREADED_KERNEL=2</c> / <c>HROT_PURE_READS=1</c>); exp/parallel-sim-3 built a second
    /// copy (<c>HROT_MANAGED_READ_NO_STAMP=1</c>) — unified into this one.</para>
    /// </summary>
    public static class ThreadedReads
    {
        public static readonly bool PureReads = EnvVars.Read(EnvVars.HrotManagedReadStamp) != "1";
    }
}
