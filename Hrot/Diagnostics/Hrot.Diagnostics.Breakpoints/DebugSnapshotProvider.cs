using System;
using System.Threading;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// Captures a snapshot of the live entity repository at the start of every tick
/// while the gate is open. Scheduled in <see cref="SystemPhase.BeforeSync"/> so it
/// runs before any module ticks, giving a clean pre-tick image.
///
/// The gate is a <c>volatile int</c> (0 = off, 1 = on). When the gate is 0,
/// <see cref="Execute"/> returns after a single branch — zero allocation, zero work.
/// </summary>
[UpdateInPhase(SystemPhase.BeforeSync)]
public sealed class DebugSnapshotProvider : IEcsModuleSystem
{
    private readonly EntityRepository _preTickSnapshot;
    private volatile int _isEnabled;

    /// <summary>
    /// Initializes a new <see cref="DebugSnapshotProvider"/>.
    /// </summary>
    /// <param name="preTickSnapshot">
    /// Pre-allocated repository that will receive the start-of-tick snapshot.
    /// Must be allocated by the owning manager and outlive this provider.
    /// </param>
    public DebugSnapshotProvider(EntityRepository preTickSnapshot)
    {
        _preTickSnapshot = preTickSnapshot ?? throw new ArgumentNullException(nameof(preTickSnapshot));
    }

    /// <summary>
    /// Atomically enables or disables snapshot capture.
    /// Thread-safe; may be called from any thread.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        Interlocked.Exchange(ref _isEnabled, enabled ? 1 : 0);
    }

    /// <summary>
    /// Exposes the raw gate value for testing (internal visibility).
    /// </summary>
    internal int IsEnabledRaw => _isEnabled;

    /// <summary>
    /// Executes the system. When the gate is off, returns immediately (zero cost).
    /// When the gate is on, synchronises <c>_preTickSnapshot</c> from the live repository.
    /// </summary>
    /// <param name="view">The live simulation view (must be an <see cref="EntityRepository"/>).</param>
    /// <param name="deltaTime">Time since last execution (seconds).</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="view"/> is not an <see cref="EntityRepository"/>.
    /// </exception>
    public void Execute(ISimulationView view, float deltaTime)
    {
        if (_isEnabled == 0) return;

        if (view is not EntityRepository repo)
            throw new InvalidOperationException(
                $"DebugSnapshotProvider requires an EntityRepository view, got {view?.GetType().Name ?? "null"}.");

        _preTickSnapshot.SyncFrom(repo);
    }
}
