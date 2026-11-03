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
/// Modes that control which type list populates the combo. ⭐ CE-4220: <see cref="Auto"/> = by the FIELD (a member named <c>EventType</c> lists the event types, every other <c>Type</c> field the component types) — the rule editor's form holds
/// both kinds of predicate in one tree, so no single mode fits.
/// </summary>
public enum TypeComboMode { Component, Event, Auto }

/// <summary>
/// Custom <see cref="IImGuiFieldDrawer"/> for <c>Type</c> fields.
/// Shows a filterable combo of component or event types.
/// </summary>
public sealed class FilteredTypeComboFieldDrawer : IImGuiFieldDrawer
{
    private readonly TypeComboMode _mode;
    private IReadOnlyList<Type>? _componentTypes, _eventTypes;
    private string _filter = string.Empty;

    public FilteredTypeComboFieldDrawer(TypeComboMode mode = TypeComboMode.Auto)
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

    public bool DrawInput(ref object value, EditNode node)
    {
        var types = TypesFor(node);

        Type? current = value as Type;
        string currentName = current?.Name ?? "(none)";
        bool changed = false;

        if (ImGuiApi.BeginCombo("##typecombo", currentName))
        {
            ImGuiApi.InputTextWithHint("##typefilter", "Filter...", ref _filter, 128);

            foreach (var t in FilterTypes(types, _filter))
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

    /// <summary>The list this field offers: the event types for an event field, else the COMPONENT types — ⭐ the very list the Replay search shows (<c>ComponentTypeRegistry</c>), one source.</summary>
    internal IReadOnlyList<Type> TypesFor(EditNode node)
    {
        bool events = _mode == TypeComboMode.Event || (_mode == TypeComboMode.Auto && node.Name == "EventType");
        return events
            ? _eventTypes ??= EventType.GetAllRegistered().OrderBy(t => t.Name).ToList()
            : _componentTypes ??= ComponentTypeRegistry.GetAllRegistered().OrderBy(t => t.Name).ToList();
    }
}
