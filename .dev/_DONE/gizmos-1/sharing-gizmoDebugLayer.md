what is the DebugGizmoLayer ? how does it relate to the GizmoMap.Viewer rendering and UI event processing layer? I guess these are those "dumb-teminals" for the gizmo primitive streams and should be implemented identically. Thay should share the sources in a DRY way. Can we put the shared parts under GizmoMap? The dumb-terminal should not need to know anything about HROT or FDP, all is just gizmo orientead and bound to raylib and imgui. So most of the code could be sharable and could live under GizmoMap.

There are actually two versions of `DebugGizmoLayer` currently existing in the codebase, and your intuition about extracting a shared "dumb terminal" layer correctly identifies a DRY violation that needs to be addressed. 

The architecture intends for the hit-testing and primitive rendering logic to be completely decoupled from the ECS and domain logic, residing entirely within the standalone `GizmoMap` boundary.

Here is the exact breakdown of how this is currently structured and how we should consolidate it.

### 1. The Pure Dumb Terminal (`GizmoMap.Presentation.DebugGizmoLayer`)
You are completely correct that the dumb terminal should know nothing about HROT or FDP. This pure implementation already exists as `GizmoMap.Presentation.DebugGizmoLayer`. 

This layer handles all the raw presentation logic:
*   It iterates over the `GizmoPrimitiveBuffer` to hit-test `Box2D` primitives against Raylib mouse coordinates.
*   It scans the frame for `InputCaptureBinding` meta-primitives to enforce exclusive hardware input capture.
*   It reads `ContextMenuBinding` primitives, resolves the JSON hash against the local intern map, and schedules the ImGui popup.

Because it lives in `GizmoMap.Presentation`, it references only `Raylib-cs`, `rlImgui-cs`, and `ImGui.NET`. It has zero dependencies on `Fdp.Core` or `Hrot`. Instead of publishing ECS events, it routes interactions back to the host via a generic `Action<GizmoPickToken, GizmoInteractionEventKind, Vector3, int, byte>` delegate.

**Relation to `GizmoMap.Viewer`:**
`GizmoMap.Viewer` is built entirely on this pure layer. It initializes a CycloneDDS participant, creates the buffer, and runs `GizmoViewerFrontend`. When the `DebugGizmoLayer` detects a click or a drag, it fires the delegate, and the viewer instantly maps those arguments into a `GizmoInteractionBatch` DDS packet and writes it to the network.

### 2. The Engine Wrapper (`Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer`)
In the FDP engine tier, `Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer` implements `IMapLayer` so it can participate in the `MapCanvas` update loop. Its responsibility is to translate presentation-tier UI interactions into semantic FDP events. 

However, instead of wrapping the `GizmoMap.Presentation` layer, it currently duplicates the hit-testing, hardware capture evaluation, and right-click context-menu resolution. It manually evaluates the mouse coordinates and then publishes `GizmoInteractionStartedEvent`, `GizmoDragUpdateEvent`, and `GizmoMouseEvent` directly to the `FdpEventBus`.

### The Architectural Fix (Achieving DRY)
To achieve the DRY, pure-component design you described, we must refactor `Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer` so that it acts strictly as an adapter. 

We can gut the duplicated spatial intersection logic from the FDP layer and have it internally instantiate `GizmoMap.Presentation.DebugGizmoLayer`. 

The FDP layer would simply provide the `onInteraction` delegate to the inner layer. When the delegate fires, the FDP layer performs a simple `switch` on the `GizmoInteractionEventKind` enum and publishes the corresponding typed `FdpEventBus` struct (e.g., mapping `GizmoInteractionEventKind.Started` to `GizmoInteractionStartedEvent`). 

This consolidates all Raylib logic, spatial math, and hit-box evaluation strictly under the `GizmoMap` boundary, ensuring the IG application, the CGF subsystem, and the standalone Viewer execute identical, heavily-tested UI event processing code.




pls detail this Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer refactor


To achieve a clean, DRY architecture where the pure "dumb terminal" logic is strictly quarantined within the `GizmoMap.Presentation` assembly, we will decouple the terminal from the concrete primitive buffer and wrap it inside the FDP engine layer. 

By executing this refactor, your hit-testing, hardware input capture, rubber-band background drags, and context-menu resolutions will exist in exactly one place. The FDP engine layer will simply act as a domain-adapter, mapping the terminal's events back into the `FdpEventBus`.

Here is the exact execution plan.

### 1. Refactor the Dumb Terminal (`GizmoMap.Presentation`)
Open `FDP/ExtDeps/GizmoMap/GizmoMap.Presentation/Layers/DebugGizmoLayer.cs`. 
We will completely detach it from the concrete `GizmoPrimitiveBuffer`. We will also carry over the universal hit-testing and background rubber-band drag logic we previously built.

Replace the entire class with this pure, buffer-agnostic implementation:
```csharp
using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Interaction;
using GizmoMap.Network;
using Raylib_cs;

namespace GizmoMap.Presentation
{
    public sealed class DebugGizmoLayer
    {
        private readonly DebugPrimitiveRenderer2D _renderer;
        private GizmoInteractionProxyTool? _activeTool;
        private readonly ContextMenuAdapter _contextMenuAdapter = new();
        private const float HitRadiusWorld = 5f;

        public DebugGizmoLayer(DebugPrimitiveRenderer2D renderer)
        {
            _renderer = renderer;
        }

        public void Render(ReadOnlySpan<DebugPrimitive> primitives, Camera2D camera, float zoom)
        {
            _renderer.Render(primitives, camera, zoom);
        }

        public void HandleInput(
            ReadOnlySpan<DebugPrimitive> primitives,
            StringInternMap internMap,
            Camera2D camera,
            Action<GizmoPickToken, GizmoInteractionEventKind, Vector3, int, byte>? onInteraction = null)
        {
            var screenPos = Raylib.GetMousePosition();
            var worldPos  = Raylib.GetScreenToWorld2D(screenPos, camera);
            var worldPos3 = new Vector3(worldPos.X, worldPos.Y, 0f);

            // 1. Evaluate Exclusive Capture
            for (int i = 0; i < primitives.Length; i++)
            {
                ref readonly var prim = ref primitives[i];
                if (prim.Shape == DebugPrimitiveShape.InputCaptureBinding && prim.ConditionMask == 1u)
                {
                    var captureToken = new GizmoPickToken { AnchorId = prim.InspNetworkId, SubElementId = prim.SubElementId };

                    var delta = Raylib.GetMouseDelta();
                    if (delta.X != 0 || delta.Y != 0)
                        onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.DragUpdate, worldPos3, 0, 0);

                    if (Raylib.IsMouseButtonPressed(MouseButton.Left))
                        onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput, worldPos3, (int)MapMouseButton.Left, 0x81);
                    else if (Raylib.IsMouseButtonReleased(MouseButton.Left))
                        onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput, worldPos3, (int)MapMouseButton.Left, 0x80);

                    if (Raylib.IsMouseButtonPressed(MouseButton.Right))
                        onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput, worldPos3, (int)MapMouseButton.Right, 0x81);
                    else if (Raylib.IsMouseButtonReleased(MouseButton.Right))
                        onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput, worldPos3, (int)MapMouseButton.Right, 0x80);

                    if (Raylib.IsKeyPressed(KeyboardKey.Escape))
                        onInteraction?.Invoke(captureToken, GizmoInteractionEventKind.RawInput, worldPos3, (int)MapKeyboardKey.Escape, 0x01);

                    return;
                }
            }

            // 2. Start new interactions (Left Press)
            if (_activeTool == null && Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                var best = FindTopmostInteractivePrimitive(primitives, worldPos, camera.Zoom);
                if (best.HasValue)
                {
                    var token = new GizmoPickToken { 
                        AnchorId = best.Value.BoxAnchorId != 0 ? best.Value.BoxAnchorId : (best.Value.InspNetworkId != 0 ? best.Value.InspNetworkId : best.Value.AnchorIndex), 
                        SubElementId = best.Value.SubElementId 
                    };
                    _activeTool = new GizmoInteractionProxyTool(token, worldPos, onInteraction, () => _activeTool = null, best.Value.Space);
                    _activeTool.HandlePress(worldPos, MouseButton.Left);
                }
                else
                {
                    // Fallback for rubber-band selection (empty space click)
                    _activeTool = new GizmoInteractionProxyTool(default, worldPos, onInteraction, () => _activeTool = null);
                    _activeTool.HandlePress(worldPos, MouseButton.Left);
                }
            }

            // 3. Handle Right Clicks for Context Menus
            if (_activeTool == null && Raylib.IsMouseButtonReleased(MouseButton.Right))
            {
                var menuBindings = new Dictionary<long, uint>();
                foreach (ref readonly var prim in primitives)
                    if (prim.Shape == DebugPrimitiveShape.ContextMenuBinding)
                        menuBindings[prim.InspNetworkId] = prim.StringHash;

                var best = FindTopmostInteractivePrimitive(primitives, worldPos, camera.Zoom);
                long hitEntityId = best?.InspNetworkId ?? best?.AnchorIndex ?? -1L; // Fallback to canvas anchor

                if (hitEntityId != 0 && menuBindings.TryGetValue(hitEntityId, out uint menuHash))
                {
                    string? json = internMap.TryResolve(menuHash);
                    if (json != null) _contextMenuAdapter.Schedule(hitEntityId, json);
                }
            }

            // 4. Drive active tool
            if (_activeTool != null)
            {
                if (Raylib.IsMouseButtonReleased(MouseButton.Left)) _activeTool.HandleRelease(worldPos, MouseButton.Left);
                else if (Raylib.IsMouseButtonPressed(MouseButton.Right)) _activeTool.HandlePress(worldPos, MouseButton.Right);
                else if (Raylib.IsMouseButtonReleased(MouseButton.Right)) _activeTool.HandleRelease(worldPos, MouseButton.Right);
                
                var delta = Raylib.GetMouseDelta();
                if (delta.X != 0 || delta.Y != 0) _activeTool.HandleDrag(worldPos, delta);

                if (Raylib.IsKeyPressed(KeyboardKey.Escape)) _activeTool.HandleKeyPressed(KeyboardKey.Escape);
            }
        }

        public void DrawContextMenu(Action<GizmoPickToken, int>? onMenuAction = null)
        {
            _contextMenuAdapter.DrawScheduled((anchorId, actionId) =>
                onMenuAction?.Invoke(new GizmoPickToken { AnchorId = anchorId }, actionId));
        }

        private DebugPrimitive? FindTopmostInteractivePrimitive(ReadOnlySpan<DebugPrimitive> primitives, Vector2 testPos, float zoom)
        {
            DebugPrimitive? best = null;
            foreach (ref readonly var prim in primitives)
            {
                if (prim.Shape == DebugPrimitiveShape.InputCaptureBinding || prim.Shape == DebugPrimitiveShape.ContextMenuBinding) continue;
                if (prim.InspNetworkId == 0 && prim.AnchorIndex == 0 && prim.SubElementId == 0) continue;

                float effectiveRadius = prim.SizeMode == SizeMode.ScreenPixels ? HitRadiusWorld / (zoom > 0f ? zoom : 1f) : HitRadiusWorld;
                bool hit = false;

                if (prim.Shape == DebugPrimitiveShape.Sphere)
                {
                    hit = Vector2.Distance(testPos, new Vector2(prim.SphereCenter.X, prim.SphereCenter.Y)) <= prim.SphereRadius + effectiveRadius;
                }
                else if (prim.Shape == DebugPrimitiveShape.Box2D)
                {
                    float dx = Math.Abs(testPos.X - prim.BoxCenterX);
                    float dy = Math.Abs(testPos.Y - prim.BoxCenterY);
                    hit = dx <= prim.BoxExtentX && dy <= prim.BoxExtentY;
                }

                if (hit && (best == null || prim.DebugLayer > best.Value.DebugLayer))
                    best = prim;
            }
            return best;
        }
    }
}
```

### 2. Update the Viewer Application
Since we changed the terminal's constructor and `HandleInput` method, we must fix the remote viewer loop in `GizmoMap.Presentation/GizmoViewerFrontend.cs`:
```csharp
var propertyAdapter = new ImGuiPropertyTreeAdapter(schemaRegistry);
var renderer = new DebugPrimitiveRenderer2D(imGuiAdapter: propertyAdapter);
var layer = new DebugGizmoLayer(renderer); // Buffer removed from ctor

rlImGui.Setup(true);

while (!Raylib.WindowShouldClose())
{
    float dt = Raylib.GetFrameTime();

    onUpdateTick(dt);
    // Pass buffer data and camera locally each frame
    layer.HandleInput(renderBuffer.GetFrame(), renderBuffer.InternMap, camera, onInteraction);
    onCustomInput?.Invoke();
    // ...
```

### 3. Gut the FDP Engine Layer
Now, we rewrite `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs` to act purely as an ECS/Engine adapter around the dumb terminal. We map the terminal's `onInteraction` enum directly into `FdpEventBus` structs (the exact reverse of what the DDS translator does).

```csharp
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Vis2D.Abstractions;
using Fdp.Toolkit.Vis2D.Components;
using GizmoMap.Network;
using GizmoMouseButton = Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapMouseButton;
using GizmoKeyboardKey = Fdp.Toolkit.Diagnostics.Gizmos.Interaction.MapKeyboardKey;
using GizmoPickToken = Fdp.Toolkit.Diagnostics.Gizmos.GizmoPickToken;

namespace Fdp.Toolkit.Vis2D.Layers
{
    public class DebugGizmoLayer : IMapLayer
    {
        public string Name => "Debug Gizmos";
        public int LayerBitIndex { get; private set; }

        private readonly DebugPrimitiveBuffer _buffer;
        private readonly FdpEventBus _eventBus;
        private readonly MapCamera _camera;

        private readonly GizmoMap.Presentation.DebugGizmoLayer _innerTerminal;
        private readonly GizmoMap.Presentation.DebugPrimitiveRenderer2D _innerRenderer;

        public DebugGizmoLayer(int layerBitIndex, DebugPrimitiveBuffer buffer, FdpEventBus eventBus, MapCamera camera)
        {
            LayerBitIndex = layerBitIndex;
            _buffer = buffer;
            _eventBus = eventBus;
            _camera = camera;

            _innerRenderer = new GizmoMap.Presentation.DebugPrimitiveRenderer2D();
            _innerTerminal = new GizmoMap.Presentation.DebugGizmoLayer(_innerRenderer);
        }

        public void Update(float dt)
        {
            // Evaluate raw HW inputs via the pure Raylib terminal.
            _innerTerminal.HandleInput(
                _buffer.GetFrame(),
                _buffer.InternMap,
                _camera.InnerCamera,
                OnInteractionDelegate);
        }

        public void Draw(RenderContext ctx)
        {
            if (LayerBitIndex >= 0 && LayerBitIndex < 32)
            {
                if ((ctx.VisibleLayersMask & (1u << LayerBitIndex)) == 0) return;
            }

            _innerRenderer.SetLayerMask((ushort)ctx.VisibleLayersMask);
            _innerTerminal.Render(_buffer.GetFrame(), _camera.InnerCamera, ctx.Zoom);
        }

        public void DrawContextMenu()
        {
            _innerTerminal.DrawContextMenu((token, actionId) =>
            {
                _eventBus.Publish(new GizmoMenuActionEvent
                {
                    AnchorId = token.AnchorId,
                    ActionId = actionId,
                });
            });
        }

        // IMapLayer FDP inputs: Muted. The Raylib terminal gathers its own inputs during Update().
        public bool HandleInput(Vector2 worldPos, MapMouseButton button, bool isPressed) => false;
        public void HandleHover(Vector2 mouseWorldPos) { }
        public bool HandleDrag(Vector2 worldPos, Vector2 delta) => false;
        public bool HandleKeyInput(MapKeyboardKey key) => false;
        public Entity? PickEntity(Vector2 worldPos) => null;

        private void OnInteractionDelegate(GizmoPickToken token, GizmoInteractionEventKind kind, Vector3 worldPos, int actionId, byte stateFlags)
        {
            var fdpToken = new PickToken
            {
                Target = new Entity((int)token.AnchorId, (ushort)token.StreamId),
                SubElementId = token.SubElementId
            };

            var space = (CoordinateSpace)stateFlags;

            switch (kind)
            {
                case GizmoInteractionEventKind.Started:
                    _eventBus.Publish(new GizmoInteractionStartedEvent { Token = fdpToken, WorldPos = worldPos });
                    break;
                case GizmoInteractionEventKind.DragUpdate:
                    _eventBus.Publish(new GizmoDragUpdateEvent { Token = fdpToken, WorldPos = worldPos, Space = space });
                    break;
                case GizmoInteractionEventKind.Commit:
                    _eventBus.Publish(new GizmoInteractionCommitEvent { Token = fdpToken, WorldPos = worldPos, Space = space });
                    break;
                case GizmoInteractionEventKind.Cancel:
                    _eventBus.Publish(new GizmoInteractionCancelEvent { Token = fdpToken });
                    break;
                case GizmoInteractionEventKind.RawInput:
                    bool isMouse = (stateFlags & 0x80) != 0;
                    bool isPressed = (stateFlags & 0x01) != 0;
                    if (isMouse)
                    {
                        _eventBus.Publish(new GizmoMouseEvent
                        {
                            Token = fdpToken,
                            Button = (GizmoMouseButton)actionId,
                            IsPressed = isPressed,
                            WorldPos = worldPos,
                        });
                    }
                    else
                    {
                        _eventBus.Publish(new GizmoKeyEvent
                        {
                            Token = fdpToken,
                            Key = (GizmoKeyboardKey)actionId,
                            IsPressed = isPressed,
                        });
                    }
                    break;
            }
        }
    }
}
```

### 4. Provide the Camera from Composition Roots
Because the new wrapper requires the `MapCamera` to feed Raylib screen-to-world conversion, update the layer registrations across the application shells:

*   **`IgApplication.cs`**:
    `var gizmoLayer = new DebugGizmoLayer(31, _gizmoBuffer!, _interactionBus, _canvas.Camera);`
*   **`EditorSubsystem.cs`**:
    `_canvas!.AddLayer(new DebugGizmoLayer(31, _gizmoBuffer!, interactionBus, _canvas.Camera));`
*   **`CgfSubsystem.cs`**:
    `var cgfGizmoLayer = new Fdp.Toolkit.Vis2D.Layers.DebugGizmoLayer(31, _cgfGizmoBuffer, _cgfInteractionBus!, _canvas.Camera);`
*   **`SimHostVisualization.cs`**:
    `_map.AddLayer(new DebugGizmoLayer(31, _gizmoBuffer, interactionBus ?? repo.Bus, _map.Camera));`

By doing this, you perfectly accomplish the DRY goal. Hit testing, menu extraction, and primitive projection logic are completely owned by `GizmoMap`, decoupling them from ECS domain types. The FDP engine just receives the final FDP events on the bus like any network consumer would.

