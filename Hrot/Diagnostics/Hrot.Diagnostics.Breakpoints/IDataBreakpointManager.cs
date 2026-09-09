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

    /// <summary>
    /// ⭐⭐⭐ <b>Batch 84 — stage a write to a KNOWN BYTE RANGE of a component.</b>
    ///
    /// <para>📌 <b>Ruling 14</b> <i>(user)</i>: <i>"the command buffer might need a special 'change
    /// concrete variable in a concrete blackboard component' … it can not be full component overwrite
    /// only, but <b>chirurgical change</b>."</i></para>
    ///
    /// <para>⭐ <b>Why this exists alongside the baseline overload.</b> 📐 The 4-arg
    /// <see cref="StageMutation(Entity, Type, object, object?)"/> already produces field writes — it
    /// DIFFS a before/after pair of boxed components. ⛔ That shape is wrong for a variable edit: the
    /// editor knows the field's offset directly from the layout, and has no boxed
    /// <c>Blackboard1024</c> to diff. ⚠ Manufacturing a 1024-byte baseline just to diff back down to
    /// four bytes would also be a second answer to <i>"which bytes changed?"</i>.</para>
    ///
    /// <para>🔴🔴 <b>An out-of-range offset is MEMORY CORRUPTION, not a wrong value</b> *(📌 <c>Q32</c>
    /// §2.1)*. ⇒ ⛔ <b>implementations MUST bounds-check against the registered component size and
    /// throw</b> — ⛔ not <c>Debug.Assert</c>, not a silent clamp, not "does nothing".</para>
    ///
    /// <para>⛔⛔ <b>The default implementation THROWS, and must not forward.</b> ⚠ Forwarding to the
    /// whole-component path is precisely <c>R-65</c>'s clobber: <c>Blackboard1024</c> is ONE component
    /// shared by BTree, HSM and Blueprint at disjoint offsets, so a whole-component write carries
    /// other subsystems' bytes back a tick. ⭐ A manager that cannot do this must say so out loud.</para>
    /// </summary>
    /// <param name="byteOffset">
    /// ⭐ The offset <b>within the component</b>. ⚠ For blueprint working state that is
    /// <c>WorkingStateLayout.ComponentOffsetOf(field.OffsetBytes)</c> — the header is already included;
    /// ⛔ do not add it again here.
    /// </param>
    void StageFieldMutation(Entity entity, Type componentType, int byteOffset, ReadOnlySpan<byte> bytes)
        => throw new NotSupportedException(
            $"{GetType().Name} does not implement StageFieldMutation. A surgical field write cannot "
            + "fall back to a whole-component write: Blackboard1024 is shared by BTree, HSM and "
            + "Blueprint at disjoint offsets, so the fallback would clobber other subsystems' state.");

    /// <summary>
    /// ⭐⭐⭐ <b><c>MIN</c> — is the SIMULATION CLOCK halted?</b> 📌 <c>R-126</c>, the user's ruling:
    /// <i>"time is paused OR debugger hit a breakpoint — in both cases the simulation is stopped and we
    /// can write new values."</i> ⇒ ⭐ <b>there is ONE source of "paused", and it is the clock</b> —
    /// ⛔ not <see cref="IsPaused"/>, which answers the much narrower <i>"is the DEBUGGER holding a
    /// rewound tick?"</i>
    ///
    /// <para>⚠⚠ <b><c>AS-1b</c>: implementations MUST read the LIVE WORLD's <c>GlobalTime</c>
    /// singleton</b> — ⛔ <b>never</b> the time controller's <c>GetCurrentState()</c>, which hard-codes
    /// its delta to <c>0</c> and therefore answers <i>"halted"</i> for ever. 📌 Pinned by
    /// <c>ThePauseFlagOnTheClockIsFalseWhilePausedTests</c>.</para>
    ///
    /// <para>⛔⛔ <b>The default THROWS and must not answer <c>false</c>.</b> ⚠ A silent <c>false</c>
    /// here reads as <i>"the simulation is advancing"</i>, so every live edit would be refused with a
    /// message about pausing something the designer already paused — 📌 exactly the confusion
    /// <c>M-36</c> cost three handoffs. ⭐ A manager that cannot see a clock says so out loud.</para>
    /// </summary>
    bool IsClockHalted()
        => throw new NotSupportedException(
            $"{GetType().Name} does not implement IsClockHalted, so it cannot say whether the "
            + "simulation is advancing. Answering 'not halted' by default would refuse every live "
            + "edit and blame the designer for it.");

    // ⛔⛔⛔ W3 — `WriteFieldNow` IS GONE from this interface. 📄 DESIGN_Staged_Live_Write.md §6's W3
    //    row; 📌 R-130 ("yellow makes no sense if the value is directly written now").
    // ⭐ Every live edit now goes through StageFieldMutation, in every run state, and the kernel's
    //   PreFrame ResumeAndDrainSystem pulls it into the repository at the next advancing tick.
    // ⚠ IsClockHalted above SURVIVES deliberately: it answers "is the simulation advancing?", which is
    //   a truthful general question about the clock (R-126 names the clock as the one source of
    //   "paused"), and it is railed. ⛔ Losing its last caller does not make a predicate wrong —
    //   📌 CLAUDE.md: "unreferenced is not unintentional."

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
