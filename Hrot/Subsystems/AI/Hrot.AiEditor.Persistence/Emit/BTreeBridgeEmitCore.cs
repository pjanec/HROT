using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hrot.AiEditor.Persistence.BTree;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// Emits the <c>[BlueprintRegistrar]</c> self-registration bridge class for a BTree asset.
/// Design §3 D14, §6.3, §14 (PU-203): emits a per-asset isolated static class decorated
/// <c>[BlueprintRegistrar]</c> (NOT <c>[FbtRegistrar]</c>/<c>[HsmActionRegistrar]</c>) with
/// <c>public static void Register(BehaviorRegistry beh, BlueprintRegistryStaging staging)</c>.
///
/// Inside Register:
/// - Builds the tree blob from the topology-core thunk, wraps it in an
///   <c>Interpreter&lt;BrainBlackboard, BTreeContext&gt;</c> and calls
///   <c>beh.Register(id, name, BehaviorDefinition)</c>.
/// - Registers each BTree action thunk via
///   <c>beh.RegisterAction(id, name, BlueprintBTreeActionDelegate)</c>.
/// - Registers each BTree condition thunk via
///   <c>beh.RegisterCondition(id, name, BlueprintBTreeConditionDelegate)</c>.
///
/// The bridge is ADDITIVE: it is a separate class from the topology-core class (PU-205
/// equivalence compares only the topology core; bridge is excluded per §14 item 3).
/// HSM bridge is analogous — see <see cref="HsmBridgeEmitCore"/>.
/// </summary>
public static class BTreeBridgeEmitCore
{
    private const string Indent = "    ";

    /// <summary>
    /// Emits the [BlueprintRegistrar] bridge class source for the given BTree DTO.
    /// Emits as a separate top-level file (separate hint name from the topology core).
    /// </summary>
    public static string EmitBridge(BehaviorTreeAssetDto dto)
    {
        var sb = new StringBuilder();

        // Header (same marker so the file is recognized as editor-generated)
        sb.AppendLine(AiEmitCoreBase.BuildHeader(dto.AssetId));

        // Usings
        var usings = CollectBridgeUsings(dto);
        foreach (var ns in usings)
        {
            if (ns.Length == 0)
                sb.AppendLine();
            else
                sb.AppendLine($"using {ns};");
        }
        sb.AppendLine();

        // Namespace + bridge class
        var targetNs  = string.IsNullOrEmpty(dto.TargetNamespace)
            ? "Hrot.AI.Behaviors.Trees"
            : dto.TargetNamespace;
        var coreClass = SanitizeIdentifier(dto.Name);
        var bridgeClass = coreClass + "Registrar";

        sb.AppendLine($"namespace {targetNs};");
        sb.AppendLine();

        // [BlueprintRegistrar] ONLY — not [FbtRegistrar]/[HsmActionRegistrar] (§14 item 4).
        sb.AppendLine($"[BlueprintRegistrar]");
        sb.AppendLine($"public static class {bridgeClass}");
        sb.AppendLine("{");

        EmitBTreeRegisterMethod(sb, dto, coreClass);

        sb.AppendLine("}");

        return sb.ToString();
    }

    // ── Register method ─────────────────────────────────────────────────────────

    private static void EmitBTreeRegisterMethod(
        StringBuilder sb, BehaviorTreeAssetDto dto, string coreClass)
    {
        string pad = Indent;
        string pad2 = Indent + Indent;

        // Deterministic behavior ID from the asset GUID (not string.GetHashCode()).
        int behaviorId = DeterministicIdFromGuid(dto.AssetId);
        string name    = dto.Name.Replace("\"", "\\\"");
        var bbShort    = ShortTypeName(dto.BlackboardTypeName);
        var ctxShort   = ShortTypeName(dto.ContextTypeName);

        sb.AppendLine($"{pad}/// <summary>");
        sb.AppendLine($"{pad}/// Coordinator-injectable registrar (§3 D14, PU-203).");
        sb.AppendLine($"{pad}/// Registers the JSON-owned BTree definition and action/condition thunks.");
        sb.AppendLine($"{pad}/// Called by <see cref=\"AiHotReloadCoordinator\"/> during hot reload.");
        sb.AppendLine($"{pad}/// </summary>");
        sb.AppendLine($"{pad}public static void Register(BehaviorRegistry beh, BlueprintRegistryStaging staging, ActionRegistry<{bbShort}, {ctxShort}> actionRegistry)");
        sb.AppendLine($"{pad}{{");

        // Build blob + interpreter. The action registry is injected by the coordinator/
        // scanner, pre-populated from this assembly's [FbtRegistrar] (FbtActionRegistrar),
        // so bound actions/conditions resolve to real logic instead of the Failure fallback.
        sb.AppendLine($"{pad2}// Build the blob from the topology-core thunk.");
        sb.AppendLine($"{pad2}var blob = {coreClass}.Build();");
        sb.AppendLine($"{pad2}var interpreter = new Interpreter<{bbShort}, {ctxShort}>(blob, actionRegistry);");
        sb.AppendLine();

        // Register definition
        sb.AppendLine($"{pad2}// Register the JSON-owned definition (FbtTreeCatalog cannot see in-memory defs).");
        sb.AppendLine($"{pad2}beh.Register({behaviorId}, \"{name}\", new BehaviorDefinition");
        sb.AppendLine($"{pad2}{{");
        sb.AppendLine($"{pad2}{Indent}Name         = \"{name}\",");
        sb.AppendLine($"{pad2}{Indent}BrainTier    = BehaviorConstants.BrainTierBTree,");
        sb.AppendLine($"{pad2}{Indent}BTreeInterpreter = interpreter,");
        sb.AppendLine($"{pad2}}});");

        // Action thunks
        var actions = CollectActions(dto);
        if (actions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"{pad2}// BTree action thunks — coordinator-injectable via BehaviorRegistry.RegisterAction.");
            int i = 0;
            foreach (var fqn in actions)
            {
                string thunkName = ShortMethodRef(fqn).Replace(".", "_") + "_Action";
                sb.AppendLine($"{pad2}beh.RegisterAction({behaviorId + i + 1}, \"{fqn}\",");
                sb.AppendLine($"{pad2}{Indent}(ref {bbShort} bb, ref Fbt.BehaviorTreeState st, ref {ctxShort} ctx, int pi) =>");
                sb.AppendLine($"{pad2}{Indent}{Indent}Fbt.NodeStatus.Success);");
                i++;
            }
        }

        // Condition thunks
        var conditions = CollectConditions(dto);
        if (conditions.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"{pad2}// BTree condition thunks — coordinator-injectable via BehaviorRegistry.RegisterCondition.");
            int i = 0;
            foreach (var fqn in conditions)
            {
                sb.AppendLine($"{pad2}beh.RegisterCondition({behaviorId + actions.Count + i + 1}, \"{fqn}\",");
                sb.AppendLine($"{pad2}{Indent}(ref {bbShort} bb, ref Fbt.BehaviorTreeState st, ref {ctxShort} ctx, int pi) =>");
                sb.AppendLine($"{pad2}{Indent}{Indent}true);");
                i++;
            }
        }

        sb.AppendLine($"{pad}}}");
    }

    // ── Usings ─────────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> CollectBridgeUsings(BehaviorTreeAssetDto dto)
    {
        var set = new HashSet<string>
        {
            "Fbt",
            "Fbt.Runtime",
            "Fdp.Toolkit.Behavior",
            "Fdp.Toolkit.Blueprints",
            "Fdp.Toolkit.Blueprints.Attributes",
        };

        // Namespaces from blackboard / context type names
        AddNamespaceFromTypeName(set, dto.BlackboardTypeName);
        AddNamespaceFromTypeName(set, dto.ContextTypeName);

        return AiEmitCoreBase.SortUsings(set);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes a deterministic int ID from a GUID using FNV-1a-32 over the 16 GUID bytes.
    /// NOT string.GetHashCode() (process-randomized). Satisfies DEBT-006 stable-ID rule.
    /// </summary>
    public static int DeterministicIdFromGuid(Guid assetId)
    {
        // FNV-1a-32 over the 16 bytes of the GUID
        byte[] bytes = assetId.ToByteArray();
        unchecked
        {
            uint hash = 2166136261u; // FNV offset basis
            foreach (byte b in bytes)
            {
                hash ^= b;
                hash *= 16777619u; // FNV prime
            }
            // Convert to non-negative int (preserve bit pattern, clear sign bit)
            return (int)(hash & 0x7FFFFFFFu);
        }
    }

    private static List<string> CollectActions(BehaviorTreeAssetDto dto)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var node in dto.Nodes)
        {
            if (node is BTreeActionNodeDto act && act.Action?.MethodFqn != null)
                set.Add(act.Action.MethodFqn);
        }
        return new List<string>(set);
    }

    private static List<string> CollectConditions(BehaviorTreeAssetDto dto)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var node in dto.Nodes)
        {
            if (node is BTreeConditionNodeDto cond && cond.Condition?.MethodFqn != null)
                set.Add(cond.Condition.MethodFqn);
        }
        return new List<string>(set);
    }

    private static void AddNamespaceFromTypeName(HashSet<string> set, string typeName)
    {
        int last = typeName.LastIndexOf('.');
        if (last > 0) set.Add(typeName.Substring(0, last));
    }

    private static string ShortTypeName(string fqn)
    {
        int last = fqn.LastIndexOf('.');
        return last >= 0 ? fqn.Substring(last + 1) : fqn;
    }

    private static string ShortMethodRef(string fqn)
    {
        int last = fqn.LastIndexOf('.');
        if (last <= 0) return fqn;
        return fqn.Substring(last + 1);
    }

    private static string SanitizeIdentifier(string name)
    {
        var sb = new StringBuilder();
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
        }
        if (sb.Length == 0) return "BTreeAsset";
        if (char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.ToString();
    }
}
