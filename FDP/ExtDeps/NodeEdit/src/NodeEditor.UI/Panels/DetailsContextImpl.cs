using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.Panels;

/// <summary>
/// Concrete implementation of <see cref="IDetailsContext"/>.
/// </summary>
internal sealed class DetailsContext : IDetailsContext
{
    public required IGraphCommandSink CommandSink { get; init; }
    public required IPinDefaultValueEditorRegistry Editors { get; init; }
    public required IIconProvider Icons { get; init; }
    public required IEditorTheme Theme { get; init; }

    /// <summary>BP-63: optional — set it and Details views can build inverse commands.</summary>
    public IGraphModel? Model { get; init; }

    /// <summary>
    /// BP-63: optional undo-recording seam. When the host supplies one (typically
    /// <c>view.Execute</c>), Details-panel edits land on the same stack as canvas edits; when it is
    /// null the interface default applies the forward through the sink, unchanged from before.
    /// </summary>
    public Func<GraphCommand, GraphCommand, string, GraphCommandResult>? Recorder { get; init; }

    public GraphCommandResult Execute(GraphCommand forward, GraphCommand inverse, string label)
        => Recorder is not null
            ? Recorder(forward, inverse, label)
            : CommandSink.Apply(forward);
}

/// <summary>
/// Concrete implementation of <see cref="IDetailsRenderContext"/>.
/// </summary>
internal sealed class DetailsRenderContext : IDetailsRenderContext
{
    public required IIconProvider Icons { get; init; }
    public required IEditorTheme Theme { get; init; }
    public bool ShowAdvanced { get; set; }
    public bool ShowHelpTooltips { get; set; } = true;
}
