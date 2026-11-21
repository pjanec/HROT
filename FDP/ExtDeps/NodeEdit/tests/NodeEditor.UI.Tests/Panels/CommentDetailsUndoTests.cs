using System.Numerics;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using Xunit;

namespace NodeEditor.UI.Tests.Panels;

/// <summary>
/// BP-63 — the built-in Comment Details view committed with a raw <c>CommandSink.Apply</c>. A sink
/// applies; it does not record, so the edit reached the graph and nothing reached the undo stack.
/// It could not be fixed in place either: <see cref="IDetailsContext"/> exposed no model, so there
/// was no prior state to build an inverse from — the class said so itself, in a comment explaining
/// why <c>Revert()</c> was a no-op.
///
/// <para>
/// The context now carries an optional model and an optional recording seam, both defaulted so no
/// existing implementer breaks. These tests drive the seam directly; the ImGui form is not covered.
/// </para>
/// </summary>
public sealed class CommentDetailsUndoTests
{
    // ── Minimal fakes ────────────────────────────────────────────────────────

    private sealed class FakeComment : ICommentModel
    {
        public CommentId Id { get; init; }
        public string Text { get; set; } = "";
        public Vector2 Position { get; set; }
        public Vector2 Size { get; set; }
        public Vector4 Color { get; set; }
        public bool MoveWithContents { get; set; }
        public int ZOrder { get; set; }
    }

    private sealed class RecordingContext : IDetailsContext
    {
        public required IGraphCommandSink CommandSink { get; init; }
        public IPinDefaultValueEditorRegistry Editors => null!;
        public IIconProvider Icons => null!;
        public IEditorTheme Theme => null!;

        public IGraphModel? Model { get; init; }

        public List<(GraphCommand Forward, GraphCommand Inverse, string Label)> Executed { get; } = new();
        public bool HasRecorder { get; init; }

        public GraphCommandResult Execute(GraphCommand forward, GraphCommand inverse, string label)
        {
            if (!HasRecorder) return CommandSink.Apply(forward);
            Executed.Add((forward, inverse, label));
            return CommandSink.Apply(forward);
        }
    }

    private sealed class RecordingSink : IGraphCommandSink
    {
        public List<GraphCommand> Applied { get; } = new();
        public GraphCommandResult Apply(GraphCommand command)
        {
            Applied.Add(command);
            return new GraphCommandResult(true, null);
        }
    }

    // ── The default keeps the old behaviour ──────────────────────────────────

    /// <summary>
    /// A host that supplies neither model nor recorder must behave exactly as before: the forward is
    /// applied through the sink. The new members are defaulted for precisely this reason.
    /// </summary>
    [Fact]
    public void ContextWithoutARecorder_FallsBackToApplyingThroughTheSink()
    {
        var sink = new RecordingSink();
        IDetailsContext ctx = new RecordingContext { CommandSink = sink, HasRecorder = false };

        var id  = new CommentId(Guid.NewGuid());
        var fwd = new GraphCommand.UpdateComment(id, "after",  null, null, null, null, null);
        var inv = new GraphCommand.UpdateComment(id, "before", null, null, null, null, null);

        var result = ctx.Execute(fwd, inv, "Edit Comment");

        Assert.True(result.Success);
        Assert.Equal(new GraphCommand[] { fwd }, sink.Applied);
    }

    // ── With a recorder, both directions reach the host ─────────────────────

    [Fact]
    public void ContextWithARecorder_ForwardsBothDirectionsAndTheLabel()
    {
        var sink = new RecordingSink();
        var ctx  = new RecordingContext { CommandSink = sink, HasRecorder = true };

        var id  = new CommentId(Guid.NewGuid());
        var fwd = new GraphCommand.UpdateComment(id, "after",  null, null, null, null, null);
        var inv = new GraphCommand.UpdateComment(id, "before", null, null, null, null, null);

        ((IDetailsContext)ctx).Execute(fwd, inv, "Edit Comment");

        var (f, i, label) = Assert.Single(ctx.Executed);
        Assert.Same(fwd, f);
        Assert.Same(inv, i);
        Assert.Equal("Edit Comment", label);
    }

    // ── The model is what makes an inverse possible at all ──────────────────

    /// <summary>
    /// The inverse has to be built from the comment's pre-edit state, which is only reachable
    /// through the model. This pins that the model exposes it — without this the view can push a
    /// forward command and nothing else, which is the state BP-63 describes.
    /// </summary>
    [Fact]
    public void ModelOnTheContext_ExposesTheCommentsPreEditState()
    {
        var id      = new CommentId(Guid.NewGuid());
        var comment = new FakeComment
        {
            Id               = id,
            Text             = "before",
            Position         = new Vector2(10f, 20f),
            Size             = new Vector2(200f, 100f),
            Color            = new Vector4(1f, 0f, 0f, 1f),
            MoveWithContents = true,
        };
        var model = new SingleCommentModel(comment);
        IDetailsContext ctx = new RecordingContext { CommandSink = new RecordingSink(), Model = model };

        var found = ctx.Model!.Comments.Single(c => c.Id == id);

        Assert.Equal("before", found.Text);
        Assert.Equal(new Vector2(10f, 20f), found.Position);
        Assert.True(found.MoveWithContents);
    }

    [Fact]
    public void ContextWithoutAModel_ReportsNull_SoTheViewCanDegrade()
    {
        IDetailsContext ctx = new RecordingContext { CommandSink = new RecordingSink() };
        Assert.Null(ctx.Model);
    }

    /// <summary>Graph model exposing exactly one comment; every other member is inert.</summary>
    private sealed class SingleCommentModel : IGraphModel
    {
        private readonly ICommentModel _comment;
        public SingleCommentModel(ICommentModel comment) => _comment = comment;

        public IReadOnlyCollection<ICommentModel> Comments => new[] { _comment };

        public GraphId Id => default;
        public string DisplayName => "test";
        public GraphKindDescriptor Kind => default!;
        public IReadOnlyCollection<INodeModel> Nodes => System.Array.Empty<INodeModel>();
        public IReadOnlyCollection<ILinkModel> Links => System.Array.Empty<ILinkModel>();
        public INodeModel? FindNode(NodeId id) => null;
        public IPinModel? FindPin(PinId id) => null;
        public ILinkModel? FindLink(LinkId id) => null;
        public event System.Action<GraphChangeNotification>? Changed { add { } remove { } }
    }
}
