using Fdp.Toolkit.Orchestration;

namespace Fdp.Presentation.WindowManager;

/// <summary>
/// ⭐⭐ <c>CE-4096</c> (AQ108 §2.6) — what the start gate reads: ONE fact about the ONE cluster, taken from any node.
/// </summary>
/// <param name="State">The cluster state this host last heard; <see langword="null"/> = no master seen yet.</param>
/// <param name="MasterGone">The master announced it is gone (or a node's slave says so).</param>
/// <param name="CanOperate">Some provider in this process can drive the master (<c>Master != null</c>) — the actions are meaningful here.</param>
public readonly record struct ClusterRest(ClusterState? State, bool MasterGone, bool CanOperate)
{
    /// <summary>The cluster is AT REST: Idle, or no master to say otherwise (never seen / gone). ⚠ Resting for one frame is not yet "at rest" —
    /// <see cref="StartGate"/> holds it for <see cref="StartGate.SettleSeconds"/>.</summary>
    public bool IsResting => MasterGone || State is null || State == ClusterState.Idle;
}

/// <summary>
/// ⭐ <c>CE-4096</c> — a subsystem that knows the cluster's state but builds no <c>ISubsystemDebugProvider</c> (the Editor: it hosts the
/// in-process master and answers its own state). The runner folds these with the providers into ONE <see cref="ClusterRest"/>.
/// </summary>
public interface IClusterRestSource
{
    /// <summary>What this subsystem knows about the cluster right now.</summary>
    ClusterRest ReadClusterRest();
}

/// <summary>⭐ What the start surface shows (<c>D16</c>): the actions, or one waiting sentence.</summary>
public enum StartVariant
{
    /// <summary>"Waiting for the server to load a scenario" — a host that cannot operate the cluster, or no master seen yet.</summary>
    WaitingForServer,
    /// <summary>The menu items marked allowed-in-Idle, as buttons.</summary>
    Actions,
    /// <summary>"Server lost — waiting".</summary>
    ServerLost,
}

/// <summary>
/// ⭐⭐⭐ <b>The ONE start gate</b> — enters <see cref="WindowManager.StartPerspectiveName"/> when the cluster has RESTED in Idle, leaves it
/// the moment the cluster does anything else. 📄 <c>docs/blueprints/Architect_Question_108_Run_Mode_Picker.md</c> §2.6 / D16.
/// <para>⭐ <b>Resting = Idle (or MasterGone, or no master yet) held continuously ≥ <see cref="SettleSeconds"/>.</b> The ordinary load is TWO
/// requests (Idle, then the load — CE-295), so every host sees a real Idle between them; the settle swallows it on the HOST — no wire change.</para>
/// <para>⛔ It owns no remember-and-restore: <see cref="WindowManager.SwitchPerspective"/> remembers on entry and
/// <see cref="WindowManager.LeaveStartPerspective"/> restores (CE-4094).</para>
/// </summary>
public sealed class StartGate
{
    /// <summary>⭐ How long the cluster must rest before Start shows. Long enough for CE-295's two-request pass-through, short enough to feel instant.</summary>
    public const double SettleSeconds = 0.5;

    private readonly WindowManager _wm;
    private double? _restingSince;

    public StartGate(WindowManager wm) => _wm = wm ?? throw new ArgumentNullException(nameof(wm));

    /// <summary>What the surface shows now (recomputed every <see cref="Tick"/>).</summary>
    public StartVariant Variant { get; private set; } = StartVariant.WaitingForServer;

    /// <summary>The cluster has been resting, unbroken, since this time (seconds); <see langword="null"/> while it is not.</summary>
    public double? RestingSince => _restingSince;

    /// <summary>Once a frame, before <see cref="WindowManager.Render"/>.</summary>
    public void Tick(double nowSeconds, ClusterRest rest)
    {
        Variant = rest.MasterGone ? StartVariant.ServerLost
                : rest.CanOperate ? StartVariant.Actions
                : StartVariant.WaitingForServer;

        if (!rest.IsResting)
        {
            _restingSince = null;
            if (_wm.IsStartShowing) _wm.LeaveStartPerspective(out _);
            return;
        }

        _restingSince ??= nowSeconds;
        if (!_wm.IsStartShowing && nowSeconds - _restingSince.Value >= SettleSeconds)
            _wm.SwitchPerspective(WindowManager.StartPerspectiveName);
    }
}
