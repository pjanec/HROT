using System;
using System.IO;
using Fdp.Core;
using Fdp.Core.FlightRecorder;

namespace Hrot.Blueprints.Core.Debug;

/// <summary>
/// Captures whole-repo sub-tick snapshots per blueprint node during a debug session.
/// Allows reconstructing ECS state as-of-entering any recorded node via <see cref="RestoreTo"/>.
///
/// <para><b>Capture strategy (keyframe-per-node):</b>
/// Each <see cref="RecordNodeEntry"/> call records a FULL KEYFRAME of the entire repo at
/// that moment. This avoids the chunk-version tracking problem that would arise if delta frames
/// were used: blueprint SetVar nodes write directly into the blackboard span (not via
/// <c>GetComponentRW</c>), so chunk versions are NOT updated by those writes and delta
/// detection would miss them.
/// </para>
///
/// <para><b>Ordering and attribution semantics (correct):</b>
/// Inside <see cref="RecordNodeEntry"/>, the ordering is:
/// <list type="number">
///   <item>Record a FULL KEYFRAME of current repo state — captures all mutations the PREVIOUS
///         node wrote before this probe fires.</item>
///   <item>Store <c>(nodeId, snapshotBytes)</c> in the ring — snapshot[K] = state as-of ENTERING
///         node K (before node K's own writes).</item>
///   <item>Bump <c>repo.BumpMemoryVersion()</c> — advances GV for sub-tick debug granularity
///         (required by other sub-systems; does not affect snapshot correctness).</item>
/// </list>
/// </para>
///
/// <para><b>Attribution:</b>
/// <list type="bullet">
///   <item><c>snapshot[0]</c> (for n0): state before n0 ran → initial tick state.</item>
///   <item><c>snapshot[1]</c> (for n1): state after n0 wrote, before n1 ran.</item>
///   <item>Restoring to index K = apply snapshot[K] as keyframe.</item>
///   <item>With value 5→(n0)→6→(n1)→7: restore(0)=5, restore(1)=6, restore(2)=7.</item>
/// </list>
/// Capture is WHOLE-REPO (not per-entity) because blueprints can write managed components
/// and other entities' components synchronously within a single node execution.
/// </para>
///
/// <para><b>Ring overflow:</b> when <see cref="Capacity"/> entries are exceeded, the oldest
/// entry is dropped and <see cref="DroppedFrameCount"/> is incremented. The recorder never
/// throws on overflow; the caller can check the counter as the overflow signal.</para>
/// </summary>
public sealed class SubTickSnapshotRecorder
{
    /// <summary>Default ring capacity (number of node-entry slots).</summary>
    public const int DefaultCapacity = 256;

    private readonly RecorderSystem _recorder;
    private readonly PlaybackSystem _playback;

    // Ring of per-node keyframe snapshots (full repo state as-of entering that node).
    // No separate keyframe baseline needed: each ring entry IS a keyframe.
    private readonly SubTickEntry[] _ring;
    private int _ringHead;   // next write position (mod Capacity)
    private int _count;      // number of valid entries (0..Capacity)
    private int _droppedFrameCount;

    // Is a tick recording in progress?
    private bool _inTick;

    /// <summary>Ring capacity (maximum number of stored node entries).</summary>
    public int Capacity { get; }

    /// <summary>Number of currently stored entries.</summary>
    public int Count => _count;

    /// <summary>
    /// Number of entries dropped due to ring overflow since the last <see cref="BeginTick"/> call.
    /// A non-zero value signals that the ring has wrapped and history was lost.
    /// </summary>
    public int DroppedFrameCount => _droppedFrameCount;

    /// <summary>Create a recorder with the specified ring capacity.</summary>
    public SubTickSnapshotRecorder(int capacity = DefaultCapacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity      = capacity;
        _ring         = new SubTickEntry[capacity];
        _recorder     = new RecorderSystem();
        _playback     = new PlaybackSystem();
    }

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Start a new tick recording session.
    /// Resets the ring. Must be called once per tick before any <see cref="RecordNodeEntry"/> calls.
    /// </summary>
    public void BeginTick(EntityRepository repo)
    {
        if (repo is null) throw new ArgumentNullException(nameof(repo));

        // Reset ring state.
        _ringHead          = 0;
        _count             = 0;
        _droppedFrameCount = 0;
        _inTick            = true;
    }

    /// <summary>
    /// Record a per-node entry for <paramref name="nodeId"/>.
    /// Must be called at the start of a node's execution (before the node writes anything).
    ///
    /// <para>Records a FULL KEYFRAME of the current repo state. This correctly captures
    /// blueprint variable values even though blueprint SetVar nodes write directly into
    /// the blackboard span (bypassing <c>GetComponentRW</c> and therefore not stamping
    /// chunk versions). Delta-frame recording would miss those writes.</para>
    ///
    /// <para>Ordering performed internally:</para>
    /// <list type="number">
    ///   <item>Capture full keyframe of current repo state — includes all writes the PREVIOUS
    ///         node made before this probe fires.</item>
    ///   <item>Store <c>(nodeId, snapshotBytes)</c> in the ring.</item>
    ///   <item>Bump <c>repo.BumpMemoryVersion()</c> — advances GV for sub-tick debug
    ///         granularity (required by other sub-systems).</item>
    /// </list>
    /// </summary>
    public void RecordNodeEntry(EntityRepository repo, string nodeId)
    {
        if (repo is null)    throw new ArgumentNullException(nameof(repo));
        if (nodeId is null)  throw new ArgumentNullException(nameof(nodeId));
        if (!_inTick) throw new InvalidOperationException(
            "RecordNodeEntry called outside of a tick. Call BeginTick first.");

        // Step 1: Capture a full keyframe of current repo state.
        //         Blueprint SetVar nodes write directly into the blackboard memory span,
        //         bypassing GetComponentRW and therefore NOT updating chunk versions.
        //         Delta frames would see no changes (chunk version didn't advance).
        //         A keyframe always captures the complete current state regardless.
        using var ms     = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        _recorder.RecordKeyframe(repo, writer, wallClockTicks: 0L);
        byte[] snapshotBytes = ms.ToArray();

        // Step 2: Store (nodeId, snapshotBytes) in the ring.
        bool overflow = _count == Capacity;
        if (overflow)
        {
            // Drop the oldest entry (advance the "logical start" of the ring).
            _droppedFrameCount++;
            _count--; // will be re-incremented below
        }

        _ring[_ringHead % Capacity] = new SubTickEntry(nodeId, snapshotBytes);
        _ringHead++;
        _count++;

        // Step 3: Bump the memory version for sub-tick GV granularity.
        //         Other sub-systems (e.g. SimulationTickFrozen tests) rely on GV advancing
        //         during a recorded debug tick even though no real Tick() was called.
        repo.BumpMemoryVersion();
    }

    /// <summary>
    /// Returns the node-id string for the entry at logical <paramref name="index"/> (0 = oldest stored).
    /// </summary>
    public string NodeIdAt(int index)
    {
        if (index < 0 || index >= _count)
            throw new ArgumentOutOfRangeException(nameof(index));
        int slot = RingSlot(index);
        return _ring[slot].NodeId;
    }

    /// <summary>
    /// Reconstruct the whole-repo state AS-OF entering the node at logical
    /// <paramref name="nodeIndex"/> into <paramref name="scratchRepo"/>.
    /// "As-of entering" means: before that node's own writes have been applied.
    ///
    /// <para>Algorithm: apply the stored keyframe for <paramref name="nodeIndex"/> directly.
    /// Each ring entry is a complete keyframe snapshot taken when that node's probe fired,
    /// capturing all writes by prior nodes and none by the current node.</para>
    ///
    /// <para>The caller owns <paramref name="scratchRepo"/> — pass a reusable throwaway repo
    /// registered with the same component types as the source repo.</para>
    /// </summary>
    public void RestoreTo(int nodeIndex, EntityRepository scratchRepo)
    {
        if (scratchRepo is null) throw new ArgumentNullException(nameof(scratchRepo));
        if (nodeIndex < 0 || nodeIndex >= _count)
            throw new ArgumentOutOfRangeException(nameof(nodeIndex));

        // Apply the keyframe snapshot for this node directly.
        // Each ring entry is a full-repo keyframe, so no delta accumulation is needed.
        int slot             = RingSlot(nodeIndex);
        byte[] snapshotBytes = _ring[slot].SnapshotBytes;
        using var ms         = new MemoryStream(snapshotBytes);
        using var reader     = new BinaryReader(ms);
        _playback.ApplyFrame(scratchRepo, reader);
    }

    // ── Internals ───────────────────────────────────────────────────────────

    /// <summary>Map logical index (0 = oldest) to ring slot.</summary>
    private int RingSlot(int logicalIndex)
    {
        // The ring head always points one-past the most-recently written slot.
        // Oldest entry is at (head - count) mod Capacity.
        int oldestSlot = (_ringHead - _count + Capacity * 8) % Capacity;
        return (oldestSlot + logicalIndex) % Capacity;
    }

    private readonly struct SubTickEntry
    {
        public readonly string NodeId;
        public readonly byte[] SnapshotBytes;
        public SubTickEntry(string nodeId, byte[] snapshotBytes)
        {
            NodeId         = nodeId;
            SnapshotBytes  = snapshotBytes;
        }
    }
}
