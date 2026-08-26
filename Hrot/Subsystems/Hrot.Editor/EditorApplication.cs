using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Fdp.Core;
using Fdp.Core.Logging;
using Fdp.Core.Serialization.Migrations;
using Fdp.Toolkit.DER;
using Fdp.Toolkit.NetworkSpawning.Events;
using Hrot.Common.Events;
using Hrot.Editor.Commands;
using Hrot.Editor.Events;
using Hrot.Editor.Modules;
using Hrot.ScenarioEditor.Services;
using Fdp.ModuleHost;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Editor;

/// <summary>
/// Implements <see cref="IEditorLogic"/> by delegating file operations to
/// <see cref="ScenarioFileService"/> and tool/command operations to
/// <see cref="FdpEventBus"/> events.
///
/// <para>
/// Panels must bind exclusively to <see cref="IEditorLogic"/> — no direct
/// field references to this class or any ECS/DDS types.
/// </para>
/// </summary>
public sealed class EditorApplication : IEditorLogic
{
    // ⭐⭐⭐ CE-046 (Axis-C E1) — the scenario half of this class now lives in
    //    Hrot.Editor.AiShared.Scenarios.EditorScenarioSession, which CGF instantiates over its own world.
    //    📄 docs/DESIGN_Cgf_Scenario_Session_Slice.md §3 ①/②. ⛔ Every scenario member below DELEGATES;
    //    the behaviour is unchanged by construction because the logic MOVED rather than being rewritten.
    private readonly Hrot.Editor.AiShared.Scenarios.EditorScenarioSession _session;
    private readonly FdpEventBus         _simBus;
    private readonly DerRepo             _view = new(localNodeId: 0);

    private readonly ModuleHostKernel?          _kernel;
    private readonly IReadOnlyList<IEcsModule>? _logicPacks;
    private readonly IReadOnlyList<IEcsModule>? _translatorPacks;
    private readonly HotReloadMessageLogSource? _hotReloadSource;
    private readonly string[]                   _aiProjectPathSegments;
    private SimHostMode _currentMode = SimHostMode.Internal;

    /// <summary>The current cluster lifecycle state. Exposed for the AI-debug API (MCP) host.</summary>
    public Fdp.Toolkit.Orchestration.ClusterState CurrentClusterState => _session.CurrentClusterState;

    // ── Scenario tracking ─────────────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐ <b>The shared scenario session this editor drives</b> — handed to
    /// <see cref="ScenarioMenuCommands"/> so the registrar binds to the host-agnostic seam rather than to
    /// the editor-only <see cref="IEditorLogic"/>. 📄 design §3 ④.
    /// </summary>
    public Hrot.Editor.AiShared.Scenarios.IScenarioSession ScenarioSession => _session;

    /// <summary>
    /// Optional delegate that returns the available scenario names.
    /// Injected by <see cref="SetAvailableScenariosSource"/> after construction to
    /// avoid a circular reference with Hrot.ClusterRunner.
    /// </summary>
    private Func<IReadOnlyList<string>>? _availableScenariosSource;

    public IDerRepo View => _view;
    public SimHostMode CurrentMode => _currentMode;

    /// <inheritdoc/>
    public string? LoadedScenarioName => _session.LoadedScenarioName;

    /// <inheritdoc/>
    public IReadOnlyList<string> AvailableScenarios =>
        _availableScenariosSource?.Invoke() ?? Array.Empty<string>();

    /// <summary>
    /// Alert manager for migration events (degraded-mode banner, alert modal).
    /// </summary>
    internal Hrot.Editor.AiShared.Scenarios.MigrationAlertManager AlertManager => _session.AlertManager;

    /// <inheritdoc/>
    public void Update() => _session.Update();

    public EditorApplication(
        ScenarioFileService fileService,
        FdpEventBus simBus,
        FdpEventBus orchestrationBus,
        EntityRepository world,
        ModuleHostKernel?          kernel          = null,
        IReadOnlyList<IEcsModule>? logicPacks      = null,
        IReadOnlyList<IEcsModule>? translatorPacks = null,
        HotReloadMessageLogSource? hotReloadSource = null,
        string[]? aiProjectPathSegments            = null)
    {
        _simBus               = simBus           ?? throw new ArgumentNullException(nameof(simBus));

        // ⭐⭐ CE-046 — the scenario half, over THIS host's world. ⭐ `ScenariosRoot` is passed as a
        //    DELEGATE, not a captured string: it is a computed property over
        //    ClusterConfiguration.Default.NasBasePath, so snapshotting it here would change when the
        //    value is read. ⛔ The ctor still validates its own arguments so a wiring bug fails at boot
        //    with the parameter name, exactly as before.
        _session = new Hrot.Editor.AiShared.Scenarios.EditorScenarioSession(
            fileService      ?? throw new ArgumentNullException(nameof(fileService)),
            orchestrationBus ?? throw new ArgumentNullException(nameof(orchestrationBus)),
            world            ?? throw new ArgumentNullException(nameof(world)),
            () => EditorBootstrap.ScenariosRoot);

        _kernel               = kernel;
        _logicPacks           = logicPacks;
        _translatorPacks      = translatorPacks;
        _hotReloadSource      = hotReloadSource;
        _aiProjectPathSegments = aiProjectPathSegments
            ?? new[] { "Subsystems", "Hrot.AI.Behaviors", "Hrot.AI.Behaviors.csproj" };
    }

    /// <summary>
    /// Provides a source for the available scenario list (e.g. from ClusterUiCache).
    /// Call this after construction to avoid circular assembly references.
    /// </summary>
    public void SetAvailableScenariosSource(Func<IReadOnlyList<string>> source)
    {
        _availableScenariosSource = source;
    }

    // ── Scenario members — every one DELEGATES to the shared session (CE-046, design §3 ②) ─────
    // ⚠⚠ `NewScenario` maps to `ClearWorld`, NOT to `NewExercise`. 📐 Measured: the deferred-load state
    //    machine calls this as step 1 of its OWN sequence, so pointing it at the cluster-wide reset would
    //    publish a second Idle intent from inside the handler for the first one. ⭐ `NewExercise` is
    //    reachable from the File/Live menu item, which is the operator-facing action.

    /// <inheritdoc/>
    public void NewScenario() => _session.ClearWorld();

    /// <inheritdoc/>
    public void SaveScenario(string filePath) => _session.SaveTo(filePath);

    /// <inheritdoc/>
    public void LoadScenarioByName(string scenarioName) => _session.OpenForEdit(scenarioName);

    /// <inheritdoc/>
    public void SaveCurrentScenario() => _session.SaveCurrent();

    /// <inheritdoc/>
    public void SaveScenarioAs(string scenarioName) => _session.SaveAs(scenarioName);

    /// <inheritdoc/>
    public void ActivateTool(EditorTool tool)
    {
        // Publish an event that the active tool controller listens for.
        // The actual tool switch logic lives in EditorSubsystem.DrainToolActivationEvents().
        _simBus.Publish(new ActivateEditorToolEvent(tool));
    }

    /// <inheritdoc/>
    public void CommitPropertyEdit(long networkId, IReadOnlyList<object> updatedComponents)
    {
        if (updatedComponents == null) throw new ArgumentNullException(nameof(updatedComponents));
        _simBus.PublishManaged(new UpdateEntityCommand
        {
            NetworkId          = networkId,
            ComponentsToUpdate = new List<object>(updatedComponents),
        });
    }

    /// <inheritdoc/>
    public async Task SwitchToExternalAsync()
    {
        if (_kernel == null || _logicPacks == null) return;
        if (_currentMode == SimHostMode.External) return;

        await _kernel.UninstallModulesAsync(_logicPacks);

        if (_translatorPacks != null)
            await _kernel.InstallModulesAsync(_translatorPacks);

        _currentMode = SimHostMode.External;
    }

    /// <inheritdoc/>
    public async Task SwitchToInternalAsync()
    {
        if (_kernel == null || _logicPacks == null) return;
        if (_currentMode == SimHostMode.Internal) return;

        if (_translatorPacks != null)
            await _kernel.UninstallModulesAsync(_translatorPacks);

        await _kernel.InstallModulesAsync(_logicPacks);

        _currentMode = SimHostMode.Internal;
    }

    /// <inheritdoc/>
    public void CenterOnEntity(long entityId) =>
        _simBus.Publish(new CenterOnEntityCommand { NetworkId = entityId });

    /// <inheritdoc/>
    public void SelectEntity(long entityId) =>
        _simBus.Publish(new SelectEntityCommand { NetworkId = entityId });

    /// <inheritdoc/>
    public void OpenRenameDialog(long entityId) =>
        _simBus.Publish(new OpenRenameDialogCommand { NetworkId = entityId });

    /// <inheritdoc/>
    public void RebuildAndReloadAI()
    {
        // Fire-and-forget background compilation to keep the 60Hz UI responsive.
        // Once MSBuild overwrites the DLL, the FileSystemWatcher in FbtAssemblyHotReloader
        // will automatically detect it and swap the BTree interpreters via the ALC.
        Task.Run(() =>
        {
            string? projectPath = ResolveProjectFilePath(_aiProjectPathSegments);
            if (projectPath == null)
            {
                _hotReloadSource?.PushLine(
                    $"ERROR: AI Behaviors project file not found. Searched parent directories" +
                    $" from CWD for relative path: {Path.Combine(_aiProjectPathSegments)}");
                return;
            }

            _hotReloadSource?.PushLine($"Starting dotnet build: {projectPath}");

            var psi = new ProcessStartInfo
            {
                FileName               = "dotnet",
                Arguments              = $"build \"{projectPath}\"",
                CreateNoWindow         = true,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = false };

            // Async line-by-line reading avoids the classic OS pipe-buffer deadlock
            // that occurs when both stdout and stderr fill up while the caller is
            // blocked on WaitForExit() with synchronous ReadToEnd().
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) _hotReloadSource?.PushLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) _hotReloadSource?.PushLine($"ERROR: {e.Data}");
            };

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                _hotReloadSource?.PushLine($"Build exited with code {process.ExitCode}.");
            }
            catch (Exception ex)
            {
                _hotReloadSource?.PushLine($"ERROR: Failed to start dotnet process: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>CE-018</c> — the FOURTH copy of the <c>.csproj</c> walk-up, routed to the one
    /// implementation</b> *(<see cref="Hrot.Editor.AiShared.AssetRoots.ResolveProjectDir"/>)*.
    ///
    /// <para>⚠ <b>Two behaviour changes, both wanted.</b> ① The copy searched <b>only</b> from
    /// <c>Environment.CurrentDirectory</c>; the shared one also searches from the output directory, so a
    /// build launched from a bin folder now finds the project instead of reporting *"not found"*.
    /// ② A node configured under ruling 67 is honoured — the copy could not see that arm at all.</para>
    ///
    /// <para>⚠ Returns the <b>file</b> path, as the caller needs it for <c>dotnet build</c> — the shared
    /// resolver answers the DIRECTORY, so the leaf segment is re-joined here.</para>
    /// </summary>
    private static string? ResolveProjectFilePath(string[] pathSegments)
    {
        if (pathSegments is null || pathSegments.Length == 0) return null;

        var dir = Hrot.Editor.AiShared.AssetRoots.ResolveProjectDir(pathSegments);
        return dir == null ? null : Path.Combine(dir, pathSegments[^1]);
    }

    /// <inheritdoc/>
    public bool IsScenarioDegraded => _session.IsDegraded;

    /// <inheritdoc/>
    public IReadOnlyList<SidecarFileInfo> GetMigrationSidecarsForCurrentScenario()
        => _session.GetMigrationSidecars();

    /// <summary>
    /// Returns an <see cref="EditorSystemsModule"/> initialised against this application's
    /// <see cref="EntityRepository"/>.  The caller must register the returned module with
    /// the kernel <em>before</em> calling <c>kernel.Initialize()</c>.
    /// </summary>
    public EditorSystemsModule CreateEditorSystemsModule()
        => new EditorSystemsModule();
}
