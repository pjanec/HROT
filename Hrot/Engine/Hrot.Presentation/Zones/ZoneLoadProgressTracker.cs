using System;
using System.Collections.Generic;

namespace Hrot.Presentation.Zones;

/// <summary>
/// ⭐⭐⭐ <c>E3</c> — which zones have a cluster round in flight, tracked WITHOUT asking the orchestrator.
///
/// <para>⛔⛔ <b>Why this exists instead of reading <c>ClusterMaster</c>.</b> Two independent reasons,
/// either sufficient:</para>
/// <list type="number">
///   <item>📐 <b>Measured, design §9.4:</b> <c>ClusterMaster.HasInFlightTransaction</c> is
///     <c>_activeTransaction != null</c>, and <c>_activeTransaction</c> is assigned and cleared <b>in the
///     same method</b> ("ClusterMaster uses sync fan-out; clear immediately"). ⇒ it answers <c>false</c>
///     while rounds are genuinely pending, so a progress indicator sourced from it reads "idle"
///     throughout every load. The honest in-flight set is <c>_pendingTransactions</c> — which is
///     PRIVATE, with no accessor.</item>
///   <item>⛔ <b>Layering:</b> <c>Hrot.Editor.AiShared</c> (where the details shell lives) does not
///     reference <c>Hrot.Orchestrator</c>, and must not start to — that is the same inversion
///     <c>C8</c> refused for IG. ⇒ even a new accessor would be unreachable from the view.</item>
/// </list>
///
/// <para>⭐⭐ <b>So the requester tracks its own requests.</b> The UI knows what it asked for, and the
/// completion event tells it when a request is done — that is the same information
/// <c>_pendingTransactions</c> holds, observed from the side that cares. ⚠ It is deliberately keyed by
/// ZONE rather than by transaction: one op per zone (§9.3) makes those equivalent, and the view's rows
/// are zones.</para>
///
/// <para>⚠ <b>Stated limit, so nobody reads more into it.</b> This tracks what THIS UI requested. A round
/// another operator started elsewhere is invisible here until its completion event arrives. ⛔ That is
/// not a defect to paper over with a timer: the alternative — inferring cluster state from a local
/// marker — is exactly what §9.1 rules out.</para>
///
/// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §9.4, §9.5, §9.3.
/// </summary>
public sealed class ZoneLoadProgressTracker
{
    private readonly HashSet<string> _inFlight = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> _byRequest = new();

    /// <summary>The zone ids with a round this UI is still waiting on.</summary>
    public IReadOnlySet<string> InFlight => _inFlight;

    /// <summary>Record that a zone-load op was published, so the row can show it running.</summary>
    public void Requested(Guid requestId, string zoneId)
    {
        if (string.IsNullOrEmpty(zoneId)) return;
        _byRequest[requestId] = zoneId;
        _inFlight.Add(zoneId);
    }

    /// <summary>
    /// Clear a zone once its op completes — success or failure alike.
    /// <para>⭐ The OUTCOME is not stored here: on success the node's own marker becomes
    /// <c>Loaded</c>/<c>Failed</c> and the row reads it, so keeping a second copy of the answer would be
    /// two producers for one slot (<c>R-132</c>). This tracks only <i>"am I still waiting?"</i>.</para>
    /// </summary>
    public void Completed(Guid requestId)
    {
        if (!_byRequest.Remove(requestId, out var zoneId)) return;
        _inFlight.Remove(zoneId);
    }

    /// <summary>
    /// ⚠ Drop everything, for a world/cluster boundary. Without this a round that was in flight when the
    /// cluster went down would leave a row spinning for the rest of the session.
    /// </summary>
    public void Reset()
    {
        _inFlight.Clear();
        _byRequest.Clear();
    }
}
