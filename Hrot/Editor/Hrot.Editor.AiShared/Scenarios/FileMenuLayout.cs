using System;
using System.Collections.Generic;
using Fdp.Presentation.WindowManager;
using Fdp.Toolkit.Orchestration;

namespace Hrot.Editor.AiShared.Scenarios;

/// <summary>
/// ⭐⭐⭐ <c>CE-1443</c> (U2a, AQ108 §2.15 / §2.15a) — <b>THE File menu's layout: ONE table, declared once, applied to the registry whoever registers an item and whenever.</b>
/// 📄 <c>docs/blueprints/Architect_Question_108_Run_Mode_Picker.md</c> §2.15a (the classes and the sequence).
///
/// <para>⭐ The <c>File</c> menu is filled by four registrars at different times — <see cref="ScenarioMenuCommands"/>, the shell toolbar's asset items (<c>CgfEditorShellToolbar</c>), the editor's Save As / Save All, and the window controller's Exit — so the
/// registration order is not a layout. <see cref="Apply"/> declares each item's rank (<see cref="GlobalMenuRegistry.SetLayout"/>) and when it is DRAWN, once; the registry applies the rule whether the node exists yet or not. ⇒ every host that registers
/// <see cref="ScenarioMenuCommands"/> draws the same menu by construction, and no host can forget to hide an item.</para>
///
/// <para>⭐ The three groups: <b>the first four</b> (Load Scenario · New Scenario · Load Checkpoint · Open Exercise Recording — always drawn; on Start they are what the surface offers), <b>Save Checkpoint</b> (only during a live run),
/// <b>the editing group</b> (asset and save items — only while editing). ⛔ No submenus (user, <c>2026-10-29</c>).</para>
/// </summary>
public static class FileMenuLayout
{
    // ── the first four ────────────────────────────────────────────────────────
    /// <summary>Load Scenario… — goes into the cluster's armed mode (R-1043).</summary>
    public const string LoadScenario = "File/Load Scenario…";
    /// <summary>New Scenario… — authoring.</summary>
    public const string NewScenario = "File/New Scenario…";
    /// <summary>Load Checkpoint… — a live run, paused at the checkpoint.</summary>
    public const string LoadCheckpoint = "File/Load Checkpoint…";
    /// <summary>Open Exercise Recording… — review (AAR).</summary>
    public const string OpenExerciseRecording = "File/Open Exercise Recording…";

    // ── a live run ────────────────────────────────────────────────────────────
    /// <summary>Save Checkpoint — only during a live run.</summary>
    public const string SaveCheckpoint = "File/Save Checkpoint";

    // ── the editing group ──
    /// <summary>The asset items, registered by the shell toolbar.</summary>
    public const string OpenAsset = "File/Open Asset…", NewAsset = "File/New Asset…", NewBehavior = "File/New Behavior…", NewAction = "File/New Action…", NewCondition = "File/New Condition…";
    /// <summary>The shell's saves (<c>shell.save</c> / <c>shell.saveAs</c> / <c>shell.saveAll</c>). ⭐ CE-1443 part B — <c>Save</c> / <c>Save As…</c> are drawn only while a DOCUMENT is the save target (the scenario has its own two items), and their labels name it.</summary>
    public const string Save = "File/Save", SaveAs = "File/Save As…", SaveAll = "File/Save All";
    /// <summary>⭐ CE-1443 B2 — the recipe saves: the shell's (<c>shell.saveAsRecipe</c>, a document) and the scenario's (<c>scenario.saveAsRecipe</c>).</summary>
    public const string SaveAsRecipe = "File/Save as Recipe…", SaveScenarioAsRecipe = "File/Save Scenario as Recipe…";
    /// <summary>The scenario's own items (<c>scenario.save</c> / <c>scenario.saveAs</c> / migration history / the curated git copy).</summary>
    public const string SaveScenario = "File/Save Scenario", SaveScenarioAs = "File/Save Scenario As", MigrationHistory = "File/Migration History", SaveCuratedToGit = "File/Save Curated Scenarios to Git";

    // ── leaving ───────────────────────────────────────────────────────────────
    /// <summary>Close — back to Idle through the one door.</summary>
    public const string Close = "File/Close";
    /// <summary>Exit — registered by the window controller.</summary>
    public const string Exit = "File/Exit";

    /// <summary>The separator after the first four (always drawn).</summary>
    public const string SeparatorAfterLoads = "File/sep-after-loads";
    /// <summary>The separator before Close (drawn when anything above it in the context groups is).</summary>
    public const string SeparatorBeforeClose = "File/sep-before-close";

    /// <summary>The editing group's items, in order.</summary>
    public static readonly IReadOnlyList<string> EditingGroup = new[]
    {
        OpenAsset, NewAsset, NewBehavior, NewAction, NewCondition, Save, SaveScenario, SaveAs, SaveScenarioAs, SaveAsRecipe, SaveScenarioAsRecipe, SaveAll, MigrationHistory, SaveCuratedToGit,
    };

    /// <summary>⭐ EVERY File path of this menu in DRAW order, separators included — the table a rail reads (what the menu is MEANT to be, not what a host happened to register).</summary>
    public static readonly IReadOnlyList<string> InOrder = Build();

    private static string[] Build()
    {
        var l = new List<string> { LoadScenario, NewScenario, LoadCheckpoint, OpenExerciseRecording, SeparatorAfterLoads, SaveCheckpoint };
        l.AddRange(EditingGroup);
        l.Add(SeparatorBeforeClose); l.Add(Close); l.Add(Exit);
        return l.ToArray();
    }

    /// <summary>
    /// Declares every rank and every visibility on <paramref name="menu"/>. <paramref name="state"/> answers the cluster's state NOW (the session's <c>CurrentClusterState</c>): the editing group is drawn in <see cref="ClusterState.OperatingEdit"/>,
    /// Save Checkpoint in <see cref="ClusterState.OperatingLive"/>; everything else always. ⛔ Idempotent — a host composed twice (a rail) writes the same rules.
    /// </summary>
    /// <param name="documentToSave">⭐ CE-1443 part B (AQ108 §2.15 "Save names what it saves") — a DOCUMENT (BTree / HSM / Blueprint) is the focused save target. <c>Save</c> / <c>Save As…</c> are drawn only then, so the menu never shows a
    /// second item saving the scenario (that is <c>Save Scenario 'x'</c>'s). Null ⇒ no document saver on this host (the Orchestrator) ⇒ never drawn. ⭐ A host that registers the shell saves must pass it (the silent-default rule).</param>
    public static void Apply(GlobalMenuRegistry menu, Func<ClusterState> state, Func<bool>? documentToSave = null)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentNullException.ThrowIfNull(state);
        bool Editing() => state() == ClusterState.OperatingEdit;
        bool LiveRun() => state() == ClusterState.OperatingLive;

        int rank = 0;
        foreach (var path in InOrder)
        {
            rank += 10;
            Func<bool>? visible = path switch
            {
                SaveCheckpoint       => LiveRun,
                Save or SaveAs or SaveAsRecipe => () => Editing() && documentToSave?.Invoke() == true,
                SeparatorBeforeClose => () => Editing() || LiveRun(),
                _ when System.Linq.Enumerable.Contains(EditingGroup, path) => Editing,
                _                    => null,
            };
            menu.SetLayout(path, rank, visible);
        }
        // the separators are nodes of their own (no command): registered here so they sit in the same table
        menu.RegisterSeparator(SeparatorAfterLoads);
        menu.RegisterSeparator(SeparatorBeforeClose);
    }
}
