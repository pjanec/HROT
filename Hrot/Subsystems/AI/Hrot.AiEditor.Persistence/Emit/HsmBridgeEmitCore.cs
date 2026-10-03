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
    public static string EmitBridge(HsmAssetDto dto, Func<string, int?>? sizeResolver,
        Func<string, SharedAiMethodInfo?>? sharedAi = null)
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

        // ⭐⭐⭐ CE-416 (Q76 §12.27) — the HSM is a BLACKBOARD OWNER like any BTree: the root-params emission (bake →
        //   supply → resolve, the block, the State-only bake) is BTreeBridgeEmitCore's, CALLED through this view.
        //   ⛔ `owned` is null exactly when Pack failed — the same "layout not knowable ⇒ emit nothing" rule the BTree applies.
        var owner = BlackboardOwner(dto);
        bool isManaged = owner.Blackboard.Managed && owner.Blackboard.Variables.Count > 0;
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? owned = null;
        if (isManaged)
        {
            try { owned = BTreeBlackboardPackHelper.Pack(owner.Blackboard.Variables, sizeResolver, out _); }
            catch { owned = null; }
        }
        bool emitsParseParams = isManaged;   // the options field + pragma: the BTree's needsJsonOpts condition

        // Header
        sb.AppendLine(AiEmitCoreBase.BuildHeader(dto.AssetId));

        // The emitted ParseParams lambda annotated `IHostVariableAccess? host` (retired by CE-445; the pragma
        // stays so the goldens do not move), which is a
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

        // ⭐⭐⭐ CE-417 B-2 (a′) — one generated call per bound C# [SharedAi*] binding (SharedAiBindings).
        var sharedAiEntries = SharedAiBindings.Collect(dto, packedFields, sharedAi, sizeResolver);

        EmitHsmRegisterMethod(sb, dto, coreClass, packedFields, owner, owned, isManaged, sharedAiEntries);

        for (int i = 0; i < sharedAiEntries.Count; i++)
            SharedAiBindings.EmitThunk(sb, sharedAiEntries[i], SharedAiThunkName(i), Indent);

        sb.AppendLine("}");

        return sb.ToString();
    }

    // ── Register method ─────────────────────────────────────────────────────────

    private static void EmitHsmRegisterMethod(
        StringBuilder sb, HsmAssetDto dto, string coreClass,
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField> packedFields,
        BehaviorTreeAssetDto owner, IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? owned, bool isManaged,
        IReadOnlyList<SharedAiBindings.Entry> sharedAiEntries)
    {
        string pad  = Indent;
        string pad2 = Indent + Indent;

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
        //   ⭐ CE-416: the SHARED emission (BTreeBridgeEmitCore.EmitRootParamsLocals), not an HSM copy.
        bool hasParseParams = BTreeBridgeEmitCore.EmitRootParamsLocals(sb, owner, owned, isManaged, pad2);

        // Register definition
        sb.AppendLine($"{pad2}// Register the JSON-owned HSM definition.");
        // ⭐⭐ CE-2037 — the id is the NAME's hash, as for every other producer (Behavior_Architecture_Implementation_Plan
        //   Phase 1b: "both producers mint id = FromName(name)"). ⛔ This registrar was the one Phase 1b missed — it minted
        //   FNV over the asset GUID, so BehaviorHashOf(name) and any FromName recompute never matched a JSON HSM.
        sb.AppendLine($"{pad2}beh.Register(global::Fdp.Toolkit.Behavior.BehaviorHash.FromName(\"{name}\"), \"{name}\", new BehaviorDefinition");
        sb.AppendLine($"{pad2}{{");
        sb.AppendLine($"{pad2}{Indent}Name          = \"{name}\",");
        sb.AppendLine($"{pad2}{Indent}BrainTier     = BehaviorConstants.BrainTierHsm,");
        sb.AppendLine($"{pad2}{Indent}HsmDefinition = blob,");
        // ⭐⭐ CE-370 — SYMBOLICATION. Three production consumers read
        //    BehaviorDefinition.HsmMetadata (HsmTraceWorkingMemoryTranslator:52,
        //    HsmTraceWorkingMemoryRenderer:55, BrainTickSystem:447) and this emitter never set it,
        //    so state/event/variable names rendered as NUMBERS for every JSON-authored machine while
        //    the one hand-written machine had them.
        // 📐 The fix is one line because the metadata was ALREADY THERE: StateMachineGraph.Compile()
        //    ends with `blob.Metadata = HsmEmitter.BuildMachineMetadata(this)`. Nothing needed
        //    building — only carrying across.
        sb.AppendLine($"{pad2}{Indent}HsmMetadata   = blob.Metadata,");
        // ⭐⭐⭐ CE-416 — the root-params MEMBERS are the BTree's, called: layout hash (Inputs + State), manifest,
        //   JsonParamsDtoType / BlackboardLayoutType = {Asset}_Block, ParseParams / BakeDefaults. ⛔ SUPERSEDED: CE-235's
        //   "no JsonParamsDtoType here — the HSM emits no struct"; HsmJsonGenerator now emits {Asset}.Blackboard.g.cs.
        BTreeBridgeEmitCore.EmitRootParamsMembers(sb, owner, owned, isManaged, hasParseParams, pad2);
        EmitStatefulWorkingSlotsArray(sb, dto, pad2 + Indent, owner, owned);
        sb.AppendLine($"{pad2}}});");

        // ⭐⭐⭐ E3b-0 — which variable does each STATE's occurrence seed its params from.
        EmitStateParamBindings(sb, dto, packedFields, pad2);

        // ⭐⭐⭐ E5 — which STATES host a child behaviour. 📄 DESIGN §32.8 item 4.
        EmitHostedSubtrees(sb, dto, pad2, owned);

        // ⭐⭐⭐ CE-417 B-2 (a′) — the per-binding C# calls, under the id the blob addresses (Fqn@hostOffset).
        //   ⚠ Two assets binding the same method at the same offset register the same id with an identical body —
        //   the dispatcher's last-writer-wins is then harmless.
        if (sharedAiEntries.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"{pad2}// CE-417: one call per bound [SharedAi*] binding — host offset baked, no occurrence, no state base.");
            for (int i = 0; i < sharedAiEntries.Count; i++)
            {
                var e = sharedAiEntries[i];
                ushort id = Fdp.Toolkit.Behavior.Shared.HsmActionKey.ForCompoundKey(e.Key);
                if (e.Method.IsCondition)
                    sb.AppendLine($"{pad2}unsafe {{ global::Fhsm.Kernel.HsmActionDispatcher.RegisterGuard({id}, (global::System.IntPtr)(delegate* <void*, void*, ushort, global::Fhsm.Kernel.Data.HsmCommandWriter*, bool>)&{SharedAiThunkName(i)}); }}   // {e.Key}");
                else
                    sb.AppendLine($"{pad2}unsafe {{ global::Fhsm.Kernel.HsmActionDispatcher.RegisterAction({id}, (global::System.IntPtr)(delegate* <void*, void*, global::Fhsm.Kernel.Data.HsmCommandWriter*, void>)&{SharedAiThunkName(i)}); }}   // {e.Key}");
            }
        }

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
    /// <summary>
    /// ⭐⭐⭐ <b><c>E3b-0</c> — the <c>state → params offset</c> table, so two parallel regions can seed
    /// from DIFFERENT variables.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §28.6 / §28.6a.
    ///
    /// <para>🔴 <b>The gap.</b> <c>E3a</c> gave every hosted occurrence its own params BYTES, but all of
    /// them seeded from <c>BehaviorParameters[0] + 0</c> — the first packed variable. ⛔ The BTree bridge
    /// avoids this by emitting <b>one adapter per node</b> at a per-site key; the HSM dispatcher takes
    /// <b>one thunk per <c>ushort</c> action id</b>, so there is nowhere to bake a per-site offset.
    /// ⭐ Since <c>E3a</c> moved the params into the slot, the binding only has to reach the SEED.</para>
    ///
    /// <para>⭐⭐ <b>Baked as authoring <c>StableId</c>s, resolved to flat state indices AT RUNTIME</b>
    /// through <c>MachineMetadata.StateStableIds</c> — which the compiler already populates for the
    /// editor projection layer. ⛔ That is why this emitter never needs the flattener's ordering.</para>
    ///
    /// <para>🔒 <b>And it keeps the user's ruling (<c>2026-09-21</c>).</b> This maps the asset's OWN
    /// states to the asset's OWN blackboard variables. ⛔ Nothing here knows a blueprint exists — the
    /// blueprint side only asks <i>"what offset for this (machine, state)?"</i>.</para>
    ///
    /// <para>⚠ <b>Emits NOTHING when no state is bound</b>, which is every asset authored before
    /// <c>E3b-0</c> ⇒ their generated source stays byte-identical, and their occurrences keep seeding
    /// from offset <c>0</c>. ⭐ That is the same gating rule the <c>AssetId</c> constant learned the
    /// hard way: an emitter addition is gated on the feature that needs it.</para>
    /// </summary>
    private static void EmitStateParamBindings(
        StringBuilder sb,
        HsmAssetDto dto,
        IReadOnlyList<BTreeBlackboardPackHelper.PackedField> packedFields,
        string pad2)
    {
        if (packedFields.Count == 0) return;

        var offsetMap = new Dictionary<string, BTreeBlackboardPackHelper.PackedField>(StringComparer.Ordinal);
        foreach (var f in packedFields)
            offsetMap[f.Name] = f;

        // (state StableId, SITE, offset). ⭐⭐⭐ CE-414: the SITE is the hosted blueprint's asset Guid,
        //   or Guid.Empty for the state's own field — the default every unmatched site falls back to.
        var bound = new List<(Guid StableId, Guid SiteId, int Offset)>();

        foreach (var st in dto.States)
        {
            // ⛔ An unbound state is the COMMON case, not an error — it seeds from 0 as before.
            // ⭐ CE-417: the state-wide field is the one its slot bindings carry (the migrator copied v1's single field
            //   onto each). Slice 3 (B-2) replaces this state-wide default with per-binding addresses.
            string? stateField = StateWideField(st);
            if (string.IsNullOrEmpty(stateField)) continue;
            // ⚠ A target naming a variable that is not packed (State-role, or renamed away) is
            //   skipped rather than emitted as a guess — the same "fails closed" rule §3.4 states.
            if (!offsetMap.TryGetValue(stateField!, out var field)) continue;

            // ⭐ Guid.Empty, not the activity blueprint's id: ONE entry then serves the activity
            //   blueprint AND all four C# action slots, which is what keeps this purely additive.
            bound.Add((st.StableId, Guid.Empty, field.ByteOffset));
        }

        // ⭐⭐⭐ CE-413 — A TRANSITION'S OWN ExpressionTargetField, KEYED BY ITS GUARD BLUEPRINT.
        //
        //   🔴 The field has been on TransitionNodeDto since E7b and was INERT for the HSM seed: the
        //      kernel stamps a polled guard with its SOURCE STATE (HsmKernelCore.EvaluateGuard:768),
        //      so before CE-414 a guard could only ever read the source state's binding — through its
        //      OWN Params type, over bytes laid out for the activity's. ⛔ A type-pun, unguarded.
        //   ⭐ The guard's asset id is the discriminator, and it needs no kernel change: it is the
        //      same Guid the guard thunk already passes to HsmOccurrence.KeyFor for its slot.
        //   ⚠ A transition whose guard is a C# METHOD is skipped: it has no asset id, so it would
        //      register under Guid.Empty and silently overwrite the SOURCE STATE's own binding.
        //      Its params come from the source state's field, which is what §28.6's comment in
        //      HsmChannelE2E already documented as the behaviour.
        foreach (var tr in dto.Transitions)
        {
            // ⭐ CE-417: the GUARD binding's own field (one transition-wide field used to serve guard and action).
            var guard = tr.Guard;
            if (guard == null || string.IsNullOrEmpty(guard.ExpressionTargetField)) continue;
            if (guard.BlueprintAssetId == Guid.Empty) continue;
            if (!offsetMap.TryGetValue(guard.ExpressionTargetField!, out var field)) continue;
            bound.Add((tr.SourceStableId, guard.BlueprintAssetId, field.ByteOffset));
        }

        // ⛔ GlobalTransitions are NOT emitted, and that is a property of the model rather than an
        //   omission: a global transition has no SourceStableId — it is evaluated against whatever
        //   leaf is active — so there is no (state, site) pair to key it by. Its guard therefore
        //   reads the ACTIVE state's binding. 📄 DESIGN_Occurrence_Scoped_Storage.md §28.6c.

        if (bound.Count == 0) return;

        sb.AppendLine($"{pad2}// E3b-0 / CE-414: which blackboard variable each HOSTING SITE seeds its");
        sb.AppendLine($"{pad2}//        params from. Guid.Empty is the state-wide default; a non-empty");
        sb.AppendLine($"{pad2}//        site is the hosted blueprint's asset id (CE-413: a guard).");
        sb.AppendLine($"{pad2}//        Resolved to flat state indices at runtime via the blob's own");
        sb.AppendLine($"{pad2}//        MachineMetadata.StateStableIds.");
        sb.AppendLine($"{pad2}global::Fdp.Toolkit.Behavior.HsmParamBindings.Register(blob, new (global::System.Guid, global::System.Guid, int)[]");
        sb.AppendLine($"{pad2}{{");
        foreach (var (stableId, siteId, offset) in bound)
            sb.AppendLine($"{pad2}{Indent}(new global::System.Guid(\"{stableId}\"), new global::System.Guid(\"{siteId}\"), {offset}),");
        sb.AppendLine($"{pad2}}});");
        sb.AppendLine();
    }

    // ⛔ CE-416 (2026-10-01) — the HSM's own EmitParseParamsLocal is DELETED: it was the pre-CE-427 copy of the BTree's (bake
    //   inlined, no BakeDefaults, no State half). Both tiers now call BTreeBridgeEmitCore.EmitRootParamsLocals.

    /// <summary>
    /// ⭐⭐ Packs an HSM asset's managed blackboard into inline param offsets. ⛔ Returns an EMPTY
    /// list — never null — for every case that has no inline params: a non-managed blackboard, no
    /// variables, only <c>State</c>-role variables, or a type no resolver can size. ⭐ One return
    /// shape means the caller has one condition to test, which is the whole point of hoisting this.
    /// </summary>
    /// <summary>⭐ <c>E7b</c>: the same packing, for <c>HsmEmitCore</c>'s expression-target binding.
    /// ⛔ One packer call, so the offset a transition bakes and the offset <c>ParseParams</c> writes
    /// are the same number by construction rather than by agreement.</summary>
    public static IReadOnlyList<BTreeBlackboardPackHelper.PackedField> PackParamsFor(
        HsmAssetDto dto, Func<string, int?>? sizeResolver)
        => PackParams(dto, sizeResolver);

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
    /// ⭐⭐⭐ <c>CE-416</c> (<c>Q76</c> §12.27) — this HSM as a <b>blackboard owner</b>: a <see cref="BehaviorTreeAssetDto"/>
    /// carrying ONLY what the shared root-params / struct emitters read — name, asset id, namespace, blackboard and (⭐ <c>CE-503</c>)
    /// the bound resolver asset; no nodes. ⭐ The namespace is the HSM's own (<c>Hrot.AI.Behaviors.Machines</c>
    /// by default), so <c>{Asset}_Blackboard</c> / <c>_Block</c> land beside the registrar that names them.
    /// </summary>
    public static BehaviorTreeAssetDto BlackboardOwner(HsmAssetDto dto) => new()
    {
        Name            = dto.Name,
        AssetId         = dto.AssetId,
        TargetNamespace = string.IsNullOrEmpty(dto.TargetNamespace) ? "Hrot.AI.Behaviors.Machines" : dto.TargetNamespace,
        Blackboard      = new BlackboardBlockDto
        {
            Managed   = dto.Blackboard?.Managed ?? false,
            TypeName  = dto.Blackboard?.TypeName ?? string.Empty,
            Variables = dto.Blackboard?.Variables is { } vs ? new List<BlackboardVariableDto>(ToPackable(vs)) : new List<BlackboardVariableDto>(),
        },
        // ⭐ CE-503 — the bound resolver asset rides the view, so the shared emission's resolve stage (CE-428/443) serves
        //   the HSM unchanged: bake → the resolver INSTEAD of the default copy, published as ResolveStage + ResolverName.
        Resolver        = dto.Resolver,
    };

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
    /// <summary>
    /// ⭐⭐⭐ <c>E5</c> — <b>the hosting states of <paramref name="dto"/>, each with the slot key its
    /// child's <c>BehaviorTreeState</c> will live under.</b>
    ///
    /// <para>⭐ The exact analogue of <c>BTreeBridgeEmitCore.CollectHostedTreeStateSlots</c>, and it
    /// calls the SAME key function — <c>OccurrenceSlotKey.ComputeTreeStateKey</c>, with the hosting
    /// STATE's <c>StableId</c> as the site. 🔒 One arithmetic, two hosts (ruling 9), and the site is
    /// a stable authoring id rather than an ordinal (<c>D5</c>).</para>
    ///
    /// <para>⛔ <b>A state needs BOTH halves of the pair.</b> A <c>SubtreeAssetId</c> with no
    /// <c>SubtreeName</c> is skipped: the name is how the host resolves the child through
    /// <c>BehaviorRegistry</c> (<c>Q36-B</c> = A), so a Guid alone cannot be hosted — and guessing a
    /// name would be worse than not hosting. ⚠ That is also why every pre-<c>E5</c> asset emits
    /// nothing: <c>SubtreeName</c> did not exist, so no state can carry one.</para>
    ///
    /// <para>📐 Measured <c>2026-09-23</c>: <b>0</b> shipped <c>.hsm.json</c> carries a
    /// <c>SubtreeAssetId</c> at all, so this returns empty for the whole corpus and the generated
    /// output stays byte-identical — acceptance <c>A7</c>.</para>
    /// </summary>
    private static List<(Guid StableId, string ChildName, int SlotKey)> CollectHostedSubtrees(HsmAssetDto dto)
    {
        var result = new List<(Guid, string, int)>();
        if (dto.States == null || dto.States.Count == 0) return result;

        var seen = new HashSet<int>();
        foreach (var st in dto.States)
        {
            if (st == null) continue;
            if (st.SubtreeAssetId == Guid.Empty) continue;
            if (string.IsNullOrWhiteSpace(st.SubtreeName)) continue;

            int key = Fdp.Toolkit.Behavior.Shared.OccurrenceSlotKey.ComputeTreeStateKey(
                dto.AssetId, st.StableId, st.SubtreeAssetId);

            // ⚠ Two states hosting the same child would have DIFFERENT keys (the site differs), so a
            //   collision here means a duplicated StableId — malformed input, not a co-scoped share.
            if (!seen.Add(key)) continue;

            result.Add((st.StableId, st.SubtreeName!.Trim(), key));
        }
        return result;
    }

    private static void EmitStatefulWorkingSlotsArray(
        StringBuilder sb, HsmAssetDto dto, string pad,
        BehaviorTreeAssetDto owner, IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? owned)
    {
        // ⭐⭐⭐ E5 — one slot per HOSTED SUBTREE, so the child gets its OWN BehaviorTreeState.
        // ⛔⛔ THIS AND THE HOSTING CALL SHIP TOGETHER OR NEITHER — HostedSubtree.Tick THROWS on a
        //    slot the manifest never declared (§19.6 ⑤: a silent miss is the failure A1 exists to
        //    kill), so registering the host without this entry turns every hosted state into a hard
        //    failure. 📄 §32.2.2 — the first draft of §32 omitted exactly this.
        var hostedSlots = CollectHostedSubtrees(dto);

        var variables = dto.Blackboard?.Variables;
        // ⚠ The variable list may be empty while a state still hosts — an HSM that hosts a BTree and
        //   declares no State-role variable of its own is entirely legitimate.
        if ((variables == null || variables.Count == 0) && hostedSlots.Count == 0) return;
        variables ??= new List<HsmBlackboardVariableDto>();

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
            if (v.Scope != WorkingStateScope.Behavior) continue;

            string typeId = v.Type?.TypeId ?? string.Empty;
            if (string.IsNullOrEmpty(typeId)) continue;

            int slotKey = BTreeBridgeEmitCore.ComputeStatefulSlotKey(
                dto.AssetId, v.Scope, Guid.Empty, v.Name);
            if (!seenKeys.Add(slotKey)) continue;   // co-scoped duplicates share one slot
            // ⭐ CE-416 (CE-437's HSM half) — a variable in the block's St has ONE home: no side slot.
            if (BTreeBridgeEmitCore.TryGetBlockStateVariable(owner, owned, slotKey, out _)) continue;

            slots.Add((slotKey, typeId, v.Name, (int)v.Role, (int)v.Scope));
        }

        if (slots.Count == 0 && hostedSlots.Count == 0) return;

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

        // ⭐ E5 — the hosted children's tree-state slots, emitted AFTER the authored ones so the
        //   existing corpus's slot ORDER is byte-identical. 📐 Nothing in today's corpus hosts, so
        //   this loop emits nothing for every shipped asset (A7).
        // ⭐⭐ Role=State / Scope=Behavior is what makes HostedSubtree.IsTreeStateSlot's manifest test
        //   work: WorkingStateType == typeof(BehaviorTreeState) is a type an authored WorkingState
        //   can never be, so an external reset clears a hosted CURSOR and never author state.
        //   ⛔ Identical to BTreeBridgeEmitCore's emission — re-spelling it is how the two would drift.
        foreach (var (_, childName, slotKey) in hostedSlots)
        {
            string escaped = childName.Replace("\\", "\\\\").Replace("\"", "\\\"") + " (hosted)";
            sb.AppendLine(
                $"{pad}{Indent}new global::Fdp.Toolkit.Behavior.StatefulSlotInfo({slotKey}, " +
                "global::System.Runtime.InteropServices.Marshal.SizeOf<global::Fbt.BehaviorTreeState>(), " +
                $"unchecked({BTreeBridgeEmitCore.ComputeTypeNameHash("Fbt.BehaviorTreeState")}u ^ " +
                "(uint)global::System.Runtime.InteropServices.Marshal.SizeOf<global::Fbt.BehaviorTreeState>()), " +
                $"typeof(global::Fbt.BehaviorTreeState), \"{escaped}\", " +
                "(byte)global::Fdp.Toolkit.Blueprints.Partitioning.StatefulSlotRole.State, " +
                "(byte)global::Fdp.Toolkit.Blueprints.Partitioning.StatefulSlotScope.Behavior),");
        }

        sb.AppendLine($"{pad}}},");
    }

    /// <summary>
    /// ⭐⭐⭐ <c>E5</c> item 4 — <b>the hosting table, baked as authoring <c>StableId</c>s.</b>
    ///
    /// <para>⭐ Emitted beside <see cref="EmitStateParamBindings"/> and for the same reason: the
    /// emitter knows the asset's own states and NOT the flattener's ordering, so it bakes
    /// <c>StableId</c>s and lets <c>HsmHostedSubtrees.Register</c> recover the flat indices from the
    /// blob's own <c>MachineMetadata.StateStableIds</c>.</para>
    ///
    /// <para>⚠ <b>Emits NOTHING when no state hosts</b>, which is every asset in the corpus ⇒ their
    /// generated source stays byte-identical. ⭐ The same gating rule the <c>AssetId</c> constant and
    /// the <c>E3b-0</c> table learned: an emitter addition is gated on the feature that needs it.</para>
    /// </summary>
    private static void EmitHostedSubtrees(
        StringBuilder sb, HsmAssetDto dto, string pad2, IReadOnlyList<BTreeBlackboardPackHelper.PackedField>? owned)
    {
        var hosted = CollectHostedSubtrees(dto);
        if (hosted.Count == 0) return;

        sb.AppendLine($"{pad2}// E5: which STATES host a child behaviour, and the slot key that child's");
        sb.AppendLine($"{pad2}//     BehaviorTreeState lives under. Resolved to flat state indices at");
        sb.AppendLine($"{pad2}//     runtime via the blob's own MachineMetadata.StateStableIds.");
        sb.AppendLine($"{pad2}// ⛔ BrainTickSystem is the HOST — not a generated [HsmAction]: an HSM action");
        sb.AppendLine($"{pad2}//    is dispatched at most once per event-driven round (CE-334), and a hosted");
        sb.AppendLine($"{pad2}//    BTree needs a frame cursor. 📄 DESIGN §32.2.1 / §32.3.");
        sb.AppendLine($"{pad2}global::Fdp.Toolkit.Behavior.HsmHostedSubtrees.Register(blob, new (global::System.Guid, string, int)[]");
        sb.AppendLine($"{pad2}{{");
        foreach (var (stableId, childName, slotKey) in hosted)
        {
            string escaped = childName.Replace("\\", "\\\\").Replace("\"", "\\\"");
            sb.AppendLine($"{pad2}{Indent}(new global::System.Guid(\"{stableId}\"), \"{escaped}\", {slotKey}),");
        }
        // ⭐⭐ CE-439 — each hosting state's SEED binding, built by the ONE site-binding builder the BTree host uses
        //   (BTreeBridgeEmitCore.EmitSiteBindings), keyed by the state's StableId. ⛔ null ⇒ the call is unchanged.
        var sites = new List<(Guid SiteId, string? ParamsVariable)>();
        foreach (var st in dto.States ?? new List<StateNodeDto>())
            if (st != null) foreach (var (stableId, _, _) in hosted)
                if (st.StableId == stableId) { sites.Add((stableId, st.SubtreeParamsVariable)); break; }
        string? bindings = BTreeBridgeEmitCore.EmitSiteBindings(sites, owned, dto.Name);
        sb.AppendLine(bindings is null ? $"{pad2}}});" : $"{pad2}}}, {bindings});");
        sb.AppendLine();

        // ⭐⭐ And BIND each child's interpreter to its slot, resolved HERE because `beh` is in hand.
        //   ⛔ A tick-time lookup is not available to every host: a generated thunk is static and
        //      there is no ambient BehaviorRegistry (CE-333/CE-335's root cause). One table, one
        //      answer per slot — ruling 9.
        foreach (var (_, childName, slotKey) in hosted)
        {
            string escaped = childName.Replace("\\", "\\\\").Replace("\"", "\\\"");
            sb.AppendLine($"{pad2}global::Fdp.Toolkit.Behavior.HostedChildren.Register(beh, {slotKey}, \"{escaped}\");");
        }
        sb.AppendLine();
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

    // ⭐ CE-2039 (S8g G4) — the HSM's CLASS uses the strip shape its own _Block/_Blackboard structs already used (via
    //   BlackboardOwner → BTreeEmitCore): it replaced with '_' instead, so "Guard-Patrol" made class Guard_Patrol but
    //   GuardPatrol_Block, and a second HSM "GuardPatrol" collided on that struct. bare: a keyword name gets '_'.
    private static string SanitizeIdentifier(string name)
        => global::Fdp.Toolkit.Behavior.Shared.IdentifierSanitizer.StripInvalid(name, "HsmAsset", bare: true);

    /// <summary>⭐ CE-417 — the field a state's slot bindings share (Activity first, then OnEntry, OnExit, Timer).</summary>
    public static string? StateWideField(StateNodeDto st)
        => FirstField(st.Activity) ?? FirstField(st.OnEntry) ?? FirstField(st.OnExit) ?? FirstField(st.Timer);

    private static string? FirstField(BehaviorActionBindingDto? b)
        => string.IsNullOrEmpty(b?.ExpressionTargetField) ? null : b!.ExpressionTargetField;

    private static string SharedAiThunkName(int i) => "__SharedAiBinding" + i;
}
