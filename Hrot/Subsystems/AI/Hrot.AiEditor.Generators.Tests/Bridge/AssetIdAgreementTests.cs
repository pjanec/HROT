using System;
using FluentAssertions;
using Fdp.Toolkit.Behavior;
using Hrot.Editor.AiShared.Identity;
using Xunit;

namespace Hrot.AiEditor.Generators.Tests.Bridge;

/// <summary>
/// ⭐⭐ <b><c>CE-368</c> — the EDITOR and the RUNTIME must derive the SAME asset id for a
/// hand-written tree.</b> 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §33.6.
///
/// <para>⚠ <b>Why this can drift at all.</b> <c>BTreeDefinitionAttribute.AssetId</c> is documented
/// *"null for hand-authored"*, so both sides fall back to hashing the NAME — the editor through
/// <see cref="AssetIdHasher.FromName"/>, the runtime through
/// <see cref="BTreeHostedSites.AssetIdFromName"/>. Both are FNV-1a-32 on offset basis
/// <c>2166136261</c>, but they are <b>two implementations</b>: ⛔
/// <c>Hrot.Editor.AiShared</c> does not reference <c>Fdp.Toolkits</c>, so they cannot delegate.</para>
///
/// <para>⛔⛔ <b>What a drift would cost:</b> the editor would compute one
/// <c>ComputeTreeStateKey</c> and the runtime another, so a hosted child's slot would be declared
/// under one key and read under a different one — and <c>HostedSubtree.Tick</c> would THROW on a
/// slot the manifest "declares". ⭐ Loud rather than silent, but only at runtime.</para>
/// </summary>
public sealed class AssetIdAgreementTests
{
    /// <summary>
    /// ⭐ Real behaviour names plus the shapes that exercise the edge: empty, unicode, and a name
    /// long enough to spill any small buffer.
    /// </summary>
    public static TheoryData<string> Names => new()
    {
        "MoveToLocation", "FollowRoute", "JoinFormation", "WanderMilitary", "FireAtTarget",
        "Idle", "HullDownAttackRun", "PlatoonHillAttack", "HideInCover_BT", "HideInCover_BT_v2",
        "E6_Host", "E6_Child", "a", "Ünïcödé_Näme", new string('x', 300),
    };

    [Theory]
    [MemberData(nameof(Names))]
    public void TheEditorAndTheRuntime_DeriveTheSameAssetId(string name)
    {
        AssetIdHasher.FromName(name).Should().Be(
            BTreeHostedSites.AssetIdFromName(name),
            $"the editor and the runtime must agree on the identity of '{name}'");
    }

    /// <summary>
    /// ⚠⚠ <b>THE ONE KNOWN DIVERGENCE, asserted rather than discovered later.</b>
    /// <c>BehaviorHash.FromName</c> — which the runtime side wraps — maps a null/empty name to the
    /// <c>0</c> sentinel and nudges a genuinely-zero hash to <c>FnvPrime</c> so a real behaviour can
    /// never alias "no behaviour". <see cref="AssetIdHasher"/> does neither.
    ///
    /// <para>⭐ Harmless in practice: an empty behaviour name is rejected long before it reaches a
    /// hosting site. ⛔ But it is a real difference, and a rail that pretended the two functions were
    /// identical would be lying — so it is pinned here instead of hidden by the theory above.</para>
    /// </summary>
    [Fact]
    public void TheEmptyName_IsTheOneDocumentedDivergence()
    {
        BTreeHostedSites.AssetIdFromName(string.Empty).Should().Be(Guid.Empty,
            "the runtime maps an empty name to the zero sentinel");

        AssetIdHasher.FromName(string.Empty).Should().NotBe(Guid.Empty,
            "the editor hashes it like any other string — this is the documented divergence");
    }
}
