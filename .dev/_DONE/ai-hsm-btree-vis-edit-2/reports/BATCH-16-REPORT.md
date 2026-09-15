# BATCH-16 REPORT — Break-link works for projected (JSON-loaded) links

**Date:** 2026-06-12
**Task:** TASK-BT-16 (Fix-A2 #3)
**Branch:** blueprint-integ-1

## Summary

Rewrote `BTreeCommandSink.ApplyRemoveLinks` to resolve links via the **graph model** (`_graph.FindLink` + `FindPin` → child/parent) instead of the session-only `_links` dict. This fixes "Break link does nothing for existing wires" — links loaded from JSON and projected by `BTreeGraphModel` were never in the `_links` dict, so `ApplyRemoveLinks` was a silent no-op for any pre-existing wire.

The fix queries `_graph.FindLink(linkId)` first (authoritative for all rendered links — both projected and session-added), resolves the child/parent VisualIds via `FindPin`, removes the child from the parent's `ChildVisualIds`, and falls back to the session-only `_links` dict only when the graph model doesn't know about the link (defensive).

## Changes

### 1. `Hrot/Subsystems/AI/Hrot.BTree.Editor/Host/BTreeCommandSink.cs`

**`ApplyRemoveLinks` (rewritten, lines 231–258):**

- **Graph-first resolution:** For each `LinkId`, calls `_graph.FindLink(id)` to get the `ILinkModel`. If found, resolves `childId` from `_graph.FindPin(link.FromPin)?.OwnerNodeId.Value` and `parentId` from `_graph.FindPin(link.ToPin)?.OwnerNodeId.Value`, then calls `_asset.FindNode(parentId)?.ChildVisualIds.Remove(childId)`.
- **Fallback:** If `_graph.FindLink` returns null, falls back to the existing `_links` dict lookup (same behavior as before the fix).
- **Cleanup:** Removes from `_links` unconditionally (harmless no-op if absent).
- **Dirty marker:** `_asset.MarkDirty()` called once after processing all link IDs (unchanged from before).

### 2. `Hrot/Subsystems/AI/Hrot.BTree.Editor.Tests/BTreeCommandSinkTests.cs`

**Stub infrastructure:**
- Added `StubLink : ILinkModel` — simple stub carrying `Id`, `FromPin`, `ToPin`.
- Added `_links` dictionary to `StubGraph` with `RegisterLink(LinkId, PinId, PinId)` helper.
- `StubGraph.FindLink` now returns registered links (was always `null`).
- `StubGraph.Links` now returns registered links (was always empty).
- Added `using System.Linq;` for `First()` in the projected-link test.

**Three new tests:**

| Test | What it proves |
|------|---------------|
| `RemoveLinks_ProjectedLink_DeletesIt` | A link projected by `BTreeGraphModel` (not session-added) is correctly deleted — the core fix |
| `RemoveLinks_SessionAddedLink_DeletesIt` | A session-added link is still removable via the graph path (regression — existing path still works) |
| `RemoveLinks_UnknownLink_NoThrow` | Removing a non-existent `LinkId` returns success, no exception, model unchanged |

## Diff

### `BTreeCommandSink.cs` — `ApplyRemoveLinks`

```diff
-        foreach (var id in linkIds)
-        {
-            if (_links.TryGetValue(id.Value, out var pair))
-            {
-                var parent = _asset.FindNode(pair.parent);
-                parent?.ChildVisualIds.Remove(pair.child);
-                _links.Remove(id.Value);
-            }
-        }
+        foreach (var id in linkIds)
+        {
+            // Resolve via the graph model first — works for both projected
+            // (JSON-loaded) and session-added links.
+            var link = _graph.FindLink(id);
+            if (link != null)
+            {
+                var fromPin = _graph.FindPin(link.FromPin);
+                var toPin   = _graph.FindPin(link.ToPin);
+                if (fromPin != null && toPin != null)
+                {
+                    var childId  = fromPin.OwnerNodeId.Value;
+                    var parentId = toPin.OwnerNodeId.Value;
+                    _asset.FindNode(parentId)?.ChildVisualIds.Remove(childId);
+                }
+            }
+            else if (_links.TryGetValue(id.Value, out var pair))
+            {
+                // Fallback: session-only lookup (defensive).
+                var parent = _asset.FindNode(pair.parent);
+                parent?.ChildVisualIds.Remove(pair.child);
+            }
+            _links.Remove(id.Value);
+        }
```

### `BTreeCommandSinkTests.cs` — Stub + tests

Stub changes: added `StubLink` class, updated `StubGraph` with `_links` dict, `RegisterLink()`, updated `FindLink()` and `Links` properties.
Three new `[Fact]` methods: `RemoveLinks_ProjectedLink_DeletesIt`, `RemoveLinks_SessionAddedLink_DeletesIt`, `RemoveLinks_UnknownLink_NoThrow`.

## Test results

| Project | Passed | Failed | Notes |
|---------|--------|--------|-------|
| Hrot.BTree.Editor.Tests | 501 | 0 | +3 new break-link tests; 21 pre-existing BTreeCommandSink tests unchanged |

## Build

- `dotnet build Hrot.BTree.Editor.csproj` — **0 errors, 0 warnings**.
- `dotnet build Hrot.BTree.Editor.Tests.csproj` — **0 errors, 0 warnings**.

## Design notes

- `ApplyAddLink` was NOT changed (BATCH-15). It continues to populate `_links` and uses the graph model for pin resolution. The link-id derivation (`BTreeParentChildLink` XOR-based) remains unchanged.
- `BTreeGraphModel.FindLink` + `FindPin` is the authoritative lookup — it works for both projected (JSON-loaded) and session-added links because `BuildCaches()` projects links from `parent.ChildVisualIds` and is rebuilt on every `_asset.Changed` event.
- The fallback to `_links` is defensive: if the graph model implementation doesn't know about a link ID (e.g., a stub in tests), the session dict still works. In production with `BTreeGraphModel`, the graph path always succeeds first.
- `_links.Remove(id.Value)` is called unconditionally to keep the dict consistent regardless of which path resolved the link.
