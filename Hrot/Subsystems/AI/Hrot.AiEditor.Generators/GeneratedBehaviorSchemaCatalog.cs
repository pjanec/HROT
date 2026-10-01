using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;

namespace Hrot.AiEditor.Generators;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-439</c> (<c>Q76</c> §12.28) — the size of a SIBLING behaviour's generated Inputs struct.</b>
///
/// <para>
/// 🔴 A host that binds a hosted subtree declares a variable typed as the child's <c>{Child}_…_Blackboard</c> — a type THIS
/// generator run emits, so Roslyn cannot see it, <c>Pack</c> throws on it, and the host emits no params at all, silently. The
/// <c>CE-414</c> "F" fix (<see cref="GeneratedBlueprintSchemaCatalog"/>) solved exactly this for blueprint <c>Params</c>; this is
/// its twin for behaviour Inputs.
/// </para>
///
/// <para>
/// ⭐ The size is <c>Pack</c>'s total over the child's own variables — the SAME call <c>BTreeEmitCore.EmitBlackboardStructSource</c>
/// makes to emit <c>[StructLayout(Explicit, Size = …)]</c>, so the two cannot disagree. The type id is
/// <see cref="BTreeEmitCore.InputsStructTypeId"/> — the one naming rule.
/// </para>
/// </summary>
internal static class GeneratedBehaviorSchemaCatalog
{
    /// <summary>One sibling behaviour: its Inputs struct type id and its own variables.</summary>
    internal sealed class Schema   // ⚠ a class, not a record: this assembly targets netstandard2.0 (no IsExternalInit)
    {
        public Schema(string inputsTypeId, IReadOnlyList<BlackboardVariableDto> variables)
        { InputsTypeId = inputsTypeId; Variables = variables; }
        public string InputsTypeId { get; }
        public IReadOnlyList<BlackboardVariableDto> Variables { get; }
    }

    /// <summary>Every managed <c>*.btree.json</c> that publishes an Inputs struct. Unparseable files are skipped (their own
    /// generator run reports them).</summary>
    public static IReadOnlyList<Schema> Parse(ImmutableArray<(string Path, string Text)> btreeJsonFiles)
    {
        var result = new List<Schema>();
        foreach (var (_, text) in btreeJsonFiles)
        {
            BehaviorTreeAssetDto? dto;
            try { dto = BTreeJsonServices.Deserialize(text); }
            catch { continue; }
            if (dto is null) continue;
            string? typeId = BTreeEmitCore.InputsStructTypeId(dto);
            if (typeId != null) result.Add(new Schema(typeId, dto.Blackboard.Variables));
        }
        return result;
    }

    /// <summary>
    /// The packed size of <paramref name="typeId"/> when it is a sibling's Inputs struct, else null. ⚠ A child may itself bind
    /// a grandchild's Inputs — <paramref name="resolver"/> is the caller's whole resolver, and <paramref name="visiting"/>
    /// stops a cycle (an A-hosts-B-hosts-A layout has no size; the validator reports the cycle).
    /// </summary>
    public static int? TryResolveInputsSize(
        string typeId, IReadOnlyList<Schema> schemas, Func<string, int?> resolver, HashSet<string>? visiting = null)
    {
        foreach (var s in schemas)
        {
            if (!string.Equals(s.InputsTypeId, typeId, StringComparison.Ordinal)) continue;
            visiting ??= new HashSet<string>(StringComparer.Ordinal);
            if (!visiting.Add(typeId)) return null;
            try
            {
                BTreeBlackboardPackHelper.Pack(s.Variables, t => resolver(t), out int bytes);
                return bytes;
            }
            catch (NotSupportedException) { return null; }
            finally { visiting.Remove(typeId); }
        }
        return null;
    }
}
