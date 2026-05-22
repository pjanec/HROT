using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.UI.MiniEditors;

/// <summary>
/// Inline editor for enum pins. Uses an ImGui Combo populated from an
/// optional <see cref="IEnumValueProvider"/>. Without a provider, falls back
/// to a DragInt showing the raw integer value.
/// </summary>
public sealed class EnumPinEditor : IPinDefaultValueEditor
{
    private readonly IEnumValueProvider? _provider;

    /// <param name="provider">
    /// Host-supplied enum value source. Pass <see langword="null"/> when registering
    /// a generic fallback; hosts should re-register with a real provider per enum type.
    /// </param>
    public EnumPinEditor(IEnumValueProvider? provider = null)
    {
        _provider = provider;
    }

    /// <inheritdoc/>
    public bool Draw(ref object? value, DefaultEditorContext ctx, out bool committed)
    {
        committed = false;
        long rawValue = value is long l ? l : value is int i ? i : 0L;

        if (_provider == null)
        {
            // No provider — show raw int
            int raw = (int)rawValue;
            ImGui.PushItemWidth(ctx.MaxWidth);
            bool changed = ImGui.DragInt("##enum_raw", ref raw, 1f);
            ImGui.PopItemWidth();
            if (changed)
            {
                value = (long)raw;
                committed = ImGui.IsItemDeactivated();
                return true;
            }
            return false;
        }

        var entries = _provider.GetValues(ctx.Type);
        if (entries.Count == 0)
            return false;

        // Find current selection index
        int selectedIdx = 0;
        for (int k = 0; k < entries.Count; k++)
        {
            if (entries[k].Value == rawValue) { selectedIdx = k; break; }
        }

        string[] names = new string[entries.Count];
        for (int k = 0; k < entries.Count; k++) names[k] = entries[k].DisplayName;

        ImGui.PushItemWidth(ctx.MaxWidth);
        bool picked = ImGui.Combo("##enum", ref selectedIdx, names, names.Length);
        ImGui.PopItemWidth();

        if (picked)
        {
            value = entries[selectedIdx].Value;
            committed = true;
            return true;
        }

        return false;
    }
}
