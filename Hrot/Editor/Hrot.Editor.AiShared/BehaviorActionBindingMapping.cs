using System;
using Hrot.AiEditor.Persistence;

namespace Hrot.Editor.AiShared;

/// <summary>
/// ⭐ <c>CE-417</c> (slice 4a) — THE copy between the editor's <see cref="BehaviorActionBinding"/> and the persisted
/// <see cref="BehaviorActionBindingDto"/>, used by BOTH hosts' asset mappers. 📄 <c>DESIGN_Behavior_Action_Binding.md</c> §5.4.
///
/// <para>The two records are field-for-field twins, so this is a copy plus the two normalizations the slice-2 mappers
/// already applied: a blueprint name with no Guid is not a blueprint reference (dropped on save), and a binding that holds
/// nothing at all is absent (<c>null</c>).</para>
/// </summary>
public static class BehaviorActionBindingMapping
{
    /// <param name="b">The editor binding.</param>
    /// <param name="keepWhenEmpty">⭐ BTree: an action/condition NODE always carries its binding, even before a method is
    /// picked (the slice-2 BTree mapper wrote it unconditionally). HSM: an empty slot is an unbound slot ⇒ absent.</param>
    public static BehaviorActionBindingDto? ToDto(BehaviorActionBinding? b, bool keepWhenEmpty = false)
    {
        if (b is null) return null;
        if (!keepWhenEmpty && HoldsNothing(b.MethodFqn, b.BlueprintAssetId, b.ExpressionTargetField, b.WorkingStateTypeId, b.WorkingStateTargetField))
            return null;
        return new BehaviorActionBindingDto
        {
            MethodFqn               = NullIfEmpty(b.MethodFqn),
            BlueprintAssetId        = b.BlueprintAssetId,
            BlueprintName           = b.BlueprintAssetId == Guid.Empty ? null : NullIfEmpty(b.BlueprintName),
            ExpressionTargetField   = NullIfEmpty(b.ExpressionTargetField),
            WorkingStateTypeId      = NullIfEmpty(b.WorkingStateTypeId),
            WorkingStateTargetField = NullIfEmpty(b.WorkingStateTargetField),
        };
    }

    /// <param name="d">The persisted binding.</param>
    /// <param name="keepWhenEmpty">The mirror of <see cref="ToDto"/>'s — a BTree node keeps an empty binding.</param>
    public static BehaviorActionBinding? FromDto(BehaviorActionBindingDto? d, bool keepWhenEmpty = false)
    {
        if (d is null) return null;
        if (!keepWhenEmpty && HoldsNothing(d.MethodFqn, d.BlueprintAssetId, d.ExpressionTargetField, d.WorkingStateTypeId, d.WorkingStateTargetField))
            return null;
        return new BehaviorActionBinding
        {
            MethodFqn               = NullIfEmpty(d.MethodFqn),
            BlueprintAssetId        = d.BlueprintAssetId,
            BlueprintName           = NullIfEmpty(d.BlueprintName),
            ExpressionTargetField   = NullIfEmpty(d.ExpressionTargetField),
            WorkingStateTypeId      = NullIfEmpty(d.WorkingStateTypeId),
            WorkingStateTargetField = NullIfEmpty(d.WorkingStateTargetField),
        };
    }

    private static bool HoldsNothing(string? method, Guid blueprint, string? etf, string? wsType, string? wsField)
        => string.IsNullOrEmpty(method) && blueprint == Guid.Empty && string.IsNullOrEmpty(etf)
        && string.IsNullOrEmpty(wsType) && string.IsNullOrEmpty(wsField);

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
