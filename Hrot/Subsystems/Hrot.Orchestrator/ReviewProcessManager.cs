using System;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.ModuleHost.Time;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Time;
using Fdp.Toolkit.Time.Domain;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-3329</c> P3 (§3c, rows 19 and 42) — the CLOCK of a During Action Review: the master remembers the live point L, the
/// nodes remember their worlds.</b> 📄 docs/DESIGN_Replay_Time_Axis_And_Review.md §3c, §11b F3, F12, F19.
/// </summary>
/// <remarks>
/// <para>⭐ ENTRY — a review request from a live run pauses the cluster on the synchronised BARRIER (<c>PauseTimeIntent</c> →
/// <c>SwitchToDeterministic</c>), never a snap: a snap moves the timeline epoch and the kernel would reset every slot before the nodes
/// capture (F3). Must tick BEFORE <see cref="ClusterMaster"/>, so the pause precedes the <c>PrepareReview</c> fan-out; each node's
/// handler waits until its own clock has landed paused before it captures (F5). When the clock reports paused, L (the master's
/// <see cref="GlobalTime"/>) and the playback rate are remembered.</para>
/// <para>⭐ RETURN — when the cluster is back in <c>OperatingLive</c> from a review, the clock is put back to L the way a checkpoint
/// restore does it (<c>SnapTo</c>; each node's handler told its kernel the snap is a restore — <c>ResumeAfterRestore</c>, CE-3316) and
/// the live rate restored (F19). Live, paused.</para>
/// </remarks>
public sealed class ReviewProcessManager
{
    private readonly FdpEventBus _bus;
    private readonly ITimeController _clock;
    private readonly ITimeCommands _time;
    private readonly Func<ClusterState> _state;

    private bool _awaitingLivePoint;
    private bool _inReview;
    private GlobalTime _livePoint;
    private float _liveRate = 1f;

    /// <summary>The live clock the review returns to (valid once the entry pause has landed).</summary>
    public GlobalTime LivePoint => _livePoint;

    /// <summary>True from the entry request until the cluster is live again.</summary>
    public bool InReview => _inReview;

    /// <summary>⭐ CE-3329 F18 (row 45) — the review open now, minted at its entry request; <see cref="Guid.Empty"/> outside one.</summary>
    public Guid ReviewId { get; private set; }

    /// <summary>⭐ The live point has been captured (the entry pause landed) — <see cref="LivePoint"/> is valid.</summary>
    public bool HasLivePoint => _inReview && !_awaitingLivePoint;

    public ReviewProcessManager(FdpEventBus bus, ITimeController clock, ITimeCommands time, Func<ClusterState> state)
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public void Tick()
    {
        foreach (var intent in _bus.ReadManaged<TransitionStateIntent>())
        {
            if (intent.TargetState != ClusterState.OperatingReview || _state() != ClusterState.OperatingLive || _inReview) continue;
            _inReview = true;
            ReviewId = Guid.NewGuid();
            _awaitingLivePoint = true;
            _liveRate = _clock.GetTimeScale();
            _time.Pause();   // ⭐ the barrier pause — the PrepareReview fan-out follows in this tick (F3, F5)
            FdpLog<ReviewProcessManager>.Info("[Orchestrator] CE-3329: review {0} — pausing the live run on the barrier.", intent.TransactionId);
        }

        if (_awaitingLivePoint && _clock.GetMode() == TimeMode.Deterministic)
        {
            _livePoint = _clock.GetCurrentState();
            _awaitingLivePoint = false;
            FdpLog<ReviewProcessManager>.Info("[Orchestrator] CE-3329: the live point L = sim {0:F3}s (frame {1}).",
                _livePoint.TotalTime, _livePoint.FrameNumber);
        }

        foreach (var ev in _bus.ReadManaged<ClusterStateTransitionedEvent>())
        {
            if (!_inReview || ev.NewStateId != ClusterState.OperatingLive) continue;
            _inReview = false;
            ReviewId = Guid.Empty;
            _time.SnapTo(_livePoint);          // live, paused, at L — the checkpoint restore's clock (CE-3316)
            _time.SetTimeScale(_liveRate);     // F19 — the review's rate does not carry into live
            FdpLog<ReviewProcessManager>.Info("[Orchestrator] CE-3329: back to live at sim {0:F3}s, rate {1}.", _livePoint.TotalTime, _liveRate);
        }
    }
}
