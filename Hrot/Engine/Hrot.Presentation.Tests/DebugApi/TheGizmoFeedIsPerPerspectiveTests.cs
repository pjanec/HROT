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
