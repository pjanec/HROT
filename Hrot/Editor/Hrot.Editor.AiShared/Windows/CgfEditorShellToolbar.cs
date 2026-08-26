using System;
using System.Collections.Generic;
using Fdp.Presentation.WindowManager;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-016</c> §7 (A2) — THE ONE main-toolbar registration list, shared by every host.</b>
/// 📄 <c>docs/DESIGN_Cgf_Shell_Command_Toolbar_Slice.md</c> §3 ①.
///
/// <para>🔒 <b>UXI-35 / ruling 58, verbatim:</b> <i>"One registration list… No per-host menu file, no
/// <c>if (host==…)</c>."</i> ⇒ ⛔ the editor's toolbar layout was inline in <c>EditorSubsystem</c>, which
/// made it the sole writer of the shell registries — <b>seam-law instance 30</b>. ⭐ It lives here now, and
/// both hosts call it.</para>
///
/// <para>⭐⭐⭐ <b>THE SUBSET IS DERIVED, NOT DECLARED — and that is the whole mechanism.</b> A toolbar entry
/// is emitted <b>only for a command the host's shell can actually service</b>
/// *(<c>IEditorCommands.Get(id) != null</c>)*. ⇒ ⛔ there is no CGF list and no editor list: the editor
/// registers more commands, so it gets more buttons, from <b>this same table</b>.
/// 📌 That is also ruling <b>49</b> discharged by construction — a command a host cannot service is
/// <b>ABSENT, not greyed</b>, because nothing registers an entry for it.</para>
///
/// <para>⚠⚠ <b>A separator is emitted only when the group after it emitted something.</b> ⛔ Otherwise a
/// host that omits a group keeps a rule floating against nothing — 📌 exactly the dangling
/// <c>ToolbarSep_TimeToPersp</c> this slice deletes from CGF. ⭐ On the editor every group is present, so
/// every separator survives and the rendered list is unchanged.</para>
/// </summary>
public static class CgfEditorShellToolbar
{
    // ⭐⭐ THE LAYOUT — ids and sort orders lifted verbatim from EditorSubsystem's inline block, so the
    //    editor's rendered entry list is byte-identical after the extraction. ⛔ Do not renumber: the
    //    conformance rail compares CGF against the editor BY id AND sortOrder.
    // ⚠ The PERSPECTIVE group (sortOrder 20) is deliberately NOT here — it is a section, not a command,
    //   and each host constructs its own with its own switcher. The separator that PRECEDES the AI-debug
    //   group is here because it belongs to that group.

    /// <summary>Save — <c>shell.save</c>, registered by <see cref="Documents.ShellSaveCommands"/>.</summary>
    public const string SaveId = Documents.ShellSaveCommands.SaveId;

    /// <summary>Save All — <c>shell.saveAll</c>, same registrar.</summary>
    public const string SaveAllId = Documents.ShellSaveCommands.SaveAllId;

    /// <summary>Open Asset — the descriptor this helper registers when a handler is supplied.</summary>
    public const string OpenAssetId = "shell.openAsset";

    /// <summary>New Asset — the descriptor this helper registers when a handler is supplied.</summary>
    public const string NewAssetId = "shell.newAsset";

    /// <summary>Compile / hot-reload the active AI asset.</summary>
    public const string CompileReloadId = "blueprint.compileReload";

    /// <summary>Rebuild every AI behaviour asset. ⚠ Editor-only in practice — CGF supplies no handler.</summary>
    public const string FullRebuildId = "blueprint.fullRebuild";

    /// <summary>
    /// One toolbar slot. ⭐ <paramref name="Group"/> is what a SEPARATOR depends on: the rule is emitted
    /// only if that group emitted at least one command.
    /// ⚠ A separator may TRAIL its group *(<c>ToolbarSep_OpenAsset</c> after File)* or LEAD it
    /// *(<c>ToolbarSep_PerspToAiDebug</c> before AI-debug)* — either way it names the group whose presence
    /// justifies it, ⛔ never a group of its own.
    /// </summary>
    private sealed record Slot(string? CommandId, int SortOrder, string? SeparatorId = null, int Group = 0);

    // ⭐ One table. Groups exist only so a separator can ask "did anything after me appear?".
    private static readonly Slot[] Layout =
    {
        new(NewAssetId,      -11, Group: 1),
        new(OpenAssetId,     -10, Group: 1),
        new(SaveId,           -9, Group: 1),
        // ⛔⛔ NO SaveAll SLOT, and that is a DELIBERATE DEVIATION from the design's §3 subset sentence
        //    ("Save · SaveAll · Open Asset · New Asset · QuickReload"). 📐 Measured: the editor's toolbar
        //    has NO Save-All button — only `shell.save` at -9. ⇒ adding one here would emit it on the
        //    EDITOR too (its shell registers `shell.saveAll`), breaking item ②'s byte-identical gate and
        //    changing a UI nobody asked to change. ⭐ The gate wins; `SaveAllId` stays exposed below as a
        //    constant so a later slice can add it to BOTH hosts deliberately.
        // ⚠ Group 1, NOT a group of its own: this rule TRAILS the File group, so it lives or dies with
        //   it. 📌 The first cut gave it its own group and it vanished from the editor — caught by
        //   `The_editor_full_shell_yields_exactly_the_pre_extraction_layout` before anything shipped.
        new(null,              0, "ToolbarSep_OpenAsset", Group: 1),
        new(null,             30, "ToolbarSep_PerspToAiDebug", Group: 3),
        new(AiDebugCommandIds.Continue, 40, Group: 3),
        new(AiDebugCommandIds.StepOver, 41, Group: 3),
        new(AiDebugCommandIds.StepInto, 42, Group: 3),
        new(AiDebugCommandIds.StepOut,  43, Group: 3),
        new(AiDebugCommandIds.Pause,    44, Group: 3),
        new(AiDebugCommandIds.StepBack, 45, Group: 3),
        new(null,             49, "ToolbarSep_AiDebugToBuild", Group: 4),
        new(CompileReloadId,  50, Group: 4),
        new(FullRebuildId,    51, Group: 4),
    };

    /// <summary>
    /// ⭐⭐ The AI-debug command ids, MIRRORED because <c>Hrot.Blueprints.Editor.Debug.AiDebugCommands</c>
    /// sits <b>above</b> this assembly and cannot be referenced from it.
    ///
    /// <para>⚠⚠ <b>A mirror can drift, and here a drift is SILENT</b> — the derived-subset rule means an id
    /// nothing registers simply yields no button, so a typo removes a toolbar group without failing
    /// anything. 📌 <b>Measured during this slice:</b> the first draft guessed <c>"ai.debug.continue"</c>;
    /// the real id is <c>"debug.continue"</c>, and nothing but reading the source caught it.
    /// ⇒ ⭐⭐⭐ <c>TheToolbarLayoutIsOneListTests</c> asserts each of these equals its
    /// <c>AiDebugCommands</c> constant, from a project that can see both.</para>
    /// </summary>
    public static class AiDebugCommandIds
    {
        public const string Continue = "debug.continue";
        public const string StepOver = "debug.stepOver";
        public const string StepInto = "debug.stepInto";
        public const string StepOut  = "debug.stepOut";
        public const string Pause    = "debug.pause";
        public const string StepBack = "debug.stepBack";
    }

    /// <summary>
    /// ⭐ The handlers a host supplies for the two commands this helper OWNS the descriptors for.
    /// ⛔ Everything else *(save, AI-debug)* already has a shared registrar the host calls itself —
    /// duplicating those descriptors here would be a second definition of one command.
    /// </summary>
    /// <param name="OpenAsset">Opens the asset picker, or <see langword="null"/> to omit the command.</param>
    /// <param name="NewAsset">Opens the new-asset recipe picker, or <see langword="null"/> to omit.</param>
    /// <param name="CompileReload">Compile/hot-reload the active asset, or <see langword="null"/> to omit.</param>
    /// <param name="FullRebuild">Rebuild all AI assets, or <see langword="null"/> to omit.</param>
    /// <param name="CompileReloadEnabled">
    /// Live enablement for <see cref="CompileReloadId"/>. ⚠ A <see cref="Func{T}"/>, re-read per frame:
    /// it depends on the ACTIVE document's kind, which changes as tabs change.
    /// </param>
    public sealed record HostServices(
        Action? OpenAsset = null,
        Action? NewAsset = null,
        Action? CompileReload = null,
        Action? FullRebuild = null,
        Func<bool>? CompileReloadEnabled = null);

    /// <summary>
    /// Registers the common-core descriptors this helper owns, then emits a toolbar entry for every
    /// layout slot whose command the shell can service.
    /// </summary>
    /// <returns>The ids that actually got a toolbar entry, in sort order — for the host to log and a rail to assert.</returns>
    /// <param name="toolbar">
    /// ⚠⚠ <b>NULLABLE, and that preserves a measured behaviour.</b> 📐 The editor registered
    /// <c>shell.openAsset</c>/<c>shell.newAsset</c> OUTSIDE its <c>if (MainToolbar != null)</c> guard, so a
    /// bare <c>EditorSubsystem</c> *(window-registration unit tests)* still got the commands and their
    /// File-menu items. ⇒ ⛔ folding everything inside the guard would have silently removed the menu
    /// entries on a toolbar-less host. ⭐ Descriptors are registered always; toolbar entries only when
    /// there is a toolbar.
    /// </param>
    public static IReadOnlyList<string> RegisterCommonCore(
        ShellEditorCommands shell,
        MainToolbarManager? toolbar,
        IIconProvider? icons,
        HostServices services)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(services);

        if (services.OpenAsset is { } openAsset)
            shell.Register(
                new EditorCommandDescriptor(
                    Id:          OpenAssetId,
                    DisplayName: "Open Asset…",
                    Category:    "File",
                    Description: "Open an AI asset (blueprint, behavior tree, HSM, scenario, etc.)",
                    IconKey:     "browser/open",
                    DefaultKey:  new KeyBinding(EditorKey.O, KeyModifiers.Ctrl),
                    IsEnabled:   () => true),
                _ => openAsset());

        if (services.NewAsset is { } newAsset)
            shell.Register(
                new EditorCommandDescriptor(
                    Id:          NewAssetId,
                    DisplayName: "New Asset…",
                    Category:    "File",
                    Description: "Create a new AI asset from a recipe",
                    IconKey:     "asset/new",
                    DefaultKey:  new KeyBinding(EditorKey.N, KeyModifiers.Ctrl),
                    IsEnabled:   () => true),
                _ => newAsset());

        if (services.CompileReload is { } compileReload)
            shell.Register(
                new EditorCommandDescriptor(
                    Id:          CompileReloadId,
                    DisplayName: "Compile / Reload",
                    Category:    "Blueprint",
                    Description: "Compile & hot-reload the active blueprint / BTree / HSM",
                    IconKey:     "build/compile",
                    DefaultKey:  null,
                    IsEnabled:   services.CompileReloadEnabled ?? (() => true)),
                _ => compileReload());

        if (services.FullRebuild is { } fullRebuild)
            shell.Register(
                new EditorCommandDescriptor(
                    Id:          FullRebuildId,
                    DisplayName: "Full Rebuild",
                    Category:    "Build",
                    Description: "Rebuild all AI behavior assets",
                    IconKey:     "build/rebuild",
                    DefaultKey:  null,
                    IsEnabled:   () => true),
                _ => fullRebuild());

        // ── emit ────────────────────────────────────────────────────────────
        // ⚠ TWO passes, and the order matters: a separator can only know whether to appear once the
        //   group behind it is known, so the commands are resolved FIRST and the separators second.
        if (toolbar == null || icons == null) return Array.Empty<string>();

        var emitted   = new List<(int SortOrder, string Id)>();
        var liveGroups = new HashSet<int>();

        foreach (var slot in Layout)
        {
            if (slot.CommandId == null) continue;
            // ⭐⭐ THE DERIVATION. ⛔ Not "is this host CGF?" — "can this shell service this command?"
            if (shell.Get(slot.CommandId) == null) continue;

            ToolbarCommandAdapter.Register(toolbar, shell, slot.CommandId, icons, slot.SortOrder);
            emitted.Add((slot.SortOrder, slot.CommandId));
            liveGroups.Add(slot.Group);
        }

        foreach (var slot in Layout)
        {
            if (slot.SeparatorId == null) continue;
            // ⭐ A separator introduces its own group. ⛔ Emitting one for an absent group leaves a rule
            //   floating against nothing — the dangling-separator defect this slice removes from CGF.
            if (!liveGroups.Contains(slot.Group)) continue;

            toolbar.RegisterSeparator(slot.SeparatorId, slot.SortOrder);
        }

        emitted.Sort((a, b) => a.SortOrder.CompareTo(b.SortOrder));
        return emitted.ConvertAll(e => e.Id);
    }
}
