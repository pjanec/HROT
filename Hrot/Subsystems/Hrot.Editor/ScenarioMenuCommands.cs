using Fdp.Core.Serialization.Migrations;
using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared;
using Hrot.Editor.AiShared.Browser;
using NodeEditor.Core.Action;

namespace Hrot.Editor;

/// <summary>
/// Registers the global <c>scenario.new</c>, <c>scenario.load</c>, and
/// <c>scenario.migrationHistory</c> shell commands and surfaces them as <b>Scenario</b>
/// main-menu items via <see cref="MenuCommandAdapter"/>.
/// (<c>scenario.save</c> / <c>scenario.saveAs</c> are registered by <see cref="ShellSaveCommands"/>;
/// the consts remain declared here for other consumers.)
/// </summary>
/// <remarks>
/// <para>The registrar operates over <see cref="IEditorLogic"/> plus modal/dialog seams so
/// all decision logic is headless-testable. ImGui draw calls live in the production
/// composition root (EditorSubsystem), not here.</para>
/// </remarks>
public static class ScenarioMenuCommands
{
    // ── Command identifiers ────────────────────────────────────────────────────

    /// <summary>Id for the New Scenario command: <c>"scenario.new"</c>.</summary>
    public const string NewId = "scenario.new";

    /// <summary>Id for the Save Scenario command: <c>"scenario.save"</c>.</summary>
    public const string SaveId = "scenario.save";

    /// <summary>Id for the Save As Scenario command: <c>"scenario.saveAs"</c>.</summary>
    public const string SaveAsId = "scenario.saveAs";

    /// <summary>Id for the Load Scenario command: <c>"scenario.load"</c>.</summary>
    public const string LoadId = "scenario.load";

    /// <summary>Id for the Migration History command: <c>"scenario.migrationHistory"</c>.</summary>
    public const string MigrationHistoryId = "scenario.migrationHistory";

    // ── Menu path prefix ───────────────────────────────────────────────────────

    /// <summary>Top-level menu path segment for scenario items.</summary>
    public const string MenuPrefix = "Scenario";

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers the three scenario shell commands and surfaces them as
    /// <b>Scenario</b> menu items.
    /// </summary>
    /// <param name="registerCommand">
    /// Registration delegate: receives an <see cref="EditorCommandDescriptor"/> and its
    /// handler <see cref="Action{EditorCommandContext}"/>.
    /// In production this is <c>WindowManager.ShellCommands.Register</c>; in tests a
    /// recording lambda.
    /// </param>
    /// <param name="menu">
    /// The global menu registry where menu items are surfaced.
    /// </param>
    /// <param name="commands">
    /// The command set containing the registered shell commands.
    /// Used by <see cref="MenuCommandAdapter"/> to look up command descriptors.
    /// </param>
    /// <param name="editorLogic">
    /// The editor logic facade for scenario operations. Never <see langword="null"/>.
    /// </param>
    /// <param name="openPicker">
    /// Seam that opens the <see cref="AssetPickerModal"/> filtered to a given
    /// <see cref="AssetKindFilter"/> and invokes the callback with the selected asset
    /// (or <see langword="null"/> on cancel). Tests inject a recording fake.
    /// </param>
    /// <param name="openSaveAsDialog">
    /// Seam that opens the scenario Save-As dialog and invokes the callback with the
    /// chosen full scenario name on confirm (or never calls back on cancel).
    /// Tests inject a recording fake.
    /// </param>
    /// <param name="showMigrationHistory">
    /// Seam that surfaces the migration sidecar list to the user (e.g. via a popup or
    /// log panel). When <see langword="null"/>, the command is still registered but
    /// the handler is a no-op. Tests inject a recording fake.
    /// </param>
    public static void Register(
        Action<EditorCommandDescriptor, Action<EditorCommandContext>> registerCommand,
        GlobalMenuRegistry                                        menu,
        IEditorCommands                                           commands,
        IEditorLogic                                              editorLogic,
        Action<AssetKindFilter, Action<IEditableAsset?>>          openPicker,
        Action<Action<string>>                                    openSaveAsDialog,
        Action<IReadOnlyList<SidecarFileInfo>>?                   showMigrationHistory = null)
    {
        if (registerCommand    is null) throw new ArgumentNullException(nameof(registerCommand));
        if (menu               is null) throw new ArgumentNullException(nameof(menu));
        if (commands           is null) throw new ArgumentNullException(nameof(commands));
        if (editorLogic        is null) throw new ArgumentNullException(nameof(editorLogic));
        if (openPicker         is null) throw new ArgumentNullException(nameof(openPicker));
        if (openSaveAsDialog   is null) throw new ArgumentNullException(nameof(openSaveAsDialog));

        // ── scenario.new ───────────────────────────────────────────────────────
        RegisterCommand(
            registerCommand, menu, commands,
            NewId, "New", "Create an empty scenario",
            isEnabled: () => true,
            handler: _ => editorLogic.NewScenario());

        // ── scenario.load ──────────────────────────────────────────────────────
        RegisterCommand(
            registerCommand, menu, commands,
            LoadId, "Load…", "Load a scenario from the scenario catalog",
            isEnabled: () => true,
            handler: _ =>
            {
                openPicker(
                    AssetKindFilter.Scenario,
                    picked =>
                    {
                        if (picked != null)
                            editorLogic.LoadScenarioByName(picked.Name);
                    });
            });

        // ── scenario.migrationHistory ──────────────────────────────────────────
        bool migrationHistoryEnabled() =>
            !string.IsNullOrEmpty(editorLogic.LoadedScenarioName);

        RegisterCommand(
            registerCommand, menu, commands,
            MigrationHistoryId, "Migration History…",
            "Show migration sidecars for the loaded scenario",
            isEnabled: migrationHistoryEnabled,
            handler: _ =>
            {
                var sidecars = editorLogic.GetMigrationSidecarsForCurrentScenario();
                showMigrationHistory?.Invoke(sidecars);
            });
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static void RegisterCommand(
        Action<EditorCommandDescriptor, Action<EditorCommandContext>> registerCommand,
        GlobalMenuRegistry menu,
        IEditorCommands commands,
        string commandId,
        string displayName,
        string description,
        Func<bool> isEnabled,
        Action<EditorCommandContext> handler)
    {
        var descriptor = new EditorCommandDescriptor(
            Id:          commandId,
            DisplayName: displayName,
            Category:    "Scenario",
            Description: description,
            IconKey:     null,
            DefaultKey:  null,
            IsEnabled:   isEnabled);

        registerCommand(descriptor, handler);

        var menuPath = $"{MenuPrefix}/{displayName.Replace("…", "")}";
        MenuCommandAdapter.Register(menu, commands, commandId, menuPath);
    }
}
