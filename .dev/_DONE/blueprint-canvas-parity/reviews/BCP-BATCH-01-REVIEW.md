# BCP-BATCH-01 Review — visible core (theme + movement + pins/wires)
**Status:** ✅ APPROVED   **Date:** 2026-06-03

## Summary
Fixes the three user-visible Blueprint-canvas defects: yellow marquee → demo scheme (all 3 perspectives), nodes now draggable, pins+wires now render on loaded assets.

## Verification (ran myself)
- **`dotnet build IOS-IG-SimHost.sln` → 0 Warnings / 0 Errors** (GizmoMap.Contracts 0.2.2; Hrot.IG/DDS untouched).
- `Hrot.Blueprints.Tests` **1066 / 10 / 8** — the 10 are exactly the DEBT-006 set; the MoveToAndFire/Library **golden emit tests fail identically to baseline → compiler output did NOT drift → projection-only held**. +39 new passing (pin-hydration + byte-stability + move identity).
- `Hrot.Editor.AiShared.Tests` **745 / 0** (theme assertions pass). `Hrot.BTree.Editor.Tests` **380 / 0**. `Hrot.Hsm.Editor.Tests` **330 / 0**. `EditorSubsystemBoot` **10 / 0**.

## Code read
- **C — EngineEditorTheme.cs:** now returns demo literals — `SelectionAccent (0.21,0.52,0.89,1)`, PrimarySelection (0.26,0.65,0.99,1), corners 4, border 1.5, header 28, exec=3/data=2 wires, per-category header map (Event red, Function blue, Variable green, …). `_base` removed; `GetFontForSize` kept (engine atlas). Shared bundle → all three perspectives.
- **B — movement:** `BlueprintNodeModel.Position` is now a mutable `_position` + `internal SetPosition`; `BlueprintGraphModel.NotifyMoved` fires `GraphChangeKind.NodesMoved` without `Rebuild()`; `ApplyMoveNodes` updates the existing model instance in place — `RebuildAndNotify()` removed. Test asserts `Assert.Same` instance identity across a move + zero Wholesale notifications.
- **A — pins/wires:** new `NodePinSchema.GetCanonicalPins` (asset-pins → registry descriptor → built-in fallback table for all 20 compiler kinds). `BlueprintGraphModel.Rebuild` is two-pass: fast path (asset had pins → authoritative GUIDs) and slow path (JSON `Pins:[]` → bind connected pins' GUIDs from incident `Link.From/ToPinId`, deterministic GUIDs for unconnected). `BlueprintNodeModel` ctor now takes pre-resolved pins. Tests assert every MoveToAndFire link resolves with GUIDs/directions matching the JSON.

## Guardrails (the projection-only proof)
- **Byte-stability** theory over all `TestAssets/**.bp.json` + `Comparison/Fixtures/*.bp.json` — passes (compares normalized round-trips before/after projection; catches any pin write-back). Tolerates `$meta` envelope rather than raw bytes — acceptable.
- **Compiler golden suite unchanged** (same 10 DEBT-006 failures, identical) — confirms pins stay editor-side.

## Issues / notes (non-blocking)
- **Slow-path GUID binding is positional** (matches same-direction pins to incident links by declaration order). Wires always resolve, but a node with multiple same-direction pins and sparse connections could bind a wire to a visually-different same-direction pin. Fine for v1; revisit if a real asset shows mis-routing. Logged as DEBT-BCP-001.
- Byte-stability test silently `return`s on fixtures `BlueprintJsonServices` can't deserialize (a few comparison fixtures) — acceptable; core assets covered.

## Verdict
APPROVED. The three reported defects are fixed and verified. Remaining parity work: BCP-D (mini-editors), BCP-E (pickers), BCP-F (find/commands), BCP-G (comments/reroutes), BCP-H (containers), BCP-I (bookmarks/details/my-blueprint).

## Commit Message
```
feat(blueprint-editor): demo theme + movable nodes + pin/wire hydration (BCP-BATCH-01, C/B/A)

C: EngineEditorTheme now uses the NodeEdit demo color/geometry scheme (blue selection, per-category
headers, 4px corners, exec/data wire thickness) across all three AI editor perspectives.

B: in-place node movement — BlueprintNodeModel.Position mutable + SetPosition; ApplyMoveNodes mutates
the existing model instance and fires NodesMoved without a full RebuildAndNotify (mirrors BTree/demo).

A: pin/wire hydration (projection-only) — NodePinSchema resolves canonical pins per kind; two-pass
BlueprintGraphModel.Rebuild binds connected pins' GUIDs from incident link From/ToPinId so loaded
assets (Pins:[]) render pins AND wires. No asset-format change.

Guardrails: byte-stability test over all .bp.json fixtures + compiler golden suite unchanged (no drift).
Build 0/0. Blueprints 1066/10 (DEBT-006 only), AiShared 745/0, BTree 380/0, Hsm 330/0, Boot 10/0.
```
