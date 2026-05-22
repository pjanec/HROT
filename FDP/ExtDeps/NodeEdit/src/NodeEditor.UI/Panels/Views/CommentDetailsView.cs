using ImGuiNET;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using System.Numerics;

namespace NodeEditor.UI.Panels.Views;

/// <summary>
/// Built-in Details view for a <see cref="DetailsTarget.Comment"/> target.
/// Renders editable fields for text, color, move-with-contents, position, and size.
/// Commits changes via <see cref="GraphCommand.UpdateComment"/>.
/// </summary>
internal sealed class CommentDetailsView : IDetailsView
{
    private readonly CommentId _commentId;
    private readonly IDetailsContext _ctx;

    // Editable fields (mirrored from comment model; dirty-tracked locally).
    private string _text = "";
    private Vector4 _color = new(1f, 1f, 0f, 1f);
    private bool _moveWithContents;
    private Vector2 _position;
    private Vector2 _size;

    private bool _isDirty;

    /// <summary>Construct for the given comment.</summary>
    public CommentDetailsView(CommentId commentId, IDetailsContext ctx)
    {
        _commentId = commentId;
        _ctx       = ctx;
    }

    /// <inheritdoc/>
    public bool IsDirty => _isDirty;

    /// <inheritdoc/>
    public void Commit()
    {
        if (!_isDirty) return;

        _ctx.CommandSink.Apply(new GraphCommand.UpdateComment(
            _commentId,
            Text: _text,
            Position: _position,
            Size: _size,
            Color: _color,
            ZOrder: null,
            MoveWithContents: _moveWithContents));

        _isDirty = false;
    }

    /// <inheritdoc/>
    public void Revert()
    {
        _isDirty = false;
        // Re-load from model not possible here (no IGraphModel reference).
        // The host will rebuild the view on the next target switch.
    }

    /// <inheritdoc/>
    public void Draw(IDetailsRenderContext ctx)
    {
        ImGui.PushItemWidth(-140f);

        // Text.
        ImGui.Text("Text:");
        ImGui.SameLine(140f);
        string textBuf = _text;
        if (ImGui.InputTextMultiline("##cmt_text", ref textBuf, 1024,
                new Vector2(-1f, 60f), ImGuiInputTextFlags.EnterReturnsTrue))
        {
            _text = textBuf;
            MarkDirtyAndCommit();
        }
        // Also commit on focus loss.
        if (ImGui.IsItemDeactivatedAfterEdit())
        {
            _text = textBuf;
            MarkDirtyAndCommit();
        }

        // Color.
        ImGui.Text("Color:");
        ImGui.SameLine(140f);
        if (ImGui.ColorEdit4("##cmt_color", ref _color,
                ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar))
        {
            MarkDirtyAndCommit();
        }

        // Move with contents.
        ImGui.Text("Move With Contents:");
        ImGui.SameLine(140f);
        bool mwc = _moveWithContents;
        if (ImGui.Checkbox("##cmt_mwc", ref mwc))
        {
            _moveWithContents = mwc;
            MarkDirtyAndCommit();
        }

        // Position.
        ImGui.Text("Position:");
        ImGui.SameLine(140f);
        if (ImGui.DragFloat2("##cmt_pos", ref _position, 0.5f))
        {
            _isDirty = true;
        }
        if (ImGui.IsItemDeactivatedAfterEdit()) Commit();

        // Size.
        ImGui.Text("Size:");
        ImGui.SameLine(140f);
        if (ImGui.DragFloat2("##cmt_size", ref _size, 0.5f, 64f, 4096f))
        {
            _isDirty = true;
        }
        if (ImGui.IsItemDeactivatedAfterEdit()) Commit();

        ImGui.PopItemWidth();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private void MarkDirtyAndCommit()
    {
        _isDirty = true;
        Commit();
    }
}
