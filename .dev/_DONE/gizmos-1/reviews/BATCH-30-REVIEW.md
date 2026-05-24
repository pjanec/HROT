# BATCH-30 Review

**Status: APPROVED WITH CORRECTION (applied inline)**

---

## Summary

BATCH-30 implements Phase 23 (TASK-GZ068, TASK-GZ069, TASK-GZ070). Build is clean (0 errors).
Tests: 19/19 GizmoMap.Presentation.Tests, 72/72 Hrot.Presentation.Tests (regression). One
production timing bug was found in `ReceiveUiState` and corrected inline by the dev lead.

---

## TASK-GZ068: Fix ImGui Window Stable ID + Root Node Elimination

### Code quality
`MakeWindowTitle` internal static helper cleanly extracts the string interpolation so tests
can call it without a live ImGui context. Both `windowTitle` variants include
`_{item.GizmoTypeId}` in the stable-ID segment after `###StructInsp_{item.NetworkId}`.
The visible title (before `###`) is unchanged. `DrawEditNode(doc!.Root, item.IsReadOnly)` is
replaced by `foreach (var child in doc!.Root.Children) DrawEditNode(child, item.IsReadOnly)`.
These are the exact changes specified in DESIGN.md §11.7. **No issues.**

### Test quality
| ID | Test | Assessment |
|----|------|-----------|
| SC-GZ068-1 | Different `GizmoTypeId` → different stable IDs | Strong — the critical collision scenario from DESIGN §11.7 |
| SC-GZ068-2 | Same `GizmoTypeId`, different `SchemaHash` → same stable ID | Strong — regression check proves the discriminator is `GizmoTypeId` not `SchemaHash` |
| SC-GZ068-3 | `MakeWindowTitle` produces title containing `###` separator | Structural sanity check; adequate |

**Verdict: Accepted.**

---

## TASK-GZ069: Per-Inspector Viewing/Editing State Machine

### Code quality
`InspectorState` enum is `internal`; `_inspectorStates` is `internal` with
`InternalsVisibleTo("GizmoMap.Presentation.Tests")`. Both are accessible in tests. State
cleanup runs at the top of both `DrawScheduled` overloads; stale keys are removed correctly.
State transitions match the table in DESIGN.md §11.8:
- Viewing + focused → Editing; no callback.
- Editing + unfocused → Viewing; `onStructUpdate` invoked once.

The Apply-button guard correctly checks `applyState == Editing` before invoking the callback,
preventing double-invocation when focus-loss fires in the same frame. The
`internal DrawScheduled(onStructUpdate, Func<string,bool>? isFocusedOverride)` test-overload
correctly skips all ImGui calls and runs only state cleanup + transitions. **No issues.**

### Test quality
| ID | Test | Assessment |
|----|------|-----------|
| SC-GZ069-1 | Viewing + focused → Editing; callback count = 0 | Strong — state and no-callback both asserted |
| SC-GZ069-2 | Editing + unfocused → Viewing; callback count = 1 | Strong — state transition and exact-once semantics both asserted |
| SC-GZ069-3 | Editing→Viewing transition sends correct `(networkId, gizmoTypeId, json)` | Strong — verifies the triple is correct, not just "called" |
| SC-GZ069-4 | Stale key removed after item not scheduled | Strong — exercises the cleanup code path exactly |
| SC-GZ069-5 | Null callback + Editing state does not throw | Edge case; adequate |

SC-GZ069-3 uses the focus-loss path rather than the Apply button; the Apply button code is
present in production but the test rightly avoids the ImGui context requirement. **Accepted.**

---

## TASK-GZ070: Wire GizmoUiState Subscription

### Code quality — correction applied

**Bug found:** `ReceiveUiState` originally iterated `_items` to find matching items and check
their Editing state. However, `_items` is cleared at the end of `DrawScheduled`. In production
the call order is: `onUpdateTick` (`ReceiveUiState`) → render pass (`Schedule`) → ImGui pass
(`DrawScheduled`). So `ReceiveUiState` always sees `_items` as empty (cleared by the previous
frame's `DrawScheduled`), meaning the Editing block could never fire and user edits would be
overwritten by incoming DDS samples — exactly the bug DESIGN.md §11.9 was designed to prevent.

**Correction applied by dev lead** (`ReceiveUiState`, lines ~237-252): Changed the blocking
check from iterating `_items` to iterating `_inspectorStates.Values` directly:
```csharp
foreach (var kv in _inspectorStates)
{
    if (kv.Value == InspectorState.Editing)
        return;
}
```
This check is timing-independent — `_inspectorStates` persists across frames and always
reflects the current focus state. The behaviour is slightly more conservative (blocks ANY schema
update if ANY inspector is editing), which is safe given the low DDS sample rate of
`GizmoUiState`. SC-GZ070-2 still passes with the corrected implementation because it
pre-seeds `_inspectorStates` directly.

**`GizmoViewerFrontend.Run`** gained an optional `ImGuiPropertyTreeAdapter? externalAdapter`
parameter (default null, backward-compatible). `Program.cs` creates `adapter` before `Run`,
adds `DdsReader<GizmoUiState>`, and calls `adapter.ReceiveUiState(sample.Data)` per sample
inside `onUpdateTick`. Wiring is correct and complete.

### Test quality
| ID | Test | Assessment |
|----|------|-----------|
| SC-GZ070-1 | `ReceiveUiState` deserializes JSON into binding when all Viewing | Strong — verifies actual value propagation end-to-end |
| SC-GZ070-2 | Any Editing item blocks `Deserialize` | Strong — two-item setup with explicit state seeding; binding value confirms non-update |
| SC-GZ070-3 | Unknown `GizmoInstanceId` silently no-ops | Edge case; adequate |
| SC-GZ070-4 | Null registry silently no-ops | Edge case; adequate |
| SC-GZ070-5 | `ReceiveUiState` method exists on public API | Compilation/API-surface check; adequate |

**Verdict: Accepted (after dev lead correction).**

---

## Issues Summary

| ID | Severity | Description | Resolution |
|----|----------|-------------|-----------|
| GZ070-TIMING | Medium | `ReceiveUiState` checked `_items` (empty in production) instead of `_inspectorStates` (always valid); Editing block would never fire in production | Fixed inline by dev lead |

---

## Verdict

**APPROVED** — Phase 23 complete. Mark GZ068, GZ069, GZ070 as done.
