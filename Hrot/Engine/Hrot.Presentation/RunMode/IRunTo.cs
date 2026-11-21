using System;
using Fdp.Core;

namespace Hrot.UI.Common.RunMode;

/// <summary>
/// ⭐ N-5 (CE-4467) — Run to … as the UI's surfaces (the toolbar's mode menu, its popup) use it. <see cref="RunToController"/> is the run itself
/// (it steps the node frame, so in a threaded node it lives on the SIM thread); <see cref="ThreadedRunTo"/> is the UI thread's view of it.
/// </summary>
public interface IRunTo
{
    bool IsTurbo { get; }
    bool IsRunning { get; }
    bool IsStarting { get; }
    bool CanStartFromEdit { get; }
    /// <summary>The last outcome / progress line.</summary>
    string Status { get; }
    /// <summary>The world the popup reads its phases and rules from (the caller's own world).</summary>
    EntityRepository? World { get; }
    /// <summary>Asks for a run to <paramref name="atSeconds"/> or until <paramref name="until"/> holds (evaluated by the run on its world).</summary>
    bool Request(double? atSeconds, Func<EntityRepository, Entity, bool>? until, float? stepSeconds = null);
    void Cancel(string why = "stopped");
}

/// <summary>
/// ⭐ N-5 (CE-4467; D-9's pattern) — Run to … on the UI thread of a threaded node: <see cref="Request"/> / <see cref="Cancel"/> are POSTED to the
/// sim thread and run on the real <see cref="RunToController"/> there; what the UI shows (running, starting, the status line) comes from a
/// <see cref="Readout"/> the sim publishes when it changes (<see cref="ReadIfChanged"/>, applied on the UI thread in tick order). Request returns
/// true when the ask was SENT — its outcome (accepted, or refused with a reason) arrives as the status.
/// </summary>
public sealed class ThreadedRunTo : IRunTo
{
    /// <summary>What the UI reads, computed on the sim thread after a tick (immutable; replaced whole).</summary>
    public sealed record Readout(bool Running, bool Starting, string Status);

    private readonly RunToController _sim;
    private readonly Action<string, Action> _post;
    private readonly Func<EntityRepository?> _uiWorld;
    private Readout _readout = new(false, false, "");

    public ThreadedRunTo(RunToController sim, Action<string, Action> post, Func<EntityRepository?> uiWorld)
    {
        _sim = sim ?? throw new ArgumentNullException(nameof(sim));
        _post = post ?? throw new ArgumentNullException(nameof(post));
        _uiWorld = uiWorld ?? throw new ArgumentNullException(nameof(uiWorld));
    }

    /// <summary>Fixed at construction (no sim state).</summary>
    public bool IsTurbo => _sim.IsTurbo;
    public bool CanStartFromEdit => _sim.CanStartFromEdit;
    public bool IsRunning => _readout.Running;
    public bool IsStarting => _readout.Starting;
    public string Status => _readout.Status;
    public EntityRepository? World => _uiWorld();

    public bool Request(double? atSeconds, Func<EntityRepository, Entity, bool>? until, float? stepSeconds = null)
    {
        _post("run to: request", () => _sim.Request(atSeconds, until, stepSeconds));
        return true;
    }

    public void Cancel(string why = "stopped") => _post("run to: cancel", () => _sim.Cancel(why));

    /// <summary>The UI thread: the readout the sim published.</summary>
    public void Apply(Readout readout) => _readout = readout ?? _readout;

    /// <summary>The sim thread, after a tick: a new readout when the run's visible state changed, else null (no allocation).</summary>
    public static Readout? ReadIfChanged(Readout? last, RunToController sim)
    {
        if (last != null && last.Running == sim.IsRunning && last.Starting == sim.IsStarting && ReferenceEquals(last.Status, sim.Status)) return null;
        return new Readout(sim.IsRunning, sim.IsStarting, sim.Status);
    }
}
