using Hrot.Diagnostics.Breakpoints;

namespace Hrot.Editor.AiShared.Debug;

/// <summary>
/// Reference-counted asset observation tracker.
/// When multiple observers request the same asset, the effective TraceLevel
/// is the bitwise OR (union) of all requested levels.
/// On refcount reaching zero, EndObservingAssetImpl is called.
/// Subsystem coordinators derive and override BeginObservingAssetImpl/EndObservingAssetImpl
/// to talk to their kernel.
///
/// <para>⭐⭐⭐ <b><c>CE-349</c> — TIME CONTROL COMES FROM <see cref="IEngineDebugTimeController"/>,
/// THE ONE DEBUGGER TIME ABSTRACTION.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.24.</para>
///
/// <para>🔒 User, <c>2026-09-26</c>: *"cgf should use the cluster time control but i think editor
/// should do the same, as editor also has its local cluster orchestrator, so for consistency they
/// should be using same time control means."*</para>
///
/// <para>📐 Measured: <see cref="IEngineDebugTimeController"/> was ALREADY the shared surface —
/// <c>BlueprintDebugSession</c>, <c>DataBreakpointManager</c> and <c>PerspectiveWorkspaceRegistrar</c>
/// all consume it, and BOTH hosts already construct an implementation
/// (<c>MasterSyncTimeControllerAdapter</c> on the editor, <c>CgfClusterDebugTimeController</c> on CGF —
/// both even spelled <c>bpTimeAdapter</c>). ⛔ This class was the single hold-out, reaching time
/// through a SECOND abstraction (<c>ITimeCommands</c>) that had one implementation on one host.
/// ⇒ the per-host subclass is deleted; BTree/HSM pause and step now work on BOTH hosts.</para>
///
/// <para>⚠ <b>The property that was given up, stated plainly.</b> The deleted editor subclass
/// published time INTENTS on the orchestration bus, so its pause would have fanned out cluster-wide
/// for free. The adapter it is replaced with calls the local <c>MasterSyncController</c> directly.
/// ⭐ That is not a new asymmetry — it is the path the editor's OWN Blueprint debugger and breakpoint
/// manager have always taken. ⇒ if direct-vs-intent is wrong, it is now wrong in ONE place for all
/// four debuggers instead of right in one corner and wrong in three (<c>CE-350</c>).</para>
/// </summary>
public class AiTracerCoordinator
{
    // Key: assetId. Value: (refcount, effective TraceLevel)
    private readonly Dictionary<Guid, (int RefCount, TraceLevel Level)> _observed = new();

    /// <summary>
    /// ⭐ The host's debugger time control, or <see langword="null"/> for the inert coordinator that
    /// tests and lightweight containers construct.
    /// ⛔ <b>A production host that HAS one MUST pass it</b> — the silent-default rule, and the exact
    /// defect <c>T4d</c> fixed once already (production built the bare base and pause did nothing).
    /// ⭐ <c>AiDebugSessionComposer</c> is the guard: it refuses <see langword="null"/>.
    /// </summary>
    protected IEngineDebugTimeController? TimeController { get; }

    /// <summary>⭐ For rails: whether this coordinator can actually stop the simulation.</summary>
    public bool HasTimeControl => TimeController is not null;

    /// <param name="timeController">
    /// The host's debugger time control. ⚠ Optional ONLY so the three test spies and the DI default
    /// keep a parameterless path; production passes one.
    /// </param>
    public AiTracerCoordinator(IEngineDebugTimeController? timeController = null)
        => TimeController = timeController;

    /// <summary>Increments refcount for the asset. Calls BeginObservingAssetImpl on first call.</summary>
    public void AddObserver(Guid assetId, TraceLevel level)
    {
        if (_observed.TryGetValue(assetId, out var existing))
        {
            _observed[assetId] = (existing.RefCount + 1, existing.Level | level);
        }
        else
        {
            _observed[assetId] = (1, level);
            BeginObservingAssetImpl(assetId, level);
        }
    }

    /// <summary>Decrements refcount. Calls EndObservingAssetImpl on reaching zero.</summary>
    public void RemoveObserver(Guid assetId)
    {
        if (!_observed.TryGetValue(assetId, out var existing)) return;

        if (existing.RefCount <= 1)
        {
            _observed.Remove(assetId);
            EndObservingAssetImpl(assetId);
        }
        else
        {
            _observed[assetId] = (existing.RefCount - 1, existing.Level);
        }
    }

    /// <summary>Effective TraceLevel for the asset (union of all observer levels). Zero if not observed.</summary>
    public TraceLevel GetEffectiveLevel(Guid assetId) =>
        _observed.TryGetValue(assetId, out var v) ? v.Level : TraceLevel.None;

    /// <summary>True if at least one observer is watching this asset.</summary>
    public bool IsObserving(Guid assetId) => _observed.ContainsKey(assetId);

    /// <summary>
    /// Called on first observer for an asset.
    /// Override in subsystem-specific subclasses to set DebugState.Flags on matching entities.
    /// Default: no-op (test-friendly).
    /// </summary>
    protected virtual void BeginObservingAssetImpl(Guid assetId, TraceLevel level) { }

    /// <summary>Called when refcount reaches zero. Override to clear DebugState.Flags.</summary>
    protected virtual void EndObservingAssetImpl(Guid assetId) { }

    /// <summary>Requests the simulation to execute exactly one tick, then pause.</summary>
    public virtual void RequestStepOneTick() => TimeController?.RequestStepOneTick();

    /// <summary>Requests the simulation to pause at the next tick boundary.</summary>
    public virtual void RequestPause() => TimeController?.RequestPause();

    /// <summary>
    /// Requests the simulation to resume continuous execution.
    /// ⚠ Named <c>Continue</c> here and <c>Resume</c> on the interface — the debugger vocabulary the
    /// BTree/HSM sessions were written against. ⭐ One verb, two spellings, mapped once, right here.
    /// </summary>
    public virtual void RequestContinue() => TimeController?.RequestResume();
}
