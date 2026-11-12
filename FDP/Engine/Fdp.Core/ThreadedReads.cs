using System;

namespace Fdp.Core
{
    /// <summary>
    /// POC 2 of the threaded editor (P-1) — the switch. OFF (default): every read behaves exactly as before. ON
    /// (<c>HROT_THREADED_KERNEL=2</c> or <c>HROT_PURE_READS=1</c>): a managed-component read, and <c>TryGetComponent</c>, stamp no chunk version
    /// and allocate no chunk — a read is a read. A <c>static readonly</c>, so the JIT folds it away when OFF.
    /// 📄 docs/blueprints/batches/HANDOFF_Coordinator_To_Backend_Threaded_POC_2.md
    /// </summary>
    public static class ThreadedReads
    {
        public static readonly bool PureReads =
            Environment.GetEnvironmentVariable("HROT_THREADED_KERNEL") == "2" ||
            Environment.GetEnvironmentVariable("HROT_PURE_READS") == "1";
    }
}
