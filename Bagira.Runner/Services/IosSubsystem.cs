using Bagira.BDC.SSTD;
using Bagira.BDC.SSTM;
using Bagira.IOS;
using Bagira.IOS.Logic;
using Bagira.IOS.Panels;
using Bagira.IOS.Services;
using Bagira.Runner.Abstractions;
using Bagira.Runner.Models;
using FDP.Toolkit.DER;

namespace Bagira.Runner.Services
{
    /// <summary>
    /// <see cref="ISubsystem"/> implementation that embeds the IOS (Interactive Operations Station).
    ///
    /// <para>Lifecycle:
    /// <list type="number">
    ///   <item><see cref="Initialize"/> — creates <see cref="DerRepo"/>, all IOS panels,
    ///   <see cref="IosLogic"/>, and <see cref="IosMock"/>.</item>
    ///   <item><see cref="Update"/> — delegates to <see cref="IosMock.Update"/>.</item>
    ///   <item><see cref="DrawWorld"/> — no-op (IOS has no 3-D world visuals; all UI is ImGui).</item>
    ///   <item><see cref="DrawUI"/> — delegates to <see cref="IosMock.DrawUI"/>
    ///   (rendered inside <c>rlImGui.Begin()</c>).
    ///   Skipped when <see cref="SubsystemConfig.Headless"/> is <c>true</c>.</item>
    ///   <item><see cref="Shutdown"/> — disposes <see cref="IosMock"/> and underlying logic.</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class IosSubsystem : ISubsystem
    {
        /// <inheritdoc/>
        public string Name => "IOS";

        // Map group 0 = broadcast to all IG instances (matches IosLogicConstants.DefaultMapGroupId).
        private const int DefaultMapGroupId = 0;

        private IosMock?           _mock;
        private bool               _headless;

        /// <inheritdoc/>
        public void Initialize(SubsystemConfig config)
        {
            _headless = config.Headless;

            // ── Construct services ─────────────────────────────────────────────
            // DerRepo takes no external dependencies; node ID uses a fixed default.
            var repo              = new DerRepo();
            var transactionMgr    = new RequestTransactionManager();
            var interactionPanel  = new InteractionPanel();

            var clickQueue     = new ConcurrentEventQueue<MapClickEvent>();
            var selectionQueue = new ConcurrentEventQueue<SelectionChangedEvent>();

            // Null writers — replaced with live DDS writers when DDS is wired in.
            var configWriter       = new NullDdsWriter<MapInteractionConfig>();
            var createEntityWriter = new NullDdsWriter<CreateEntityRequest>();
            var missionCmdWriter   = new NullDdsWriter<MissionControlRequest>();
            var contextMenuWriter  = new NullDdsWriter<ContextActionsUpdate>();

            var missionEditorSvc = new MissionEditorService(repo, missionCmdWriter);
            var contextMenuLogic  = new ContextMenuLogic(contextMenuWriter);

            var logic = new IosLogic(
                repo:                 repo,
                missionEditorService: missionEditorSvc,
                contextMenuLogic:     contextMenuLogic,
                transactionManager:   transactionMgr,
                configWriter:         configWriter,
                createEntityWriter:   createEntityWriter,
                clickQueue:           clickQueue,
                selectionQueue:       selectionQueue,
                interactionPanel:     interactionPanel,
                ingressHandlers:      null,
                mapGroupId:           DefaultMapGroupId);

            _mock = new IosMock(
                logic:            logic,
                configPanel:      new ConfigPanel(),
                orbatPanel:       new OrbatPanel(),
                missionPanel:     new MissionPanel(),
                interactionPanel: interactionPanel,
                spawnerPanel:     new SpawnerPanel());
        }

        /// <inheritdoc/>
        public void Update(float deltaTime)
        {
            _mock?.Update(deltaTime);
        }

        /// <summary>No-op — IOS has no 3-D world visuals; all content is rendered via <see cref="DrawUI"/>.</summary>
        public void DrawWorld() { }

        /// <inheritdoc/>
        /// <remarks>
        /// Renders all IOS ImGui panels (config, orbat, mission, interaction, spawner).
        /// Called inside <c>rlImGui.Begin()</c> by the orchestrator.
        /// No-op in headless mode.
        /// </remarks>
        public void DrawUI()
        {
            if (!_headless)
                _mock?.DrawUI();
        }

        /// <inheritdoc/>
        public void Shutdown()
        {
            _mock?.Dispose();
            _mock = null;
        }

        // ── Null writer (no-op until live DDS is wired) ───────────────────────

        private sealed class NullDdsWriter<T> : IDdsWriter<T>
        {
            /// <inheritdoc/>
            public void Write(T sample) { /* intentional no-op */ }
        }
    }
}
