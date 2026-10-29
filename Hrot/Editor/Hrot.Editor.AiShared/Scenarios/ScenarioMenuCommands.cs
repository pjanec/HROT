using System;
using System.Collections.Generic;
using Fdp.Core.Serialization.Migrations;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Browser;
using Hrot.Editor.AiShared.Documents;
using NodeEditor.Core.Action;

namespace Hrot.Editor.AiShared.Scenarios;

/// <summary>
/// ⭐⭐⭐ <b>Registers the scenario shell commands and surfaces them as DISTINCT <c>File</c> menu items on
/// EVERY host.</b> 📄 <b><c>docs/DESIGN_Cgf_Scenario_Session_Slice.md</c></b> §3 ④ and §3a *(the item
/// table)*; <c>AQ60</c> ruling <b>R2</b> — <i>distinct menu items, NO chameleons, no per-host default in
/// the menu</i>.
///
/// <para>⭐⭐ <b>Two structural changes from the pre-<c>CE-046</c> registrar, both required by R2.</b>
/// <list type="number">
///   <item>⭐ It binds to <see cref="IScenarioSession"/>, not to the editor-only <c>IEditorLogic</c>.
///     📐 That binding WAS the wall *(design §2: CGF registers only the engine-default <c>Settings</c>
///     because this registrar could not be called there)*.</item>
///   <item>⭐⭐ <b>It lives in <c>Hrot.Editor.AiShared</c>.</b> ⚠ <b>DEVIATION from design §3 ④</b>, which
///     says the registrar *"takes <c>IScenarioSession</c>"* without saying it moves — ⛔ but CGF cannot
///     reference <c>Hrot.Editor</c> *(the assembly wall the whole slice exists to cross)*, so taking the
///     interface is necessary and not sufficient. Folded into the design's as-built section.</item>
/// </list></para>
///
/// <para>⭐⭐⭐ <b>CE-1443 (U2a, AQ108 §2.15) — THE ONE FILE MENU, NO SUBMENUS.</b> <i>Load Scenario… · New Scenario… · Load Checkpoint… · Open Exercise Recording… · Save Checkpoint (live run only) · the editing group (editing only) · Close · Exit</i>
/// — the order, the hide rules and the separators are ONE table, <see cref="FileMenuLayout"/>, applied by <see cref="Register"/> whoever registers the other items. ⛔ SUPERSEDED: the <c>File/Live</c> · <c>File/Edit</c> · <c>File/Checkpoint</c>
/// submenus and <i>New Exercise</i>. ⭐ <i>Load Scenario…</i> goes into the cluster's CURRENT armed mode on every host (R-1043).</para>
///
/// <para>⭐⭐ <b>Ruling 49 — every item is registered on every host; SERVICEABILITY decides enablement,
/// never presence.</b> ⚠ A host that composes no asset picker *(CGF today)* gets the item DISABLED with
/// the reason in its label — the <c>VC-3</c> shape this file already used for
/// <c>Save Curated Scenarios to Git</c>. ⛔ Never a silently missing item, and never a live-looking
/// control that does nothing.</para>
///
/// <para>⛔⛔ <b>No <c>File/Save</c> item is registered here, and that is deliberate.</b> 📐 Measured
/// <c>2026-08-26</c>: <c>CgfEditorShellToolbar</c>'s shared slot table already emits <c>File/Save</c>
/// bound to <c>ShellSaveCommands.SaveId</c>, whose handler ALREADY branches to the scenario when
/// <c>isScenarioContext</c> says so. ⇒ design §3a's <c>File/Save</c> row is satisfied by SUPPLYING those
/// scenario seams on CGF, ⛔ not by registering a second item at the same path. ⭐ Ruling 58 — one
/// registration list; ruling R3 — the toolbar is untouched.</para>
/// </summary>
/// <remarks>
/// The registrar operates over <see cref="IScenarioSession"/> plus modal/dialog seams so all decision
/// logic is headless-testable. ImGui draw calls live in each host's composition root, not here.
/// </remarks>
public static class ScenarioMenuCommands
{
    // ── Command identifiers ────────────────────────────────────────────────────

    /// <summary>Id for the New Scenario command: <c>"scenario.new"</c> — the LOCAL empty world.</summary>
    public const string NewId = "scenario.new";

    /// <summary>Id for the Save Scenario command: <c>"scenario.save"</c>.</summary>
    public const string SaveId = "scenario.save";

    /// <summary>Id for the Save As Scenario command: <c>"scenario.saveAs"</c>.</summary>
    public const string SaveAsId = "scenario.saveAs";

    /// <summary>⭐ CE-1443 B2 — <i>Save Scenario 'x' as Recipe…</i>: the scenario saved under <c>recipes/scenarios/&lt;name&gt;</c> (the same Save As), which <i>New Scenario</i> lists.</summary>
    public const string SaveAsRecipeId = "scenario.saveAsRecipe";

    /// <summary>
    /// Id for the Load Scenario command: <c>"scenario.load"</c>. ⭐ The id is UNCHANGED so hotkeys, MCP identity and every existing rail keep working; ⭐ <c>CE-1443</c> (R-1043) its meaning is now
    /// <i>load into the cluster's armed mode</i> (it used to say <i>open for edit</i>).
    /// </summary>
    public const string LoadId = "scenario.load";

    /// <summary>⭐ <c>CE-1443</c> — the menu path of Load Scenario… (see <see cref="FileMenuLayout"/>: the ONE table of every File path and its rank).</summary>
    public const string LoadScenarioMenuPath = FileMenuLayout.LoadScenario;

    /// <summary>⭐ <c>CE-1443</c> — the menu path of New Scenario….</summary>
    public const string NewScenarioMenuPath = FileMenuLayout.NewScenario;

    /// <summary>⭐ CE-4098 (AQ108 D16/D19) — Id for the Open Recording command: <c>"scenario.openRecording"</c>.</summary>
    public const string OpenRecordingId = "scenario.openRecording";

    /// <summary>⭐ CE-1443 — the menu path of Open Exercise Recording… (a recording is not a scenario, and the verb names no mode — it always opens into AAR).</summary>
    public const string OpenRecordingMenuPath = FileMenuLayout.OpenExerciseRecording;

    /// <summary>
    /// Id for the Save Checkpoint command: <c>"scenario.takeCheckpoint"</c>. ⭐ The id is unchanged (hotkeys / MCP identity); ⭐ <c>CE-1443</c> the item is <i>Save Checkpoint</i>, drawn ONLY during a live run.
    /// </summary>
    public const string TakeCheckpointId = "scenario.takeCheckpoint";

    /// <summary>⭐ <c>CE-1443</c> — Id for the Load Checkpoint command: <c>"scenario.loadCheckpoint"</c> — a live run, paused at the checkpoint.</summary>
    public const string LoadCheckpointId = "scenario.loadCheckpoint";

    /// <summary>⭐ <c>CE-1443</c> — Id for the Close command: <c>"scenario.close"</c> — back to Idle through the one door (<see cref="IScenarioSession.Close"/>).</summary>
    public const string CloseId = "scenario.close";

    /// <summary>Id for the Migration History command: <c>"scenario.migrationHistory"</c>.</summary>
    public const string MigrationHistoryId = "scenario.migrationHistory";

    /// <summary>Id for the Save Curated Scenarios to Git command: <c>"scenario.updateCurated"</c>.</summary>
    public const string UpdateCuratedId = "scenario.updateCurated";

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers the scenario shell commands as FLAT <c>File</c> items (<see cref="FileMenuLayout"/>) and declares the whole File menu's layout.
    /// </summary>
    /// <param name="registerCommand">
    /// Registration delegate. In production <c>WindowManager.ShellCommands.Register</c>; in tests a
    /// recording lambda.
    /// </param>
    /// <param name="menu">The global menu registry where menu items are surfaced.</param>
    /// <param name="commands">
    /// The command set containing the registered shell commands, used by <see cref="MenuCommandAdapter"/>
    /// to look up descriptors.
    /// </param>
    /// <param name="session">The host-agnostic scenario session. Never <see langword="null"/>.</param>
    /// <param name="openPicker">
    /// Seam that opens the asset picker filtered to a kind and invokes the callback with the selection
    /// (or <see langword="null"/> on cancel).
    /// <para>⭐ <b>Nullable, and a null is HONEST rather than a stub</b>: a host that composes no picker
    /// *(CGF)* gets both load items registered but DISABLED, with the reason in the label. ⛔ Ruling 49 —
    /// absent capability, present-and-explained item.</para>
    /// </param>
    /// <param name="openSaveAsDialog">
    /// Seam that opens the scenario Save-As dialog and invokes the callback with the chosen name.
    /// Nullable for the same reason as <paramref name="openPicker"/>.
    /// </param>
    /// <param name="showMigrationHistory">
    /// Seam that surfaces the migration sidecar list. When null the command is still registered but its
    /// handler only reads the sidecars.
    /// </param>
    /// <param name="openRecordingPicker">⭐ <c>CE-4098</c> (AQ108 D16/D19) — opens the recording picker (<see cref="RecordingPickerLauncher.Open"/>),
    /// which lists the exercises the master recorded and opens the pick into AAR through <see cref="IScenarioSession.OpenRecording"/>.
    /// Null ⇒ the item is registered but DISABLED with the reason in its label (ruling 49: serviceability decides enablement, never presence).</param>
    /// <param name="closedReason">⭐⭐ <c>CE-1442</c> (U1, §11.7 ②) — the run gate's reason while it is CLOSED (<c>RunModeChoice.ClosedReason</c>), <see langword="null"/> when open. Composed into every command's <c>IsEnabled</c> and <c>DynamicDisplayName</c> here, in the ONE registration helper —
    /// so the File menu AND the Start surface (which draws the same menu nodes) grey together, with the reason in the label. ⛔ Exempt: <i>New Exercise</i> (it LEAVES, never gated). Null ⇒ no gate (a rail / a host with no run choice).
    /// ⭐ A production caller that HAS a run choice must pass it (the silent-default rule).</param>
    /// <param name="takeCheckpoint">⭐ <c>CE-1443</c> — what <i>Save Checkpoint</i> does: the host's run choice (<c>RunModeChoice.TakeCheckpoint</c>: the session's checkpoint + the toast). Null ⇒ the session's own. ⭐ A production caller that HAS a run choice must pass it.</param>
    /// <param name="checkpointDisabledReason">⭐ <c>CE-1443</c> — why a checkpoint cannot be taken now (<c>RunModeChoice.CheckpointDisabledReason</c>), <see langword="null"/> when it can (and for a host with no choice).</param>
    /// <param name="openCheckpointPicker">⭐ <c>CE-1443</c> — opens the host's list of the run's checkpoints and calls back with the chosen LABEL (the item then calls <see cref="IScenarioSession.RestoreCheckpoint"/>). Null ⇒ the item is registered but DISABLED with the reason (ruling 49) — ⚠ no host supplies one yet (the live run's marks are not on the cluster view).</param>
    /// <param name="documentToSave">⭐ CE-1443 part B — a document (not the scenario) is the focused save target: <c>File/Save</c> and <c>File/Save As…</c> are drawn only then (<see cref="FileMenuLayout.Apply"/>). A host that registers the shell saves must pass it.</param>
    /// <param name="isCuratedSaveEnabled">Whether the curated-scenarios git copy can run here.</param>
    /// <param name="saveCuratedToGit">Performs the curated-scenarios git copy.</param>
    public static void Register(
        Action<EditorCommandDescriptor, Action<EditorCommandContext>> registerCommand,
        GlobalMenuRegistry                                        menu,
        IEditorCommands                                           commands,
        IScenarioSession                                          session,
        Action<AssetKindFilter, Action<IEditableAsset?>>?          openPicker,
        Action<Action<string>>?                                    openSaveAsDialog,
        Action<IReadOnlyList<SidecarFileInfo>>?                    showMigrationHistory = null,
        Func<bool>?                                               isCuratedSaveEnabled = null,
        Action?                                                   saveCuratedToGit     = null,
        Action?                                                   openRecordingPicker  = null,
        Func<string?>?                                            closedReason         = null,
        Action?                                                   takeCheckpoint       = null,
        Func<string?>?                                            checkpointDisabledReason = null,
        Action<Action<string>>?                                    openCheckpointPicker = null,
        Func<bool>?                                               documentToSave       = null)
    {
        if (registerCommand is null) throw new ArgumentNullException(nameof(registerCommand));
        if (menu            is null) throw new ArgumentNullException(nameof(menu));
        if (commands        is null) throw new ArgumentNullException(nameof(commands));
        if (session         is null) throw new ArgumentNullException(nameof(session));

        // ⭐⭐⭐ CE-1443 (AQ108 §2.15a) — the ONE layout of the File menu (rank, hide rule, separators), declared here and applied to the registry whoever registers an item and whenever (the asset items, Save As / Save All and Exit are
        //   other registrars'). ⛔ No submenus: File/Live, File/Edit, File/Checkpoint and New Exercise are gone (user, 2026-10-29: "Nothing like 'New exercise' makes clear sense to the user.").
        FileMenuLayout.Apply(menu, () => session.CurrentClusterState, documentToSave);

        // ══ the first four — what Start offers ══════════════════════════════════

        // ⭐⭐⭐ LOAD SCENARIO — R-1043: it loads into the cluster's CURRENT armed mode, on every host (Live armed ⇒ live, paused at the start · Preview armed ⇒ editing · AAR armed ⇒ the last load's mode). The session's LoadScenario IS the guarded
        //   door's Load (a run → Stop & Load, unsaved edits → Save / Discard / Cancel, AQ108 §3) resolved by the run choice — ⛔ no host seam, no per-host default, and no armed mode is SENT with the load.
        RegisterCommand(
            registerCommand, menu, commands, closedReason,
            LoadId, "Load Scenario…", LoadScenarioMenuPath,
            "Load a scenario into the armed mode (cluster-wide): Live armed starts a run paused at its start, Preview armed opens it for editing",
            isEnabled: () => openPicker != null,
            handler: _ => PickScenario(openPicker, session.LoadScenario),
            dynamicDisplayName: Unavailable("Load Scenario…", openPicker != null, NoPickerReason),
            allowedInIdle: true);   // ⭐ CE-4096 (AQ108 D16)

        RegisterCommand(
            registerCommand, menu, commands, closedReason,
            NewId, "New Scenario…", NewScenarioMenuPath,
            "Create an empty scenario to author",
            isEnabled: () => true,
            handler: _ => session.ClearWorld(),
            allowedInIdle: true);   // ⭐ CE-4096 (AQ108 D16) — ⚠ until CE-4089 it is today's local blank scenario, not yet "from a recipe"

        // ⭐ LOAD CHECKPOINT — a live run, paused at the checkpoint: the existing restore (the session's RestoreCheckpoint = RestoreCheckpointIntent). ⚠ The host supplies the picker of the labels it can list; none does yet (the live run's
        //   marks are not on the cluster view — only a replay session carries them), so the item says so rather than doing nothing (ruling 49).
        RegisterCommand(
            registerCommand, menu, commands, closedReason,
            LoadCheckpointId, "Load Checkpoint…", FileMenuLayout.LoadCheckpoint,
            "Restore a checkpoint of the running exercise: live, paused at the checkpointed state",
            isEnabled: () => openCheckpointPicker != null,
            handler: _ => openCheckpointPicker?.Invoke(session.RestoreCheckpoint),
            dynamicDisplayName: Unavailable("Load Checkpoint…", openCheckpointPicker != null, NoCheckpointListReason),
            allowedInIdle: true);

        // ⭐⭐ CE-4098 (AQ108 D16 "Open recording (AAR)", D19) — lists the master's exercises (the picker's Recent first) and opens the pick through the session's ONE replay intent.
        RegisterCommand(
            registerCommand, menu, commands, closedReason,
            OpenRecordingId, "Open Exercise Recording…", OpenRecordingMenuPath,
            "Open a recording the master offers into AAR (cluster-wide)",
            isEnabled: () => openRecordingPicker != null,
            handler: _ => openRecordingPicker?.Invoke(),
            dynamicDisplayName: Unavailable("Open Exercise Recording…", openRecordingPicker != null, NoRecordingsReason),
            allowedInIdle: true);

        // ══ during a live run ═══════════════════════════════════════════════════

        // ⭐⭐ SAVE CHECKPOINT — drawn ONLY in a live run (FileMenuLayout). The host's run choice takes it (the session's TakeCheckpoint + the toast, and ITS reason when it cannot — an unrecorded run); a host with no choice takes the session's.
        RegisterCommand(
            registerCommand, menu, commands, closedReason,
            TakeCheckpointId, "Save Checkpoint", FileMenuLayout.SaveCheckpoint,
            "Save the live running state as a checkpoint",
            isEnabled: () => checkpointDisabledReason?.Invoke() is null,
            handler: _ => (takeCheckpoint ?? session.TakeCheckpoint).Invoke(),
            dynamicDisplayName: () => checkpointDisabledReason?.Invoke() is { } why ? $"Save Checkpoint (unavailable — {why})" : "Save Checkpoint");

        // ══ the editing group — drawn ONLY while editing (FileMenuLayout); the names are unchanged in this part ═══════════════

        // ⭐ Save falls back to Save-As when nothing is loaded — unchanged behaviour. ⚠ When the host has
        //   no Save-As dialog the fallback cannot run, so the item says so rather than doing nothing.
        RegisterCommand(
            registerCommand, menu, commands, closedReason,
            SaveId, "Save Scenario", FileMenuLayout.SaveScenario,
            "Save the currently loaded scenario",
            isEnabled: () => !string.IsNullOrEmpty(session.LoadedScenarioName) || openSaveAsDialog != null,
            handler: _ =>
            {
                if (string.IsNullOrEmpty(session.LoadedScenarioName))
                    openSaveAsDialog?.Invoke(session.SaveAs);
                else
                    session.SaveCurrent();
            },
            // ⭐ CE-1443 part B (AQ108 §2.15, user: "the save menu item should never be just plain save") — the label names what it saves
            dynamicDisplayName: () => SaveLabels.Named("Save", "Scenario", session.LoadedScenarioName));

        RegisterCommand(
            registerCommand, menu, commands, closedReason,
            SaveAsId, "Save Scenario As", FileMenuLayout.SaveScenarioAs,
            "Save the scenario under a new name",
            isEnabled: () => openSaveAsDialog != null,
            handler: _ => openSaveAsDialog?.Invoke(session.SaveAs),
            dynamicDisplayName: openSaveAsDialog != null
                ? () => string.IsNullOrWhiteSpace(session.LoadedScenarioName) ? "Save Scenario As…" : SaveLabels.Named("Save", "Scenario", session.LoadedScenarioName) + " As…"
                : Unavailable("Save Scenario As", false, NoSaveAsReason));

        // ⭐ CE-1443 B2 (AQ108 §2.15) — the scenario as a RECIPE: the same Save As, under the recipe name (ContentLayout.ScenarioRecipeName — recipes/scenarios/<name>, which New Scenario re-reads on every open); terrain and entities kept.
        RegisterCommand(
            registerCommand, menu, commands, closedReason,
            SaveAsRecipeId, "Save Scenario as Recipe…", FileMenuLayout.SaveScenarioAsRecipe,
            "Save the scenario as a recipe New Scenario offers",
            isEnabled: () => openSaveAsDialog != null,
            handler: _ => openSaveAsDialog?.Invoke(name => session.SaveAs(Fdp.Toolkit.Orchestration.ContentLayout.ScenarioRecipeName(name))),
            dynamicDisplayName: openSaveAsDialog != null
                ? () => string.IsNullOrWhiteSpace(session.LoadedScenarioName) ? "Save Scenario as Recipe…" : SaveLabels.Named("Save", "Scenario", session.LoadedScenarioName) + " as Recipe…"
                : Unavailable("Save Scenario as Recipe…", false, NoSaveAsReason));

        RegisterCommand(
            registerCommand, menu, commands, closedReason,
            MigrationHistoryId, "Migration History", FileMenuLayout.MigrationHistory,
            "Show migration sidecars for the loaded scenario",
            isEnabled: () => true,
            handler: _ => showMigrationHistory?.Invoke(session.GetMigrationSidecars()));

        // ── scenario.updateCurated ─────────────────────────────────────────────
        // Copies the curated test scenarios' working copies back into the git-committed set. Enabled only
        // when running from a checkout (a source tree exists); disabled-with-reason otherwise, exactly as
        // the layout feature's "Save current as default" is. Always registered so the absence is
        // explainable rather than the item silently missing.
        RegisterCommand(
            registerCommand, menu, commands, closedReason,
            UpdateCuratedId, "Save Curated Scenarios to Git",
            FileMenuLayout.SaveCuratedToGit,
            "Copy the curated test scenarios' working copies back into the git-committed set (only when running from a source checkout)",
            isEnabled: isCuratedSaveEnabled ?? (() => false),
            handler: _ => saveCuratedToGit?.Invoke(),
            // ⭐⭐⭐ VC-3 — SAY WHY when it cannot run, word for word in the shape the layout feature's
            //    "Save current as default" already uses. 📌 The user's 2026-08-17 ruling: an explanatory
            //    label beats a control that looks broken.
            dynamicDisplayName: () => (isCuratedSaveEnabled ?? (() => false))()
                ? "Save Curated Scenarios to Git"
                : "Save Curated Scenarios to Git (unavailable — not running from the source tree)");

        // ══ leaving ═════════════════════════════════════════════════════════════

        // ⭐⭐⭐ CLOSE — back to Idle (Start), through the ONE door (IScenarioSession.Close = Door.Leave(LeaveReason.Close)): a run is stopped, unsaved edits ask first, a cluster in its error state asks only "discard and close?".
        //   ⛔ NEVER gated (null closedReason): it is how an operator leaves a cluster that is loading or in error. Offered on Start (a no-op at a cluster at rest). Replaces New Exercise.
        RegisterCommand(
            registerCommand, menu, commands, null,
            CloseId, "Close", FileMenuLayout.Close,
            "Close the scenario or recording and go back to the start screen (cluster-wide)",
            isEnabled: () => true,
            handler: _ => session.Close(),
            allowedInIdle: true);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private const string NoPickerReason = "no scenario picker on this host";
    private const string NoSaveAsReason = "no Save-As dialog on this host";
    private const string NoRecordingsReason = "no recording inventory on this host";
    private const string NoCheckpointListReason = "this host cannot list the checkpoints of the run";

    /// <summary>
    /// ⭐ VC-3 label helper: when the capability is present the plain name is used *(a null dynamic label,
    /// so nothing changes for a serviceable item)*; when it is absent the reason is appended.
    /// </summary>
    private static Func<string>? Unavailable(string displayName, bool serviceable, string reason)
        => serviceable ? null : () => $"{displayName} (unavailable — {reason})";

    private static void PickScenario(
        Action<AssetKindFilter, Action<IEditableAsset?>>? openPicker,
        Action<string>                                   load)
    {
        if (openPicker == null) return;
        openPicker(AssetKindFilter.Scenario, picked =>
        {
            if (picked != null) load(picked.Name);
        });
    }

    private static void RegisterCommand(
        Action<EditorCommandDescriptor, Action<EditorCommandContext>> registerCommand,
        GlobalMenuRegistry menu,
        IEditorCommands commands,
        Func<string?>? closedReason,
        string commandId,
        string displayName,
        string menuPath,
        string description,
        Func<bool> isEnabled,
        Action<EditorCommandContext> handler,
        Func<string>? dynamicDisplayName = null,
        bool allowedInIdle = false)
    {
        // ⭐⭐ CE-1442 — the ONE place the run gate becomes IsEnabled + a label saying why (EditorCommandDescriptor's own mechanism, VC-3): no per-control check
        if (closedReason is not null)
        {
            var enabledInner = isEnabled;
            var nameInner = dynamicDisplayName;
            isEnabled = () => closedReason() is null && enabledInner();
            dynamicDisplayName = () => closedReason() is { } why ? $"{displayName} ({why})" : nameInner?.Invoke() ?? displayName;
        }
        var descriptor = new EditorCommandDescriptor(
            Id:          commandId,
            DisplayName: displayName,
            Category:    "Scenario",
            Description: description,
            IconKey:     null,
            DefaultKey:  null,
            IsEnabled:   isEnabled,
            // ⭐⭐ VC-3 — a DISABLED item must say WHY. MenuCommandAdapter.ApplyLeafNode copies this
            //    onto the menu node's DynamicLabel; leaving it null is what made "Save Curated
            //    Scenarios to Git" read as MISSING rather than as unavailable.
            DynamicDisplayName: dynamicDisplayName);

        registerCommand(descriptor, handler);

        MenuCommandAdapter.Register(menu, commands, commandId, menuPath, allowedInIdle: allowedInIdle);
    }
}
