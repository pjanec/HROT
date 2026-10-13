using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hrot.Map.Definitions.Behavior;
using ImGuiNET;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// ⭐ <c>CE-1061</c> (U5a, R-3001) — an asset that names its params DTO: a BTree, an HSM (here, via <see cref="IBlackboardManagedAsset"/>'s
/// implementers). The blueprint writes its own field (<c>BlueprintParamsDto</c>). 📄 <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §13.10b D5.
/// </summary>
public interface IParamsDtoAsset
{
    /// <summary>The named params DTO's type id (<c>Type.FullName</c>), or null.</summary>
    string? ParamsDtoTypeId { get; }

    /// <summary>The asset's params default overrides as the open document holds them (field → value JSON), or null.</summary>
    string? CurrentParamsDefaults { get; }

    /// <summary>
    /// ⭐ <c>CE-1061</c> U5a/U5b (§13.10c E3) — THE one write: the params DTO and its default overrides together. The chooser passes the new
    /// DTO and null (another DTO's overrides name other fields); a default edit passes the same DTO and the new overrides; every undo
    /// passes the old pair. Rebuilds the reserved <c>Params</c> entry and marks the asset dirty; a no-op when nothing changes.
    /// </summary>
    void SetParams(string? typeId, string? defaultsJson);
}

/// <summary>One params DTO the chooser offers.</summary>
/// <param name="TypeId">What the asset stores (<c>Type.FullName</c>).</param>
/// <param name="Display">Its simple name.</param>
/// <param name="Origin">Where it comes from: <c>C# File.cs:line</c>, <c>asset</c> (a <c>.params.json</c>), <c>C#</c> when unknown, or
/// <c>not loaded</c> for the asset's current DTO when the registry does not list it.</param>
public readonly record struct ParamsDtoChoice(string TypeId, string Display, string Origin)
{
    public string Label => $"{Display}  ({Origin})";
}

/// <summary>
/// ⭐ <c>CE-1061</c> (U5a, design §13.10b D6) — the params DTOs an asset may name: every <see cref="ParamsDtoRegistry"/> entry with the
/// <see cref="DtoPurpose.Params"/> purpose, hand-written C# and generated from <c>.params.json</c> alike (F2: one registry), the origin
/// shown; and THE combo every asset kind draws it with (BTree / HSM variables panel, the blueprint outline).
/// </summary>
public static class ParamsDtoChooser
{
    /// <summary>The choices, by name; <paramref name="current"/> is kept (marked <c>not loaded</c>) when the registry does not list it.</summary>
    public static IReadOnlyList<ParamsDtoChoice> Choices(ParamsDtoRegistry registry, string? current = null)
    {
        if (registry is null) throw new ArgumentNullException(nameof(registry));
        var list = registry.Dtos
            .Where(d => (d.Purposes & DtoPurpose.Params) != 0 && d.Type.FullName is not null)
            .Select(d => new ParamsDtoChoice(d.Type.FullName!, d.Type.Name, OriginOf(d.Origin)))
            .GroupBy(c => c.TypeId, StringComparer.Ordinal).Select(g => g.First())
            .OrderBy(c => c.Display, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.TypeId, StringComparer.Ordinal)
            .ToList();
        if (!string.IsNullOrWhiteSpace(current) && list.All(c => !string.Equals(c.TypeId, current, StringComparison.Ordinal)))
            list.Insert(0, new ParamsDtoChoice(current!, SimpleName(current!), "not loaded"));
        return list;
    }

    /// <summary>How a DTO's origin reads in the chooser.</summary>
    public static string OriginOf(DtoOrigin origin)
        => origin.IsAsset ? "asset"
         : !string.IsNullOrEmpty(origin.SourceFile) ? $"C# {Path.GetFileName(origin.SourceFile)}:{origin.SourceLine}"
         : "C#";

    /// <summary>The simple name of a type id (<c>A.B.C+D</c> ⇒ <c>D</c>).</summary>
    public static string SimpleName(string typeId)
    {
        var t = typeId.Trim();
        int cut = Math.Max(t.LastIndexOf('.'), t.LastIndexOf('+'));
        return cut >= 0 ? t[(cut + 1)..] : t;
    }

    private static readonly Dictionary<string, string> Filters = new(StringComparer.Ordinal);

    /// <summary>
    /// Draws the "Params DTO" combo (with a type-to-filter box) over <paramref name="choices"/>; returns the picked type id when the
    /// author picks a DIFFERENT one this frame, else null. <paramref name="id"/> keeps two choosers on one screen apart.
    /// </summary>
    public static string? Draw(string id, string? current, IReadOnlyList<ParamsDtoChoice> choices, bool readOnly = false)
    {
        string preview = "(none — choose the params DTO)";
        if (current is { Length: > 0 })
        {
            preview = SimpleName(current);
            foreach (var c in choices)
                if (string.Equals(c.TypeId, current, StringComparison.Ordinal)) { preview = c.Label; break; }
        }
        string? picked = null;
        ImGui.BeginDisabled(readOnly);
        ImGui.SetNextItemWidth(-1f);
        if (ImGui.BeginCombo($"##paramsDto_{id}", "Params DTO: " + preview))
        {
            Filters.TryGetValue(id, out var filter);
            filter ??= "";
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.InputTextWithHint($"##paramsDtoFilter_{id}", "filter…", ref filter, 128)) Filters[id] = filter;
            foreach (var c in choices)
            {
                if (filter.Length > 0 && c.Label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                bool selected = string.Equals(c.TypeId, current, StringComparison.Ordinal);
                if (ImGui.Selectable(c.Label + "##" + c.TypeId, selected) && !selected) picked = c.TypeId;
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(c.TypeId);
            }
            ImGui.EndCombo();
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("The behaviour's params DTO: its fields are the inputs an assignment or an order fills. Changing it clears this asset's default overrides.");
        ImGui.EndDisabled();
        return picked;
    }
}
