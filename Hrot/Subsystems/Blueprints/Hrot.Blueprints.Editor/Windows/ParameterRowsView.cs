using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Editor.Variables;

namespace Hrot.Blueprints.Editor.Windows;

/// <summary>
/// BP-89: the parameter-rows table (Name text-input + Type combo + Remove button, plus a
/// trailing "+ Add" row) shared by <see cref="GraphSignatureWindow"/> (Inputs/Outputs tables in
/// the standalone Graph Signature window) and <see cref="Hrot.Blueprints.Editor.NodeDrawers.ReturnNodeDrawer"/>
/// (the Outputs table on the Return node's Details panel, added to close the "where do I add
/// function outputs?" affordance gap). Extracted verbatim out of
/// <see cref="GraphSignatureWindow"/> so the two call sites render byte-identical UI instead of
/// drifting apart as two hand-maintained copies.
/// </summary>
public static class ParameterRowsView
{
    /// <summary>
    /// Renders an editable table of parameter rows (Name text-input + Type combo +
    /// Remove button) and a trailing "+ Add" row.  All ImGui calls are local to
    /// this method; mutations are routed through <paramref name="model"/>.
    /// </summary>
    public static void Draw(
        string                          tableId,
        IReadOnlyList<ParameterDecl>    parameters,
        GraphSignatureEditModel         model)
    {
        const int    NameBufLen  = 256;
        const float  RemoveWidth = 24f;

        string? toRemove = null;

        if (ImGuiNET.ImGui.BeginTable(tableId, 3,
            ImGuiNET.ImGuiTableFlags.BordersInnerV | ImGuiNET.ImGuiTableFlags.SizingStretchProp))
        {
            ImGuiNET.ImGui.TableSetupColumn("Name",   ImGuiNET.ImGuiTableColumnFlags.WidthStretch,  1.5f);
            ImGuiNET.ImGui.TableSetupColumn("Type",   ImGuiNET.ImGuiTableColumnFlags.WidthStretch,  1.0f);
            ImGuiNET.ImGui.TableSetupColumn("##del",  ImGuiNET.ImGuiTableColumnFlags.WidthFixed,    RemoveWidth);
            ImGuiNET.ImGui.TableHeadersRow();

            for (int i = 0; i < parameters.Count; i++)
            {
                var param = parameters[i];
                ImGuiNET.ImGui.TableNextRow();

                // ── Name column ───────────────────────────────────────────────
                ImGuiNET.ImGui.TableSetColumnIndex(0);
                var nameBuf = System.Text.Encoding.UTF8.GetBytes(param.Name + "\0");
                Array.Resize(ref nameBuf, NameBufLen);
                ImGuiNET.ImGui.PushID($"name_{tableId}_{i}");
                if (ImGuiNET.ImGui.InputText("##n", nameBuf, (uint)nameBuf.Length))
                {
                    var newName = Fdp.Presentation.Utils.ImGuiBufferText.Decode(nameBuf);
                    if (newName != param.Name)
                        model.RenameParameter(param.Name, newName);
                }
                ImGuiNET.ImGui.PopID();

                // ── Type column ───────────────────────────────────────────────
                ImGuiNET.ImGui.TableSetColumnIndex(1);
                var typeNames  = BlueprintTypeChoices.TypeIds;
                var typeId     = param.Type?.TypeId ?? "";
                var currentIdx = Enumerable.Range(0, typeNames.Count)
                    .FirstOrDefault(j => typeNames[j] == typeId, -1);
                if (currentIdx < 0) currentIdx = 0;

                ImGuiNET.ImGui.PushID($"type_{tableId}_{i}");
                if (ImGuiNET.ImGui.Combo("##t", ref currentIdx,
                    typeNames.ToArray(), typeNames.Count))
                {
                    model.RetypeParameter(param.Name, typeNames[currentIdx]);
                }
                ImGuiNET.ImGui.PopID();

                // ── Remove column ─────────────────────────────────────────────
                ImGuiNET.ImGui.TableSetColumnIndex(2);
                ImGuiNET.ImGui.PushID($"del_{tableId}_{i}");
                if (ImGuiNET.ImGui.SmallButton("X"))
                    toRemove = param.Name;
                ImGuiNET.ImGui.PopID();
            }

            ImGuiNET.ImGui.EndTable();
        }

        // Apply pending removal after iterating (avoid modifying list mid-loop).
        if (toRemove != null)
            model.RemoveParameter(toRemove);

        // ── "+ Add" row ───────────────────────────────────────────────────────
        if (ImGuiNET.ImGui.Button($"+##{tableId}_add"))
        {
            var defaultType = BlueprintTypeChoices.DefaultTypeId;
            model.AddParameter($"Param{model.Parameters.Count}", defaultType);
        }
    }
}
