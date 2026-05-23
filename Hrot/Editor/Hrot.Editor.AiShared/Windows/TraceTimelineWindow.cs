using Fdp.Presentation.WindowManager;
using Hrot.Editor.AiShared.Debug;
using Hrot.Editor.AiShared.Selection;

namespace Hrot.Editor.AiShared.Windows;

/// <summary>
/// Shell for the shared trace timeline. Renders swim lanes provided by
/// the registered ITraceLaneProvider for the active asset kind.
/// Subsystems provide ITraceLaneProvider implementations.
/// </summary>
public sealed class TraceTimelineWindow : ManagedWindow
{
    private readonly EditorSelectionStore _store;
    private readonly IDebugSessionRegistry _registry;
    private readonly List<ITraceLaneProvider> _providers = new();

    public TraceTimelineWindow(
        EditorSelectionStore store,
        IDebugSessionRegistry registry)
        : base("ai_trace_timeline", "Trace Timeline", "Authoring", WindowScope.PerspectiveBound)
    {
        _store = store;
        _registry = registry;
    }

    /// <summary>Register a subsystem-provided lane provider. Called at editor startup.</summary>
    public void RegisterProvider(ITraceLaneProvider provider) => _providers.Add(provider);

    /// <summary>Number of registered providers. Exposed for test verification.</summary>
    internal int RegisteredProviderCount => _providers.Count;

    protected override void DrawClientArea()
    {
        // Shell: show empty state until lane providers are registered.
        ImGuiNET.ImGui.TextDisabled("No trace data.");
    }
}
