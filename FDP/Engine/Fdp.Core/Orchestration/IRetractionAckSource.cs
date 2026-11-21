using System;

namespace Fdp.Core.Orchestration
{
    /// <summary>
    /// ⭐⭐ <c>CE-4079</c> ④ — what the replay / review handlers ask of the network layer before a node reports <i>prepared</i> or
    /// <i>seek-done</i>: <b>have this node's retraction disposes been ACKNOWLEDGED by the readers?</b>
    /// 📄 <c>docs/DESIGN_Replay_Time_Axis_And_Review.md</c> §12c W3 (the bounded wait), §12f row 37.
    ///
    /// <para>Implemented by <c>CycloneNetworkCleanupSystem</c> — the one system that both retracts (the sweep) and holds every
    /// translator — which puts itself into the world as a managed singleton, so no host has to wire it (the
    /// <c>ContributeDescriptorMap</c> precedent). The controller (<c>IRecordReplayController.AwaitRetractionAcksAsync</c>) reads it from
    /// the world; <c>Fdp.Core</c> knows nothing of DDS.</para>
    /// </summary>
    public interface IRetractionAckSource
    {
        /// <summary>
        /// The <c>EntityRepository.ClearGeneration</c> the last retraction sweep covered — set only AFTER the sweep's disposes were
        /// written. While it differs from the world's, a world swap is waiting for its sweep (the next frame's cleanup).
        /// </summary>
        uint SweptClearGeneration { get; }

        /// <summary>
        /// Blocks until every writer this node holds has had everything it wrote so far — the disposes included — acknowledged by its
        /// matched reliable readers, or until <paramref name="timeout"/> (shared by all writers) has elapsed. Never throws for a
        /// timeout. ⚠ Blocking: call it off the frame thread.
        /// </summary>
        RetractionAckReport WaitForAcks(TimeSpan timeout);
    }

    /// <summary>
    /// ⭐ <c>CE-4079</c> ④ — where a world's <see cref="IRetractionAckSource"/> is found: a table keyed by the world OBJECT, not a world singleton.
    /// 📐 Why not a singleton: every in-place world replacement (<c>EntityRepository.Clear</c> — a seek's keyframe, a restore, a review) clears the
    /// component tables the singletons live in, so a singleton would be gone exactly when the controller asks. The registration is the source's own
    /// (<c>CycloneNetworkCleanupSystem</c>, at its first frame — no host wires it), and it lives and dies with the world object.
    /// </summary>
    public static class RetractionAckSources
    {
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, IRetractionAckSource> Sources = new();

        /// <summary>Makes <paramref name="source"/> the one for <paramref name="world"/> (replaces an earlier one).</summary>
        public static void Publish(object world, IRetractionAckSource source) => Sources.AddOrUpdate(world, source);

        /// <summary>The world's source, or <see langword="null"/> when its node has no network layer.</summary>
        public static IRetractionAckSource? Of(object world) => Sources.TryGetValue(world, out var source) ? source : null;
    }

    /// <summary>What <see cref="IRetractionAckSource.WaitForAcks"/> found.</summary>
    /// <param name="Writers">How many writers were waited on.</param>
    /// <param name="TimedOut">How many still had unacknowledged data when the time ran out (0 = everything acknowledged).</param>
    /// <param name="WaitedMilliseconds">How long the wait took.</param>
    public readonly record struct RetractionAckReport(int Writers, int TimedOut, double WaitedMilliseconds)
    {
        /// <summary>Every writer's data was acknowledged.</summary>
        public bool Complete => TimedOut == 0;
    }
}
