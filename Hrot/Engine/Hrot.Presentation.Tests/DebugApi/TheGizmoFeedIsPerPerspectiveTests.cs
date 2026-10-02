using Fdp.Toolkit.Diagnostics.Gizmos;
using Hrot.Presentation.DebugApi;

namespace Hrot.Presentation.Tests.DebugApi;

/// <summary>
/// ⭐⭐⭐ <b><c>BP-487</c> — the map feed is a PER-PERSPECTIVE capability, and the manifest must measure it.</b>
/// 📄 <c>DESIGN_Subsystem_Composition_Unification.md</c> §5.6 *(the <c>classDiagram</c> these types are drawn
/// in)* · <c>DESIGN_UI_Observability_Snapshot.md</c> STATUS ③ *(where the finding was filed and sat open)*.
///
/// <para>🔴 <b>The defect these rails pin, measured <c>2026-08-27</c>.</b> <c>GET /panels/_gizmo</c> reads a
/// <see cref="DebugPrimitiveBuffer"/> that only <c>EditorSubsystem</c> ever handed to the API service, so
/// <c>--mode all</c> answered <b>404</b> — while <b>CGF, IG and SimHost each drive a buffer of their own</b>.
/// ⛔ Worse, <c>CapabilityManifest</c> hard-coded <c>panels.gizmo = true</c> on <b>every</b> perspective row,
/// on the strength of a comment calling the buffer a *"process-wide static"*. 📐 It is not: one buffer per
/// subsystem, and ExCon has none. ⇒ the manifest advertised a feed that did not answer.</para>
///
/// <para>⭐⭐ <b>Why these are UNIT rails and not only the system one.</b> The
/// <c>The_manifest_describes_this_host_truthfully</c> rail asserts the same claim on a real two-process
/// <c>--mode all</c> boot, which is the real control — ⚠ but it is <c>T3</c>, ~minutes, and async. ⭐ These
/// run in milliseconds and fail on the SEAM rather than on a boot, so a regression is named at the line that
/// caused it. ⛔ Neither replaces the other.</para>
///
/// <para>⚠⚠ <b>TWO providers in every dispatcher rail, deliberately.</b> 🔒 <c>BP-485</c>'s own lesson, from
/// this very feed: <i>"A SINGLETON CANNOT DEMONSTRATE AN ADDRESSING RULE — rail a second instance or the rule
/// is untested by construction."</i> 📌 That is exactly how the gizmo panel's address came to default to its
/// kind and nobody noticed. ⇒ a one-provider rail here would pass whether or not
/// <see cref="PerspectiveScopedDispatcher"/> resolved anything at all.</para>
/// </summary>
public sealed class TheGizmoFeedIsPerPerspectiveTests
{
    private static PerspectiveScopedDispatcher TwoHosts(
        DebugPrimitiveBuffer? drawing, DebugPrimitiveBuffer? notDrawing, string active)
        => new(
            new ISubsystemDebugProvider[]
            {
                // ⭐ The names mirror the real pair the finding was measured on: CGF answers for the
                //   "Scenario" perspective (its key and value differ — the one such entry), ExCon draws
                //   no map at all.
                new SubsystemDebugProvider("CGF",   "Scenario", gizmoBuffer: () => drawing),
                new SubsystemDebugProvider("ExCon", "ExCon",    gizmoBuffer: () => notDrawing),
            },
            currentPerspective: () => active,
            // ⛔ null = "no orchestrator on this host", the dispatcher's own documented meaning. Irrelevant
            //   to the feed, and passing a fake master would imply a gate these rails do not exercise.
            acksPending: null);

    /// <summary>
    /// ⭐⭐ A provider given a buffer reports it, and one given none reports ABSENT — ⛔ not an empty buffer.
    /// 📌 Ruling 49: absent-and-explained beats present-and-broken. An empty feed would read as *"the map
    /// drew nothing this frame"*, which is a completely different claim from *"this host has no map"*.
    /// </summary>
    [Fact]
    public void A_provider_reports_its_own_buffer_and_absence_is_absence()
    {
        var buffer = new DebugPrimitiveBuffer();

        var draws = new SubsystemDebugProvider("CGF", "Scenario", gizmoBuffer: () => buffer);
        var does_not = new SubsystemDebugProvider("ExCon", "ExCon", gizmoBuffer: null);

        Assert.Same(buffer, draws.GizmoBuffer);
        Assert.Null(does_not.GizmoBuffer);

        // ⭐⭐⭐ The capability is MEASURED from the wiring, which is the half CapabilityManifest got wrong.
        Assert.True(draws.DescribeCapabilities()[DebugCapabilities.GizmoFrame]);
        Assert.False(does_not.DescribeCapabilities()[DebugCapabilities.GizmoFrame]);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The accessor is LAZY — the buffer does not exist yet when the provider is built.</b>
    /// 📐 <c>CgfSubsystem._cgfGizmoBuffer</c> is created in <c>Initialize</c> (~<c>:851</c>), and
    /// <c>ClusterRunner/Program.cs</c> builds the providers before that. ⚠⚠ A value-captured provider would
    /// report the feed ABSENT forever — 📌 the exact bug already paid for once with <c>time.drive</c>, which
    /// reported <c>false</c> for the two subsystems that definitely had an adapter.
    /// </summary>
    [Fact]
    public void The_buffer_is_read_late_so_a_provider_built_before_Initialize_still_sees_it()
    {
        DebugPrimitiveBuffer? notYet = null;
        var provider = new SubsystemDebugProvider("CGF", "Scenario", gizmoBuffer: () => notYet);

        // ⛔ Before Initialize: honestly absent.
        Assert.Null(provider.GizmoBuffer);
        Assert.False(provider.DescribeCapabilities()[DebugCapabilities.GizmoFrame]);

        // ⭐ Initialize runs and the subsystem builds its buffer.
        notYet = new DebugPrimitiveBuffer();

        Assert.Same(notYet, provider.GizmoBuffer);
        Assert.True(provider.DescribeCapabilities()[DebugCapabilities.GizmoFrame],
            "the provider latched the buffer at construction, so the manifest will report panels.gizmo "
          + "FALSE for a host that has a feed — the measured time.drive bug, repeated.");
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The dispatcher resolves the ACTIVE perspective's buffer</b> — ⛔ never a host-wide one.
    /// 📌 This is the whole reason the member sits on <see cref="ISubsystemDebugProvider"/> instead of being
    /// a <c>DebugApiService</c> field: <c>--mode all</c> runs CGF <b>and</b> IG <b>and</b> SimHost, each with
    /// its own map, so one latched buffer would answer for whichever was constructed first.
    /// </summary>
    [Fact]
    public void The_dispatcher_answers_with_the_active_perspectives_buffer()
    {
        var cgfBuffer = new DebugPrimitiveBuffer();

        // ⭐ Same two providers, only the ACTIVE perspective differs between the two dispatchers.
        Assert.Same(cgfBuffer, TwoHosts(cgfBuffer, null, active: "Scenario").GizmoBuffer);

        // ⛔ ExCon is active: the feed is absent even though the HOST has one — that is the point.
        Assert.Null(TwoHosts(cgfBuffer, null, active: "ExCon").GizmoBuffer);
    }

    /// <summary>
    /// ⭐⭐ <b>And the two perspectives get DIFFERENT buffers</b>, not the same one twice.
    /// ⚠ Written separately from the rail above because that one is still satisfiable by a dispatcher that
    /// returns *the first provider that has a buffer*: with only one buffer in play, "resolved the active
    /// perspective" and "found any buffer" are indistinguishable. ⛔ Two live buffers separate them.
    /// </summary>
    [Fact]
    public void Two_drawing_hosts_do_not_share_one_feed()
    {
        var cgfBuffer   = new DebugPrimitiveBuffer();
        var otherBuffer = new DebugPrimitiveBuffer();

        Assert.Same(cgfBuffer,   TwoHosts(cgfBuffer, otherBuffer, active: "Scenario").GizmoBuffer);
        Assert.Same(otherBuffer, TwoHosts(cgfBuffer, otherBuffer, active: "ExCon").GizmoBuffer);
    }

    /// <summary>
    /// ⭐⭐ An unknown perspective resolves nothing — ⛔ and must not fall back to *"some provider's"* feed.
    /// ⚠ The fallback shape is legitimate for <c>ClusterState</c> and <c>AvailableScenarios</c> *(one
    /// cluster, one state, cached per node)* and ⛔ WRONG here: each host draws its own map, so answering
    /// with another host's primitives would be a confident lie about what is on screen.
    /// </summary>
    [Fact]
    public void An_unknown_perspective_gets_no_feed_rather_than_someone_elses()
    {
        var cgfBuffer = new DebugPrimitiveBuffer();
        Assert.Null(TwoHosts(cgfBuffer, new DebugPrimitiveBuffer(), active: "NoSuchPerspective").GizmoBuffer);
    }

    // ══ CE-066 — THE MISSION EDITOR, the SAME seam and the SAME defect ═══════════════════════════
    //
    // 🔴 Third instance in one batch: CgfSubsystem builds the SAME shared ScenarioMissionService the editor
    //    builds (:1095 vs EditorSubsystem:1962) and hands it to nobody, while EditorSubsystem:1967 hands
    //    its instance to the debug service. ⇒ all four /missions routes answered "no mission service" on
    //    --mode all, and the routes sat UNCLASSIFIED in CapabilityFor because nobody had asked what a
    //    cluster host answers. ⭐ Classifying and routing were ONE fix.
    // ⚠ These rails are deliberately thinner than the gizmo ones above: the LAZY-read and
    //   ACTIVE-PERSPECTIVE mechanics are the same code path, already pinned there. ⛔ Re-testing the
    //   mechanism per member would be volume, not coverage — what is member-SPECIFIC is the capability
    //   cell and the honest absence.

    /// <summary>
    /// ⭐⭐ The mission cell is MEASURED from the wiring, and absence is absence.
    /// 📌 This is the cell whose ABSENCE from <c>CapabilityFor</c> kept
    /// <c>The_manifest_describes_this_host_truthfully</c> red before its matrix loop for three reports.
    /// </summary>
    [Fact]
    public void A_provider_reports_its_own_mission_editor_and_absence_is_absence()
    {
        var editor = new FakeMissionEditor();

        var hosts   = new SubsystemDebugProvider("CGF", "Scenario", missionEditor: () => editor);
        var doesNot = new SubsystemDebugProvider("IG", "IG", missionEditor: null);

        Assert.Same(editor, hosts.MissionEditor);
        Assert.Null(doesNot.MissionEditor);

        Assert.True(hosts.DescribeCapabilities()[DebugCapabilities.MissionEdit]);
        Assert.False(doesNot.DescribeCapabilities()[DebugCapabilities.MissionEdit]);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The two members are INDEPENDENT.</b> ⛔ A host may draw a map and host no mission editing —
    /// 📐 measured: that is IG and SimHost exactly. ⚠ Written because the cheap way to add a second member
    /// is to derive both from one *"is this host wired?"* flag, which would make the manifest claim mission
    /// editing wherever it claims a map feed. 📌 The `time.drive`/`world.read` pair already proved these
    /// capabilities are genuinely independent, not one bit.
    /// </summary>
    [Fact]
    public void Drawing_a_map_does_not_imply_hosting_mission_editing()
    {
        var mapOnly = new SubsystemDebugProvider(
            "IG", "IG", gizmoBuffer: () => new DebugPrimitiveBuffer(), missionEditor: null);

        var caps = mapOnly.DescribeCapabilities();
        Assert.True(caps[DebugCapabilities.GizmoFrame]);
        Assert.False(caps[DebugCapabilities.MissionEdit]);
    }

    /// <summary>
    /// ⭐⭐ The dispatcher resolves the ACTIVE perspective's editor — ⛔ never another node's.
    /// ⚠ This one matters more than the gizmo equivalent: a mission plan belongs to an entity in a specific
    /// node's world, so committing through the wrong node's editor would WRITE to the wrong world.
    /// </summary>
    [Fact]
    public void The_dispatcher_answers_with_the_active_perspectives_mission_editor()
    {
        var cgf = new FakeMissionEditor();

        PerspectiveScopedDispatcher Dispatcher(string active) => new(
            new ISubsystemDebugProvider[]
            {
                new SubsystemDebugProvider("CGF", "Scenario", missionEditor: () => cgf),
                new SubsystemDebugProvider("IG",  "IG",       missionEditor: null),
            },
            currentPerspective: () => active,
            acksPending: null);

        Assert.Same(cgf, Dispatcher("Scenario").MissionEditor);
        Assert.Null(Dispatcher("IG").MissionEditor);
        Assert.Null(Dispatcher("NoSuchPerspective").MissionEditor);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    // ⭐⭐⭐ CE-110 — THE TKB CATALOG, the THIRD member of this seam and the third instance of ONE
    //     defect at ONE line (ClusterRunner/Program.cs:429). 📄 §5.10.
    //
    // 🔴🔴 What separates this one from the two above, and why it earned its own rails: the gizmo feed
    //     and the mission editor were ABSENT (404 / "no mission service") — loud, and obviously wrong.
    //     ⛔⛔ The catalog was WRONG-BUT-PLAUSIBLE: `_tkbDb = tkbDb ?? new TkbDatabase()` meant
    //     `GET /tkb/types` answered `[]` and `/tkb/types/303` answered "not found" — ⚠⚠ VALID-LOOKING
    //     ANSWERS, which is why they were believed. 📌 Measured 2026-08-28, the empty list was read as
    //     evidence that the cluster's TKB genuinely differed from the editor's, and it became the
    //     leading hypothesis for CE-103 (tanks that will not move). ⇒ ⭐⭐⭐ AN INSTRUMENT THAT REPORTS
    //     ABSENT WHERE THE TRUTH IS PRESENT DOES NOT MERELY FAIL TO HELP — IT ARGUES FOR THE WRONG
    //     ROOT CAUSE. Once fixed, the same probe showed all 10 shared templates IDENTICAL on both
    //     hosts, which refuted the hypothesis outright.
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    private static Fdp.Toolkit.Tkb.TkbDatabase ACatalogWith(params long[] tkbTypes)
    {
        var db = new Fdp.Toolkit.Tkb.TkbDatabase();
        foreach (var t in tkbTypes)
            db.Register(new Fdp.Interfaces.TkbTemplate($"Template{t}", t));
        return db;
    }

    private static PerspectiveScopedDispatcher TwoHostsWithCatalogs(
        Fdp.Interfaces.ITkbDatabase? scenarioHas, Fdp.Interfaces.ITkbDatabase? exConHas, string active)
        => new(
            new ISubsystemDebugProvider[]
            {
                new SubsystemDebugProvider("CGF",   "Scenario", tkbDb: () => scenarioHas),
                new SubsystemDebugProvider("ExCon", "ExCon",    tkbDb: () => exConHas),
            },
            currentPerspective: () => active,
            acksPending: null);

    /// <summary>
    /// ⭐⭐ A provider given a catalog reports it; one given none reports ABSENT — ⛔ never an empty
    /// catalog. 📌 Ruling 49, and here the distinction is the whole finding: an empty catalog reads as
    /// *"this node knows no templates"*, which is a claim about DATA. *"This node has no catalog"* is a
    /// claim about CAPABILITY. ⚠ The old silent default made the second indistinguishable from the first.
    /// </summary>
    // ── CE-171 — the extraction service is per-perspective too ───────────────────

    /// <summary>
    /// <c>CE-171</c>. The debug API used to BUILD its own <c>EntityStateExtractionService</c> from the
    /// active world alone. One built without a <c>ScenarioSerializer</c> silently takes the reflection
    /// path, so every inline fixed array collapses to a single <c>FixedElementField</c> and a
    /// behaviour's decoded <c>BehaviorParameters</c> are lost — on the very node that holds them.
    /// The provider now carries the subsystem's OWN service so the API can prefer it.
    /// </summary>
    [Fact]
    public void A_provider_reports_its_own_extraction_service_and_absence_is_absence()
    {
        var mine = new StubExtraction();

        var has = new SubsystemDebugProvider("CGF", "Scenario", extraction: () => mine);
        var has_not = new SubsystemDebugProvider("ExCon", "ExCon");

        Assert.Same(mine, has.Extraction);
        Assert.Null(has_not.Extraction);
    }

    /// <summary>
    /// Lazy, for the reason every other accessor here is: CGF builds its extraction service during
    /// window registration, long AFTER the composition root builds the provider. A value captured at
    /// construction would be null forever — the exact shape of the defect this fixes.
    /// </summary>
    [Fact]
    public void The_extraction_service_is_read_late_so_a_provider_built_before_it_exists_still_sees_it()
    {
        Fdp.Toolkit.Diagnostics.IEntityStateExtractionService? notYet = null;
        var provider = new SubsystemDebugProvider("CGF", "Scenario", extraction: () => notYet);

        Assert.Null(provider.Extraction);

        var built = new StubExtraction();
        notYet = built;

        Assert.Same(built, provider.Extraction);
    }

    /// <summary>
    /// And the dispatcher must answer with the ACTIVE perspective's service — reading the Brain's
    /// entity through the Muscle's projection would decode nothing, silently.
    /// </summary>
    [Fact]
    public void The_dispatcher_answers_with_the_active_perspectives_extraction_service()
    {
        var brain  = new StubExtraction();
        var muscle = new StubExtraction();
        string current = "Scenario";

        var dispatcher = new PerspectiveScopedDispatcher(
            new[]
            {
                new SubsystemDebugProvider("CGF",     "Scenario", extraction: () => brain),
                new SubsystemDebugProvider("SimHost", "SimHost",  extraction: () => muscle),
            },
            () => current,
            null);

        Assert.Same(brain, dispatcher.Extraction);

        current = "SimHost";
        Assert.Same(muscle, dispatcher.Extraction);
    }

    private sealed class StubExtraction : Fdp.Toolkit.Diagnostics.IEntityStateExtractionService
    {
        public IReadOnlyList<Fdp.Toolkit.Diagnostics.EntityStateDumpDto> ExtractEntities(
            IReadOnlyList<long>? networkIds = null)
            => System.Array.Empty<Fdp.Toolkit.Diagnostics.EntityStateDumpDto>();
    }

    [Fact]
    public void A_provider_reports_its_own_catalog_and_absence_is_absence()
    {
        var catalog = ACatalogWith(100, 303);

        var has = new SubsystemDebugProvider("CGF", "Scenario", tkbDb: () => catalog);
        var has_not = new SubsystemDebugProvider("ExCon", "ExCon", tkbDb: null);

        Assert.Same(catalog, has.TkbDb);
        Assert.Null(has_not.TkbDb);

        // ⭐⭐⭐ MEASURED from the wiring. ⛔ Before CE-110 this cell DID NOT EXIST: CapabilityManifest
        //    classified every /tkb route as the bare string "tkb.read" and no provider ever reported that
        //    key — so the routes were documented while their availability was never checked at all.
        Assert.True(has.DescribeCapabilities()[DebugCapabilities.TkbRead]);
        Assert.False(has_not.DescribeCapabilities()[DebugCapabilities.TkbRead]);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The accessor is LAZY, and for this member that is LOAD-BEARING rather than defensive.</b>
    /// ⚠⚠ <c>TkbLoadClusterStateHandler</c> does not merely populate a catalog late — it <b>CLEARS and
    /// RE-INGESTS</b> it on every <c>PrepareLive</c>/<c>PrepareEdit</c>, swapping in the scenario's own
    /// TKB. ⇒ ⛔ a value-captured provider would report the BOOT catalog forever, so the node's actual
    /// scenario templates would never appear — and, being a non-empty plausible list, nobody would
    /// suspect it. 📌 A strictly worse failure than the <c>time.drive</c> bug that made these accessors
    /// lazy in the first place, because that one at least reported <c>false</c>.
    /// </summary>
    [Fact]
    public void The_catalog_is_read_late_so_a_reingested_TKB_is_seen()
    {
        Fdp.Interfaces.ITkbDatabase? current = null;
        var provider = new SubsystemDebugProvider("SimHost", "SimHost", tkbDb: () => current);

        // ⛔ Before the node stages anything: honestly absent.
        Assert.Null(provider.TkbDb);
        Assert.False(provider.DescribeCapabilities()[DebugCapabilities.TkbRead]);

        // ⭐ Boot: HrotNodeBuilder's HrotEnvironment.CreateTkb() catalog.
        var atBoot = ACatalogWith(100);
        current = atBoot;
        Assert.Same(atBoot, provider.TkbDb);

        // ⭐⭐ PrepareLive: the handler swaps in the scenario's TKB. A latched provider fails HERE.
        var afterLoad = ACatalogWith(100, 303, 8802);
        current = afterLoad;

        Assert.Same(afterLoad, provider.TkbDb);
        Assert.NotSame(atBoot, provider.TkbDb);
    }

    /// <summary>
    /// ⭐⭐⭐ <b>The dispatcher resolves the ACTIVE perspective's catalog</b> — ⛔ never a host-wide one.
    /// ⭐⭐ For this member that is not a precaution but a REQUIREMENT: each node loads its own
    /// scenario-specific TKB from its own staging area, so a single latched catalog would report one
    /// node's templates as every node's. ⚠ Two providers, per this class's <c>BP-485</c> remarks — with
    /// one, the rail would pass whether or not the dispatcher resolved anything.
    /// </summary>
    [Fact]
    public void The_dispatcher_resolves_the_active_perspectives_catalog()
    {
        var cgf = ACatalogWith(303);

        Assert.Same(cgf, TwoHostsWithCatalogs(cgf, null, active: "Scenario").TkbDb);
        Assert.Null(TwoHostsWithCatalogs(cgf, null, active: "ExCon").TkbDb);
        Assert.Null(TwoHostsWithCatalogs(cgf, null, active: "NoSuchPerspective").TkbDb);
    }

    /// <summary>
    /// ⭐⭐⭐ <b><c>SubsystemDebugProvider.TkbFrom</c> reads the WORLD SINGLETON — the same handle every
    /// PRODUCTION reader resolves.</b> 📌 <c>DisEntityTypeTranslator</c>,
    /// <c>EntityPresentationGizmoShared</c> and <c>IgApplication</c> all read it from the world.
    ///
    /// <para>⛔⛔ <b>Why not each subsystem's private field</b> *(CGF holds <c>_context.TkbDb</c>)*: the
    /// API would then be able to report a catalog the node's own systems do NOT consult. ⚠ That is a
    /// subtler version of the very lie CE-110 fixes and a much harder one to notice — the answer would
    /// look right.</para>
    ///
    /// <para>⚠ Also pins the <b>absent</b> arm, which is <c>CE-111</c>'s red-proof: CGF registered no
    /// such singleton *(SimHost and IG both did)*, so before that fix this accessor would have reported
    /// null for the Scenario perspective.</para>
    /// </summary>
    [Fact]
    public void TkbFrom_reads_the_world_singleton_every_production_reader_uses()
    {
        var world   = new Fdp.Core.EntityRepository();
        var catalog = ACatalogWith(100, 303);

        var accessor = SubsystemDebugProvider.TkbFrom(() => world);

        // ⛔ CE-111's shape: a world whose owner never published the singleton.
        Assert.Null(accessor());

        world.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(catalog);
        Assert.Same(catalog, accessor());

        // ⭐ And a subsystem with no world at all (ExCon) is absent, not a throw — composition calls
        //   this during boot, where an exception would take the host down.
        Assert.Null(SubsystemDebugProvider.TkbFrom(() => null)());
    }

    /// <summary>
    /// ⭐ The narrowest possible stand-in: these rails only ever ask *"is it there?"*, so every member
    /// throws. ⛔ A stub that returned plausible values would invite a future rail to assert against
    /// fiction.
    /// </summary>
    private sealed class FakeMissionEditor : Hrot.UI.Common.Facades.IMissionEditorService
    {
        private static T No<T>() => throw new NotSupportedException(
            "FakeMissionEditor exists to be non-null; it models no behaviour.");

        public IReadOnlyList<string> GetAvailableBehaviors(long entityId) => No<IReadOnlyList<string>>();

        public (Hrot.Core.Mission.MissionPlan? Plan, long Version) GetMissionSnapshot(long entityId)
            => No<(Hrot.Core.Mission.MissionPlan?, long)>();

        public Task<Hrot.UI.Common.Models.MissionCommitResult> CommitMissionAsync(
            long entityId, Hrot.Core.Mission.MissionPlan plan, long baseVersion)
            => No<Task<Hrot.UI.Common.Models.MissionCommitResult>>();

        public Task<Hrot.UI.Common.Models.MissionCommitResult> SendControlCommandAsync(
            long entityId, Hrot.Core.Mission.eMissionCommandType type, Guid taskId)
            => No<Task<Hrot.UI.Common.Models.MissionCommitResult>>();
    }
}
