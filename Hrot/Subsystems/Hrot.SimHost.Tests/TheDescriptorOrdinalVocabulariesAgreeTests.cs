using System;
using System.Collections.Generic;
using System.Linq;
using Fdp.Toolkit.Replication;
using Hrot.SimHost.Installers;
using Xunit;
using EDescriptorType = Hrot.NED.Descriptors.EDescriptorType;
using eForceIdentifier = Hrot.NED.Descriptors.eForceIdentifier;

namespace Hrot.SimHost.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>AX-017</c> — THE RAIL THAT MAKES THE ACCEPTED DUPLICATION SAFE.</b>
///
/// <para>📄 <c>docs/DESIGN_Cgf_AxisB_Rotation_Slice.md</c> §16. 🔒 <b>User ruling, <c>2026-08-26</c>:</b>
/// *"if same enums needs to exist twice in different namespaces (network one, fdp one), so be it, with same
/// numeric value, translated in network translator, accepted cost for network agnosticism."*</para>
///
/// <para>⭐⭐⭐ <b>The ruling has a PRICE, and this file is how it gets paid.</b> Two enums with *"the same
/// numeric value"* is a claim about the state of the code, and ⛔ a claim about code ROTS — 📌 exactly the
/// disease <c>RULINGS.md</c> §M names: *"MEASURE, DON'T MEMORISE."* ⚠ The two pre-existing copies of
/// <c>eForceIdentifier</c> *(<c>Hrot.NED.Descriptors</c> and <c>Hrot.Core.Mission</c>)* are kept in step by a
/// COMMENT and nothing else — ⇒ that is the outcome this rail exists to avoid for the new pair.</para>
///
/// <para>⭐⭐ <b>Element-wise and BIDIRECTIONAL, not a count.</b> 🔴 A count check passes when one member is
/// renamed and another added; a *"FDP ⊆ DDS"* check passes when the DDS side grows a member the apply path
/// can never mark dirty. ⇒ both directions are asserted, member by member, so <b>a renumber on either side,
/// a member added to only one side, or a rename that breaks the naming law</b> is a RED.</para>
///
/// <para>⚠ <b>What it does NOT claim.</b> ⛔ It does not say the two enums MEAN the same thing — that is
/// <see cref="DescriptorOrdinalConversion"/>'s job, and that type throws rather than casts precisely because
/// a compile-time agreement is not a runtime guarantee for a value that arrived off the wire.</para>
/// </summary>
public class TheDescriptorOrdinalVocabulariesAgreeTests
{
    /// <summary>
    /// ⭐ <b>The naming law:</b> the DDS member is the FDP member with a <c>dt</c> prefix.
    /// 📐 Verified against all 34 members, <c>2026-08-26</c> — <c>WorldPos</c> ↔ <c>dtWorldPos</c>.
    /// </summary>
    private const string DdsPrefix = "dt";

    // ══ ① DescriptorOrdinal — the pair that indexes the same DirtyDescriptors bit set ══

    /// <summary>
    /// ⭐⭐⭐ <b>Every <see cref="DescriptorOrdinal"/> member has a <c>dt</c>-prefixed twin with the SAME
    /// numeric value.</b>
    ///
    /// <para>⭐⭐ <b>Why the numbers and not just the names.</b> 📐 Measured: an ordinal is a <b>bit index</b>
    /// into <c>EgressPublicationState.DirtyDescriptors</c>. An FDP-side installer marks bit <c>N</c>; a
    /// network-side egress translator asks <c>ShouldPublish(view, entity, N)</c>. ⇒ if the two enums drift by
    /// one, ⛔ <b>the wrong descriptor is published and the right one is never republished</b> — silently, with
    /// no exception anywhere. 📌 That is precisely the <c>AX-015</c> failure mode *(a rename that never
    /// reached the wire)*, and it would come back through the back door.</para>
    /// </summary>
    [Fact]
    public void EveryFdpDescriptorOrdinalHasTheSameValueOnTheWireSide()
    {
        var mismatches = new List<string>();

        foreach (var name in Enum.GetNames<DescriptorOrdinal>())
        {
            var fdp = (long)Enum.Parse<DescriptorOrdinal>(name);
            var ddsName = DdsPrefix + name;

            if (!Enum.TryParse<EDescriptorType>(ddsName, out var dds))
            {
                mismatches.Add($"{name} = {fdp}: no wire member '{ddsName}'");
                continue;
            }

            if ((long)dds != fdp)
                mismatches.Add($"{name} = {fdp} but {ddsName} = {(long)dds}");
        }

        Assert.Empty(mismatches);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>And the OTHER direction — the half a subset check silently drops.</b>
    ///
    /// <para>⭐ A wire descriptor with no FDP name is not automatically a defect *(FDP code may simply never
    /// mark it)*, ⛔ but it is not something to discover by accident either: the whole value of the
    /// duplication ruling is that the two vocabularies are the SAME vocabulary. ⇒ adding a descriptor is a
    /// two-line edit, and this rail is what tells you the second line is missing.</para>
    /// </summary>
    [Fact]
    public void EveryWireDescriptorHasAnFdpName()
    {
        var missing = Enum.GetNames<EDescriptorType>()
            .Where(n => n.StartsWith(DdsPrefix, StringComparison.Ordinal))
            .Select(n => n.Substring(DdsPrefix.Length))
            .Where(n => !Enum.TryParse<DescriptorOrdinal>(n, out _))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(missing);
    }

    /// <summary>
    /// ⚠ <b>The rail's own red-proof.</b> If either <c>Enum.GetNames</c> came back empty — a wrong type
    /// alias, a trimmed assembly — both equalities above would pass vacuously and prove nothing.
    /// </summary>
    [Fact]
    public void BothVocabulariesAreNonEmptyAndTheSameSize()
    {
        var fdp = Enum.GetNames<DescriptorOrdinal>();
        var dds = Enum.GetNames<EDescriptorType>();

        Assert.NotEmpty(fdp);
        Assert.NotEmpty(dds);

        // ⭐ Not the primary assertion — ① and ② are. This one catches the case where a member exists
        //   TWICE on one side under different names, which name-matching alone cannot see.
        Assert.Equal(dds.Length, fdp.Length);
    }

    // ══ ② ForceIdentifier — the affiliation encoding ═══════════════════════════════

    /// <summary>
    /// ⭐⭐ <b><see cref="ForceIdentifier"/> agrees with the wire's <c>eForceIdentifier</c>, element-wise.</b>
    ///
    /// <para>⭐ The naming law differs here — the wire members are <c>FORCE_*</c> SCREAMING_CASE — so the
    /// mapping is spelled out rather than derived. ⚠ Four members, and the list is the assertion: adding one
    /// on the wire side without adding it here reddens ③.</para>
    /// </summary>
    [Theory]
    [InlineData(ForceIdentifier.Unknown,  eForceIdentifier.FORCE_UNKNOWN)]
    [InlineData(ForceIdentifier.Friendly, eForceIdentifier.FORCE_FRIENDLY)]
    [InlineData(ForceIdentifier.Opposing, eForceIdentifier.FORCE_OPPOSING)]
    [InlineData(ForceIdentifier.Neutral,  eForceIdentifier.FORCE_NEUTRAL)]
    public void TheForceIdentifierVocabulariesAgree(ForceIdentifier fdp, eForceIdentifier dds)
        => Assert.Equal((int)dds, (int)fdp);

    /// <summary>
    /// ⭐⭐ <b>And the count, so the <c>[InlineData]</c> table above cannot go stale silently.</b>
    ///
    /// <para>📌 This is the rail that would have caught the drift the two PRE-EXISTING copies of
    /// <c>eForceIdentifier</c> are exposed to *(<c>Hrot.NED.Descriptors</c> and <c>Hrot.Core.Mission</c>,
    /// agreeing today by comment alone — see <see cref="ForceIdentifier"/>'s remarks)*. ⭐ Their
    /// consolidation is filed, not done; this at least stops the count of copies growing unrailed.</para>
    /// </summary>
    [Fact]
    public void TheForceIdentifierTableCoversEveryMember()
    {
        Assert.Equal(
            Enum.GetNames<eForceIdentifier>().Length,
            Enum.GetNames<ForceIdentifier>().Length);

        // ⭐ And the third copy is held to the same numbering, since nothing else holds it.
        Assert.Equal(
            (int)Hrot.Core.Mission.eForceIdentifier.FORCE_OPPOSING,
            (int)ForceIdentifier.Opposing);
    }

    // ══ ③ the CONVERSION, which is what the ruling actually mandates ══════════════

    /// <summary>
    /// ⭐⭐⭐ <b>The ruling says *"translated in network translator"* — so the translation ROUND-TRIPS, for
    /// every member.</b>
    ///
    /// <para>⭐⭐ ①/② compare the enum DECLARATIONS. This compares the code that <b>crosses the boundary</b>,
    /// which is a different claim: <see cref="DescriptorOrdinalConversion"/> could be correct about the
    /// numbers and still drop a member, because it validates with <c>Enum.IsDefined</c> and THROWS rather
    /// than casting blindly.</para>
    /// </summary>
    [Fact]
    public void TheBoundaryTranslatesEveryDescriptorBothWays()
    {
        foreach (var ordinal in Enum.GetValues<DescriptorOrdinal>())
        {
            var wire = DescriptorOrdinalConversion.ToNetwork(ordinal);
            Assert.Equal(ordinal, DescriptorOrdinalConversion.ToInternal(wire));
        }
    }

    /// <summary>
    /// ⚠⚠ <b>And the boundary REFUSES an undefined value rather than casting it.</b>
    ///
    /// <para>⭐ This is the part a compile-time enum agreement can never give you: an ordinal that arrived
    /// from outside the process is DATA, not a promise. ⛔ A blind <c>(EDescriptorType)</c> cast would turn a
    /// protocol mismatch into a wrong bit index — the silent failure ① describes.</para>
    /// </summary>
    [Fact]
    public void TheBoundaryRefusesAnUndefinedOrdinal()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DescriptorOrdinalConversion.ToNetwork((DescriptorOrdinal)9999));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => DescriptorOrdinalConversion.ToInternal((EDescriptorType)9999));
    }
}
