using System;
using System.Collections.Generic;
using Fdp.Core.Serialization.Migrations;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Browser;
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
/// <para>⭐⭐⭐ <b>THE SUBMENU STRUCTURE — <c>Live/</c> · <c>Edit/</c> · <c>Checkpoint/</c>.</b> The old
/// flat <c>File/Scenario/*</c> group is what made <c>Load Scenario…</c> a chameleon: it was labelled
/// generically and meant *load for AUTHORING*. ⇒ every item now names its mode, and the two load modes
/// are two items rather than one item with a hidden default. ⭐ E1 establishes the structure; the later
/// asset items *(E2)* and <c>Restore Checkpoint</c> *(Feature X)* slot in with zero menu code.</para>
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

    /// <summary>
    /// Id for the New Exercise command: <c>"scenario.newExercise"</c> — the CLUSTER-WIDE reset.
    /// ⚠ Distinct from <see cref="NewId"/>: that one wipes locally, this one finishes a running exercise
    /// everywhere. ⭐ Two ids because they are two actions *(R2)*.
    /// </summary>
    public const string NewExerciseId = "scenario.newExercise";

    /// <summary>Id for the Save Scenario command: <c>"scenario.save"</c>.</summary>
    public const string SaveId = "scenario.save";

    /// <summary>Id for the Save As Scenario command: <c>"scenario.saveAs"</c>.</summary>
    public const string SaveAsId = "scenario.saveAs";

    /// <summary>
    /// Id for the Open-for-EDIT command: <c>"scenario.load"</c>. ⭐ The id is UNCHANGED so hotkeys, MCP
    /// identity and every existing rail keep working; only its label and menu path now say *edit*.
    /// </summary>
    public const string LoadId = "scenario.load";

    /// <summary>Id for the load-for-LIVE command: <c>"scenario.loadLive"</c>.</summary>
    public const string LoadLiveId = "scenario.loadLive";

    /// <summary>Id for the Take Checkpoint command: <c>"scenario.takeCheckpoint"</c>.</summary>
    public const string TakeCheckpointId = "scenario.takeCheckpoint";

    /// <summary>Id for the Migration History command: <c>"scenario.migrationHistory"</c>.</summary>
    public const string MigrationHistoryId = "scenario.migrationHistory";

    /// <summary>Id for the Save Curated Scenarios to Git command: <c>"scenario.updateCurated"</c>.</summary>
    public const string UpdateCuratedId = "scenario.updateCurated";

    // ── Menu paths (design §3a) ────────────────────────────────────────────────

    /// <summary>Submenu for actions on the LIVE running exercise.</summary>
    public const string LiveMenuPrefix = "File/Live";

    /// <summary>Submenu for AUTHORING actions on a scenario opened for edit.</summary>
    public const string EditMenuPrefix = "File/Edit";

    /// <summary>Submenu for checkpoint actions on the live state.</summary>
    public const string CheckpointMenuPrefix = "File/Checkpoint";

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers the scenario shell commands and surfaces them under
    /// <see cref="LiveMenuPrefix"/> / <see cref="EditMenuPrefix"/> / <see cref="CheckpointMenuPrefix"/>.
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
    /// <param name="confirmNewExercise">
    /// ⭐⭐⭐ Seam that CONFIRMS the destructive cluster-wide reset and then runs the continuation.
    /// <para>🔒 <b>Ruling 53</b> — <i>a hard reset is a confirmed cluster-wide operation and the confirm
    /// belongs where the OPERATOR sits</i>. ⭐ An interactive host passes a modal; a headless-first host
    /// passes a log-and-proceed *(<c>UX_Feature_Modal_Surfaces.md</c> §2.0b: "the origin still LOGS what
    /// it skipped … the origin-side log IS THE WHOLE SAFETY NET")*. ⛔ <b>Null runs the reset with no
    /// prompt and no log</b> — legal only in a rail; a production caller must pass one.</para>
    /// </param>
    /// <param name="showMigrationHistory">
    /// Seam that surfaces the migration sidecar list. When null the command is still registered but its
    /// handler only reads the sidecars.
    /// </param>
    /// <param name="isCuratedSaveEnabled">Whether the curated-scenarios git copy can run here.</param>
    /// <param name="saveCuratedToGit">Performs the curated-scenarios git copy.</param>
    public static void Register(
        Action<EditorCommandDescriptor, Action<EditorCommandContext>> registerCommand,
        GlobalMenuRegistry                                        menu,
        IEditorCommands                                           commands,
        IScenarioSession                                          session,
        Action<AssetKindFilter, Action<IEditableAsset?>>?          openPicker,
        Action<Action<string>>?                                    openSaveAsDialog,
        Action<Action>?                                            confirmNewExercise   = null,
        Action<IReadOnlyList<SidecarFileInfo>>?                    showMigrationHistory = null,
        Func<bool>?                                               isCuratedSaveEnabled = null,
        Action?                                                   saveCuratedToGit     = null)
    {
        if (registerCommand is null) throw new ArgumentNullException(nameof(registerCommand));
        if (menu            is null) throw new ArgumentNullException(nameof(menu));
        if (commands        is null) throw new ArgumentNullException(nameof(commands));
        if (session         is null) throw new ArgumentNullException(nameof(session));

        // ══ File/Live — actions on the running exercise ═════════════════════════

        // ⭐⭐⭐ NEW EXERCISE — the cluster-wide reset, behind a confirmation. ⛔ NOT "New Scenario":
        //    that one is a local wipe for authoring and is registered under File/Edit below. Two ids,
        //    two labels, two menu homes — R2.
        RegisterCommand(
            registerCommand, menu, commands,
            NewExerciseId, "New Exercise", $"{LiveMenuPrefix}/New Exercise",
            "Finish the running exercise and start fresh (cluster-wide)",
            isEnabled: () => true,
            handler: _ =>
            {
                if (confirmNewExercise != null) confirmNewExercise(session.NewExercise);
                else                            session.NewExercise();
            });

        // ⭐⭐ LOAD FOR LIVE — /scenario/load/live. A fresh exercise run.
        RegisterCommand(
            registerCommand, menu, commands,
            LoadLiveId, "Load Scenario", $"{LiveMenuPrefix}/Load Scenario",
            "Load a scenario and run it live (cluster-wide)",
            isEnabled: () => openPicker != null,
            handler: _ => PickScenario(openPicker, session.LoadForLive),
            dynamicDisplayName: Unavailable("Load Scenario", openPicker != null, NoPickerReason));

        // ══ File/Edit — authoring on a scenario opened for edit ═════════════════

        // ⭐ OPEN FOR EDIT — /scenario/load/edit. The id is unchanged; the LABEL stopped lying.
        RegisterCommand(
            registerCommand, menu, commands,
            LoadId, "Open Scenario", $"{EditMenuPrefix}/Open Scenario",
            "Open a scenario for editing (cluster-wide)",
            isEnabled: () => openPicker != null,
            handler: _ => PickScenario(openPicker, session.OpenForEdit),
            dynamicDisplayName: Unavailable("Open Scenario", openPicker != null, NoPickerReason));

        RegisterCommand(
            registerCommand, menu, commands,
            NewId, "New Scenario", $"{EditMenuPrefix}/New Scenario",
            "Create an empty scenario to author",
            isEnabled: () => true,
            handler: _ => session.ClearWorld());

        // ⭐ Save falls back to Save-As when nothing is loaded — unchanged behaviour. ⚠ When the host has
        //   no Save-As dialog the fallback cannot run, so the item says so rather than doing nothing.
        RegisterCommand(
            registerCommand, menu, commands,
            SaveId, "Save Scenario", $"{EditMenuPrefix}/Save Scenario",
            "Save the currently loaded scenario",
            isEnabled: () => !string.IsNullOrEmpty(session.LoadedScenarioName) || openSaveAsDialog != null,
            handler: _ =>
            {
                if (string.IsNullOrEmpty(session.LoadedScenarioName))
                    openSaveAsDialog?.Invoke(session.SaveAs);
                else
                    session.SaveCurrent();
            });

        RegisterCommand(
            registerCommand, menu, commands,
            SaveAsId, "Save Scenario As", $"{EditMenuPrefix}/Save Scenario As",
            "Save the scenario under a new name",
            isEnabled: () => openSaveAsDialog != null,
            handler: _ => openSaveAsDialog?.Invoke(session.SaveAs),
            dynamicDisplayName: Unavailable("Save Scenario As", openSaveAsDialog != null, NoSaveAsReason));

        RegisterCommand(
            registerCommand, menu, commands,
            MigrationHistoryId, "Migration History", $"{EditMenuPrefix}/Migration History",
            "Show migration sidecars for the loaded scenario",
            isEnabled: () => true,
            handler: _ => showMigrationHistory?.Invoke(session.GetMigrationSidecars()));

        // ══ File/Checkpoint — the live state, saved ═════════════════════════════

        // ⭐⭐ TAKE CHECKPOINT — the existing TakeCheckpointIntent, via the session (which owns the bus).
        //    ⛔ Restore Checkpoint is NOT here: the save exists cluster-wide, the restore does not
        //    (design §8, Feature X). Registering a restore item would be a control that cannot work.
        RegisterCommand(
            registerCommand, menu, commands,
            TakeCheckpointId, "Take Checkpoint", $"{CheckpointMenuPrefix}/Take Checkpoint",
            "Save the live running state as a checkpoint",
            isEnabled: () => true,
            handler: _ => session.TakeCheckpoint());

        // ── scenario.updateCurated ─────────────────────────────────────────────
        // Copies the curated test scenarios' working copies back into the git-committed set. Enabled only
        // when running from a checkout (a source tree exists); disabled-with-reason otherwise, exactly as
        // the layout feature's "Save current as default" is. Always registered so the absence is
        // explainable rather than the item silently missing.
        RegisterCommand(
            registerCommand, menu, commands,
            UpdateCuratedId, "Save Curated Scenarios to Git",
            $"{EditMenuPrefix}/Save Curated Scenarios to Git",
            "Copy the curated test scenarios' working copies back into the git-committed set (only when running from a source checkout)",
            isEnabled: isCuratedSaveEnabled ?? (() => false),
            handler: _ => saveCuratedToGit?.Invoke(),
            // ⭐⭐⭐ VC-3 — SAY WHY when it cannot run, word for word in the shape the layout feature's
            //    "Save current as default" already uses. 📌 The user's 2026-08-17 ruling: an explanatory
            //    label beats a control that looks broken. 📐 Measured: RenderGlobalMenu DOES draw a
            //    disabled leaf (greyed, :707) — so the item was never hidden; it simply said nothing,
            //    which is indistinguishable from "not implemented" to someone looking for it.
            dynamicDisplayName: () => (isCuratedSaveEnabled ?? (() => false))()
                ? "Save Curated Scenarios to Git"
                : "Save Curated Scenarios to Git (unavailable — not running from the source tree)");
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private const string NoPickerReason = "no scenario picker on this host";
    private const string NoSaveAsReason = "no Save-As dialog on this host";

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
        string commandId,
        string displayName,
        string menuPath,
        string description,
        Func<bool> isEnabled,
        Action<EditorCommandContext> handler,
        Func<string>? dynamicDisplayName = null)
    {
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

        MenuCommandAdapter.Register(menu, commands, commandId, menuPath);
    }
}
