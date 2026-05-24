# BATCH-04 Report — Interaction Events and Proxy Tool

**Tasks:** GZ009, GZ010
**Status:** COMPLETE

---

## Files Created

1. `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Events/GizmoInteractionEvents.cs`
   — Four interaction event structs: `GizmoInteractionStartedEvent` (8051), `GizmoDragUpdateEvent` (8052),
     `GizmoInteractionCommitEvent` (8053), `GizmoInteractionCancelEvent` (8054).

2. `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmosInteractionEventTests.cs`
   — SC-GZ009-1 (unmanaged constraint via `RegisterEvent<T>()`), SC-GZ009-2 (publish/swap/read round-trip).

3. `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/GizmoInteractionProxyTool.cs`
   — `GizmoInteractionProxyTool` implementing `IMapTool`. Publishes drag/commit/cancel events and pops canvas on click/escape.

4. `FDP/Engine/Fdp.Presentation.Tests/Vis2D/Gizmos/GizmoInteractionProxyToolTests.cs`
   — SC-GZ010-1 through SC-GZ010-6 (drag publishes, left/right click, escape key, middle/other-key negatives).

---

## Build Results

| Project | Result |
|---|---|
| `Fdp.Toolkits` | Build succeeded — 0 errors, 0 warnings |
| `Fdp.Presentation` | Build succeeded — 0 errors, 1 pre-existing warning (CA2014 in PerspectiveShapeRenderer.cs, not related to this batch) |

---

## Test Results

| Test Run | Filter | Passed | Failed | Total |
|---|---|---|---|---|
| `Fdp.Toolkits.Tests` | `FullyQualifiedName~Gizmos` | 97 | 0 | 97 |
| `Fdp.Presentation.Tests` | `FullyQualifiedName~Gizmos` | 6 | 0 | 6 |

Prior Toolkits gizmo count was 95; 2 new tests added (SC-GZ009-1, SC-GZ009-2).
Presentation had 0 prior gizmo tests; 6 new tests added (SC-GZ010-1 through SC-GZ010-6).

---

## Design Deviations

**SC-GZ009-2 API:** The batch instructions referenced `view.ReadEvents<T>()` but this method does not exist on `FdpEventBus` or `EntityRepository`. The actual API is `bus.Read<T>()` (returning `ReadOnlySpan<T>`). Used the correct API.

**GizmosInteractionEventTests uses `Assert.Equal(1, events.Length)`:** Per codebase convention (xunit2013 analyzer warnings seen in adjacent test files), `Assert.Single()` would be preferred. However, `ReadOnlySpan<T>` does not have an overload-compatible `Assert.Single()` in xUnit, so `Assert.Equal(1, events.Length)` is the correct form here.

**SC-GZ010 canvas pop verification:** Tests push the proxy tool onto a fresh `MapCanvas`, then call the handler methods directly (not via the canvas's input pipeline). `_canvas?.PopTool()` inside the handler correctly removes the tool and `canvas.ActiveTool` returns null, confirming the behavior.

---

## Issues Encountered

None. The implementation matches the spec exactly.

---

## Weak Points Spotted

- `FdpEventBus` requires `Publish<T>()` to be called before `Read<T>()` returns non-empty results (stream is created lazily on first publish). This is correct behavior but could surprise callers who call `Read<T>()` before any publish. The `Register<T>()` method exists to pre-register streams if needed.
- `MapCanvas._isSwitching` guard prevents re-entrant tool switching during `PushTool`/`PopTool`, but the tests call handler methods directly (bypassing the canvas input pipeline), which avoids this guard entirely. This is fine for unit tests.

---

## Design Decisions Beyond the Spec

None required. The implementation follows the spec verbatim.
