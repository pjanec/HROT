using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Hrot.BTree.Editor.Model;
using Hrot.Editor.AiShared.Blackboard;
using Hrot.Editor.AiShared.Emit;

namespace Hrot.BTree.Editor.Emit;

/// <summary>
/// Emits a companion <c>{AssetName}.Orchestrators.g.cs</c> file for a BTree master asset that
/// has aliased sub-tree variable bindings.
/// One <c>[BTreeAction]</c> static method is generated per unique (variable, sub-tree) pair.
/// Returns <c>null</c> when the asset has no alias bindings — the caller should skip writing.
/// </summary>
public static class BTreeOrchestratorEmitter
{
    private const string Indent = "    ";
    // Namespace used in the generated file for BTreeAction, NodeStatus, BehaviorTreeState, BTreeContext.
    private const string FbtNamespace         = "Fbt";
    private const string BTreeContextNs       = "Fdp.Toolkit.Behavior";
    private const string RuntimeCompilerServNs = "System.Runtime.CompilerServices";

    /// <summary>
    /// Generates the orchestrator source text for <paramref name="asset"/>.
    /// Returns <c>null</c> when there are no alias bindings.
    /// </summary>
    public static string? Emit(BehaviorTreeAsset asset)
    {
        // Collect unique (variableName, subTreeName) pairs and associated DTO types.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var methods = new List<(string varName, string subTreeName, string dtoTypeName, string? dtoTypeNs)>();

        foreach (var v in asset.BlackboardVariables)
        {
            var aliases = asset.GetAliasesFor(v.Name);
            foreach (var binding in aliases)
            {
                string subTreeName = SanitizeIdentifier(binding.RequiringAssetName);
                // Unit-separator (0x1F) is not a legal C# identifier char so it cannot appear in
                // either part, making this key unambiguous.
                string key = v.Name + "\x1F" + subTreeName;
                if (!seen.Add(key)) continue;

                string dtoTypeName = binding.DtoType.Name;
                string? dtoTypeNs  = binding.DtoType.Namespace;
                methods.Add((v.Name, subTreeName, dtoTypeName, dtoTypeNs));
            }
        }

        if (methods.Count == 0 && asset.GetApproachBSyncGroups().Count == 0) return null;

        // Collect usings.
        var usingsSet = new HashSet<string>(StringComparer.Ordinal)
        {
            RuntimeCompilerServNs,
            FbtNamespace,
            BTreeContextNs,
        };
        foreach (var (_, _, _, dtoNs) in methods)
        {
            if (!string.IsNullOrEmpty(dtoNs)
                && !string.Equals(dtoNs, asset.TargetNamespace, StringComparison.Ordinal))
            {
                usingsSet.Add(dtoNs);
            }
        }

        string targetNs = string.IsNullOrEmpty(asset.TargetNamespace)
            ? "Hrot.AI.Behaviors"
            : asset.TargetNamespace;

        // Approach B: subtree nodes with field-level sync bindings.
        var syncGroups = asset.GetApproachBSyncGroups();
        var approachBMethods = new List<ApproachBSyncGroup>();
        foreach (var group in syncGroups)
        {
            string key = SanitizeIdentifier(group.SubtreeName);
            // Skip if already covered by Approach A.
            if (methods.Any(m => m.subTreeName == key)) continue;
            // Collect effective sync-in and sync-out bindings.
            var syncIn  = group.Bindings
                .Where(b => b.SyncIn  && b.MasterVariableName != null)
                .ToList();
            var syncOut = group.Bindings
                .Where(b => b.SyncOut && b.MasterVariableName != null)
                .ToList();
            if (syncIn.Count == 0 && syncOut.Count == 0) continue;
            approachBMethods.Add(group);
            // Collect using directives.
            if (!string.IsNullOrEmpty(group.SubtreeDtoTypeNs)
                && !string.Equals(group.SubtreeDtoTypeNs, targetNs, StringComparison.Ordinal))
                usingsSet.Add(group.SubtreeDtoTypeNs!);
        }

        if (methods.Count == 0 && approachBMethods.Count == 0) return null;

        var sortedUsings = FluentCSharpEmitterBase.SortUsings(usingsSet);

        string bbShort  = ShortTypeName(asset.BlackboardTypeName);
        string ctxShort = ShortTypeName(asset.ContextTypeName);
        string className = SanitizeIdentifier(asset.Name);

        var sb = new StringBuilder();

        // Header
        sb.Append(FluentCSharpEmitterBase.BuildHeader(asset.AssetId));
        sb.AppendLine("// Auto-generated orchestrator actions for aliased sub-trees.");
        sb.AppendLine($"// OwningAssetName: {asset.Name}");
        sb.AppendLine();

        // Usings
        foreach (var ns in sortedUsings)
        {
            if (ns.Length == 0)
                sb.AppendLine();
            else
                sb.AppendLine($"using {ns};");
        }
        sb.AppendLine();

        sb.AppendLine($"namespace {targetNs};");
        sb.AppendLine();
        sb.AppendLine($"public static class {className}_Orchestrators");
        sb.AppendLine("{");

        for (int i = 0; i < methods.Count; i++)
        {
            var (varName, subTreeName, dtoTypeName, _) = methods[i];

            sb.AppendLine($"{Indent}[BTreeAction(Name = \"Orchestrate_{subTreeName}\")]");
            sb.AppendLine($"{Indent}public static NodeStatus Orchestrate_{subTreeName}_Tick(");
            sb.AppendLine($"{Indent}{Indent}ref {bbShort} master,");
            sb.AppendLine($"{Indent}{Indent}ref BehaviorTreeState state,");
            sb.AppendLine($"{Indent}{Indent}ref {ctxShort} ctx,");
            sb.AppendLine($"{Indent}{Indent}int paramIndex)");
            sb.AppendLine($"{Indent}{{");
            sb.AppendLine($"{Indent}{Indent}ref var subBb = ref Unsafe.As<{dtoTypeName}, {dtoTypeName}>(ref master.{varName});");
            sb.AppendLine($"{Indent}{Indent}return {subTreeName}.GetInterpreter().Tick(ref subBb, ref state, ref ctx);");
            sb.AppendLine($"{Indent}}}");

            if (i < methods.Count - 1 || approachBMethods.Count > 0)
                sb.AppendLine();
        }

        // Approach B orchestrator methods
        foreach (var group in approachBMethods)
        {
            string subTreeId = SanitizeIdentifier(group.SubtreeName);
            string sliceField = $"{subTreeId}_{group.SubtreeDtoTypeName}";
            var syncIn  = group.Bindings
                .Where(b => b.SyncIn  && b.MasterVariableName != null)
                .OrderBy(b => b.FieldName, StringComparer.Ordinal).ToList();
            var syncOut = group.Bindings
                .Where(b => b.SyncOut && b.MasterVariableName != null)
                .OrderBy(b => b.FieldName, StringComparer.Ordinal).ToList();

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
    /// Writes the sidecar file using atomic write. No-op when <paramref name="sidecarContent"/> is
    /// <c>null</c> (no aliases; existing file is preserved).
    /// </summary>
    public static void WriteOrchestratorFile(BehaviorTreeAsset asset, string? sidecarContent)
    {
        if (sidecarContent is null) return;
        string path = Path.ChangeExtension(asset.SourceFilePath, null) + ".Orchestrators.g.cs";
        FluentCSharpEmitterBase.WriteAtomic(path, sidecarContent);
    }

    // ---- Helpers ----

    private static string SanitizeIdentifier(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name)
            if (char.IsLetterOrDigit(c) || c == '_') sb.Append(c);
        if (sb.Length == 0) return "BTreeAsset";
        if (char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.ToString();
    }

    private static string ShortTypeName(string fqn)
    {
        int last = fqn.LastIndexOf('.');
        return last >= 0 ? fqn[(last + 1)..] : fqn;
    }
}
