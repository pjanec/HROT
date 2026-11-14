using System;
using Hrot.Editor.DebugApi;
using Hrot.Presentation.DebugApi;
using Xunit;

namespace Hrot.Editor.Tests;

/// <summary>
/// ⭐⭐⭐ <b><c>CE-110</c> — THE CLUSTER API MAY NOT INVENT A TKB CATALOG.</b>
/// 📄 <c>docs/DESIGN_Subsystem_Composition_Unification.md</c> §5.10.
///
/// <para>🔴🔴 <b>The defect, measured <c>2026-08-28</c>.</b> The cluster constructor read
/// <c>_tkbDb = tkbDb ?? new TkbDatabase()</c>, and <c>ClusterRunner/Program.cs:429</c> passes no
/// <c>tkbDb:</c> ⇒ every cluster node served <c>/tkb/*</c> from a <b>private empty database</b>.
/// <c>GET /tkb/types</c> answered <c>[]</c>; <c>GET /tkb/types/303</c> answered
/// <i>"TKB type 303 not found."</i> — ⛔ while <c>HrotNodeBuilder:197</c> had built a real 10-template
/// catalog for that very node at boot.</para>
///
/// <para>⛔⛔ <b>Why this rail exists separately from the seam rails.</b> ⭐ The seam rails
/// *(<c>TheGizmoFeedIsPerPerspectiveTests</c>, the <c>CE-110</c> block)* prove the provider and the
/// dispatcher carry the catalog correctly. ⚠ <b>Neither can see a service that ignores them and
/// substitutes its own.</b> 📌 That substitution was the actual defect, and it is one line.</para>
///
/// <para>⭐⭐⭐ <b>The distinction this pins is EMPTY vs ABSENT, and it is not pedantry.</b> The two
/// sibling instances of this defect at the same line *(<c>BP-487</c>'s gizmo feed, <c>CE-066</c>'s
/// mission editor)* both failed LOUDLY — 404, and a written refusal. ⛔ This one returned a
/// <b>valid-looking empty list</b>, so it was believed: the empty catalog became the leading hypothesis
/// for <c>CE-103</c> *(tanks that render a path and do not move)*, on the reading that the cluster's
/// TKB genuinely differed from the editor's. 📐 With the instrument fixed, the same probe showed all 10
/// shared templates <b>byte-identical</b> on both hosts, refuting it. ⇒ ⭐ <b>an instrument that reports
/// ABSENT where the truth is PRESENT does not merely fail to help — it argues for the wrong root
/// cause</b>, and that is the cost this rail is priced against.</para>
/// </summary>
public sealed class TheClusterApiDoesNotFabricateATkbCatalogTests
{
    private static Fdp.Toolkit.Tkb.TkbDatabase ACatalogWith(params long[] tkbTypes)
    {
        var db = new Fdp.Toolkit.Tkb.TkbDatabase();
        foreach (var t in tkbTypes)
            db.Register(new Fdp.Interfaces.TkbTemplate($"Template{t}", t));
        return db;
    }

    private static PerspectiveScopedDispatcher OneHost(Fdp.Interfaces.ITkbDatabase? catalog)
        => new(
            new ISubsystemDebugProvider[]
            {
                new SubsystemDebugProvider("CGF", "Scenario", tkbDb: () => catalog),
            },
            currentPerspective: () => "Scenario",
            acksPending: null);

    /// <summary>
    /// ⭐⭐⭐ <b>A node that HAS a catalog reports ITS templates</b> — ⛔ not a private empty one.
    /// 📌 This is the exact call that answered <c>[]</c> on a live <c>--mode all</c> boot.
    /// </summary>
    [Fact]
    public void The_cluster_service_reports_the_active_nodes_own_catalog()
    {
        var svc = new DebugApiService(OneHost(ACatalogWith(100, 303)));

        var listed = svc.ListTkbTypes();

        var arr = Assert.IsType<System.Text.Json.Nodes.JsonArray>(listed);
        Assert.Equal(2, arr.Count);

        var found = svc.GetTkbType(303);
        Assert.NotNull(found);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>A node with NO catalog REFUSES — it does not answer with an empty list.</b>
    /// ⛔⛔ The whole finding in one assertion: <c>Assert.Empty(arr)</c> would have passed against the
    /// defect, which is precisely why nothing caught it. ⚠ Ruling 49 — absent-and-explained beats
    /// present-and-broken — and <c>NOT_SUPPORTED_HERE(tkb.read)</c> is the explanation.
    /// </summary>
    [Fact]
    public void A_node_without_a_catalog_refuses_rather_than_answering_empty()
    {
        var svc = new DebugApiService(OneHost(catalog: null));

        var refusal = Assert.Throws<Hrot.Presentation.DebugApi.NotSupportedHereException>(
            () => svc.ListTkbTypes());

        Assert.Contains(DebugCapabilities.TkbRead, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// ⭐⭐ <b>The catalog is re-read per call, so a node that stages a scenario TKB mid-run is
    /// reported.</b> ⚠ <c>TkbLoadClusterStateHandler</c> CLEARS and re-ingests on every
    /// <c>PrepareLive</c>/<c>PrepareEdit</c>; a service that resolved once at construction would serve
    /// the boot catalog forever — ⛔ and being a plausible non-empty list, nobody would suspect it.
    /// </summary>
    [Fact]
    public void The_service_re_reads_the_catalog_so_a_staged_scenario_TKB_appears()
    {
        Fdp.Interfaces.ITkbDatabase? current = ACatalogWith(100);
        var svc = new DebugApiService(new PerspectiveScopedDispatcher(
            new ISubsystemDebugProvider[]
            {
                new SubsystemDebugProvider("SimHost", "SimHost", tkbDb: () => current),
            },
            currentPerspective: () => "SimHost",
            acksPending: null));

        Assert.Single(Assert.IsType<System.Text.Json.Nodes.JsonArray>(svc.ListTkbTypes()));

        current = ACatalogWith(100, 303, 8802);   // the node staged the scenario's TKB

        Assert.Equal(3, Assert.IsType<System.Text.Json.Nodes.JsonArray>(svc.ListTkbTypes()).Count);
    }
}
