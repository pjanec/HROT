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
/// - Registers each BTree action/condition thunk (managed assets) into the FastBTree
///   <c>actionRegistry</c> at its baked <c>{MethodFqn}@{offset}[@{slotKey}]</c> key; the
///   Interpreter binds against that registry. (Non-managed assets bind through the
///   assembly's <c>[FbtRegistrar]</c> node logic and emit no per-asset thunks.)
/// - Registers <c>[BTreeDeactivator]</c> hooks via
///   <c>actionRegistry.RegisterDeactivator(key, delegate)</c> so that JSON-authored trees
///   fire cleanup callbacks on branch abort, matching the hardcoded-tree path.
///
/// The bridge is ADDITIVE: it is a separate class from the topology-core class (PU-205
/// equivalence compares only the topology core; bridge is excluded per §14 item 3).
/// HSM bridge is analogous — see <see cref="HsmBridgeEmitCore"/>.
/// </summary>
public static class BTreeBridgeEmitCore
{
    private const string Indent = "    ";

    /// <summary>Describes a deactivator discovered in the compilation for this asset.</summary>
    public sealed class DeactivatorEntry
    {
        /// <summary>The exact key the action is registered under (e.g. "Ns.Class.Method@8").</summary>
        public string ActionKey { get; set; } = string.Empty;

        /// <summary>FQN of the deactivator method (e.g. "Ns.Class.Deactivate_Method").</summary>
        public string DeactivatorFqn { get; set; } = string.Empty;

        /// <summary>
        /// Number of parameters on the deactivator method.
        ///   4 → FourParamFull shape: (ref TBB, ref BehaviorTreeState, ref TCtx, int)
        ///       → registered directly as NodeDeactivatorDelegate.
        ///   3 → ThreeParamReusable shape: (ref TDto, ref BehaviorTreeState, ref TCtx)
        ///       → bridge emits a wrapper lambda projecting TDto at <see cref="DtoByteOffset"/>.
        ///   5 → ThreeParamReusableStateful shape (S3-G):
        ///       (ref TParams, ref TWorkingState, ref BehaviorTreeState, ref TCtx, int)
        ///       → bridge emits a wrapper that projects TParams at <see cref="DtoByteOffset"/> AND the
        ///         working state from the paired stateful node's partition slot, then registers under the
        ///         node's full stateful key {fqn}@{offset}@{slotKey} (so the interpreter's
        ///         deactivator lookup by node MethodName resolves it).
        /// </summary>
        public int ParamCount { get; set; }

        /// <summary>
        /// Global C# type name of param-0 for 3-param deactivators (e.g. "global::Ns.EqsParams") and the
        /// params DTO for 5-param stateful deactivators. Null for 4-param deactivators (full blackboard).
        /// </summary>
        public string? DtoTypeFqn { get; set; }

        /// <summary>
        /// Byte offset of the DTO within the blackboard for 3-param and 5-param deactivators.
        /// Extracted from the suffix of <see cref="ActionKey"/> after the last '@'.
        /// Zero for 4-param deactivators (not used).
        /// </summary>
        public int DtoByteOffset { get; set; }

        /// <summary>
        /// (S3-G) Global C# type name of the working-state param (param-1) for 5-param stateful
        /// deactivators, e.g. "global::Ns.HillAttackMutableState". Null for 3-/4-param deactivators.
        /// </summary>
        public string? WorkingStateTypeFqn { get; set; }
    }

    /// <summary>
    /// Emits the [BlueprintRegistrar] bridge class source for the given BTree DTO.
    /// Emits as a separate top-level file (separate hint name from the topology core).
    /// </summary>
    public static string EmitBridge(BehaviorTreeAssetDto dto)
        => EmitBridge(dto, sizeResolver: null, deactivators: null);

    /// <summary>
    /// Emits the [BlueprintRegistrar] bridge class source for the given BTree DTO,
    /// using an optional size resolver for struct-DTO types.
    /// </summary>
    public static string EmitBridge(BehaviorTreeAssetDto dto, SizeResolverDelegate? sizeResolver)
        => EmitBridge(dto, sizeResolver, deactivators: null);

    /// <summary>
    /// Emits the [BlueprintRegistrar] bridge class source for the given BTree DTO,
    /// using an optional size resolver and an optional list of pre-scanned deactivator entries.
    ///
    /// When <paramref name="deactivators"/> is non-null and non-empty the emitter outputs
    /// <c>actionRegistry.RegisterDeactivator(…)</c> calls for each entry so JSON-authored
    /// trees fire cleanup callbacks on branch abort (HAJSON-B fix).
    /// When null the deactivator section is omitted (backward-compatible with callers that
    /// do not have Roslyn available, e.g. unit tests that call EmitBridge directly).
    /// </summary>
    public static string EmitBridge(BehaviorTreeAssetDto dto, SizeResolverDelegate? sizeResolver,
        IReadOnlyList<DeactivatorEntry>? deactivators)
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
        var usings = CollectBridgeUsings(dto, deactivators);
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
            sb.AppendLine($"{Indent}// JSON options for ParseParams — the platform-canonical options (IncludeFields,");
            sb.AppendLine($"{Indent}// vector/FixedString/strict-enum converters, and FC-3b fixed-list support) so");
            sb.AppendLine($"{Indent}// Params defaults share ONE wire format with scenario save/load.");
            sb.AppendLine($"{Indent}private static readonly global::System.Text.Json.JsonSerializerOptions __paramJsonOpts =");
            sb.AppendLine($"{Indent}{Indent}global::Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;");
            sb.AppendLine();
        }

        EmitBTreeRegisterMethod(sb, dto, coreClass, sizeResolver, deactivators);

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

    /// <summary>
    /// S3-2: scope-aware overload.  Derives the stateful slot key according to the
    /// variable's declared <see cref="WorkingStateScope"/>:
    ///
    /// <list type="bullet">
    ///   <item><term><see cref="WorkingStateScope.Node"/></term>
    ///     <description>FNV-1a-32(assetId bytes ++ nodeVisualId bytes) — byte-identical
    ///     to the existing 2-arg overload (S2 keys are preserved).</description></item>
    ///   <item><term><see cref="WorkingStateScope.Behavior"/></term>
    ///     <description>FNV-1a-32(assetId bytes ++ variableId UTF-8 bytes) — shared by
    ///     every node in the same asset that binds the same variable.</description></item>
    ///   <item><term><see cref="WorkingStateScope.Entity"/></term>
    ///     <description>FNV-1a-32(variableId UTF-8 bytes only) — survives a behavior
    ///     switch; assetId is intentionally excluded (post-MVP).</description></item>
    /// </list>
    ///
    /// Result is always masked to a non-negative int (<c>&amp; 0x7FFFFFFF</c>).
    /// <paramref name="nodeVisualId"/> is only consumed for <see cref="WorkingStateScope.Node"/>;
    /// pass <see cref="Guid.Empty"/> for other scopes.
    /// <paramref name="variableId"/> is the binding's <c>ExpressionTargetField</c> (variable Name).
    /// </summary>
    public static int ComputeStatefulSlotKey(
        Guid assetId,
        WorkingStateScope scope,
        Guid nodeVisualId,
        string variableId)
    {
        unchecked
        {
            uint hash = 2166136261u; // FNV offset basis
            const uint Prime = 16777619u;

            switch (scope)
            {
                case WorkingStateScope.Node:
                    // Byte-identical to the 2-arg overload — delegates to it to guarantee parity.
                    return ComputeStatefulSlotKey(assetId, nodeVisualId);

                case WorkingStateScope.Behavior:
                    foreach (byte b in assetId.ToByteArray())
                    {
                        hash ^= b;
                        hash *= Prime;
                    }
                    foreach (byte b in System.Text.Encoding.UTF8.GetBytes(variableId))
                    {
                        hash ^= b;
                        hash *= Prime;
                    }
                    return (int)(hash & 0x7FFFFFFFu);

                case WorkingStateScope.Entity:
                    foreach (byte b in System.Text.Encoding.UTF8.GetBytes(variableId))
                    {
                        hash ^= b;
                        hash *= Prime;
                    }
                    return (int)(hash & 0x7FFFFFFFu);

                default:
                    throw new System.ArgumentOutOfRangeException(nameof(scope), scope, null);
            }
        }
    }

    /// <summary>
    /// S3-4: resolves the scope-aware stateful slot key for a binding. Looks up the bound
    /// variable (by Name == <paramref name="targetField"/>) in the asset blackboard; a
    /// State-role variable contributes its declared <see cref="WorkingStateScope"/>. Any other
    /// case (Input role, variable absent, null target) falls back to <see cref="WorkingStateScope.Node"/>,
    /// which yields the byte-identical legacy per-node key (Slice-2 untouched).
    /// </summary>
    /// <summary>
    /// S3-G: the variable whose declared Role/Scope govern a stateful node's slot key. Prefers the
    /// explicit working-state variable (<see cref="BTreeActionPayloadDto.WorkingStateTargetField"/>)
    /// when the behavior separates params from working state (e.g. Hill Attack); falls back to the
    /// param field so Slice-2 assets and the conflated Slice-3 tests stay byte-identical.
    /// </summary>
    internal static string? StatefulScopeVariable(BTreeActionPayloadDto p)
        => string.IsNullOrEmpty(p.WorkingStateTargetField) ? p.ExpressionTargetField : p.WorkingStateTargetField;

    /// <summary>
    /// Slice 1 (shared working-state): condition-side mirror of
    /// <see cref="StatefulScopeVariable(BTreeActionPayloadDto)"/>. Prefers the explicit
    /// working-state variable (<see cref="BTreeConditionPayloadDto.WorkingStateTargetField"/>) when
    /// the composed condition separates params from working state; falls back to
    /// <see cref="BTreeConditionPayloadDto.ExpressionTargetField"/> so pre-Slice-1 condition assets
    /// (no WorkingStateTargetField authored) stay byte-identical.
    /// </summary>
    internal static string? StatefulScopeVariable(BTreeConditionPayloadDto p)
        => string.IsNullOrEmpty(p.WorkingStateTargetField) ? p.ExpressionTargetField : p.WorkingStateTargetField;

    internal static int ResolveStatefulSlotKey(BehaviorTreeAssetDto dto, string? targetField, Guid nodeVisualId)
    {
        var scope = WorkingStateScope.Node;
        if (!string.IsNullOrEmpty(targetField) && dto.Blackboard?.Variables != null)
        {
            foreach (var v in dto.Blackboard.Variables)
            {
                if (string.Equals(v.Name, targetField, StringComparison.Ordinal) && v.Role == BlackboardVariableRole.State)
                {
                    scope = v.Scope;
                    break;
                }
            }
        }
        return ComputeStatefulSlotKey(dto.AssetId, scope, nodeVisualId, targetField ?? string.Empty);
    }

    /// <summary>
    /// S3-7: resolves the authored (Role, Scope) of the bound variable for the live inspector
    /// manifest. Returns the integer enum values (Role: 0=Input,1=State; Scope: 0=Node,1=Behavior,
    /// 2=Entity). Defaults to (Input, Node) when the variable is absent — matching a legacy slot.
    /// </summary>
    internal static (int Role, int Scope) ResolveVariableRoleScope(BehaviorTreeAssetDto dto, string? targetField)
    {
        var role = BlackboardVariableRole.Input;
        var scope = WorkingStateScope.Node;
        if (!string.IsNullOrEmpty(targetField) && dto.Blackboard?.Variables != null)
        {
            foreach (var v in dto.Blackboard.Variables)
            {
                if (string.Equals(v.Name, targetField, StringComparison.Ordinal))
                {
                    role = v.Role;
                    scope = v.Scope;
                    break;
                }
            }
        }
        return ((int)role, (int)scope);
    }

    // ── Register method ─────────────────────────────────────────────────────────

    private static void EmitBTreeRegisterMethod(
        StringBuilder sb, BehaviorTreeAssetDto dto, string coreClass,
        SizeResolverDelegate? sizeResolver = null,
        IReadOnlyList<DeactivatorEntry>? deactivators = null)
    {
        string pad = Indent;
        string pad2 = Indent + Indent;

        // Deterministic behavior ID from the asset GUID (not string.GetHashCode()).
        int behaviorId = DeterministicIdFromGuid(dto.AssetId);
        string name    = dto.Name.Replace("\"", "\\\"");
        var bbShort    = ShortTypeName(AiEmitCoreBase.EffectiveBlackboardTypeName(dto.BlackboardTypeName));
        var ctxShort   = ShortTypeName(AiEmitCoreBase.EffectiveContextTypeName(dto.ContextTypeName));

        sb.AppendLine($"{pad}/// <summary>");
        sb.AppendLine($"{pad}/// Coordinator-injectable registrar (§3 D14, PU-203).");
        sb.AppendLine($"{pad}/// Registers the JSON-owned BTree definition and action/condition thunks.");
        sb.AppendLine($"{pad}/// Called by <see cref=\"AiHotReloadCoordinator\"/> during hot reload.");
        sb.AppendLine($"{pad}/// </summary>");
        sb.AppendLine($"{pad}public static void Register(BehaviorRegistry beh, BlueprintRegistryStaging staging, ActionRegistry<{bbShort}, {ctxShort}> actionRegistry)");
        sb.AppendLine($"{pad}{{");

        bool hasDeactivators = deactivators != null && deactivators.Count > 0;

        // S1-G ordering: thunks must be registered BEFORE the Interpreter is constructed.
        // Interpreter.BindActions runs in the constructor; a thunk registered after construction
        // is missed and the action falls back to the silent Failure delegate.
        //
        // Correct order:
        //   1. Build the blob (pure data, no registry dependency).
        //   2. Register all action/condition thunks into actionRegistry.
        //   3. Construct the Interpreter (BindActions now sees the populated registry).
        //   4. Call beh.Register with the definition.
        //
        // HAJSON-B: when the asset has deactivators, the blob is instead compiled AFTER the
        // deactivators are registered (see below) so FastBTree's Compile(treeName, isResourceOwning)
        // seam can bake the resource-owning bit — the interpreter fires deactivators only for
        // resource-owning nodes. Assets without deactivators keep the byte-identical Build() here.
        sb.AppendLine($"{pad2}// 1. Build the blob from the topology-core thunk.");
        if (!hasDeactivators)
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
            EmitBlueprintActionThunks(sb, dto, pad2, bbShort, ctxShort, packedFields);
            EmitBlueprintConditionThunks(sb, dto, pad2, bbShort, ctxShort, packedFields);
        }
        else
        {
            // Non-managed (hand-written-struct) trees bind their action/condition delegates
            // through the FastBTree ActionRegistry (populated from the assembly's [FbtRegistrar]
            // node logic and injected into the Interpreter below), keyed by the method names
            // baked into the blob. No per-asset registration is emitted here: the former
            // BehaviorRegistry.RegisterAction/RegisterCondition stubs (always Success/true) were
            // never read by any binding path and have been removed.
        }

        // HAJSON-B: Register deactivator hooks for every action/condition key that has a
        // paired [BTreeDeactivator]-annotated method.
        // Must be registered BEFORE Interpreter construction (same ordering rule as thunks).
        if (hasDeactivators)
        {
            EmitDeactivatorRegistrations(sb, dto, packedFields, deactivators!, pad2, bbShort, ctxShort);

            // HAJSON-B: compile the blob now that the deactivators are in the registry, so the
            // resource-owning bit is baked for every node whose method has a paired deactivator.
            // The interpreter fires deactivators only for resource-owning nodes on branch abort/exit;
            // Compile(treeName, isResourceOwning) is FastBTree's existing seam (no ExtDep change).
            sb.AppendLine();
            sb.AppendLine($"{pad2}// 2b. Compile the blob AFTER deactivators are registered → resource-owning baked.");
            sb.AppendLine($"{pad2}var blob = {coreClass}.CreateBuilder().Compile(\"{name}\", __k => actionRegistry.TryGetDeactivator(__k, out _));");
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
        sb.AppendLine($"{pad2}beh.Register(global::Fdp.Toolkit.Behavior.BehaviorHash.FromName(\"{name}\"), \"{name}\", new BehaviorDefinition");
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

            // S3-3: scope-aware baked const — Behavior-scoped co-bound nodes bake the same key
            // (and dedup via `seen` below), so they dispatch to one thunk over one shared slot.
            // Must stay in lockstep with the topology blob key in BTreeEmitCore.EmitAction.
            // S3-G: scope is governed by the working-state variable when distinct from params.
            int slotKey = ResolveStatefulSlotKey(dto, StatefulScopeVariable(p), actNode.VisualId);

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
            AppendReusableStatefulThunk(sb, pad2, bbShort, ctxShort, key, dtoTypeFqn, offset, slotKey, wsTypeFqn,
                $"{methodRef}(ref dto, ref ws, ref st, ref ctx)");
        }
    }

    /// <summary>
    /// Emits one <c>actionRegistry.Register("{key}", static (...) =&gt; {...})</c> reusable-stateful
    /// thunk: projects Params at the baked <paramref name="offset"/> from <c>bb.BehaviorParameters</c>,
    /// locates the entity's WorkingState partition slot across the 16384→4096→1024 tiers, and returns
    /// <paramref name="callExpr"/> (the node call, with <c>dto</c>/<c>ws</c>/<c>st</c>/<c>ctx</c> in scope).
    /// Shared by the S2-1 stateful path and the I2/I3 blueprint-AiPrimitive path — the only difference
    /// between them is <paramref name="callExpr"/>.
    /// </summary>
    private static void AppendReusableStatefulThunk(
        StringBuilder sb, string pad2, string bbShort, string ctxShort,
        string key, string dtoTypeFqn, int offset, int slotKey, string wsTypeFqn, string callExpr)
    {
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
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}return {callExpr};");
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
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}return {callExpr};");
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
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}return {callExpr};");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}}}");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}}}");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}// No tier component found — fail loud.");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}global::System.Diagnostics.Debug.Assert(false, \"S2-1: entity has no BlueprintBlackboard* tier component for stateful slot {slotKey}\");");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}return Fbt.NodeStatus.Failure;");
        sb.AppendLine($"{pad2}{Indent}{Indent}}}");
        sb.AppendLine($"{pad2}{Indent}}});");
    }

    /// <summary>
    /// Condition-side sibling of <see cref="AppendReusableStatefulThunk"/>: emits one
    /// <c>actionRegistry.RegisterCondition("{key}", static (...) =&gt; {...})</c> reusable-stateful
    /// thunk with the identical Params/WorkingState projection + tier-dispatch scaffold. The
    /// registered delegate is still a <c>NodeLogicDelegate&lt;TBlackboard,TContext&gt;</c> returning
    /// <see cref="Fbt.NodeStatus"/> — <c>ActionRegistry.RegisterCondition</c> is internally identical
    /// to <c>Register</c> (see Fbt.Runtime.ActionRegistry) — but <paramref name="callExpr"/> evaluates
    /// to <c>bool</c> (e.g. <c>TickCore(...) == Fbt.NodeStatus.Success</c>, collapsing a Running result
    /// to false same as Failure) and this helper converts it back to NodeStatus.Success/Failure,
    /// mirroring the bool→NodeStatus wrapping <c>Hrot.Blueprints.Compiler</c> already emits for plain
    /// (non-composed) blueprint BTreeCondition hosting. Kept as a dedicated sibling — rather than a
    /// bool/branch parameter on the action helper — so the validated action path stays byte-identical.
    /// </summary>
    private static void AppendReusableStatefulConditionThunk(
        StringBuilder sb, string pad2, string bbShort, string ctxShort,
        string key, string dtoTypeFqn, int offset, int slotKey, string wsTypeFqn, string callExpr)
    {
        sb.AppendLine($"{pad2}actionRegistry.RegisterCondition(\"{key}\",");
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
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}{Indent}global::System.Diagnostics.Debug.Assert(false, \"E2: stateful condition slot {slotKey} missing from BlueprintBlackboard16384\");");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}{Indent}return Fbt.NodeStatus.Failure;");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}}}");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}ref var ws = ref Unsafe.AsRef<{wsTypeFqn}>(mem + wsOff);");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}return {callExpr} ? Fbt.NodeStatus.Success : Fbt.NodeStatus.Failure;");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}}}");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}}}");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}if (ctx.World.HasComponent<global::Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard4096>(ctx.Self))");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{{");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}ref var tier = ref ctx.World.GetComponentRW<global::Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard4096>(ctx.Self);");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}fixed (byte* mem = tier.Memory)");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{{");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}if (!global::Fdp.Toolkit.Blueprints.Partitioning.BlueprintBlackboardPartitions.TryGetSlotOffset(mem, __slotKey, out int wsOff))");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}{{");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}{Indent}global::System.Diagnostics.Debug.Assert(false, \"E2: stateful condition slot {slotKey} missing from BlueprintBlackboard4096\");");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}{Indent}return Fbt.NodeStatus.Failure;");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}}}");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}ref var ws = ref Unsafe.AsRef<{wsTypeFqn}>(mem + wsOff);");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}return {callExpr} ? Fbt.NodeStatus.Success : Fbt.NodeStatus.Failure;");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}}}");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}}}");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}if (ctx.World.HasComponent<global::Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard1024>(ctx.Self))");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{{");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}ref var tier = ref ctx.World.GetComponentRW<global::Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard1024>(ctx.Self);");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}fixed (byte* mem = tier.Memory)");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{{");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}if (!global::Fdp.Toolkit.Blueprints.Partitioning.BlueprintBlackboardPartitions.TryGetSlotOffset(mem, __slotKey, out int wsOff))");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}{{");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}{Indent}global::System.Diagnostics.Debug.Assert(false, \"E2: stateful condition slot {slotKey} missing from BlueprintBlackboard1024\");");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}{Indent}return Fbt.NodeStatus.Failure;");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}}}");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}ref var ws = ref Unsafe.AsRef<{wsTypeFqn}>(mem + wsOff);");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}return {callExpr} ? Fbt.NodeStatus.Success : Fbt.NodeStatus.Failure;");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}}}");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}}}");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}// No tier component found — fail loud.");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}global::System.Diagnostics.Debug.Assert(false, \"E2: entity has no BlueprintBlackboard* tier component for stateful condition slot {slotKey}\");");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}return Fbt.NodeStatus.Failure;");
        sb.AppendLine($"{pad2}{Indent}{Indent}}}");
        sb.AppendLine($"{pad2}{Indent}}});");
    }

    /// <summary>
    /// I2/I3: emits reusable-stateful thunks for host-BTree nodes that compose a blueprint-authored
    /// AiPrimitive action (<see cref="BTreeDelegateShapeDto.AiPrimitiveTickCore"/>). Identical
    /// projection/slot scaffold as <see cref="EmitStatefulActionThunks"/>, but the final call
    /// dispatches to the blueprint's generated <c>TickCore(ref Params, ref WorkingState, Entity self,
    /// EntityRepository world, float time)</c>. The blueprint owns Params/WorkingState; the host BTree
    /// owns the param offset + partition slot. Only emitted for AiPrimitiveTickCore nodes, so assets
    /// without them stay byte-identical.
    /// </summary>
    private static void EmitBlueprintActionThunks(
        StringBuilder sb, BehaviorTreeAssetDto dto,
        string pad2, string bbShort, string ctxShort,
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? packedFields)
    {
        if (packedFields == null) return;

        var offsetMap = new Dictionary<string, BTreeBlackboardPackHelper.PackedField>(StringComparer.Ordinal);
        foreach (var f in packedFields)
            offsetMap[f.Name] = f;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<(string Key, string MethodFqn, string DtoTypeId, int Offset, int SlotKey, string WsTypeId, int PredictedSize)>();

        foreach (var node in dto.Nodes)
        {
            if (node is not BTreeActionNodeDto actNode) continue;
            var p = actNode.Action;
            if (p == null || string.IsNullOrEmpty(p.MethodFqn)) continue;
            if (p.DelegateShape != BTreeDelegateShapeDto.AiPrimitiveTickCore) continue;
            string? targetField = p.ExpressionTargetField;
            if (string.IsNullOrEmpty(targetField)) continue;
            if (!offsetMap.TryGetValue(targetField!, out var field)) continue;

            int slotKey = ResolveStatefulSlotKey(dto, StatefulScopeVariable(p), actNode.VisualId);
            // WorkingState type is the blueprint's generated WorkingState struct FQN (authored on the node).
            string wsTypeId = string.IsNullOrEmpty(p.WorkingStateTypeId)
                ? DeriveWorkingStateTypeFromMethod(p.MethodFqn)
                : p.WorkingStateTypeId!;

            string key = $"{p.MethodFqn}@{field.ByteOffset}@{slotKey}";
            if (!seen.Add(key)) continue;

            entries.Add((key, p.MethodFqn, field.TypeId, field.ByteOffset, slotKey, wsTypeId, field.ByteSize));
        }

        if (entries.Count == 0) return;

        sb.AppendLine();
        sb.AppendLine($"{pad2}// I2/I3: blueprint AiPrimitive action thunks — Params at baked offset + WorkingState from");
        sb.AppendLine($"{pad2}// partition slot, dispatched to the blueprint's generated TickCore. Key = {{MethodFqn}}@{{offset}}@{{slotKey}}.");
        foreach (var (key, methodFqn, dtoTypeId, offset, slotKey, wsTypeId, predictedSize) in entries)
        {
            string dtoTypeFqn = DtoTypeToGlobal(dtoTypeId);
            string wsTypeFqn  = DtoTypeToGlobal(wsTypeId);
            string methodRef  = GlobalMethodRef(methodFqn);

            // AAR integrity (architect-mandated, fail-loud): the composed blueprint Params struct is
            // produced by the *blueprint* incremental generator, which the BTree generator cannot see at
            // emit time — so the baked param offset ({offset}) and field size are computed from the
            // .bp.json schema (an *advisory* prediction). The compiled struct layout is authoritative.
            // Validate predicted == reflected once at registration; a mismatch means the two generators
            // disagree on layout and every projection at this offset would corrupt the AAR schema, so we
            // fail startup loudly rather than read/write past the baked slot.
            //
            // Exception: a zero-param blueprint predicts 0 bytes, but the CLR reflects a fieldless struct
            // as 1 byte (its minimum). Such a Params has no fields whose offsets could drift, so there is
            // nothing to corrupt — skip the guard rather than emit a check that always throws.
            if (predictedSize > 0)
            {
                sb.AppendLine($"{pad2}if (global::System.Runtime.InteropServices.Marshal.SizeOf<{dtoTypeFqn}>() != {predictedSize})");
                sb.AppendLine($"{pad2}{Indent}throw new global::System.InvalidOperationException(");
                sb.AppendLine($"{pad2}{Indent}{Indent}\"Composed blueprint Params layout drift: {dtoTypeFqn} compiled size (\" +");
                sb.AppendLine($"{pad2}{Indent}{Indent}global::System.Runtime.InteropServices.Marshal.SizeOf<{dtoTypeFqn}>() +");
                sb.AppendLine($"{pad2}{Indent}{Indent}\") != predicted {predictedSize} bytes baked at offset {offset}. \" +");
                sb.AppendLine($"{pad2}{Indent}{Indent}\"Rebuild the behavior blackboard layout so predicted and reflected sizes agree.\");");
            }
            AppendReusableStatefulThunk(sb, pad2, bbShort, ctxShort, key, dtoTypeFqn, offset, slotKey, wsTypeFqn,
                $"{methodRef}(ref dto, ref ws, ctx.Self, ctx.World, ctx.World.SimulationTime)");
        }
    }

    /// <summary>
    /// E2: emits reusable-stateful thunks for host-BTree CONDITION nodes that compose a
    /// blueprint-authored AiPrimitive (<see cref="BTreeDelegateShapeDto.AiPrimitiveTickCore"/>).
    /// Mirrors <see cref="EmitBlueprintActionThunks"/> exactly — same Params/WorkingState
    /// projection + partition-slot scaffold (a composed condition needs the SAME cross-tick
    /// WorkingState memory as an action; edge-detection/hysteresis require it, so this is never a
    /// transient/zeroed state) — but scans <see cref="BTreeConditionNodeDto"/> nodes, registers via
    /// <c>actionRegistry.RegisterCondition</c>, and the dispatched call compares the blueprint's
    /// <c>TickCore</c> result against <see cref="Fbt.NodeStatus.Success"/> to produce the bool the
    /// condition delegate shape requires. Only emitted for AiPrimitiveTickCore condition nodes, so
    /// assets without them stay byte-identical.
    /// </summary>
    private static void EmitBlueprintConditionThunks(
        StringBuilder sb, BehaviorTreeAssetDto dto,
        string pad2, string bbShort, string ctxShort,
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? packedFields)
    {
        if (packedFields == null) return;

        var offsetMap = new Dictionary<string, BTreeBlackboardPackHelper.PackedField>(StringComparer.Ordinal);
        foreach (var f in packedFields)
            offsetMap[f.Name] = f;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<(string Key, string MethodFqn, string DtoTypeId, int Offset, int SlotKey, string WsTypeId, int PredictedSize)>();

        foreach (var node in dto.Nodes)
        {
            if (node is not BTreeConditionNodeDto condNode) continue;
            var p = condNode.Condition;
            if (p == null || string.IsNullOrEmpty(p.MethodFqn)) continue;
            if (p.DelegateShape != BTreeDelegateShapeDto.AiPrimitiveTickCore) continue;
            string? targetField = p.ExpressionTargetField;
            if (string.IsNullOrEmpty(targetField)) continue;
            if (!offsetMap.TryGetValue(targetField!, out var field)) continue;

            // Slice 1: scope is governed by the working-state variable when distinct from params
            // (mirrors the action path via the BTreeConditionPayloadDto StatefulScopeVariable
            // overload). Falls back to ExpressionTargetField when WorkingStateTargetField is
            // unauthored, so pre-Slice-1 condition assets stay byte-identical.
            int slotKey = ResolveStatefulSlotKey(dto, StatefulScopeVariable(p), condNode.VisualId);
            // WorkingState type is the blueprint's generated WorkingState struct FQN (authored on the node).
            string wsTypeId = string.IsNullOrEmpty(p.WorkingStateTypeId)
                ? DeriveWorkingStateTypeFromMethod(p.MethodFqn)
                : p.WorkingStateTypeId!;

            string key = $"{p.MethodFqn}@{field.ByteOffset}@{slotKey}";
            if (!seen.Add(key)) continue;

            entries.Add((key, p.MethodFqn, field.TypeId, field.ByteOffset, slotKey, wsTypeId, field.ByteSize));
        }

        if (entries.Count == 0) return;

        sb.AppendLine();
        sb.AppendLine($"{pad2}// E2: blueprint AiPrimitive condition thunks — Params at baked offset + WorkingState from");
        sb.AppendLine($"{pad2}// partition slot, dispatched to the blueprint's generated TickCore. Key = {{MethodFqn}}@{{offset}}@{{slotKey}}.");
        foreach (var (key, methodFqn, dtoTypeId, offset, slotKey, wsTypeId, predictedSize) in entries)
        {
            string dtoTypeFqn = DtoTypeToGlobal(dtoTypeId);
            string wsTypeFqn  = DtoTypeToGlobal(wsTypeId);
            string methodRef  = GlobalMethodRef(methodFqn);

            // Same AAR integrity guard as the action path (see EmitBlueprintActionThunks) — the
            // composed blueprint Params struct's compiled layout must match the .bp.json-predicted
            // layout baked into the offset, or every projection at this offset corrupts the AAR schema.
            if (predictedSize > 0)
            {
                sb.AppendLine($"{pad2}if (global::System.Runtime.InteropServices.Marshal.SizeOf<{dtoTypeFqn}>() != {predictedSize})");
                sb.AppendLine($"{pad2}{Indent}throw new global::System.InvalidOperationException(");
                sb.AppendLine($"{pad2}{Indent}{Indent}\"Composed blueprint Params layout drift: {dtoTypeFqn} compiled size (\" +");
                sb.AppendLine($"{pad2}{Indent}{Indent}global::System.Runtime.InteropServices.Marshal.SizeOf<{dtoTypeFqn}>() +");
                sb.AppendLine($"{pad2}{Indent}{Indent}\") != predicted {predictedSize} bytes baked at offset {offset}. \" +");
                sb.AppendLine($"{pad2}{Indent}{Indent}\"Rebuild the behavior blackboard layout so predicted and reflected sizes agree.\");");
            }
            AppendReusableStatefulConditionThunk(sb, pad2, bbShort, ctxShort, key, dtoTypeFqn, offset, slotKey, wsTypeFqn,
                $"{methodRef}(ref dto, ref ws, ctx.Self, ctx.World, ctx.World.SimulationTime) == Fbt.NodeStatus.Success");
        }
    }

    /// <summary>
    /// S2-1: emits the <c>StatefulWorkingSlots</c> array initializer inside the
    /// <c>BehaviorDefinition</c> object initializer.
    /// Only emitted when the asset has at least one stateful node; otherwise emits nothing
    /// (non-managed or no-stateful assets stay byte-identical).
    /// E2: also scans <see cref="BTreeConditionNodeDto"/> nodes whose DelegateShape is
    /// AiPrimitiveTickCore — a composed blueprint condition rides the same partition-slot rail as
    /// a composed action and must get its WorkingState slot provisioned identically (mirrors the
    /// action loop below; action behavior/output is unchanged).
    /// </summary>
    private static void EmitStatefulWorkingSlotsArray(
        StringBuilder sb, BehaviorTreeAssetDto dto, string pad)
    {
        // Collect unique stateful entries (deduped by SlotKey).
        var slotsBySeen = new Dictionary<int, (int SlotKey, string WsTypeId, string NodeLabel, int Role, int Scope)>();

        // Need packed fields for size lookup — we rebuild the offset map using variable names.
        foreach (var node in dto.Nodes)
        {
            string methodFqn;
            BTreeDelegateShapeDto delegateShape;
            string? targetField;
            string? wsTypeIdRaw;
            string? workingStateTargetField = null;
            Guid visualId;
            string displayLabel;

            if (node is BTreeActionNodeDto actNode && actNode.Action != null)
            {
                var p = actNode.Action;
                methodFqn               = p.MethodFqn;
                delegateShape           = p.DelegateShape;
                targetField             = p.ExpressionTargetField;
                wsTypeIdRaw             = p.WorkingStateTypeId;
                workingStateTargetField = p.WorkingStateTargetField;
                visualId                = actNode.VisualId;
                displayLabel            = actNode.DisplayLabel;
            }
            else if (node is BTreeConditionNodeDto condNode && condNode.Condition != null)
            {
                // Slice 1: mirrors the action branch above — workingStateTargetField carries the
                // condition's authored working-state variable (when distinct from params) so the
                // scope-governing variable below matches the blob/thunk slot key.
                var p = condNode.Condition;
                methodFqn               = p.MethodFqn;
                delegateShape           = p.DelegateShape;
                targetField             = p.ExpressionTargetField;
                wsTypeIdRaw             = p.WorkingStateTypeId;
                workingStateTargetField = p.WorkingStateTargetField;
                visualId                = condNode.VisualId;
                displayLabel            = condNode.DisplayLabel;
            }
            else
            {
                continue;
            }

            if (string.IsNullOrEmpty(methodFqn)) continue;
            // Both reusable-stateful shapes and composed blueprint AiPrimitive actions/conditions
            // ride the partition-slot rail, so both contribute a StatefulWorkingSlots manifest
            // entry (I2/I3/E2).
            if (delegateShape != BTreeDelegateShapeDto.ThreeParamReusableStateful &&
                delegateShape != BTreeDelegateShapeDto.AiPrimitiveTickCore) continue;

            // S3-4: scope-aware key so co-bound Behavior-scoped nodes dedup onto one shared slot.
            // S3-G: scope governed by the working-state variable when distinct from params.
            string? scopeVar = string.IsNullOrEmpty(workingStateTargetField) ? targetField : workingStateTargetField;
            int slotKey = ResolveStatefulSlotKey(dto, scopeVar, visualId);
            if (slotsBySeen.ContainsKey(slotKey)) continue;

            string wsTypeId = string.IsNullOrEmpty(wsTypeIdRaw)
                ? DeriveWorkingStateTypeFromMethod(methodFqn)
                : wsTypeIdRaw!;

            // NodeLabel: prefer DisplayLabel, fall back to VisualId string.
            string nodeLabel = !string.IsNullOrEmpty(displayLabel)
                ? displayLabel
                : visualId.ToString();

            // S3-7: carry the authored role/scope so the live inspector can group/label by scope.
            var (role, scope) = ResolveVariableRoleScope(dto, scopeVar);

            slotsBySeen[slotKey] = (slotKey, wsTypeId, nodeLabel, role, scope);
        }

        // Slice 2a-3 (ADDITIVE): a standalone Role=State blackboard variable that is NOT bound to
        // any node's WorkingState (no composed node's WorkingStateTargetField/ExpressionTargetField
        // names it) still needs its partition slot provisioned so the Slice-2 GetShared/SetShared
        // blueprint accessor (BlueprintSharedState) can find it — the node-driven loop above only
        // sees variables reachable through a node binding. This pass mirrors ResolveStatefulSlotKey's
        // scope derivation, but reads the variable directly since there is no node to inspect.
        //
        // Dedup via the SAME slotsBySeen dictionary: a variable that IS node-bound (e.g. T35's
        // "bpSharedWorkingState") was already inserted above under the identical scope-derived key,
        // so it is skipped here — no duplicate entry, existing composed-node assets stay byte-identical.
        //
        // Node scope is intentionally NOT handled here: WorkingStateScope.Node's key formula collapses
        // to FNV(assetId ++ nodeVisualId) and ignores the variable name entirely (see the 4-arg
        // ComputeStatefulSlotKey's Node case), so a "standalone" Node-scoped variable has no node
        // identity to key off — that configuration is not a meaningful standalone slot and is skipped.
        if (dto.Blackboard?.Variables != null)
        {
            foreach (var v in dto.Blackboard.Variables)
            {
                if (v.Role != BlackboardVariableRole.State) continue;
                if (v.Scope != WorkingStateScope.Behavior && v.Scope != WorkingStateScope.Entity) continue;

                int standaloneSlotKey = ComputeStatefulSlotKey(dto.AssetId, v.Scope, Guid.Empty, v.Name);
                if (slotsBySeen.ContainsKey(standaloneSlotKey)) continue; // node-bound (e.g. T35) — already seen above.

                string standaloneWsTypeId = v.Type?.TypeId ?? string.Empty;
                if (string.IsNullOrEmpty(standaloneWsTypeId)) continue;

                slotsBySeen[standaloneSlotKey] = (standaloneSlotKey, standaloneWsTypeId, v.Name, (int)v.Role, (int)v.Scope);
            }
        }

        if (slotsBySeen.Count == 0) return;

        sb.AppendLine($"{pad}StatefulWorkingSlots = new global::Fdp.Toolkit.Behavior.StatefulSlotInfo[]");
        sb.AppendLine($"{pad}{{");
        foreach (var (slotKey, wsTypeId, nodeLabel, role, scope) in slotsBySeen.Values)
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
            // WorkingStateType: typeof(global::...) so the inspector can project typed values.
            // NodeLabel: the node's DisplayLabel for a friendly row label in the inspector.
            string escapedLabel = nodeLabel.Replace("\\", "\\\\").Replace("\"", "\\\"");
            // S3-7: Role/Scope (inspector metadata). Only appended when non-default (State/Behavior/
            // Entity) so the existing Node-scoped corpus (e.g. T20) emits the byte-identical 5-arg form.
            // Clarity-only: emit named enum casts instead of raw ints. The (int)role/(int)scope values
            // come from the editor's BlackboardVariableRole/WorkingStateScope enums, but their member
            // names (Input/State, Node/Behavior/Entity) line up 1:1 with the runtime-side twins
            // StatefulSlotRole/StatefulSlotScope (Fdp.Toolkit.Blueprints.Partitioning), so mapping the
            // int to a member NAME via the editor enum and re-casting through the runtime enum produces
            // the exact same byte StatefulSlotInfo.Role/.Scope would have held as a raw literal — this
            // changes only the emitted source text, not the compiled/runtime bytes.
            string roleScopeArgs = (role != 0 || scope != 0)
                ? $", (byte)global::Fdp.Toolkit.Blueprints.Partitioning.StatefulSlotRole.{(BlackboardVariableRole)role}, (byte)global::Fdp.Toolkit.Blueprints.Partitioning.StatefulSlotScope.{(WorkingStateScope)scope}"
                : string.Empty;
            sb.AppendLine($"{pad}{Indent}new global::Fdp.Toolkit.Behavior.StatefulSlotInfo({slotKey}, global::System.Runtime.InteropServices.Marshal.SizeOf<{wsTypeFqn}>(), unchecked({typeNameHash}u ^ (uint)global::System.Runtime.InteropServices.Marshal.SizeOf<{wsTypeFqn}>()), typeof({wsTypeFqn}), \"{escapedLabel}\"{roleScopeArgs}),");
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
        sb.AppendLine($"{pad3}__parseParams = static (string json, byte* memory, global::Fdp.Core.EntityRepository world, global::Fdp.Core.Entity self, global::Fdp.Toolkit.Behavior.IHostVariableAccess? host) =>");
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

    private static IReadOnlyList<string> CollectBridgeUsings(BehaviorTreeAssetDto dto,
        IReadOnlyList<DeactivatorEntry>? deactivators = null)
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
        AddNamespaceFromTypeName(set, AiEmitCoreBase.EffectiveBlackboardTypeName(dto.BlackboardTypeName));
        AddNamespaceFromTypeName(set, AiEmitCoreBase.EffectiveContextTypeName(dto.ContextTypeName));

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

        // HAJSON-B: 3-param deactivator wrappers use Unsafe.As + Unsafe.AddByteOffset.
        // Add System.Runtime.CompilerServices if any 3-param deactivator wrappers are present.
        if (deactivators != null && deactivators.Any(d => d.ParamCount == 3))
        {
            set.Add("System.Runtime.CompilerServices");
        }

        return AiEmitCoreBase.SortUsings(set);
    }

    // ── HAJSON-B: Deactivator scanning and emission ────────────────────────────

    /// <summary>
    /// Collects all action/condition keys that will be registered into <c>actionRegistry</c>
    /// for this asset. These are the keys that deactivators can pair with.
    ///
    /// For managed ThreeParamReusable/ThreeParamReusableStateful nodes: key = <c>{MethodFqn}@{offset}</c>.
    /// For non-managed FourParamFull nodes: key = <c>{MethodFqn}</c> (no offset suffix).
    /// Called by the generator before deactivator scanning to build the match set.
    /// </summary>
    public static HashSet<string> CollectRegisteredActionKeys(
        BehaviorTreeAssetDto dto,
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? packedFields)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        bool isManaged = dto.Blackboard.Managed && dto.Blackboard.Variables.Count > 0 && packedFields != null;

        if (isManaged)
        {
            var offsetMap = new Dictionary<string, BTreeBlackboardPackHelper.PackedField>(StringComparer.Ordinal);
            foreach (var f in packedFields!)
                offsetMap[f.Name] = f;

            foreach (var node in dto.Nodes)
            {
                if (node is BTreeActionNodeDto actNode)
                {
                    var p = actNode.Action;
                    if (p == null || string.IsNullOrEmpty(p.MethodFqn)) continue;
                    if (p.DelegateShape == BTreeDelegateShapeDto.ThreeParamReusable ||
                        p.DelegateShape == BTreeDelegateShapeDto.ThreeParamReusableStateful)
                    {
                        string? targetField = p.ExpressionTargetField;
                        if (!string.IsNullOrEmpty(targetField) && offsetMap.TryGetValue(targetField!, out var field))
                        {
                            // For stateful, key is {fqn}@{offset}@{slotKey} — but deactivators pair with
                            // the base key {fqn}@{offset}, so we add both forms.
                            keys.Add($"{p.MethodFqn}@{field.ByteOffset}");
                        }
                    }
                }
                else if (node is BTreeConditionNodeDto condNode)
                {
                    var p = condNode.Condition;
                    if (p == null || string.IsNullOrEmpty(p.MethodFqn)) continue;
                    if (p.DelegateShape == BTreeDelegateShapeDto.ThreeParamReusable)
                    {
                        string? targetField = p.ExpressionTargetField;
                        if (!string.IsNullOrEmpty(targetField) && offsetMap.TryGetValue(targetField!, out var field))
                        {
                            keys.Add($"{p.MethodFqn}@{field.ByteOffset}");
                        }
                    }
                }
            }
        }

        // Non-managed (FourParamFull) actions: key is bare MethodFqn (no @offset suffix).
        // These are the stub-thunk keys registered under beh.RegisterAction (legacy path).
        // Deactivators for these use the FourParamFull shape (4-param) with key = {methodFqn}.
        // However, per the problem statement the FourParamFull deactivators are already
        // handled by FbtActionRegistrar (source-gen). We include them here so the bridge
        // does not double-register — the scanner filters by signature (4-param vs 3-param).
        // For non-managed assets we DO include the bare key so 4-param deactivators register
        // correctly in the bridge's own actionRegistry (even though FbtActionRegistrar also
        // registers them into the shared registry, the bridge builds a SEPARATE ActionRegistry
        // for its Interpreter, so it must register them itself).
        if (!isManaged)
        {
            foreach (var node in dto.Nodes)
            {
                if (node is BTreeActionNodeDto actNode)
                {
                    var p = actNode.Action;
                    if (p != null && !string.IsNullOrEmpty(p.MethodFqn))
                        keys.Add(p.MethodFqn);
                }
                else if (node is BTreeConditionNodeDto condNode)
                {
                    var p = condNode.Condition;
                    if (p != null && !string.IsNullOrEmpty(p.MethodFqn))
                        keys.Add(p.MethodFqn);
                }
            }
        }

        return keys;
    }

    /// <summary>
    /// Emits <c>actionRegistry.RegisterDeactivator(…)</c> calls for the given deactivator entries.
    /// Must be called BEFORE Interpreter construction (same ordering rule as thunk registration).
    ///
    /// For 4-param deactivators: registers the method directly.
    /// For 3-param deactivators: emits a wrapper lambda that projects the DTO at the baked offset
    /// and forwards to the 3-param method, mirroring the action-thunk pattern.
    /// For 5-param stateful deactivators (S3-G): emits a wrapper that projects params + the paired
    /// stateful node's partition slot, registered under the node's full {fqn}@{offset}@{slotKey} key.
    /// </summary>
    private static void EmitDeactivatorRegistrations(
        StringBuilder sb,
        BehaviorTreeAssetDto dto,
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? packedFields,
        IReadOnlyList<DeactivatorEntry> deactivators,
        string pad2,
        string bbShort,
        string ctxShort)
    {
        if (deactivators.Count == 0) return;

        sb.AppendLine();
        sb.AppendLine($"{pad2}// HAJSON-B: deactivator hooks — fired by the Interpreter on branch abort/exit.");
        foreach (var d in deactivators)
        {
            string methodRef = $"global::{d.DeactivatorFqn}";

            if (d.ParamCount == 4)
            {
                // 4-param: matches NodeDeactivatorDelegate<TBB,TCtx> directly.
                sb.AppendLine($"{pad2}actionRegistry.RegisterDeactivator(\"{d.ActionKey}\", {methodRef});");
            }
            else if (d.ParamCount == 5)
            {
                // S3-G: stateful deactivator. Resolve the paired stateful node's slot key so the wrapper
                // is registered under the node's full key {fqn}@{offset}@{slotKey} — the interpreter looks
                // up deactivators by the node's blob MethodName, which is the full stateful key.
                int? slotKey = ResolveStatefulDeactivatorSlotKey(dto, packedFields, d.ActionKey);
                if (slotKey == null)
                    continue; // paired stateful node not found — skip (should not happen for a matched key)

                string fullKey = $"{d.ActionKey}@{slotKey.Value}";
                sb.AppendLine($"{pad2}actionRegistry.RegisterDeactivator(\"{fullKey}\",");
                sb.AppendLine($"{pad2}{Indent}static (ref {bbShort} bb, ref Fbt.BehaviorTreeState st, ref {ctxShort} ctx, int pi) =>");
                sb.AppendLine($"{pad2}{Indent}{{");
                sb.AppendLine($"{pad2}{Indent}{Indent}unsafe");
                sb.AppendLine($"{pad2}{Indent}{Indent}{{");
                sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}// Project Params from BrainBlackboard (Slice-1 pattern).");
                sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}ref var dto = ref Unsafe.As<byte, {d.DtoTypeFqn}>(");
                sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint){d.DtoByteOffset}));");
                sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}// Project WorkingState from the entity's active partition tier (16384 → 4096 → 1024).");
                sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}const int __slotKey = {slotKey.Value};");
                EmitStatefulDeactivatorTierBlock(sb, pad2, "16384", methodRef, d.WorkingStateTypeFqn!, slotKey.Value);
                EmitStatefulDeactivatorTierBlock(sb, pad2, "4096", methodRef, d.WorkingStateTypeFqn!, slotKey.Value);
                EmitStatefulDeactivatorTierBlock(sb, pad2, "1024", methodRef, d.WorkingStateTypeFqn!, slotKey.Value);
                sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}// No tier component found — nothing to clean up.");
                sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}global::System.Diagnostics.Debug.Assert(false, \"S3-G: entity has no BlueprintBlackboard* tier component for stateful deactivator slot {slotKey.Value}\");");
                sb.AppendLine($"{pad2}{Indent}{Indent}}}");
                sb.AppendLine($"{pad2}{Indent}}});");
            }
            else
            {
                // 3-param: emit a wrapper lambda that projects TDto at the baked byte offset,
                // mirroring the managed action thunk pattern (EmitManagedActionThunks).
                sb.AppendLine($"{pad2}actionRegistry.RegisterDeactivator(\"{d.ActionKey}\",");
                sb.AppendLine($"{pad2}{Indent}static (ref {bbShort} bb, ref Fbt.BehaviorTreeState st, ref {ctxShort} ctx, int pi) =>");
                sb.AppendLine($"{pad2}{Indent}{{");
                sb.AppendLine($"{pad2}{Indent}{Indent}unsafe");
                sb.AppendLine($"{pad2}{Indent}{Indent}{{");
                sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}ref var dto = ref Unsafe.As<byte, {d.DtoTypeFqn}>(");
                sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}ref Unsafe.AddByteOffset(ref bb.BehaviorParameters[0], (nint){d.DtoByteOffset}));");
                sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{methodRef}(ref dto, ref st, ref ctx);");
                sb.AppendLine($"{pad2}{Indent}{Indent}}}");
                sb.AppendLine($"{pad2}{Indent}}});");
            }
        }
    }

    /// <summary>
    /// S3-G: emits one tier-dispatch block for a stateful deactivator wrapper — mirrors the stateful
    /// action thunk's tier block but returns void (deactivators have no return value).
    /// </summary>
    private static void EmitStatefulDeactivatorTierBlock(
        StringBuilder sb, string pad2, string tierSize, string methodRef, string wsTypeFqn, int slotKey)
    {
        string tierType = $"global::Fdp.Toolkit.Blueprints.Components.BlueprintBlackboard{tierSize}";
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}if (ctx.World.HasComponent<{tierType}>(ctx.Self))");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{{");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}ref var tier = ref ctx.World.GetComponentRW<{tierType}>(ctx.Self);");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}fixed (byte* mem = tier.Memory)");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{{");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}if (global::Fdp.Toolkit.Blueprints.Partitioning.BlueprintBlackboardPartitions.TryGetSlotOffset(mem, __slotKey, out int wsOff))");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}{{");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}{Indent}ref var ws = ref Unsafe.AsRef<{wsTypeFqn}>(mem + wsOff);");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}{Indent}{methodRef}(ref dto, ref ws, ref st, ref ctx, pi);");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}{Indent}return;");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}}}");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}global::System.Diagnostics.Debug.Assert(false, \"S3-G: stateful deactivator slot {slotKey} missing from BlueprintBlackboard{tierSize}\");");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{Indent}return;");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}}}");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}}}");
    }

    /// <summary>
    /// S3-G: resolves the FNV-1a slot key of the stateful node whose base action key
    /// (<c>{MethodFqn}@{offset}</c>) equals <paramref name="actionKey"/>, so a 5-param deactivator can be
    /// registered under the node's full <c>{MethodFqn}@{offset}@{slotKey}</c> key. Returns null if no
    /// matching stateful node is present.
    /// </summary>
    private static int? ResolveStatefulDeactivatorSlotKey(
        BehaviorTreeAssetDto dto,
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? packedFields,
        string actionKey)
    {
        if (packedFields == null) return null;
        var offsetMap = new Dictionary<string, BTreeBlackboardPackHelper.PackedField>(StringComparer.Ordinal);
        foreach (var f in packedFields)
            offsetMap[f.Name] = f;

        foreach (var node in dto.Nodes)
        {
            if (node is not BTreeActionNodeDto actNode) continue;
            var p = actNode.Action;
            if (p == null || string.IsNullOrEmpty(p.MethodFqn)) continue;
            if (p.DelegateShape != BTreeDelegateShapeDto.ThreeParamReusableStateful) continue;
            string? targetField = p.ExpressionTargetField;
            if (string.IsNullOrEmpty(targetField)) continue;
            if (!offsetMap.TryGetValue(targetField!, out var field)) continue;

            if (string.Equals($"{p.MethodFqn}@{field.ByteOffset}", actionKey, StringComparison.Ordinal))
                return ResolveStatefulSlotKey(dto, StatefulScopeVariable(p), actNode.VisualId);
        }
        return null;
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
