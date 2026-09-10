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

        /// <summary>
        /// ⭐⭐⭐ <b>R3 (DESIGN_Gizmo_Renderer_Seam.md §6) — THE ONE BUFFER CONSTRUCTOR.</b>
        ///
        /// <para>⛔⛔ There used to be TWO, byte-identical apart from a fourth parameter: this one takes an
        /// injectable <c>renderer</c>, the other took an <c>ISimulationView? view</c> — <b>and stored it
        /// nowhere.</b> Four production hosts passed a live world into it
        /// (<c>IgApplication</c>, <c>ReplayBrowserSubsystem</c>, <c>SimHostVisualization</c>,
        /// <c>EditorSubsystem</c>), so the signature promised an ECS dependency that no code consumed.</para>
        ///
        /// <para>🔒 That promise is not merely unimplemented, it is <b>designed away</b>:
        /// <c>.dev/_DONE/gizmos-1/feedback2.md:798</c> — the <c>SpatialAnchor</c> two-pass
        /// <i>"completely severs the presentation layer's reliance on the heavy simulation ECS (like
        /// SimTransform or NetworkEntityMap)"</i> — and <c>:871</c> <i>"Eradicating Entity"</i>.
        /// ⭐ <c>EntityLocal</c> resolution goes through <c>SpatialAnchor</c> primitives, which
        /// <c>EntityPresentationGizmo.cs:95</c> really does emit.</para>
        ///
        /// <para>⚠ <b>Deleting the parameter is the point, not tidiness:</b> it is what made
        /// <c>CE-259y</c> conclude <i>"the whole EntityLocal gizmo path is inert"</i> — a finding that
        /// cost real time and was <b>false</b>. A parameter nobody reads is a claim nobody checks.</para>
        ///
        /// <para>⭐ Collapsing the two also removes an ambiguity that deleting the parameter would
        /// otherwise have created: <c>new DebugGizmoLayer(31, buf, bus)</c> would have matched both.</para>
        /// </summary>
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
            _renderer = renderer ?? new Fdp.Toolkit.Vis2D.Gizmos.DebugPrimitiveRenderer2D(shapeLibrary, imGuiAdapter);
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

            // ⛔⛔ R2 (DESIGN_Gizmo_Renderer_Seam.md §6) — `_renderer.SetLayerMask((ushort)ctx
            //   .VisibleLayersMask)` USED TO BE HERE, every frame, and the method was an EMPTY BODY.
            //   🔒 Routing it would have been wrong, not a fix: Architect_Question_28_Map_Layers.md:20
            //     rules the backend's LayerControlMask primitive "the only filter that reaches drawn
            //     primitives", authoritative per frame. ctx.VisibleLayersMask is 32 bits against that
            //     256 and would be a SECOND authority for one fact (ruling 9 / R-132).
            //   ⭐ The LayerBitIndex gate directly above is a DIFFERENT concern — it is whether THIS
            //     LAYER draws at all, which the map canvas legitimately owns. It stays.
            var mapCamera = ctx.Resources.Get<MapCamera>();
            if (mapCamera != null)
                _camera = mapCamera.InnerCamera;

            _innerTerminal.ExtractMetaPrimitives(primitives, _buffer.InternMap);
            _renderer.Render(primitives, ctx);
        }

        /// <summary>
        /// ⭐⭐ <b>Deliberately returns <see langword="false"/> — this layer does not consume
        /// <c>IMapLayer</c> input.</b> <c>MapCanvas.cs:229-252</c> really does offer it to every layer
        /// (and <c>GridMapLayer.cs:94</c> declines the same way); the gizmo layer declines because its
        /// input arrives through <c>_innerTerminal.HandleInput(...)</c> in <see cref="Update"/>, which
        /// polls the hardware directly.
        ///
        /// <para>⛔ <b>Do not "fix" this to make a test pass.</b> 📌 Seven rails in three classes asserted
        /// that it returns <c>true</c> and publishes an event — rails for the pre-terminal input route
        /// (<c>.dev/_DONE/gizmos-1/</c> BATCH-23 era). They never ran, because those classes crashed
        /// first. Wiring a second input mechanism to satisfy them is exactly what ruling 9 forbids.
        /// 📄 docs/DESIGN_Gizmo_Renderer_Seam.md §6 R4.</para>
        /// </summary>
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

        /// <summary>
        /// ⭐⭐⭐ <b>R4 (docs/DESIGN_Gizmo_Renderer_Seam.md §6) — <c>internal</c>, on purpose.</b>
        ///
        /// <para>This is the layer's whole reason for existing on the FDP side: turn a terminal
        /// <c>GizmoPickToken</c> into a <c>PickToken</c> and publish the typed event. ⛔ In production it
        /// is reached only through <c>_innerTerminal.HandleInput(..., OnInteraction)</c> in
        /// <see cref="Update"/>, which polls Raylib — so a headless rail cannot get here through the
        /// front door, and for years the rails tried the back one: <c>layer.HandleInput(...)</c>, which
        /// returns <c>false</c> by design (see its note).</para>
        ///
        /// <para>⭐⭐ Making it <c>internal</c> lets a rail assert the thing that actually matters and is
        /// otherwise UNCOVERED: that a token's <b>payload</b> becomes the right <c>PickToken.Target</c>
        /// (<c>ToPickToken</c>, the S3 payload path of <c>DESIGN_Gizmo_Anchor_Identity.md</c>) and that
        /// each <c>GizmoInteractionEventKind</c> publishes its matching event exactly once.</para>
        /// </summary>
        internal void OnInteraction(
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

        // 🔴🔴 DELETED 2026-09-10 (R4, DESIGN_Gizmo_Renderer_Seam.md §6):
        //     internal bool TestHook_IsCaptureActive     => false;
        //     internal bool TestHook_IsInteractionActive => false;
        //   ⛔⛔ Two HARD-CODED constants whose comment read "Test hooks preserved for existing test
        //     surface." ⇒ every rail asserting `Assert.True(layer.TestHook_...)` was FAILING BY
        //     CONSTRUCTION and every `Assert.False(...)` was VACUOUSLY GREEN. Nobody noticed either,
        //     because those classes SIGSEGV'd before running at all (CE-259aa).
        //   ⭐ R-142 ③ — fix the blindness in place, do not route around it. The real interaction and
        //     capture state lives in GizmoMap.Presentation.DebugGizmoLayer._activeTool, one layer down
        //     and private, behind a HandleInput that polls Raylib.
        //   🔒 So the honest position, stated rather than hidden: that state is NOT railable headlessly
        //     at this layer. What IS railable — and is now railed — is OnInteraction: the token→PickToken
        //     conversion and the event publication. A constant that lets a rail claim otherwise is worse
        //     than an admitted gap.
    }
}
