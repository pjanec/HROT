# BATCH-HS-08 — Authoring-loop round-trip test (create → edit → save → reopen)

**Task:** TASK-HS-08 (headless portion). **One objective only.** Add an end-to-end headless test proving the HSM authoring loop holds on fresh content: build a machine via the command sink, save it through the real persistence path, reload, and assert topology + layout are preserved.

> **Scope note (decided by lead):** The "register HsmEventsWindow / HsmGlobalsStrip" part of TASK-HS-08 is **deferred** to VE-DEBT-005 (those windows are asset-bound, not doc-singletons, so registration needs a doc-retarget adapter — a design change on the shared composition root, not a mechanical edit). Do NOT attempt window registration or touch `EditorSubsystem.cs` in this batch. The container/transition/label/history **rendering** verification is the lead's REVIEW-HS visual gate — not your job. This batch is the round-trip test ONLY.

Design ref: TASK-DETAIL.md §TASK-HS-08.

## Working agreement (MANDATORY — restated)
1. **One task per batch.** Touch ONLY a new test file (+ a read accessor on the model ONLY if strictly required to assert state and none exists — prefer existing public API). Do NOT modify the command sink, the model's behavior, renderers, `EditorSubsystem.cs`, or persistence code.
2. **No cheating to pass.** If the round trip reveals a real persistence bug, STOP and write the blocker in the report — do NOT paper over it or weaken the assertion.
3. **Finish without asking** — build + run tests until `Failed: 0`, then report.
4. **Headless only.** 5. **Tests assert behavior** (topology refs/counts, layout values, flags), not strings. 6. **Litter-free.** 7. **Report = truth.**

## What to build (the test)
In `Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests/` add `HsmAuthoringRoundTripTests.cs`.

**First discover the real save path.** Find how the editor serializes a live `HsmAsset` back to JSON — the inverse of the recipe/open path. Look for an `HsmAsset → HsmAssetDto` projection + `HsmJsonServices.Serialize` (e.g. a `Save`/`Project`/`ToDto`/`Export` method, or how `HsmDocumentFactory`/the doc-save flow persists). Use the REAL path; do not hand-roll a parallel serializer. If no asset→DTO projection exists at all, STOP and report that gap (it would mean the editor can't save HSMs — a real finding).

**The test flow (create → edit → save → reopen):**
1. **Create:** start from an empty machine (root only) or the Starter recipe. Via `HsmCommandSink`, author a small but non-trivial machine:
   - AddNode a composite-ish parent + 2–3 child states (Simple), a Final state.
   - Reparent a child under another state (ChangeParent) so a composite forms.
   - AddLink to draw ≥1 transition between two states.
   - SetContainerCollapsed(true) on the composite.
   - MoveNodes to set distinct positions.
2. **Save:** project the live `HsmAsset` to its DTO and `HsmJsonServices.Serialize` to a JSON string (the real save path found above).
3. **Reopen:** `HsmJsonServices.Deserialize` + re-project to a fresh `HsmAsset` (the real open path, as `HsmNewAssetService`/`HsmDocumentFactory` does).
4. **Assert preserved:**
   - State count + each state's StableId, Name, kind flags (IsFinal etc.), and parent/child topology (a child is under the same parent).
   - Transition count + each transition's Source/Target StableIds + VisualId.
   - Layout: a moved state's `Position` and the composite's `IsCollapsed == true` survive the round trip.
   - No dangling references (every transition's Source/Target resolves in the reloaded asset).
5. Add a second, smaller test: **Starter recipe → save → reopen** preserves its single initial state.

Keep assertions on values/refs/flags. If a specific field (e.g. IsCollapsed or Position) does NOT survive the real save path, that's a genuine persistence bug — STOP and report it (do not delete the assertion).

## Verification (no regenerate env var)
```
dotnet build Hrot/Subsystems/AI/Hrot.Hsm.Editor/Hrot.Hsm.Editor.csproj
dotnet test  Hrot/Subsystems/AI/Hrot.Hsm.Editor.Tests
```
Must end `Failed: 0`, 0 build errors. Baseline before this batch: 454 passed. List pre-existing failures; confirm 0 new.

## Report → `.dev/ai-hsm-btree-vis-edit-2/reports/BATCH-HS-08-REPORT.md`
The real save/open path you used (file + method); the test flow + exactly what's asserted preserved; any field that did NOT survive (if any → blocker); before/after counts; confirmation you did NOT touch EditorSubsystem.cs / the window classes (VE-DEBT-005 deferral). Do not commit.
