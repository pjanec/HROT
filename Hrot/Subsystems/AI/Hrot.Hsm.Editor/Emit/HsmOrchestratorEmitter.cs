using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Hrot.Editor.AiShared.Emit;
using Hrot.Hsm.Editor.Model;

namespace Hrot.Hsm.Editor.Emit;

/// <summary>
/// Emits a companion <c>{AssetName}.Orchestrators.g.cs</c> file for an HSM master asset that
/// has aliased sub-tree (BTree) variable bindings.
/// One <c>[HsmAction]</c> static method is generated per unique (variable, sub-tree) pair.
/// Returns <c>null</c> when the asset has no alias bindings — the caller should skip writing.
/// </summary>
public static class HsmOrchestratorEmitter
{
    private const string Indent = "    ";
    private const string HsmActionNs          = "Fhsm.Kernel.Attributes";
    private const string FbtNamespace         = "Fbt";
    private const string BTreeContextNs       = "Fdp.Toolkit.Behavior";
    private const string RuntimeCompilerServNs = "System.Runtime.CompilerServices";

    /// <summary>
    /// Generates the orchestrator source text for <paramref name="asset"/>.
    /// Returns <c>null</c> when there are no alias bindings.
    /// </summary>
    public static string? Emit(HsmAsset asset)
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
                // Unit-separator (0x1F) is not a legal C# identifier char, making this key unambiguous.
                string key = v.Name + "\x1F" + subTreeName;
                if (!seen.Add(key)) continue;

                string dtoTypeName = binding.DtoType.Name;
                string? dtoTypeNs  = binding.DtoType.Namespace;
                methods.Add((v.Name, subTreeName, dtoTypeName, dtoTypeNs));
            }
        }

        if (methods.Count == 0) return null;

        // Collect usings.
        var usingsSet = new HashSet<string>(StringComparer.Ordinal)
        {
            RuntimeCompilerServNs,
            HsmActionNs,
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
        var sortedUsings = FluentCSharpEmitterBase.SortUsings(usingsSet);

        string bbShort  = ShortTypeName(asset.BlackboardTypeName);
        string className = SanitizeIdentifier(asset.Name);

        string targetNs = string.IsNullOrEmpty(asset.TargetNamespace)
            ? "Hrot.AI.Behaviors.Machines"
            : asset.TargetNamespace;

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

            sb.AppendLine($"{Indent}[HsmAction(Name = \"Orchestrate_{subTreeName}\")]");
            sb.AppendLine($"{Indent}public static NodeStatus Orchestrate_{subTreeName}_Tick(");
            sb.AppendLine($"{Indent}{Indent}ref {bbShort} master,");
            sb.AppendLine($"{Indent}{Indent}ref BehaviorTreeState state,");
            sb.AppendLine($"{Indent}{Indent}ref BTreeContext ctx,");
            sb.AppendLine($"{Indent}{Indent}int paramIndex)");
            sb.AppendLine($"{Indent}{{");
            sb.AppendLine($"{Indent}{Indent}ref var subBb = ref Unsafe.As<{dtoTypeName}, {dtoTypeName}>(ref master.{varName});");
            sb.AppendLine($"{Indent}{Indent}return {subTreeName}.GetInterpreter().Tick(ref subBb, ref state, ref ctx);");
            sb.AppendLine($"{Indent}}}");

            if (i < methods.Count - 1)
                sb.AppendLine();
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Writes the sidecar file using atomic write. No-op when <paramref name="sidecarContent"/> is
    /// <c>null</c> (no aliases; existing file is preserved).
    /// </summary>
    public static void WriteOrchestratorFile(HsmAsset asset, string? sidecarContent)
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
        if (sb.Length == 0) return "HsmAsset";
        if (char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.ToString();
    }

    private static string ShortTypeName(string fqn)
    {
        int last = fqn.LastIndexOf('.');
        return last >= 0 ? fqn[(last + 1)..] : fqn;
    }
}
