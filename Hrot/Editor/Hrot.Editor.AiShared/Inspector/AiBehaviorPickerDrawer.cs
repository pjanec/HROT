using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Editing;
using StructEdit.Core;

namespace Hrot.Editor.AiShared.Inspector;

/// <summary>
/// ⭐ <c>CE-2079</c> — the combo for <see cref="AiBehaviorPickerAttribute"/>: the host's registered behaviour names, the same
/// list the blueprint Behaviour Task offers (<c>BehaviorTaskNodeDrawer</c>). ⚠ A stored name not in the list is KEPT and
/// flagged, never dropped — a behaviour may register later (a child authored but not yet built).
/// </summary>
public sealed class AiBehaviorPickerDrawer : IImGuiFieldDrawer, IPickerListSource
{
    private readonly Func<IEnumerable<string>> _names;

    /// <param name="names">The host's registered behaviour names (<c>BehaviorRegistry.GetRegisteredNames</c>).</param>
    public AiBehaviorPickerDrawer(Func<IEnumerable<string>> names)
        => _names = names ?? throw new ArgumentNullException(nameof(names));

    public Type TargetType => typeof(string);

    public IReadOnlyList<string> GetItems()
        => _names().Where(n => !string.IsNullOrWhiteSpace(n))
                   .Distinct(StringComparer.Ordinal)
                   .OrderBy(n => n, StringComparer.Ordinal)
                   .ToList();

    public bool DrawInput(ref object value, EditNode node)
    {
        if (ImGuiNET.ImGui.GetCurrentContext() == IntPtr.Zero) return false;

        var current = value as string ?? string.Empty;
        var items   = GetItems();
        bool changed = false;
        if (ImGuiNET.ImGui.BeginCombo("##aibehaviourpicker", current))
        {
            bool noneSelected = string.IsNullOrEmpty(current);
            if (ImGuiNET.ImGui.Selectable("(none)", noneSelected) && !noneSelected)
            {
                value   = string.Empty;
                changed = true;
            }
            foreach (var name in items)
            {
                bool selected = string.Equals(name, current, StringComparison.Ordinal);
                if (ImGuiNET.ImGui.Selectable(name, selected) && !selected)
                {
                    value   = name;
                    changed = true;
                }
                if (selected) ImGuiNET.ImGui.SetItemDefaultFocus();
            }
            ImGuiNET.ImGui.EndCombo();
        }
        if (!string.IsNullOrEmpty(current) && !items.Contains(current, StringComparer.Ordinal))
            ImGuiNET.ImGui.TextDisabled($"⚠ '{current}' is not a registered behaviour");
        return changed;
    }
}
