#nullable enable
using System;
using System.Linq;
using System.Text.Json.Nodes;
using Fdp.Toolkit.Behavior;
using Hrot.CGF.Configuration;
using Hrot.Editor.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests.DebugApi;

/// <summary>
/// <c>CE-226</c> → <c>CE-235</c> — a <b>generated</b> (JSON-authored) behaviour advertises its
/// parameters too, and the type it advertises is a real, emitted DTO.
///
/// <para>
/// ⭐⭐⭐ <b>The thing this suite exists to pin: for a generated asset the authored shape and the
/// blackboard layout are the SAME TYPE, and that is the design's default case, not a shortcut.</b>
/// 📄 <c>Behavior_Parameter_Resolver_Detailed_Design.md</c> §3.2 — <i>"one shape by default — the
/// authored DTO is an auto-generated mirror; two shapes only on divergence."</i>
/// 📄 <c>Blackboard_Authoring_Detailed_Design.md</c>:18 — <i>"the param-DTO struct … is generated from
/// the JSON at build … emitted to <c>obj/GeneratedFiles</c>."</i>
/// </para>
///
/// <para>
/// ⚠ <b>An earlier reading of this got it wrong and is worth recording.</b> A grep for
/// <c>ParamsDtoType</c> over the generated files returned nothing, and that was reported as "generated
/// behaviours have no params DTO class". 📐 It measured the ASSIGNMENT, not the EXISTENCE: 15
/// <c>*.Blackboard.g.cs</c> files were sitting in <c>obj/</c> the whole time. <c>CE-235</c> makes the
/// registrar emit the assignment, so the type is named rather than merely present.
/// </para>
/// </summary>
public sealed class AGeneratedBehaviourAdvertisesItsManifestTests
{
    private static BehaviorRegistry LoadProductionRegistry()
    {
        var registry = new BehaviorRegistry();
        CgfBehaviorSetup.LoadFromAiAssembly(registry);
        return registry;
    }

    private static BehaviorDefinition Definition(BehaviorRegistry registry, string name)
    {
        Assert.True(registry.TryGetId(name, out int id), $"Behaviour '{name}' is not registered.");
        Assert.True(registry.TryGetDefinition(id, out var def) && def is not null, $"'{name}' has no definition.");
        return def!;
    }

    private static string[] PropertyNames(JsonObject schema)
        => (schema["properties"] as JsonObject)?.Select(kv => kv.Key).ToArray() ?? Array.Empty<string>();

    /// <summary>
    /// ⭐ THE ONE THAT MATTERS. A generated BTree asset advertises its packed variables by name, from the
    /// struct its own generator emitted.
    ///
    /// <para>⚠ Inverse-edit red-proof: delete the <c>JsonParamsDtoType</c> line from
    /// <c>BTreeBridgeEmitCore</c>'s definition initializer and this still passes via the manifest
    /// fallback — so the second assertion below is the one that pins the emission.</para>
    /// </summary>
    [Fact]
    public void AGeneratedBehaviourAdvertisesItsPackedVariables()
    {
        BehaviorDefinition def = Definition(LoadProductionRegistry(), "T09_BlackboardManaged");

        Assert.Equal(
            new[] { "AttackRange", "HomePosition", "PatrolLoops", "IsAlerted" },
            PropertyNames(DtoJsonSchemaExtractor.ExtractParams(def)));
    }

    /// <summary>
    /// ⭐⭐ <b>S3</b> (<c>DESIGN_Unified_Behaviour_Run</c> §4) → ⭐ <c>CE-2135</c> P4 (R-2004) — a shipped BLUEPRINT behaviour advertises
    /// its NAMED params DTO (the contract it shares with its C# twin), and its block's <c>In</c> — the generated <c>Params</c> its
    /// Inputs live in — mirrors that DTO field for field: the schema, the Input manifest and the block agree.
    /// ✅ Red-proof: drop <c>ParamsDtoTypeId</c> from <c>DangerCrossingBp.bp.json</c> → the DTO is not the published contract.
    /// </summary>
    [Fact]
    public void ABlueprintBehaviourAdvertisesItsParameters()
    {
        // ⭐ R-2006 — PlatoonHillAttackBp retired; DangerCrossingBp is the shipped blueprint behaviour with params.
        BehaviorDefinition def = Definition(LoadProductionRegistry(), "DangerCrossingBp");

        Assert.Equal("Hrot.AI.Behaviors.Brains.DangerCrossingParams", def.JsonParamsDtoType?.FullName);
        var fields = new[] { "Objective", "WalkSpeed", "RushSpeed", "CrossWithin" };
        Assert.Equal(fields, PropertyNames(DtoJsonSchemaExtractor.ExtractParams(def)));
        Assert.Equal(fields, def.ManagedBlackboardVariables!.Select(v => v.Name).ToArray());
        Assert.Equal(fields, def.BlackboardLayoutType!.GetField("In")!.FieldType
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Select(f => f.Name).ToArray());
    }

    /// <summary>
    /// ⭐ A generated behaviour's layout is its <c>{Asset}_Block</c> and the block's <c>In</c> field is
    /// the published params struct — the shape <c>CE-437</c> emits. Everything that must only look at
    /// the Inputs half filters on this, rather than on the two members being the same type.
    /// </summary>
    private static bool IsGeneratedBlock(BehaviorDefinition d)
        => d.JsonParamsDtoType is not null
        && d.BlackboardLayoutType?.GetField("In")?.FieldType == d.JsonParamsDtoType;

    /// <summary>
    /// ⭐⭐⭐ <c>CE-235</c> → <c>CE-437</c> — the generator names the emitted Inputs struct as the
    /// authored contract and the BLOCK as the layout. ⛔⛔ <b>This used to assert the two were the SAME
    /// type</b> — true while the struct held Inputs only. 📄 <c>Q76</c> §12.2b named this flip in
    /// advance: <i>"flip <c>Assert.Same</c> to 'the layout's first field is the published contract'"</i>
    /// — the separation <c>CE-235</c> made so an engine-internal layout is never a wire contract.
    ///
    /// <para>⚠ Inverse-edit red-proof: emit <c>BlackboardLayoutType = typeof(Inputs)</c> again and the
    /// name assertion fails with <c>T09_BlackboardManaged_Blackboard</c>.</para>
    /// </summary>
    [Fact]
    public void AGeneratedBehaviourPublishesItsInputsAndLaysOutItsBlock()
    {
        BehaviorDefinition def = Definition(LoadProductionRegistry(), "T09_BlackboardManaged");

        Assert.NotNull(def.JsonParamsDtoType);
        Assert.NotNull(def.BlackboardLayoutType);
        Assert.Equal("T09Params", def.JsonParamsDtoType!.Name);   // ⭐ CE-2135 P4 ⑥ — its NAMED DTO (was the generated _Blackboard mirror)
        Assert.Equal("T09_BlackboardManaged_Block", def.BlackboardLayoutType!.Name);
        Assert.Same(def.JsonParamsDtoType, def.BlackboardLayoutType.GetField("In")!.FieldType);
        Assert.Equal(0, (int)System.Runtime.InteropServices.Marshal.OffsetOf(def.BlackboardLayoutType, "In"));
    }

    /// <summary>
    /// ⭐⭐ THE CORRESPONDENCE THAT MAKES IT TRUTHFUL: what is advertised is what the definition's own
    /// manifest says, entry for entry. The generator emits the struct, the manifest and the
    /// <c>ParseParams</c> switch from ONE packed-field list, so a schema that drifted from the manifest
    /// would be a schema that drifted from the parser.
    /// </summary>
    [Fact]
    public void TheAdvertisedNamesAreExactlyTheManifestEntries()
    {
        BehaviorRegistry registry = LoadProductionRegistry();

        var generated = registry.GetRegisteredNames()
            .Select(n => Definition(registry, n))
            .Where(d => d.ManagedBlackboardVariables is { Count: > 0 } && IsGeneratedBlock(d))
            .ToArray();

        Assert.NotEmpty(generated);   // anti-vacuity

        foreach (BehaviorDefinition def in generated)
        {
            // ⭐ CE-2133 (B1) — a FLAT behaviour (its one Input IS the params DTO) advertises that DTO's fields, which are
            //   exactly the keys its ParseParams reads (BehaviorParams.OverlayFlat); every other one, its variables.
            var manifest = def.ManagedBlackboardVariables!;
            string[] expected = manifest.Count == 1 && manifest[0].Type == def.JsonParamsDtoType
                ? PropertyNames(DtoJsonSchemaExtractor.ExtractParams(manifest[0].Type)).ToArray()
                : manifest.Select(v => v.Name).ToArray();
            Assert.Equal(expected, PropertyNames(DtoJsonSchemaExtractor.ExtractParams(def)));
        }
        // a flat one is in the set (anti-vacuity for the CE-2133 arm). ⭐ CE-1410: it named Sentry, C#-built since — any flat one counts
        Assert.Contains(generated, d => d.ManagedBlackboardVariables!.Count == 1 && d.ManagedBlackboardVariables[0].Type == d.JsonParamsDtoType);
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-418</c> — <b>ONE LAYOUT AUTHORITY: the struct's offsets ARE the manifest's.</b>
    ///
    /// <para>🔴 <b>The defect this pins.</b> A generated behaviour stated its params layout TWICE —
    /// the manifest, bin-packed by <c>BTreeBlackboardPackHelper.Pack</c>, and the emitted struct, laid
    /// out by the CLR. 📐 Measured <c>2026-09-28</c> by a runtime probe over the built
    /// <c>Hrot.AI.Behaviors.dll</c>: <b>3 of 15 behaviours and 9 of 31 fields DISAGREED</b>, because
    /// <c>Pack</c> derives alignment from SIZE (<c>Math.Min(size, 8)</c>) while the CLR aligns by
    /// TYPE — <c>Vector3</c> is 12 bytes aligned 4, so <c>Pack</c> put it on 8 and the CLR on 4.</para>
    ///
    /// <para>⛔⛔ It was LIVE: StructEdit's "Active Parameters" READS AND WRITES at the struct's
    /// offsets and the ReplayBrowser predicate compiler binds against them, while
    /// <c>RootParamsProjection</c> uses the manifest ⇒ two panels disagreed about one entity. Sizing
    /// was safe (<c>RootParamsBytes</c> prefers the manifest), which is exactly why nothing crashed
    /// and nothing caught it: <b>the manifest's own tests assert names and round-tripping, never an
    /// offset against the struct.</b> ⇒ this test is that missing assertion.</para>
    ///
    /// <para>✅ <b>Inverse-edit red-proof — RUN <c>2026-09-29</c>:</b> with <c>BTreeEmitCore</c>'s
    /// emission put back to <c>LayoutKind.Sequential</c> (the <c>[FieldOffset]</c> lines dropped) this
    /// FAILED with 5 disagreements on 2 behaviours — <c>T09_BlackboardManaged</c>
    /// <c>HomePosition</c> 8/4 · <c>PatrolLoops</c> 20/16 · <c>IsAlerted</c> 24/20, and
    /// <c>T39_TwoDistinctPrimitives</c> <c>bpParamsB</c> 8/4 · <c>bpParamsC</c> 16/12 — and passed 7/7
    /// with the fix restored. ⚠ The original probe's third behaviour, <c>PlatoonHillAttack2</c>, was
    /// deleted by <c>CE-436</c>. ⛔ The first attempt used an <c>if (false)</c> toggle, which raises
    /// <c>CS0162</c> under warnings-as-errors and so never produced a test host — do the edit textually.</para>
    /// </summary>
    [Fact]
    public void TheStructsOffsetsAreExactlyTheManifestsOffsets()
    {
        BehaviorRegistry registry = LoadProductionRegistry();

        var generated = registry.GetRegisteredNames()
            .Select(n => Definition(registry, n))
            .Where(d => d.ManagedBlackboardVariables is { Count: > 0 } && IsGeneratedBlock(d))
            .ToArray();

        Assert.NotEmpty(generated);   // anti-vacuity: the probe measured 15 such behaviours

        var disagreements = new System.Collections.Generic.List<string>();

        foreach (BehaviorDefinition def in generated)
        {
            foreach (ManagedBlackboardVariable v in def.ManagedBlackboardVariables!)
            {
                // ⚠ A field the struct does not carry is a DIFFERENT defect (name drift), and
                //   TheAdvertisedNamesAreExactlyTheManifestEntries owns it. Skip rather than
                //   throw, so a name failure is reported by the test that explains it.
                // ⚠ CE-437: the Inputs struct is the block's `In` at offset 0, so its offsets ARE the
                //   block's — compare against it, not the block (which has no field named v.Name).
                if (def.JsonParamsDtoType!.GetField(v.Name) is null) continue;

                int structOffset = (int)System.Runtime.InteropServices.Marshal
                    .OffsetOf(def.JsonParamsDtoType!, v.Name);

                if (structOffset != v.ByteOffset)
                    disagreements.Add(
                        $"{def.Name}.{v.Name}: manifest {v.ByteOffset}, struct {structOffset}");
            }
        }

        Assert.True(disagreements.Count == 0,
            "The emitted struct and the manifest must state ONE layout (CE-418). Disagreements:\n  "
            + string.Join("\n  ", disagreements));
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-425</c> — <b>every generated behaviour emits ONE BLOCK, and its Inputs half IS the
    /// published params struct, at offset 0.</b> 📄 <c>Q76</c> §12.2a / §12.7 row 1: <i>"the Input region
    /// is byte-identical — assert on the ARTEFACT, not on a re-computation."</i>
    ///
    /// <para>⭐ Asserting that <c>In</c> is the SAME <see cref="Type"/> as <c>JsonParamsDtoType</c> is
    /// stronger than comparing offsets field by field: one type cannot have two layouts, so every
    /// manifest offset <see cref="TheStructsOffsetsAreExactlyTheManifestsOffsets"/> pins holds inside
    /// the block unchanged.</para>
    ///
    /// <para>⭐⭐ <c>CE-437</c> + <c>CE-429</c>: the State in <c>St</c> has ONE home — the definition
    /// provisions no <c>Scope=Behavior</c> side slot any more — and the root slot is sized to hold the
    /// whole block. Both would be silent if wrong: a leftover side slot is storage nobody reads, and an
    /// undersized root slot makes <c>TryGetBlockFor</c> refuse and every stateful node fail.</para>
    ///
    /// <para>⚠ <b>Inverse-edit red-proof:</b> drop the <c>TryGetBlockStateVariable</c> skip in
    /// <c>EmitStatefulWorkingSlotsArray</c> and the side-slot assertion fails for
    /// <c>T35_SharedWorkingState</c> and <c>PlatoonHillAttack</c>; size the root from the manifest extent
    /// alone and the width assertion fails for both.</para>
    /// </summary>
    [Fact]
    public void EveryGeneratedBehaviourHasOneBlockWhoseInputsAreItsParamsStructAtOffsetZero()
    {
        BehaviorRegistry registry = LoadProductionRegistry();

        var generated = registry.GetRegisteredNames()
            .Select(n => Definition(registry, n))
            .Where(d => d.ManagedBlackboardVariables is { Count: > 0 }
                     && d.JsonParamsDtoType is not null
                     && d.BlackboardLayoutType is not null
                     && d.BlackboardLayoutType.Name.EndsWith("_Block", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(generated);   // anti-vacuity

        int withState = 0;
        var problems = new System.Collections.Generic.List<string>();

        foreach (BehaviorDefinition def in generated)
        {
            Type layout = def.JsonParamsDtoType!;       // the published Inputs struct
            Type block  = def.BlackboardLayoutType!;    // CE-437: the layout IS the block

            var inField = block.GetField("In");
            // ⭐ CE-443 (DESIGN_Parameter_Model §P.8): a behaviour bound to a RESOLVER publishes the resolver's AUTHORED
            //   type as its JSON DTO — the source the resolver receives — and the block's In is what the resolver WRITES.
            //   ⇒ the two are deliberately different types there. (This rail was missed when CE-443 landed: only the
            //   resolver-filtered Editor tests were run; found by CE-449's full-suite gate.)
            // ⭐ CE-2134: a CURATED resolver likewise (PlatoonHillAttack declares its resolver's JSON DTO; its In is the struct it stores).
            bool resolverBound = def.ResolveStage is not null || registry.HasCuratedResolver(def.Name);
            // ⭐ CE-2135 (B3a): NoParams stores nothing — the block's In holds only plain variables, never the contract; and a named
            //   DTO beside plain variables sits at In's first field, Params.
            bool storesNothing = RootParamsAccess.IsNoParams(layout);
            bool paramsFirst = inField?.FieldType.GetField("Params") is { } pf && pf.FieldType == layout
                && (int)System.Runtime.InteropServices.Marshal.OffsetOf(inField.FieldType, "Params") == 0;
            if (inField is null)
                problems.Add($"{def.Name}: block.In is missing");
            else if (!resolverBound && !storesNothing && !paramsFirst && inField.FieldType != layout)
                problems.Add($"{def.Name}: block.In is {inField.FieldType.Name}, not {layout.Name}");
            else if ((int)System.Runtime.InteropServices.Marshal.OffsetOf(block, "In") != 0)
                problems.Add($"{def.Name}: block.In is not at offset 0");

            var stField = block.GetField("St");
            if (stField is null) continue;
            withState++;

            // ⭐ CE-437: a variable in St must NOT also have a Behavior-scoped side slot — one home.
            var sideSlots = (def.StatefulWorkingSlots ?? Array.Empty<StatefulSlotInfo>())
                .Where(s => s.Scope == (byte)Fdp.Toolkit.Blueprints.Partitioning.StatefulSlotScope.Behavior)
                .ToArray();
            if (sideSlots.Length > 0)
                problems.Add($"{def.Name}: still provisions {sideSlots.Length} Behavior-scoped side slot(s) although its State lives in the block");

            // ⭐ CE-429: the root slot is sized from the block, so the State half is allocated.
            int rootBytes = RootParamsAccess.RootParamsBytes(def);
            if (rootBytes < System.Runtime.InteropServices.Marshal.SizeOf(block))
                problems.Add($"{def.Name}: root slot {rootBytes} B is narrower than the block");

            int inBytes = System.Runtime.InteropServices.Marshal.SizeOf(inField?.FieldType ?? layout);
            if ((int)System.Runtime.InteropServices.Marshal.OffsetOf(block, "St") < inBytes)
                problems.Add($"{def.Name}: block.St overlaps block.In");
        }

        Assert.True(problems.Count == 0, "CE-425 block shape:\n  " + string.Join("\n  ", problems));
        Assert.True(withState > 0, "anti-vacuity: at least one generated behaviour carries Behavior-scoped State");
    }

    /// <summary>
    /// ⭐⭐⭐ <c>CE-416</c> (<c>Q76</c> §12.27) — THE HSM ARM, no longer an exception. ⛔ SUPERSEDED (<c>2026-10-01</c>): <i>"the
    /// HSM generator emits no blackboard struct, so there is no type to name and the manifest IS the authored contract"</i>.
    /// The HSM generator now calls the same <c>EmitBlackboardStructSource</c> as the BTree, so an HSM publishes its Inputs
    /// struct and lays out its block exactly like a BTree — and the generic block rail above covers it.
    /// <para>⛔ <b>SUPERSEDED in part (CE-2135 P4 ⑥, R-2004):</b> "publishes its Inputs struct" — the generated mirror is no longer a
    /// contract; the HSM publishes its NAMED DTO. The block half stands.</para>
    /// </summary>
    [Fact]
    public void AnHsmBehaviourPublishesItsNamedDto_AndLaysOutItsBlock()
    {
        BehaviorDefinition def = Definition(LoadProductionRegistry(), "HsmVariableShowcase");

        // ⭐ CE-2135 P4 ⑥ (R-2004) — the asset names NoParams: its published contract is the DTO, not a generated Inputs mirror;
        //   it still lays out its block like a BTree (the CE-416 point), with its plain variable in In and its State in St.
        Assert.Equal("Hrot.Map.Definitions.Behavior.NoParams", def.JsonParamsDtoType?.FullName);
        Assert.Equal("HsmVariableShowcase_Block", def.BlackboardLayoutType!.Name);
        Assert.NotNull(def.BlackboardLayoutType.GetField("In"));   // Threshold, Cursor, Ticks — plain packed variables
        // ⭐ CE-3193 — nothing names Cursor/Ticks as working state, so there is no St (the state block is HsmStatefulBindingDemo's, below)
        Assert.Null(def.BlackboardLayoutType.GetField("St"));
        Assert.DoesNotContain("Threshold", PropertyNames(DtoJsonSchemaExtractor.ExtractParams(def)));   // ⛔ never JSON
    }

    /// <summary>
    /// ⭐⭐ <c>CE-416</c> — an HSM with State and NO plain variable (<c>HsmStatefulBindingDemo</c>, <c>steps</c> — named as its state
    /// binding's working state, CE-3193; ⛔ was <c>HsmOrthogonalRegions</c>' <c>SharedCursor</c>, which nothing names) still owns a block
    /// (<c>CE-429</c>'s rule, HSM half): an empty manifest, a parse that allocates and bakes, a root slot as wide as the block,
    /// and no side slot. 🔴 RED before: no layout type and no parse ⇒ the ingress allocated nothing for it.
    /// </summary>
    [Fact]
    public void AStateOnlyHsm_StillOwnsItsBlock()
    {
        BehaviorDefinition def = Definition(LoadProductionRegistry(), "HsmStatefulBindingDemo");

        Assert.Equal("HsmStatefulBindingDemo_Block", def.BlackboardLayoutType?.Name);
        Assert.NotNull(def.BlackboardLayoutType!.GetField("St"));
        Assert.DoesNotContain(def.ManagedBlackboardVariables!, v => v.Name == "steps");   // working state is not a packed variable
        Assert.NotNull(def.ParseParams);
        Assert.True(RootParamsAccess.RootParamsBytes(def) >= System.Runtime.InteropServices.Marshal.SizeOf(def.BlackboardLayoutType!));
        Assert.DoesNotContain(def.StatefulWorkingSlots ?? Array.Empty<StatefulSlotInfo>(),
            s => s.Scope == (byte)Fdp.Toolkit.Blueprints.Partitioning.StatefulSlotScope.Behavior);
    }

    /// <summary>
    /// ⛔ <c>R-132</c> — <i>"a curated (hand-authored) artefact outranks a generated one"</i>. When both a
    /// curated authored contract and a generated manifest exist, the authored DTO wins and the two are
    /// NOT unioned. ⛔ <c>HullDownAttackRun</c> was the real instance (generated topology + a curated overlay) until <c>CE-1410</c> made
    /// it a curated C# tree, which publishes no manifest; no shipped behaviour has both now, so the rule is pinned on a definition of
    /// that same shape (its contract, its old manifest entry).
    /// </summary>
    [Fact]
    public void WhenBothExistTheAuthoredContractWins()
    {
        var def = new BehaviorDefinition
        {
            Name = "BothShapes",
            JsonParamsDtoType = typeof(Hrot.AI.Behaviors.Brains.HullDownAttackParams),
            ManagedBlackboardVariables = new[] { new ManagedBlackboardVariable("fight", typeof(Hrot.AI.Behaviors.Brains.VehicleFightFromCoverParams), 0) },
        };

        Assert.NotNull(def.JsonParamsDtoType);
        Assert.NotNull(def.ManagedBlackboardVariables);   // both present — the interesting case

        string[] advertised  = PropertyNames(DtoJsonSchemaExtractor.ExtractParams(def));
        string[] fromContract = PropertyNames(DtoJsonSchemaExtractor.ExtractParams(def.JsonParamsDtoType));

        Assert.Equal(fromContract, advertised);
        Assert.True(advertised.Length > 0);
    }

    /// <summary>
    /// ⭐⭐ THE RESIDUE, ASSERTED SO IT CANNOT SILENTLY REGROW. A behaviour that describes NAMED
    /// parameters must advertise them; an empty schema is allowed only where the description is itself
    /// empty. This is the defect <c>CE-226</c> closed — 34 of 40 behaviours reporting <c>{}</c> while
    /// their generators knew every key.
    ///
    /// <para>
    /// ⚠ The <c>JoinFormation</c> carve-out is REAL, not a weakening: its <c>[Behavior(Params = …)]</c> DTO
    /// is deliberately memberless — <i>"currently parameterless; the contract exists to anchor the
    /// behavior ID and category"</i> — so <c>{}</c> is the truthful answer. The predicate below keys on
    /// whether the description carries NAMES, which is the property that actually matters.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryBehaviourThatDescribesNamedParametersAdvertisesThem()
    {
        BehaviorRegistry registry = LoadProductionRegistry();

        int checkedCount = 0;
        foreach (string name in registry.GetRegisteredNames())
        {
            BehaviorDefinition def = Definition(registry, name);

            // What the definition CLAIMS to describe, by name — from the authored contract when it has
            // one, else from the packed manifest.
            string[] described = def.JsonParamsDtoType is not null
                ? PropertyNames(DtoJsonSchemaExtractor.ExtractParams(def.JsonParamsDtoType))
                : def.ManagedBlackboardVariables?.Select(v => v.Name).ToArray() ?? Array.Empty<string>();

            if (described.Length == 0) continue;   // genuinely parameterless — {} is the truth

            checkedCount++;
            Assert.True(PropertyNames(DtoJsonSchemaExtractor.ExtractParams(def)).Length > 0,
                $"Behaviour '{name}' describes [{string.Join(", ", described)}] but advertises none.");
        }

        Assert.True(checkedCount > 0, "no behaviour described a named parameter — the loader is broken.");
    }
}
