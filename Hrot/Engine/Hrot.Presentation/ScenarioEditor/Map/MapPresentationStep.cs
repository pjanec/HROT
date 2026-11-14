using System;
using System.Collections.Generic;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Hrot.Common.Infrastructure;

namespace Hrot.ScenarioEditor.Map
{
    /// <summary>
    /// ⭐⭐⭐ CE-3446 (docs/DESIGN_Threaded_Simulation_Presentation.md §15; R-4211) — the map pack's THREADED SPLIT, one implementation for every host
    /// a UI draws: the pack's WORLD WRITERS (the debug-state patch, effect / wreck spawn, lifespan — they create and destroy recorded entities) stay
    /// in the tick (<see cref="NodeModule"/>); every other member (action dispatch, the WHOLE gizmo group, menu orders / shortcuts, hats), the canvas
    /// menu and a host's own UI systems run on the UI thread over A (<see cref="RunPerPulledTick"/>, <see cref="RunEveryFrame"/>).
    /// ⛔ No split inside the gizmo group. 📐 Was <c>EditorSubsystem.NodeSideInteractionSystems</c> / <c>RunInteractionBlock</c>.
    /// </summary>
    public sealed class MapPresentationStep
    {
        private readonly MapInteraction _mi;
        private readonly StagedPresentation _p;
        private readonly string? _feedAddress;
        private readonly IEcsModuleSystem[] _nodeSide;
        private readonly List<IEcsModuleSystem> _ui = new();
        private readonly List<IEcsModuleSystem> _hostUi = new();
        private readonly List<IEcsModuleSystem> _hostUiFirst = new();
        private Func<IReadOnlyList<IEcsModuleSystem>?>? _lateHostUi;
        private IReadOnlyList<IEcsModuleSystem>? _late;
        private IEcsModuleSystem? _contextIngress;
        private Entity[] _rows = new Entity[64];
        private long _faults;

        /// <param name="gizmoFeedAddress">Where the host publishes its gizmo feed for the panels (null: it publishes none).</param>
        public MapPresentationStep(MapInteraction interaction, StagedPresentation presentation, string? gizmoFeedAddress)
        {
            _mi = interaction ?? throw new ArgumentNullException(nameof(interaction));
            _p = presentation ?? throw new ArgumentNullException(nameof(presentation));
            _feedAddress = gizmoFeedAddress;
            var node = new List<IEcsModuleSystem>();
            foreach (var sys in interaction.InteractionSystems)
            {
                bool worldWriter = ReferenceEquals(sys, interaction.DebugStatePatch) || ReferenceEquals(sys, interaction.EffectSpawn)
                                   || ReferenceEquals(sys, interaction.WreckSpawn) || ReferenceEquals(sys, interaction.Lifespan);
                if (worldWriter) node.Add(sys); else _ui.Add(sys);
            }
            _nodeSide = node.ToArray();
        }

        /// <summary>The selection whose rows ride to A with every publish (all their components copied). Null: none.</summary>
        public Func<Fdp.Toolkit.Vis2D.Abstractions.ISelectionState?>? Selection { get; set; }

        /// <summary>The faults the UI systems threw (each logged, the first 20).</summary>
        public long Faults => System.Threading.Interlocked.Read(ref _faults);

        /// <summary>The members the TICK runs (the world writers).</summary>
        public IReadOnlyList<IEcsModuleSystem> NodeSideSystems => _nodeSide;

        /// <summary>The members the UI runs over A.</summary>
        public IReadOnlyList<IEcsModuleSystem> UiSystems => _ui;

        /// <summary>The interaction module the kernel registers: the world writers on a bus of their own that nothing else touches.</summary>
        public Hrot.Common.Interactions.GizmoInteractionModule NodeModule()
            => new(new FdpEventBus(), contextIngress: null, interactionSystems: _nodeSide, gizmoIngress: null, gizmoEgress: null);

        /// <summary>A host's own UI system (its selection systems, its gizmo network ingress / egress), run after the pack's, in order.</summary>
        public void AddHostUiSystem(IEcsModuleSystem system) => _hostUi.Add(system ?? throw new ArgumentNullException(nameof(system)));

        /// <summary>A host's own UI system that runs BEFORE the pack's (its gizmo network ingress: what it puts on the interaction bus is read this block).</summary>
        public void AddHostUiSystemFirst(IEcsModuleSystem system) => _hostUiFirst.Add(system ?? throw new ArgumentNullException(nameof(system)));

        /// <summary>A host's own UI systems that exist only later (built at the first block).</summary>
        public void AddLateHostUiSystems(Func<IReadOnlyList<IEcsModuleSystem>?> build) => _lateHostUi = build;

        /// <summary>
        /// UI thread, once per pulled tick, over A: the gizmo feed published and the frame ended, the interaction bus swapped, the context ingress
        /// (over A's entity map), the pack's UI systems (the gizmo group whole), the host's, then the gizmo frame published for drawing; the
        /// selection's rows ride to the staging so their every component is copied.
        /// </summary>
        public void RunPerPulledTick(float dt)
        {
            var a = _p.World;
            using var trace = AccessTrace.Scope("ui: interaction block (gizmos, selection, tools)", TraceContext.Presentation);
            var buffer = _mi.Buffer;
            if (_feedAddress != null) Fdp.Diagnostics.Contracts.Panels.GizmoFramePanel.Publish(buffer, _feedAddress);
            buffer.EndFrame(dt);
            _mi.InteractionBus.SwapBuffers();
            for (int i = 0; i < _hostUiFirst.Count; i++) Run(_hostUiFirst[i], a, dt);
            _contextIngress ??= new Hrot.Common.Systems.ContextActionIngressSystem(_p.Map, _mi.InteractionBus);
            Run(_contextIngress, a, dt);
            for (int i = 0; i < _ui.Count; i++) Run(_ui[i], a, dt);
            for (int i = 0; i < _hostUi.Count; i++) Run(_hostUi[i], a, dt);
            if (_late == null && _lateHostUi != null) _late = _lateHostUi();
            if (_late != null) for (int i = 0; i < _late.Count; i++) Run(_late[i], a, dt);
            buffer.PublishFrame();
            int n = 0;
            if (Selection?.Invoke()?.SelectedEntities is { } selected)
            {
                if (selected is List<Entity> list)   // ⭐ R-2084 — the usual case: an index loop, no boxed enumerator per frame
                    for (int i = 0; i < list.Count; i++) Add(list[i]);
                else foreach (var e in selected) Add(e);
            }
            _p.Staging.SetRows(new ReadOnlySpan<Entity>(_rows, 0, n));

            void Add(Entity e)
            {
                if (n == _rows.Length) Array.Resize(ref _rows, n * 2);
                _rows[n++] = e;
            }
        }

        /// <summary>UI thread, every frame after the pull, over A: the canvas menu's state (UI state on the presentation side).</summary>
        public void RunEveryFrame(float dt) => Run(_mi.CanvasMenu, _p.World, dt);

        private void Run(IEcsModuleSystem system, EntityRepository world, float dt)
        {
            try { system.Execute(world, dt); }
            catch (Exception ex)
            {
                if (System.Threading.Interlocked.Increment(ref _faults) <= 20) Console.WriteLine("[STAGED] map presentation fault: " + ex);
            }
        }
    }
}
