using System;
using System.Collections.Generic;
using ModuleHost.Core.Abstractions;
using ModuleHost.Core.Scheduling;

namespace ModuleHost.Core
{
    /// <summary>
    /// Immutable snapshot of the kernel's complete execution state.
    /// 
    /// <para>
    /// This is the core data structure for the Read-Copy-Update (RCU) hot-plugging pattern.
    /// Background threads compile a new topology by cloning the current one and applying
    /// module changes. The main thread then performs a single O(1) atomic pointer swap
    /// during <see cref="SystemPhase.BeforeSync"/> to atomically activate the new execution
    /// topology, guaranteeing zero allocations and zero stalls on the 60Hz hot path.
    /// </para>
    /// 
    /// <para>
    /// A topology is always treated as immutable once published. The background compilation
    /// task creates a fresh instance; the main thread never mutates a live topology in-place.
    /// </para>
    /// </summary>
    internal sealed class KernelExecutionTopology
    {
        /// <summary>
        /// The ordered list of active module entries to be dispatched each frame.
        /// This list is produced by the background compilation task and is never mutated
        /// after the topology is published.
        /// </summary>
        public IReadOnlyList<ModuleHostKernel.ModuleEntry> Modules { get; }

        /// <summary>
        /// The compiled, topologically-sorted system scheduler for this topology.
        /// Contains both static global systems and all module-provided systems,
        /// fully sorted based on their <c>[UpdateBefore]</c> / <c>[UpdateAfter]</c> attributes.
        /// </summary>
        public SystemScheduler Scheduler { get; }

        public KernelExecutionTopology(
            IReadOnlyList<ModuleHostKernel.ModuleEntry> modules,
            SystemScheduler scheduler)
        {
            Modules = modules ?? throw new ArgumentNullException(nameof(modules));
            Scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        }
    }
}
