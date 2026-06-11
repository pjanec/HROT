using Hrot.Blueprints.Core.Debug;
using Hrot.Editor.AiShared.Debug;
using NodeEditor.Core.Action;

namespace Hrot.Blueprints.Editor.Debug;

/// <summary>
/// Registers the polymorphic "AI Debug" toolbar group into the shell command set,
/// keyed off <see cref="IDebugSessionRegistry.ActiveSession"/>.
///
/// <para>
/// <b>Common commands</b> (Continue, StepOver, StepInto, StepOut, Pause) work for any
/// <see cref="IAiDebugSession"/> — Blueprint, BTree, or HSM — and are always registered.
/// <b>Blueprint-only extras</b> (StepBack, node-position text) are present only when
/// <see cref="IDebugSessionRegistry.ActiveSession"/> is an <see cref="IBlueprintDebugSession"/>.
/// </para>
///
/// <para>
/// The <see cref="Register"/> method mirrors the delegate pattern from
/// <see cref="Hrot.Editor.AiShared.Documents.ShellSaveCommands"/> for testability:
/// production passes <c>windowManager.ShellCommands.Register</c>, tests pass a
/// recording lambda. The <see cref="BuildGroupModel"/> and <see cref="NodePositionText"/>
/// seams expose the same pure decision logic headlessly so the toolbar render path
/// can compose them without touching ImGui.
/// </para>
/// </summary>
public static class AiDebugCommands
{
    // ── Command identifiers ─────────────────────────────────────────────────────

    /// <summary>Id for the Continue command: <c>"debug.continue"</c>.</summary>
    public const string ContinueId = "debug.continue";

    /// <summary>Id for the Step Over command: <c>"debug.stepOver"</c>.</summary>
    public const string StepOverId = "debug.stepOver";

    /// <summary>Id for the Step Into command: <c>"debug.stepInto"</c>.</summary>
    public const string StepIntoId = "debug.stepInto";

    /// <summary>Id for the Step Out command: <c>"debug.stepOut"</c>.</summary>
    public const string StepOutId = "debug.stepOut";

    /// <summary>Id for the Pause command: <c>"debug.pause"</c>.</summary>
    public const string PauseId = "debug.pause";

    /// <summary>Id for the blueprint-only Step Back command: <c>"debug.stepBack"</c>.</summary>
    public const string StepBackId = "debug.stepBack";

    // ── Group model ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A single entry in the toolbar group model, produced by
    /// <see cref="BuildGroupModel"/> so the render path can compose the icon surface
    /// without touching the session or registry.
    /// </summary>
    /// <param name="Id">Command id (e.g. <c>"debug.continue"</c>).</param>
    /// <param name="DisplayName">User-visible label.</param>
    /// <param name="IconKey">Semantic icon key (e.g. <c>"debug/continue"</c>).</param>
    /// <param name="IsPresent">True when this command should appear in the toolbar
    /// group (common commands always true; StepBack true only for blueprint sessions).</param>
    /// <param name="IsEnabled">True when the command can be invoked.</param>
    public sealed record DebugCommandModel(
        string Id,
        string DisplayName,
        string IconKey,
        bool IsPresent,
        bool IsEnabled);

    // ── Public API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers all AI Debug commands.
    /// Called once at editor startup by the composition root.
    /// </summary>
    /// <param name="register">
    /// Registration delegate: receives an <see cref="EditorCommandDescriptor"/> and its
    /// handler <see cref="Action{EditorCommandContext}"/>.
    /// In production this is <c>WindowManager.ShellCommands.Register</c>; in tests a
    /// recording lambda.
    /// </param>
    /// <param name="registry">
    /// The live debug session registry; commands read <see cref="IDebugSessionRegistry.ActiveSession"/>
    /// dynamically on each <c>IsEnabled</c> check and on invocation.
    /// </param>
    public static void Register(
        Action<EditorCommandDescriptor, Action<EditorCommandContext>> register,
        IDebugSessionRegistry registry)
    {
        if (register is null) throw new ArgumentNullException(nameof(register));
        if (registry is null) throw new ArgumentNullException(nameof(registry));

        // Helper: shorthand to read the current active session.
        IAiDebugSession? Active() => registry.ActiveSession;

        // ── debug.continue ───────────────────────────────────────────────────────
        register(
            new EditorCommandDescriptor(
                Id:          ContinueId,
                DisplayName: "Continue",
                Category:    "Debug",
                Description: "Continue execution from the current breakpoint",
                IconKey:     "debug/continue",
                DefaultKey:  null,
                IsEnabled:   () => Active() is { IsPaused: true }),
            _ => Active()?.Continue());

        // ── debug.stepOver ───────────────────────────────────────────────────────
        register(
            new EditorCommandDescriptor(
                Id:          StepOverId,
                DisplayName: "Step Over",
                Category:    "Debug",
                Description: "Step over the current node",
                IconKey:     "debug/step_over",
                DefaultKey:  null,
                IsEnabled:   () => Active() is { IsPaused: true }),
            _ => Active()?.StepOver());

        // ── debug.stepInto ───────────────────────────────────────────────────────
        register(
            new EditorCommandDescriptor(
                Id:          StepIntoId,
                DisplayName: "Step Into",
                Category:    "Debug",
                Description: "Step into the current node",
                IconKey:     "debug/step_into",
                DefaultKey:  null,
                IsEnabled:   () => Active() is { IsPaused: true }),
            _ => Active()?.StepInto());

        // ── debug.stepOut ────────────────────────────────────────────────────────
        register(
            new EditorCommandDescriptor(
                Id:          StepOutId,
                DisplayName: "Step Out",
                Category:    "Debug",
                Description: "Step out of the current node",
                IconKey:     "debug/step_out",
                DefaultKey:  null,
                IsEnabled:   () => Active() is { IsPaused: true }),
            _ => Active()?.StepOut());

        // ── debug.pause ──────────────────────────────────────────────────────────
        // DESIGN-NOTE: There is no "debug/pause" key in the icon atlas (§5.1).
        // We reuse "debug/continue" for the Pause icon as a temporary measure;
        // a dedicated debug/pause icon should be added to the atlas in a future
        // icon-set update.  See BATCH-09-REPORT.md "Design Decisions".
        register(
            new EditorCommandDescriptor(
                Id:          PauseId,
                DisplayName: "Pause",
                Category:    "Debug",
                Description: "Pause execution",
                IconKey:     "debug/continue",
                DefaultKey:  null,
                IsEnabled:   () => Active() is { IsAttached: true, IsPaused: false }),
            _ => Active()?.Pause());

        // ── debug.stepBack (blueprint-only) ──────────────────────────────────────
        register(
            new EditorCommandDescriptor(
                Id:          StepBackId,
                DisplayName: "Step Back",
                Category:    "Debug",
                Description: "Step back to the previous recorded node (blueprint only)",
                IconKey:     "debug/step_back",
                DefaultKey:  null,
                IsEnabled:   () => Active() is IBlueprintDebugSession bp && bp.CurrentNodePointer > 0),
            _ =>
            {
                if (Active() is IBlueprintDebugSession bp)
                    bp.StepBack();
            });
    }

    // ── Headless seams ──────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the toolbar group model for the current session state.
    /// Common commands are always present; StepBack is present only when
    /// <see cref="IDebugSessionRegistry.ActiveSession"/> is an
    /// <see cref="IBlueprintDebugSession"/>.
    /// </summary>
    /// <param name="registry">The debug session registry.</param>
    /// <returns>A read-only list of command models for the toolbar group.</returns>
    public static IReadOnlyList<DebugCommandModel> BuildGroupModel(IDebugSessionRegistry registry)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));

        var session = registry.ActiveSession;
        var items = new List<DebugCommandModel>(7);

        bool isPaused = session is { IsPaused: true };
        bool isAttachedAndRunning = session is { IsAttached: true, IsPaused: false };

        // Common commands — always present.
        items.Add(new DebugCommandModel(ContinueId, "Continue", "debug/continue", IsPresent: true, isPaused));
        items.Add(new DebugCommandModel(StepOverId, "Step Over", "debug/step_over", IsPresent: true, isPaused));
        items.Add(new DebugCommandModel(StepIntoId, "Step Into", "debug/step_into", IsPresent: true, isPaused));
        items.Add(new DebugCommandModel(StepOutId,  "Step Out",  "debug/step_out",  IsPresent: true, isPaused));
        items.Add(new DebugCommandModel(PauseId,    "Pause",     "debug/continue", IsPresent: true, isAttachedAndRunning));

        // Blueprint-only extras — present only for IBlueprintDebugSession.
        if (session is IBlueprintDebugSession bp)
        {
            items.Add(new DebugCommandModel(
                StepBackId,
                "Step Back",
                "debug/step_back",
                IsPresent:  true,
                IsEnabled:  bp.CurrentNodePointer > 0));
        }

        return items;
    }

    /// <summary>
    /// Returns the node-position indicator text for the current session.
    /// Delegates to <see cref="DebugStepControls.FormatNodePosition"/> when
    /// the active session is an <see cref="IBlueprintDebugSession"/>;
    /// returns <see cref="string.Empty"/> for non-blueprint sessions.
    /// </summary>
    /// <param name="registry">The debug session registry.</param>
    /// <returns>
    /// A human-readable position string (e.g. <c>"node 3 / 12"</c>), or
    /// <see cref="string.Empty"/> when the session is not a blueprint session.
    /// </returns>
    public static string NodePositionText(IDebugSessionRegistry registry)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));

        if (registry.ActiveSession is IBlueprintDebugSession bp)
            return DebugStepControls.FormatNodePosition(bp);

        return string.Empty;
    }
}
