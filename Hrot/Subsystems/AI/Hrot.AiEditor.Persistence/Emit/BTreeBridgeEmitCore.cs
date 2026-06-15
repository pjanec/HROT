using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Emit;

namespace Hrot.AiEditor.Persistence.Emit;

// Alias to avoid repeating the long Func<> signature.
using SizeResolverDelegate = System.Func<string, int?>;

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
        => EmitBridge(dto, sizeResolver: null);

    /// <summary>
    /// Emits the [BlueprintRegistrar] bridge class source for the given BTree DTO,
    /// using an optional size resolver for struct-DTO types.
    /// </summary>
    public static string EmitBridge(BehaviorTreeAssetDto dto, SizeResolverDelegate? sizeResolver)
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

        EmitBTreeRegisterMethod(sb, dto, coreClass, sizeResolver);

        sb.AppendLine("}");

        return sb.ToString();
    }

    // ── Register method ─────────────────────────────────────────────────────────

    private static void EmitBTreeRegisterMethod(
        StringBuilder sb, BehaviorTreeAssetDto dto, string coreClass,
        SizeResolverDelegate? sizeResolver = null)
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

        // S1-3: For managed assets, emit real baked-offset thunks for each
        // (MethodFqn, ExpressionTargetField) → offset binding.
        // For non-managed assets, fall back to stub thunks (pre-BATCH-02 behaviour).
        bool isManaged = dto.Blackboard.Managed && dto.Blackboard.Variables.Count > 0;

        if (isManaged)
        {
            EmitManagedActionThunks(sb, dto, pad2, bbShort, ctxShort, sizeResolver);
            EmitManagedConditionThunks(sb, dto, pad2, bbShort, ctxShort, sizeResolver);
        }
        else
        {
            // Legacy stub thunks — byte-identical to pre-BATCH-02 output.
            var actions = CollectActions(dto);
            if (actions.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"{pad2}// BTree action thunks — coordinator-injectable via BehaviorRegistry.RegisterAction.");
                int i = 0;
                foreach (var fqn in actions)
                {
                    sb.AppendLine($"{pad2}beh.RegisterAction({behaviorId + i + 1}, \"{fqn}\",");
                    sb.AppendLine($"{pad2}{Indent}(ref {bbShort} bb, ref Fbt.BehaviorTreeState st, ref {ctxShort} ctx, int pi) =>");
                    sb.AppendLine($"{pad2}{Indent}{Indent}Fbt.NodeStatus.Success);");
                    i++;
                }
            }

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
        }

        sb.AppendLine($"{pad}}}");
    }

    /// <summary>
    /// S1-3: emits baked-offset Action registry entries for a managed blackboard asset.
    /// Key format: <c>{MethodFqn}@{offset}</c> — matches the blob key produced by BTreeEmitCore.
    /// Thunk: <c>Unsafe.As&lt;byte, TDto&gt;(ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint){offset}))</c>
    /// </summary>
    private static void EmitManagedActionThunks(
        StringBuilder sb, BehaviorTreeAssetDto dto,
        string pad2, string bbShort, string ctxShort,
        SizeResolverDelegate? sizeResolver = null)
    {
        // Build offset map once.
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField> packedFields;
        try
        {
            packedFields = BTreeBlackboardPackHelper.Pack(dto.Blackboard.Variables, sizeResolver, out _);
        }
        catch
        {
            return; // unknown type — skip (validator should have caught this)
        }

        var offsetMap = new Dictionary<string, BTreeBlackboardPackHelper.PackedField>(StringComparer.Ordinal);
        foreach (var f in packedFields)
            offsetMap[f.Name] = f;

        // Collect unique (key, MethodFqn, dtoTypeFqn, offset) tuples for actions.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<(string Key, string MethodFqn, string DtoTypeId, int Offset)>();

        foreach (var node in dto.Nodes)
        {
            if (node is not BTreeActionNodeDto actNode) continue;
            var p = actNode.Action;
            if (p == null || string.IsNullOrEmpty(p.MethodFqn)) continue;
            if (p.DelegateShape != BTreeDelegateShapeDto.ThreeParamReusable) continue;
            string? targetField = p.ExpressionTargetField;
            if (string.IsNullOrEmpty(targetField)) continue;
            if (!offsetMap.TryGetValue(targetField!, out var field)) continue;

            string key = $"{p.MethodFqn}@{field.ByteOffset}";
            if (!seen.Add(key)) continue;

            entries.Add((key, p.MethodFqn, field.TypeId, field.ByteOffset));
        }

        if (entries.Count == 0) return;

        sb.AppendLine();
        sb.AppendLine($"{pad2}// S1-3: baked-offset action thunks for managed blackboard.");
        sb.AppendLine($"{pad2}// Key = {{MethodFqn}}@{{offset}} — matches blob key from topology emit.");
        foreach (var (key, methodFqn, dtoTypeId, offset) in entries)
        {
            string dtoTypeFqn = DtoTypeToGlobal(dtoTypeId);
            string methodRef  = GlobalMethodRef(methodFqn);
            sb.AppendLine($"{pad2}actionRegistry.Register(\"{key}\",");
            sb.AppendLine($"{pad2}{Indent}static (ref {bbShort} bb, ref Fbt.BehaviorTreeState st, ref {ctxShort} ctx, int pi) =>");
            sb.AppendLine($"{pad2}{Indent}{{");
            sb.AppendLine($"{pad2}{Indent}{Indent}unsafe");
            sb.AppendLine($"{pad2}{Indent}{Indent}{{");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}ref var dto = ref Unsafe.As<byte, {dtoTypeFqn}>(");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint){offset}));");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}return {methodRef}(ref dto, ref st, ref ctx);");
            sb.AppendLine($"{pad2}{Indent}{Indent}}}");
            sb.AppendLine($"{pad2}{Indent}}});");
        }
    }

    /// <summary>
    /// S1-3: emits baked-offset Condition registry entries for a managed blackboard asset.
    /// Mirrors <see cref="EmitManagedActionThunks"/>.
    /// </summary>
    private static void EmitManagedConditionThunks(
        StringBuilder sb, BehaviorTreeAssetDto dto,
        string pad2, string bbShort, string ctxShort,
        SizeResolverDelegate? sizeResolver = null)
    {
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField> packedFields;
        try
        {
            packedFields = BTreeBlackboardPackHelper.Pack(dto.Blackboard.Variables, sizeResolver, out _);
        }
        catch
        {
            return;
        }

        var offsetMap = new Dictionary<string, BTreeBlackboardPackHelper.PackedField>(StringComparer.Ordinal);
        foreach (var f in packedFields)
            offsetMap[f.Name] = f;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<(string Key, string MethodFqn, string DtoTypeId, int Offset)>();

        foreach (var node in dto.Nodes)
        {
            if (node is not BTreeConditionNodeDto condNode) continue;
            var p = condNode.Condition;
            if (p == null || string.IsNullOrEmpty(p.MethodFqn)) continue;
            if (p.DelegateShape != BTreeDelegateShapeDto.ThreeParamReusable) continue;
            string? targetField = p.ExpressionTargetField;
            if (string.IsNullOrEmpty(targetField)) continue;
            if (!offsetMap.TryGetValue(targetField!, out var field)) continue;

            string key = $"{p.MethodFqn}@{field.ByteOffset}";
            if (!seen.Add(key)) continue;

            entries.Add((key, p.MethodFqn, field.TypeId, field.ByteOffset));
        }

        if (entries.Count == 0) return;

        sb.AppendLine();
        sb.AppendLine($"{pad2}// S1-3: baked-offset condition thunks for managed blackboard.");
        foreach (var (key, methodFqn, dtoTypeId, offset) in entries)
        {
            string dtoTypeFqn = DtoTypeToGlobal(dtoTypeId);
            string methodRef  = GlobalMethodRef(methodFqn);
            sb.AppendLine($"{pad2}actionRegistry.RegisterCondition(\"{key}\",");
            sb.AppendLine($"{pad2}{Indent}static (ref {bbShort} bb, ref Fbt.BehaviorTreeState st, ref {ctxShort} ctx, int pi) =>");
            sb.AppendLine($"{pad2}{Indent}{{");
            sb.AppendLine($"{pad2}{Indent}{Indent}unsafe");
            sb.AppendLine($"{pad2}{Indent}{Indent}{{");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}ref var dto = ref Unsafe.As<byte, {dtoTypeFqn}>(");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint){offset}));");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}return {methodRef}(ref dto, ref st, ref ctx);");
            sb.AppendLine($"{pad2}{Indent}{Indent}}}");
            sb.AppendLine($"{pad2}{Indent}}});");
        }
    }

    /// <summary>
    /// Converts a CLR type FQN to a global:: qualified C# type name for use in thunks.
    /// For struct-DTO types, converts nested-type separator <c>+</c> → <c>.</c> so the
    /// emitted C# is valid (e.g. <c>global::Hrot.AI.Behaviors.Brains.DemoCounterNodes.DemoCounterParams</c>).
    /// </summary>
    private static string DtoTypeToGlobal(string typeId)
    {
        // For well-known primitives, use keywords. For all others, qualify with global::.
        // S1-2b: nested type separator `+` must be converted to `.` for valid C# syntax.
        return typeId switch
        {
            "System.Int32"   or "int"    => "int",
            "System.UInt32"  or "uint"   => "uint",
            "System.Single"  or "float"  => "float",
            "System.Int64"   or "long"   => "long",
            "System.UInt64"  or "ulong"  => "ulong",
            "System.Double"  or "double" => "double",
            "System.Boolean" or "bool"   => "bool",
            "System.Byte"    or "byte"   => "byte",
            "System.SByte"   or "sbyte"  => "sbyte",
            "System.Int16"   or "short"  => "short",
            "System.UInt16"  or "ushort" => "ushort",
            "System.Char"    or "char"   => "char",
            // C# alias forms — mirror of BlackboardTypeHelper
            "Vector2"    => "global::System.Numerics.Vector2",
            "Vector3"    => "global::System.Numerics.Vector3",
            "Vector4"    => "global::System.Numerics.Vector4",
            "Quaternion" => "global::System.Numerics.Quaternion",
            _ => $"global::{typeId.Replace('+', '.')}",
        };
    }

    /// <summary>
    /// Converts a method FQN to a global:: qualified static method reference.
    /// E.g. "Hrot.AI.Behaviors.Brains.DemoCounterNodes.Action_IncrementCounter"
    /// → "global::Hrot.AI.Behaviors.Brains.DemoCounterNodes.Action_IncrementCounter"
    /// </summary>
    private static string GlobalMethodRef(string methodFqn)
    {
        return $"global::{methodFqn}";
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

        // S1-3: managed assets need Unsafe for baked-offset thunks.
        if (dto.Blackboard.Managed && dto.Blackboard.Variables.Count > 0)
        {
            set.Add("System.Runtime.CompilerServices");
        }

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
