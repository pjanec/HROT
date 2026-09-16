using System;
using Hrot.Blueprints.Editor.Variables;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// BP-80 / BP-225 — the exec-declaration rows table for a macro graph: Name + a wire count + Remove,
/// plus a trailing "+ Add".
///
/// <para>
/// ⭐ <b>Why not <see cref="ParameterRowsView"/> with the Type column suppressed.</b> Two reasons,
/// and the second is the load-bearing one:
/// </para>
///
/// <list type="number">
///   <item><see cref="ExecInDecl"/>/<see cref="ExecOutDecl"/> have no <c>Type</c>, so a shared widget
///     would carry a mode flag through every row.</item>
///   <item>⭐ <b>The commit semantics have to differ.</b> <see cref="ParameterRowsView"/> renames on
///     <b>every keystroke</b> — <c>InputText</c> returns true per edit and it calls
///     <c>RenameParameter</c> straight away. For a parameter that is merely noisy. For an exec
///     declaration it is destructive: a rename moves a pin, so typing <c>"Start"</c> over
///     <c>"Alpha"</c> would perform five pin migrations and leave four one-character declarations in
///     the undo stack. This view commits on <c>IsItemDeactivatedAfterEdit</c> — one rename, one undo
///     entry, one wire migration.</item>
/// </list>
///
/// <para>
/// ⚠ Reordering is offered because it is <b>safe</b> — pins are name-keyed and both projections
/// derive their order from this one list. See <c>MacroExecPinMaintenance</c> for the argument and
/// <c>ExecDeclarationEditTests</c> for the proof.
/// </para>
/// </summary>
public static class ExecRowsView
{
    /// <summary>
    /// Renders the rows. All ImGui calls are local to this method; every mutation goes through
    /// <paramref name="model"/>.
    /// </summary>
    /// <param name="addPrefix">Default name stem for the "+" button — <c>"Entry"</c> or <c>"Exit"</c>.</param>
    public static void Draw(string tableId, ExecSignatureEditModel model, string addPrefix)
    {
        ArgumentNullException.ThrowIfNull(model);

        const int   NameBufLen = 256;
        const float ButtonWidth = 24f;

        string? toRemove = null;
        var declarations = model.Declarations;

        if (ImGuiNET.ImGui.BeginTable(tableId, 4,
            ImGuiNET.ImGuiTableFlags.BordersInnerV | ImGuiNET.ImGuiTableFlags.SizingStretchProp))
        {
            ImGuiNET.ImGui.TableSetupColumn("Name",   ImGuiNET.ImGuiTableColumnFlags.WidthStretch, 2.0f);
            ImGuiNET.ImGui.TableSetupColumn("Wires",  ImGuiNET.ImGuiTableColumnFlags.WidthFixed,   44f);
            ImGuiNET.ImGui.TableSetupColumn("##move", ImGuiNET.ImGuiTableColumnFlags.WidthFixed,   ButtonWidth * 2);
            ImGuiNET.ImGui.TableSetupColumn("##del",  ImGuiNET.ImGuiTableColumnFlags.WidthFixed,   ButtonWidth);
            ImGuiNET.ImGui.TableHeadersRow();

            for (int i = 0; i < declarations.Count; i++)
            {
                var decl = declarations[i];
                ImGuiNET.ImGui.TableNextRow();

                // ── Name ──────────────────────────────────────────────────────
                ImGuiNET.ImGui.TableSetColumnIndex(0);
                var nameBuf = System.Text.Encoding.UTF8.GetBytes(decl.Name + "\0");
                Array.Resize(ref nameBuf, NameBufLen);
                ImGuiNET.ImGui.PushID($"execname_{tableId}_{i}");
                ImGuiNET.ImGui.InputText("##n", nameBuf, (uint)nameBuf.Length);
                // ⭐ Commit on deactivate, NOT per keystroke — see the class docs. A rename here
                // migrates a pin and its wires; doing that per character would be a stream of
                // destructive edits with one undo entry each.
                if (ImGuiNET.ImGui.IsItemDeactivatedAfterEdit())
                {
                    var newName = Fdp.Presentation.Utils.ImGuiBufferText.Decode(nameBuf);
                    if (newName != decl.Name && !string.IsNullOrWhiteSpace(newName))
                        model.RenameDeclaration(decl.Name, newName);   // refuses a duplicate
                }
                ImGuiNET.ImGui.PopID();

                // ── Wire count — the cost of removing this row, stated up front ──
                ImGuiNET.ImGui.TableSetColumnIndex(1);
                var wires = model.WireCount(decl.Name);
                if (wires > 0) ImGuiNET.ImGui.TextUnformatted(wires.ToString());
                else           ImGuiNET.ImGui.TextDisabled("—");

                // ── Reorder ───────────────────────────────────────────────────
                ImGuiNET.ImGui.TableSetColumnIndex(2);
                ImGuiNET.ImGui.PushID($"execmove_{tableId}_{i}");
                if (ImGuiNET.ImGui.SmallButton("^") && i > 0)
                    model.MoveDeclaration(i, i - 1);
                ImGuiNET.ImGui.SameLine();
                if (ImGuiNET.ImGui.SmallButton("v") && i < declarations.Count - 1)
                    model.MoveDeclaration(i, i + 1);
                ImGuiNET.ImGui.PopID();

                // ── Remove ────────────────────────────────────────────────────
                ImGuiNET.ImGui.TableSetColumnIndex(3);
                ImGuiNET.ImGui.PushID($"execdel_{tableId}_{i}");
                if (ImGuiNET.ImGui.SmallButton("X"))
                    toRemove = decl.Name;
                if (wires > 0 && ImGuiNET.ImGui.IsItemHovered())
                    ImGuiNET.ImGui.SetTooltip(
                        $"Removes this {addPrefix.ToLowerInvariant()} and the {wires} wire(s) on it. Undoable.");
                ImGuiNET.ImGui.PopID();
            }

            ImGuiNET.ImGui.EndTable();
        }

        // Applied after iterating — never mutate the list mid-loop.
        if (toRemove != null)
            model.RemoveDeclaration(toRemove);

        if (ImGuiNET.ImGui.Button($"+##{tableId}_add"))
        {
            // Counts up until free: AddDeclaration refuses a duplicate, and silently doing nothing
            // when "Entry1" happens to exist would read as a dead button.
            for (int n = model.Count; n < model.Count + 64; n++)
                if (model.AddDeclaration($"{addPrefix}{n}"))
                    break;
        }
    }
}
