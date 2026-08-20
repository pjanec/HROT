using System;
using System.Collections.Generic;
using System.Text;
using Hrot.AiEditor.Persistence.Hsm;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// ⭐⭐⭐ <b>Batch 92 (<c>92a</c>) — the HSM orchestrator emit BODY, moved off the editor model.</b>
///
/// <para>📐 <b>Why it had to move.</b> <c>HsmOrchestratorEmitter</c> emits from <c>HsmAsset</c> — an
/// editor type. ⛔ A Roslyn generator has only the <b>DTO</b>. ⇒ this core emits from
/// <see cref="HsmAssetDto"/>, so ⭐ <b>one body</b> serves both the editor sidecar path and the
/// generator's <c>{baseName}.Orchestrators.g.cs</c> (<c>92b</c>) — 📌 ruling 9.</para>
///
/// <para>⭐⭐ <b>The HSM arm is the one <c>91b</c> made meaningful.</b> HSM has no Approach-B field
/// sync at all; it hosts a sub-tree <b>only</b> through an Approach-A alias, and until <c>91b</c> those
/// aliases never survived a reload ⇒ 🔴 nothing an HSM ever loaded could emit an orchestrator.
/// ⛔ <b>Do not read this as "HSM sub-tree hosting is complete"</b> — there is still no authoring
/// gesture that creates the alias, and no blackboard aggregation behind it.</para>
///
/// <para>⭐⭐⭐ <b><c>DtoTypeId</c> is SPLIT, never resolved.</b> The editor emitter reads
/// <c>binding.DtoType.Name</c> / <c>.Namespace</c> — a live <c>System.Type</c>. ⛔ A generator cannot
/// load behavior assemblies, so this core takes <see cref="BlackboardAliasBindingDto.DtoTypeId"/>
/// (a <c>Type.FullName</c>, written by <c>91b</c>) and splits it at the last <c>'.'</c>. ⭐ That is
/// pure string work and needs no assembly.</para>
/// </summary>
public static class HsmOrchestratorEmitCore
{
    private const string Indent = "    ";
    private const string HsmActionNs           = "Fhsm.Kernel.Attributes";
    private const string FbtNamespace          = "Fbt";
    private const string BTreeContextNs        = "Fdp.Toolkit.Behavior";
    private const string RuntimeCompilerServNs = "System.Runtime.CompilerServices";

    /// <summary>Fallback namespace when the asset declares none — matches the editor emitter.</summary>
    public const string DefaultTargetNamespace = "Hrot.AI.Behaviors.Machines";

    /// <summary>
    /// Generates the orchestrator source text for <paramref name="dto"/>.
    /// ⭐ Returns <c>null</c> when the asset has no alias bindings — ⛔ the caller emits <b>nothing</b>,
    /// which is what keeps the whole corpus byte-identical (no shipped asset has an alias).
    /// </summary>
    public static string? Emit(HsmAssetDto dto)
    {
        if (dto is null) throw new ArgumentNullException(nameof(dto));

        var methods = OrchestratorAliasCollector.Collect(dto.Aliases, VariableNamesOf(dto), "HsmAsset");
        if (methods.Count == 0) return null;

        var usingsSet = new HashSet<string>(StringComparer.Ordinal)
        {
            RuntimeCompilerServNs,
            HsmActionNs,
            FbtNamespace,
            BTreeContextNs,
        };
        OrchestratorAliasCollector.AddDtoNamespaces(usingsSet, methods, dto.TargetNamespace);
        var sortedUsings = AiEmitCoreBase.SortUsings(usingsSet);

        string bbShort   = OrchestratorAliasCollector.ShortTypeName(dto.BlackboardTypeName);
        string className = OrchestratorAliasCollector.SanitizeIdentifier(dto.Name, "HsmAsset");
        string targetNs  = string.IsNullOrEmpty(dto.TargetNamespace)
            ? DefaultTargetNamespace
            : dto.TargetNamespace;

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

            sb.AppendLine($"{Indent}[HsmAction(Name = \"Orchestrate_{m.SubTreeName}\")]");
            sb.AppendLine($"{Indent}public static NodeStatus Orchestrate_{m.SubTreeName}_Tick(");
            sb.AppendLine($"{Indent}{Indent}ref {bbShort} master,");
            sb.AppendLine($"{Indent}{Indent}ref BehaviorTreeState state,");
            sb.AppendLine($"{Indent}{Indent}ref BTreeContext ctx,");
            sb.AppendLine($"{Indent}{Indent}int paramIndex)");
            sb.AppendLine($"{Indent}{{");
            sb.AppendLine($"{Indent}{Indent}ref var subBb = ref Unsafe.As<{m.DtoTypeName}, {m.DtoTypeName}>(ref master.{m.VarName});");
            sb.AppendLine($"{Indent}{Indent}return {m.SubTreeName}.GetInterpreter().Tick(ref subBb, ref state, ref ctx);");
            sb.AppendLine($"{Indent}}}");

            if (i < methods.Count - 1) sb.AppendLine();
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// ⚠ The variable ORDER is the emission order, so it is taken from the blackboard block rather
    /// than from the alias dictionary — ⛔ a <c>Dictionary</c>'s enumeration order is not a contract.
    /// </summary>
    private static IEnumerable<string> VariableNamesOf(HsmAssetDto dto)
    {
        var vars = dto.Blackboard?.Variables;
        if (vars is null) yield break;
        foreach (var v in vars) yield return v.Name;
    }
}
