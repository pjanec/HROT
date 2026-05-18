using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Presentation.Editing;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.ReplayBrowser.Search;
using ImGuiNET;
using StructEdit.Core;

namespace Fdp.Presentation.Panels.ReplayBrowser.Drawers;

using ImGuiApi = ImGuiNET.ImGui;

/// <summary>
/// Custom <see cref="IImGuiFieldDrawer"/> for <c>int</c> fields decorated with
/// <see cref="BehaviorHashPickerAttribute"/>. Shows a filterable combo of registered
/// behavior names and maps the selection back to its stable integer ID.
/// Falls back to <c>InputInt</c> for int fields without the attribute.
/// </summary>
internal sealed class BehaviorHashFieldDrawer : IImGuiFieldDrawer
{
    private readonly BehaviorRegistry _registry;
    private IReadOnlyList<string>? _cachedNames;
    private string _filter = string.Empty;

    public BehaviorHashFieldDrawer(BehaviorRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public Type TargetType => typeof(int);

    public bool DrawInput(ref object value, EditNodeMetadata meta)
    {
        bool hasPicker = meta.CustomAttributes.Any(a => a is BehaviorHashPickerAttribute);
        if (!hasPicker)
        {
            int v = value is int i ? i : 0;
            bool ok = ImGuiApi.InputInt("##bhv", ref v);
            if (ok) value = v;
            return ok;
        }

        _cachedNames ??= _registry.GetRegisteredNames();
        int current = value is int hash ? hash : 0;

        // Find display name for the current hash.
        string currentName = _cachedNames.FirstOrDefault(
            n => _registry.TryGetId(n, out int id) && id == current) ?? current.ToString();

        bool changed = false;
        if (ImGuiApi.BeginCombo("##bhvcombo", currentName))
        {
            ImGuiApi.InputTextWithHint("##bhvfilter", "Filter...", ref _filter, 128);

            foreach (var name in _cachedNames)
            {
                if (_filter.Length > 0 &&
                    name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                bool selected = string.Equals(name, currentName, StringComparison.Ordinal);
                if (selected)
                    ImGuiApi.SetItemDefaultFocus();

                if (ImGuiApi.Selectable(name, selected))
                {
                    if (_registry.TryGetId(name, out int newId))
                    {
                        value   = newId;
                        changed = true;
                    }
                }
            }
            ImGuiApi.EndCombo();
        }
        return changed;
    }
}
