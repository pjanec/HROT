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

    /// <remarks>
    /// ⚠ <b><c>S5</c> reversed which spelling is OFFERED and which is merely ACCEPTED.</b> The list is
    /// canonical FQNs now, so <c>"System.Int32"</c> hits on the exact-match pass and <c>"int"</c> is
    /// the one that has to be resolved. ⭐ <b>The claim under test is unchanged and is the durable
    /// one:</b> both spellings land on ONE entry, and neither falls back to index 0.
    /// </remarks>
    [Fact]
    public void BothSpellingsOfOneType_ResolveToOneEntry_NotToTheBoolFallback()
    {
        var intIdx  = BlueprintTypeChoices.IndexOfTypeId("System.Int32");
        var boolIdx = BlueprintTypeChoices.IndexOfTypeId("System.Boolean");

        Assert.Equal(intIdx, BlueprintTypeChoices.IndexOfTypeId("int"));
        Assert.True(intIdx >= 0);
        Assert.NotEqual(0, intIdx);
        Assert.NotEqual(boolIdx, intIdx);
    }

    [Theory]
    [InlineData("System.Single",           "float")]
    [InlineData("System.Boolean",          "bool")]
    [InlineData("System.Numerics.Vector3", "Vector3")]
    [InlineData("Fdp.Core.FixedString64",  "FixedString64")]
    public void TheAliasAndTheCanonicalFqn_ShareAnIndex(string fqn, string alias)
    {
        var expected = BlueprintTypeChoices.TypeIds.ToList().IndexOf(fqn);
        Assert.True(expected >= 0, $"'{fqn}' must be an offered type id (test setup check)");

        Assert.Equal(expected, BlueprintTypeChoices.IndexOfTypeId(fqn));
        Assert.Equal(expected, BlueprintTypeChoices.IndexOfTypeId(alias));
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
    /// that the picker deliberately does not offer (<c>System.String</c>, <c>System.Object</c>).
    /// Exact-match missed these too, so such a parameter also displayed <c>bool</c> — and unlike the
    /// alias cases there is no offered entry that could ever be right, so a blank preview is the only
    /// honest answer.
    ///
    /// <para>
    /// ⚠ <b><c>Fdp.Core.Entity</c> left this list with <c>S5</c>, and that is the fix, not a
    /// regression.</b> It resolves, it is unmanaged, and it was already offered by the VARIABLE
    /// modal — <i>"deliberately not offered"</i> was only ever true of the parameter combo, which is
    /// the asymmetry <c>S5</c> exists to remove. ⭐ The two entries that remain are managed reference
    /// types, so <c>BP1503</c> means nothing can ever legally offer them.
    /// </para>
    /// </summary>
    [Theory]
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
