# BATCH-04 Instructions — Interaction Events and Proxy Tool

**Tasks:** GZ009, GZ010
**Phase:** Phase 4 — Interactive Input Routing
**Design references:** TASK-DETAIL.md §TASK-GZ009, §TASK-GZ010; DESIGN.md §4.2–4.3

---

## Context

BATCH-01 delivered the primitive layer. BATCH-02 delivered gizmo lifecycle contracts and systems.
BATCH-03 delivered the settings store.

This batch delivers the interaction event structs (GZ009, in Fdp.Toolkits) and the
`GizmoInteractionProxyTool` (GZ010, in Fdp.Presentation).

GZ009 tests go in `FDP/Toolkits/Fdp.Toolkits.Tests` (same project as all prior gizmo tests).
GZ010 tests go in `FDP/Engine/Fdp.Presentation.Tests` (the existing Presentation test project,
which uses xUnit + Moq).

---

## Codebase conventions

- Fdp.Toolkits namespace: `Fdp.Toolkit.Diagnostics.Gizmos.Events`
- Fdp.Presentation namespace for the proxy tool: `Fdp.Toolkit.Vis2D.Gizmos`
- Fdp.Presentation.Tests namespace: `Fdp.Toolkit.Vis2D.Tests.Gizmos`
- Test frameworks: xUnit + Moq (Presentation.Tests already references Moq — see existing tests)
- EventId 8050 is taken (GizmoSettingChangedEvent). EventIds 8051–8054 are for interaction events.
  Confirm no collision before assigning.

---

## Task GZ009 — Interaction Event Structs

**Full spec:** TASK-DETAIL.md §TASK-GZ009

All four structs go in a single file.

### File: `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Events/GizmoInteractionEvents.cs`

```csharp
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos.Primitives;

namespace Fdp.Toolkit.Diagnostics.Gizmos.Events
{
    [EventId(8051)]
    public struct GizmoInteractionStartedEvent
    {
        public PickToken Token;
        public Vector3 WorldPos;
    }

    [EventId(8052)]
    public struct GizmoDragUpdateEvent
    {
        public PickToken Token;
        public Vector3 WorldPos;
    }

    [EventId(8053)]
    public struct GizmoInteractionCommitEvent
    {
        public PickToken Token;
        public Vector3 WorldPos;
    }

    [EventId(8054)]
    public struct GizmoInteractionCancelEvent
    {
        public PickToken Token;
    }
}
```

**Constraints:**
- All four must be unmanaged (satisfy `where T : unmanaged`). `PickToken` already satisfies this
  (it contains `Entity` which is a blittable struct, and `uint SubElementId`).
- `Vector3` is `System.Numerics.Vector3`.
- Verify that EventIds 8051–8054 are not taken (search `EventId` in the codebase — they are free).

**Tests** in `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmosInteractionEventTests.cs`:

Minimum scenarios:
- **SC-GZ009-1**: Each of the four structs satisfies `where T : unmanaged` — tested by calling
  `repo.RegisterEvent<GizmoInteractionStartedEvent>()` etc. without error (the RegisterEvent
  method has an unmanaged constraint).
- **SC-GZ009-2**: Publish `GizmoDragUpdateEvent` to `EntityRepository.Bus`, swap buffers, read
  back via `view.ReadEvents<GizmoDragUpdateEvent>()`. Assert `Token` and `WorldPos` round-trip.

Use `GizmoTestRepo.Create()` from `GizmosSystemTests.cs` as the base (same test namespace), then
add the event registrations needed.

---

## Task GZ010 — GizmoInteractionProxyTool

**Full spec:** TASK-DETAIL.md §TASK-GZ010

### File: `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/GizmoInteractionProxyTool.cs`

```csharp
using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos.Events;
using Fdp.Toolkit.Diagnostics.Gizmos.Primitives;
using Fdp.Toolkit.Vis2D.Abstractions;
using Raylib_cs;

namespace Fdp.Toolkit.Vis2D.Gizmos
{
    public sealed class GizmoInteractionProxyTool : IMapTool
    {
        public string Name => "GizmoInteractionProxy";

        private readonly PickToken _token;
        private readonly FdpEventBus _eventBus;
        private MapCanvas? _canvas;

        public GizmoInteractionProxyTool(PickToken token, FdpEventBus eventBus)
        {
            _token    = token;
            _eventBus = eventBus;
        }

        public void OnEnter(MapCanvas canvas)  => _canvas = canvas;
        public void OnExit()                   => _canvas = null;
        public void Update(float dt)           { }
        public void Draw(RenderContext ctx)    { }

        public bool HandleHover(Vector2 worldPos) => true;

        public bool HandleDrag(Vector2 worldPos, Vector2 delta)
        {
            _eventBus.Publish(new GizmoDragUpdateEvent
            {
                Token    = _token,
                WorldPos = new Vector3(worldPos.X, worldPos.Y, 0f),
            });
            return true;
        }

        public bool HandleClick(Vector2 worldPos, MouseButton button)
        {
            // Left button released = commit
            if (button == MouseButton.Left)
            {
                _eventBus.Publish(new GizmoInteractionCommitEvent
                {
                    Token    = _token,
                    WorldPos = new Vector3(worldPos.X, worldPos.Y, 0f),
                });
                _canvas?.PopTool();
                return true;
            }

            // Right button = cancel
            if (button == MouseButton.Right)
            {
                _eventBus.Publish(new GizmoInteractionCancelEvent { Token = _token });
                _canvas?.PopTool();
                return true;
            }

            return false;
        }

        public bool HandleKeyPressed(KeyboardKey key)
        {
            if (key == KeyboardKey.Escape)
            {
                _eventBus.Publish(new GizmoInteractionCancelEvent { Token = _token });
                _canvas?.PopTool();
                return true;
            }
            return false;
        }
    }
}
```

**Key design note from spec:** Click-away detection is NOT done in the proxy tool itself
(that is `DebugGizmoLayer.HandleInput`'s responsibility, GZ013). The proxy tool returns
`false` on a left-click that is not a drag continuation; returning `false` on left-click is
only for the "click-away" case but the spec says left click = commit. Keep it as: left button
= commit. The proxy tool only receives input because the canvas dispatched to it — the canvas
is responsible for routing.

### Tests: `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Gizmos/GizmoInteractionProxyToolTests.cs`

Test namespace: `Fdp.Toolkit.Vis2D.Tests.Gizmos`

Use `MockInputProvider` (already exists at
`Fdp.Presentation.Tests/Vis2D/Input/MockInputProvider.cs`).

For event verification, use a real `FdpEventBus` (construct with `new FdpEventBus()`, swap buffers
after publish, then call `bus.Read<T>()` — or check via subscription). Look at how existing tests
use `FdpEventBus` if any; otherwise, call `bus.Publish` and verify via `bus.SwapBuffers()` +
`bus.Read<GizmoDragUpdateEvent>()`.

To check what methods `FdpEventBus` exposes for reading, look at
`FDP/Engine/Fdp.Core/FdpEventBus.cs` — it has `Read<T>()` or equivalent methods.

Minimum success conditions to test:
- **SC-GZ010-1**: `HandleDrag` publishes `GizmoDragUpdateEvent` with `WorldPos.X == 5f, Y == 10f`.
  Use a real `FdpEventBus`. After `HandleDrag`, swap buffers and read events.
- **SC-GZ010-2**: `HandleClick(worldPos, MouseButton.Right)` publishes `GizmoInteractionCancelEvent`
  and calls `_canvas.PopTool()` (verify canvas has popped by checking `ActiveTool` is null or the
  previous tool).
- **SC-GZ010-3**: `HandleKeyPressed(KeyboardKey.Escape)` publishes `GizmoInteractionCancelEvent`
  and pops the canvas.
- **SC-GZ010-4**: `HandleClick(worldPos, MouseButton.Left)` publishes `GizmoInteractionCommitEvent`
  and pops the canvas.
- **SC-GZ010-5** (negative): `HandleClick(worldPos, MouseButton.Middle)` returns `false`.
- **SC-GZ010-6** (negative): `HandleKeyPressed(KeyboardKey.A)` returns `false`.

For canvas testing, construct a real `MapCanvas` with a `MockInputProvider`. Push the proxy tool
onto it via `canvas.PushTool(proxyTool)`. Then call `proxyTool.HandleClick(...)`. Verify
`canvas.ActiveTool` is null (or the tool it was pushed on top of) after PopTool.

**Note:** `FdpEventBus` is the bus used in `FDP/Engine/Fdp.Core`. To read events after
`SwapBuffers`, look at the `Read<T>()` or `ReadCurrentFrame<T>()` API on `FdpEventBus`.
Do NOT construct `EntityRepository` just for event testing in the Presentation tests — use
`FdpEventBus` directly.

---

## Verification commands

After implementing, run from `d:\Work\IOS-IG-SimHost-FDP-2`:

```powershell
dotnet build FDP\Toolkits\Fdp.Toolkits\Fdp.Toolkits.csproj --nologo
dotnet build FDP\Engine\Fdp.Presentation\Fdp.Presentation.csproj --nologo
dotnet test FDP\Toolkits\Fdp.Toolkits.Tests\Fdp.Toolkits.Tests.csproj --nologo --filter "FullyQualifiedName~Gizmos"
dotnet test FDP\Engine\Fdp.Presentation.Tests\Fdp.Presentation.Tests.csproj --nologo --filter "FullyQualifiedName~Gizmos"
```

All prior 95 gizmo tests plus all new tests must pass.

## Deliverables

1. `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Events/GizmoInteractionEvents.cs`
2. `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmosInteractionEventTests.cs`
3. `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/GizmoInteractionProxyTool.cs`
4. `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Gizmos/GizmoInteractionProxyToolTests.cs`

## Report

Write `d:\Work\IOS-IG-SimHost-FDP-2\.dev\gizmos-1\reports\BATCH-04-REPORT.md` with:
- Files created
- Test results: total gizmo tests (Toolkits), total gizmo tests (Presentation), fail count
- Any design deviations and reasons
