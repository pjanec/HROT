using System;
using NodeEditor.Core.Commands;
using NodeEditor.Core.View;

namespace Hrot.Editor.AiShared.Documents;

/// <summary>
/// ⭐ <c>CE-1062</c> (<c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §13.10c E4) — "run this apply / run that undo" on an AI
/// document's ONE undo stack (<see cref="GraphView.Undo"/>): an ASSET-level edit no built-in <see cref="GraphCommand"/> can describe
/// (the params DTO, its defaults). Every AI sink (BTree, HSM, Blueprint) has an explicit arm for it.
/// <para>⭐ The blueprint's <c>BlueprintEditCommand</c> (BP-11) is a subtype — one concept, one family. ⚠ The blueprint sink's
/// <c>default:</c> arm reports SUCCESS for an unknown command, so a missing arm would make an undo that silently does nothing.</para>
/// </summary>
/// <param name="Label">The undo entry's label.</param>
/// <param name="Mutate">ONE direction; the caller builds a forward and an inverse (<see cref="AssetEdits.Run"/>).</param>
public record AssetEditCommand(string Label, Action Mutate) : GraphCommand;

/// <summary>⭐ <c>CE-1062</c> — runs an asset edit as one undo entry on a document's undo stack (<see cref="GraphView.Undo"/> — what
/// <see cref="GraphView.Execute"/> records on); with no stack (a headless host) it applies directly and records nothing.</summary>
public static class AssetEdits
{
    /// <returns>True when the edit went onto an undo stack.</returns>
    public static bool Run(UndoStack? history, string label, Action apply, Action undo)
    {
        if (apply is null) throw new ArgumentNullException(nameof(apply));
        if (undo is null) throw new ArgumentNullException(nameof(undo));
        if (history is null) { apply(); return false; }
        return history.ApplyAndRecord(new AssetEditCommand(label, apply), new AssetEditCommand(label, undo), label).Success;
    }
}
