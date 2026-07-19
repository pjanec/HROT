using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.Editor.AiShared;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// EQS batch area-query curated helper surface for blueprints (Hill-attack migration, architect
    /// Q#6-D + Q#7). The batch area-query is a DISTINCT EQS surface from <c>SpawnEqsSensor</c>: a
    /// fire-and-forget request into <see cref="AreaQueryBatchHelper"/>'s ring buffer, polled by slot id.
    /// Per Q#7-B the node-level control flow (Running/Success/Failure routing, the 5 s timeout) stays on
    /// the visual graph — only the batch-system touch is curated here, decomposed into scalar accessors
    /// (not a struct-returning verb) so the graph reads <c>bool</c>/<c>int</c>/<c>long</c> directly.
    /// <para>
    /// <b>P7 trailing-context convention</b> (mirrors <see cref="WorldOps.IsAlive"/>): a trailing
    /// <c>ISimulationView view</c> is baked <c>TrailingContext:"View"</c> and auto-appended by Stage5;
    /// <see cref="Request"/> additionally takes a trailing <c>Entity self</c> baked
    /// <c>TrailingContext:"SelfAndView"</c> (Stage5 appends <c>self</c> then <c>view</c>, in that order).
    /// </para>
    /// <para>
    /// <b>GAP-10:</b> <see cref="AreaQueryBatchHelper"/> keys off the concrete
    /// <see cref="EntityRepository"/> (singleton access), not <see cref="ISimulationView"/>, so each
    /// helper downcasts <paramref name="view"/> back to it (graceful sentinel/no-op for a non-repository
    /// view rather than throwing). Does not modify the C# oracle (<c>HillAttackCommanderNodes</c>).
    /// </para>
    /// </summary>
    public static class AreaQueryBatchOps
    {
        /// <summary>
        /// Submits a hostile-force area query for <paramref name="targetArea"/> on behalf of
        /// <paramref name="self"/>. Returns the non-negative slot id to poll, or <c>-1</c> if the batch
        /// is full (or the view is not a real repository) — matching the oracle's <c>id == -1</c> retry
        /// signal. Wraps <see cref="AreaQueryBatchHelper.RequestAreaQuery"/> with
        /// <see cref="ForceId.Hostile"/> baked (the oracle's only caller uses Hostile).
        /// </summary>
        [BlueprintCallable("EQS")]
        public static long Request(Entity targetArea, Entity self, ISimulationView view)
        {
            if (view is not EntityRepository world)
                return -1;

            return AreaQueryBatchHelper.RequestAreaQuery(world, self, targetArea, ForceId.Hostile);
        }

        /// <summary>
        /// True once the solver has resolved the slot <paramref name="requestId"/> (mirrors the oracle's
        /// <c>GetAreaQueryResult(...).IsReady</c> guard). False for an unresolved slot or a non-repository
        /// view.
        /// </summary>
        [BlueprintCallable("EQS")]
        public static bool IsReady(long requestId, ISimulationView view)
        {
            if (view is not EntityRepository world)
                return false;

            return AreaQueryBatchHelper.GetAreaQueryResult(world, requestId).IsReady;
        }

        /// <summary>
        /// Number of targets found inside the polygon for the resolved slot <paramref name="requestId"/>
        /// (<c>GetAreaQueryResult(...).TargetCount</c>). <c>0</c> for a non-repository view — the graph
        /// only reads this on the ready path, where the result is the real solver output.
        /// </summary>
        [BlueprintCallable("EQS")]
        public static int TargetCount(long requestId, ISimulationView view)
        {
            if (view is not EntityRepository world)
                return 0;

            return AreaQueryBatchHelper.GetAreaQueryResult(world, requestId).TargetCount;
        }

        /// <summary>
        /// Target-pool handle to cache for the wave dispatch (<c>GetAreaQueryResult(...).TargetGroupHandle</c>).
        /// <c>-1</c> for a non-repository view (matches the pool-exhausted sentinel).
        /// </summary>
        [BlueprintCallable("EQS")]
        public static int TargetGroupHandle(long requestId, ISimulationView view)
        {
            if (view is not EntityRepository world)
                return -1;

            return AreaQueryBatchHelper.GetAreaQueryResult(world, requestId).TargetGroupHandle;
        }

        /// <summary>
        /// Releases the batch slot <paramref name="requestId"/> (<see cref="AreaQueryBatchHelper.FreeAreaQuerySlot"/>).
        /// Authored as an IMPURE (exec) <c>FunctionCall</c> so this side effect is never dead-code
        /// eliminated; the accompanying <c>CachedEqsRequestId = -1</c> WorkingState reset stays a visual
        /// <c>SetVariable</c> (architect Q#7-D). No-op for a non-repository view.
        /// </summary>
        [BlueprintCallable("EQS", IsPure = false)]
        public static void Free(long requestId, ISimulationView view)
        {
            if (view is not EntityRepository world)
                return;

            AreaQueryBatchHelper.FreeAreaQuerySlot(world, requestId);
        }
    }
}
