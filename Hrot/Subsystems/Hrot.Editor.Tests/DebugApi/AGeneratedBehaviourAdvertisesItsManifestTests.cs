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
/// <c>CE-226</c> — a <b>generated</b> (JSON-authored) behaviour has no params DTO struct, so it must
/// advertise its parameters from <see cref="BehaviorDefinition.ManagedBlackboardVariables"/>.
///
/// <para>
/// <c>CE-224</c> fixed the extractor and took <c>GET /behaviors</c> from 0/40 to 6/40 — exactly the
/// behaviours that carry a <c>ParamsDtoType</c>. The other 34 were the generated ones. They were never
/// silent about their parameters: every generated registrar already emitted a manifest of
/// <c>ManagedBlackboardVariable(Name, Type, ByteOffset)</c>; nothing read it.
/// </para>
///
/// <para>
/// ⭐⭐ <b>The manifest is the MORE truthful source for these behaviours, not a fallback.</b> 📐 Measured
/// on <c>T09_BlackboardManaged</c>: its manifest names are <b>exactly</b> the <c>case</c> labels of the
/// generated <c>ParseParams</c> switch, because the generator emits both from the same packed-field
/// list. So the schema cannot drift from the parser — which is precisely what this endpoint promises
/// and what <c>CE-228</c> shows the curated arm cannot yet guarantee.
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
    /// ⭐ THE ONE THAT MATTERS. A generated behaviour with packed managed variables advertises them by
    /// name, with no <c>ParamsDtoType</c> anywhere in sight.
    ///
    /// <para>⚠ Inverse-edit red-proof: delete the <c>ManagedBlackboardVariables</c> loop from
    /// <c>DtoJsonSchemaExtractor.ExtractParams(BehaviorDefinition)</c> and this fails with an empty
    /// list — the pre-<c>CE-226</c> state.</para>
    /// </summary>
    [Fact]
    public void AGeneratedBehaviourAdvertisesItsPackedVariables()
    {
        BehaviorRegistry registry = LoadProductionRegistry();
        BehaviorDefinition def = Definition(registry, "T09_BlackboardManaged");

        Assert.Null(def.ParamsDtoType);          // no DTO struct — this is the generated shape
        Assert.NotNull(def.ManagedBlackboardVariables);

        Assert.Equal(
            new[] { "AttackRange", "HomePosition", "PatrolLoops", "IsAlerted" },
            PropertyNames(DtoJsonSchemaExtractor.ExtractParams(def)));
    }

    /// <summary>
    /// ⭐⭐ THE CORRESPONDENCE THAT MAKES IT TRUTHFUL: what is advertised is what the definition's own
    /// manifest says, entry for entry. A schema that drifted from the manifest would be a schema that
    /// drifted from <c>ParseParams</c>, since the generator emits both from one list.
    /// </summary>
    [Fact]
    public void TheAdvertisedNamesAreExactlyTheManifestEntries()
    {
        BehaviorRegistry registry = LoadProductionRegistry();

        var generated = registry.GetRegisteredNames()
            .Select(n => Definition(registry, n))
            .Where(d => d.ParamsDtoType is null && d.ManagedBlackboardVariables is { Count: > 0 })
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
    /// ⛔ <c>R-132</c> — <i>"a curated (hand-authored) artefact outranks a generated one"</i>. When a
    /// behaviour has BOTH a curated DTO type and a generated manifest, the DTO wins and the two are NOT
    /// unioned. <c>HullDownAttackRun</c> is the real instance: its topology is generated, its params DTO
    /// comes from the curated registrar's resolver overlay.
    /// </summary>
    [Fact]
    public void WhenBothExistTheCuratedDtoWins()
    {
        BehaviorRegistry registry = LoadProductionRegistry();
        BehaviorDefinition def = Definition(registry, "HullDownAttackRun");

        Assert.NotNull(def.ParamsDtoType);
        Assert.NotNull(def.ManagedBlackboardVariables);   // both present — the interesting case

        string[] advertised = PropertyNames(DtoJsonSchemaExtractor.ExtractParams(def));
        string[] fromDto    = PropertyNames(DtoJsonSchemaExtractor.ExtractParams(def.ParamsDtoType));

        Assert.Equal(fromDto, advertised);
        Assert.True(advertised.Length > 0);
    }

    /// <summary>
    /// ⭐⭐ THE RESIDUE, ASSERTED SO IT CANNOT SILENTLY REGROW. After <c>CE-226</c>, a behaviour may
    /// advertise nothing only if it genuinely describes nothing — no DTO type AND no manifest. An entry
    /// that has a description but shows an empty schema is the defect this row closes.
    /// </summary>
    [Fact]
    public void EveryBehaviourThatDescribesItsParametersAdvertisesThem()
    {
        BehaviorRegistry registry = LoadProductionRegistry();

        foreach (string name in registry.GetRegisteredNames())
        {
            BehaviorDefinition def = Definition(registry, name);
            bool describesSomething =
                def.ParamsDtoType is not null || def.ManagedBlackboardVariables is { Count: > 0 };

            if (!describesSomething) continue;

            Assert.True(PropertyNames(DtoJsonSchemaExtractor.ExtractParams(def)).Length > 0,
                $"Behaviour '{name}' describes parameters but advertises none.");
        }
    }
}
