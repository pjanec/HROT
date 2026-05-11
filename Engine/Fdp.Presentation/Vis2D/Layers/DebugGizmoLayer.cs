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
            _renderer = renderer ?? new Fdp.Toolkit.Vis2D.Gizmos.DebugPrimitiveRenderer2D(null, shapeLibrary);
            var imGuiAdapter = new GizmoMap.Presentation.ImGuiPropertyTreeAdapter(schemaRegistry);
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
            _renderer = new Fdp.Toolkit.Vis2D.Gizmos.DebugPrimitiveRenderer2D(view, shapeLibrary);
            var imGuiAdapter = new GizmoMap.Presentation.ImGuiPropertyTreeAdapter(schemaRegistry);
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

        public Entity? PickEntity(Vector2 worldPos) => null;

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
            _innerTerminal.DrawStructInspector((networkId, json) =>
            {
                _eventBus?.PublishManaged(new GizmoStructUpdateEvent
                {
                    AnchorId = networkId,
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

        private static PickToken ToPickToken(GizmoPickToken token)
        {
            // Reconstruct the ECS handle from the multiplexed payload.
            // WARNING: A token.AnchorId of 0 is a perfectly valid ECS Index (Entity 0).
            // Negative values denote canvas clicks or stateless tools, which safely fall through to Entity.Null.
            if (token.AnchorId < 0 || token.AnchorId > int.MaxValue)
                return default;

            return new PickToken
            {
                Target = new Entity((int)token.AnchorId, (ushort)token.StreamId),
                SubElementId = token.SubElementId,
            };
        }

        // Test hooks preserved for existing test surface.
        internal bool TestHook_IsCaptureActive => false;
        internal bool TestHook_IsInteractionActive => false;
    }
}
