using ImGuiNET;
using NodeEditor.Core.Interfaces;

namespace NodeEditor.UI.MiniEditors;

/// <summary>
/// Inline editor for <c>Array&lt;T&gt;</c> pins. Shows a button with the element
/// count that opens a popup with per-element editors plus add/remove controls.
/// </summary>
public sealed class ArrayPinEditor : IPinDefaultValueEditor
{
    /// <inheritdoc/>
    public bool Draw(ref object? value, DefaultEditorContext ctx, out bool committed)
    {
        committed = false;

        var list = value as IList<object?> ?? [];
        string label = $"[{list.Count} items] ▾";

        if (ImGui.Button(label, new System.Numerics.Vector2(ctx.MaxWidth, 0)))
            ImGui.OpenPopup("##array_popup");

        bool changed = false;

        if (ImGui.BeginPopup("##array_popup"))
        {
            ImGui.Text($"Array [{list.Count}]");
            ImGui.Separator();

            // The concrete IList<object?> we will mutate
            var mutableList = new List<object?>(list);
            bool listChanged = false;

            for (int idx = 0; idx < mutableList.Count; idx++)
            {
                using var scope = new Util.ImGuiPushIdScope(idx);
                ImGui.Text($"[{idx}]");
                ImGui.SameLine();
                // Element editing: show a simple text input as fallback.
                string elem = mutableList[idx]?.ToString() ?? "";
                if (ImGui.InputText("##elem", ref elem, 256, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    mutableList[idx] = elem;
                    listChanged = true;
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("×"))
                {
                    mutableList.RemoveAt(idx);
                    listChanged = true;
                    idx--;
                }
            }

            if (ImGui.Button("+ Add"))
            {
                mutableList.Add(null);
                listChanged = true;
            }

            if (listChanged)
            {
                value = mutableList;
                changed = true;
                committed = true;
            }

            ImGui.EndPopup();
        }

        return changed;
    }
}
