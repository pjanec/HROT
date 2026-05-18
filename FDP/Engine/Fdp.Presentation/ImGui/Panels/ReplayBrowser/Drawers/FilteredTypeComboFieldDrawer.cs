using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Core;
using Fdp.Presentation.Editing;
using ImGuiNET;
using StructEdit.Core;

namespace Fdp.Presentation.Panels.ReplayBrowser.Drawers;

using ImGuiApi = ImGuiNET.ImGui;

/// <summary>
/// Modes that control which type list populates the combo.
/// </summary>
internal enum TypeComboMode { Component, Event }

/// <summary>
/// Custom <see cref="IImGuiFieldDrawer"/> for <c>Type</c> fields.
/// Shows a filterable combo of component or event types.
/// </summary>
internal sealed class FilteredTypeComboFieldDrawer : IImGuiFieldDrawer
{
    private readonly TypeComboMode _mode;
    private IReadOnlyList<Type>? _cachedTypes;
    private string _filter = string.Empty;

    public FilteredTypeComboFieldDrawer(TypeComboMode mode)
    {
        _mode = mode;
    }

    public Type TargetType => typeof(Type);

    /// <summary>
    /// Filters <paramref name="types"/> by name containing <paramref name="filter"/>
    /// (OrdinalIgnoreCase). Returns all when filter is empty or null.
    /// Exposed internal for unit testing.
    /// </summary>
    internal static IEnumerable<Type> FilterTypes(IEnumerable<Type> types, string? filter)
    {
        if (string.IsNullOrEmpty(filter))
            return types;
        return types.Where(
            t => t.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    public bool DrawInput(ref object value, EditNodeMetadata meta)
    {
        _cachedTypes ??= LoadTypes();

        Type? current = value as Type;
        string currentName = current?.Name ?? "(none)";
        bool changed = false;

        if (ImGuiApi.BeginCombo("##typecombo", currentName))
        {
            ImGuiApi.InputTextWithHint("##typefilter", "Filter...", ref _filter, 128);

            foreach (var t in FilterTypes(_cachedTypes, _filter))
            {
                bool selected = t == current;
                if (selected)
                    ImGuiApi.SetItemDefaultFocus();

                if (ImGuiApi.Selectable(t.Name, selected))
                {
                    value   = t;
                    changed = true;
                }
            }
            ImGuiApi.EndCombo();
        }
        return changed;
    }

    private IReadOnlyList<Type> LoadTypes()
    {
        return _mode == TypeComboMode.Event
            ? EventType.GetAllRegistered().ToList()
            : ComponentTypeRegistry.GetAllRegistered().ToList();
    }
}
