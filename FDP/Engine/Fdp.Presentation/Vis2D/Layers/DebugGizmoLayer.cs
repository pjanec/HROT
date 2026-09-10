using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Components;
using GizmoMap.Network;
using Raylib_cs;
using AbstractionMouseButton = Fdp.Toolkit.Vis2D.Abstractions.MapMouseButton;
using AbstractionKeyboardKey = Fdp.Toolkit.Vis2D.Abstractions.MapKeyboardKey;
using InteractionMouseButton = Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton;
using InteractionKeyboardKey = Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey;

namespace Fdp.Toolkit.Vis2D.Layers
{
    public class DebugGizmoLayer : IMapLayer
    {
        public string Name => "Debug Gizmos";
        public int LayerBitIndex { get; private set; }

        private readonly DebugPrimitiveBuffer? _buffer;
        private readonly FdpEventBus? _eventBus;
        private readonly Fdp.Toolkit.Vis2D.Gizmos.DebugPrimitiveRenderer2D _renderer;
        private readonly GizmoMap.Presentation.DebugGizmoLayer _innerTerminal;
        private readonly MapCamera? _mapCamera;
        private Camera2D _camera;

        public DebugGizmoLayer(int layerBitIndex = 31)
        {
            LayerBitIndex = layerBitIndex;
            _renderer = new Fdp.Toolkit.Vis2D.Gizmos.DebugPrimitiveRenderer2D();
            _innerTerminal = new GizmoMap.Presentation.DebugGizmoLayer(
                new GizmoMap.Presentation.DebugPrimitiveRenderer2D());
        }

        public DebugGizmoLayer(
            int layerBitIndex,
            DebugPrimitiveBuffer buffer,
            FdpEventBus eventBus,
            Fdp.Toolkit.Vis2D.Gizmos.DebugPrimitiveRenderer2D? renderer = null,
            MapCamera? camera = null,
            GizmoMap.Presentation.Shapes.IEntityShapeLibrary? shapeLibrary = null,
            GizmoMap.Presentation.GizmoSchemaRegistry? schemaRegistry = null)
        {
            LayerBitIndex = layerBitIndex;
            _buffer = buffer;
            _eventBus = eventBus;
            _mapCamera = camera;
            var imGuiAdapter = new GizmoMap.Presentation.ImGuiPropertyTreeAdapter(schemaRegistry);
            _renderer = renderer ?? new Fdp.Toolkit.Vis2D.Gizmos.DebugPrimitiveRenderer2D(null, shapeLibrary, imGuiAdapter);
            var innerRenderer = new GizmoMap.Presentation.DebugPrimitiveRenderer2D(null, imGuiAdapter);
            _innerTerminal = new GizmoMap.Presentation.DebugGizmoLayer(
                innerRenderer);
        }

        public DebugGizmoLayer(
            int layerBitIndex,
            DebugPrimitiveBuffer buffer,
            FdpEventBus eventBus,
            Fdp.ModuleHost.Abstractions.ISimulationView? view,
            MapCamera? camera = null,
            GizmoMap.Presentation.Shapes.IEntityShapeLibrary? shapeLibrary = null,
            GizmoMap.Presentation.GizmoSchemaRegistry? schemaRegistry = null)
        {
            LayerBitIndex = layerBitIndex;
            _buffer = buffer;
            _eventBus = eventBus;
            _mapCamera = camera;
            var imGuiAdapter = new GizmoMap.Presentation.ImGuiPropertyTreeAdapter(schemaRegistry);
            _renderer = new Fdp.Toolkit.Vis2D.Gizmos.DebugPrimitiveRenderer2D(view, shapeLibrary, imGuiAdapter);
            var innerRenderer = new GizmoMap.Presentation.DebugPrimitiveRenderer2D(null, imGuiAdapter);
            _innerTerminal = new GizmoMap.Presentation.DebugGizmoLayer(
                innerRenderer);
        }

        public void Update(float dt)
        {
            if (_buffer == null) return;
            if (_mapCamera != null)
                _camera = _mapCamera.InnerCamera;

            _innerTerminal.HandleInput(
                _buffer.GetFrame(),
                _buffer.InternMap,
                _camera,
                OnInteraction);
        }

        public void Draw(RenderContext ctx)
        {
            if (_buffer == null) return;
            var primitives = _buffer.GetFrame();

            if (LayerBitIndex >= 0 && LayerBitIndex < 32)
            {
                if ((ctx.VisibleLayersMask & (1u << LayerBitIndex)) == 0) return;
            }

            _renderer.SetLayerMask((ushort)ctx.VisibleLayersMask);

            var mapCamera = ctx.Resources.Get<MapCamera>();
            if (mapCamera != null)
                _camera = mapCamera.InnerCamera;

            _innerTerminal.ExtractMetaPrimitives(primitives, _buffer.InternMap);
            _renderer.Render(primitives, ctx);
        }

        // FDP Inputs are muted. Raw input is polled by the inner terminal.
        public bool HandleInput(Vector2 worldPos, AbstractionMouseButton button, bool isPressed) => false;
        public void HandleHover(Vector2 mouseWorldPos) { }
        public bool HandleDrag(Vector2 worldPos, Vector2 delta) => false;
        public bool HandleKeyInput(AbstractionKeyboardKey key) => false;

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-259p</c> — THE ENTITY HIT-TEST, implemented for real.</b>
        /// 📄 <c>docs/UX/UX_Feature_Tool_Model.md</c> §4.7g.
        ///
        /// <para>🔴 <b>Measured `2026-09-09`, by an operator who could not complete a pick:</b> EVERY
        /// production <c>IMapLayer.PickEntity</c> in the repo returned <c>null</c> — <c>GridMapLayer</c>,
        /// <c>PerceptionMapLayer</c>, <c>SelectionRenderSystem</c>, both SimHost layers, and this one. The
        /// only real implementation lived in an EXAMPLES project. ⇒ <c>MapCanvas.PickTopmostEntity</c> was
        /// dead in production, and <c>EntityPickerGizmo</c> — whose entire pick arm is gated on it — could
        /// never pick anything.</para>
        ///
        /// <para>⭐⭐ <b>The seam law:</b> there were TWO entity hit-tests and the picker used the dead one.
        /// The LIVE one is the terminal's own <c>FindTopmostInteractivePrimitive</c> — the mechanism that
        /// makes ordinary SELECTION work (<c>SelectionInteractionSystem</c> resolves
        /// <c>GizmoInteractionStartedEvent.Token.Target</c>). ⇒ this routes to that, rather than adding a
        /// third.</para>
        ///
        /// <para>⚠ The hit-test is deliberately NOT filtered by the capture binding — see
        /// <c>GizmoMap.Presentation.DebugGizmoLayer.PickTopmostEntityAnchor</c>: <i>"only the capture
        /// holder receives interactions"</i> and <i>"the capture holder may hit-test"</i> are compatible
        /// claims, and conflating them is what blinded the picker.</para>
        /// </summary>
        public Entity? PickEntity(Vector2 worldPos)
        {
            if (_buffer == null) return null;

            // ⚠ The camera is refreshed in Update(); a pick can arrive before the first frame, so fall
            //   back to the live one rather than hit-testing against a default zoom of 0.
            float zoom = _mapCamera?.InnerCamera.Zoom ?? _camera.Zoom;
            if (zoom <= 0f) return null;

            var anchor = GizmoMap.Presentation.DebugGizmoLayer.PickTopmostEntityAnchor(
                _buffer.GetFrame(), worldPos, zoom);

            return anchor is { } a ? new Entity(a.Index, a.Generation) : null;
        }

        /// <summary>
        /// Resolver used to render colored icons for right-click context-menu items that carry an
        /// <c>"icon"</c> key. Forwarded to the inner terminal layer. Injected by the host.
        /// </summary>
        public GizmoMap.Presentation.MenuIconResolver? ContextMenuIconResolver
        {
            get => _innerTerminal.ContextMenuIconResolver;
            set => _innerTerminal.ContextMenuIconResolver = value;
        }

        public void DrawContextMenu()
        {
            _innerTerminal.DrawContextMenu((token, actionId) =>
            {
                _eventBus?.Publish(new GizmoMenuActionEvent
                {
                    AnchorId = token.AnchorId,
                    ActionId = actionId,
                });
            });
        }

        public void DrawStructInspector()
        {
            _innerTerminal.DrawStructInspector((networkId, gizmoTypeId, json) =>
            {
                _eventBus?.PublishManaged(new GizmoStructUpdateEvent
                {
                    AnchorId = networkId,
                    GizmoTypeId = gizmoTypeId,
                    PayloadJson = json,
                });
            });
        }

        /// <summary>
        /// Returns main-menu items contributed by gizmos via <see cref="DebugPrimitiveShape.MainMenuBinding"/>
        /// primitives during the most recent <see cref="Draw"/> call, then clears internal state.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<Fdp.Toolkit.Diagnostics.Gizmos.Interaction.ContextMenuItemDto> ConsumeMainMenu()
            => _innerTerminal.ConsumeMainMenu();

        private void OnInteraction(
            GizmoPickToken token,
            GizmoInteractionEventKind kind,
            Vector3 worldPos,
            int actionId,
            byte stateFlags)
        {
            if (_eventBus == null) return;

            var pickToken = ToPickToken(token);
            switch (kind)
            {
                case GizmoInteractionEventKind.Started:
                    _eventBus.Publish(new GizmoInteractionStartedEvent
                    {
                        Token = pickToken,
                        WorldPos = worldPos,
                    });
                    break;
                case GizmoInteractionEventKind.DragUpdate:
                    _eventBus.Publish(new GizmoDragUpdateEvent
                    {
                        Token = pickToken,
                        WorldPos = worldPos,
                        Space = pickToken.IsValid ? CoordinateSpace.EntityLocal : CoordinateSpace.World,
                    });
                    break;
                case GizmoInteractionEventKind.Commit:
                    _eventBus.Publish(new GizmoInteractionCommitEvent
                    {
                        Token = pickToken,
                        WorldPos = worldPos,
                        Space = pickToken.IsValid ? CoordinateSpace.EntityLocal : CoordinateSpace.World,
                    });
                    break;
                case GizmoInteractionEventKind.Cancel:
                    _eventBus.Publish(new GizmoInteractionCancelEvent
                    {
                        Token = pickToken,
                    });
                    break;
                case GizmoInteractionEventKind.MenuAction:
                    _eventBus.Publish(new GizmoMenuActionEvent
                    {
                        AnchorId = token.AnchorId,
                        ActionId = actionId,
                    });
                    break;
                case GizmoInteractionEventKind.RawInput:
                {
                    bool isMouse = (stateFlags & 0x80) != 0;
                    bool isPressed = (stateFlags & 0x01) != 0;
                    if (isMouse)
                    {
                        _eventBus.Publish(new GizmoMouseEvent
                        {
                            Token = pickToken,
                            Button = (InteractionMouseButton)actionId,
                            IsPressed = isPressed,
                            WorldPos = worldPos,
                        });
                    }
                    else
                    {
                        _eventBus.Publish(new GizmoKeyEvent
                        {
                            Token = pickToken,
                            Key = (InteractionKeyboardKey)actionId,
                            IsPressed = isPressed,
                        });
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// ⭐⭐⭐ S3/S5 (DESIGN_Gizmo_Anchor_Identity.md §6) — REBUILD THE HANDLE FROM THE TOKEN'S PAYLOAD.
        ///
        /// <para>⭐ <c>token.AnchorId</c> is the IDENTITY (a network id) and is deliberately NOT used here:
        /// <c>AnchorIndex</c> + <c>StreamId</c> are an in-process payload the producer already had, so this
        /// needs no lookup and no map. ⛔ That matters — <c>ReplayBrowser</c> composes
        /// <c>SelectionInteractionSystem</c> and has NO <c>NetworkEntityMap</c>, so resolving here would
        /// silently drop its selection.</para>
        ///
        /// <para>⛔ A canvas click or a stateless tool has no entity: <c>AnchorGeneration</c> is 0, so
        /// <c>Entity.Null</c> results and <c>PickToken.IsValid</c> reports invalid. ⚠ The WIRE never
        /// carries this payload — S1/S2 resolve at the translators, where a process-local handle is
        /// meaningless.</para>
        /// </summary>
        private static PickToken ToPickToken(GizmoPickToken token)
        {
            if (token.StreamId == 0) return default;   // no live local entity anchor

            return new PickToken
            {
                Target       = new Entity(token.AnchorIndex, (ushort)token.StreamId),
                SubElementId = token.SubElementId,
                GizmoTypeId  = token.GizmoTypeId,
            };
        }

        // Test hooks preserved for existing test surface.
        internal bool TestHook_IsCaptureActive => false;
        internal bool TestHook_IsInteractionActive => false;
    }
}
