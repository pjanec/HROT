using System;
using Hrot.AiEditor.Persistence.BTree;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-417</c> B-1 (slice 4c) — a BTree blueprint binding is NAMED by its asset id; the method it calls is
/// DERIVED here, never persisted.</b> 📄 <c>docs/blueprints/DESIGN_Behavior_Action_Binding.md</c> §5.5.
///
/// <para>The ONE derivation point on the build side: <c>BTreeJsonGenerator</c> calls <see cref="ResolveMethods"/> once,
/// right after deserializing, so every reader downstream (method-compatibility validator, topology and bridge emitters,
/// deactivator scan) sees the generated <c>…_Bp.TickCore</c> it has always seen ⇒ emitted source is unchanged by
/// construction. The DTO is the generator's in-memory copy; nothing is written back.</para>
///
/// <para>⭐ The class comes from <paramref name="classNameById"/> — the generator's <c>.bp.json</c> catalog keyed by asset
/// id, so a renamed blueprint still resolves (two blueprints may share a NAME; their ids never collide). ⚠ Only when the
/// catalog has no entry (the blueprint is not part of this build) is the class built from the persisted
/// <c>BlueprintName</c>, which the editor heals from the id on load; a wrong guess then fails the C# compile (CS0234),
/// never silently.</para>
/// </summary>
public static class BTreeBlueprintBindings
{
    /// <summary>Fills <c>MethodFqn</c> of every action/condition binding that names a blueprint. Returns how many.</summary>
    /// <param name="classNameById">The generated class name for a blueprint asset id, or null when unknown.</param>
    public static int ResolveMethods(BehaviorTreeAssetDto dto, Func<Guid, string?>? classNameById)
    {
        if (dto is null) throw new ArgumentNullException(nameof(dto));
        int resolved = 0;
        foreach (var node in dto.Nodes)
        {
            var binding = node switch
            {
                BTreeActionNodeDto a    => a.Action,
                BTreeConditionNodeDto c => c.Condition,
                _                       => null,
            };
            if (binding is null || binding.BlueprintAssetId == Guid.Empty) continue;

            string? cls = classNameById?.Invoke(binding.BlueprintAssetId);
            if (string.IsNullOrEmpty(cls) && !string.IsNullOrEmpty(binding.BlueprintName))
                cls = BlueprintClassNaming.ClassName(binding.BlueprintAssetId, binding.BlueprintName!);
            if (string.IsNullOrEmpty(cls)) continue;

            binding.MethodFqn = BlueprintClassNaming.TickCoreFqn(cls!);
            resolved++;
        }
        return resolved;
    }
}
