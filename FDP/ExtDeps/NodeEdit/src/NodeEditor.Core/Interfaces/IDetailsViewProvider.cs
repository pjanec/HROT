using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Builds a <see cref="IDetailsView"/> for a given target. Multiple
/// providers may register; first matching (highest priority) wins.
/// </summary>
public interface IDetailsViewProvider
{
    int Priority { get; }
    bool CanHandle(DetailsTarget target);
    IDetailsView Build(DetailsTarget target, IDetailsContext ctx);
}

/// <summary>An instance of a Details panel view bound to a specific target.</summary>
public interface IDetailsView
{
    void Draw(IDetailsRenderContext ctx);
    bool IsDirty { get; }
    void Commit();
    void Revert();
}

/// <summary>Context handed to providers at Build time.</summary>
public interface IDetailsContext
{
    IGraphCommandSink CommandSink { get; }
    IPinDefaultValueEditorRegistry Editors { get; }
    IIconProvider Icons { get; }
    IEditorTheme Theme { get; }
}

/// <summary>Context handed to a view at Draw time.</summary>
public interface IDetailsRenderContext
{
    IIconProvider Icons { get; }
    IEditorTheme Theme { get; }
    bool ShowAdvanced { get; }
    bool ShowHelpTooltips { get; }
}

/// <summary>Target the Details panel is currently displaying.</summary>
public abstract record DetailsTarget
{
    public sealed record None : DetailsTarget;
    public sealed record SingleNode(NodeId Id) : DetailsTarget;
    public sealed record MultipleNodes(IReadOnlyList<NodeId> Ids) : DetailsTarget;
    public sealed record Variable(string VariableId) : DetailsTarget;
    public sealed record Function(string FunctionId) : DetailsTarget;
    public sealed record Macro(string MacroId) : DetailsTarget;
    public sealed record CustomEvent(string EventId) : DetailsTarget;
    public sealed record EventDispatcher(string DispatcherId) : DetailsTarget;
    public sealed record LocalVariable(string FunctionId, string LocalId) : DetailsTarget;
    public sealed record FunctionEntry(string FunctionId) : DetailsTarget;
    public sealed record Comment(CommentId Id) : DetailsTarget;
    public sealed record Asset : DetailsTarget;
}
