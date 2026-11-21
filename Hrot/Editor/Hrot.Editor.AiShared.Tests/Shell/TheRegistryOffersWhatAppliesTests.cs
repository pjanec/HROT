using System;
using System.Collections.Generic;
using Hrot.Editor.AiShared.Selection;
using Hrot.Editor.AiShared.Shell;
using Hrot.Editor.AiShared.Variables;
using Xunit;

namespace Hrot.Editor.AiShared.Tests.Shell;

/// <summary>
/// ⭐⭐⭐ <b><c>L1.1</c>'s rail — the registry decides AVAILABILITY and nothing else.</b>
/// 📄 <c>DESIGN_Details_Panel_View_Switching.md</c> §2 *(the <c>classDiagram</c>'s three members)* ·
/// §2b *(the empty-offer sequence and the <c>stateDiagram</c>)* · §3 *(<c>R-98</c>, <c>R-112</c>,
/// <c>R-116</c>, <c>R-117</c>, <c>R-120</c>)*.
/// </summary>
public sealed class TheRegistryOffersWhatAppliesTests
{
    private static DetailsContext Ctx(string perspective = "Blueprint")
        => DetailsContext.Empty(perspective);

    private static DetailsViewDescriptor View(
        string id, int rank, Func<DetailsContext, bool>? applies = null)
        => new(id, id, rank, applies ?? (_ => true), () => new NullInstance());

    private sealed class NullInstance : IDetailsViewInstance
    {
        public void Draw(DetailsContext context, string idScope) { }
        public void Dispose() { }
    }

    // ══ the offer set ════════════════════════════════════════════════════════

    /// <summary>⭐⭐ <b>Only the views whose PREDICATE says yes are offered</b> — 📌 <c>R-116</c>: the
    /// predicate ships with the view, so the registry never learns what a context contains.</summary>
    [Fact]
    public void OnlyTheViewsThatApplyAreOffered()
    {
        var r = new DetailsViewRegistry();
        r.Add(View("yes", 0, _ => true));
        r.Add(View("no",  0, _ => false));

        var offered = r.OfferSet(Ctx());

        Assert.Equal("yes", Assert.Single(offered).Id);
    }

    /// <summary>⭐ <b>Highest <c>Rank</c> first, and <c>Default</c> is the head</b> — 📌 <c>R-98</c>.</summary>
    [Fact]
    public void TheOfferSetIsRankedAndDefaultIsTheHighest()
    {
        var r = new DetailsViewRegistry();
        r.Add(View("low",  1));
        r.Add(View("high", 9));
        r.Add(View("mid",  5));

        var offered = r.OfferSet(Ctx());

        Assert.Equal(new[] { "high", "mid", "low" }, Names(offered));
        Assert.Equal("high", r.Default(Ctx())!.Id);
    }

    /// <summary>
    /// ⛔⛔ <b>EQUAL RANKS KEEP REGISTRATION ORDER — the sort must be STABLE.</b>
    ///
    /// <para>📐 <b>This rail exists because the first implementation was wrong.</b> It used
    /// <c>List.Sort</c>, which is <b>introsort and therefore UNSTABLE</b>, while the doc comment beside
    /// it claimed ties kept registration order. ⇒ ⚠ equal-ranked views would have come back in an order
    /// depending on list internals — ⛔ a default that can flip between runs, which a designer
    /// experiences as the panel changing its mind.</para>
    ///
    /// <para>⚠⚠ <b>THE COUNT IS LOAD-BEARING, and I measured it.</b> 📐 The first version of this rail
    /// used <b>10</b> views and <b>the probe did not redden</b>: <c>List.Sort</c> is introsort, which
    /// delegates partitions <b>below 16 elements to INSERTION SORT</b> — stable in practice. ⇒ ⛔ at
    /// n=10 the rail could not fail, so it was not a rail. ⭐ <b>64</b> forces the quicksort path, and
    /// the probe then reddens. 📌 A rail that cannot fail is not a measurement.</para>
    /// </summary>
    [Fact]
    public void EqualRanks_KeepRegistrationOrder()
    {
        var r = new DetailsViewRegistry();
        var expected = new List<string>();
        for (int i = 0; i < 64; i++)
        {
            var id = $"v{i:D2}";
            expected.Add(id);
            r.Add(View(id, rank: 7));       // ⭐ all identical rank
        }

        Assert.Equal(expected, Names(r.OfferSet(Ctx())));
        Assert.Equal("v00", r.Default(Ctx())!.Id);
    }

    // ══ empty is an ANSWER, not an error ═════════════════════════════════════

    /// <summary>
    /// ⭐⭐⭐ <b>An empty offer set is a REAL, EXPECTED answer</b> — 📄 §2b's first sequence ends
    /// <i>"R--&gt;&gt;D: [] empty"</i> and the shell draws the grey line. 📌 <c>R-117</c>: <i>"a blank
    /// panel is a defect"</i> — ⛔ the defect is the BLANK, not the empty offer.
    /// </summary>
    [Fact]
    public void NothingApplies_IsAnEmptyOfferAndANullDefault()
    {
        var r = new DetailsViewRegistry();
        r.Add(View("never", 0, _ => false));

        Assert.Empty(r.OfferSet(Ctx()));

        // ⛔ null, not a fallback descriptor — a view that claims a context it rejected would lie.
        Assert.Null(r.Default(Ctx()));
    }

    /// <summary>⭐ An empty registry is the same shape — ⛔ not an exception.</summary>
    [Fact]
    public void AnEmptyRegistry_OffersNothing()
    {
        var r = new DetailsViewRegistry();
        Assert.Empty(r.OfferSet(Ctx()));
        Assert.Null(r.Default(Ctx()));
        Assert.Empty(r.All);
    }

    // ══ R-112 — the registry never keys on asset kind ════════════════════════

    /// <summary>
    /// ⭐⭐ <b>The SAME registry answers differently for two contexts</b>, purely through the
    /// predicates. 📌 <c>R-112</c>: <i>"`AssetKind` is never a view key — a host says so in its own
    /// predicate."</i> ⛔ That is the mistake §4 dissolves <c>RuntimeInspectorWindow</c> for
    /// (<c>_panes.Find(p =&gt; p.TargetKind == asset.Kind)</c>).
    /// </summary>
    [Fact]
    public void TheRegistryKeysOnThePredicate_NotOnTheAssetKind()
    {
        var r = new DetailsViewRegistry();
        r.Add(View("bp",  0, c => c.Perspective == "Blueprint"));
        r.Add(View("hsm", 0, c => c.Perspective == "HSM"));

        Assert.Equal("bp",  Assert.Single(r.OfferSet(Ctx("Blueprint"))).Id);
        Assert.Equal("hsm", Assert.Single(r.OfferSet(Ctx("HSM"))).Id);
    }

    // ══ R-120 — descriptors, not instances ═══════════════════════════════════

    /// <summary>
    /// ⭐⭐ <b>Each caller composes its OWN instance</b> — 📌 <c>R-120</c>: <i>"a view owns no shared
    /// state ⇒ no arbitration."</i> ⛔ If the registry cached one instance, two windows showing the same
    /// view would share an edit buffer.
    /// </summary>
    [Fact]
    public void TwoCallers_GetTwoInstances()
    {
        var d = View("v", 0);
        Assert.NotSame(d.Create(), d.Create());
    }

    // ══ a duplicate id is a WIRING bug ═══════════════════════════════════════

    /// <summary>
    /// ⛔⛔ <b>A duplicate id THROWS at registration.</b> ⚠ Not last-wins, not first-wins: both are
    /// silent, and the symptom would be a view that mysteriously never appears. ⭐ Same reasoning as
    /// <c>G4</c>'s duplicate-name guard — an id collision is a wiring bug and must fail at the wiring.
    /// </summary>
    [Fact]
    public void ADuplicateId_ThrowsAtRegistration()
    {
        var r = new DetailsViewRegistry();
        r.Add(View("same", 0));

        var ex = Assert.Throws<InvalidOperationException>(() => r.Add(View("same", 5)));
        Assert.Contains("same", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>⭐ …and the guards fire at registration too, for the same reason.</summary>
    [Fact]
    public void ANullPredicate_FailsWhereItIsWired()
    {
        var r = new DetailsViewRegistry();
        Assert.Throws<ArgumentNullException>(() =>
            r.Add(new DetailsViewDescriptor("id", "t", 0, null!, () => new NullInstance())));
    }

    private static IEnumerable<string> Names(IReadOnlyList<DetailsViewDescriptor> d)
    {
        foreach (var x in d) yield return x.Id;
    }
}
