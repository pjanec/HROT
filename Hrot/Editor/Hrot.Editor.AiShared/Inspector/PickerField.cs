using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Picker;
using StructEdit.Core;

namespace Hrot.Editor.AiShared.Inspector;

/// <summary>⭐ <c>CE-1008</c> — one thing a choice field offers: the stored value, how it is shown, and where it sits in the picker's tree.</summary>
public sealed record PickerChoice(string Value, string Label, string? Category = null, IReadOnlyList<string>? Keywords = null,
                                  string? Description = null);

/// <summary>⭐ <c>CE-1008</c> (docs/blueprints/AI_Editor_Shared_Infrastructure.md §7.1b P-4) — the shapes a list of values takes in the picker.</summary>
public static class PickerChoices
{
    /// <summary>A flat list: each value shown by <paramref name="label"/> (else itself).</summary>
    public static IReadOnlyList<PickerChoice> Flat(IEnumerable<string> values, Func<string, string>? label = null)
        => values.Select(v => new PickerChoice(v, label?.Invoke(v) ?? v)).ToList();

    /// <summary>
    /// ⭐ P-4 — C# methods as an FQN TREE: <c>Hrot.AI.Doors.OpenDoor</c> sits in the folder of its namespace and type (<c>Hrot/AI/Doors</c>),
    /// named <c>OpenDoor</c>, its full FQN a keyword (so typing any fragment finds it) and <paramref name="label"/> (the C# / Blueprint tag)
    /// its description. A value with no dot has no folder.
    /// </summary>
    public static IReadOnlyList<PickerChoice> Methods(IEnumerable<string> fqns, Func<string, string>? label = null)
        => fqns.Select(fqn =>
        {
            int dot = fqn.LastIndexOf('.');
            return dot <= 0 || dot == fqn.Length - 1
                ? new PickerChoice(fqn, fqn, null, new[] { fqn }, label?.Invoke(fqn))
                : new PickerChoice(fqn, fqn[(dot + 1)..], fqn[..dot].Replace('.', '/'), new[] { fqn }, label?.Invoke(fqn));
        }).ToList();
}

/// <summary>
/// ⭐⭐ <c>CE-1008</c> (docs/blueprints/AI_Editor_Shared_Infrastructure.md §7.1b) — THE choice field of the AI inspectors: a button that
/// opens the ONE searchable picker (<see cref="PickerWindow"/>, through the host's shell <see cref="PickerRegistry"/>), and takes the
/// pick on the drawer's NEXT frame (the picker answers by callback, later). 🔒 User: <i>"want the general picker window used for opening
/// assets, with incremental search; the C# method picker ideally as an FQN-segment tree."</i>
/// <para>⭐ P-2 — a pick is held per <see cref="EditNode"/> object and field id: an <c>EditSession</c> keeps its nodes for one selection
/// and replaces them on the next, so a pick finished after the operator selected another node is never written to it.</para>
/// <para>⭐ P-3 — with no opener (a test, a lightweight host) it draws the plain combo every field drew before; both production hosts
/// pass one (<c>AiFacetPickerServices.OpenPicker</c>).</para>
/// </summary>
public sealed class PickerField
{
    /// <summary>The picker entry that clears the field.</summary>
    public const string NoneId = "\u0001(none)";

    private readonly Action<PickerRequest, Action<PickerResult>>? _open;
    private readonly ConditionalWeakTable<EditNode, Dictionary<string, string>> _pending = new();

    /// <param name="open">The host's <see cref="PickerRegistry.OpenPicker"/>; null ⇒ plain combos.</param>
    public PickerField(Action<PickerRequest, Action<PickerResult>>? open) => _open = open;

    /// <summary>True when a field opens the searchable picker (the host passed one).</summary>
    public bool CanSearch => _open != null;

    /// <summary>
    /// Draws the field: a button with the current choice's label that opens the picker (or, with no picker, a combo). True when a choice
    /// landed this frame; <paramref name="picked"/> is its value, empty for "(none)". ⭐ <c>CE-1011</c> — <paramref name="context"/>, when
    /// given, is drawn as the field's RIGHT-CLICK menu (it runs between <c>BeginPopupContextItem</c> and <c>EndPopup</c>).
    /// </summary>
    public bool Draw(string id, EditNode? node, string? current, IReadOnlyList<PickerChoice> choices, string title, out string? picked,
                     Action? context = null)
    {
        picked = null;
        current ??= string.Empty;
        if (_open == null || node == null)
        {
            bool combo = Combo(id, current, choices, out picked);
            ContextMenu(id, context);
            return combo;
        }
        if (TryConsume(node, id, out var value))
        {
            picked = value;
            return value != current;
        }
        string shown = current.Length == 0 ? "(none)" : choices.FirstOrDefault(c => c.Value == current)?.Label ?? current;
        if (ImGuiNET.ImGui.Button($"{shown}  ▾##{id}", new System.Numerics.Vector2(ImGuiNET.ImGui.CalcItemWidth(), 0f)))
            Open(id, node, current, choices, title);
        if (ImGuiNET.ImGui.IsItemHovered()) ImGuiNET.ImGui.SetTooltip(current.Length == 0 ? "click to choose (type to search)" : current);
        ContextMenu(id, context);
        if (VisibleLabel(id) is { Length: > 0 } text) { ImGuiNET.ImGui.SameLine(); ImGuiNET.ImGui.TextUnformatted(text); }   // as the combo labelled it
        return false;
    }

    private static void ContextMenu(string id, Action? context)
    {
        if (context == null || !ImGuiNET.ImGui.BeginPopupContextItem($"##ctx-{id}")) return;
        context();
        ImGuiNET.ImGui.EndPopup();
    }

    /// <summary>Opens the picker for field <paramref name="id"/> of <paramref name="node"/> (the button's click; public for the rails).</summary>
    public void Open(string id, EditNode node, string current, IReadOnlyList<PickerChoice> choices, string title)
    {
        if (_open == null) return;
        var entries = new List<PickerEntry>(choices.Count + 1) { new(NoneId, "(none)", "clear this field", null, null, null, null) };
        foreach (var c in choices)
            entries.Add(new PickerEntry(c.Value, c.Label, c.Description, c.Category, c.Keywords ?? new[] { c.Value }, null, null));
        bool tree = choices.Any(c => c.Category != null);
        _open(new PickerRequest
        {
            ContextKey = "ai-inspector." + id,
            Title = title,
            ItemsProvider = () => entries,
            InitialSelectionId = current.Length == 0 ? NoneId : current,
            Layout = tree ? PickerLayout.Tree : PickerLayout.Standard,
            FoldSingleChildFolders = tree,
        }, result =>
        {
            if (result.Cancelled || result.First is not { } chosen) return;
            _pending.GetOrCreateValue(node)[id] = chosen.Id == NoneId ? string.Empty : chosen.Id;
        });
    }

    /// <summary>The pick waiting for field <paramref name="id"/> of <paramref name="node"/>, removed as it is taken.</summary>
    public bool TryConsume(EditNode node, string id, out string value)
    {
        value = string.Empty;
        if (!_pending.TryGetValue(node, out var fields) || !fields.Remove(id, out var v)) return false;
        value = v;
        return true;
    }

    /// <summary>The visible part of an ImGui id (<c>"method##abm"</c> ⇒ <c>method</c>).</summary>
    public static string VisibleLabel(string id) { int i = id.IndexOf("##", StringComparison.Ordinal); return i < 0 ? string.Empty : id[..i]; }

    /// <summary>The plain combo (no picker): a "(none)" row, then every choice; each row's ImGui id is its VALUE so equal labels stay distinct.</summary>
    public static bool Combo(string id, string? current, IReadOnlyList<PickerChoice> choices, out string? picked)
    {
        picked = null;
        current ??= string.Empty;
        bool changed = false;
        string shown = current.Length == 0 ? "(none)" : choices.FirstOrDefault(c => c.Value == current)?.Label ?? current;
        if (ImGuiNET.ImGui.BeginCombo(id.Contains("##", StringComparison.Ordinal) ? id : $"##{id}", shown))
        {
            if (ImGuiNET.ImGui.Selectable("(none)", current.Length == 0) && current.Length != 0)
            {
                picked = string.Empty;
                changed = true;
            }
            foreach (var c in choices)
            {
                bool sel = c.Value == current;
                if (ImGuiNET.ImGui.Selectable($"{c.Label}##{c.Value}", sel) && !sel)
                {
                    picked = c.Value;
                    changed = true;
                }
                if (sel) ImGuiNET.ImGui.SetItemDefaultFocus();
            }
            ImGuiNET.ImGui.EndCombo();
        }
        return changed;
    }
}
