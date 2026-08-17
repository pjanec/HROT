using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hrot.AiEditor.Persistence.BTree;
using Hrot.AiEditor.Persistence.Hsm;

namespace Hrot.AiEditor.Persistence.Emit;

/// <summary>
/// Emits the <c>[BlueprintRegistrar]</c> self-registration bridge class for an HSM asset.
/// Design §3 D14, §6.3, §14 (PU-203): emits a per-asset isolated static class decorated
/// <c>[BlueprintRegistrar]</c> (NOT <c>[FbtRegistrar]</c>/<c>[HsmActionRegistrar]</c>) with
/// <c>public static void Register(BehaviorRegistry beh, BlueprintRegistryStaging staging)</c>.
///
/// Inside Register:
/// - Compiles the HSM definition blob from the topology-core thunk and calls
///   <c>beh.Register(id, name, BehaviorDefinition)</c> with the HsmDefinition.
/// - Registers HSM action thunks via the STATIC
///   <c>HsmActionDispatcher.RegisterAction(ushort, IntPtr)</c>.
/// - Registers HSM guard thunks via the STATIC
///   <c>HsmActionDispatcher.RegisterGuard(ushort, IntPtr)</c>.
///   (HsmActionDispatcher is a static class and cannot be injected; §14 item 4.)
///
/// The bridge is ADDITIVE: a separate class from the topology-core class (PU-205
/// equivalence compares only the topology core; bridge is excluded per §14 item 3).
/// BTree bridge is analogous — see <see cref="BTreeBridgeEmitCore"/>.
/// </summary>
public static class HsmBridgeEmitCore
{
    private const string Indent = "    ";

    /// <summary>
    /// Emits the [BlueprintRegistrar] bridge class source for the given HSM DTO.
    /// </summary>
    public static string EmitBridge(HsmAssetDto dto)
        => EmitBridge(dto, sizeResolver: null);

    /// <summary>
    /// Emits the [BlueprintRegistrar] bridge class source for the given HSM DTO, using an optional
    /// size resolver for struct-DTO types — the BTree bridge's own seam
    /// (<see cref="BTreeBridgeEmitCore.EmitBridge(BTree.BehaviorTreeAssetDto, System.Func{string, int?})"/>),
    /// so a managed HSM blackboard can carry the same struct-typed variables a managed BTree one can.
    /// </summary>
    public static string EmitBridge(HsmAssetDto dto, Func<string, int?>? sizeResolver)
    {
        var sb = new StringBuilder();

        // ⭐⭐⭐ BP-281 — the params supply is decided ONCE, here, and the decision is the PACKED
        //   FIELD LIST itself, not a predicate re-derived at each use site.
        //
        // ⚠ Defects (b) and (c) of DEBT-AIB-021 were not "the wrong condition" — they were TWO
        //   conditions that disagreed: the options field and the ParseParams body each decided for
        //   themselves whether params existed, and one of them said "≥1 default". ⛔ Copying the BTree
        //   bridge's guards would have reproduced that split on a second host.
        //
        // ⭐ So the three emissions below (the `#nullable enable` pragma, the options field, the
        //   ParseParams local) all read ONE value, and it is the same value the body consumes. An
        //   asset whose variables are all Role=State packs to nothing and emits none of the three —
        //   which is right, because State lives in the partition tier, not the inline param region.
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField> packedFields = PackParams(dto, sizeResolver);
        bool emitsParseParams = packedFields.Count > 0;

        // Header
        sb.AppendLine(AiEmitCoreBase.BuildHeader(dto.AssetId));

        // The emitted ParseParams lambda annotates `IHostVariableAccess? host`, which is a
        // nullable-reference annotation and needs an in-file pragma in generator output (CS8632/CS8669
        // otherwise — the project-level <Nullable>enable</Nullable> does not propagate). ⭐ Emitted
        // ONLY for assets that emit a ParseParams, so every other asset's bridge stays byte-identical.
        if (emitsParseParams)
        {
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
        }

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

        var targetNs    = string.IsNullOrEmpty(dto.TargetNamespace)
            ? "Hrot.AI.Behaviors.Machines"
            : dto.TargetNamespace;
        var coreClass   = SanitizeIdentifier(dto.Name);
        var bridgeClass = coreClass + "Registrar";

        sb.AppendLine($"namespace {targetNs};");
        sb.AppendLine();

        // [BlueprintRegistrar] ONLY — not [FbtRegistrar]/[HsmActionRegistrar] (§14 item 4).
        sb.AppendLine($"[BlueprintRegistrar]");
        sb.AppendLine($"public static class {bridgeClass}");
        sb.AppendLine("{");

        // ⭐⭐ BP-281 — the JSON options field, guarded by the SAME value as everything else.
        //    ⚠ DEFECT (c) of DEBT-AIB-021 was keying this on "≥1 default": the overlay needs the
        //    options whether or not anything was defaulted.
        if (emitsParseParams)
        {
            sb.AppendLine($"{Indent}// JSON options for ParseParams — the platform-canonical options (IncludeFields,");
            sb.AppendLine($"{Indent}// vector/FixedString/strict-enum converters, and FC-3b fixed-list support) so");
            sb.AppendLine($"{Indent}// Params defaults share ONE wire format with scenario save/load.");
            sb.AppendLine($"{Indent}private static readonly global::System.Text.Json.JsonSerializerOptions __paramJsonOpts =");
            sb.AppendLine($"{Indent}{Indent}global::Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;");
            sb.AppendLine();
        }

        EmitHsmRegisterMethod(sb, dto, coreClass, packedFields);

        sb.AppendLine("}");

        return sb.ToString();
    }

    // ── Register method ─────────────────────────────────────────────────────────

    private static void EmitHsmRegisterMethod(
        StringBuilder sb, HsmAssetDto dto, string coreClass,
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField> packedFields)
    {
        string pad  = Indent;
        string pad2 = Indent + Indent;

        int behaviorId = BTreeBridgeEmitCore.DeterministicIdFromGuid(dto.AssetId);
        string name    = dto.Name.Replace("\"", "\\\"");

        sb.AppendLine($"{pad}/// <summary>");
        sb.AppendLine($"{pad}/// Coordinator-injectable registrar (§3 D14, PU-203).");
        sb.AppendLine($"{pad}/// Registers the JSON-owned HSM definition and action/guard thunks.");
        sb.AppendLine($"{pad}/// HsmActionDispatcher is a static class and is called STATICALLY (§14 item 4).");
        sb.AppendLine($"{pad}/// </summary>");
        sb.AppendLine($"{pad}public static void Register(BehaviorRegistry beh, BlueprintRegistryStaging staging)");
        sb.AppendLine($"{pad}{{");

        // Build blob
        sb.AppendLine($"{pad2}// Compile the blob from the topology-core thunk.");
        sb.AppendLine($"{pad2}var blob = {coreClass}.Compile();");
        sb.AppendLine();

        // ⭐⭐⭐ BP-281 — the params supply, emitted BEFORE the definition that carries it.
        bool hasParseParams = EmitParseParamsLocal(sb, dto, packedFields, pad2);

        // Register definition
        sb.AppendLine($"{pad2}// Register the JSON-owned HSM definition.");
        sb.AppendLine($"{pad2}beh.Register({behaviorId}, \"{name}\", new BehaviorDefinition");
        sb.AppendLine($"{pad2}{{");
        sb.AppendLine($"{pad2}{Indent}Name          = \"{name}\",");
        sb.AppendLine($"{pad2}{Indent}BrainTier     = BehaviorConstants.BrainTierHsm,");
        sb.AppendLine($"{pad2}{Indent}HsmDefinition = blob,");
        if (hasParseParams)
            sb.AppendLine($"{pad2}{Indent}ParseParams   = __parseParams,");
        EmitStatefulWorkingSlotsArray(sb, dto, pad2 + Indent);
        sb.AppendLine($"{pad2}}});");

        // ⛔⛔ W3 (Batch 59) — THE COUNTER-ALLOCATED STUB REGISTRATIONS ARE GONE.
        //
        // This used to emit, for each action FQN in the asset:
        //     static void __hsActionStub(void*, void*, HsmCommandWriter*) { }
        //     HsmActionDispatcher.RegisterAction(100++, &__hsActionStub);
        // and the guard twin from 200. ⭐ Two facts, both measured, make that pure hazard:
        //
        //   1. 🔴 NOTHING EVER LOOKED THEM UP. `HsmFlattener:111` builds its action table as
        //      `actionTable[name] = ComputeHash(name)` and `:172-175` / `:233` / `:376` set every
        //      `OnEntryActionId` / `ActivityActionId` / transition `ActionId` from THAT table. ⇒ the
        //      blob addresses hashed ids only; 100.. and 200.. were never reachable. (The one bypass,
        //      a numeric `entryActionId` in the JSON — `JsonStateMachineParser:48` — is used by
        //      neither shipped asset.)
        //   2. 🔴🔴 AND THEY COULD OVERWRITE A REAL ACTION. `HsmActionDispatcher.RegisterAction` is
        //      `ActionTable[id] = a` — last writer wins, silently — while `ComputeHash` ranges over the
        //      whole `0…65535`, INCLUDING 100.. and 200… A real action whose name hashed into the
        //      window was replaced by a body that does nothing: no crash, no log, one state that
        //      quietly did nothing, forever.
        //
        // ⚠ Nothing is lost by deleting them. The bodies they registered were empty, so even in the
        //   hot-reload case the comment invoked ("the bridge ensures the IDs are known to the
        //   dispatcher"), what was known was a no-op. The real bodies come from the hand-authored
        //   `[HsmAction]` methods, registered by `HsmActionRegistrar.RegisterAll()` under the hashed
        //   ids the blob actually uses.
        //
        // ⭐ `BHU_020` (Batch 58) is the rail that proves this stays gone: it ranges over the FINAL id
        //   set, so a reintroduced counter-allocated registration colliding with a hashed one fails
        //   the build instead of silently winning.

        sb.AppendLine($"{pad}}}");
    }

    // ── BP-281: the params supply ──────────────────────────────────────────────

    /// <summary>
    /// ⭐⭐⭐ <b><c>BP-281</c> — HSM's <c>ParseParams</c> counterpart.</b> Before this, an HSM asset
    /// could declare a <c>Role = Input</c> variable, round-trip it, and see it in the editor's own
    /// section — and <b>nothing wrote it at runtime</b>. 📄 <c>DESIGN_Parameter_Model.md</c> §3:
    /// one pipeline for every host.
    ///
    /// <para>
    /// ⭐⭐ <b>Mirrored from <c>BTreeBridgeEmitCore.EmitParseParamsLocal</c> AS IT STANDS AFTER
    /// <c>DEBT-AIB-021</c></b>, not from the pre-021 shape:
    /// <list type="number">
    ///   <item><description>Step 1 — bake the authored defaults, in declaration order.</description></item>
    ///   <item><description>Step 2 — overlay the incoming JSON, keyed by VARIABLE NAME. An unknown key
    ///   is <b>ignored</b>; a variable the JSON does not mention keeps its default.</description></item>
    ///   <item><description>⛔ Malformed JSON <b>throws, deliberately</b> — <c>BehaviorIngressSystem</c>
    ///   parses into a stack shadow and commits only on success, so a throw is exactly what leaves the
    ///   entity on its old behaviour.</description></item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// ⭐⭐ <b>The DESTINATION POINTER.</b> <c>memory</c> is the base of the entity's
    /// <c>BrainBlackboard</c> — <c>BehaviorIngressSystem:100</c> passes a shadow of the whole
    /// component — and <c>BehaviorParameters</c> sits at <c>[FieldOffset(0)]</c> of it. ⇒ writing at
    /// <c>memory + packedOffset</c> is the SAME region the analyzer's HSM thunks read at
    /// <c>bb.BehaviorParameters[0] + offset</c>. ⭐ <b>A root HSM behaviour has exactly one params
    /// area, so this needs nothing from <c>E3</c></b> (per-occurrence storage) — see the batch report.
    /// </para>
    ///
    /// <para>
    /// ⭐⭐ <b>The PACKER is BTree's, called — not copied</b> (ruling 9, and the same choice
    /// <c>E1</c> made for the slot key). <c>State</c>-role variables are excluded by
    /// <c>Pack</c> itself: they live in the partition tier, not the inline param region.
    /// </para>
    /// </summary>
    /// <returns>true when a <c>__parseParams</c> local was emitted.</returns>
    private static bool EmitParseParamsLocal(
        StringBuilder sb, HsmAssetDto dto,
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField> packedFields, string pad2)
    {
        // ⭐⭐ DEFECT (b) of DEBT-AIB-021: the BTree guard used to be "≥1 DEFAULT", so an asset whose
        //    variables had no defaults emitted no ParseParams and could never be overridden at all.
        //    ⛔ The overlay is useful for EVERY packed variable, default or not — so the condition is
        //    "≥1 PACKED variable", and it is the SAME list the caller already computed.
        if (packedFields.Count == 0) return false;

        var variables = dto.Blackboard!.Variables;

        // Baked defaults: variables carrying a non-null DefaultValueJson that are also packed.
        var offsetMap = new Dictionary<string, BTreeBlackboardPackHelper.PackedField>(StringComparer.Ordinal);
        foreach (var f in packedFields)
            offsetMap[f.Name] = f;

        var defaults = new List<(BTreeBlackboardPackHelper.PackedField Field, string DefaultJson)>();
        foreach (var v in variables)
        {
            if (v.DefaultValueJson == null) continue;
            if (!offsetMap.TryGetValue(v.Name, out var field)) continue;
            defaults.Add((field, v.DefaultValueJson));
        }

        string pad3 = pad2 + Indent;       // inside the unsafe { }
        string pad4 = pad3 + Indent;       // inside the lambda body
        string pad5 = pad4 + Indent;       // inside each { } block per variable

        sb.AppendLine($"{pad2}// BP-281: managed parameter supply — bake defaults, then overlay from json.");
        sb.AppendLine($"{pad2}// The SAME ParseParamsDelegate the BTree bridge emits (DESIGN_Parameter_Model.md §3).");
        sb.AppendLine($"{pad2}// ParseParamsDelegate uses byte* — must be captured in an unsafe block.");
        sb.AppendLine($"{pad2}global::Fdp.Toolkit.Behavior.ParseParamsDelegate? __parseParams;");
        sb.AppendLine($"{pad2}unsafe");
        sb.AppendLine($"{pad2}{{");
        sb.AppendLine($"{pad3}__parseParams = static (string json, byte* memory, global::Fdp.Core.EntityRepository world, global::Fdp.Core.Entity self, global::Fdp.Toolkit.Behavior.IHostVariableAccess? host) =>");
        sb.AppendLine($"{pad3}{{");

        // ── step 1: bake the defaults ────────────────────────────────────────────
        sb.AppendLine($"{pad4}// Step 1 — baked defaults. DESIGN_Parameter_Model.md §3.2: the ORDER is the ruling.");
        foreach (var (field, defaultJson) in defaults)
        {
            string dtoTypeFqn = BTreeBridgeEmitCore.DtoTypeToGlobal(field.TypeId);
            string escaped    = EscapeCSharpStringLiteral(defaultJson);
            sb.AppendLine($"{pad4}{{");
            sb.AppendLine($"{pad5}var __v = global::System.Text.Json.JsonSerializer.Deserialize<{dtoTypeFqn}>(\"{escaped}\", __paramJsonOpts);");
            sb.AppendLine($"{pad5}global::System.Runtime.CompilerServices.Unsafe.Write(memory + {field.ByteOffset}, __v);");
            sb.AppendLine($"{pad4}}}");
        }

        // ── step 2: overlay from the incoming json ───────────────────────────────
        sb.AppendLine();
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
            string dtoTypeFqn = BTreeBridgeEmitCore.DtoTypeToGlobal(f.TypeId);
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

        sb.AppendLine($"{pad3}}};");
        sb.AppendLine($"{pad2}}}");
        sb.AppendLine();

        return true;
    }

    /// <summary>
    /// ⭐⭐ Packs an HSM asset's managed blackboard into inline param offsets. ⛔ Returns an EMPTY
    /// list — never null — for every case that has no inline params: a non-managed blackboard, no
    /// variables, only <c>State</c>-role variables, or a type no resolver can size. ⭐ One return
    /// shape means the caller has one condition to test, which is the whole point of hoisting this.
    /// </summary>
    private static IReadOnlyList<BTreeBlackboardPackHelper.PackedField> PackParams(
        HsmAssetDto dto, Func<string, int?>? sizeResolver)
    {
        var variables = dto.Blackboard?.Variables;
        if (dto.Blackboard == null || !dto.Blackboard.Managed || variables == null || variables.Count == 0)
            return Array.Empty<BTreeBlackboardPackHelper.PackedField>();

        try
        {
            return BTreeBlackboardPackHelper.Pack(ToPackable(variables), sizeResolver, out _);
        }
        catch
        {
            // ⚠ An unsizeable type means the layout is not knowable. ⛔ Emit nothing rather than a
            //   ParseParams writing at offsets that are a guess — the BTree bridge makes the same
            //   choice around its own Pack call.
            return Array.Empty<BTreeBlackboardPackHelper.PackedField>();
        }
    }

    /// <summary>
    /// ⭐ Projects HSM blackboard variables onto the shape <see cref="BTreeBlackboardPackHelper.Pack"/>
    /// consumes. ⚠ <c>HsmBlackboardVariableDto</c> and <c>BlackboardVariableDto</c> are
    /// field-for-field twins in two namespaces — a duplication that predates this item and is
    /// <b>not</b> resolved here. ⭐ What matters for ruling 9 is that the PACKING ALGORITHM has one
    /// home: this projection exists so the algorithm can be called rather than copied.
    /// </summary>
    private static IReadOnlyList<BlackboardVariableDto> ToPackable(
        IReadOnlyList<HsmBlackboardVariableDto> variables)
    {
        var result = new List<BlackboardVariableDto>(variables.Count);
        foreach (var v in variables)
        {
            result.Add(new BlackboardVariableDto
            {
                Name             = v.Name,
                Type             = new BlackboardTypeRefDto
                {
                    TypeId      = v.Type?.TypeId ?? string.Empty,
                    IsArray     = v.Type?.IsArray ?? false,
                    FixedLength = v.Type?.FixedLength,
                },
                DefaultValueJson = v.DefaultValueJson,
                Comment          = v.Comment,
                IsAutoManaged    = v.IsAutoManaged,
                Role             = v.Role,
                Scope            = v.Scope,
            });
        }
        return result;
    }

    /// <summary>Escapes a string for use inside a C# double-quoted string literal.</summary>
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

    // ── Usings ─────────────────────────────────────────────────────────────────


    /// <summary>
    /// ⭐⭐⭐ <b><c>E1</c> — an HSM asset's authored <c>Role = State</c> variables become a slot
    /// manifest.</b>
    ///
    /// <para>
    /// 🔴🔴 <b>What this closes.</b> <c>HsmEmitCore</c> and <c>HsmBridgeEmitCore</c> contained
    /// <b>zero</b> <c>Role</c>/<c>Scope</c> references while <c>BTreeBridgeEmitCore</c> contained 45 —
    /// and <c>HsmBlackboardVariableDto</c> persists both faithfully. ⇒ ⛔ <b>a designer could author
    /// working-state variables on an HSM asset, save them, reload them, and have them exist nowhere at
    /// runtime.</b> ⭐ User ruling: <i>"if something is not present in HSM, it is not because it is not
    /// needed, just not implemented yet."</i>
    /// </para>
    ///
    /// <para>
    /// ⭐⭐ <b>The KEY ALGORITHM IS BTREE'S, called — not copied.</b>
    /// <c>BTreeBridgeEmitCore.ComputeStatefulSlotKey(assetId, scope, nodeVisualId, variableId)</c>, and
    /// <c>ComputeTypeNameHash</c>/<c>DtoTypeToGlobal</c> with it. ⛔ A second key algorithm is the one
    /// thing that fails this item's rail: two tiers would hash the same variable to two slots and the
    /// shared allocator would hand out two regions for one concept.
    /// </para>
    ///
    /// <para>
    /// ⭐ <b>Provisioning came free, and that is the point of emitting into
    /// <c>BehaviorDefinition</c>:</b> <c>BehaviorIngressSystem</c> reads
    /// <c>def.StatefulWorkingSlots</c> and provisions <b>without consulting <c>BrainTier</c></b>
    /// (<c>:142-154</c>). ⇒ <c>E2</c> is satisfied by the manifest existing, not by a second
    /// provisioner. <b>Emitting the manifest without provisioning would have been dead data</b> — which
    /// is why the handoff pairs them.
    /// </para>
    ///
    /// <para>
    /// ⚠ <b><c>Node</c> scope is skipped deliberately</b>, mirroring the BTree standalone pass: the
    /// <c>Node</c> key collapses to <c>FNV(assetId ++ nodeVisualId)</c> and ignores the variable name,
    /// so a variable with no node to key off has no meaningful <c>Node</c>-scoped slot.
    /// </para>
    /// </summary>
    private static void EmitStatefulWorkingSlotsArray(StringBuilder sb, HsmAssetDto dto, string pad)
    {
        var variables = dto.Blackboard?.Variables;
        if (variables == null || variables.Count == 0) return;

        // ⭐⭐ Batch 73 — ORDER BY CONSTRUCTION, not by implementation detail.
        //
        // ⛔ This used to accumulate into a Dictionary<int, …> and emit `slotsByKey.Values`. An
        //    insert-only Dictionary<int,V> does enumerate in insertion order IN PRACTICE, but that is a
        //    convention of the current BCL implementation rather than a documented guarantee, and a
        //    single Remove would break it. ⚠ A golden baseline over output ordered by convention can
        //    move for a reason nobody changed — which trains everyone to regenerate, and a gate that is
        //    routinely regenerated is not a gate.
        //
        // ⭐ The emitted ORDER is deliberately unchanged: declaration order, which is what shipped. The
        //    list carries it explicitly and the set does the dedup, so the two jobs the dictionary was
        //    doing at once are now separate and neither is implicit.
        var seenKeys = new HashSet<int>();
        var slots    = new List<(int SlotKey, string TypeId, string Label, int Role, int Scope)>();
        foreach (var v in variables)
        {
            if (v.Role != BlackboardVariableRole.State) continue;
            if (v.Scope != WorkingStateScope.Behavior && v.Scope != WorkingStateScope.Entity) continue;

            string typeId = v.Type?.TypeId ?? string.Empty;
            if (string.IsNullOrEmpty(typeId)) continue;

            int slotKey = BTreeBridgeEmitCore.ComputeStatefulSlotKey(
                dto.AssetId, v.Scope, Guid.Empty, v.Name);
            if (!seenKeys.Add(slotKey)) continue;   // co-scoped duplicates share one slot

            slots.Add((slotKey, typeId, v.Name, (int)v.Role, (int)v.Scope));
        }

        if (slots.Count == 0) return;

        sb.AppendLine($"{pad}StatefulWorkingSlots = new global::Fdp.Toolkit.Behavior.StatefulSlotInfo[]");
        sb.AppendLine($"{pad}{{");
        foreach (var (slotKey, typeId, label, role, scope) in slots)
        {
            string typeFqn = BTreeBridgeEmitCore.DtoTypeToGlobal(typeId);
            // DEBT-AIB-027: the structure hash folds in Marshal.SizeOf<T>() at REGISTRATION time so it
            // changes when the struct grows — identical to the BTree emission, by calling the same helper.
            uint typeNameHash  = BTreeBridgeEmitCore.ComputeTypeNameHash(typeId);
            string escapedLabel = label.Replace("\\", "\\\\").Replace("\"", "\\\"");
            sb.AppendLine(
                $"{pad}{Indent}new global::Fdp.Toolkit.Behavior.StatefulSlotInfo({slotKey}, " +
                $"global::System.Runtime.InteropServices.Marshal.SizeOf<{typeFqn}>(), " +
                $"unchecked({typeNameHash}u ^ (uint)global::System.Runtime.InteropServices.Marshal.SizeOf<{typeFqn}>()), " +
                $"typeof({typeFqn}), \"{escapedLabel}\", " +
                $"(byte)global::Fdp.Toolkit.Blueprints.Partitioning.StatefulSlotRole.{(BlackboardVariableRole)role}, " +
                $"(byte)global::Fdp.Toolkit.Blueprints.Partitioning.StatefulSlotScope.{(WorkingStateScope)scope}),");
        }
        sb.AppendLine($"{pad}}},");
    }

    private static IReadOnlyList<string> CollectBridgeUsings(HsmAssetDto dto)
    {
        var set = new HashSet<string>
        {
            "Fdp.Toolkit.Behavior",
            "Fdp.Toolkit.Blueprints",
            "Fdp.Toolkit.Blueprints.Attributes",
            "Fhsm.Kernel",
            "Fhsm.Kernel.Data",
        };
        return AiEmitCoreBase.SortUsings(set);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    // ⚠ W3 (Batch 59) removed `CollectActions`/`CollectGuards` with the stub registrations they fed.
    //   ⛔ They are NOT a general "what does this asset call" service — nothing else read them, and the
    //   authoritative answer already lives in `HsmFlattener`'s action table, which is what the blob is
    //   addressed by. Keeping an unused collector here would have been a second source for a question
    //   that already has one.

    private static string SanitizeIdentifier(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                sb.Append(c);
            else
                sb.Append('_');
        }
        string result = sb.ToString();
        if (result.Length == 0 || char.IsDigit(result[0]))
            result = "_" + result;
        return result;
    }
}
