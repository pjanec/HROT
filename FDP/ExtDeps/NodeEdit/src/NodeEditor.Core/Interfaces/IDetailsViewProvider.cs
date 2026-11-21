using System.Collections.Generic;
using NodeEditor.Core.Commands;
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

    /// <summary>
    /// BP-63 — the graph being edited, so a view can read the state it is about to overwrite and
    /// build a matching inverse command. Without it a Details view can only push a forward command
    /// blind, which is why <c>CommentDetailsView</c> was neither undoable nor revertable.
    ///
    /// <para>
    /// Defaulted to <c>null</c> so existing implementers keep compiling; a view must degrade to a
    /// plain <see cref="CommandSink"/> apply when it is absent.
    /// </para>
    /// </summary>
    Core.Interfaces.IGraphModel? Model => null;

    /// <summary>
    /// BP-63 — applies a change through the host's undo stack, given both directions.
    ///
    /// <para>
    /// The default implementation applies the forward through <see cref="CommandSink"/> and drops
    /// the inverse, preserving the previous (non-undoable) behaviour for hosts that have no stack.
    /// A host with a <c>GraphView</c> should override it with <c>view.Execute</c> so Details-panel
    /// edits share the one stack Ctrl+Z drains.
    /// </para>
    /// </summary>
    GraphCommandResult Execute(GraphCommand forward, GraphCommand inverse, string label)
        => CommandSink.Apply(forward);
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
    /// <summary>A single selected attachment.</summary>
    public sealed record SingleAttachment(AttachmentId Id) : DetailsTarget;
    /// <summary>Multiple selected attachments.</summary>
    public sealed record MultipleAttachments(IReadOnlyList<AttachmentId> Ids) : DetailsTarget;
    /// <summary>A single selected custom-drawn element.</summary>
    public sealed record SingleCustomElement(CustomElementRef Element) : DetailsTarget;
    /// <summary>Multiple selected custom-drawn elements.</summary>
    public sealed record MultipleCustomElements(IReadOnlyList<CustomElementRef> Elements) : DetailsTarget;
}
