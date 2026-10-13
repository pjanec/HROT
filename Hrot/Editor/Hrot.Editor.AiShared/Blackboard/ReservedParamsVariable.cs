using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.AiEditor.Persistence;

namespace Hrot.Editor.AiShared.Blackboard;

/// <summary>
/// ⭐⭐ <c>CE-2135</c> (B3a, R-3000; <c>docs/DESIGN_Orders_Params_Entity_Classes.md</c> §5c) — the editor's view of the reserved
/// <c>Params</c> variable. An asset that names its params DTO (<c>ParamsDtoTypeId</c>) and declares no Input variable stores its
/// params there; on disk it is two ASSET fields (<c>ParamsDtoTypeId</c>, <c>ParamsDefaultJson</c>), never a variable. In the
/// editor it is ONE variable entry (<see cref="BlackboardVariableEntry.IsReservedParams"/>), so pickers, bindings and validation see
/// it like any other; a save writes it back as the two fields. ⭐ The same rule the generators apply
/// (<c>BTreeEmitCore.WithParamsVariable</c>), so editor and build agree on what exists.
/// </summary>
public static class ReservedParamsVariable
{
    public const string Name = "Params";
    private const string NoParams = "Hrot.Map.Definitions.Behavior.NoParams";

    /// <summary>Adds the reserved entry at the head of <paramref name="vars"/> when the asset names a DTO, is editor-managed, has no
    /// resolver asset and has no variable called <c>Params</c>. No-op otherwise. ⭐ The asset's other variables stay plain
    /// variables: packed and bindable, but not the JSON.</summary>
    public static void AddTo(List<BlackboardVariableEntry> vars, string? paramsDtoTypeId, string? defaultJson, bool managed, bool hasResolver)
    {
        string? named = paramsDtoTypeId?.Trim();
        if (!managed || hasResolver || string.IsNullOrEmpty(named) || named == NoParams) return;
        if (vars.Any(v => v.Name == Name || v.IsReservedParams)) return;
        vars.Insert(0, new BlackboardVariableEntry(Name, BlackboardTypeHelper.ResolveClrType(named!),
            "the behaviour's params — its named DTO (ParamsDtoTypeId); reserved, not authored",
            DefaultValueJson: defaultJson, IsReservedParams: true));
    }

    /// <summary>The reserved entry in <paramref name="vars"/>, or null.</summary>
    public static BlackboardVariableEntry? Find(IEnumerable<BlackboardVariableEntry> vars)
        => vars.FirstOrDefault(v => v.IsReservedParams);
}
