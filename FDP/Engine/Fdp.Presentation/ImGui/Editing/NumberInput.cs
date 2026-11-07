using System;
using System.Collections.Generic;
using System.Globalization;
using ImGuiNET;

namespace Fdp.Presentation.Editing;

/// <summary>
/// ⭐ U-7 ⑤ (docs/UX/DESIGN_Condition_Editor.md §8.2) — a number as a person writes it: the shortest form (<c>60</c>, <c>0.5</c> — never
/// <c>60.000000</c>), its unit after it, and an OPEN bound (the type's <c>MaxValue</c>, the sentinel the condition DTOs store for "no end")
/// shown as <i>no limit</i> — typing a number there sets it; the sentinel is what is stored. Format strings are cached per unit: 0 B per
/// steady frame (R-2084).
/// </summary>
public static class NumberInput
{
    /// <summary>What an open bound reads as.</summary>
    public const string NoLimit = "no limit";

    private static readonly Dictionary<string, string> Formats = new(StringComparer.Ordinal);
    private static string _typed = string.Empty;

    /// <summary>The ImGui format for a value with <paramref name="unit"/> (a <c>%</c> in the unit escaped).</summary>
    public static string FormatFor(string? unit)
    {
        if (string.IsNullOrEmpty(unit)) return "%g";
        if (Formats.TryGetValue(unit, out var f)) return f;
        f = "%g " + unit.Replace("%", "%%");
        Formats[unit] = f;
        return f;
    }

    public static bool Float(ref float v, string? unit)
    {
        if (v == float.MaxValue) return OpenBound(out var typed) && float.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        return ImGui.InputFloat("##v", ref v, 0f, 0f, FormatFor(unit));
    }

    public static bool Double(ref double v, string? unit)
    {
        if (v == double.MaxValue) return OpenBound(out var typed) && double.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        return ImGui.InputDouble("##v", ref v, 0d, 0d, FormatFor(unit));
    }

    public static bool Int(ref int v, string? unit)
    {
        if (v == int.MaxValue) return OpenBound(out var typed) && int.TryParse(typed, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        if (string.IsNullOrEmpty(unit)) return ImGui.InputInt("##v", ref v);
        float unitWidth = ImGui.CalcTextSize(unit).X + ImGui.GetStyle().ItemSpacing.X;
        ImGui.SetNextItemWidth(Math.Max(40f, ImGui.GetContentRegionAvail().X - unitWidth));
        bool changed = ImGui.InputInt("##v", ref v, 0, 0);
        ImGui.SameLine();
        ImGui.TextDisabled(unit);
        return changed;
    }

    /// <summary>The open bound: an empty box with the hint <see cref="NoLimit"/>; the text typed, once Enter or focus-out commits it.
    /// One box is edited at a time, so one buffer (keyed by the item's id) carries the typing across frames.</summary>
    private static bool OpenBound(out string typed)
    {
        typed = string.Empty;
        uint id = ImGui.GetID("##v");
        string buf = id == _activeId ? _typed : string.Empty;
        ImGui.InputTextWithHint("##v", NoLimit, ref buf, 32, ImGuiInputTextFlags.CharsScientific);
        if (ImGui.IsItemActive()) { _activeId = id; _typed = buf; }
        if (!ImGui.IsItemDeactivatedAfterEdit()) return false;
        typed = buf;
        _activeId = 0; _typed = string.Empty;
        return typed.Length > 0;
    }

    private static uint _activeId;
}

/// <summary>
/// ⭐ U-7 ⑥ (Condition editor §8.2) — THE hook a collapsed struct-editor node asks for its one-line summary (drawn dimmed in column 2).
/// A type registers once (the condition kinds through <c>ConditionSentence</c>, U-14); a lookup walks the value's base types, cached per
/// concrete type. ⛔ No drawer composes a summary of its own.
/// </summary>
public static class EditSummaries
{
    private static readonly Dictionary<Type, Func<object, string?>> Registered = new()
    {
        // ⭐ U-14 / C2 — a collapsed CONDITION says its sentence: the ONE renderer (ConditionSentence), cached per node so a frame allocates nothing
        [typeof(Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto)] = v => Fdp.Toolkit.ReplayBrowser.Search.Conditions.ConditionSentence.Summary((Fdp.Toolkit.ReplayBrowser.Search.SearchPredicateDto)v),
    };
    private static readonly Dictionary<Type, Func<object, string?>?> Resolved = new();

    /// <summary>Registers the summary of <paramref name="type"/> and every type derived from it.</summary>
    public static void Register(Type type, Func<object, string?> summary)
    {
        Registered[type] = summary;
        Resolved.Clear();
    }

    /// <summary>The summary of <paramref name="value"/>, or null when no type in its chain registered one.</summary>
    public static string? Of(object value)
    {
        var t = value.GetType();
        if (!Resolved.TryGetValue(t, out var f))
        {
            f = null;
            for (var c = t; c != null && f == null; c = c.BaseType) Registered.TryGetValue(c, out f);
            Resolved[t] = f;
        }
        return f?.Invoke(value);
    }
}
