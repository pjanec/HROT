using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.ReplayBrowser.Search;

namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// Per-subsystem orchestrator for data-driven simulation breakpoints.
///
/// Lifecycle:
///   - Call <see cref="Add"/> to register a breakpoint (returns its stable id).
///   - Toggle with <see cref="SetEnabled"/>; the snapshot gate is reference-counted:
///     opened on first enabled breakpoint, closed when the last is disabled.
///   - The engine system (<see cref="DataBreakpointSystem"/>, P2) evaluates predicates
///     and calls <see cref="OnHit"/> when a condition fires.
///   - <see cref="RequestStep"/> / <see cref="RequestContinue"/> advance / resume the clock.
/// </summary>
public interface IDataBreakpointManager
{
    // ---- Registry -------------------------------------------------------

    /// <summary>
    /// Registers a new breakpoint. If <paramref name="breakpoint"/>.Enabled is true,
    /// the snapshot gate is mounted (0 → 1 transition).
    /// </summary>
    /// <returns>The stable <see cref="BreakpointId"/> assigned to the breakpoint.</returns>
    BreakpointId Add(Breakpoint breakpoint);

    /// <summary>
    /// Convenience method: creates and registers a breakpoint from a predicate and optional
    /// parameters. Equivalent to calling <see cref="Add"/> with a fully constructed
    /// <see cref="Breakpoint"/> record.
    /// </summary>
    /// <param name="occurrenceThreshold">
    /// Number of hits required before the breakpoint pauses execution.
    /// Must be >= 1. Pass 1 (default) to pause on the very first hit.
    /// </param>
    BreakpointId AddBreakpoint(SearchPredicateDto condition, Entity? filter = null,
                               int occurrenceThreshold = 1, string displayName = "",
                               Guid? sourceElementId = null);

    /// <summary>
    /// Removes the breakpoint with the given <paramref name="id"/>.
    /// If the breakpoint was enabled, the gate reference count is decremented.
    /// No-op when <paramref name="id"/> is unknown.
    /// </summary>
    void Remove(BreakpointId id);

    /// <summary>
    /// Enables or disables the breakpoint, adjusting the gate reference count accordingly.
    /// No-op when <paramref name="id"/> is unknown or when the state is already as requested.
    /// </summary>
    void SetEnabled(BreakpointId id, bool enabled);

    /// <summary>
    /// Replaces the predicate condition for an existing breakpoint.
    /// No-op when <paramref name="id"/> is unknown.
    /// </summary>
    void UpdateCondition(BreakpointId id, SearchPredicateDto? condition);

    /// <summary>
    /// Marks or clears the watch flag on a breakpoint.
    /// No-op when <paramref name="id"/> is unknown.
    /// </summary>
    void MarkAsWatch(BreakpointId id, bool isWatch);

    /// <summary>Persists all watch-flagged breakpoints to <paramref name="path"/>.</summary>
    void SaveWatches(string path);

    /// <summary>
    /// Restores watch entries from <paramref name="path"/>. Attempts to recompile each
    /// condition; marks <see cref="Breakpoint.IsBroken"/> on schema mismatch.
    /// </summary>
    void LoadWatches(string path);

    /// <summary>
    /// Called when the hot-reload cycle completes. Drops all cached compiled delegates
    /// and recompiles from retained DTOs. Marks <see cref="Breakpoint.IsBroken"/> on failure.
    /// </summary>
    void OnHotReloadCompleted();

    /// <summary>
    /// Called when a hot-reload cycle begins. If currently paused, forces
    /// <see cref="RequestContinue"/>, flushes pending mutations, and notifies the user.
    /// </summary>
    void OnHotReloadBegin();

    // ---- Deferred mutation (P4 stub) ------------------------------------

    /// <summary>
    /// Stages a component mutation to be applied at the N+1 tick boundary.
    /// Full implementation in P4; throws <see cref="NotImplementedException"/> until then.
    /// </summary>
    void StageMutation(Entity entity, Type componentType, object componentValue);

    /// <summary>
    /// Ruling 14 — stage an edit together with the value the editor was SEEDED with, so the
    /// implementation can write only the bytes the designer actually changed.
    ///
    /// <para>
    /// 🔴 <b>Why the baseline is a parameter and not something the callee can find.</b> The staged
    /// value and the value in the world differ in TWO ways at drain time — the designer's edit and
    /// whatever the simulation changed during the paused tick — and nothing at the drain can tell
    /// those apart. Only the caller knows what the dialog opened on.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b>Default-implemented on purpose:</b> it forwards to the whole-component
    /// <see cref="StageMutation(Entity, Type, object)"/>, so an implementer that has no baseline —
    /// every test double, and any caller that genuinely replaces a component — keeps working
    /// unchanged and unsurgically.
    /// </para>
    /// </summary>
    void StageMutation(Entity entity, Type componentType, object componentValue, object? baseline)
        => StageMutation(entity, componentType, componentValue);


    // ---- Hit callback (called by DataBreakpointSystem) -----------------

    /// <summary>
    /// Called by <c>DataBreakpointSystem</c> when a compiled predicate or event scanner fires.
    /// Implements the triple-buffer rewind and pauses the simulation clock.
    /// </summary>
    void OnHit(Breakpoint bp, Entity entity);

    // ---- Step / Continue ------------------------------------------------

    /// <summary>
    /// Restores the live repository to the post-tick snapshot captured at the last hit,
    /// then requests a single-tick advance from the time controller.
    /// No-op when not paused.
    /// </summary>
    void RequestStep();

    /// <summary>
    /// Restores the live repository to the post-tick snapshot and resumes continuous
    /// time advancement.
    /// No-op when not paused.
    /// </summary>
    void RequestContinue();

    // ---- External-hit bridge (P7 stub) ----------------------------------

    /// <summary>
    /// Called by Slice 1 probe-driven subsystems (e.g. Blueprint debugger) when a
    /// non-predicate breakpoint fires externally.
    /// Full implementation in P7; no-op until then.
    /// </summary>
    void OnExternalHit(string tag, Entity entity);

    // ---- Events ---------------------------------------------------------

    /// <summary>Raised when a breakpoint fires and the simulation is paused.</summary>
    event Action<Breakpoint, Entity>? OnBreakpointHit;

    /// <summary>
    /// Raised whenever the pause state changes.
    /// Parameter is <c>true</c> when pausing, <c>false</c> when resuming.
    /// </summary>
    event Action<bool>? OnPauseStateChanged;

    // ---- Properties -----------------------------------------------------

    /// <summary>Whether the simulation is currently paused by this manager.</summary>
    bool IsPaused { get; }

    /// <summary>
    /// Returns the appropriate view for rendering: the pre-tick snapshot when paused,
    /// or the live repo when running. Gizmo systems use this to feed the correct view
    /// to <c>IEntityStatefulGizmo.UpdateAndDraw</c>.
    /// </summary>
    ISimulationView ActiveView { get; }

    /// <summary>
    /// The engine tick at which the current pause was engaged. 0 when not paused.
    /// Used by the temporal status banner.
    /// </summary>
    long PausedTick { get; }

    /// <summary>Number of pending deferred mutations queued since the last pause.</summary>
    int PendingMutationsCount { get; }

    /// <summary>Snapshot of all registered breakpoints in registration order.</summary>
    IReadOnlyList<Breakpoint> AllBreakpoints { get; }

    /// <summary>
    /// Returns true if there are any compiled component predicates or event scanners mounted.
    /// Used by <c>DataBreakpointSystem</c> for a fast early-out.
    /// </summary>
    bool HasMountedDelegates { get; }

    /// <summary>
    /// True when any structural, spatial, or lifecycle breakpoints are mounted.
    /// Used by DataBreakpointSystem for a secondary gate check.
    /// </summary>
    bool HasStatefulTrackers { get; }

    /// <summary>
    /// Called once per tick by DataBreakpointSystem after compiled-predicate evaluation.
    /// Evaluates all structural, spatial, and lifecycle trackers against the current repo state.
    /// </summary>
    void EvaluateStatefulBreakpoints(EntityRepository repo);

    /// <summary>
    /// Active compiled component predicates keyed by their owning breakpoint.
    /// </summary>
    IReadOnlyList<(Breakpoint Breakpoint, CompiledComponentPredicate Compiled)> MountedComponentPredicates { get; }

    /// <summary>
    /// Active compiled event scanners keyed by their owning breakpoint.
    /// </summary>
    IReadOnlyList<(Breakpoint Breakpoint, CompiledEventScanner Scanner)> MountedEventScanners { get; }
}
