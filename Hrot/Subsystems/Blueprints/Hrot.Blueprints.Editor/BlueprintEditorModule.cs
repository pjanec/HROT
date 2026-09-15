using Hrot.Blueprints.Core.Debug;
using Hrot.Blueprints.Core.Compiler.Emit;

namespace Hrot.Blueprints.Editor;

/// <summary>
/// Entry point for the Blueprint editor integration. Owns all editor windows,
/// handles reload events, and wires the debug session lifecycle.
/// </summary>
public sealed class BlueprintEditorModule
{
    private readonly IShellCommandRegistrar _windowRegistrar;
    private readonly DirtyTracker _dirtyTracker;
    private readonly EditorSelectionStore _selectionStore;
    private readonly EditorState _editorState;
    private readonly IOutputConsole _outputConsole;
    private readonly IBlueprintDebugSession? _session;

    private readonly List<IBlueprintEditorWindow> _windows = new();
    private bool _activated;

    public BlueprintEditorModule(
        IShellCommandRegistrar windowRegistrar,
        DirtyTracker dirtyTracker,
        EditorSelectionStore selectionStore,
        EditorState editorState,
        IOutputConsole outputConsole,
        IBlueprintDebugSession? session = null)
    {
        _windowRegistrar = windowRegistrar ?? throw new ArgumentNullException(nameof(windowRegistrar));
        _dirtyTracker    = dirtyTracker    ?? throw new ArgumentNullException(nameof(dirtyTracker));
        _selectionStore  = selectionStore  ?? throw new ArgumentNullException(nameof(selectionStore));
        _editorState     = editorState     ?? throw new ArgumentNullException(nameof(editorState));
        _outputConsole   = outputConsole   ?? throw new ArgumentNullException(nameof(outputConsole));
        _session         = session;
    }

    public void OnEditorActivated()
    {
        if (_activated) return;
        _activated = true;

        _session?.Attach();

        // Register menu entries for each window via IShellCommandRegistrar.
        foreach (var window in _windows)
            _windowRegistrar.RegisterMenuEntry($"Blueprint/{window.Title}", () => window.ToggleVisible());

        foreach (var window in _windows)
            window.OnActivated();
    }

    public void OnEditorDeactivated()
    {
        if (!_activated) return;
        _activated = false;

        _session?.Detach();

        foreach (var window in _windows)
            window.OnDeactivated();
    }

    public void RegisterWindow(IBlueprintEditorWindow window)
        => _windows.Add(window ?? throw new ArgumentNullException(nameof(window)));

    public IReadOnlyList<IBlueprintEditorWindow> Windows => _windows;

    /// <summary>
    /// Called by the editor frame loop. Draws all visible windows.
    /// </summary>
    public void DrawAllWindows()
    {
        foreach (var window in _windows)
            if (window.IsVisible) window.DrawUI();
    }

    /// <summary>
    /// Called when a reload completes (from either Quick Reload or Full Rebuild).
    /// Routes by source per Patch 2.
    /// </summary>
    public void OnReloadCompleted(ReloadCompletedInfo info)
    {
        if (info.Source == ReloadSource.QuickReloadViaApi)
        {
            // Map already registered by QuickReloadService before apply.
            // Nothing to do here for map registration.
            _outputConsole.LogInfo(
                $"Quick reload completed in {info.DurationMs}ms " +
                $"({info.ReloadedAssetIds.Length} asset(s)).");
        }
        else if (info.Source == ReloadSource.FullRebuildViaFileWatcher)
        {
            if (info.DllPath != null)
            {
                _outputConsole.LogInfo($"Full rebuild completed: {info.DllPath}");

                // Load and register debug maps for all assets in the build output.
                var dir = Path.GetDirectoryName(info.DllPath);
                if (dir != null)
                {
                    foreach (var mapFile in Directory.EnumerateFiles(dir, "*.dbgmap.json"))
                    {
                        try
                        {
                            var json = File.ReadAllText(mapFile);
                            var map  = DebugMapSerializer.Deserialize(json);
                            if (map != null && _session != null)
                                _session.RegisterDebugMap(map);
                        }
                        catch (Exception ex)
                        {
                            _outputConsole.LogError($"Failed to load debug map {mapFile}: {ex.Message}");
                        }
                    }
                }
            }
        }
    }
}
