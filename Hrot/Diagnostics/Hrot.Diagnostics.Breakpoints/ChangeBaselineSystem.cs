using System;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// ⭐⭐ <c>CE-1284</c> (Behaviour Debugging DBG-8) — the baseline a "changed" breakpoint compares against, taken in <see cref="SystemPhase.PreFrame"/>,
/// BEFORE Input. 🔴 Measured: the only baseline was the rewind snapshot at <see cref="SystemPhase.BeforeSync"/> (<see cref="DebugSnapshotProvider"/>),
/// AFTER Input — and Input is where a unit's task changes (the ingress writes the brain journal there), so the baseline already held the
/// change and <i>"Break when it changes its mind"</i> never broke (🔒 user: <i>"never seen it breaking"</i>).
/// <para>⭐ Copies ONLY the component types a mounted "changed" condition compares (cheap), into its own world; the rewind snapshot stays at
/// <c>BeforeSync</c> (moving it would make the rewind replay Input and lose its events — DBG-8's rejected alternative).</para>
/// </summary>
[UpdateInPhase(SystemPhase.PreFrame)]
[TickMode(TickPolicy.WhenTimeAdvances)]
public sealed class ChangeBaselineSystem : IEcsModuleSystem
{
    private readonly DataBreakpointManager _manager;
    private readonly EntityRepository _baseline;

    public ChangeBaselineSystem(DataBreakpointManager manager, EntityRepository baseline)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
        _baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
        _manager.UseChangeBaseline(_baseline);
    }

    public void Execute(ISimulationView view, float deltaTime)
    {
        if (!_manager.HasMountedDelegates || view is not EntityRepository repo) return;
        var mask = _manager.ChangedComponentMask();
        if (mask.Equals(default(BitMask512))) return;
        _baseline.SyncFrom(repo, mask);
    }
}
