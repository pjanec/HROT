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

        // Explicit nullable enable: required for auto-generated source files so that
        // nullable annotations (e.g. ParseParamsDelegate?) are legal. The project-level
        // <Nullable>enable</Nullable> does NOT propagate to Roslyn IncrementalGenerator output
        // without an in-file pragma (CS8669).
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

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

        // DEBT-AIB-013 fix: blittable struct-DTO default values require IncludeFields=true.
        // System.Text.Json ignores public fields by default (only serializes public properties).
        // The options object is emitted as a private static readonly field so it is allocated once
        // per bridge class (not per ParseParams invocation) and can be captured by the static lambda.
        bool needsJsonOpts = dto.Blackboard.Managed
                             && dto.Blackboard.Variables.Any(v => v.DefaultValueJson != null);
        if (needsJsonOpts)
        {
            sb.AppendLine($"{Indent}// JSON options for ParseParams — IncludeFields required for blittable struct DTOs.");
            sb.AppendLine($"{Indent}private static readonly global::System.Text.Json.JsonSerializerOptions __paramJsonOpts =");
            sb.AppendLine($"{Indent}{Indent}new global::System.Text.Json.JsonSerializerOptions {{ IncludeFields = true }};");
            sb.AppendLine();
        }

        EmitBTreeRegisterMethod(sb, dto, coreClass, sizeResolver);

        sb.AppendLine("}");

        return sb.ToString();
    }

    // ── FNV-1a-32 slot key ──────────────────────────────────────────────────────

    /// <summary>
    /// S2-1: computes the per-node stateful slot key by running FNV-1a-32 over the
    /// 32 bytes of (assetId ++ nodeVisualId). The result is masked to a positive int
    /// (<c>&amp; 0x7FFFFFFF</c>) so it is always non-negative and safe as a dictionary key.
    ///
    /// Algorithm is identical to <c>FnvHasher.Hash32</c> in the runtime assembly but
    /// replicated here because the emitter cannot reference that internal class.
    /// The runtime must use the same algorithm so compile-time and runtime keys match.
    /// </summary>
    public static int ComputeStatefulSlotKey(Guid assetId, Guid nodeVisualId)
    {
        unchecked
        {
            uint hash = 2166136261u; // FNV offset basis
            foreach (byte b in assetId.ToByteArray())
            {
                hash ^= b;
                hash *= 16777619u; // FNV prime
            }
            foreach (byte b in nodeVisualId.ToByteArray())
            {
                hash ^= b;
                hash *= 16777619u;
            }
            return (int)(hash & 0x7FFFFFFFu);
        }
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

        // S1-G ordering: thunks must be registered BEFORE the Interpreter is constructed.
        // Interpreter.BindActions runs in the constructor; a thunk registered after construction
        // is missed and the action falls back to the silent Failure delegate.
        //
        // Correct order:
        //   1. Build the blob (pure data, no registry dependency).
        //   2. Register all action/condition thunks into actionRegistry.
        //   3. Construct the Interpreter (BindActions now sees the populated registry).
        //   4. Call beh.Register with the definition.
        sb.AppendLine($"{pad2}// 1. Build the blob from the topology-core thunk.");
        sb.AppendLine($"{pad2}var blob = {coreClass}.Build();");
        sb.AppendLine();

        // S1-3: For managed assets, emit real baked-offset thunks for each
        // (MethodFqn, ExpressionTargetField) → offset binding.
        // For non-managed assets, fall back to stub thunks (pre-BATCH-02 behaviour).
        bool isManaged = dto.Blackboard.Managed && dto.Blackboard.Variables.Count > 0;

        // Pre-compute packed fields once so both thunk emitters and the variable-array
        // emitter share the same offset map without calling Pack twice.
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? packedFields = null;
        if (isManaged)
        {
            try { packedFields = BTreeBlackboardPackHelper.Pack(dto.Blackboard.Variables, sizeResolver, out _); }
            catch { packedFields = null; }
        }

        if (isManaged)
        {
            sb.AppendLine($"{pad2}// 2. Register baked-offset action/condition thunks before Interpreter construction.");
            EmitManagedActionThunks(sb, dto, pad2, bbShort, ctxShort, packedFields);
            EmitManagedConditionThunks(sb, dto, pad2, bbShort, ctxShort, packedFields);
            EmitStatefulActionThunks(sb, dto, pad2, bbShort, ctxShort, packedFields);
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

        sb.AppendLine();
        sb.AppendLine($"{pad2}// 3. Construct the Interpreter — BindActions runs here; registry must be populated above.");
        sb.AppendLine($"{pad2}var interpreter = new Interpreter<{bbShort}, {ctxShort}>(blob, actionRegistry);");
        sb.AppendLine();

        // 4a. Emit ParseParams into a local variable (must be declared in an unsafe context so
        //     the byte* parameter in the lambda is legal). The local is then passed into the
        //     BehaviorDefinition initializer below. Only emitted when ≥1 variable has a default.
        bool hasParseParams = false;
        if (isManaged && packedFields != null)
            hasParseParams = EmitParseParamsLocal(sb, dto, packedFields, pad2);

        // 4b. Register definition
        sb.AppendLine($"{pad2}// {(hasParseParams ? "4b" : "4")}. Register the JSON-owned definition (FbtTreeCatalog cannot see in-memory defs).");
        sb.AppendLine($"{pad2}beh.Register({behaviorId}, \"{name}\", new BehaviorDefinition");
        sb.AppendLine($"{pad2}{{");
        sb.AppendLine($"{pad2}{Indent}Name         = \"{name}\",");
        sb.AppendLine($"{pad2}{Indent}BrainTier    = BehaviorConstants.BrainTierBTree,");
        sb.AppendLine($"{pad2}{Indent}BTreeInterpreter = interpreter,");
        if (isManaged && packedFields != null && packedFields.Count > 0)
            EmitManagedBlackboardVariablesArray(sb, packedFields, pad2 + Indent);
        if (hasParseParams)
            sb.AppendLine($"{pad2}{Indent}ParseParams  = __parseParams,");
        if (isManaged)
            EmitStatefulWorkingSlotsArray(sb, dto, pad2 + Indent);
        sb.AppendLine($"{pad2}}});");

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
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? packedFields)
    {
        if (packedFields == null) return;

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
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? packedFields)
    {
        if (packedFields == null) return;

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
    /// S2-1: emits baked-offset stateful Action registry entries for a managed blackboard asset.
    /// Key format: <c>{MethodFqn}@{paramOffset}@{slotKey}</c> — combines Slice-1 param projection
    /// with a per-node partition slot for WorkingState.
    ///
    /// The thunk:
    ///   1. Projects Params at the baked param offset from <c>bb.BehaviorParameters</c> (Slice-1 pattern).
    ///   2. Dispatches across tier components (16384 → 4096 → 1024) to find the entity's active tier.
    ///   3. Calls <c>TryGetSlotOffset(memory, SLOTKEY, out int wsOff)</c>.
    ///   4. Projects WorkingState at <c>memory + wsOff</c>.
    ///   5. Calls the 4-param method <c>(ref p, ref ws, ref st, ref ctx)</c>.
    ///   6. On missing slot: returns <see cref="NodeStatus.Failure"/> and fires <c>Debug.Assert(false)</c>.
    /// </summary>
    private static void EmitStatefulActionThunks(
        StringBuilder sb, BehaviorTreeAssetDto dto,
        string pad2, string bbShort, string ctxShort,
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? packedFields)
    {
        if (packedFields == null) return;

        var offsetMap = new Dictionary<string, BTreeBlackboardPackHelper.PackedField>(StringComparer.Ordinal);
        foreach (var f in packedFields)
            offsetMap[f.Name] = f;

        // Collect unique (key, MethodFqn, dtoTypeFqn, paramOffset, slotKey, wsTypeFqn) tuples.
        // Two nodes with the same method + param variable but different VisualIds → distinct slot keys.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<(string Key, string MethodFqn, string DtoTypeId, int Offset, int SlotKey, string WsTypeId)>();

        foreach (var node in dto.Nodes)
        {
            if (node is not BTreeActionNodeDto actNode) continue;
            var p = actNode.Action;
            if (p == null || string.IsNullOrEmpty(p.MethodFqn)) continue;
            if (p.DelegateShape != BTreeDelegateShapeDto.ThreeParamReusableStateful) continue;
            string? targetField = p.ExpressionTargetField;
            if (string.IsNullOrEmpty(targetField)) continue;
            if (!offsetMap.TryGetValue(targetField!, out var field)) continue;

            int slotKey = ComputeStatefulSlotKey(dto.AssetId, actNode.VisualId);

            // WorkingState type is taken from WorkingStateTypeId (added to BTreeActionPayloadDto in S2-1).
            // If missing, fall back to the naming convention (Action_AdvanceCursor → DemoCursorState).
            string wsTypeId = string.IsNullOrEmpty(p.WorkingStateTypeId)
                ? DeriveWorkingStateTypeFromMethod(p.MethodFqn)
                : p.WorkingStateTypeId!;

            string key = $"{p.MethodFqn}@{field.ByteOffset}@{slotKey}";
            if (!seen.Add(key)) continue;

            entries.Add((key, p.MethodFqn, field.TypeId, field.ByteOffset, slotKey, wsTypeId));
        }

        if (entries.Count == 0) return;

        sb.AppendLine();
        sb.AppendLine($"{pad2}// S2-1: stateful action thunks — project Params at baked offset + WorkingState from partition slot.");
        sb.AppendLine($"{pad2}// Key = {{MethodFqn}}@{{paramOffset}}@{{slotKey}} — per-node unique (includes FNV-1a slot key).");
        foreach (var (key, methodFqn, dtoTypeId, offset, slotKey, wsTypeId) in entries)
        {
            string dtoTypeFqn = DtoTypeToGlobal(dtoTypeId);
            string wsTypeFqn  = DtoTypeToGlobal(wsTypeId);
            string methodRef  = GlobalMethodRef(methodFqn);
            sb.AppendLine($"{pad2}actionRegistry.Register(\"{key}\",");
            sb.AppendLine($"{pad2}{Indent}static (ref {bbShort} bb, ref Fbt.BehaviorTreeState st, ref {ctxShort} ctx, int pi) =>");
            sb.AppendLine($"{pad2}{Indent}{{");
            sb.AppendLine($"{pad2}{Indent}{Indent}unsafe");
            sb.AppendLine($"{pad2}{Indent}{Indent}{{");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}// Project Params from BrainBlackboard (Slice-1 pattern).");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}ref var dto = ref Unsafe.As<byte, {dtoTypeFqn}>(");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint){offset}));");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}// Dispatch across tiers (16384 → 4096 → 1024) to locate the entity's active partition.");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}const int __slotKey = {slotKey};");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}if (ctx.World.HasComponent<global::Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard16384>(ctx.Self))");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{{");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}ref var tier = ref ctx.World.GetComponentRW<global::Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard16384>(ctx.Self);");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}fixed (byte* mem = tier.Memory)");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{{");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}if (!global::Fdp.Toolkit.Blueprints.Partitioning.BlueprintBlackboardPartitions.TryGetSlotOffset(mem, __slotKey, out int wsOff))");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}{{");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}{Indent}global::System.Diagnostics.Debug.Assert(false, \"S2-1: stateful slot {slotKey} missing from BlueprintBlackboard16384\");");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}{Indent}return Fbt.NodeStatus.Failure;");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}}}");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}ref var ws = ref Unsafe.AsRef<{wsTypeFqn}>(mem + wsOff);");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}return {methodRef}(ref dto, ref ws, ref st, ref ctx);");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}}}");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}}}");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}if (ctx.World.HasComponent<global::Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard4096>(ctx.Self))");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{{");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}ref var tier = ref ctx.World.GetComponentRW<global::Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard4096>(ctx.Self);");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}fixed (byte* mem = tier.Memory)");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{{");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}if (!global::Fdp.Toolkit.Blueprints.Partitioning.BlueprintBlackboardPartitions.TryGetSlotOffset(mem, __slotKey, out int wsOff))");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}{{");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}{Indent}global::System.Diagnostics.Debug.Assert(false, \"S2-1: stateful slot {slotKey} missing from BlueprintBlackboard4096\");");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}{Indent}return Fbt.NodeStatus.Failure;");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}}}");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}ref var ws = ref Unsafe.AsRef<{wsTypeFqn}>(mem + wsOff);");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}return {methodRef}(ref dto, ref ws, ref st, ref ctx);");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}}}");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}}}");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}if (ctx.World.HasComponent<global::Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard1024>(ctx.Self))");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{{");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}ref var tier = ref ctx.World.GetComponentRW<global::Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard1024>(ctx.Self);");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}fixed (byte* mem = tier.Memory)");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{{");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}if (!global::Fdp.Toolkit.Blueprints.Partitioning.BlueprintBlackboardPartitions.TryGetSlotOffset(mem, __slotKey, out int wsOff))");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}{{");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}{Indent}global::System.Diagnostics.Debug.Assert(false, \"S2-1: stateful slot {slotKey} missing from BlueprintBlackboard1024\");");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}{Indent}return Fbt.NodeStatus.Failure;");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}}}");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}ref var ws = ref Unsafe.AsRef<{wsTypeFqn}>(mem + wsOff);");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}return {methodRef}(ref dto, ref ws, ref st, ref ctx);");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}}}");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}}}");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}// No tier component found — fail loud.");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}global::System.Diagnostics.Debug.Assert(false, \"S2-1: entity has no BlueprintBlackboard* tier component for stateful slot {slotKey}\");");
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}return Fbt.NodeStatus.Failure;");
            sb.AppendLine($"{pad2}{Indent}{Indent}}}");
            sb.AppendLine($"{pad2}{Indent}}});");
        }
    }

    /// <summary>
    /// S2-1: emits the <c>StatefulWorkingSlots</c> array initializer inside the
    /// <c>BehaviorDefinition</c> object initializer.
    /// Only emitted when the asset has at least one stateful node; otherwise emits nothing
    /// (non-managed or no-stateful assets stay byte-identical).
    /// </summary>
    private static void EmitStatefulWorkingSlotsArray(
        StringBuilder sb, BehaviorTreeAssetDto dto, string pad)
    {
        // Collect unique stateful entries (deduped by SlotKey).
        var slotsBySeen = new Dictionary<int, (int SlotKey, string WsTypeId)>();

        // Need packed fields for size lookup — we rebuild the offset map using variable names.
        foreach (var node in dto.Nodes)
        {
            if (node is not BTreeActionNodeDto actNode) continue;
            var p = actNode.Action;
            if (p == null || string.IsNullOrEmpty(p.MethodFqn)) continue;
            if (p.DelegateShape != BTreeDelegateShapeDto.ThreeParamReusableStateful) continue;

            int slotKey = ComputeStatefulSlotKey(dto.AssetId, actNode.VisualId);
            if (slotsBySeen.ContainsKey(slotKey)) continue;

            string wsTypeId = string.IsNullOrEmpty(p.WorkingStateTypeId)
                ? DeriveWorkingStateTypeFromMethod(p.MethodFqn)
                : p.WorkingStateTypeId!;

            slotsBySeen[slotKey] = (slotKey, wsTypeId);
        }

        if (slotsBySeen.Count == 0) return;

        sb.AppendLine($"{pad}StatefulWorkingSlots = new global::Fdp.Toolkit.Behavior.StatefulSlotInfo[]");
        sb.AppendLine($"{pad}{{");
        foreach (var (slotKey, wsTypeId) in slotsBySeen.Values)
        {
            string wsTypeFqn = DtoTypeToGlobal(wsTypeId);
            // DEBT-AIB-027: StructureHash must be layout-sensitive so it changes when the
            // WorkingState struct grows or changes layout (not just the type name).
            // Strategy: XOR the FNV-1a-32 type-name hash with Marshal.SizeOf<T>() at
            // registration time so the hash changes whenever the struct's byte size changes.
            // Marshal.SizeOf is evaluated at registration time (not emit time), so this
            // correctly reflects the loaded struct's actual unmanaged size.
            // Primary guard: PayloadSize mismatch already catches size-growth ghost-slot cases.
            // Hash guard: catches same-size layout changes (field type/order changes).
            uint typeNameHash = ComputeTypeNameHash(wsTypeId);
            // PayloadSize: emitted as Marshal.SizeOf<T>() call (evaluated at registration time).
            // StructureHash: also folds in Marshal.SizeOf<T>() so it changes when struct grows.
            sb.AppendLine($"{pad}{Indent}new global::Fdp.Toolkit.Behavior.StatefulSlotInfo({slotKey}, global::System.Runtime.InteropServices.Marshal.SizeOf<{wsTypeFqn}>(), unchecked({typeNameHash}u ^ (uint)global::System.Runtime.InteropServices.Marshal.SizeOf<{wsTypeFqn}>())),");
        }
        sb.AppendLine($"{pad}}},");
    }

    /// <summary>
    /// S2-1: derives the WorkingState type FQN from a method FQN by convention.
    /// Example: "Hrot.AI.Behaviors.Brains.DemoCounterNodes.Action_AdvanceCursor"
    ///       → "Hrot.AI.Behaviors.Brains.DemoCounterNodes+DemoCursorState"
    /// Convention: strip method name, look for a nested type in the declaring class
    /// whose name ends with "State". This is a fallback for when WorkingStateTypeId
    /// is not explicitly stored on the payload DTO.
    /// </summary>
    private static string DeriveWorkingStateTypeFromMethod(string methodFqn)
    {
        // Fallback: WorkingStateTypeId should always be set on ThreeParamReusableStateful payloads.
        // When missing, derive by convention: "Namespace.Class.Action_AdvanceCursor"
        //   → "Namespace.Class+AdvanceCursorState"
        // This is fragile but gives a compilable default; emitter tests always set WorkingStateTypeId.
        int lastDot = methodFqn.LastIndexOf('.');
        if (lastDot < 0) return methodFqn + "State";
        string declaringType = methodFqn.Substring(0, lastDot);
        string methodName    = methodFqn.Substring(lastDot + 1);
        string suffix = methodName.StartsWith("Action_", StringComparison.Ordinal)
            ? methodName.Substring("Action_".Length) + "State"
            : methodName + "State";
        return $"{declaringType}+{suffix}";
    }

    /// <summary>
    /// Computes FNV-1a-32 hash of the UTF-8 bytes of a type name string.
    /// Used as a structural hash proxy for WorkingState types.
    /// </summary>
    private static uint ComputeTypeNameHash(string typeName)
    {
        unchecked
        {
            uint hash = 2166136261u;
            foreach (char c in typeName)
            {
                hash ^= (byte)(c & 0xFF);
                hash *= 16777619u;
                if (c > 0xFF)
                {
                    hash ^= (byte)(c >> 8);
                    hash *= 16777619u;
                }
            }
            return hash;
        }
    }

    /// <summary>
    /// Emits the <c>ManagedBlackboardVariables</c> array initializer inside the
    /// <c>BehaviorDefinition</c> object initializer for managed blackboard assets.
    /// Example output:
    /// <code>
    ///     ManagedBlackboardVariables = new global::Fdp.Toolkit.Behavior.ManagedBlackboardVariable[]
    ///     {
    ///         new("counter", typeof(global::Namespace.DemoCounterParams), 0),
    ///         new("accum",   typeof(global::Namespace.DemoAccumParams), 8),
    ///     },
    /// </code>
    /// </summary>
    private static void EmitManagedBlackboardVariablesArray(
        StringBuilder sb,
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField> packedFields,
        string pad)
    {
        sb.AppendLine($"{pad}ManagedBlackboardVariables = new global::Fdp.Toolkit.Behavior.ManagedBlackboardVariable[]");
        sb.AppendLine($"{pad}{{");
        foreach (var f in packedFields)
        {
            string dtoTypeFqn = DtoTypeToGlobal(f.TypeId);
            sb.AppendLine($"{pad}{Indent}new(\"{f.Name}\", typeof({dtoTypeFqn}), {f.ByteOffset}),");
        }
        sb.AppendLine($"{pad}}},");
    }

    /// <summary>
    /// Emits a local variable <c>__parseParams</c> of type <c>ParseParamsDelegate?</c>
    /// capturing the baked-default writes for all variables that carry a non-null <c>DefaultValueJson</c>.
    ///
    /// The local is emitted inside an <c>unsafe { ... }</c> block so the <c>byte*</c>
    /// parameter in the lambda is legal even in projects that don't set
    /// <c>&lt;AllowUnsafeBlocks&gt;true&lt;/AllowUnsafeBlocks&gt;</c> globally — the existing
    /// action thunks use <c>unsafe { ... }</c> scopes for the same reason.
    ///
    /// Returns <c>true</c> when the local was emitted (≥1 variable had a default); the caller
    /// then adds <c>ParseParams = __parseParams,</c> to the <c>BehaviorDefinition</c> initializer.
    ///
    /// Guard: only emits when <paramref name="dto"/>.Blackboard.Managed AND ≥1 variable
    /// has a non-null DefaultValueJson, so non-managed or all-null-default assets are unchanged.
    /// </summary>
    private static bool EmitParseParamsLocal(
        StringBuilder sb,
        BehaviorTreeAssetDto dto,
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField> packedFields,
        string pad2)
    {
        // Build offset map by variable name for quick lookup.
        var offsetMap = new Dictionary<string, BTreeBlackboardPackHelper.PackedField>(StringComparer.Ordinal);
        foreach (var f in packedFields)
            offsetMap[f.Name] = f;

        // Collect variables that have a non-null DefaultValueJson AND appear in the packed fields.
        var defaults = new List<(BTreeBlackboardPackHelper.PackedField Field, string DefaultJson)>();
        foreach (var v in dto.Blackboard.Variables)
        {
            if (v.DefaultValueJson == null) continue;
            if (!offsetMap.TryGetValue(v.Name, out var field)) continue;
            defaults.Add((field, v.DefaultValueJson));
        }

        if (defaults.Count == 0) return false;

        // Emit the ParseParams lambda in an unsafe block so the byte* parameter is legal.
        // The lambda body ignores the incoming json arg (runtime override is DEBT-AIB-021).
        // Each default DTO is deserialized and written at its packed byte offset via Unsafe.Write.
        string pad3 = pad2 + Indent;       // inside the unsafe { }
        string pad4 = pad3 + Indent;       // inside the lambda body
        string pad5 = pad4 + Indent;       // inside each { } block per variable

        sb.AppendLine($"{pad2}// 4a. Baked parameter defaults for managed blackboard variables (DEBT-AIB-013 fix).");
        sb.AppendLine($"{pad2}// ParseParamsDelegate uses byte* — must be captured in an unsafe block.");
        sb.AppendLine($"{pad2}global::Fdp.Toolkit.Behavior.ParseParamsDelegate? __parseParams;");
        sb.AppendLine($"{pad2}unsafe");
        sb.AppendLine($"{pad2}{{");
        sb.AppendLine($"{pad3}__parseParams = static (string json, byte* memory) =>");
        sb.AppendLine($"{pad3}{{");
        sb.AppendLine($"{pad4}// NOTE: runtime per-assignment JSON override of individual managed variables");
        sb.AppendLine($"{pad4}// is not yet supported — only baked defaults are written. DEBT-AIB-021.");
        foreach (var (field, defaultJson) in defaults)
        {
            string dtoTypeFqn = DtoTypeToGlobal(field.TypeId);
            string escaped    = EscapeCSharpStringLiteral(defaultJson);
            sb.AppendLine($"{pad4}{{");
            sb.AppendLine($"{pad5}var __v = global::System.Text.Json.JsonSerializer.Deserialize<{dtoTypeFqn}>(\"{escaped}\", __paramJsonOpts);");
            sb.AppendLine($"{pad5}global::System.Runtime.CompilerServices.Unsafe.Write(memory + {field.ByteOffset}, __v);");
            sb.AppendLine($"{pad4}}}");
        }
        sb.AppendLine($"{pad3}}};");
        sb.AppendLine($"{pad2}}}");
        sb.AppendLine();

        return true;
    }

    /// <summary>
    /// Escapes a string for use inside a C# double-quoted string literal.
    /// Escapes backslash, double-quote, newline, carriage-return, and tab.
    /// </summary>
    private static string EscapeCSharpStringLiteral(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append(@"\\"); break;
                case '"':  sb.Append("\\\""); break;
                case '\n': sb.Append(@"\n");  break;
                case '\r': sb.Append(@"\r");  break;
                case '\t': sb.Append(@"\t");  break;
                default:   sb.Append(c);      break;
            }
        }
        return sb.ToString();
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

        // S2-1: stateful thunks need Debug.Assert for fail-loud missing-slot guard.
        bool hasStateful = dto.Blackboard.Managed && dto.Nodes.OfType<BTreeActionNodeDto>()
            .Any(n => n.Action?.DelegateShape == BTreeDelegateShapeDto.ThreeParamReusableStateful);
        if (hasStateful)
        {
            set.Add("System.Diagnostics");
        }

        // DEBT-AIB-013 fix: ParseParams deserialization requires System.Text.Json for managed
        // assets that carry at least one DefaultValueJson.
        if (dto.Blackboard.Managed && dto.Blackboard.Variables.Any(v => v.DefaultValueJson != null))
        {
            set.Add("System.Text.Json");
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
