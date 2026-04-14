using System.Runtime.InteropServices;
using Fdp.Core;

namespace Hrot.IG.Components;

/// <summary>
/// ECS component storing a fixed-size circular buffer of XY world-space positions
/// that record where an entity has travelled.
///
/// Written each simulation tick by <see cref="Hrot.IG.Systems.HistoryRecordingSystem"/>
/// when <see cref="ResolvedStyle.ShowTrail"/> is <c>true</c>.
///
/// Implementation notes (§CODE-STANDARDS §4, §5):
/// <list type="bullet">
///   <item>
///     Unmanaged value type — safe to store inline in ECS chunk memory with no GC pressure.
///   </item>
///   <item>
///     <c>fixed float</c> arrays give O(1) append, O(1) indexed read, and zero allocation on the
///     hot path.  The buffer is treated as a circular queue: <see cref="Head"/> is the next write
///     slot; when <see cref="Count"/> equals <see cref="HistoryTrailConstants.MaxTrailPoints"/>
///     the oldest sample is silently overwritten.
///   </item>
///   <item>
///     All size constants are referenced from <see cref="HistoryTrailConstants"/> (§CODE-STANDARDS §1).
///   </item>
/// </list>
/// </summary>
[ComponentId(GlobalComponentIds.HistoryTrail)]
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct HistoryTrail
{
    // ── Circular buffer storage ───────────────────────────────────────────────
    // X and Y stored in separate arrays to preserve alignment and keep arithmetic clean.

    private fixed float _x[HistoryTrailConstants.MaxTrailPoints];
    private fixed float _y[HistoryTrailConstants.MaxTrailPoints];

    // ── Buffer state ──────────────────────────────────────────────────────────

    /// <summary>
    /// Number of valid samples currently stored (0 to
    /// <see cref="HistoryTrailConstants.MaxTrailPoints"/>).
    /// </summary>
    public int Count;

    /// <summary>
    /// Index of the <em>next write slot</em> in the circular buffer.
    /// When the buffer is full, this also identifies the oldest sample that will
    /// be overwritten on the next <see cref="AddPoint"/> call.
    /// </summary>
    public int Head;

    // ── Timing state ──────────────────────────────────────────────────────────

    /// <summary>Minimum seconds that must elapse between consecutive samples.</summary>
    public float SampleInterval;

    /// <summary>
    /// Accumulated <c>deltaTime</c> since the most recent sample was taken.
    /// Reset to the remainder (<c>ElapsedSinceSample - SampleInterval</c>) on
    /// each sample so that sub-frame timing is preserved.
    /// </summary>
    public float ElapsedSinceSample;

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a zeroed <see cref="HistoryTrail"/> ready to record, using
    /// <paramref name="sampleInterval"/> (defaults to
    /// <see cref="HistoryTrailConstants.DefaultSampleIntervalSeconds"/>).
    /// </summary>
    public static HistoryTrail Create(
        float sampleInterval = HistoryTrailConstants.DefaultSampleIntervalSeconds)
        => new HistoryTrail
        {
            Count              = 0,
            Head               = 0,
            SampleInterval     = sampleInterval,
            ElapsedSinceSample = 0f,
        };

    // ── Circular buffer mutation ──────────────────────────────────────────────

    /// <summary>
    /// Appends a world-space XY position to the circular buffer.
    /// If the buffer is already full the oldest sample is silently overwritten.
    /// </summary>
    public unsafe void AddPoint(float x, float y)
    {
        _x[Head] = x;
        _y[Head] = y;
        Head = (Head + 1) % HistoryTrailConstants.MaxTrailPoints;
        if (Count < HistoryTrailConstants.MaxTrailPoints)
            Count++;
    }

    // ── Circular buffer reading ───────────────────────────────────────────────

    /// <summary>
    /// Returns the XY world-space position at <paramref name="orderedIndex"/>,
    /// where index 0 is the oldest stored point and <c>Count − 1</c> is the most recent.
    /// </summary>
    /// <remarks>
    /// No bounds check is performed for performance reasons; callers must ensure
    /// <c>0 &lt;= orderedIndex &lt; Count</c>.
    /// </remarks>
    public unsafe (float X, float Y) GetPoint(int orderedIndex)
    {
        // When not yet full the oldest sample is always at slot 0.
        // When full, Head points to the slot that will be overwritten NEXT, which
        // is also the current oldest sample.
        int start = Count < HistoryTrailConstants.MaxTrailPoints ? 0 : Head;
        int slot  = (start + orderedIndex) % HistoryTrailConstants.MaxTrailPoints;
        return (_x[slot], _y[slot]);
    }
}
