using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Fbt.Runtime;
using Fhsm.Kernel.Data;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;

namespace Fdp.Toolkit.Behavior
{
    /// <summary>
    /// Delegate that parses a JSON parameter string and writes the result directly into
    /// a behaviour blackboard's inline memory — zero allocation, no boxing.
    /// </summary>
    /// <param name="json">Serialised parameter payload (cold path only).</param>
    /// <param name="memory">Pointer to the first byte of <c>RootParamsAccess</c> (the ROOT PARAMS SLOT; this was <c>BrainBlackboard.BehaviorParameters</c> before <c>P3-C</c>).</param>
    /// <param name="capacity">
    /// ⭐⭐⭐ <b><c>CE-331</c> — HOW MANY BYTES <paramref name="memory"/> ACTUALLY HOLDS.</b>
    /// 🔒 <b>A parser MUST NOT write past it.</b>
    /// <para>🔴 Until <c>CE-331</c> this delegate took a bare <c>byte*</c> with no length, so no
    /// implementation — generated or hand-written — could bounds-check even in principle. <c>CE-328</c>
    /// stopped an UNDECLARED width reaching here; it could not stop a parse overrunning a width that
    /// IS declared. ⇒ this is the other half, and it is the half that makes the guarantee checkable.</para>
    /// <para>⚠ It is the extent of the WRITABLE REGION, not the behaviour's declared params size:
    /// the ingress parses into a shadow buffer first, and hands that buffer's length.</para>
    /// </param>
    /// <summary>
    /// Cold-path resolver: parses the authored JSON parameter payload into the behavior's
    /// runtime params region, and may post-process it using world context — geographic transform,
    /// entity map, etc. — reached via <paramref name="world"/> singletons (rather than a
    /// registration-time closure). Runs once at behavior activation (<see cref="Systems.BehaviorIngressSystem"/>).
    /// </summary>
    /// <param name="host">
    /// ⭐⭐ <c>G1</c>/<c>E7</c> — the HOSTING occurrence's variables, or <c>null</c> for a root
    /// behaviour. 📄 <c>DESIGN_Parameter_Model.md</c> §3.4.
    /// ⛔ <b>Always <c>null</c> today</b>: <see cref="IHostVariableAccess"/> is declared and
    /// unimplemented on purpose. ⭐ The parameter is here NOW because adding one is a breaking change
    /// to every resolver, and <c>E7a</c> should populate it without a second such change.
    /// </param>
    public unsafe delegate void ParseParamsDelegate(
        string json, byte* memory, int capacity, EntityRepository world, Entity self, IHostVariableAccess? host);

    /// <summary>Variable metadata for one packed slot in the root params region.</summary>
    public sealed record ManagedBlackboardVariable(string Name, Type Type, int ByteOffset);

    /// <summary>
    /// S2-1: per-node stateful slot manifest entry. Describes one working-state partition slot
    /// that must be pre-provisioned before the first BTree tick of the behavior.
    /// SlotKey is FNV-1a-32(assetGuid, nodeVisualId) &amp; 0x7FFFFFFF (positive int).
    /// <para>
    /// <paramref name="WorkingStateType"/> and <paramref name="NodeLabel"/> are optional
    /// (default null) so all existing 3-arg constructions compiled in BATCH-06/08 tests
    /// remain valid without change.  When non-null, <paramref name="WorkingStateType"/>
    /// enables typed live-value rendering in <c>BlueprintBlackboard*Renderer</c> and
    /// <paramref name="NodeLabel"/> provides a friendly row label in the inspector.
    /// </para>
    /// </summary>
    /// <param name="Role">
    /// (S3-7) Authoring role of the bound variable, as the integer value of the editor's
    /// <c>BlackboardVariableRole</c> enum: 0 = Input, 1 = State. A stateful working slot is
    /// always State; carried for the live inspector. Default 0 for legacy 3-/5-arg constructions.
    /// (Stored as <see cref="byte"/> because this low-level runtime assembly must not reference
    /// the editor's persistence enums.)
    /// </param>
    /// <param name="Scope">
    /// (S3-7) Working-state scope, as the integer value of the editor's <c>WorkingStateScope</c>
    /// enum: 0 = Node, 1 = Behavior, 2 = Entity. Lets the inspector group/label slots by scope.
    /// Default 0 (Node) for legacy constructions.
    /// </param>
    public sealed record StatefulSlotInfo(
        int SlotKey,
        int PayloadSize,
        uint StructureHash,
        Type? WorkingStateType = null,
        string? NodeLabel = null,
        byte Role = 0,
        byte Scope = 0);

    /// <summary>
    /// ⭐⭐⭐ <b>How much store a behaviour's HOSTED occurrences will need — the half <c>E-cap</c>
    /// deliberately did not deliver.</b> <c>O7b-3</c> — 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §27.7.
    ///
    /// <para>⛔⛔ <b>Why this is a DEMAND and not a manifest.</b> A hosted occurrence attaches
    /// <b>lazily</b>, on first dispatch (§24.8), and its KEY needs the region slot the kernel picks at
    /// runtime — which is not knowable at registration. ⭐ But its SIZE is: the tier must be chosen
    /// before the first tick, and choosing it needs only <i>how many</i> and <i>how big</i>. ⇒ this
    /// carries exactly that, and nothing it cannot honestly know.</para>
    ///
    /// <para>⚠ <b>It is an UPPER BOUND, on purpose.</b> Counting every hosted <c>(state, blueprint)</c>
    /// pair over-counts a machine whose regions never all activate at once. ⛔ The opposite error —
    /// under-sizing — is a throw from inside a kernel dispatch, so the bound leans the safe way, and
    /// over-sizing costs one tier step.</para>
    ///
    /// <para>⭐ <c>null</c> means <i>"nobody computed one"</i> — a behaviour registered by hand, or one
    /// whose blueprints live in another assembly than the scan that found it. ⚠ That is the
    /// pre-<c>O7b-3</c> behaviour exactly: the smallest tier, and the loud throw if it does not fit.
    /// ⛔ It is NOT <i>"this behaviour hosts nothing"</i> — that is <c>SlotCount: 0</c>.</para>
    /// </summary>
    /// <param name="PayloadBytes">
    /// Sum of the hosted working-state sizes, each ALREADY ROUNDED UP to the store's alignment.
    /// ⛔ Slot-entry overhead is NOT included — <see cref="Systems.BehaviorIngressSystem"/> adds
    /// <c>SlotCount × SlotEntrySize</c> itself, because it owns the identical arithmetic for the
    /// manifest and the two must not drift. ⭐ Use <see cref="Of"/> rather than summing by hand.
    /// </param>
    /// <param name="SlotCount">How many distinct occurrences the host can have live at once.</param>
    public sealed record HostedOccurrenceDemand(int PayloadBytes, int SlotCount)
    {
        /// <summary>
        /// ⭐ The only correct way to build one: aligns each occurrence's payload the way the store
        /// does, so a caller cannot under-count by summing raw <c>sizeof</c>s. ⛔ Returns a
        /// <c>SlotCount: 0</c> demand for an empty set — which means <i>"hosts nothing"</i>, and is
        /// deliberately different from a <c>null</c> demand.
        /// </summary>
        public static HostedOccurrenceDemand Of(IEnumerable<int> payloadSizes)
        {
            if (payloadSizes is null) throw new ArgumentNullException(nameof(payloadSizes));

            int bytes = 0, count = 0;
            foreach (int size in payloadSizes)
            {
                const int alignment = Fdp.Toolkit.Blueprints.Partitioning
                                         .BlueprintBlackboardPartitions.Alignment;
                bytes += (size + alignment - 1) & ~(alignment - 1);
                count++;
            }
            return new HostedOccurrenceDemand(bytes, count);
        }
    }

    /// <summary>
    /// Immutable definition of a single registered behavior (i.e., a named AI behaviour).
    /// Created once at startup; read-only thereafter.
    /// </summary>
    public sealed class BehaviorDefinition
    {
        /// <summary>Human-readable name, e.g. "FleeToSafety" or "Patrol".</summary>
        public required string Name { get; init; }

        /// <summary>
        /// Brain tier for entities assigned this behavior.
        /// Use <see cref="BehaviorConstants.BrainTierBTree"/> or
        /// <see cref="BehaviorConstants.BrainTierHsm"/>.
        /// </summary>
        public byte BrainTier { get; init; }

        /// <summary>
        /// Pre-built FastBTree interpreter for this behavior.
        /// <c>null</c> when <see cref="BrainTier"/> is not <see cref="BehaviorConstants.BrainTierBTree"/>.
        /// </summary>
        public Interpreter<byte, BTreeContext>? BTreeInterpreter { get; init; }

        /// <summary>
        /// FastHSM definition blob for this behavior.
        /// <c>null</c> when <see cref="BrainTier"/> is not <see cref="BehaviorConstants.BrainTierHsm"/>.
        /// </summary>
        public HsmDefinitionBlob? HsmDefinition { get; init; }

        /// <summary>
        /// Optional FastHSM symbolication metadata. Populated by <c>AiBehaviorFactory</c>
        /// for HSM-backed behaviors so diagnostic renderers / JSON translators can
        /// resolve raw state, event, and action IDs to human-readable names. May be
        /// <c>null</c> for legacy behaviors compiled without sidecar metadata.
        /// </summary>
        public MachineMetadata? HsmMetadata { get; init; }

        /// <summary>
        /// Cold-path delegate that parses the behavior's JSON parameter payload into
        /// <c>RootParamsAccess</c> (the ROOT PARAMS SLOT; this was <c>BrainBlackboard.BehaviorParameters</c> before <c>P3-C</c>).  May be <c>null</c> if the behavior
        /// carries no configurable parameters.
        /// <para>
        /// Settable (not <c>init</c>-only) so the registry can bind a <b>named resolver</b> to a
        /// behavior whose topology was self-registered without one — e.g. a generated
        /// <c>[BlueprintRegistrar]</c> registers the interpreter/slots, and a curated
        /// <see cref="BehaviorRegistry.RegisterResolver"/> supplies the geo/entity-aware resolver
        /// by name.  Bound exactly once at startup; treated as read-only after the world starts.
        /// </para>
        /// </summary>
        public ParseParamsDelegate? ParseParams { get; set; }

        /// <summary>
        /// ⭐⭐⭐ <b>THE PUBLIC CONTRACT.</b> The <b>authored JSON DTO</b> — the shape a scenario, the
        /// editor's mission panel, or an agent over the debug API writes when it assigns this behavior.
        /// This is what <c>GET /behaviors</c> publishes as <c>paramSchema</c>.
        ///
        /// <para>
        /// ⛔⛔ <b>This is NEVER a blackboard layout type.</b> Blackboard layout is engine-internal and
        /// belongs in <see cref="BlackboardLayoutType"/>; only the behavior implementation and the
        /// inspector may see it. 📄 <c>Behavior_Parameter_Resolver_Detailed_Design.md</c> §3.2 — the
        /// authored DTO is <i>"editor fields + JSON schema"</i>; the usable params are <i>"hot-path
        /// input"</i> and are <b>not authored</b>. <see cref="ParseParams"/> is the translator between
        /// the two.
        /// </para>
        ///
        /// <para>
        /// ⭐ <b>The two often coincide, and that is the design's default case</b>, not an accident:
        /// §3.2 <i>"one shape by default — the authored DTO is an auto-generated mirror; two shapes only
        /// on divergence"</i>. A JSON-authored (generated) asset's emitted <c>*_Blackboard</c> struct
        /// serves as both. The curated behaviors that DIVERGE — a geo point vs a Cartesian pair, a
        /// network id vs a resolved <c>Entity</c> — are exactly the ones §3.2 names, and there the two
        /// members hold different types.
        /// </para>
        ///
        /// <para>
        /// Populated by <c>BehaviorSchemaDiscovery</c> from <c>[BehaviorContract]</c> for curated
        /// behaviors, and by the JSON generators for authored assets. Settable (not <c>init</c>-only)
        /// for the same reason as <see cref="ParseParams"/> — a curated registrar can bind it by name
        /// through <see cref="BehaviorRegistry.RegisterResolver"/> after a generated registrar has
        /// registered the topology.
        /// </para>
        /// </summary>
        public Type? JsonParamsDtoType { get; set; }

        /// <summary>
        /// 🔒 <b>ENGINE-INTERNAL.</b> The blittable struct laid out at the start of
        /// <c>RootParamsAccess</c> (the ROOT PARAMS SLOT; this was <c>BrainBlackboard.BehaviorParameters</c> before <c>P3-C</c>). Consumers project it <b>over raw
        /// blackboard bytes</b> (<c>Marshal.PtrToStructure</c> / <c>Unsafe.As</c>), so its field order
        /// and packing are load-bearing and the type must be unmanaged.
        ///
        /// <para>
        /// ⛔⛔ <b>Never publish this outside the engine.</b> It is not a wire contract: it may carry
        /// runtime outputs (<c>FireAtTargetParams.RoundsFired</c>) and resolved handles
        /// (<c>TargetPacked</c>) that no caller may set, and it may omit authored keys the resolver
        /// accepts (<c>TargetLat</c>/<c>TargetLon</c>). Publishing it was <c>CE-224</c>'s defect;
        /// <c>CE-235</c> split the two members so the mistake cannot be made silently again.
        /// </para>
        ///
        /// <para>Readers: <c>BrainDiagnosticsTranslator</c> (⚠ a historical NAME — it reads the root
        /// params slot, §30.28), <c>RootParamsProjection</c>/<c>RootParamsViewProvider</c> (StructEdit,
        /// <c>CE-312</c>) and the
        /// ReplayBrowser predicate compiler + its two field drawers.</para>
        /// </summary>
        public Type? BlackboardLayoutType { get; set; }

        /// <summary>
        /// Optional DTO type stored in a generic heavy blackboard component (e.g., <c>Blackboard1024</c>)
        /// for this behavior.  When non-null, enables typed rendering in <c>Blackboard1024Renderer</c>
        /// for unmanaged DTOs projected via <c>Unsafe.As</c> over the component's raw byte array.
        /// ⛔ <c>CE-327</c>: <c>[SharedAiHeavyAction]</c> is DELETED (§30.29), so the managed-component
        /// case it described no longer exists. Leave this null
        /// (the managed class reference is fetched directly and does not need Inspector projection).
        /// </summary>
        public Type? HeavyDtoType { get; init; }

        /// <summary>
        /// For managed-blackboard BTree assets: ordered list of packed variables,
        /// each at its bin-packed ByteOffset. Used by RootParamsProjection (CE-312) to
        /// project each DTO at its own offset instead of only reading offset 0.
        /// Null for non-managed or HSM behaviors.
        /// </summary>
        public IReadOnlyList<ManagedBlackboardVariable>? ManagedBlackboardVariables { get; init; }

        /// <summary>
        /// S2-1: per-node stateful working-state slot manifest. One entry per distinct stateful
        /// node instance in the asset (deduped by SlotKey). Consumed by
        /// <see cref="Fdp.Toolkit.Behavior.Systems.BehaviorIngressSystem"/> (S2-2) to
        /// pre-provision partition slots before the first BTree tick.
        /// Null for non-managed assets, HSM behaviors, or assets with no stateful nodes.
        /// </summary>
        public IReadOnlyList<StatefulSlotInfo>? StatefulWorkingSlots { get; init; }
    }

    /// <summary>
    /// Startup-time registry that maps stable assigned <c>int</c> IDs to their
    /// <see cref="BehaviorDefinition"/>s.
    ///
    /// Thread-safe for reads after the world is started (registrations happen before
    /// the first frame).  All mutations must complete before the simulation loop begins.
    ///
    /// <b>DEBT-006 fix:</b> Key is now a stable assigned <c>int</c> constant (see
    /// <see cref="BehaviorIds"/>), NOT <c>name.GetHashCode()</c>.  Using
    /// <c>string.GetHashCode()</c> is forbidden: it is process-randomised in .NET,
    /// making behavior IDs non-reproducible across runs and processes.
    /// </summary>
    public sealed class BehaviorRegistry
    {
        private readonly Dictionary<int, BehaviorDefinition> _definitions = new();
        private readonly Dictionary<string, int> _nameToId = new(StringComparer.Ordinal);
        // Named resolver overlays keyed by behavior name. Lets a curated registrar supply the
        // geo/entity-aware ParseParams resolver (and the params DTO type for diagnostics/inspector)
        // for a behavior whose topology was self-registered (by a generated [BlueprintRegistrar])
        // without them. Binding is order-independent: whichever of {topology, overlay} arrives second
        // reconciles against the first.
        private readonly Dictionary<string, (ParseParamsDelegate Resolver, Type? BlackboardLayoutType)> _resolversByName
            = new(StringComparer.Ordinal);

        // CE-235: authored JSON contracts keyed by behavior name, supplied by BehaviorSchemaDiscovery
        // from [BehaviorContract]. Same order-independent reconciliation as _resolversByName, and for
        // the same reason: discovery scans the Hrot.Core assembly at editor/CGF setup time, which may
        // run before or after the [BlueprintRegistrar] scan that registers the topologies.
        private readonly Dictionary<string, Type> _jsonParamsDtoByName = new(StringComparer.Ordinal);

        // ⭐⭐⭐ O7b-3: how much occurrence store each behaviour's HOSTED blueprints will need, so
        // BehaviorIngressSystem can size the tier before the first dispatch instead of defaulting to
        // the smallest. 📄 DESIGN_Occurrence_Scoped_Storage.md §27.7.
        //
        // ⛔⛔ AN OVERLAY, NOT A FIELD ON BehaviorDefinition, and for the same reason the two
        //   dictionaries above are overlays: the definition's TOPOLOGY is registered by a generated
        //   [BlueprintRegistrar] that must NOT know about blueprints (user ruling, 2026-09-21), while
        //   the demand can only be computed once the BLUEPRINT registry is populated. ⇒ whichever
        //   arrives second reconciles against the first, exactly as _resolversByName does.
        private readonly Dictionary<string, HostedOccurrenceDemand> _hostedDemandByName
            = new(StringComparer.Ordinal);

        /// <summary>
        /// Register a behavior <b>by name</b> — the preferred, name-as-identity entry point.
        /// The integer id is derived from the name via <see cref="BehaviorHash.FromName"/>, so
        /// callers reference a behavior by its name exactly once and never hand-pick an id.
        /// </summary>
        public void Register(string name, BehaviorDefinition definition)
            => Register(BehaviorHash.FromName(name), name, definition);

        /// <summary>
        /// Register a behavior under an explicit integer <paramref name="id"/>.
        /// The id becomes the value stored in <see cref="Components.BehaviorState.ActiveBehaviorHash"/>.
        /// Prefer the <see cref="Register(string, BehaviorDefinition)"/> overload; this one exists for
        /// callers that already hold the derived id (e.g. generated registrars).
        /// </summary>
        public void Register(int id, string name, BehaviorDefinition definition)
        {
            // Startup-time firewall: ensure the params DTO can actually be STORED.
            // ⭐ CE-235: this guard is about the params REGION, so it reads the layout type, never the
            //   authored JSON contract — a JSON DTO is a heap class whose Marshal size means nothing here.
            //
            // ⭐⭐⭐ CE-307 (2026-09-22) — THIS WAS A 100-BYTE CAP, AND THE REASON IT GAVE WAS ALREADY
            //   FALSE. It read "would corrupt the SoftAdvice and Interrupt registers in BrainBlackboard":
            //   O2 moved those registers to BrainInterrupts, and P3-C moved params out of the blackboard
            //   entirely. ⇒ there are no neighbours to corrupt — params occupy their own occurrence slot,
            //   sized RootParamsBytes(def) and promoted up the tier ladder.
            // ⛔ So this is no longer a CAP. It is a CAPACITY check, and the honest number is the largest
            //   tier's whole payload: above it, no tier can hold the region at all.
            // ⚠ Below it is NOT a guarantee of fit — the region shares its tier with the behaviour's
            //   stateful slots and hosted occurrences, and ingress throws (naming CE-302) when the store
            //   has no room. This catches the case that is impossible to satisfy, at registration.
            if (definition.BlackboardLayoutType != null)
            {
                int dtoSize = System.Runtime.InteropServices.Marshal.SizeOf(definition.BlackboardLayoutType);
                if (dtoSize > BehaviorConstants.MaxRootParamsByteSize)
                    throw new InvalidOperationException(
                        $"Behavior '{name}' params DTO '{definition.BlackboardLayoutType.Name}' requires {dtoSize} bytes, " +
                        $"which exceeds {BehaviorConstants.MaxRootParamsByteSize} — the payload of the largest occurrence " +
                        "storage tier. No tier can hold a root params region this wide, so the behaviour could never be " +
                        "assigned. Split the parameters, or add a larger tier to BlueprintTierLadder.");
            }

            // ⭐⭐⭐ CE-328 (2026-09-23) — AN UNDER-DECLARED BEHAVIOUR IS REFUSED HERE, LOUDLY, RATHER
            //   THAN SILENTLY RESERVING AN ARBITRARY 100 BYTES.
            //
            // 🔒 User: "why such concrete fallback? why fallback at all and not hard error if no
            //   reasonable fallback exists?" — and the answer is that there is no reasonable fallback.
            //
            // 📐 A behaviour that declares a ParseParams but NEITHER a manifest NOR a layout type has
            //   params and never said how wide they are. RootParamsAccess.RootParamsBytes used to hand
            //   back BehaviorConstants.MaxBehaviorParamByteSize (100) for exactly this shape, to
            //   reproduce the pre-P3-C world where the whole 100-byte BrainBlackboard region existed
            //   whether anyone declared it or not.
            //
            // ⛔⛔ TWO THINGS MADE THAT INDEFENSIBLE:
            //   ① The 100 stopped measuring anything — `P4` deleted BrainBlackboard, so the number
            //      reproduced the geometry of storage that no longer exists.
            //   ② THE SAFETY DIRECTION INVERTED. ParseParams is (string, byte* mem, …) — a raw pointer
            //      with NO LENGTH — so the parse cannot bounds-check. The 100 used to be the GUARD
            //      against overrunning the region; as a fallback WIDTH it became the thing that gets
            //      overrun, silently, by any parse that writes more.
            //
            // ⭐ Refusing at registration is loud, early, and a one-line fix for the author: declare a
            //   BlackboardLayoutType or a manifest. ⚠ Blast radius measured: production behaviours come
            //   from the generators (BTreeBridgeEmitCore, HsmBridgeEmitCore, CSharpEmitter), which
            //   always emit one of the two ⇒ only hand-registered and TEST behaviours reach this, and
            //   making them state a width makes them better tests.
            // ⚠ NOT fixed here, and named in CE-328: ParseParams still takes no capacity, so a parse
            //   can overrun a width that IS declared. That is a signature change across every generator.
            if (definition.ParseParams != null
                && definition.BlackboardLayoutType == null
                && (definition.ManagedBlackboardVariables == null
                    || definition.ManagedBlackboardVariables.Count == 0))
            {
                throw new InvalidOperationException(
                    $"Behavior '{name}' declares a ParseParams but neither a BlackboardLayoutType nor a "
                    + "ManagedBlackboardVariables manifest, so nothing says how wide its params region "
                    + "is. Its params live in an occurrence slot that must be sized at attach, and "
                    + "ParseParams writes through a pointer with no length — so an undeclared width "
                    + "cannot be bounds-checked. Declare BlackboardLayoutType (the params struct) or a "
                    + "manifest. Before CE-328 this silently reserved "
                    + $"{BehaviorConstants.MaxBehaviorParamByteSize} bytes, the width of a component "
                    + "that no longer exists.");
            }

            // Duplicate-name hard error (Phase 1e, unblocked by Phase 2c factory retirement).
            // Each behavior now self-registers exactly once under a unique name via its own
            // [BlueprintRegistrar]; the curated↔generated double-registration that the interim
            // anti-shadow warn absorbed no longer occurs. A second registration of the same name is
            // therefore a genuine authoring/collision error and must fail loudly rather than silently
            // shadow one definition with another. (Re-registering the exact same definition instance is
            // tolerated so an idempotent re-scan into the same registry is a no-op.)
            if (_nameToId.TryGetValue(name, out var existingId))
            {
                if (_definitions.TryGetValue(existingId, out var existingDef)
                    && ReferenceEquals(existingDef, definition))
                    return;

                throw new InvalidOperationException(
                    $"Behavior name '{name}' is already registered (id {existingId}). Each behavior must "
                    + "self-register exactly once under a unique name. A second registration indicates a "
                    + "name collision between two [BlueprintRegistrar]s or a stale duplicate registrar.");
            }

            // ⭐⭐ G4 — the OTHER half of the same guard, and the one that was missing.
            //
            // The name guard above cannot see this: `id` is FNV-1a-32 of the name
            // (BehaviorHash.FromName), so two DISTINCT names can hash to one id. `_nameToId` would
            // then hold both names -> the same id while `_definitions[id]` holds only the second
            // definition -- ⛔ so the FIRST behavior silently resolves to the SECOND's topology.
            // No throw, no log, one behaviour quietly replaced by another.
            //
            // ⭐ This is `W1`'s sibling on the other registry, and the shape is transplanted from
            //   BlueprintRegistry.RegisterDirect rather than invented (the design says to copy it):
            //   name the incoming, name the resident, say what to do about it.
            // ⚠ An explicit-id caller (a generated registrar) can reach this without any hashing at
            //   all, which is the other way in.
            if (_definitions.TryGetValue(id, out var idHolder))
                throw new InvalidOperationException(
                    $"Behavior id 0x{id:X8} collision: '{name}' would replace '{idHolder.Name}'. "
                    + "The id is FNV-1a-32 of the behavior name, so two distinct names hashed to one "
                    + "id -- rename one of them.");

            _definitions[id] = definition;
            _nameToId[name] = id;

            // Bind a previously-registered named resolver overlay to this topology. Order-independent
            // with RegisterResolver: whichever arrives second applies the overlay.
            if (_resolversByName.TryGetValue(name, out var overlay))
                ApplyResolverOverlay(definition, overlay);

            // CE-235: same, for an authored JSON contract discovered before the topology registered.
            // ⭐ The definition WINS if it already carries one — a generated asset emits its own
            //   JsonParamsDtoType and that is the asset's own authored shape, not something discovery
            //   should overwrite.
            if (definition.JsonParamsDtoType == null
                && _jsonParamsDtoByName.TryGetValue(name, out var jsonDto))
                definition.JsonParamsDtoType = jsonDto;
        }

        /// <summary>
        /// <c>CE-235</c> — binds a behavior's <b>authored JSON contract</b> by name, the shape a
        /// scenario or an agent writes when assigning it. Called by <c>BehaviorSchemaDiscovery</c> for
        /// every <c>[BehaviorContract]</c>-tagged DTO.
        ///
        /// <para>
        /// Order-independent, exactly like <see cref="RegisterResolver"/>: applied immediately when the
        /// topology is already registered, stored and applied on registration otherwise.
        /// </para>
        /// <para>
        /// ⛔ A definition that already carries a <see cref="BehaviorDefinition.JsonParamsDtoType"/>
        /// keeps it — a JSON-authored asset emits its own and outranks a curated overlay for the same
        /// name, because there the emitted struct <i>is</i> the authored shape.
        /// </para>
        /// </summary>
        public void RegisterJsonParamsDtoType(string name, Type jsonParamsDtoType)
        {
            if (name              == null) throw new ArgumentNullException(nameof(name));
            if (jsonParamsDtoType == null) throw new ArgumentNullException(nameof(jsonParamsDtoType));

            _jsonParamsDtoByName[name] = jsonParamsDtoType;

            if (_nameToId.TryGetValue(name, out var id)
                && _definitions.TryGetValue(id, out var def)
                && def.JsonParamsDtoType == null)
            {
                def.JsonParamsDtoType = jsonParamsDtoType;
            }
        }

        /// <summary>
        /// ⭐⭐⭐ <c>O7b-3</c> — records how much occurrence store a behaviour's HOSTED blueprints will
        /// need, so <see cref="Systems.BehaviorIngressSystem"/> can size the tier before the first
        /// dispatch. 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §27.7.
        ///
        /// <para>⭐ Order-independent, like the other two overlays: the demand can only be computed
        /// once the BLUEPRINT registry is populated, which may be before or after the behaviour's
        /// topology registers. ⛔ There is nothing to reconcile INTO the definition — the demand is read
        /// through <see cref="TryGetHostedOccurrenceDemand"/>, so a late arrival is simply available
        /// from then on.</para>
        ///
        /// <para>⚠ <b>Absent is not zero.</b> No entry means <i>"nobody computed one"</i> and the
        /// smallest tier is used — the pre-<c>O7b-3</c> behaviour. A behaviour that genuinely hosts
        /// nothing is recorded with <c>SlotCount: 0</c>.</para>
        /// </summary>
        public void RegisterHostedOccurrenceDemand(string name, HostedOccurrenceDemand demand)
        {
            if (name   == null) throw new ArgumentNullException(nameof(name));
            if (demand == null) throw new ArgumentNullException(nameof(demand));

            _hostedDemandByName[name] = demand;
        }

        /// <summary>
        /// The hosted-occurrence demand recorded for <paramref name="name"/>, if any.
        /// ⚠ <see langword="false"/> means <i>"nobody computed one"</i>, never <i>"hosts nothing"</i>.
        /// </summary>
        public bool TryGetHostedOccurrenceDemand(string name, out HostedOccurrenceDemand? demand)
        {
            if (name == null) { demand = null; return false; }
            return _hostedDemandByName.TryGetValue(name, out demand);
        }

        /// <summary>
        /// Registers a named resolver overlay for a behavior, keyed by its <paramref name="name"/>.
        /// Used by curated <c>[BlueprintRegistrar]</c> classes to supply the geo/entity-aware parameter
        /// resolver (and, optionally, the engine-internal blackboard layout type for
        /// diagnostics/inspector rendering) for behaviors whose topology (interpreter, slots) is
        /// self-registered by a generated registrar that cannot express the resolver.
        /// <para>
        /// ⛔ <c>CE-235</c>: the optional type is <see cref="BehaviorDefinition.BlackboardLayoutType"/>,
        /// <b>never</b> <see cref="BehaviorDefinition.JsonParamsDtoType"/>. The authored JSON contract
        /// comes from <c>[BehaviorContract]</c> via <c>BehaviorSchemaDiscovery</c>, not from here — a
        /// resolver overlay describes how bytes are laid out, not what a caller may write.
        /// </para>
        /// <para>
        /// Binding is order-independent: if the behavior's <see cref="BehaviorDefinition"/> is already
        /// registered, the overlay is applied immediately; otherwise it is stored and applied when the
        /// topology registers.
        /// </para>
        /// <para>
        /// ⭐ <b>The overlay WINS over anything the topology registered</b> — a hand-authored resolver
        /// outranks a generated one (user ruling, 2026-08-23). See
        /// <see cref="ApplyResolverOverlay"/> for why the previous "never overwrite" rule silently
        /// broke <c>PlatoonHillAttack</c>.
        /// </para>
        /// </summary>
        public void RegisterResolver(string name, ParseParamsDelegate resolver, Type? blackboardLayoutType = null)
        {
            if (name     == null) throw new ArgumentNullException(nameof(name));
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));

            // ⭐⭐⭐ R-149 — TWO EXPLICIT BINDINGS FOR ONE PARAMS REGION MUST THROW, NEVER RACE.
            //
            // 🔒 The ruling is R-132's own sentence applied to its successor: "where a curated and a
            //    generated artefact can both fill a slot, curated wins BY DECLARATION, not by arriving
            //    first." ⇒ two CURATED bindings have no such tie-break, and the silent
            //    last-writer-wins this line used to be would pick one by source order — the exact
            //    "not a precedence rule, a race" shape R-132 names.
            //
            // ⛔⛔ THE SCOPE IS ONE REGISTRY INSTANCE, and that is load-bearing, not caution.
            //    📐 Measured: every scan builds a FRESH staging registry
            //    (AiHotReloadCoordinator.cs:315, QuickReloadService.cs:142) and the live registry is
            //    written by MergeFrom, which is a separate overwrite path. ⇒ a duplicate seen HERE
            //    can only be two registrations in ONE scan — a genuine authoring error — while
            //    re-registration across a hot reload never reaches this check. A throw without that
            //    distinction would have broken reload.
            //
            // ⚠ Its sibling HostedParamResolvers.Register deliberately does the OPPOSITE and
            //    overwrites: it is keyed by ASSET id and re-registered by every rescan, so the two
            //    registries need opposite duplicate policies for opposite reasons.
            if (_resolversByName.ContainsKey(name))
                throw new InvalidOperationException(
                    $"Two resolvers are registered for behaviour '{name}' in one scan. A parameters "
                    + "region names exactly one resolver (R-149), so this is an authoring error: "
                    + "picking one by registration order would be a race, not a precedence rule.");

            var overlay = (resolver, blackboardLayoutType);
            _resolversByName[name] = overlay;

            if (_nameToId.TryGetValue(name, out var id)
                && _definitions.TryGetValue(id, out var def))
            {
                ApplyResolverOverlay(def, overlay);
            }
        }

        /// <summary>
        /// Applies a named resolver overlay to a definition. ⭐ <b>The overlay WINS</b> — a
        /// hand-authored resolver outranks whatever the topology registered.
        ///
        /// <para>
        /// 📌 <b>User ruling (2026-08-23):</b> <i>"if curated (hand-authored) exists, then no other is
        /// needed — having automatically generated is undesired in such a case."</i>
        /// <see cref="RegisterResolver"/> is reached ONLY from the curated registrar
        /// (<c>CgfCuratedBehaviorRegistrar</c>); generated registrars never call it. So the presence
        /// of an overlay is itself the signal that a human wrote a resolver for this behavior.
        /// </para>
        ///
        /// <para>
        /// 🔴 <b>This used to read <c>if (def.ParseParams == null)</c></b>, i.e. the generated
        /// <c>ParseParams</c> won. That silently discarded the curated resolver, and only the curated
        /// one understands the geo-authored parameter shape — <c>PlatoonHillAttack</c>'s
        /// <c>firingLineStart</c>/<c>baselineStart</c> arrive as <c>[lat, lon]</c> and must go through
        /// <c>geoTransform.ToCartesian</c>. The generated lambda knows nothing of them, so the
        /// commander's <c>PlatoonHillAttackParams</c> stayed all-zero, the baseline collapsed to the
        /// origin, and the platoon drove to (0,0). Observed in the running editor; the tell was
        /// <c>TankSpacing == 0</c>, a value the curated parser cannot produce (it clamps to 30).
        /// </para>
        ///
        /// <para>
        /// ⚠ <b>Why it appeared only recently:</b> <c>DEBT-AIB-021</c> (Batch 70) widened the
        /// generated emit guard from "≥1 variable with a default" to "≥1 packed managed variable", so
        /// generated registrars began emitting <c>ParseParams</c> for assets that previously had none
        /// — quietly shadowing every curated resolver whose behavior also has a generated registrar.
        /// </para>
        /// </summary>
        private static void ApplyResolverOverlay(
            BehaviorDefinition def, (ParseParamsDelegate Resolver, Type? BlackboardLayoutType) overlay)
        {
            def.ParseParams = overlay.Resolver;
            if (overlay.BlackboardLayoutType != null)
                def.BlackboardLayoutType = overlay.BlackboardLayoutType;
        }

        /// <summary>
        /// Resolve a behavior name to its stable integer ID.
        /// Returns <c>false</c> when the name has not been registered.
        /// Used by <see cref="Systems.BehaviorIngressSystem"/> to map event names
        /// to IDs without calling <c>string.GetHashCode()</c>.
        /// </summary>
        public bool TryGetId(string name, out int id)
            => _nameToId.TryGetValue(name, out id);

        /// <summary>
        /// Look up a definition by its stable integer ID.  Returns <c>false</c>
        /// when the behavior has not been registered (entity is silently skipped
        /// by brain tick systems).
        /// </summary>
        public bool TryGetDefinition(
            int behaviorId,
            [MaybeNullWhen(false)] out BehaviorDefinition definition)
            => _definitions.TryGetValue(behaviorId, out definition);

        /// <summary>
        /// Returns a snapshot of all behavior names currently registered.
        /// The returned list is a copy of the internal key set and cannot mutate the registry.
        /// </summary>
        public IReadOnlyList<string> GetRegisteredNames()
            => _nameToId.Keys.ToList();

        /// <summary>
        /// Removes all registered behaviors. Called by AiHotReloadCoordinator.Dispose()
        /// to release managed delegate references into collectible assemblies so they
        /// can be GC-reclaimed.
        /// </summary>
        public void Clear()
        {
            _definitions.Clear();
            _nameToId.Clear();
            _resolversByName.Clear();
            _jsonParamsDtoByName.Clear();
            // O7b-3: a stale demand outliving its behaviour would size the NEXT one's tier.
            _hostedDemandByName.Clear();
        }

        /// <summary>
        /// Copies all registrations from <paramref name="source"/> into this registry.
        /// Existing entries with the same key are overwritten.
        /// Called by <see cref="AiHotReloadCoordinator.ApplyQuickReload"/> to commit
        /// a staging registry into the live registry.
        /// </summary>
        public void MergeFrom(BehaviorRegistry source)
        {
            // Carry named resolver overlays across the staging→live merge first, so that any definition
            // copied below that still lacks a resolver/DTO gets it bound during the copy.
            foreach (var (name, overlay) in source._resolversByName)
                _resolversByName[name] = overlay;

            // CE-235: same for authored JSON contracts — carried first so the copy below can bind them.
            foreach (var (name, jsonDto) in source._jsonParamsDtoByName)
                _jsonParamsDtoByName[name] = jsonDto;

            // O7b-3: and the hosted-occurrence demands. ⛔ Dropping them here would silently return
            //   every merged behaviour to the smallest tier — the exact regression this overlay fixes,
            //   reintroduced at the staging→live boundary where nothing would look for it.
            foreach (var (name, demand) in source._hostedDemandByName)
                _hostedDemandByName[name] = demand;

            foreach (var (name, id) in source._nameToId)
            {
                _nameToId[name] = id;
                if (source._definitions.TryGetValue(id, out var def))
                {
                    if (_resolversByName.TryGetValue(name, out var overlay))
                        ApplyResolverOverlay(def, overlay);
                    if (def.JsonParamsDtoType == null
                        && _jsonParamsDtoByName.TryGetValue(name, out var jsonDto))
                        def.JsonParamsDtoType = jsonDto;
                    _definitions[id] = def;
                }
            }
        }

        /// <summary>
        /// Reverse-maps a stable integer behavior ID back to its registered name.
        /// Returns <c>false</c> when the ID has not been registered.
        /// Used by egress translators to emit the human-readable <c>BehaviorId</c>
        /// string rather than the raw numeric behavior ID.
        /// </summary>
        public bool TryGetName(int behaviorId, [MaybeNullWhen(false)] out string name)
        {
            if (_definitions.TryGetValue(behaviorId, out var def))
            {
                name = def.Name;
                return true;
            }
            name = null;
            return false;
        }
    }
}
