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
    private readonly PickerField _field;

    /// <param name="names">The host's registered behaviour names (<c>BehaviorRegistry.GetRegisteredNames</c>).</param>
    /// <param name="field">⭐ <c>CE-1008</c> (§7.1b) — the searchable choice field; null ⇒ a plain combo.</param>
    public AiBehaviorPickerDrawer(Func<IEnumerable<string>> names, PickerField? field = null)
    {
        _names = names ?? throw new ArgumentNullException(nameof(names));
        _field = field ?? new PickerField(null);
    }

    /// <summary>⭐ CE-1008 — the choice field (the forwarding rail reads it).</summary>
    public PickerField Field => _field;

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
        if (_field.Draw("aibehaviourpicker", node, current, PickerChoices.Flat(items), "Behaviour", out var picked))   // ⭐ CE-1008
        {
            value   = picked ?? string.Empty;
            changed = true;
        }
        if (!string.IsNullOrEmpty(current) && !items.Contains(current, StringComparer.Ordinal))
            ImGuiNET.ImGui.TextDisabled($"⚠ '{current}' is not a registered behaviour");
        return changed;
    }
}
