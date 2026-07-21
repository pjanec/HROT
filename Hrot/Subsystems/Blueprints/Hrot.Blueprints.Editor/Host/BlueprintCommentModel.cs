using System.Numerics;
using Hrot.Blueprints.Core.Assets;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// Read-only <see cref="ICommentModel"/> adapter projecting a
/// <see cref="Hrot.Blueprints.Core.Assets.GraphComment"/> onto the NodeEdit canvas contract.
/// Mirrors <see cref="BlueprintLinkModel"/>'s role for links.
/// </summary>
internal sealed class BlueprintCommentModel : ICommentModel
{
    public CommentId Id               { get; }
    public string     Text            { get; }
    public Vector2    Position        { get; }
    public Vector2    Size            { get; }
    public Vector4    Color           { get; }
    public int        ZOrder          { get; }
    public bool       MoveWithContents { get; }

    public BlueprintCommentModel(GraphComment comment)
    {
        Id               = new CommentId(comment.Id);
        Text             = comment.Text;
        Position         = new Vector2(comment.X, comment.Y);
        Size             = new Vector2(comment.W, comment.H);
        Color            = new Vector4(comment.ColorR, comment.ColorG, comment.ColorB, comment.ColorA);
        ZOrder           = comment.ZOrder;
        MoveWithContents = comment.MoveWithContents;
    }
}
