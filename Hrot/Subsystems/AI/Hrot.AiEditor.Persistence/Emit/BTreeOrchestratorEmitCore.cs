using System;
using System.Collections.Generic;
using System.Text;
using Hrot.AiEditor.Persistence.BTree;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// ⭐⭐⭐ <b>Batch 92 (<c>92a</c>) — the BTree orchestrator emit BODY, moved off the editor model.</b>
///
/// <para>📐 Companion to <see cref="HsmOrchestratorEmitCore"/>; the Approach-A alias arm is shared via
/// <see cref="OrchestratorAliasCollector"/>. ⭐ <b>One body</b> serves the editor sidecar path
/// (<c>92c</c>) and the generator's <c>{baseName}.Orchestrators.g.cs</c> (<c>92b</c>) — 📌 ruling 9.</para>
///
/// <para>⛔⛔ <b>THE APPROACH-B ARM CANNOT BE DRIVEN FROM THE DTO, AND THAT IS MEASURED, NOT ASSUMED.</b>
/// The handoff's premise — <i>"the DTOs now carry BOTH inputs: <c>SubtreeSyncBindings</c> (since PU)
/// and <c>Aliases</c> (since <c>91b</c>)"</i> — is ⭐ <b>true for the bindings and false for everything
/// else Approach B needs</b>:</para>
///
/// <list type="number">
/// <item>⭐⭐ <b>The sub-tree IDENTITY is session-local.</b>
/// <c>BehaviorTreeAsset.GetApproachBSyncGroups()</c> (<c>:719</c>) skips any node absent from
/// <c>_syncNodeMeta</c>, whose <b>only</b> writer is <c>InspectorWindow:194</c> — a UI draw. It has no
/// load path, and <c>BehaviorTreeAssetDto.cs:10</c> names it <b>deliberately excluded</b>, enforced by
/// <c>BTreeDtoRuntimeFieldExclusionTests:29</c>. ⚠ So even in the EDITOR, Approach B emits nothing
/// after a reload until a designer re-opens that panel.</item>
/// <item>⛔⛔ <b>The destination FIELD does not exist.</b> The emitted body writes
/// <c>ref master.{SubtreeName}_{DtoTypeName}</c>. That slice comes from
/// <c>GetAutoAllocatedVariables()</c> (<c>:768</c>), whose only consumer is
/// <c>BlackboardAuthoringWindow:529</c>, which merely <b>displays</b> it greyed as
/// <i>"(size unknown until build)"</i>. ⇒ it never reaches <c>Blackboard.Variables</c> and no
/// blackboard emitter declares it.</item>
/// </list>
///
/// <para>⇒ ⭐⭐⭐ <b>Approach-B groups are therefore an explicit PARAMETER, not an optional one.</b>
/// ⛔ No default: 📌 the silent-default rule — <i>"a production caller that HAS a dependency must PASS
/// it"</i>. The editor passes its <c>_syncNodeMeta</c>-derived groups; the generator passes an EMPTY
/// list because it provably has none, and says so at the call site. ⚠ <b>Widening the DTO is a
/// persistence-schema decision and is NOT taken here</b> — and it would not be sufficient anyway
/// while gap (2) stands.</para>
/// </summary>
public static class BTreeOrchestratorEmitCore
{
    private const string Indent = "    ";
    private const string FbtNamespace          = "Fbt";
    private const string BTreeContextNs        = "Fdp.Toolkit.Behavior";
    private const string RuntimeCompilerServNs = "System.Runtime.CompilerServices";

    /// <summary>Fallback namespace when the asset declares none — matches the editor emitter.</summary>
    public const string DefaultTargetNamespace = "Hrot.AI.Behaviors";

    /// <summary>
    /// Generates the orchestrator source text for <paramref name="dto"/>.
    /// ⭐ Returns <c>null</c> when there is nothing to emit — ⛔ the caller emits <b>no file at all</b>,
    /// which is what keeps the corpus byte-identical.
    /// </summary>
    /// <param name="approachBGroups">
    /// ⭐⭐ Field-sync groups, which <b>only the editor can supply</b> — see the type remarks.
    /// Pass an empty list when the caller has none.
    /// </param>
    public static string? Emit(
        BehaviorTreeAssetDto dto, IReadOnlyList<OrchestratorSyncGroup> approachBGroups)
    {
        if (dto is null)              throw new ArgumentNullException(nameof(dto));
        if (approachBGroups is null)  throw new ArgumentNullException(nameof(approachBGroups));

        var methods = OrchestratorAliasCollector.Collect(dto.Aliases, VariableNamesOf(dto), "BTreeAsset");

        if (methods.Count == 0 && approachBGroups.Count == 0) return null;

        var usingsSet = new HashSet<string>(StringComparer.Ordinal)
        {
            RuntimeCompilerServNs,
            FbtNamespace,
            BTreeContextNs,
        };
        OrchestratorAliasCollector.AddDtoNamespaces(usingsSet, methods, dto.TargetNamespace);

        string targetNs = string.IsNullOrEmpty(dto.TargetNamespace)
            ? DefaultTargetNamespace
            : dto.TargetNamespace;

        // Approach B: subtree nodes with at least one ACTIVE field-level sync binding.
        var approachBMethods = new List<OrchestratorSyncGroup>();
        foreach (var group in approachBGroups)
        {
            string key = OrchestratorAliasCollector.SanitizeIdentifier(group.SubtreeName, "BTreeAsset");
            // Skip if already covered by Approach A.
            bool coveredByA = false;
            foreach (var m in methods)
                if (string.Equals(m.SubTreeName, key, StringComparison.Ordinal)) { coveredByA = true; break; }
            if (coveredByA) continue;

            if (ActiveBindings(group, syncIn: true).Count == 0
                && ActiveBindings(group, syncIn: false).Count == 0) continue;

            approachBMethods.Add(group);

            if (!string.IsNullOrEmpty(group.SubtreeDtoTypeNs)
                && !string.Equals(group.SubtreeDtoTypeNs, targetNs, StringComparison.Ordinal))
                usingsSet.Add(group.SubtreeDtoTypeNs!);
        }

        if (methods.Count == 0 && approachBMethods.Count == 0) return null;

        var sortedUsings = AiEmitCoreBase.SortUsings(usingsSet);

        string bbShort   = OrchestratorAliasCollector.ShortTypeName(dto.BlackboardTypeName);
        string ctxShort  = OrchestratorAliasCollector.ShortTypeName(dto.ContextTypeName);
        string className = OrchestratorAliasCollector.SanitizeIdentifier(dto.Name, "BTreeAsset");

        var sb = new StringBuilder();

        sb.Append(AiEmitCoreBase.BuildHeader(dto.AssetId));
        sb.AppendLine("// Auto-generated orchestrator actions for aliased sub-trees.");
        sb.AppendLine($"// OwningAssetName: {dto.Name}");
        sb.AppendLine();

        foreach (var ns in sortedUsings)
        {
            if (ns.Length == 0) sb.AppendLine();
            else                sb.AppendLine($"using {ns};");
        }
        sb.AppendLine();

        sb.AppendLine($"namespace {targetNs};");
        sb.AppendLine();
        sb.AppendLine($"public static class {className}_Orchestrators");
        sb.AppendLine("{");

        for (int i = 0; i < methods.Count; i++)
        {
            var m = methods[i];

            sb.AppendLine($"{Indent}[BTreeAction(Name = \"Orchestrate_{m.SubTreeName}\")]");
            sb.AppendLine($"{Indent}public static NodeStatus Orchestrate_{m.SubTreeName}_Tick(");
            sb.AppendLine($"{Indent}{Indent}ref {bbShort} master,");
            sb.AppendLine($"{Indent}{Indent}ref BehaviorTreeState state,");
            sb.AppendLine($"{Indent}{Indent}ref {ctxShort} ctx,");
            sb.AppendLine($"{Indent}{Indent}int paramIndex)");
            sb.AppendLine($"{Indent}{{");
            sb.AppendLine($"{Indent}{Indent}ref var subBb = ref Unsafe.As<{m.DtoTypeName}, {m.DtoTypeName}>(ref master.{m.VarName});");
            sb.AppendLine($"{Indent}{Indent}return {m.SubTreeName}.GetInterpreter().Tick(ref subBb, ref state, ref ctx);");
            sb.AppendLine($"{Indent}}}");

            if (i < methods.Count - 1 || approachBMethods.Count > 0)
                sb.AppendLine();
        }

        // ⭐ §8.3's shape, preserved verbatim: COPY IN · TICK · COPY OUT.
        foreach (var group in approachBMethods)
        {
            string subTreeId  = OrchestratorAliasCollector.SanitizeIdentifier(group.SubtreeName, "BTreeAsset");
            // ⭐⭐⭐ Q50: ONE composer for this name. SubtreeSyncProjection DECLARES the field with it;
            //    this WRITES through it. ⛔ A one-character divergence between the two is a build break
            //    with no obvious cause, so neither side spells it out (ruling 9).
            string sliceField = SubtreeSyncProjection.SliceFieldName(subTreeId, group.SubtreeDtoTypeName);
            var syncIn  = ActiveBindings(group, syncIn: true);
            var syncOut = ActiveBindings(group, syncIn: false);

            sb.AppendLine($"{Indent}[BTreeAction(Name = \"Orchestrate_{subTreeId}\")]");
            sb.AppendLine($"{Indent}public static NodeStatus Orchestrate_{subTreeId}_Tick(");
            sb.AppendLine($"{Indent}{Indent}ref {bbShort} master,");
            sb.AppendLine($"{Indent}{Indent}ref BehaviorTreeState state,");
            sb.AppendLine($"{Indent}{Indent}ref {ctxShort} ctx,");
            sb.AppendLine($"{Indent}{Indent}int paramIndex)");
            sb.AppendLine($"{Indent}{{");
            sb.AppendLine($"{Indent}{Indent}ref var subDto = ref master.{sliceField};");
            foreach (var b in syncIn)
                sb.AppendLine($"{Indent}{Indent}subDto.{b.FieldName} = master.{b.MasterVariableName};");
            sb.AppendLine($"{Indent}{Indent}var result = {subTreeId}.GetInterpreter().Tick(ref subDto, ref state, ref ctx);");
            foreach (var b in syncOut)
                sb.AppendLine($"{Indent}{Indent}master.{b.MasterVariableName} = subDto.{b.FieldName};");
            sb.AppendLine($"{Indent}{Indent}return result;");
            sb.AppendLine($"{Indent}}}");
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// The bindings in one direction that actually copy something, ordered by field name.
    /// ⚠ A binding with no <c>MasterVariableName</c> has no source/target and is skipped.
    /// </summary>
    private static List<OrchestratorSyncBinding> ActiveBindings(OrchestratorSyncGroup group, bool syncIn)
    {
        var result = new List<OrchestratorSyncBinding>();
        foreach (var b in group.Bindings)
        {
            if (b is null || b.MasterVariableName is null) continue;
            if (syncIn ? b.SyncIn : b.SyncOut) result.Add(b);
        }
        result.Sort(static (a, b) => string.CompareOrdinal(a.FieldName, b.FieldName));
        return result;
    }

    /// <summary>
    /// ⚠ Emission order follows the blackboard declaration order — ⛔ not the alias dictionary's key
    /// order, which is not a contract.
    /// </summary>
    private static IEnumerable<string> VariableNamesOf(BehaviorTreeAssetDto dto)
    {
        var vars = dto.Blackboard?.Variables;
        if (vars is null) yield break;
        foreach (var v in vars) yield return v.Name;
    }
}

/// <summary>
/// ⭐ One subtree node needing an Approach-B orchestrator, in terms the netstandard2.0 emit core can
/// see. ⛔ Deliberately NOT <c>Hrot.Editor.AiShared.Blackboard.ApproachBSyncGroup</c> — that type lives
/// in the net8/ImGui editor assembly this one must not reference. ⭐ The editor maps onto it.
/// </summary>
public sealed class OrchestratorSyncGroup
{
    public OrchestratorSyncGroup(
        string subtreeName,
        string subtreeDtoTypeName,
        string? subtreeDtoTypeNs,
        IReadOnlyList<OrchestratorSyncBinding> bindings)
    {
        SubtreeName        = subtreeName;
        SubtreeDtoTypeName = subtreeDtoTypeName;
        SubtreeDtoTypeNs   = subtreeDtoTypeNs;
        Bindings           = bindings;
    }

    /// <summary>Sub-tree asset name; sanitised into the method-name suffix.</summary>
    public string SubtreeName { get; }
    /// <summary>Short name of the sub-tree's blackboard struct.</summary>
    public string SubtreeDtoTypeName { get; }
    /// <summary>Its namespace, or null when it shares the master's.</summary>
    public string? SubtreeDtoTypeNs { get; }
    /// <summary>All bindings on the node, including inactive ones — the core filters.</summary>
    public IReadOnlyList<OrchestratorSyncBinding> Bindings { get; }
}

/// <summary>One field-level sync binding. Mirrors <c>SubtreeSyncBindingDto</c>'s four members.</summary>
public sealed class OrchestratorSyncBinding
{
    public OrchestratorSyncBinding(
        string fieldName, string? masterVariableName, bool syncIn, bool syncOut)
    {
        FieldName          = fieldName;
        MasterVariableName = masterVariableName;
        SyncIn             = syncIn;
        SyncOut            = syncOut;
    }

    public string FieldName { get; }
    public string? MasterVariableName { get; }
    public bool SyncIn { get; }
    public bool SyncOut { get; }
}
