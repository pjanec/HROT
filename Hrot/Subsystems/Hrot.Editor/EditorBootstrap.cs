using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Orchestration;
using Fdp.Toolkit.Scenario;
using Fdp.Core.Serialization.Migrations;
using Hrot.Common.Scenario;
using Hrot.Common.Scenario.Migrations;
using Hrot.Orchestrator;
using Hrot.ScenarioEditor.Services;

namespace Hrot.Editor;

/// <summary>
/// Static factory for constructing the Editor's core services.
/// The full composition root (Raylib window, module kernel) lives in <c>Program.cs</c>
/// and is wired up in Phase 5 (PACK2-C001).
/// </summary>
public static class EditorBootstrap
{
    /// <summary>
    /// Root directory used for scenario files.
    /// Scenarios are stored as <c>{ScenariosRoot}\{scenarioName}\scenario.json</c>.
    /// </summary>
    /// <remarks>
    /// ⭐⭐⭐ <b><c>CE-346</c> — THE SHARED HELPER, not a hand-combined base.</b>
    /// 🔒 Raised by the user, <c>2026-09-26</c>: <i>"why hosts differ in … scenario sources? I would
    /// expect these to be same in both cgf and editor."</i> — 📐 <b>and they already were, spelled
    /// twice.</b>
    ///
    /// <para>📐 <b>Measured identical by construction:</b> this was
    /// <c>Path.Combine(ClusterConfiguration.Default.NasBasePath, ScenariosDirectoryName)</c>, and
    /// <c>ClusterConfiguration.NasBasePath</c> is initialised to <c>OrchestrationConstants.GetSharedRoot()</c>
    /// (<c>ClusterConfiguration.cs:34</c>) while <c>Default</c> is a get-only property that is never
    /// reassigned (<c>:37</c>, grep-verified). ⇒ both hosts resolved <c>{staging}/shared/scenarios</c>
    /// — CGF through <c>GetSharedScenariosRoot()</c>, the editor through its own <c>Path.Combine</c>.</para>
    ///
    /// <para>⛔ <b>That helper's own summary says it is the one to use:</b> <i>"the root the operator's
    /// scenario list comes from on EVERY host"</i>. ⇒ this was under-adoption of an existing seam —
    /// 🔒 the seam law — and a hand-combined base is the very shape ruling 67 and
    /// <c>TheAssetRootsComeFromTheOneResolverTests</c> exist to stamp out for ASSET roots.
    /// ⚠ Behaviour-preserving: same path, one source. 📄 <c>DESIGN_Occurrence_Scoped_Storage.md</c> §32.20.</para>
    /// </remarks>
    public static string ScenariosRoot => OrchestrationConstants.GetSharedScenariosRoot();

    /// <summary>
    /// Builds a <see cref="ScenarioFileService"/> with an auto-serializer
    /// configured for <c>"Hrot.Scenario"</c> subsystem type.
    /// </summary>
    public static ScenarioFileService CreateFileService()
    {
        // A minimal behavior registry is created here so MissionPlanTranslator can
        // resolve BehaviorId -> BehaviorId on Inject. The editor uses a full registry
        // via EditorSubsystem; this factory creates an empty one consistent with the
        // SimHostApp Muscle-tier pattern (behaviors live on the Brain/CGF node).
        var behaviorRegistry = new BehaviorRegistry();

        var serializer = Hrot.SimHost.Serializers.HrotScenarioSerializerFactory.Build(behaviorRegistry);

        var migrations = HrotMigrationBootstrap.BuildEditor();
        return new ScenarioFileService(serializer, migrationServices: migrations);
    }

    /// <summary>Creates the full Editor MigrationServices bundle.</summary>
    public static MigrationServices CreateMigrationServices() =>
        HrotMigrationBootstrap.BuildEditor();
}
