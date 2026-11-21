using System;
using System.Collections.Concurrent;
using System.Threading;
using Fdp.Core;

namespace Hrot.AI.Behaviors.Brains.Objectives
{
    /// <summary>
    /// ⭐ CE-3448 (docs/DESIGN_Parallel_Simulation.md §18.2c; CLAUDE.md "BEHAVIOUR CODE RUNS IN PARALLEL") — the ONE way a rule's verb changes
    /// ANOTHER entity (another rule's status, the World Environment, a phase trigger's control): serially. Outside a per-entity phase the work runs
    /// at once, as before; inside the brains' phase it is a follow-up (<see cref="Fdp.Core.Jobs.BatchContext.Later"/>), played after the phase in
    /// batch order — the same world at any thread count, and visible to the other rules from the next tick (the designed one-tick lag).
    /// ⚠ The verb decides Success / Failure at once from the tick-start view (its reads are declared, <see cref="AiBrainReads"/>); the work re-reads
    /// the LIVE state, so two rules changing one counter in a tick both land.
    /// ⚠ Allocates a closure per verb run — a verb runs on a rule's edge, never every frame.
    /// </summary>
    public static class RuleAfterPhase
    {
        private static long s_ticket;
        private static readonly ConcurrentDictionary<long, Action<EntityRepository>> s_pending = new();

        /// <summary>Runs <paramref name="work"/> on <paramref name="world"/> now, or after the phase <paramref name="self"/> is ticking in.</summary>
        public static void Run(EntityRepository world, Entity self, Action<EntityRepository> work)
        {
            if (!ParallelReadCheck.Defers(world)) { work(world); return; }
            long ticket = Interlocked.Increment(ref s_ticket);   // a key only: the ORDER is the batch order of the follow-ups
            s_pending[ticket] = work;
            ParallelReadCheck.CurrentBatch!.Later(Handler.Instance, self, 0, ticket);
        }

        private sealed class Handler : Fdp.Core.Jobs.IPhaseFollowUp
        {
            public static readonly Handler Instance = new();
            public void Apply(EntityRepository repo, Entity e, int code, long arg)
            {
                if (s_pending.TryRemove(arg, out var work)) work(repo);
            }
        }
    }
}
