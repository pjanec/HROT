namespace Hrot.Diagnostics.Breakpoints;

/// <summary>
/// Abstracts the engine's time control surface for diagnostic debuggers.
/// Provides soft-pause semantics (returns immediately; halts on next frame).
///
/// <para>⭐⭐⭐ <b><c>CE-350</c> (<c>2026-09-26</c>) — MOVED HERE FROM <c>Hrot.Blueprints.Core</c>, where it
/// lived for historical reasons only.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.26.</para>
///
/// <para>📐 It is named for the ENGINE and every consumer is a debugger, not a Blueprint:
/// <c>BlueprintDebugSession</c>, <c>DataBreakpointManager</c>, <c>PerspectiveWorkspaceRegistrar</c>,
/// <c>CgfClusterDebugTimeController</c> and — since <c>CE-349</c> — <c>AiTracerCoordinator</c>, i.e.
/// BTree, HSM, breakpoints and CGF. ⛔ Each of them used to drag a Blueprints dependency in to reach
/// a contract that has nothing to do with Blueprints.</para>
///
/// <para>⚠ <b>The tell that the move was overdue, not merely tidy:</b> its old file carried
/// <c>[Obsolete("Use IEngineDebugTimeController. IBlueprintTimeController will be removed after one
/// batch.")]</c> on a `IBlueprintTimeController` alias ⇒ 🔒 <b>the RENAME had already happened and the
/// MOVE never followed it</b>, and "after one batch" had long passed. That alias is deleted with this
/// move.</para>
///
/// <para>⭐ <b>Why HERE and not a new assembly.</b> 📐 Measured: <c>Hrot.Diagnostics.Breakpoints</c>
/// already references <c>Hrot.Blueprints.Core</c> (one-way — Blueprints.Core does NOT reference back,
/// so no cycle), already owns the other debugger contracts, and is ALREADY referenced by every
/// consumer — <c>Hrot.Blueprints.Editor</c>, <c>Hrot.Editor.AiShared</c> and <c>Hrot.CGF</c>.
/// ⇒ <b>zero new project references.</b></para>
/// </summary>
public interface IEngineDebugTimeController
{
    bool IsPausedByDebugger { get; }
    void RequestPause();
    void RequestResume();
    void RequestStepOneTick();
}
