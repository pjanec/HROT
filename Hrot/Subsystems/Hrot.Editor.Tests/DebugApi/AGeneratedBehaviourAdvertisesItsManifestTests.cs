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
    /// ⭐⭐⭐ <c>CE-235</c> — the generator NAMES the emitted struct as the authored contract, and names
    /// the same type as the layout. Both, and equal: for a generated asset there is one shape.
    ///
    /// <para>⚠ Inverse-edit red-proof: remove either emitted line and this fails on the null.</para>
    /// </summary>
    [Fact]
    public void AGeneratedBehaviourNamesItsEmittedStructAsBothShapes()
    {
        BehaviorDefinition def = Definition(LoadProductionRegistry(), "T09_BlackboardManaged");

        Assert.NotNull(def.JsonParamsDtoType);
        Assert.NotNull(def.BlackboardLayoutType);
        Assert.Same(def.JsonParamsDtoType, def.BlackboardLayoutType);
        Assert.Equal("T09_BlackboardManaged_Blackboard", def.JsonParamsDtoType!.Name);
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
            .Where(d => d.ManagedBlackboardVariables is { Count: > 0 }
                     && d.JsonParamsDtoType is not null
                     && ReferenceEquals(d.JsonParamsDtoType, d.BlackboardLayoutType))
            .ToArray();

        Assert.NotEmpty(generated);   // anti-vacuity

        foreach (BehaviorDefinition def in generated)
        {
            Assert.Equal(
                def.ManagedBlackboardVariables!.Select(v => v.Name).ToArray(),
                PropertyNames(DtoJsonSchemaExtractor.ExtractParams(def)));
        }
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
    /// <para>⚠ <b>Inverse-edit red-proof:</b> change <c>BTreeEmitCore</c>'s emission back to
    /// <c>LayoutKind.Sequential</c> (drop the <c>[FieldOffset]</c> lines) and this fails on
    /// <c>T09_BlackboardManaged.HomePosition</c> — manifest 8, struct 4 — plus
    /// <c>T39_TwoDistinctPrimitives</c> and <c>PlatoonHillAttack2</c>.</para>
    /// </summary>
    [Fact]
    public void TheStructsOffsetsAreExactlyTheManifestsOffsets()
    {
        BehaviorRegistry registry = LoadProductionRegistry();

        var generated = registry.GetRegisteredNames()
            .Select(n => Definition(registry, n))
            .Where(d => d.ManagedBlackboardVariables is { Count: > 0 } && d.BlackboardLayoutType is not null)
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
                if (def.BlackboardLayoutType!.GetField(v.Name) is null) continue;

                int structOffset = (int)System.Runtime.InteropServices.Marshal
                    .OffsetOf(def.BlackboardLayoutType!, v.Name);

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
    /// ⚠ THE HSM ARM, and it is a MEASURED exception rather than an oversight. The HSM generator emits no
    /// blackboard struct at all — <c>BTreeEmitCore.EmitBlackboardStructSource</c> has exactly one caller
    /// (<c>BTreeJsonGenerator</c>), and no <c>*.Blackboard.g.cs</c> is produced for any HSM asset — so
    /// there is no type to name and the manifest IS the authored contract. Legitimate for the same reason
    /// as above: manifest and <c>ParseParams</c> come from one packed-field list.
    /// </summary>
    [Fact]
    public void AnHsmBehaviourAdvertisesFromItsManifestWithNoDtoType()
    {
        BehaviorDefinition def = Definition(LoadProductionRegistry(), "HsmVariableShowcase");

        Assert.Null(def.JsonParamsDtoType);          // no emitted struct on the HSM path
        Assert.NotNull(def.ManagedBlackboardVariables);
        Assert.Contains("Threshold", PropertyNames(DtoJsonSchemaExtractor.ExtractParams(def)));
    }

    /// <summary>
    /// ⛔ <c>R-132</c> — <i>"a curated (hand-authored) artefact outranks a generated one"</i>. When both a
    /// curated authored contract and a generated manifest exist, the authored DTO wins and the two are
    /// NOT unioned. <c>HullDownAttackRun</c> is the real instance: its topology is generated, its
    /// resolver comes from the curated registrar's overlay.
    /// </summary>
    [Fact]
    public void WhenBothExistTheAuthoredContractWins()
    {
        BehaviorDefinition def = Definition(LoadProductionRegistry(), "HullDownAttackRun");

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
    /// ⚠ The <c>JoinFormation</c> carve-out is REAL, not a weakening: its <c>[BehaviorContract]</c> DTO
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
