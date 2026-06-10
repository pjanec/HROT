using System;
using System.IO;
using Fdp.Core;
using Fdp.Core.FlightRecorder;

namespace Hrot.Blueprints.Core.Debug;

/// <summary>
/// Captures whole-repo sub-tick snapshot deltas per blueprint node during a debug session.
/// Allows reconstructing ECS state as-of-entering any recorded node via <see cref="RestoreTo"/>.
///
/// <para><b>Ordering and attribution semantics (critical correctness):</b>
/// Inside <see cref="RecordNodeEntry"/>, the ordering is:
/// <list type="number">
///   <item>Capture delta from <c>_prevVersion</c> to current <c>repo.GlobalVersion</c> — captures
///         all mutations the PREVIOUS node wrote (stamped when that node ran).</item>
///   <item>Store <c>(nodeId, deltaBytes)</c> in the ring — delta[K] = "what changed between entry
///         of node K-1 and entry of node K" = effect of node K-1.</item>
///   <item>Advance <c>_prevVersion</c> to current <c>repo.GlobalVersion</c>.</item>
///   <item>Bump <c>repo.BumpMemoryVersion()</c> — advances GV so THIS node's upcoming writes
///         will be stamped at a fresh version, isolated from the delta already captured.</item>
/// </list>
/// </para>
///
/// <para><b>Why this ordering attributes writes correctly (no off-by-one):</b>
/// <list type="bullet">
///   <item><c>delta[0]</c> (stored for n0): captures nothing before n0 ran → empty delta.</item>
///   <item><c>delta[1]</c> (stored for n1): captures what n0 wrote between RecordNodeEntry("n0")
///         and RecordNodeEntry("n1").</item>
///   <item>Restoring to index K = keyframe + delta[0..K] = state BEFORE node K's own effect.</item>
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

    // Keyframe baseline: whole-repo snapshot taken at BeginTick.
    private byte[] _keyframeBytes = Array.Empty<byte>();

    // Ring of per-node deltas.
    private readonly SubTickEntry[] _ring;
    private int _ringHead;   // next write position (mod Capacity)
    private int _count;      // number of valid entries (0..Capacity)
    private int _droppedFrameCount;

    // Version cursor: we capture deltas from _prevVersion to current GV.
    private uint _prevVersion;

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
    /// Resets the ring and records a full-repo keyframe baseline.
    /// Must be called once per tick before any <see cref="RecordNodeEntry"/> calls.
    /// </summary>
    public void BeginTick(EntityRepository repo)
    {
        if (repo is null) throw new ArgumentNullException(nameof(repo));

        // Reset ring state.
        _ringHead          = 0;
        _count             = 0;
        _droppedFrameCount = 0;
        _inTick            = true;

        // Record the full-repo keyframe at the current memory version.
        // _prevVersion is set to the current GV so the first delta is "since right now".
        using var ms     = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        _recorder.RecordKeyframe(repo, writer, wallClockTicks: 0L);
        _keyframeBytes = ms.ToArray();

        // Snapshot the version cursor AFTER keyframe recording.
        _prevVersion = repo.GlobalVersion;
    }

    /// <summary>
    /// Record a per-node entry for <paramref name="nodeId"/>.
    /// Must be called at the start of a node's execution (before the node writes anything).
    ///
    /// <para>Ordering performed internally (see class-level remarks):</para>
    /// <list type="number">
    ///   <item>Capture delta from <c>_prevVersion</c> → captures the PREVIOUS node's writes.</item>
    ///   <item>Store <c>(nodeId, delta)</c> in the ring.</item>
    ///   <item>Advance <c>_prevVersion</c> to current GV.</item>
    ///   <item>Bump <c>repo.BumpMemoryVersion()</c> → isolates THIS node's upcoming writes.</item>
    /// </list>
    /// </summary>
    public void RecordNodeEntry(EntityRepository repo, string nodeId)
    {
        if (repo is null)    throw new ArgumentNullException(nameof(repo));
        if (nodeId is null)  throw new ArgumentNullException(nameof(nodeId));
        if (!_inTick) throw new InvalidOperationException(
            "RecordNodeEntry called outside of a tick. Call BeginTick first.");

        // Step 1: Capture delta from _prevVersion to current GV.
        //         This captures all mutations written since the last RecordNodeEntry
        //         (or since BeginTick for the first call).
        using var ms     = new MemoryStream();
        using var writer = new BinaryWriter(ms);
        _recorder.RecordDeltaFrame(repo, _prevVersion, writer, wallClockTicks: 0L);
        byte[] deltaBytes = ms.ToArray();

        // Step 2: Store (nodeId, deltaBytes) in the ring.
        bool overflow = _count == Capacity;
        if (overflow)
        {
            // Drop the oldest entry (advance the "logical start" of the ring).
            // The head always points at the next write slot; when full, writing there
            // overwrites the oldest entry.
            _droppedFrameCount++;
            _count--; // will be re-incremented below
        }

        _ring[_ringHead % Capacity] = new SubTickEntry(nodeId, deltaBytes);
        _ringHead++;
        _count++;

        // Step 3: Advance the version cursor to the current GV so the next capture
        //         will only pick up mutations written AFTER this call.
        _prevVersion = repo.GlobalVersion;

        // Step 4: Bump the memory version so THIS node's upcoming writes are stamped
        //         at a fresh GV, keeping them isolated from the just-captured delta.
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
    /// <para>Algorithm: apply keyframe baseline, then deltas[0..nodeIndex] in order.
    /// Each delta[K] carries what node K-1 wrote; after applying delta[K] the scratch
    /// repo reflects the state the node K was about to see when it started.</para>
    ///
    /// <para>The caller owns <paramref name="scratchRepo"/> — pass a reusable throwaway repo
    /// registered with the same component types as the source repo.</para>
    /// </summary>
    public void RestoreTo(int nodeIndex, EntityRepository scratchRepo)
    {
        if (scratchRepo is null) throw new ArgumentNullException(nameof(scratchRepo));
        if (nodeIndex < 0 || nodeIndex >= _count)
            throw new ArgumentOutOfRangeException(nameof(nodeIndex));

        // Apply keyframe baseline (performs repo.Clear() internally).
        using var kfMs     = new MemoryStream(_keyframeBytes);
        using var kfReader = new BinaryReader(kfMs);
        _playback.ApplyFrame(scratchRepo, kfReader);

        // Apply deltas[0..nodeIndex] in logical order.
        for (int i = 0; i <= nodeIndex; i++)
        {
            int slot  = RingSlot(i);
            byte[] deltaBytes = _ring[slot].DeltaBytes;
            using var dMs     = new MemoryStream(deltaBytes);
            using var dReader = new BinaryReader(dMs);
            _playback.ApplyFrame(scratchRepo, dReader);
        }
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
        public readonly byte[] DeltaBytes;
        public SubTickEntry(string nodeId, byte[] deltaBytes)
        {
            NodeId     = nodeId;
            DeltaBytes = deltaBytes;
        }
    }
}
