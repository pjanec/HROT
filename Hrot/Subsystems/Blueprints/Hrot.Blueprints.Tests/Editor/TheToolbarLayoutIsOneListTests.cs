using System.Linq;
using Fdp.Presentation.WindowManager;
using Hrot.Blueprints.Editor.Debug;
using Hrot.Editor.AiShared.Windows;
using NodeEditor.Core.Action;
using NodeEditor.Core.Interfaces;
using Xunit;

namespace Hrot.Blueprints.Tests.Editor;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-016</c> §7 — the gate for the extraction, and the guard for the id mirror.</b>
/// 📄 <c>docs/DESIGN_Cgf_Shell_Command_Toolbar_Slice.md</c> §6.
///
/// <para>⭐⭐ <b>Why here and not in <c>Hrot.Editor.AiShared.Tests</c>:</b> the mirror check needs to see
/// BOTH <c>CgfEditorShellToolbar</c> *(AiShared)* and <c>AiDebugCommands</c> *(Hrot.Blueprints.Editor,
/// which sits ABOVE AiShared)*. ⛔ Only a project referencing both can compare them — this one does.</para>
///
/// <para>⚠ Headless: <c>MainToolbarManager</c> and <c>ShellEditorCommands</c> are plain registries, so the
/// whole layout is assertable with no ImGui context.</para>
/// </summary>
public sealed class TheToolbarLayoutIsOneListTests
{
    /// <summary>⭐ A no-op provider — the layout is what is under test, not icon resolution.</summary>
    private sealed class NullIcons : IIconProvider
    {
        public bool TryGet(string key, out IconHandle handle) { handle = default; return false; }
    }

    private static EditorCommandDescriptor Desc(string id) =>
        new(Id: id, DisplayName: id, Category: "Test", Description: id,
            IconKey: null, DefaultKey: null, IsEnabled: () => true);

    private static void RegisterAll(ShellEditorCommands shell, params string[] ids)
    {
        foreach (var id in ids) shell.Register(Desc(id), _ => { });
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE EXTRACTION GATE (item ②): the editor's rendered toolbar entry list is UNCHANGED.</b>
    ///
    /// <para>📐 The expected list is the inline block <c>EditorSubsystem</c> carried before this slice,
    /// read off the pre-extraction source: ids AND sort orders, separators included. ⛔ If the helper
    /// renumbers anything, or gains/loses a slot, this reddens — which is the whole point, because the
    /// conformance rail compares CGF against the editor BY id AND sortOrder.</para>
    ///
    /// <para>⚠ The PERSPECTIVE section (sortOrder 20) is absent here by design: it is a section, not a
    /// command, and each host builds its own. It is registered by the host, not the helper.</para>
    /// </summary>
    [Fact]
    public void The_editor_full_shell_yields_exactly_the_pre_extraction_layout()
    {
        var shell   = new ShellEditorCommands();
        var toolbar = new MainToolbarManager();

        // Everything the EDITOR's shell holds by the time the helper runs.
        RegisterAll(shell,
            Hrot.Editor.AiShared.Documents.ShellSaveCommands.SaveId,
            Hrot.Editor.AiShared.Documents.ShellSaveCommands.SaveAllId,
            AiDebugCommands.ContinueId, AiDebugCommands.StepOverId, AiDebugCommands.StepIntoId,
            AiDebugCommands.StepOutId,  AiDebugCommands.PauseId,    AiDebugCommands.StepBackId);

        CgfEditorShellToolbar.RegisterCommonCore(
            shell, toolbar, new NullIcons(),
            new CgfEditorShellToolbar.HostServices(
                OpenAsset: () => { }, NewAsset: () => { },
                CompileReload: () => { }, FullRebuild: () => { }));

        var actual = toolbar.BuildViewModel(currentPerspective: "Blueprint")
                            .Entries.Select(e => (e.Id, e.SortOrder)).ToArray();

        var expected = new[]
        {
            ("shell.newAsset",             -11),
            ("shell.openAsset",            -10),
            ("shell.save",                  -9),
            ("ToolbarSep_OpenAsset",         0),
            ("ToolbarSep_PerspToAiDebug",   30),
            ("debug.continue",              40),
            ("debug.stepOver",              41),
            ("debug.stepInto",              42),
            ("debug.stepOut",               43),
            ("debug.pause",                 44),
            ("debug.stepBack",              45),
            ("ToolbarSep_AiDebugToBuild",   49),
            ("blueprint.compileReload",     50),
            ("blueprint.fullRebuild",       51),
        };

        Assert.Equal(expected, actual);

        // ⛔⛔ AND NO Save-All BUTTON. 📐 The editor's toolbar never had one — the design's §3 subset
        //    sentence lists SaveAll, but adding it would emit it on the EDITOR too and break this very
        //    gate. ⚠ Asserted explicitly so a future "round it out" is a deliberate, two-host decision.
        Assert.DoesNotContain(actual, e => e.Id == Hrot.Editor.AiShared.Documents.ShellSaveCommands.SaveAllId);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE DERIVATION (ruling 49 / UXI-35): a host that services less gets fewer BUTTONS from the
    /// SAME table — nothing is greyed, and no second list exists.</b>
    ///
    /// <para>📐 CGF's shell: save + compile-reload + the debug steps it can route to its cluster
    /// controller — ⛔ no <c>fullRebuild</c>. ⇒ the Build separator still appears *(compileReload is in
    /// that group)*, but the Full Rebuild button does not.</para>
    /// </summary>
    [Fact]
    public void A_host_that_services_less_gets_fewer_buttons_not_greyed_ones()
    {
        var shell   = new ShellEditorCommands();
        var toolbar = new MainToolbarManager();

        RegisterAll(shell,
            Hrot.Editor.AiShared.Documents.ShellSaveCommands.SaveId,
            AiDebugCommands.ContinueId, AiDebugCommands.PauseId);

        CgfEditorShellToolbar.RegisterCommonCore(
            shell, toolbar, new NullIcons(),
            new CgfEditorShellToolbar.HostServices(
                OpenAsset: () => { }, NewAsset: () => { }, CompileReload: () => { }));
        //                                                  ⛔ FullRebuild: not supplied

        var ids = toolbar.BuildViewModel("Scenario").Entries.Select(e => e.Id).ToArray();

        Assert.Contains("shell.save", ids);
        Assert.Contains("blueprint.compileReload", ids);
        Assert.Contains("debug.continue", ids);

        // ⛔ ABSENT, not greyed — the host supplied no handler, so no descriptor, so no entry.
        Assert.DoesNotContain("blueprint.fullRebuild", ids);
        // ⛔ And a command this shell simply never registered.
        Assert.DoesNotContain("debug.stepBack", ids);
    }

    /// <summary>
    /// ⭐⭐ <b>NO DANGLING SEPARATOR</b> — a rule is emitted only when the group behind it produced
    /// something. 📌 The defect this slice removes from CGF was exactly a separator
    /// *(<c>ToolbarSep_TimeToPersp</c>)* left pointing at a group that was never registered.
    /// </summary>
    [Fact]
    public void A_group_that_emits_nothing_takes_its_separator_with_it()
    {
        var shell   = new ShellEditorCommands();
        var toolbar = new MainToolbarManager();

        // Only the File group can be serviced: no debug commands, no build handlers.
        RegisterAll(shell, Hrot.Editor.AiShared.Documents.ShellSaveCommands.SaveId);

        CgfEditorShellToolbar.RegisterCommonCore(
            shell, toolbar, new NullIcons(),
            new CgfEditorShellToolbar.HostServices(OpenAsset: () => { }));

        var ids = toolbar.BuildViewModel("Scenario").Entries.Select(e => e.Id).ToArray();

        Assert.Contains("ToolbarSep_OpenAsset", ids);       // its group emitted
        Assert.DoesNotContain("ToolbarSep_PerspToAiDebug", ids);
        Assert.DoesNotContain("ToolbarSep_AiDebugToBuild", ids);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>THE MIRROR GUARD.</b> <c>CgfEditorShellToolbar.AiDebugCommandIds</c> duplicates the
    /// <c>AiDebugCommands</c> constants because AiShared sits BELOW <c>Hrot.Blueprints.Editor</c> and
    /// cannot reference it.
    ///
    /// <para>⛔⛔ A drift there is SILENT: the derived-subset rule means an id nothing registers simply
    /// yields no button, so a typo deletes the whole AI-debug toolbar group and fails nothing.
    /// 📌 <b>Measured during this slice</b> — the first draft guessed <c>"ai.debug.continue"</c>; the real
    /// id is <c>"debug.continue"</c>. ⭐ This test is why that can only happen once.</para>
    /// </summary>
    [Fact]
    public void The_mirrored_debug_ids_match_their_source_of_truth()
    {
        Assert.Equal(AiDebugCommands.ContinueId, CgfEditorShellToolbar.AiDebugCommandIds.Continue);
        Assert.Equal(AiDebugCommands.StepOverId, CgfEditorShellToolbar.AiDebugCommandIds.StepOver);
        Assert.Equal(AiDebugCommands.StepIntoId, CgfEditorShellToolbar.AiDebugCommandIds.StepInto);
        Assert.Equal(AiDebugCommands.StepOutId,  CgfEditorShellToolbar.AiDebugCommandIds.StepOut);
        Assert.Equal(AiDebugCommands.PauseId,    CgfEditorShellToolbar.AiDebugCommandIds.Pause);
        Assert.Equal(AiDebugCommands.StepBackId, CgfEditorShellToolbar.AiDebugCommandIds.StepBack);
    }

    /// <summary>
    /// ⭐ A null toolbar registers the DESCRIPTORS and no entries — the bare-<c>EditorSubsystem</c> case,
    /// where the File menu must still offer Open/New even though there is no toolbar to draw on.
    /// </summary>
    [Fact]
    public void A_host_with_no_toolbar_still_gets_the_commands()
    {
        var shell = new ShellEditorCommands();

        var emitted = CgfEditorShellToolbar.RegisterCommonCore(
            shell, toolbar: null, icons: null,
            new CgfEditorShellToolbar.HostServices(OpenAsset: () => { }, NewAsset: () => { }));

        Assert.Empty(emitted);
        Assert.NotNull(shell.Get(CgfEditorShellToolbar.OpenAssetId));
        Assert.NotNull(shell.Get(CgfEditorShellToolbar.NewAssetId));
    }
}
