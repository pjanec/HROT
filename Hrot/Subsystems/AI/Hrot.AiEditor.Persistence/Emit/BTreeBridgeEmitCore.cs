using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Fdp.Toolkit.Behavior.Shared;
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
        // ⭐ DEBT-AIB-021: the condition is "does this asset emit a ParseParams", and since Batch 70
        //   that is "managed with ≥1 packed variable" -- NOT "≥1 default". ⛔ Keying it on defaults was
        //   the same mistake as defect (b) one level up: an asset with variables but no defaults now
        //   emits an overlay that needs these options.
        bool needsJsonOpts = dto.Blackboard.Managed
                             && dto.Blackboard.Variables.Count > 0;
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
    /// <remarks>
    /// ⭐⭐ <b>A1: a THIN WRAPPER over the one shared spelling.</b> The algorithm lives in
    /// <c>Fdp.Toolkit.Behavior.Shared.OccurrenceSlotKey</c>, LINKED into this assembly (see the
    /// csproj) and compiled into the runtime as well — so a compile-time key and a runtime key
    /// cannot drift. ⛔ Do not re-inline the FNV: the divergence fails SILENTLY (the slot is never
    /// found, nothing throws).
    /// </remarks>
    public static int ComputeStatefulSlotKey(Guid assetId, Guid nodeVisualId)
        => Fdp.Toolkit.Behavior.Shared.OccurrenceSlotKey.Compute(
               assetId, Fdp.Toolkit.Behavior.Shared.OccurrenceSlotScope.Node, nodeVisualId, string.Empty);

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
    ///   <item><term><c>Entity</c></term>
    ///     <description>⛔ REMOVED by <c>CE-441</c> slice 1 (Q76 §12.25).</description></item>
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
        => Fdp.Toolkit.Behavior.Shared.OccurrenceSlotKey.Compute(
               assetId,
               (Fdp.Toolkit.Behavior.Shared.OccurrenceSlotScope)(int)scope,
               nodeVisualId,
               variableId);
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
        // ⭐⭐⭐ P4-② (2026-09-22) — THE DISPATCH TYPE IS `byte`, FOR EVERY TREE.
        //
        // 📐 `bbShort` is threaded into dispatch-typed positions ONLY — the `Register` signature's
        //    `ActionRegistry<…>`, every thunk lambda's `ref … bb`, the deactivator registrations —
        //    and never as a LAYOUT type. ⇒ one assignment retargets all ten emission sites.
        //
        // ⛔⛔ IT IS NO LONGER DERIVED FROM THE ASSET, AND THAT IS DELIBERATE. It used to read
        //    `dto.BlackboardTypeName`, which is PERSISTED and also mangles into the generated
        //    params-layout struct name ({Asset}_{SanitizedType}) and into SubtreeSyncIdentity.Derive,
        //    whose inputs match subtrees by (name, dto type, dto ns). ⇒ retargeting the ASSET field
        //    would rename 11 generated structs across 44 files AND silently break subtree matching.
        //    📄 §30.19. ⭐ Retargeting the type ARGUMENT at its emission site does neither, and the
        //    asset keeps naming its own layout — which is the only thing that name was ever about.
        //
        // ⚠ ⛔ The generated BUILDER (BTreeEmitCore:408) keeps the asset's type and MUST: its
        //    selector-form bindings need a struct with fields, and `byte` has none. That is the same
        //    reason P4-②b was withdrawn — a builder's generic is build-time only and never reaches
        //    the interpreter, because Compile() returns an untyped BehaviorTreeBlob.
        var bbShort    = "byte";
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
        // ⭐⭐⭐ P4-② (2026-09-22): the DISPATCH type is `byte` for every tree, not the asset's
        //   blackboard type. The interpreter is handed the entity's ROOT PARAMS SLOT BASE, and a slot
        //   base is bytes — what the asset calls its layout struct is irrelevant to dispatch.
        //
        // ⛔⛔ AND THIS IS WHY IT IS FIXED HERE RATHER THAN IN THE ASSET. `bbShort` comes from the
        //   asset's persisted `BlackboardTypeName`, which ALSO mangles into the generated params-layout
        //   struct name ({Asset}_{SanitizedType}) and into SubtreeSyncIdentity.Derive — whose inputs are
        //   persisted and which MATCHES SUBTREES by (name, dto type, dto ns). ⇒ retargeting the asset
        //   field would rename 11 generated structs across 44 files AND silently break subtree
        //   matching. 📄 §30.19. ⭐ Changing the type argument at its emission site does neither.
        sb.AppendLine($"{pad2}var interpreter = new Interpreter<byte, {ctxShort}>(blob, actionRegistry);");
        sb.AppendLine();

        // ⭐⭐⭐ E6 / CE-364 — BTREE-HOSTS-BTREE, the EDITOR-AUTHORED route.
        //
        // ⭐ GATED on the asset actually hosting something. 📐 0 of 26 shipped *.btree.json carries a
        //   Subtree node, so this emits NOTHING for every one of them and no golden moves. The same
        //   gating rule the AssetId constant and E5's hosting table learned.
        // ⛔⛔ THE PLAN AND THE BIND SHIP TOGETHER: HostedSubtree.Tick THROWS on a slot the manifest
        //   never declared (§19.6 ⑤), so emitting one without the other turns every hosting node into
        //   a hard failure. §32.2.2 records E5's first draft making exactly that mistake.
        bool hostsSubtrees = CountSubtreeNodes(dto) > 0;
        if (hostsSubtrees)
        {
            sb.AppendLine($"{pad2}// E6 — this asset hosts a sub-tree: plan its sites, then bind after Register.");
            sb.AppendLine($"{pad2}var __hosted = global::Fdp.Toolkit.Behavior.BTreeHostedSites.PlanFor(");
            string? bindings = EmitSiteBindings(dto, packedFields);
            sb.AppendLine(bindings is null
                ? $"{pad2}{Indent}blob, \"{name}\", new global::System.Guid(\"{dto.AssetId:D}\"));"
                : $"{pad2}{Indent}blob, \"{name}\", new global::System.Guid(\"{dto.AssetId:D}\"), {bindings});");
            sb.AppendLine($"{pad2}interpreter.SubtreeHost = global::Fdp.Toolkit.Behavior.OccurrenceSubtreeHost.Instance;");
            sb.AppendLine();
        }

        bool hasParseParams = EmitRootParamsLocals(sb, dto, packedFields, isManaged, pad2);

        // 4b. Register definition
        sb.AppendLine($"{pad2}// {(hasParseParams ? "4b" : "4")}. Register the JSON-owned definition (FbtTreeCatalog cannot see in-memory defs).");
        sb.AppendLine($"{pad2}beh.Register(global::Fdp.Toolkit.Behavior.BehaviorHash.FromName(\"{name}\"), \"{name}\", new BehaviorDefinition");
        sb.AppendLine($"{pad2}{{");
        sb.AppendLine($"{pad2}{Indent}Name         = \"{name}\",");
        sb.AppendLine($"{pad2}{Indent}BrainTier    = BehaviorConstants.BrainTierBTree,");
        sb.AppendLine($"{pad2}{Indent}BTreeInterpreter = interpreter,");
        EmitRootParamsMembers(sb, dto, packedFields, isManaged, hasParseParams, pad2);
        if (isManaged)
            EmitStatefulWorkingSlotsArray(sb, dto, pad2 + Indent, hostsSubtrees, packedFields);
        else if (hostsSubtrees)
            // ⚠ A NON-managed asset emits no authored slot array at all, so a hosting one would get
            //   no manifest and HostedSubtree.Tick would throw. The hosted slots stand alone here.
            sb.AppendLine($"{pad2}{Indent}StatefulWorkingSlots = __hosted.Slots,");
        sb.AppendLine($"{pad2}}});");
        if (hostsSubtrees)
        {
            sb.AppendLine();
            sb.AppendLine($"{pad2}// E6 — bind AFTER Register: HostedChildren resolves the CHILD through the");
            sb.AppendLine($"{pad2}//      registry, and registrars run in an arbitrary order.");
            sb.AppendLine($"{pad2}global::Fdp.Toolkit.Behavior.BTreeHostedSites.Bind(beh, blob, __hosted);");
        }

        sb.AppendLine($"{pad}}}");
    }

    /// <summary>
    /// S1-3: emits baked-offset Action registry entries for a managed blackboard asset.
    /// Key format: <c>{MethodFqn}@{offset}</c> — matches the blob key produced by BTreeEmitCore.
    /// Thunk: <c>Unsafe.As&lt;byte, TDto&gt;(ref Unsafe.AddByteOffset(ref bb, (nint){offset}))</c>
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
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{BlackboardParamsExpression.AtBlock("bb", bbShort, "ctx.World", "ctx.Self", offset)});");
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
            sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{BlackboardParamsExpression.AtBlock("bb", bbShort, "ctx.World", "ctx.Self", offset)});");
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
    ///   3. Resolves the slot via <c>OccurrenceStoreAccess.TryResolveOccurrence</c> (A2b; it calls <c>TryGetSlotOffset</c> internally).
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
            AppendReusableStatefulThunk(sb, dto, packedFields, pad2, bbShort, ctxShort, key, dtoTypeFqn, offset, slotKey, wsTypeFqn,
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
        StringBuilder sb, BehaviorTreeAssetDto dto, IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? packedFields,
        string pad2, string bbShort, string ctxShort,
        string key, string dtoTypeFqn, int offset, int slotKey, string wsTypeFqn, string callExpr)
    {
        sb.AppendLine($"{pad2}actionRegistry.Register(\"{key}\",");
        sb.AppendLine($"{pad2}{Indent}static (ref {bbShort} bb, ref Fbt.BehaviorTreeState st, ref {ctxShort} ctx, int pi) =>");
        sb.AppendLine($"{pad2}{Indent}{{");
        sb.AppendLine($"{pad2}{Indent}{Indent}unsafe");
        sb.AppendLine($"{pad2}{Indent}{Indent}{{");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}// Project Params from BrainBlackboard (Slice-1 pattern).");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}ref var dto = ref Unsafe.As<byte, {dtoTypeFqn}>(");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{BlackboardParamsExpression.AtBlock("bb", bbShort, "ctx.World", "ctx.Self", offset)});");
        // ⭐⭐⭐ A2b (PLAN_Occurrence_Storage_Build) — ONE seam call, not a hand-emitted tier ladder.
        //   📐 This used to emit the 16384 → 4096 → 1024 chain inline: three HasComponent/GetComponentRW/
        //      fixed arms plus a no-tier fallthrough, MULTIPLIED INTO EVERY GENERATED ASSEMBLY. A fourth
        //      tier (O3b's 256) would have meant a fourth arm here and in the condition sibling.
        //   ⭐ OccurrenceStoreAccess.TryResolveOccurrence preserves the probe order and the
        //      "at most one tier, first match is authoritative" rule, and resolves through
        //      GetComponentRW exactly as the emitted arms did — so the chunk-version behaviour is
        //      unchanged (RW bumps it, RO does not; see the seam's own note).
        //   ⚠ THE TWO DIAGNOSTICS ARE KEPT. TryResolveOccurrence deliberately does not distinguish
        //      "no store" from "no such slot", but the emitted code DID, with different messages.
        //      The HasStore probe that tells them apart sits INSIDE Debug.Assert's argument, and
        //      Debug.Assert is [Conditional("DEBUG")] — so a Release build evaluates neither the
        //      probe nor the strings, and the hot path is strictly cheaper than the old ladder.
        AppendWorkingStateResolve(sb, pad2 + Indent + Indent + Indent, dto, packedFields, slotKey, wsTypeFqn,
            "S2-1", "", "return Fbt.NodeStatus.Failure;");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}return {callExpr};");
        sb.AppendLine($"{pad2}{Indent}{Indent}}}");
        sb.AppendLine($"{pad2}{Indent}}});");
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-426</c> — <b>STAGE 1, the State half: bake every <c>Role=State, Scope=Behavior</c>
    /// variable's authored default into the block.</b> ⭐ This CLOSES <c>CE-420</c>: before it the bake
    /// list was built from the packed (Input) fields only, so a State default the editor offered and
    /// saved was dropped with no diagnostic and the runtime read zero.
    ///
    /// <para>⭐ Written through the TYPED block — <c>((Block*)memory)->St.name</c> — because the State
    /// half is laid out by the CLR (<c>CE-437</c>) and no manifest states its offsets. ⚠ The width guard
    /// is the parse's own <c>capacity</c>: the ingress shadow is sized from the block
    /// (<c>RootParamsBytes</c>), so a narrower buffer means a stale layout and is refused, never
    /// overrun.</para>
    ///
    /// <para>⚠ Runs on EVERY assign, a re-assign included — <c>CE-421</c>'s ruling: the shadow starts
    /// empty and an unmentioned variable lands on its authored default.</para>
    /// </summary>
    private static void EmitStateDefaultBake(
        StringBuilder sb, BehaviorTreeAssetDto dto,
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? packedFields, string pad)
    {
        if (!EmitsBlock(dto, packedFields)) return;

        var withDefaults = new List<BlackboardVariableDto>();
        foreach (var v in BTreeEmitCore.BlockStateVariables(dto))
            if (!string.IsNullOrWhiteSpace(v.DefaultValueJson)) withDefaults.Add(v);
        if (withDefaults.Count == 0) return;

        string blockFqn = BTreeEmitCore.BlockStructFqn(dto);
        sb.AppendLine($"{pad}// Step 1b — CE-426/CE-420: the State half's authored defaults, into the block.");
        sb.AppendLine($"{pad}if (capacity < global::System.Runtime.CompilerServices.Unsafe.SizeOf<{blockFqn}>())");
        sb.AppendLine($"{pad}{Indent}throw new global::System.InvalidOperationException(\"CE-426: the parse buffer is narrower than {BTreeEmitCore.BlockStructName(dto)} — a stale layout; refusing to bake the State half past its end.\");");
        foreach (var v in withDefaults)
        {
            string typeFqn = DtoTypeToGlobal(v.Type!.TypeId);
            string escaped = EscapeCSharpStringLiteral(v.DefaultValueJson!);
            sb.AppendLine($"{pad}(({blockFqn}*)memory)->St.{v.Name} = global::System.Text.Json.JsonSerializer.Deserialize<{typeFqn}>(\"{escaped}\", __paramJsonOpts);");
        }
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-437</c> — the ONE emission of "find this node's WorkingState", shared by the action,
    /// condition and deactivator thunks (it was three hand-copied blocks).
    ///
    /// <para>⭐ A <c>Role=State, Scope=Behavior</c> variable lives in the behaviour's BLOCK now
    /// (<c>R-151</c>): the thunk reaches it as <c>block.St.name</c> projected from its own <c>bb</c>
    /// (<c>CE-431</c> — the root block for a root, the child's OWN block for a hosted subtree; it was
    /// <c>TryGetBlockFor</c> keyed by the behaviour's name, which cannot tell two sites of one child apart). Every other slot — a node-bound
    /// working state, an <c>Entity</c>-scoped variable — keeps its keyed side slot, unchanged.</para>
    /// </summary>
    private static void AppendWorkingStateResolve(
        StringBuilder sb, string pad, BehaviorTreeAssetDto dto,
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? packedFields,
        int slotKey, string wsTypeFqn, string diagPrefix, string what, string failStatement)
    {
        if (TryGetBlockStateVariable(dto, packedFields, slotKey, out string? varName))
        {
            string blockFqn = BTreeEmitCore.BlockStructFqn(dto);
            sb.AppendLine($"{pad}// CE-437/CE-431: '{varName}' is Role=State, Scope=Behavior ⇒ it lives in this behaviour's OWN block,");
            sb.AppendLine($"{pad}//   which is the bb it was ticked with — the root block, or a hosted child's own block.");
            sb.AppendLine($"{pad}ref var ws = ref Unsafe.As<byte, {blockFqn}>({BlackboardParamsExpression.BlockBase("bb")}).St.{varName};");
            return;
        }

        sb.AppendLine($"{pad}// Resolve this occurrence's WorkingState across every tier, in one call (A2b).");
        sb.AppendLine($"{pad}const int __slotKey = {slotKey};");
        sb.AppendLine($"{pad}if (!global::Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess.TryResolveOccurrence(ctx.World, ctx.Self, __slotKey, out byte* __wsPtr))");
        sb.AppendLine($"{pad}{{");
        sb.AppendLine($"{pad}{Indent}global::System.Diagnostics.Debug.Assert(false,");
        sb.AppendLine($"{pad}{Indent}{Indent}global::Fdp.Toolkit.Blueprints.Partitioning.OccurrenceStoreAccess.HasStore(ctx.World, ctx.Self)");
        sb.AppendLine($"{pad}{Indent}{Indent}{Indent}? \"{diagPrefix}: stateful {what}slot {slotKey} missing from the entity's occurrence store\"");
        sb.AppendLine($"{pad}{Indent}{Indent}{Indent}: \"{diagPrefix}: entity has no BlueprintBlackboard* tier component for stateful {what}slot {slotKey}\");");
        sb.AppendLine($"{pad}{Indent}{failStatement}");
        sb.AppendLine($"{pad}}}");
        sb.AppendLine($"{pad}ref var ws = ref Unsafe.AsRef<{wsTypeFqn}>(__wsPtr);");
    }

    /// <summary>
    /// ⭐ <c>CE-437</c> — does <paramref name="slotKey"/> name a variable that lives in the block's
    /// <c>St</c> half? True only when the block is emitted (a managed blackboard whose Inputs packed —
    /// the same condition <c>BTreeEmitCore.EmitBlackboardStructSource</c> emits under) and the key is
    /// the <c>Scope=Behavior</c> key of one of <c>BTreeEmitCore.BlockStateVariables</c>.
    /// </summary>
    internal static bool TryGetBlockStateVariable(
        BehaviorTreeAssetDto dto, IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? packedFields,
        int slotKey, out string? varName)
    {
        varName = null;
        if (!EmitsBlock(dto, packedFields)) return false;
        foreach (var v in BTreeEmitCore.BlockStateVariables(dto))
        {
            if (ComputeStatefulSlotKey(dto.AssetId, WorkingStateScope.Behavior, Guid.Empty, v.Name) != slotKey) continue;
            varName = v.Name;
            return true;
        }
        return false;
    }

    /// <summary>⭐ <c>CE-437</c> — the block exists exactly when the blackboard struct does.</summary>
    internal static bool EmitsBlock(BehaviorTreeAssetDto dto, IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? packedFields)
        => dto.Blackboard.Managed && dto.Blackboard.Variables.Count > 0 && packedFields != null;

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
        StringBuilder sb, BehaviorTreeAssetDto dto, IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? packedFields,
        string pad2, string bbShort, string ctxShort,
        string key, string dtoTypeFqn, int offset, int slotKey, string wsTypeFqn, string callExpr)
    {
        sb.AppendLine($"{pad2}actionRegistry.RegisterCondition(\"{key}\",");
        sb.AppendLine($"{pad2}{Indent}static (ref {bbShort} bb, ref Fbt.BehaviorTreeState st, ref {ctxShort} ctx, int pi) =>");
        sb.AppendLine($"{pad2}{Indent}{{");
        sb.AppendLine($"{pad2}{Indent}{Indent}unsafe");
        sb.AppendLine($"{pad2}{Indent}{Indent}{{");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}// Project Params from BrainBlackboard (Slice-1 pattern).");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}ref var dto = ref Unsafe.As<byte, {dtoTypeFqn}>(");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{BlackboardParamsExpression.AtBlock("bb", bbShort, "ctx.World", "ctx.Self", offset)});");
        // ⭐⭐⭐ A2b (PLAN_Occurrence_Storage_Build) — ONE seam call, not a hand-emitted tier ladder.
        //   📐 This used to emit the 16384 → 4096 → 1024 chain inline: three HasComponent/GetComponentRW/
        //      fixed arms plus a no-tier fallthrough, MULTIPLIED INTO EVERY GENERATED ASSEMBLY. A fourth
        //      tier (O3b's 256) would have meant a fourth arm here and in the condition sibling.
        //   ⭐ OccurrenceStoreAccess.TryResolveOccurrence preserves the probe order and the
        //      "at most one tier, first match is authoritative" rule, and resolves through
        //      GetComponentRW exactly as the emitted arms did — so the chunk-version behaviour is
        //      unchanged (RW bumps it, RO does not; see the seam's own note).
        //   ⚠ THE TWO DIAGNOSTICS ARE KEPT. TryResolveOccurrence deliberately does not distinguish
        //      "no store" from "no such slot", but the emitted code DID, with different messages.
        //      The HasStore probe that tells them apart sits INSIDE Debug.Assert's argument, and
        //      Debug.Assert is [Conditional("DEBUG")] — so a Release build evaluates neither the
        //      probe nor the strings, and the hot path is strictly cheaper than the old ladder.
        AppendWorkingStateResolve(sb, pad2 + Indent + Indent + Indent, dto, packedFields, slotKey, wsTypeFqn,
            "E2", "condition ", "return Fbt.NodeStatus.Failure;");
        sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}return {callExpr} ? Fbt.NodeStatus.Success : Fbt.NodeStatus.Failure;");
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
            AppendReusableStatefulThunk(sb, dto, packedFields, pad2, bbShort, ctxShort, key, dtoTypeFqn, offset, slotKey, wsTypeFqn,
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
            AppendReusableStatefulConditionThunk(sb, dto, packedFields, pad2, bbShort, ctxShort, key, dtoTypeFqn, offset, slotKey, wsTypeFqn,
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
        StringBuilder sb, BehaviorTreeAssetDto dto, string pad,
        bool appendHostedSlots,
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? blockPackedFields)
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
            // ⭐ CE-437: a Behavior-scoped State variable lives in the block, not in a side slot.
            if (TryGetBlockStateVariable(dto, blockPackedFields, slotKey, out _)) continue;

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
                if (v.Scope != WorkingStateScope.Behavior) continue;

                int standaloneSlotKey = ComputeStatefulSlotKey(dto.AssetId, v.Scope, Guid.Empty, v.Name);
                if (slotsBySeen.ContainsKey(standaloneSlotKey)) continue; // node-bound (e.g. T35) — already seen above.
                if (TryGetBlockStateVariable(dto, blockPackedFields, standaloneSlotKey, out _)) continue; // CE-437: in the block.

                string standaloneWsTypeId = v.Type?.TypeId ?? string.Empty;
                if (string.IsNullOrEmpty(standaloneWsTypeId)) continue;

                slotsBySeen[standaloneSlotKey] = (standaloneSlotKey, standaloneWsTypeId, v.Name, (int)v.Role, (int)v.Scope);
            }
        }

        // ⛔ HISTORY — O4 / C1 emitted one slot per HOSTED SUBTREE here, from dto.Aliases, so the
        //    child got its OWN BehaviorTreeState (§19, §20), under this rule:
        //      "THIS AND THE ORCHESTRATOR'S HOSTING CALL SHIP TOGETHER OR NEITHER. HostedSubtree.Tick
        //       THROWS on a slot the manifest never declared (§19.6 ⑤ — a silent miss is the failure
        //       A1 exists to kill)."
        //    ⭐ The rule is still right; it is what makes the removal below CORRECT rather than merely
        //    tidy. The key came from the LINKED OccurrenceSlotKey — D3's "one function, two callers" —
        //    and that function is unchanged and now serves E5's per-site declaration instead.
        // ⛔⛔ CE-337 (2026-09-23) — THE ALIAS-DRIVEN HOSTED SLOT IS GONE, and this file's own rule
        //    is why: "THIS AND THE ORCHESTRATOR'S HOSTING CALL SHIP TOGETHER OR NEITHER". The call
        //    was retired with both orchestrator arms (BTreeOrchestratorEmitCore.Emit → null), so a
        //    slot emitted here would be provisioned for a tick that never happens — storage nobody
        //    reads, on every entity carrying the behaviour.
        // ⭐ The slot MECHANISM is alive and unchanged; it moved to the per-SITE declaration E5 built
        //    (HsmBridgeEmitCore.CollectHostedSubtrees). 📄 DESIGN §32.12.
        if (slotsBySeen.Count == 0)
        {
            // ⛔⛔ E6 — a MANAGED asset that hosts a sub-tree but declares no AUTHORED slot would
            //    otherwise fall out of here with no manifest at all, and HostedSubtree.Tick THROWS on
            //    an undeclared slot (§19.6 ⑤). ⚠ The Combine wrap has nothing to combine WITH here,
            //    so the hosted slots stand alone — the same array the non-managed arm emits.
            if (appendHostedSlots)
                sb.AppendLine($"{pad}StatefulWorkingSlots = __hosted.Slots,");
            return;
        }

        // ⭐ E6 — hosted tree-state slots ride AFTER the authored ones, so an existing asset's slot
        //   ORDER is byte-identical. ⛔ The wrap is emitted ONLY when this asset hosts something;
        //   0 of 26 shipped assets do, so none of their goldens move.
        if (appendHostedSlots)
        {
            sb.AppendLine($"{pad}StatefulWorkingSlots = global::Fdp.Toolkit.Behavior.BTreeHostedSites.Combine(");
            sb.AppendLine($"{pad}{Indent}new global::Fdp.Toolkit.Behavior.StatefulSlotInfo[]");
        }
        else
        {
            sb.AppendLine($"{pad}StatefulWorkingSlots = new global::Fdp.Toolkit.Behavior.StatefulSlotInfo[]");
        }
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

        sb.AppendLine(appendHostedSlots ? $"{pad}{Indent}}}, __hosted.Slots)," : $"{pad}}},");
    }

    /// <summary>
    /// ⭐ E6 — how many nodes of this asset host a sub-tree. ⚠ A node needs BOTH a name and a
    /// resolved-or-not asset id to be a site; a bare empty payload is an unfinished authoring state,
    /// not a hosting site.
    /// </summary>
    /// <summary>
    /// ⭐⭐ <c>CE-431</c> — the per-SITE seed bindings, as a dictionary expression keyed by each hosting
    /// node's VisualId (the site id <c>BTreeHostedSites.PlanFor</c> computes), or <c>null</c> when no
    /// site binds a variable (⇒ the emitted call and every golden stay unchanged).
    /// ⛔ A <c>ParamsVariable</c> naming no packed variable emits a registration-time THROW — never a
    /// silently unbound site.
    /// </summary>
    private static string? EmitSiteBindings(
        BehaviorTreeAssetDto dto, IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? packedFields)
    {
        var sites = new List<(Guid SiteId, string? ParamsVariable)>();
        foreach (var node in dto.Nodes ?? new List<BTreeNodeDto>())
            if (node is BTreeSubtreeNodeDto st && st.Subtree is not null)
                sites.Add((st.VisualId, st.Subtree.ParamsVariable));
        return EmitSiteBindings(sites, packedFields, dto.Name);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-439</c> — the ONE site-binding builder for BOTH hosts: a BTree's subtree nodes (keyed by VisualId) and an
    /// HSM's hosting states (keyed by StableId). Each bound site becomes <c>[siteId] = new(offset, size)</c> of its host
    /// variable; <c>null</c> when nothing is bound (⇒ the emitted call and every golden stay unchanged). ⛔ A variable naming no
    /// packed variable emits a registration-time THROW — never a silently unbound site.
    /// </summary>
    internal static string? EmitSiteBindings(
        IEnumerable<(Guid SiteId, string? ParamsVariable)> sites,
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? packedFields, string hostName)
    {
        var parts = new List<string>();
        foreach (var (siteId, v) in sites)
        {
            if (string.IsNullOrWhiteSpace(v)) continue;

            BTreeBlackboardPackHelper.PackedField? f = null;
            if (packedFields != null)
                foreach (var pf in packedFields)
                    if (pf.Name == v) { f = pf; break; }

            if (f is null)
                return "((global::System.Collections.Generic.IReadOnlyDictionary<global::System.Guid, global::Fdp.Toolkit.Behavior.HostedSubtree.SiteBinding>?)null ?? throw new global::System.InvalidOperationException(\"CE-431: subtree site " + siteId.ToString("D")
                     + " binds params variable '" + v!.Replace("\"", "") + "', which is not a Role=Input variable of '"
                     + hostName.Replace("\"", "") + "'.\"))";

            parts.Add("[new global::System.Guid(\"" + siteId.ToString("D") + "\")] = new(" + f.ByteOffset + ", " + f.ByteSize + ")");
        }
        if (parts.Count == 0) return null;
        return "new global::System.Collections.Generic.Dictionary<global::System.Guid, global::Fdp.Toolkit.Behavior.HostedSubtree.SiteBinding> { "
             + string.Join(", ", parts) + " }";
    }

    private static int CountSubtreeNodes(BehaviorTreeAssetDto dto)
    {
        if (dto?.Nodes == null) return 0;
        int n = 0;
        foreach (var node in dto.Nodes)
            if (node is BTreeSubtreeNodeDto st &&
                st.Subtree != null &&
                !string.IsNullOrWhiteSpace(st.Subtree.SubtreeName))
                n++;
        return n;
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
    /// <remarks>⭐ <c>E1</c>: <c>internal</c> so <c>HsmBridgeEmitCore</c> emits the SAME structure
    /// hash. ⛔ A second hash would make an HSM slot and a BTree slot of the same type disagree
    /// about whether the struct changed — the guard would fire on one tier and not the other.</remarks>
    internal static uint ComputeTypeNameHash(string typeName)
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
    /// <remarks>
    /// ⭐ <c>CE-226</c> — <b>internal, not private, because the HSM bridge emits the same array from the
    /// same <c>packedFields</c>.</b> HSM already mirrors this file's <c>EmitParseParamsLocal</c>
    /// (<c>BP-281</c>); duplicating the manifest emitter instead of sharing it would be the second
    /// producer for one slot that <c>R-132</c> forbids, and the two would drift.
    /// </remarks>
    internal static void EmitManagedBlackboardVariablesArray(
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
    /// ⭐⭐⭐ <b><c>DEBT-AIB-021</c> — the generated <c>ParseParams</c> honours the incoming JSON.</b>
    ///
    /// <para>
    /// 📄 <c>DESIGN_Parameter_Model.md</c> §3.2: <i>"defaults are baked, scenario JSON overlays them,
    /// runtime wins."</i> ⭐ <b>The SEQUENCE is the ruling</b>, and it is the order emitted below.
    /// ⚠ That sentence was true of the CURATED path and false of this one — which is what the debt row
    /// records.
    /// </para>
    ///
    /// <h3>🔴 The TWO defects this closes</h3>
    /// <list type="number">
    ///   <item><description><b>The lambda ignored <c>json</c> entirely.</b> Its own comment said so.
    ///   ⇒ a per-assignment override was silently discarded.</description></item>
    ///   <item><description>⭐⭐ <b>The EMIT GUARD was <c>defaults.Count == 0</c></b>, and
    ///   <c>defaults</c> counted only variables with a non-null <c>DefaultValueJson</c>. ⇒ ⛔⛔ <b>an
    ///   asset whose variables have NO defaults emitted NO <c>ParseParams</c> at all</b>, so fixing the
    ///   first defect alone would have left those assets exactly as broken. The guard is now
    ///   <b>≥1 packed managed variable</b>.</description></item>
    /// </list>
    ///
    /// <h3>⭐ The decided behaviours</h3>
    /// <list type="table">
    ///   <item><term>absent key</term><description>the baked default stands — that is what "overlay"
    ///   means</description></item>
    ///   <item><term>⭐⭐ unknown key</term><description><b>IGNORED, not an error</b> — ⛔ because the
    ///   CURATED path already ignores them: <c>JsonSerializer.Deserialize&lt;TDto&gt;</c> drops unmapped
    ///   members unless <c>UnmappedMemberHandling</c> says otherwise. <b>Ruling 9: one mechanism, one
    ///   behaviour.</b> ⚠ Pinned by a DECISION test so a later batch does not "fix" it</description></item>
    ///   <item><term>empty / null <c>json</c></term><description>defaults only — the shipped behaviour,
    ///   byte-identical</description></item>
    ///   <item><term>⛔ malformed <c>json</c></term><description><b>throws, deliberately.</b>
    ///   <c>BehaviorIngressSystem</c> parses into a stack shadow and commits only on success, so a
    ///   throw is exactly what leaves the entity on its old behaviour. ⚠ Swallowing would look tidier
    ///   and hand it a successful-looking all-zero params region — the same reasoning as
    ///   <c>BehaviorParams.FromJson</c> (<c>G1</c>)</description></item>
    /// </list>
    ///
    /// <para>
    /// The local is emitted inside an <c>unsafe { … }</c> block so the <c>byte*</c> parameter is legal
    /// even where <c>AllowUnsafeBlocks</c> is not set globally — the action thunks do the same.
    /// </para>
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

        // Baked defaults: variables carrying a non-null DefaultValueJson that are also packed.
        var defaults = new List<(BTreeBlackboardPackHelper.PackedField Field, string DefaultJson)>();
        foreach (var v in dto.Blackboard.Variables)
        {
            if (v.DefaultValueJson == null) continue;
            if (!offsetMap.TryGetValue(v.Name, out var field)) continue;
            defaults.Add((field, v.DefaultValueJson));
        }

        // ⭐⭐ DEFECT (b): the guard used to be `defaults.Count == 0`, so an asset whose variables had
        //    no defaults emitted NO ParseParams at all and could never be overridden. The overlay is
        //    useful for EVERY packed managed variable, default or not.
        if (packedFields.Count == 0) return false;

        string pad3 = pad2 + Indent;       // inside the unsafe { }
        string pad4 = pad3 + Indent;       // inside the lambda body
        string pad5 = pad4 + Indent;       // inside each { } block per variable
        string pad6 = pad5 + Indent;       // inside a switch case

        sb.AppendLine($"{pad2}// 4a. Managed parameter supply: bake defaults, then overlay from json (DEBT-AIB-021).");
        sb.AppendLine($"{pad2}// ParseParamsDelegate uses byte* — must be captured in an unsafe block.");
        EmitBakeDefaultsFunction(sb, dto, packedFields, defaults, pad2);
        sb.AppendLine($"{pad3}__parseParams = static (string json, byte* memory, int capacity, global::Fdp.Core.EntityRepository world, global::Fdp.Core.Entity self) =>");
        sb.AppendLine($"{pad3}{{");
        sb.AppendLine($"{pad4}// Step 1 — baked defaults (CE-427: its own function, so a curated supply keeps it).");
        sb.AppendLine($"{pad4}__BakeDefaults(memory, capacity);");

        // ── step 2: overlay from the incoming json ───────────────────────────────
        sb.AppendLine();
        if (HasResolver(dto))
        {
            // ⭐⭐ CE-443 (R-155, DESIGN_Parameter_Model §P.2) — stage 2 IS the resolver, handed the SOURCE:
            //   the JSON parsed into the resolver's own authored Params (its Parameter defaults, then the
            //   JSON by name). ⛔ No overlay onto In — the resolver writes whatever it chooses.
            sb.AppendLine($"{pad4}// Step 2 — CE-443: the bound resolver, handed the parsed authored DTO (no copy onto In).");
            sb.AppendLine($"{pad4}__ResolveRoot(json, memory, capacity, world, self);");
        }
        else
        {
        sb.AppendLine($"{pad4}// Step 2 — overlay. A wrapper object keyed by VARIABLE NAME, dispatched to each");
        sb.AppendLine($"{pad4}// variable's deserializer (DEBT-AIB-021 names this shape).");
        sb.AppendLine($"{pad4}// ⛔ Malformed json THROWS on purpose: the ingress parses into a stack shadow and");
        sb.AppendLine($"{pad4}//    commits only on success, so a throw leaves the entity on its old behaviour.");
        sb.AppendLine($"{pad4}if (!string.IsNullOrWhiteSpace(json))");
        sb.AppendLine($"{pad4}{{");
        sb.AppendLine($"{pad5}using var __doc = global::System.Text.Json.JsonDocument.Parse(json);");
        sb.AppendLine($"{pad5}if (__doc.RootElement.ValueKind == global::System.Text.Json.JsonValueKind.Object)");
        sb.AppendLine($"{pad5}{{");
        sb.AppendLine($"{pad5}{Indent}foreach (var __prop in __doc.RootElement.EnumerateObject())");
        sb.AppendLine($"{pad5}{Indent}{{");
        sb.AppendLine($"{pad5}{Indent}{Indent}switch (__prop.Name)");
        sb.AppendLine($"{pad5}{Indent}{Indent}{{");
        foreach (var f in packedFields)
        {
            string dtoTypeFqn = DtoTypeToGlobal(f.TypeId);
            sb.AppendLine($"{pad5}{Indent}{Indent}{Indent}case \"{EscapeCSharpStringLiteral(f.Name)}\":");
            sb.AppendLine($"{pad5}{Indent}{Indent}{Indent}{{");
            sb.AppendLine($"{pad5}{Indent}{Indent}{Indent}{Indent}var __o = global::System.Text.Json.JsonSerializer.Deserialize<{dtoTypeFqn}>(__prop.Value.GetRawText(), __paramJsonOpts);");
            sb.AppendLine($"{pad5}{Indent}{Indent}{Indent}{Indent}global::System.Runtime.CompilerServices.Unsafe.Write(memory + {f.ByteOffset}, __o);");
            sb.AppendLine($"{pad5}{Indent}{Indent}{Indent}{Indent}break;");
            sb.AppendLine($"{pad5}{Indent}{Indent}{Indent}}}");
        }
        sb.AppendLine($"{pad5}{Indent}{Indent}{Indent}// ⭐ Unknown key: IGNORED, matching the curated path's own behaviour.");
        sb.AppendLine($"{pad5}{Indent}{Indent}{Indent}default: break;");
        sb.AppendLine($"{pad5}{Indent}{Indent}}}");
        sb.AppendLine($"{pad5}{Indent}}}");
        sb.AppendLine($"{pad5}}}");
        sb.AppendLine($"{pad4}}}");

        }

        sb.AppendLine($"{pad3}}};");
        sb.AppendLine($"{pad2}}}");
        sb.AppendLine();

        return true;
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-416</c> (<c>Q76</c> §12.27) — the ROOT-PARAMS LOCALS, for ANY tier: <c>__bakeDefaults</c>, <c>__parseParams</c>
    /// (bake → supply → resolve) and the <c>CE-429</c> State-only bake. Extracted verbatim from the BTree register method so the
    /// HSM bridge CALLS it instead of keeping its own copy. ⭐ <paramref name="dto"/> is a blackboard owner: only its name, asset
    /// id, namespace, blackboard and resolver are read (<c>HsmBridgeEmitCore.BlackboardOwner</c> builds one for an HSM).
    /// </summary>
    /// <returns>true when a <c>__parseParams</c> local was emitted.</returns>
    internal static bool EmitRootParamsLocals(
        StringBuilder sb, BehaviorTreeAssetDto dto,
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? packedFields, bool isManaged, string pad2)
    {
        // 4a. Emit ParseParams into a local variable (must be declared in an unsafe context so
        //     the byte* parameter in the lambda is legal). The local is then passed into the
        //     BehaviorDefinition initializer below. Only emitted when ≥1 variable has a default.
        bool hasParseParams = false;
        if (isManaged && packedFields != null)
            hasParseParams = EmitParseParamsLocal(sb, dto, packedFields, pad2);

        // ⭐⭐ CE-429 — ingress allocates the root block only for a behaviour with a ParseParams
        //   (BehaviorIngressSystem: `def.ParseParams != null`). A block with a State half and no
        //   Role=Input variable has nothing to parse, but it must still be ALLOCATED ⇒ it declares a
        //   parse that supplies nothing. ⚠ Stage 1 (bake) of the State half's defaults is CE-426's.
        if (!hasParseParams && EmitsBlock(dto, packedFields) && BTreeEmitCore.BlockStateVariables(dto).Count > 0)
        {
            sb.AppendLine($"{pad2}// 4a. CE-429: a block with no Role=Input variable — nothing to overlay, but it must be allocated (and its State baked).");
            EmitBakeDefaultsFunction(sb, dto, packedFields,
                System.Array.Empty<(BTreeBlackboardPackHelper.PackedField, string)>(), pad2);
            sb.AppendLine(HasResolver(dto)
                ? $"{pad2}{Indent}__parseParams = static (string json, byte* memory, int capacity, global::Fdp.Core.EntityRepository world, global::Fdp.Core.Entity self) => {{ __BakeDefaults(memory, capacity); __ResolveRoot(json, memory, capacity, world, self); }};"
                : $"{pad2}{Indent}__parseParams = static (string json, byte* memory, int capacity, global::Fdp.Core.EntityRepository world, global::Fdp.Core.Entity self) => __BakeDefaults(memory, capacity);");
            sb.AppendLine($"{pad2}}}");
            hasParseParams = true;
        }

        return hasParseParams;
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-416</c> (<c>Q76</c> §12.27) — the ROOT-PARAMS MEMBERS of the <c>BehaviorDefinition</c> initializer, for ANY
    /// tier: the layout hash, the manifest, <c>JsonParamsDtoType</c> / <c>BlackboardLayoutType</c>, and <c>ParseParams</c> /
    /// <c>BakeDefaults</c> / <c>ResolveStage</c>. Extracted verbatim; same owner contract as <see cref="EmitRootParamsLocals"/>.
    /// </summary>
    internal static void EmitRootParamsMembers(
        StringBuilder sb, BehaviorTreeAssetDto dto,
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? packedFields, bool isManaged, bool hasParseParams, string pad2)
    {
        // ⭐ CE-455 — the root block's LAYOUT hash, so a hot reload that reorders/retypes the parameters at the SAME
        //   width restarts a running instance (BrainTickSystem.RestartIfRelaidOut already compares it for every tier).
        if ((packedFields != null && packedFields.Count > 0) || BTreeEmitCore.BlockStateVariables(dto).Count > 0)
        {
            var __state = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>();
            foreach (var v in BTreeEmitCore.BlockStateVariables(dto))
                __state.Add(new System.Collections.Generic.KeyValuePair<string, string>(v.Name ?? "", v.Type?.TypeId ?? ""));
            ulong __layout = BTreeBlackboardPackHelper.LayoutHash(packedFields, __state);
            sb.AppendLine($"{pad2}{Indent}BlueprintStructureHash = {__layout}UL,   // CE-455: the root block's layout");
        }
        if (isManaged && packedFields != null && packedFields.Count > 0)
        {
            EmitManagedBlackboardVariablesArray(sb, packedFields, pad2 + Indent);

            // ⭐⭐⭐ CE-235 — the AUTHORED JSON CONTRACT for a JSON-authored asset.
            //
            // For a generated asset the authored shape and the blackboard layout COINCIDE, and that
            // is the design's default case, not a shortcut: Behavior_Parameter_Resolver_Detailed_Design
            // §3.2 — "one shape by default — the authored DTO is an auto-generated mirror; two shapes
            // only on divergence". The emitted struct IS that mirror. Its field names are exactly the
            // `case` labels EmitParseParamsLocal writes from this same packedFields list, so the
            // schema cannot drift from the parser — one list, three artefacts.
            //
            // ⛔ The two members still hold DIFFERENT types for the curated behaviours that DO
            //   diverge (a geo point vs a Cartesian pair; a network id vs a resolved Entity) — those
            //   get their authored DTO from [BehaviorContract] via BehaviorSchemaDiscovery.
            //
            // ⚠ Guarded by `packedFields.Count > 0` because that is the exact condition under which
            //   BTreeEmitCore.EmitBlackboardStructSource emits the struct at all; naming it otherwise
            //   would emit a reference to a type that does not exist.
            //
            // ⭐⭐⭐ CE-437 + CE-429 (2026-09-29) — THE TWO MEMBERS DIVERGE, AS CE-235 SPLIT THEM TO.
            //   JsonParamsDtoType stays the Inputs struct (the PUBLIC authored contract);
            //   BlackboardLayoutType becomes {Asset}_Block — Inputs at offset 0 plus the State half —
            //   and RootParamsAccess.RootParamsBytes sizes the root slot from it. 📄 Q76 §12.2b.
            // ⭐⭐ CE-443 — with a bound resolver asset the AUTHORED contract is the resolver's own Params
            //   (its declared Parameters, DESIGN_Parameter_Model §P.7): the resolver owns the shape it converts from.
            string bbStructFqn = HasResolver(dto) ? ResolverParamsFqn(dto) : BTreeEmitCore.BlackboardStructFqn(dto);
            sb.AppendLine($"{pad2}{Indent}JsonParamsDtoType    = typeof({bbStructFqn}),");
            sb.AppendLine($"{pad2}{Indent}BlackboardLayoutType = typeof({BTreeEmitCore.BlockStructFqn(dto)}),");
        }
        else if (EmitsBlock(dto, packedFields) && BTreeEmitCore.BlockStateVariables(dto).Count > 0)
        {
            // ⭐⭐ CE-429 — R-151 requirement ④: a behaviour may declare NO Role=Input variable.
            //   It still owns a block (its State half), so it names the block as its layout and
            //   declares an EMPTY manifest — which RootParamsAccess.InputBytes reads as "0 Input
            //   bytes", so nothing carries into the State half across a behaviour change.
            //   ⛔ No JsonParamsDtoType: there is no authored contract to publish.
            sb.AppendLine($"{pad2}{Indent}ManagedBlackboardVariables = global::System.Array.Empty<global::Fdp.Toolkit.Behavior.ManagedBlackboardVariable>(),");
            sb.AppendLine($"{pad2}{Indent}BlackboardLayoutType = typeof({BTreeEmitCore.BlockStructFqn(dto)}),");
            if (HasResolver(dto))   // CE-443: the resolver's Parameters are the authored contract even with no Input half
                sb.AppendLine($"{pad2}{Indent}JsonParamsDtoType    = typeof({ResolverParamsFqn(dto)}),");
        }
        if (hasParseParams)
        {
            sb.AppendLine($"{pad2}{Indent}ParseParams  = __parseParams,");
            sb.AppendLine($"{pad2}{Indent}BakeDefaults = __bakeDefaults,");   // CE-427: stage 1 on its own
            if (HasResolver(dto))
            {
                sb.AppendLine($"{pad2}{Indent}ResolveStage = __resolveStage,");   // CE-443: the resolver, handed a source (hosted)
                sb.AppendLine($"{pad2}{Indent}ResolverName = \"{EscapeCSharpStringLiteral(dto.Resolver!.Name)}\",");
            }
        }
        else if (HasResolver(dto))
        {
            // ⛔ CE-428 — a resolver refines a BLOCK; a behaviour with no managed variables has none. Loud, at compile.
            sb.AppendLine($"#error CE-428: behaviour '{dto.Name}' names resolver asset '{dto.Resolver!.Name}' but declares no managed blackboard variables, so it has no block to resolve.");
        }
    }

    /// <summary>
    /// ⭐⭐ <c>CE-427</c> — emits STAGE 1 as a static local function <c>__BakeDefaults</c> (every
    /// Input default at its packed offset, then the State half's defaults), declares
    /// <c>__bakeDefaults</c> / <c>__parseParams</c>, and OPENS the <c>unsafe</c> block the caller's
    /// parse lambda goes in (the caller closes it). ⭐ The registrar publishes the function as
    /// <c>BehaviorDefinition.BakeDefaults</c>, so a curated resolver that replaces the SUPPLY still
    /// gets the bake composed in front of it (<c>BehaviorRegistry.ApplyResolverOverlay</c>).
    /// </summary>
    private static void EmitBakeDefaultsFunction(
        StringBuilder sb, BehaviorTreeAssetDto dto,
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? packedFields,
        IReadOnlyList<(BTreeBlackboardPackHelper.PackedField Field, string DefaultJson)> defaults,
        string pad2)
    {
        string pad3 = pad2 + Indent;
        string pad4 = pad3 + Indent;
        string pad5 = pad4 + Indent;
        sb.AppendLine($"{pad2}global::Fdp.Toolkit.Behavior.BakeDefaultsDelegate? __bakeDefaults;");
        if (HasResolver(dto))
            sb.AppendLine($"{pad2}global::Fdp.Toolkit.Behavior.ResolveStageDelegate? __resolveStage;");
        sb.AppendLine($"{pad2}global::Fdp.Toolkit.Behavior.ParseParamsDelegate? __parseParams;");
        sb.AppendLine($"{pad2}unsafe");
        sb.AppendLine($"{pad2}{{");
        sb.AppendLine($"{pad3}// Step 1 — baked defaults. DESIGN_Parameter_Model.md §3.2: the ORDER is the ruling.");
        sb.AppendLine($"{pad3}static void __BakeDefaults(byte* memory, int capacity)");
        sb.AppendLine($"{pad3}{{");
        foreach (var (field, defaultJson) in defaults)
        {
            string dtoTypeFqn = DtoTypeToGlobal(field.TypeId);
            string escaped    = EscapeCSharpStringLiteral(defaultJson);
            sb.AppendLine($"{pad4}{{");
            sb.AppendLine($"{pad5}var __v = global::System.Text.Json.JsonSerializer.Deserialize<{dtoTypeFqn}>(\"{escaped}\", __paramJsonOpts);");
            sb.AppendLine($"{pad5}global::System.Runtime.CompilerServices.Unsafe.Write(memory + {field.ByteOffset}, __v);");
            sb.AppendLine($"{pad4}}}");
        }
        EmitStateDefaultBake(sb, dto, packedFields, pad4);
        sb.AppendLine($"{pad3}}}");
        sb.AppendLine($"{pad3}__bakeDefaults = __BakeDefaults;");

        if (HasResolver(dto))
        {
            // ⭐⭐ CE-428 — STAGE 3: the bound blueprint resolver asset refines the WHOLE block in place, after
            //   bake + supply (Q76 §12.3 / §12.20). A static call — the C# compile checks TAuthored/TBlock.
            string blockFqn = BTreeEmitCore.BlockStructFqn(dto);
            string cls = BlueprintClassNaming.ClassFqn(dto.Resolver!.AssetId, dto.Resolver.Name);
            string authoredFqn = ResolverParamsFqn(dto);
            string guard = $"{pad4}if (capacity < sizeof({blockFqn}))\n{pad4}{Indent}throw new global::System.InvalidOperationException(\"CE-428: the parse buffer is narrower than {EscapeCSharpStringLiteral(dto.Name)}'s block — a stale layout.\");";
            sb.AppendLine($"{pad3}// CE-443: the resolver asset '{EscapeCSharpStringLiteral(dto.Resolver.Name)}', handed the SOURCE (R-155).");
            // Root: the JSON → the resolver's authored Params (defaults, then JSON by name) → resolve.
            sb.AppendLine($"{pad3}static void __ResolveRoot(string json, byte* memory, int capacity, global::Fdp.Core.EntityRepository world, global::Fdp.Core.Entity self)");
            sb.AppendLine($"{pad3}{{");
            sb.AppendLine(guard);
            sb.AppendLine($"{pad4}{cls}.ParseAuthored(json, out var __authored);");
            sb.AppendLine($"{pad4}{cls}.ResolveBehavior(in __authored, ref global::System.Runtime.CompilerServices.Unsafe.AsRef<{blockFqn}>(memory), world, self);");
            sb.AppendLine($"{pad3}}}");
            // Hosted: the bound host variable's bytes ARE the authored Params; none ⇒ its defaults.
            sb.AppendLine($"{pad3}static void __ResolveStage(byte* source, int sourceBytes, byte* memory, int capacity, global::Fdp.Core.EntityRepository world, global::Fdp.Core.Entity self)");
            sb.AppendLine($"{pad3}{{");
            sb.AppendLine(guard);
            sb.AppendLine($"{pad4}{authoredFqn} __authored;");
            sb.AppendLine($"{pad4}if (source == null) {cls}.ParseAuthored(null, out __authored);");
            sb.AppendLine($"{pad4}else");
            sb.AppendLine($"{pad4}{{");
            sb.AppendLine($"{pad4}{Indent}if (sourceBytes != sizeof({authoredFqn}))");
            sb.AppendLine($"{pad4}{Indent}{Indent}throw new global::System.InvalidOperationException($\"CE-443: the host variable bound to '{EscapeCSharpStringLiteral(dto.Name)}' is {{sourceBytes}} bytes but its resolver's authored Params is {{sizeof({authoredFqn})}}. The bound variable must be that type.\");");
            sb.AppendLine($"{pad4}{Indent}__authored = *({authoredFqn}*)source;");
            sb.AppendLine($"{pad4}}}");
            sb.AppendLine($"{pad4}{cls}.ResolveBehavior(in __authored, ref global::System.Runtime.CompilerServices.Unsafe.AsRef<{blockFqn}>(memory), world, self);");
            sb.AppendLine($"{pad3}}}");
            sb.AppendLine($"{pad3}__resolveStage = __ResolveStage;");
        }
    }

    /// <summary>⭐ <c>CE-443</c> — the resolver asset's authored <c>Params</c> struct (from its declared Parameters).</summary>
    private static string ResolverParamsFqn(BehaviorTreeAssetDto dto)
        => BlueprintClassNaming.ClassFqn(dto.Resolver!.AssetId, dto.Resolver.Name) + ".Params";

    /// <summary>⭐ <c>CE-428</c> — does this behaviour name a resolver asset?</summary>
    private static bool HasResolver(BehaviorTreeAssetDto dto)
        => dto.Resolver is { } r && r.AssetId != Guid.Empty;

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
    /// <remarks>⭐ <c>E1</c>: <c>internal</c> so the HSM emitter renders type names identically.</remarks>
    internal static string DtoTypeToGlobal(string typeId)
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
                sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{BlackboardParamsExpression.AtBlock("bb", bbShort, "ctx.World", "ctx.Self", d.DtoByteOffset)});");
                // ⭐ A2b — the THIRD emitted ladder, and the one my own A2 census MISSED because it is
                //   PARAMETERISED PER TIER (three calls to one helper) rather than written out inline.
                //   ⚠ Same collapse, same seam; the deactivator returns void, so a miss just returns.
                AppendWorkingStateResolve(sb, pad2 + Indent + Indent + Indent, dto, packedFields, slotKey.Value, d.WorkingStateTypeFqn ?? string.Empty,
                    "S3-G", "deactivator ", "return;");
                sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{methodRef}(ref dto, ref ws, ref st, ref ctx, pi);");
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
                sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{Indent}{BlackboardParamsExpression.AtBlock("bb", bbShort, "ctx.World", "ctx.Self", d.DtoByteOffset)});");
                sb.AppendLine($"{pad2}{Indent}{Indent}{Indent}{methodRef}(ref dto, ref st, ref ctx);");
                sb.AppendLine($"{pad2}{Indent}{Indent}}}");
                sb.AppendLine($"{pad2}{Indent}}});");
            }
        }
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
