using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor.Variables;

namespace Hrot.Blueprints.Tests.Catalog;

/// <summary>
/// BP-114: the parameter Type combo resolved its selected index by <b>exact string match</b>
/// against <see cref="BlueprintTypeChoices.TypeIds"/> — a list of <i>aliases</i> (<c>"int"</c>,
/// <c>"float"</c>, …). Most shipped assets store the compiler's <i>canonical FQN</i>
/// (<c>"System.Int32"</c>, <c>"System.Single"</c>, …) instead, so the match never hit and the combo
/// silently fell back to index 0 — <c>"bool"</c> — for every such parameter. This is display-only
/// (nothing is rewritten until the designer actually changes the combo), but a designer who
/// "corrects" the visibly-wrong <c>bool</c> back to the parameter's real type silently retypes it
/// for real.
///
/// <para>
/// ⭐ The durable half is <see cref="BlueprintTypeChoices.IndexOfTypeId"/> — a pure, testable method
/// extracted out of the (untestable) ImGui draw loop in <c>ParameterRowsView.Draw</c>.
/// </para>
/// </summary>
public sealed class BP114_TypeComboIndexTests
{
    // ── the defect itself ──────────────────────────────────────────────────────

    [Fact]
    public void CanonicalFqn_ResolvesToItsAliasEntry_NotToBoolFallback()
    {
        var intIdx  = BlueprintTypeChoices.IndexOfTypeId("System.Int32");
        var boolIdx = BlueprintTypeChoices.IndexOfTypeId("bool");

        Assert.Equal(BlueprintTypeChoices.TypeIds.ToList().IndexOf("int"), intIdx);
        Assert.NotEqual(0, intIdx);
        Assert.NotEqual(boolIdx, intIdx);
    }

    [Theory]
    [InlineData("System.Single",           "float")]
    [InlineData("System.Boolean",          "bool")]
    [InlineData("System.Numerics.Vector3", "Vector3")]
    [InlineData("Fdp.Core.FixedString64",  "FixedString64")]
    public void CanonicalFqn_ResolvesToTheMatchingAlias(string fqn, string alias)
    {
        var expected = BlueprintTypeChoices.TypeIds.ToList().IndexOf(alias);
        Assert.True(expected >= 0, $"'{alias}' must be an offered type id (test setup check)");

        Assert.Equal(expected, BlueprintTypeChoices.IndexOfTypeId(fqn));
    }

    // ── exact-alias round trip: the anti-drift lock ────────────────────────────

    [Fact]
    public void EveryOfferedAlias_RoundTripsToItsOwnIndex()
    {
        var typeIds = BlueprintTypeChoices.TypeIds;
        for (int i = 0; i < typeIds.Count; i++)
        {
            Assert.Equal(i, BlueprintTypeChoices.IndexOfTypeId(typeIds[i]));
        }
    }

    // ── unresolvable / null / empty must NOT fall back to 0 ────────────────────

    [Fact]
    public void UnresolvableTypeId_ReturnsMinusOne_NotZero()
    {
        var idx = BlueprintTypeChoices.IndexOfTypeId("Not.A.Real.Type");
        Assert.Equal(-1, idx);
        Assert.NotEqual(0, idx);
    }

    /// <summary>
    /// The case the fallback-to-0 got most wrong: a type the compiler resolves perfectly well but
    /// that the picker deliberately does not offer (<c>Fdp.Core.Entity</c>, <c>System.String</c>,
    /// the curated blittable structs). Exact-match missed these too, so an <c>Entity</c> parameter
    /// also displayed <c>bool</c> — and unlike the alias cases there is no offered entry that could
    /// ever be right, so a blank preview is the only honest answer.
    /// </summary>
    [Theory]
    [InlineData("Fdp.Core.Entity")]
    [InlineData("System.String")]
    [InlineData("System.Object")]
    public void ResolvableButNotOffered_ReturnsMinusOne_NotZero(string typeId)
    {
        // Guard: these must genuinely resolve, or the test proves nothing beyond the unresolvable case.
        Assert.True(
            StaticTypeRegistry.Instance.TryResolve(new BlueprintTypeRef { TypeId = typeId }, out _),
            $"'{typeId}' must resolve for this test to be meaningful (test setup check)");
        Assert.DoesNotContain(typeId, BlueprintTypeChoices.TypeIds);

        var idx = BlueprintTypeChoices.IndexOfTypeId(typeId);
        Assert.Equal(-1, idx);
        Assert.NotEqual(0, idx);
    }

    [Fact]
    public void NullTypeId_ReturnsMinusOne_NotZero()
    {
        var idx = BlueprintTypeChoices.IndexOfTypeId(null);
        Assert.Equal(-1, idx);
        Assert.NotEqual(0, idx);
    }

    [Fact]
    public void EmptyTypeId_ReturnsMinusOne_NotZero()
    {
        var idx = BlueprintTypeChoices.IndexOfTypeId("");
        Assert.Equal(-1, idx);
        Assert.NotEqual(0, idx);
    }
}
