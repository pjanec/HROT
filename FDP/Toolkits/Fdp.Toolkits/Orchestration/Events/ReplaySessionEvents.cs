using System;
using Fdp.Core;
using Fdp.Core.FlightRecorder;
using Fdp.Core.FlightRecorder.Metadata;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>
    /// ⭐⭐ <c>CE-3329</c> (rows 32, 45; §9e–§9g) — <b>the replay's SESSION LAYER</b>: what every host draws the same — the ruler on the
    /// active axis, the paused stretches, the checkpoint marks, the live point and the review's span — from the master, never from a
    /// local file. 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §9f.
    /// </summary>
    /// <remarks>
    /// <para>⭐ Built by the master's <c>ReplaySessionProcessManager</c> and republished whenever it changes; the DDS
    /// <c>ReplaySessionTopic</c> (TransientLocal, KeepLast 1) carries it to every host, a late joiner included. A host matches it to the
    /// cluster state by <see cref="SessionId"/> / <see cref="ExerciseId"/>, never by arrival order (DDS orders no two topics, §9g).</para>
    /// <para>⭐ Inactive (no replay, no review) is a sample too: <see cref="Active"/> false clears what a host drew.</para>
    /// </remarks>
    public sealed class ReplaySession
    {
        /// <summary>The master's cluster session (C2).</summary>
        public Guid SessionId { get; set; }

        /// <summary>The exercise shown (a replay's, or the live run's in a review).</summary>
        public Guid ExerciseId { get; set; }

        /// <summary>⭐ F18 — the review open now (minted at its entry); <see cref="Guid.Empty"/> outside a review.</summary>
        public Guid ReviewId { get; set; }

        /// <summary>A replay or a review is open.</summary>
        public bool Active { get; set; }

        /// <summary>The open session is a review of the live run (else an ordinary replay).</summary>
        public bool IsReview { get; set; }

        /// <summary>R-3058 — the axis every host's timeline draws and every seek means.</summary>
        public ReplayAxis Axis { get; set; } = ReplayAxis.Sim;

        /// <summary>The replay's zero (C9): the earliest first frame across the cluster, wall ticks.</summary>
        public long ZeroWallTicks { get; set; }

        /// <summary>The replay's length in wall seconds after the zero (in a review: up to the live point).</summary>
        public double LengthSeconds { get; set; }

        /// <summary>The cluster's wall ↔ sim map (segments, paused stretches); <see langword="null"/> for a v6 recording.</summary>
        public ReplayTimeMap? TimeMap { get; set; }

        /// <summary>The recording's checkpoint marks (label, wall, sim), the union across the nodes.</summary>
        public CheckpointMark[] Checkpoints { get; set; } = Array.Empty<CheckpointMark>();

        /// <summary>⭐ The review's live point L in the run's sim seconds; <see langword="null"/> outside a review.</summary>
        public double? LivePointSimSeconds { get; set; }

        /// <summary>⭐ The live point in wall seconds after the zero (the review's span is [0, this]); <see langword="null"/> outside a review.</summary>
        public double? LivePointSeconds { get; set; }

        /// <summary>The empty sample: no replay, no review.</summary>
        public static ReplaySession None(Guid sessionId) => new() { SessionId = sessionId };

        /// <summary>Whether two samples would draw the same (the publisher republishes only on a change).</summary>
        public bool SameAs(ReplaySession? other)
            => other != null && SessionId == other.SessionId && ExerciseId == other.ExerciseId && ReviewId == other.ReviewId
               && Active == other.Active && IsReview == other.IsReview && Axis == other.Axis && ZeroWallTicks == other.ZeroWallTicks
               && LengthSeconds.Equals(other.LengthSeconds) && ReferenceEquals(TimeMap, other.TimeMap)
               && Checkpoints.Length == other.Checkpoints.Length && Nullable.Equals(LivePointSimSeconds, other.LivePointSimSeconds)
               && Nullable.Equals(LivePointSeconds, other.LivePointSeconds);
    }

    /// <summary>
    /// ⭐ CE-3329 — the master's session layer changed: the DDS writer (<c>ClusterOpMasterTranslator</c>) puts it on
    /// <c>ReplaySessionTopic</c>. ⛔ Readers fold <see cref="ReplaySessionUpdateEvent"/>, never this (it would loop through the writer).
    /// </summary>
    [EventId(9070)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct ReplaySessionPublishedEvent
    {
        public ReplaySession Session;
    }

    /// <summary>
    /// ⭐ CE-3329 — the session layer as this host knows it: published by the master on its own bus and by the topic's readers
    /// (<c>NodeOpSlaveTranslator</c>, <c>OrchestrationObserverTranslator</c>) on every other host. <c>ClusterUiCache</c> and the time
    /// facades fold it.
    /// </summary>
    [EventId(9071)]
    [DataPolicy(DataPolicy.NoReplay)]
    public struct ReplaySessionUpdateEvent
    {
        public ReplaySession Session;
    }
}
